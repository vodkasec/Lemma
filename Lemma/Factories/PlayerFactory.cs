﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Lemma.Components;
using Microsoft.Xna.Framework.Input;
using BEPUphysics;
using Lemma.Util;
using BEPUphysics.BroadPhaseEntries.MobileCollidables;
using BEPUphysics.NarrowPhaseSystems.Pairs;

namespace Lemma.Factories
{
	public class PlayerFactory : Factory
	{
		public PlayerFactory()
		{
			this.Color = new Vector3(0.4f, 0.4f, 0.4f);
		}

		struct BlockBuildOrder
		{
			public Map Map;
			public Map.Coordinate Coordinate;
			public Map.CellState State;
		}

		public class ScheduledBlock
		{
			public Entity.Handle Map;
			public Map.Coordinate Coordinate;
			public float Time;
		}

		public struct RespawnLocation
		{
			public Entity.Handle Map;
			public Map.Coordinate Coordinate;
			public float Rotation;
			public Vector3 OriginalPosition;
		}

		public override Entity Create(Main main)
		{
			Entity result = new Entity(main, "Player");
			result.ID = "player";
			Player player = new Player();
			Transform transform = new Transform();
			AnimatedModel model = new AnimatedModel();
			AnimatedModel firstPersonModel = new AnimatedModel();
			AudioListener audioListener = new AudioListener();
			Sound footsteps = new Sound();
			Sound wind = new Sound();
			wind.Is3D.Value = false;
			wind.Cue.Value = "Speed";
			Timer footstepTimer = new Timer();

			result.Add("Player", player);
			result.Add("Transform", transform);
			result.Add("FirstPersonModel", firstPersonModel);
			result.Add("Model", model);
			result.Add("AudioListener", audioListener);
			result.Add("Footsteps", footsteps);
			result.Add("SpeedSound", wind);
			result.Add("FootstepTimer", footstepTimer);

			model.Editable = false;
			model.Filename.Value = "Models\\player";
			model.CullBoundingBox.Value = false;

			firstPersonModel.Editable = false;
			firstPersonModel.Filename.Value = "Models\\player-firstperson";
			firstPersonModel.CullBoundingBox.Value = false;

			footstepTimer.Repeat.Value = true;
			footstepTimer.Interval.Value = 0.35f;

			result.Add("Rotation", new Property<float> { Editable = false });
			result.Add("Data", new Property<Entity.Handle> { Editable = false });

			/*TextElement debug = new TextElement();
			debug.FontFile.Value = "Font";
			debug.Name.Value = "Debug";
			debug.AnchorPoint.Value = new Vector2(1, 1);
			debug.Add(new Binding<Vector2, Point>(debug.Position, x => new Vector2(x.X, x.Y), main.ScreenSize));
			ui.Root.Children.Add(debug);*/

			return result;
		}

		private class BlockPossibility
		{
			public Map Map;
			public Map.Coordinate StartCoord;
			public Map.Coordinate EndCoord;
			public ModelAlpha Model;
		}

		private class Prediction
		{
			public Vector3 Position;
			public float Time;
			public int Level;
		}

		private static Entity instance;
		public static Entity Instance
		{
			get
			{
				if (PlayerFactory.instance != null && !PlayerFactory.instance.Active)
					PlayerFactory.instance = null;
				return PlayerFactory.instance;
			}

			set
			{
				PlayerFactory.instance = value;
			}
		}

		public override void Bind(Entity result, Main main, bool creating = false)
		{
			result.CannotSuspend = true;

			PlayerFactory.Instance = result;

			this.SetMain(result, main);

			Player player = result.Get<Player>();
			Transform transform = result.Get<Transform>();

			AnimatedModel model = result.Get<AnimatedModel>("Model");
			AnimatedModel firstPersonModel = result.Get<AnimatedModel>("FirstPersonModel");

			firstPersonModel.Bind(model);

			model.UnsupportedTechniques.Add(Technique.Clip);
			model.UnsupportedTechniques.Add(Technique.MotionBlur);
			model.UnsupportedTechniques.Add(Technique.Render);
			firstPersonModel.UnsupportedTechniques.Add(Technique.Shadow);
			firstPersonModel.UnsupportedTechniques.Add(Technique.PointLightShadow);

			FPSInput input = new FPSInput();
			input.EnabledWhenPaused.Value = false;
			result.Add("Input", input);

			AudioListener audioListener = result.Get<AudioListener>();
			Sound footsteps = result.Get<Sound>("Footsteps");
			Timer footstepTimer = result.Get<Timer>("FootstepTimer");

			// Build UI

			UIRenderer ui = new UIRenderer();
			ui.DrawOrder.Value = -1;
			ui.EnabledWhenPaused.Value = false;
			result.Add("UI", ui);
			Sprite damageOverlay = new Sprite();
			damageOverlay.Image.Value = "Images\\damage";
			damageOverlay.AnchorPoint.Value = new Vector2(0.5f);
			ui.Root.Children.Add(damageOverlay);

			GameMain.Config settings = ((GameMain)main).Settings;
			input.Add(new Binding<float>(input.MouseSensitivity, settings.MouseSensitivity));
			input.Add(new Binding<bool>(input.InvertMouseX, settings.InvertMouseX));
			input.Add(new Binding<bool>(input.InvertMouseY, settings.InvertMouseY));
			input.Add(new Binding<PCInput.PCInputBinding>(input.LeftKey, settings.Left));
			input.Add(new Binding<PCInput.PCInputBinding>(input.RightKey, settings.Right));
			input.Add(new Binding<PCInput.PCInputBinding>(input.BackwardKey, settings.Backward));
			input.Add(new Binding<PCInput.PCInputBinding>(input.ForwardKey, settings.Forward));

			model.StartClip("Idle", 0, true);

			Updater update = new Updater();
			update.EnabledInEditMode.Value = false;
			result.Add(update);

			// Set up AI agent
			Agent agent = result.GetOrCreate<Agent>();
			agent.Add(new TwoWayBinding<float>(player.Health, agent.Health));
			agent.Add(new Binding<Vector3>(agent.Position, transform.Position));
			agent.Add(new CommandBinding(agent.Die, result.Delete));

#if DEVELOPMENT
			Property<bool> thirdPerson = new Property<bool> { Value = false };
			input.Add(new CommandBinding(input.GetKeyDown(Keys.C), delegate() { thirdPerson.Value = !thirdPerson; }));

			firstPersonModel.Add(new Binding<bool>(firstPersonModel.Enabled, x => !x, thirdPerson));

			model.Add(new NotifyBinding(delegate()
			{
				if (thirdPerson)
				{
					model.UnsupportedTechniques.Remove(Technique.Clip);
					model.UnsupportedTechniques.Remove(Technique.MotionBlur);
					model.UnsupportedTechniques.Remove(Technique.Render);
				}
				else
				{
					model.UnsupportedTechniques.Add(Technique.Clip);
					model.UnsupportedTechniques.Add(Technique.MotionBlur);
					model.UnsupportedTechniques.Add(Technique.Render);
				}
			}, thirdPerson));

			ModelAlpha debugCylinder = new ModelAlpha();
			debugCylinder.Filename.Value = "Models\\alpha-cylinder";
			debugCylinder.Add(new Binding<Matrix>(debugCylinder.Transform, transform.Matrix));
			debugCylinder.Serialize = false;
			debugCylinder.Editable = false;
			debugCylinder.Alpha.Value = 0.25f;
			debugCylinder.Add(new Binding<bool>(debugCylinder.Enabled, thirdPerson));
			debugCylinder.Add(new Binding<Vector3>(debugCylinder.Scale, delegate()
			{
				return new Vector3(Player.CharacterRadius, player.Height, Player.CharacterRadius);
			}, player.Height));
			result.Add(debugCylinder);
#endif

			Property<Entity.Handle> data = result.GetProperty<Entity.Handle>("Data");

			ListProperty<RespawnLocation> respawnLocations = null;

			result.Add(new PostInitialization
			{
				delegate()
				{
					if (data.Value.Target == null)
						data.Value = Factory.Get<PlayerDataFactory>().Instance(main);

					respawnLocations = data.Value.Target.GetOrMakeListProperty<RespawnLocation>("RespawnLocations");
					
					// Bind player data properties
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableRoll"), player.EnableRoll));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableKick"), player.EnableKick));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableWallRun"), player.EnableWallRun));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableWallRunHorizontal"), player.EnableWallRunHorizontal));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableEnhancedWallRun"), player.EnableEnhancedWallRun));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableSlowMotion"), player.EnableSlowMotion));
					result.Add(new TwoWayBinding<bool>(data.Value.Target.GetProperty<bool>("EnableMoves"), player.EnableMoves));
				}
			});

			result.Add(new CommandBinding(player.HealthDepleted, delegate()
			{
				Session.Recorder.Event(main, "DieFromHealth");
				Sound.PlayCue(main, "Death");
				((GameMain)main).RespawnRewindLength = 20;
			}));

			result.Add(new CommandBinding(player.HealthDepleted, result.Delete));

			result.Add(new CommandBinding(result.Delete, delegate()
			{
				Session.Recorder.Event(main, "Die");
				if (Agent.Query(transform.Position, 0.0f, 10.0f, x => x != agent) != null)
					((GameMain)main).RespawnRewindLength = 20;
			}));

			UIComponent targets = new UIComponent();
			ui.Root.Children.Add(targets);
			const string targetOnScreen = "Images\\target";
			const string targetOffScreen = "Images\\target-pointer";
			ui.Add(new ListBinding<UIComponent, Transform>(targets.Children, TargetFactory.Positions, delegate(Transform target)
			{
				Sprite sprite = new Sprite();
				sprite.Image.Value = "Images\\target";
				sprite.Opacity.Value = 0.5f;
				sprite.AnchorPoint.Value = new Vector2(0.5f, 0.5f);
				sprite.Add(new Binding<bool>(sprite.Visible, target.Enabled));
				sprite.Add(new Binding<Vector2>(sprite.Position, delegate()
				{
					Vector3 pos = target.Position.Value;
					Vector4 projectionSpace = Vector4.Transform(new Vector4(pos.X, pos.Y, pos.Z, 1.0f), main.Camera.ViewProjection);
					float originalDepth = projectionSpace.Z;
					projectionSpace /= projectionSpace.W;

					Point screenSize = main.ScreenSize;
					Vector2 screenCenter = new Vector2(screenSize.X * 0.5f, screenSize.Y * 0.5f);

					Vector2 offset = new Vector2(projectionSpace.X * (float)screenSize.X * 0.5f, -projectionSpace.Y * (float)screenSize.Y * 0.5f);

					float radius = Math.Min(screenSize.X, screenSize.Y) * 0.95f * 0.5f;

					float offsetLength = offset.Length();

					Vector2 normalizedOffset = offset / offsetLength;

					bool offscreen = offsetLength > radius;

					bool behind = originalDepth < main.Camera.NearPlaneDistance;

					string img = offscreen || behind ? targetOffScreen : targetOnScreen;

					if (sprite.Image.Value != img)
						sprite.Image.Value = img;

					if (behind)
						normalizedOffset *= -1.0f;

					if (offscreen || behind)
						sprite.Rotation.Value = -(float)Math.Atan2(normalizedOffset.Y, -normalizedOffset.X) - (float)Math.PI * 0.5f;
					else
						sprite.Rotation.Value = 0.0f;

					if (behind || offscreen)
						offset = normalizedOffset * radius;

					return screenCenter + offset;
				}, target.Position, main.Camera.ViewProjection, main.ScreenSize));
				return new[] { sprite };
			}));

			player.EnabledInEditMode.Value = false;
			ui.EnabledInEditMode.Value = false;

			input.MaxY.Value = (float)Math.PI * 0.35f;

			/*TextElement debug = (TextElement)ui.Root.GetChildByName("Debug");
			debug.Add(new Binding<string, float>(debug.Text, x => x.ToString("F"), speedSound.GetProperty("Volume")));*/

			Sound speedSound = result.Get<Sound>("SpeedSound");
			player.Add(new Binding<float, Vector3>(speedSound.GetProperty("Volume"), delegate(Vector3 velocity)
			{
				float speed = velocity.Length();
				float maxSpeed = player.MaxSpeed * 1.25f;
				if (speed > maxSpeed)
					return (speed - maxSpeed) / (maxSpeed * 2.0f);
				else
					return 0.0f;
			}, player.LinearVelocity));
			speedSound.Play.Execute();
			speedSound.GetProperty("Volume").Value = 0.0f;

			// Determine if the player is swimming
			update.Add(delegate(float dt)
			{
				bool swimming = false;
				foreach (Water w in Water.ActiveInstances)
				{
					if (transform.Position.Value.Y < w.Position.Value.Y + 1.0f)
					{
						swimming = true;
						break;
					}
				}
				player.IsSwimming.Value = swimming;
			});

			Action stopKick = null;

			// Center the damage overlay and scale it to fit the screen
			damageOverlay.Add(new Binding<Vector2, Point>(damageOverlay.Position, x => new Vector2(x.X * 0.5f, x.Y * 0.5f), main.ScreenSize));
			damageOverlay.Add(new Binding<Vector2>(damageOverlay.Scale, () => new Vector2(main.ScreenSize.Value.X / damageOverlay.Size.Value.X, main.ScreenSize.Value.Y / damageOverlay.Size.Value.Y), main.ScreenSize, damageOverlay.Size));
			damageOverlay.Add(new Binding<float, float>(damageOverlay.Opacity, x => 1.0f - x, player.Health));

			// Player rotation code
			Property<float> rotation = result.GetProperty<float>("Rotation");
			Property<bool> rotationLocked = new Property<bool>();
			const float rotationLockBlendTime = 0.3f;
			float lockedRotationValue = 0.0f;
			float rotationLockBlending = rotationLockBlendTime;

			input.Mouse.Value = new Vector2(rotation, 0.0f);

			rotation.Set = delegate(float value)
			{
				if (rotationLocked)
					input.Mouse.Value += new Vector2(value - rotation.InternalValue, 0);
				rotation.InternalValue = value;
			};

			rotationLocked.Set = delegate(bool value)
			{
				if (rotationLocked.Value && !value)
				{
					lockedRotationValue = rotation.Value.ClosestAngle(input.Mouse.Value.X);
					rotationLockBlending = 0.0f;
				}
				rotationLocked.InternalValue = value;
			};
			
			// When rotation is locked, we want to make sure the player can't turn their head
			// 180 degrees from the direction they're facing

			input.Add(new Binding<float>(input.MaxY, () => rotationLocked ? (float)Math.PI * 0.3f : (float)Math.PI * 0.4f, rotationLocked));
			input.Add(new Binding<float>(input.MinX, () => rotationLocked ? rotation + ((float)Math.PI * -0.4f) : 0.0f, rotation, rotationLocked));
			input.Add(new Binding<float>(input.MaxX, () => rotationLocked ? rotation + ((float)Math.PI * 0.4f) : 0.0f, rotation, rotationLocked));
			input.Add(new NotifyBinding(delegate() { input.Mouse.Changed(); }, rotationLocked)); // Make sure the rotation locking takes effect even if the player doesn't move the mouse

			update.Add(delegate(float dt)
			{
				if (!rotationLocked)
				{
					if (rotationLockBlending < rotationLockBlendTime)
					{
						lockedRotationValue = lockedRotationValue.ClosestAngle(input.Mouse.Value.X);
						rotation.Value = lockedRotationValue + (input.Mouse.Value.X - lockedRotationValue) * (rotationLockBlending / rotationLockBlendTime);
						rotationLockBlending += dt;
					}
					else
						rotation.Value = input.Mouse.Value.X;
				}
			});

			Map wallRunMap = null, lastWallRunMap = null;
			Direction wallDirection = Direction.None, lastWallDirection = Direction.None;
			Direction wallRunDirection = Direction.None;

			player.Add(new TwoWayBinding<Matrix>(transform.Matrix, player.Transform));
			model.Add(new Binding<Matrix>(model.Transform, () => Matrix.CreateTranslation(0, (player.Height * -0.5f) - player.SupportHeight, 0) * Matrix.CreateRotationY(rotation) * transform.Matrix, transform.Matrix, rotation, player.Height, player.SupportHeight));
			firstPersonModel.Add(new Binding<Matrix>(firstPersonModel.Transform, model.Transform));
			firstPersonModel.Add(new Binding<Vector3>(firstPersonModel.Scale, model.Scale));
			audioListener.Add(new Binding<Vector3>(audioListener.Position, main.Camera.Position));
			audioListener.Add(new Binding<Vector3>(audioListener.Forward, main.Camera.Forward));
			audioListener.Add(new Binding<Vector3>(audioListener.Velocity, player.LinearVelocity));

			Map.GlobalRaycastResult groundRaycast = new Map.GlobalRaycastResult();

			Command<Map, Map.Coordinate?> walkedOn = new Command<Map,Map.Coordinate?>();
			result.Add("WalkedOn", walkedOn);

			int infectedID = WorldFactory.StatesByName["Infected"].ID,
				infectedCriticalID = WorldFactory.StatesByName["InfectedCritical"].ID,
				temporaryID = WorldFactory.StatesByName["Temporary"].ID;
			
			result.Add(new CommandBinding<Map, Map.Coordinate?>(walkedOn, delegate(Map map, Map.Coordinate? coord)
			{
				if (coord.HasValue)
				{
					int id = map[coord.Value].ID;
					if (id == infectedID || id == infectedCriticalID)
					{
						map.Empty(coord.Value);
						map.Fill(coord.Value, WorldFactory.States[temporaryID]);
						map.Regenerate();
					}
				}
			}));

			int walkedOnCount = 0;

			update.Add(delegate(float dt)
			{
				if (player.IsSupported)
				{
					Map oldMap = groundRaycast.Map;
					Map.Coordinate? oldCoord = groundRaycast.Coordinate;
					groundRaycast = Map.GlobalRaycast(transform.Position, Vector3.Down, player.Height.Value * 0.5f + player.SupportHeight + 1.1f);
					if (groundRaycast.Map != oldMap || (oldCoord != null && groundRaycast.Coordinate != null && !oldCoord.Value.Equivalent(groundRaycast.Coordinate.Value)))
					{
						walkedOn.Execute(groundRaycast.Map, groundRaycast.Coordinate);

						if (groundRaycast.Map != null)
						{
							walkedOnCount++;
							if (walkedOnCount >= 3)
							{
								// Every 3 tiles, save off the location for the auto-respawn system
								respawnLocations.Add(new RespawnLocation
								{
									Coordinate = groundRaycast.Coordinate.Value,
									Map = groundRaycast.Map.Entity,
									Rotation = rotation,
									OriginalPosition = groundRaycast.Map.GetAbsolutePosition(groundRaycast.Coordinate.Value),
								});
								while (respawnLocations.Count > 30)
									respawnLocations.RemoveAt(0);
								walkedOnCount = 0;
							}
						}
					}
				}
				else
				{
					if (groundRaycast.Map != null)
						walkedOn.Execute(null, null);
					groundRaycast.Map = null;
					groundRaycast.Coordinate = null;
				}
			});

			footstepTimer.Add(new CommandBinding(footstepTimer.Command, delegate()
			{
				Player.WallRun wallRunState = player.WallRunState;
				if (wallRunState == Player.WallRun.None)
				{
					if (groundRaycast.Map != null)
					{
						footsteps.Cue.Value = groundRaycast.Map[groundRaycast.Coordinate.Value].FootstepCue;
						footsteps.Play.Execute();
					}
				}
				else if (wallRunState != Player.WallRun.Down && wallRunState != Player.WallRun.Reverse)
					footsteps.Play.Execute();
			}));
			footstepTimer.Add(new Binding<bool>(footstepTimer.Enabled, () => player.WallRunState.Value != Player.WallRun.None || (player.MovementDirection.Value.LengthSquared() > 0.0f && player.IsSupported && player.EnableWalking), player.MovementDirection, player.IsSupported, player.EnableWalking, player.WallRunState));
			footsteps.Add(new Binding<Vector3>(footsteps.Position, x => x - new Vector3(0, player.Height * 0.5f, 0), transform.Position));
			footsteps.Add(new Binding<Vector3>(footsteps.Velocity, player.LinearVelocity));

			main.IsMouseVisible.Value = false;

			model.Update(0.0f);
			Property<Matrix> cameraBone = model.GetBoneTransform("Camera");
			Property<Matrix> relativeHeadBone = model.GetRelativeBoneTransform("Head");
			Property<Matrix> relativeSpineBone = model.GetRelativeBoneTransform("Spine3");
			Property<Matrix> clavicleLeft = model.GetBoneTransform("Clavicle_L");
			Property<Matrix> clavicleRight = model.GetBoneTransform("Clavicle_R");
			Property<Matrix> relativeUpperLeftArm = model.GetRelativeBoneTransform("UpArm_L");
			Property<Matrix> relativeUpperRightArm = model.GetRelativeBoneTransform("UpArm_R");
			Property<Matrix> headBone = model.GetBoneTransform("Head");

			// Camera updater / fire code
			Property<float> cameraShakeAmount = new Property<float> { Value = 0.0f };
			float cameraShakeTime = 0.0f;
			const float totalCameraShakeTime = 0.5f;
			Animation cameraShakeAnimation = null;

			result.Add("ShakeCamera", new Command<Vector3, float>
			{
				Action = delegate(Vector3 pos, float size)
				{
					if (cameraShakeAnimation != null && cameraShakeAnimation.Active)
						cameraShakeAnimation.Delete.Execute();
					cameraShakeAnimation = new Animation(new Animation.FloatMoveToSpeed(cameraShakeAmount, Math.Max(0.0f, 1.0f - ((pos - main.Camera.Position).Length() / size)), 20.0f));
					cameraShakeTime = totalCameraShakeTime;
					result.Add(cameraShakeAnimation);
				}
			});

			Property<bool> enableCameraControl = result.GetOrMakeProperty<bool>("EnableCameraControl", false, true);
			enableCameraControl.Serialize = false;

			Vector3 originalCameraPosition = cameraBone.Value.Translation;
			Vector3 cameraOffset = cameraBone.Value.Translation - headBone.Value.Translation;

			update.Add(delegate(float dt)
			{
				relativeHeadBone.Value *= Matrix.CreateRotationX(input.Mouse.Value.Y * 0.5f);
				relativeSpineBone.Value *= Matrix.CreateRotationX(input.Mouse.Value.Y * 0.25f);
				model.UpdateWorldTransforms();

				float targetAngle = input.Mouse.Value.Y * 0.75f;

				float angle = targetAngle;
				if (angle < 0.0f)
					angle *= 0.9f;
				else
					angle = Math.Min(0.5f, angle * 1.1f);

				Matrix r = Matrix.CreateRotationX(angle);

				Matrix parent = clavicleLeft;
				parent.Translation = Vector3.Zero;
				relativeUpperLeftArm.Value *= parent * r * Matrix.Invert(parent);

				parent = clavicleRight;
				parent.Translation = Vector3.Zero;
				relativeUpperRightArm.Value *= parent * r * Matrix.Invert(parent);

				model.UpdateWorldTransforms();

				float shakeAngle = 0.0f;
				if (cameraShakeTime > 0.0f)
				{
					shakeAngle = (float)Math.Sin(main.TotalTime * ((float)Math.PI / 0.05f)) * 0.4f * cameraShakeAmount * (cameraShakeTime / totalCameraShakeTime);
					cameraShakeTime -= dt;
				}
				else
					cameraShakeAmount.Value = 0.0f;

				if (enableCameraControl)
				{
#if DEVELOPMENT
					if (thirdPerson)
					{
						Vector3 cameraPosition = Vector3.Transform(new Vector3(0.0f, 3.0f, 0.0f), model.Transform);

						main.Camera.Angles.Value = new Vector3(-input.Mouse.Value.Y, input.Mouse.Value.X + (float)Math.PI * 1.0f, shakeAngle);

						Map.GlobalRaycastResult hit = Map.GlobalRaycast(cameraPosition, -main.Camera.Forward.Value, 5.0f);

						float cameraDistance = 4.0f;
						if (hit.Map != null)
							cameraDistance = (hit.Position - cameraPosition).Length() - 1.0f;
						main.Camera.Position.Value = cameraPosition + (main.Camera.Right.Value * cameraDistance * -0.25f) + (main.Camera.Forward.Value * -cameraDistance);
					}
					else
#endif
					{
						Vector3 adjustedCameraOffset = cameraOffset + (cameraBone.Value.Translation - originalCameraPosition);
						Vector3 cameraPosition = Vector3.Transform(adjustedCameraOffset, headBone.Value * model.Transform);

						if (model.IsPlaying("CrouchWalkBackwards", "CrouchWalk", "CrouchStrafeRight", "CrouchStrafeLeft")
							&& !model.IsPlaying("Kick"))
							cameraPosition.Y = Math.Min(cameraPosition.Y, transform.Position.Value.Y + player.Height.Value * 0.5f);

						main.Camera.Position.Value = cameraPosition;

						Matrix camera = cameraBone.Value * Matrix.CreateRotationY(input.Mouse.Value.X);

						Matrix rot = Matrix.Identity;
						rot.Forward = Vector3.Normalize(Vector3.TransformNormal(new Vector3(0, 1.0f, 0), camera));
						rot.Up = Vector3.Normalize(Vector3.TransformNormal(new Vector3(0.0f, 0, 1.0f), camera));
						rot.Right = Vector3.Normalize(Vector3.Cross(rot.Forward, rot.Up));

						Vector3 right = Vector3.Cross(rot.Forward, Vector3.Up);

						main.Camera.RotationMatrix.Value = rot * Matrix.CreateFromAxisAngle(rot.Forward, shakeAngle) * Matrix.CreateFromAxisAngle(right, -input.Mouse.Value.Y);
					}
				}
			});

			// Movement binding
			player.Add(new Binding<Vector2>(player.MovementDirection, delegate()
			{
				Vector2 movement = input.Movement;
				if (movement.LengthSquared() == 0.0f)
					return Vector2.Zero;

				Matrix matrix = Matrix.CreateRotationY(rotation);

				Vector2 forwardDir = new Vector2(matrix.Forward.X, matrix.Forward.Z);
				Vector2 rightDir = new Vector2(matrix.Right.X, matrix.Right.Z);
				return -Vector2.Normalize((forwardDir * movement.Y) + (rightDir * movement.X));
			}, input.Movement, rotation));

			player.Crouched.Value = true;
			player.AllowUncrouch.Value = true;
			
			// Update animation
			bool lastSupported = false;
			bool canKick = false;
			float lastLandAnimationPlayed = 0.0f;
			result.Add("AnimationUpdater", new Updater
			{
				delegate(float dt)
				{
					// Update footstep sound interval when wall-running
					if (player.WallRunState != Player.WallRun.None)
						footstepTimer.Interval.Value = 0.25f / model[player.WallRunState == Player.WallRun.Straight ? "WallWalkStraight" : (player.WallRunState == Player.WallRun.Left ? "WallWalkLeft" : "WallWalkRight")].Speed;

					if (player.WallRunState.Value != Player.WallRun.None)
						return;

					model.Stop("WallWalkLeft", "WallWalkRight", "WallWalkStraight", "WallSlideDown", "WallSlideReverse");
					if (player.IsSupported && !lastSupported)
					{
						canKick = true;
						model.Stop("Jump", "JumpLeft", "JumpBackward", "JumpRight", "Fall", "JumpFall", "Vault");
						if (main.TotalTime > lastLandAnimationPlayed + 0.5f)
						{
							footstepTimer.Command.Execute();
							Sound.PlayCue(main, "Land", transform.Position + new Vector3(0, (player.Height * -0.5f) - player.SupportHeight, 0));
						}
						lastLandAnimationPlayed = main.TotalTime;
					}
					else if (player.IsSupported)
					{
						canKick = true;

						Vector2 dir = input.Movement;

						string movementAnimation;
						int animationPriority = 0;
						if (dir.LengthSquared() == 0.0f)
							movementAnimation = "Idle";
						else
							movementAnimation = dir.Y < 0.0f ? "WalkBackwards" : (dir.X > 0.0f ? "StrafeRight" : (dir.X < 0.0f ? "StrafeLeft" : "Walk"));

						if (player.Crouched)
						{
							movementAnimation = "Crouch" + movementAnimation;
							animationPriority = 2;
						}

						Vector3 velocity = player.LinearVelocity;
						velocity.Y = 0;
						float speed = velocity.Length();
						
						if (movementAnimation != "Idle" && movementAnimation != "CrouchIdle")
							model[movementAnimation].Speed = player.Crouched ? 1.0f : (speed / 7.0f);

						footstepTimer.Interval.Value = player.Crouched ? 0.5f : 0.37f / (speed / 7.0f);

						if (!model.IsPlaying(movementAnimation))
						{
							model.Stop
							(
								"CrouchIdle",
								"CrouchWalkBackwards",
								"CrouchWalk",
								"CrouchStrafeRight",
								"CrouchStrafeLeft",
								"Idle",
								"WalkBackwards",
								"Walk",
								"StrafeRight",
								"StrafeLeft",
								"Jump",
								"JumpRight",
								"JumpLeft",
								"JumpBackward"
							);
							model.StartClip(movementAnimation, animationPriority, true);
						}
					}
					else
					{
						model.Stop
						(
							"CrouchIdle",
							"CrouchWalkBackwards",
							"CrouchWalk",
							"CrouchStrafeRight",
							"CrouchStrafeLeft",
							"Idle",
							"WalkBackwards",
							"Walk",
							"StrafeRight",
							"StrafeLeft"
						);
						if (!model.IsPlaying("Fall") && !model.IsPlaying("JumpFall"))
						{
							model.Stop("JumpFall", "Fall");
							bool jumpFall = model.IsPlaying("Jump", "JumpLeft", "JumpBackward", "JumpRight");
							model.StartClip(jumpFall ? "JumpFall" : "Fall", 0, true);
						}
					}

					lastSupported = player.IsSupported;
				}
			});

			// Block possibilities
			const float blockPossibilityTotalLifetime = 2.0f;
			const float blockPossibilityInitialAlpha = 0.125f;

			float blockPossibilityLifetime = 0.0f;

			Dictionary<Map, List<BlockPossibility>> blockPossibilities = new Dictionary<Map, List<BlockPossibility>>();

			Action clearBlockPossibilities = delegate()
			{
				foreach (BlockPossibility block in blockPossibilities.Values.SelectMany(x => x))
					block.Model.Delete.Execute();
				blockPossibilities.Clear();
			};

			Action<BlockPossibility> addBlockPossibility = delegate(BlockPossibility block)
			{
				if (block.Model == null)
				{
					Vector3 start = block.Map.GetRelativePosition(block.StartCoord), end = block.Map.GetRelativePosition(block.EndCoord);

					Matrix matrix = Matrix.CreateScale(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y), Math.Abs(end.Z - start.Z)) * Matrix.CreateTranslation(new Vector3(-0.5f) + (start + end) * 0.5f);

					ModelAlpha box = new ModelAlpha();
					box.Filename.Value = "Models\\alpha-box";
					box.Color.Value = new Vector3(0.5f, 0.7f, 0.9f);
					box.Alpha.Value = blockPossibilityInitialAlpha;
					box.IsInstanced.Value = false;
					box.Editable = false;
					box.Serialize = false;
					box.DrawOrder.Value = 11; // In front of water
					box.CullBoundingBox.Value = false;
					box.DisableCulling.Value = true;
					box.Add(new Binding<Matrix>(box.Transform, () => matrix * Matrix.CreateTranslation(-block.Map.Offset.Value) * block.Map.Transform, block.Map.Transform, block.Map.Offset));
					result.Add(box);
					block.Model = box;
				}

				List<BlockPossibility> mapList;
				if (!blockPossibilities.TryGetValue(block.Map, out mapList))
				{
					mapList = new List<BlockPossibility>();
					blockPossibilities[block.Map] = mapList;
				}
				mapList.Add(block);
				blockPossibilityLifetime = 0.0f;
			};

			update.Add(delegate(float dt)
			{
				if (blockPossibilities.Count > 0)
				{
					blockPossibilityLifetime += dt;
					if (blockPossibilityLifetime > blockPossibilityTotalLifetime)
						clearBlockPossibilities();
					else
					{
						float alpha = blockPossibilityInitialAlpha * (1.0f - (blockPossibilityLifetime / blockPossibilityTotalLifetime));
						foreach (BlockPossibility block in blockPossibilities.Values.SelectMany(x => x))
							block.Model.Alpha.Value = alpha;
					}
				}
			});

			Action<IEnumerable<BlockBuildOrder>, bool> buildBlocks = delegate(IEnumerable<BlockBuildOrder> blocks, bool fake)
			{
				int index = 0;
				EffectBlockFactory factory = Factory.Get<EffectBlockFactory>();
				foreach (BlockBuildOrder entry in blocks)
				{
					if (factory.IsAnimating(new EffectBlockFactory.BlockEntry { Map = entry.Map, Coordinate = entry.Coordinate }))
						continue;

					Entity block = factory.CreateAndBind(main);
					entry.State.ApplyToEffectBlock(block.Get<ModelInstance>());
					block.GetProperty<Vector3>("Offset").Value = entry.Map.GetRelativePosition(entry.Coordinate);

					Vector3 absolutePos = entry.Map.GetAbsolutePosition(entry.Coordinate);

					float distance = (absolutePos - transform.Position).Length();
					block.GetProperty<Vector3>("StartPosition").Value = absolutePos + new Vector3(0.05f, 0.1f, 0.05f) * distance;
					block.GetProperty<Matrix>("StartOrientation").Value = Matrix.CreateRotationX(0.15f * (distance + index)) * Matrix.CreateRotationY(0.15f * (distance + index));
					block.GetProperty<float>("TotalLifetime").Value = Math.Max(0.05f, distance * 0.05f);
					factory.Setup(block, entry.Map.Entity, entry.Coordinate, fake ? 0 : entry.State.ID);
					main.Add(block);
					index++;
				}
			};

			Action<BlockPossibility> instantiateBlockPossibility = delegate(BlockPossibility block)
			{
				Map.CellState state = WorldFactory.StatesByName["Temporary"];
				block.Map.Empty(block.StartCoord.CoordinatesBetween(block.EndCoord));
				block.Map.Fill(block.StartCoord, block.EndCoord, state);
				block.Map.Regenerate();
				block.Model.Delete.Execute();
				List<BlockPossibility> mapList = blockPossibilities[block.Map];
				mapList.Remove(block);
				if (mapList.Count == 0)
					blockPossibilities.Remove(block.Map);
				Vector3 position = 0.5f * (block.Map.GetAbsolutePosition(block.StartCoord) + block.Map.GetAbsolutePosition(block.EndCoord));
				main.AddComponent(new Animation
				(
					new Animation.Repeat
					(
						new Animation.Sequence
						(
							new Animation.Execute(delegate()
							{
								Sound.PlayCue(main, "BuildBlock", position, 1.0f, 0.03f);
							}),
							new Animation.Delay(0.06f)
						),
						3
					)
				));
			};

			// Wall run

			const float minWallRunSpeed = 4.0f;

			Action<Map, Direction, Player.WallRun, Vector3, bool> setUpWallRun = delegate(Map map, Direction dir, Player.WallRun state, Vector3 forwardVector, bool addInitialVelocity)
			{
				stopKick();
				player.AllowUncrouch.Value = true;

				wallRunMap = lastWallRunMap = map;
				wallDirection = lastWallDirection = dir;

				if (state == Player.WallRun.Straight && player.LinearVelocity.Value.Y < 0.0f)
					state = Player.WallRun.Down;

				player.WallRunState.Value = state;

				string animation;
				switch (state)
				{
					case Player.WallRun.Left:
						animation = "WallWalkLeft";
						break;
					case Player.WallRun.Right:
						animation = "WallWalkRight";
						break;
					case Player.WallRun.Straight:
						animation = "WallWalkStraight";
						break;
					case Player.WallRun.Reverse:
						animation = "WallSlideReverse";
						break;
					default:
						animation = "WallSlideDown";
						break;
				}
				if (!model.IsPlaying(animation))
					model.StartClip(animation, 5, true, 0.1f);

				Session.Recorder.Event(main, "WallRun", animation);

				wallRunDirection = state == Player.WallRun.Straight ? map.GetRelativeDirection(Vector3.Up) : (state == Player.WallRun.Down ? map.GetRelativeDirection(Vector3.Down) : dir.Cross(map.GetRelativeDirection(Vector3.Up)));

				if (state == Player.WallRun.Straight || state == Player.WallRun.Down || state == Player.WallRun.Reverse)
				{
					if (state == Player.WallRun.Straight)
					{
						float verticalVelocity = addInitialVelocity ? (player.IsSupported ? player.JumpSpeed : player.LinearVelocity.Value.Y + 7.0f) : player.LinearVelocity.Value.Y;
						player.IsSupported.Value = false;
						player.HasTraction.Value = false;
						player.LinearVelocity.Value = new Vector3(0, verticalVelocity, 0);
					}
					Vector3 wallVector = wallRunMap.GetAbsoluteVector(wallDirection.GetVector());

					if (state == Player.WallRun.Reverse)
						wallVector = -wallVector;
					else
						rotation.Value = (float)Math.Atan2(wallVector.X, wallVector.Z);

					rotationLocked.Value = true;
				}
				else
				{
					Vector3 velocity = map.GetAbsoluteVector(wallRunDirection.GetVector());
					if (Vector3.Dot(velocity, forwardVector) < 0.0f)
					{
						velocity = -velocity;
						wallRunDirection = wallRunDirection.GetReverse();
					}
					rotation.Value = (float)Math.Atan2(velocity.X, velocity.Z);
					rotationLocked.Value = true;

					if (addInitialVelocity)
					{
						velocity.Y = 0.0f;
						float length = velocity.Length();
						if (length > 0)
						{
							velocity /= length;

							Vector3 currentHorizontalVelocity = player.LinearVelocity;
							currentHorizontalVelocity.Y = 0.0f;
							velocity *= Math.Min(player.MaxSpeed * 2.0f, Math.Max(currentHorizontalVelocity.Length() * 1.25f, 6.0f));

							if (state != Player.WallRun.Straight && state != Player.WallRun.Reverse && Vector3.Dot(player.LinearVelocity, forwardVector) < 0.0f)
								velocity = Vector3.Normalize(velocity) * (minWallRunSpeed + 1.0f);

							float currentVerticalSpeed = player.LinearVelocity.Value.Y;
							velocity.Y = (currentVerticalSpeed > 0.0f ? currentVerticalSpeed * 0.7f : currentVerticalSpeed * 0.5f) + 4.5f;
							player.LinearVelocity.Value = velocity;
						}
					}
				}
			};

			float lastWallRunEnded = -1.0f, lastWallJump = -1.0f;
			const float wallRunDelay = 0.5f;

			// Since block possibilities are instantiated on another thread,
			// we have to give that thread some time to do it before checking if there is actually a wall to run on.
			// Otherwise, we will immediately stop wall-running since the wall hasn't been instantiated yet.
			float wallInstantiationTimer = 0.0f;

			Func<Player.WallRun, bool> activateWallRun = delegate(Player.WallRun state)
			{
				wallInstantiationTimer = 0.0f;

				// Prevent the player from repeatedly wall-running and wall-jumping ad infinitum.
				if ((!player.IsSupported || state == Player.WallRun.Straight))
				{
					bool wallRunDelayPassed = main.TotalTime - lastWallRunEnded > wallRunDelay;
					bool wallRunJumpDelayPassed = main.TotalTime - lastWallJump > wallRunDelay;

					Matrix matrix = Matrix.CreateRotationY(rotation);
					Vector3 forwardVector = -matrix.Forward;

					Vector3 wallVector;
					switch (state)
					{
						case Player.WallRun.Straight:
							wallVector = forwardVector;
							break;
						case Player.WallRun.Left:
							wallVector = -matrix.Left;
							break;
						case Player.WallRun.Right:
							wallVector = -matrix.Right;
							break;
						case Player.WallRun.Reverse:
							wallVector = -forwardVector;
							wallInstantiationTimer = 0.25f;
							break;
						default:
							wallVector = Vector3.Zero;
							break;
					}

					Vector3 pos = transform.Position + new Vector3(0, player.Height * -0.5f, 0);

					// Attempt to wall-walk on an existing map
					bool activate = false, addInitialVelocity = false;
					foreach (Map map in Map.ActivePhysicsMaps)
					{
						Map.Coordinate coord = map.GetCoordinate(pos);
						Direction dir = map.GetRelativeDirection(wallVector);
						List<BlockPossibility> mapBlockPossibilities;
						bool hasBlockPossibilities = blockPossibilities.TryGetValue(map, out mapBlockPossibilities);
						for (int i = 1; i < 4; i++)
						{
							Map.Coordinate wallCoord = coord.Move(dir, i);
							if (map[wallCoord].ID != 0)
							{
								bool differentWall = map != lastWallRunMap || dir != lastWallDirection;
								activate = differentWall || wallRunJumpDelayPassed;
								addInitialVelocity = differentWall || wallRunDelayPassed;
							}
							else
							{
								// Check block possibilities
								if (hasBlockPossibilities)
								{
									foreach (BlockPossibility block in mapBlockPossibilities)
									{
										if (wallCoord.Between(block.StartCoord, block.EndCoord))
										{
											instantiateBlockPossibility(block);
											activate = true;
											addInitialVelocity = true;
											wallInstantiationTimer = 0.25f;
											break;
										}
									}
								}
							}

							if (activate)
							{
								// Move so the player is exactly two coordinates away from the wall
								transform.Position.Value = map.GetAbsolutePosition(coord.Move(dir, i - 2)) + new Vector3(0, player.Height * 0.5f, 0);
								break;
							}
						}
						
						if (activate)
						{
							setUpWallRun(map, dir, state, forwardVector, addInitialVelocity);
							break;
						}
					}
					return activate;
				}
				return false;
			};

			Action deactivateWallRun = delegate()
			{
				if (player.WallRunState.Value != Player.WallRun.None)
				{
					lastWallRunEnded = main.TotalTime; // Prevent the player from repeatedly wall-running and wall-jumping ad infinitum.
					wallRunMap = null;
					wallDirection = Direction.None;
					wallRunDirection = Direction.None;
					player.WallRunState.Value = Player.WallRun.None;
					rotationLocked.Value = false;
					model.Stop("WallWalkLeft", "WallWalkRight", "WallWalkStraight", "WallSlideDown", "WallSlideReverse");
				}
			};

			Action<Vector3, Vector3, bool> breakWalls = delegate(Vector3 forward, Vector3 right, bool breakFloor)
			{
				Random random = new Random();
				BlockFactory blockFactory = Factory.Get<BlockFactory>();
				Vector3 pos = transform.Position + new Vector3(0, 0.1f + (player.Height * -0.5f) - player.SupportHeight, 0);
				Vector3 basePos = pos;
				foreach (Map map in Map.ActivePhysicsMaps.ToList())
				{
					List<Map.Coordinate> removals = new List<Map.Coordinate>();
					Quaternion mapQuaternion = map.Entity.Get<Transform>().Quaternion;
					pos = basePos;
					for (int i = 0; i < 5; i++)
					{
						Map.Coordinate center = map.GetCoordinate(pos);
						Map.Coordinate top = map.GetCoordinate(basePos + new Vector3(0, Player.DefaultCharacterHeight + Player.DefaultSupportHeight + 0.5f, 0));
						Direction upDir = map.GetRelativeDirection(Vector3.Up);
						Direction rightDir = map.GetRelativeDirection(right);
						for (Map.Coordinate y = center.Move(upDir.GetReverse(), breakFloor ? 2 : 0); y.GetComponent(upDir) <= top.GetComponent(upDir); y = y.Move(upDir))
						{
							for (Map.Coordinate z = y.Move(rightDir.GetReverse(), 1); z.GetComponent(rightDir) < center.GetComponent(rightDir) + 2; z = z.Move(rightDir))
							{
								Map.CellState state = map[z];
								if (state.ID != 0 && !state.Permanent && !removals.Contains(z))
								{
									removals.Add(z);
									Vector3 cellPos = map.GetAbsolutePosition(z);
									Vector3 toCell = cellPos - pos;
									Entity block = blockFactory.CreateAndBind(main);
									Transform blockTransform = block.Get<Transform>();
									blockTransform.Position.Value = cellPos;
									blockTransform.Quaternion.Value = mapQuaternion;
									state.ApplyToBlock(block);
									toCell += forward * 4.0f;
									toCell.Normalize();
									PhysicsBlock physicsBlock = block.Get<PhysicsBlock>();
									physicsBlock.LinearVelocity.Value = toCell * 15.0f;
									physicsBlock.AngularVelocity.Value = new Vector3(((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f);
									main.Add(block);
								}
							}
						}
						pos += forward * 0.5f;
					}
					if (removals.Count > 0)
					{
						map.Empty(removals);
						map.Regenerate();
					}
				}
			};

			Func<bool, bool, bool> jump = null;

			// Keep the player glued to the wall while we wall walk
			update.Add(delegate(float dt)
			{
				Player.WallRun wallRunState = player.WallRunState;
				if (wallRunState != Player.WallRun.None)
				{
					if (jump(true, true)) // Try to vault up
						return;

					if (!wallRunMap.Active)
					{
						deactivateWallRun();
						return;
					}

					float wallRunSpeed = Vector3.Dot(player.LinearVelocity.Value, wallRunMap.GetAbsoluteVector(wallRunDirection.GetVector()));

					if (wallRunState == Player.WallRun.Straight)
					{
						if (player.IsSupported)
						{
							// We landed on the ground
							deactivateWallRun();
							return;
						}
						else if (wallRunSpeed < 0.0f)
						{
							// Start sliding down
							player.WallRunState.Value = wallRunState = Player.WallRun.Down;
							model.Stop("WallWalkStraight");
							model.StartClip("WallSlideDown", 5, true);
						}
					}
					else if (wallRunState == Player.WallRun.Down || wallRunState == Player.WallRun.Reverse)
					{
						if (player.IsSupported)
						{
							// We landed on the ground
							deactivateWallRun();
							return;
						}
					}
					else
					{
						if (player.IsSupported || wallRunSpeed < minWallRunSpeed)
						{
							// We landed on the ground or we're going too slow to continue wall-running
							deactivateWallRun();
							return;
						}
					}

					string wallRunAnimation;
					switch(wallRunState)
					{
						case Player.WallRun.Straight:
							wallRunAnimation = "WallWalkStraight";
							break;
						case Player.WallRun.Down:
							wallRunAnimation = "WallSlideDown";
							break;
						case Player.WallRun.Left:
							wallRunAnimation = "WallWalkLeft";
							break;
						case Player.WallRun.Right:
							wallRunAnimation = "WallWalkRight";
							break;
						case Player.WallRun.Reverse:
							wallRunAnimation = "WallSlideReverse";
							break;
						default:
							wallRunAnimation = null;
							break;
					}

					if (wallRunAnimation != null)
						model[wallRunAnimation].Speed = Math.Min(1.0f, wallRunSpeed / 9.0f);

					Vector3 pos = transform.Position + new Vector3(0, player.Height * -0.5f, 0);
					Map.Coordinate coord = wallRunMap.GetCoordinate(pos);
					Map.Coordinate wallCoord = coord.Move(wallDirection, 2);
					Map.CellState wallType = wallRunMap[wallCoord];
					footsteps.Cue.Value = wallType.FootstepCue;
					if (player.EnableEnhancedWallRun && wallRunState != Player.WallRun.Straight && (wallRunState != Player.WallRun.Reverse || player.LinearVelocity.Value.Y < 0))
					{
						Direction up = wallRunMap.GetRelativeDirection(Direction.PositiveY);
						Direction right = wallDirection.Cross(up);

						List<BlockBuildOrder> buildCoords = new List<BlockBuildOrder>();

						Map.CellState fillState = WorldFactory.StatesByName["Temporary"];

						const int radius = 5;
						int upwardRadius = wallRunState == Player.WallRun.Down || wallRunState == Player.WallRun.Reverse ? 0 : radius;
						for (Map.Coordinate x = wallCoord.Move(right, -radius); x.GetComponent(right) < wallCoord.GetComponent(right) + radius; x = x.Move(right))
						{
							int dx = x.GetComponent(right) - wallCoord.GetComponent(right);
							for (Map.Coordinate y = x.Move(up, -radius); y.GetComponent(up) < wallCoord.GetComponent(up) + upwardRadius; y = y.Move(up))
							{
								int dy = y.GetComponent(up) - wallCoord.GetComponent(up);
								if ((float)Math.Sqrt(dx * dx + dy * dy) < radius)
								{
									int id = wallRunMap[y].ID;
									if (id == 0 || id == infectedID || id == infectedCriticalID)
									{
										buildCoords.Add(new BlockBuildOrder
										{
											Map = wallRunMap,
											Coordinate = y,
											State = fillState,
										});
									}
								}
							}
						}
						buildBlocks(buildCoords, false);
					}
					else if (wallType.ID == 0 && wallInstantiationTimer == 0.0f) // We ran out of wall to walk on
					{
						deactivateWallRun();
						return;
					}

					if (wallRunMap == null || !wallRunMap.Active)
						return;

					wallInstantiationTimer = Math.Max(0.0f, wallInstantiationTimer - dt);
					
					Vector3 coordPos = wallRunMap.GetAbsolutePosition(coord);

					Vector3 normal = wallRunMap.GetAbsoluteVector(wallDirection.GetVector());
					// Equation of a plane
					// normal (dot) point = d
					float d = Vector3.Dot(normal, coordPos);
					
					// Distance along the normal to keep the player glued to the wall
					float snapDistance = d - Vector3.Dot(pos, normal);

					transform.Position.Value += normal * snapDistance;

					Vector3 velocity = player.LinearVelocity;

					// Also fix the velocity so we don't jitter away from the wall
					velocity -= Vector3.Dot(velocity, normal) * normal;

					// Slow our descent
					velocity += new Vector3(0, (wallRunState == Player.WallRun.Straight ? 3.0f : 10.0f) * dt, 0);

					player.LinearVelocity.Value = velocity;
				}
			});

			player.Add(new NotifyBinding(delegate()
			{
				if (!player.EnableMoves)
				{
					deactivateWallRun();
					stopKick();
					player.SlowMotion.Value = false;
				}
			}, player.EnableMoves));

			// Fall damage
			Vector3 playerLastVelocity = Vector3.Zero;
			const float damageVelocity = -20.0f; // Vertical velocity above which damage occurs
			const float rollingDamageVelocity = -28.0f; // Damage velocity when rolling

			Action<float> fallDamage = delegate(float verticalVelocity)
			{
				float v = model.IsPlaying("Roll") ? rollingDamageVelocity : damageVelocity;
				if (verticalVelocity < v)
				{
					player.Health.Value += (verticalVelocity - v) * 0.2f;
					if (player.Health.Value == 0.0f)
						((GameMain)main).RespawnRewindLength = GameMain.DefaultRespawnRewindLength;
					else
					{
						player.LinearVelocity.Value = new Vector3(0, player.LinearVelocity.Value.Y, 0);
						if (!model.IsPlaying("Roll"))
							model.StartClip("Land", 1, false, 0.1f);
					}
				}
			};

			// Damage the player if they hit something too hard
			result.Add(new CommandBinding<BEPUphysics.BroadPhaseEntries.Collidable, ContactCollection>(player.Collided, delegate(BEPUphysics.BroadPhaseEntries.Collidable other, ContactCollection contacts)
			{
				DynamicMap map = other.Tag as DynamicMap;
				if (map != null)
				{
					float force = contacts[contacts.Count - 1].NormalImpulse;
					float threshold = map.Entity.Type == "FallingTower" ? 14.0f : 24.0f;
					float playerLastSpeed = Vector3.Dot(playerLastVelocity, Vector3.Normalize(-contacts[contacts.Count - 1].Contact.Normal)) * 2.5f;
					if (force > threshold + playerLastSpeed + 4.0f)
						player.Health.Value -= (force - threshold - playerLastSpeed) * 0.04f;
				}
			}));

			update.Add(delegate(float dt)
			{
				if (!lastSupported && player.IsSupported)
				{
					// Damage the player if they fall too hard and they're not smashing or rolling
					fallDamage(playerLastVelocity.Y - player.LinearVelocity.Value.Y);
				}
				playerLastVelocity = player.LinearVelocity;
			});

			Direction[] platformBuildableDirections = DirectionExtensions.HorizontalDirections.Union(new[] { Direction.NegativeY }).ToArray();

			// Function for finding a platform to build for the player
			Func<Vector3, BlockPossibility> findPlatform = delegate(Vector3 position)
			{
				const int searchDistance = 20;
				const int platformSize = 3;

				int shortestDistance = searchDistance;
				Direction relativeShortestDirection = Direction.None, absoluteShortestDirection = Direction.None;
				Map.Coordinate shortestCoordinate = new Map.Coordinate();
				Map shortestMap = null;

				EffectBlockFactory blockFactory = Factory.Get<EffectBlockFactory>();
				foreach (Map map in Map.ActivePhysicsMaps)
				{
					List<Matrix> results = new List<Matrix>();
					Map.CellState fillValue = WorldFactory.StatesByName["Temporary"];
					Map.Coordinate absolutePlayerCoord = map.GetCoordinate(position);
					bool inMap = map.GetChunk(absolutePlayerCoord, false) != null;
					foreach (Direction absoluteDir in platformBuildableDirections)
					{
						Map.Coordinate playerCoord = absoluteDir == Direction.NegativeY ? absolutePlayerCoord : map.GetCoordinate(position + new Vector3(0, platformSize / -2.0f, 0));
						Direction relativeDir = map.GetRelativeDirection(absoluteDir);
						if (!inMap && map.GetChunk(playerCoord.Move(relativeDir, searchDistance), false) == null)
							continue;

						for (int i = 1; i < shortestDistance; i++)
						{
							Map.Coordinate coord = playerCoord.Move(relativeDir, i);
							Map.CellState state = map[coord];
							if (state.ID != 0 || blockFactory.IsAnimating(new EffectBlockFactory.BlockEntry { Map = map, Coordinate = coord, }))
							{
								shortestDistance = i;
								relativeShortestDirection = relativeDir;
								absoluteShortestDirection = absoluteDir;
								shortestCoordinate = playerCoord;
								shortestMap = map;
								break;
							}
						}
					}
				}

				if (shortestMap != null && shortestDistance > 1)
				{
					Direction yDir = relativeShortestDirection.IsParallel(Direction.PositiveY) ? Direction.PositiveX : Direction.PositiveY;
					Direction zDir = relativeShortestDirection.Cross(yDir);

					int initialOffset = absoluteShortestDirection == Direction.NegativeY ? 0 : -2;
					Map.Coordinate startCoord = shortestCoordinate.Move(relativeShortestDirection, initialOffset).Move(yDir, platformSize / -2).Move(zDir, platformSize / -2);
					Map.Coordinate endCoord = startCoord.Move(relativeShortestDirection, -initialOffset + shortestDistance).Move(yDir, platformSize).Move(zDir, platformSize);

					return new BlockPossibility
					{
						Map = shortestMap,
						StartCoord = new Map.Coordinate { X = Math.Min(startCoord.X, endCoord.X), Y = Math.Min(startCoord.Y, endCoord.Y), Z = Math.Min(startCoord.Z, endCoord.Z) },
						EndCoord = new Map.Coordinate { X = Math.Max(startCoord.X, endCoord.X), Y = Math.Max(startCoord.Y, endCoord.Y), Z = Math.Max(startCoord.Z, endCoord.Z) },
					};
				}
				return null;
			};

			// Function for finding a wall to build for the player
			Func<Vector3, Vector2, BlockPossibility> findWall = delegate(Vector3 position, Vector2 direction)
			{
				const int searchDistance = 20;
				const int additionalDistance = 6;

				Map shortestMap = null;
				Map.Coordinate shortestPlayerCoord = new Map.Coordinate();
				Direction shortestWallDirection = Direction.None;
				Direction shortestBuildDirection = Direction.None;
				int shortestDistance = searchDistance;

				foreach (Map map in Map.ActivePhysicsMaps)
				{
					foreach (Direction absoluteWallDir in DirectionExtensions.HorizontalDirections)
					{
						Direction relativeWallDir = map.GetRelativeDirection(absoluteWallDir);
						Vector3 wallVector = map.GetAbsoluteVector(relativeWallDir.GetVector());
						float dot = Vector2.Dot(direction, Vector2.Normalize(new Vector2(wallVector.X, wallVector.Z)));
						if (dot > -0.25f && dot < 0.8f)
						{
							Map.Coordinate coord = map.GetCoordinate(position).Move(relativeWallDir, 2);
							foreach (Direction dir in DirectionExtensions.Directions.Where(x => x.IsPerpendicular(relativeWallDir)))
							{
								for (int i = 0; i < shortestDistance; i++)
								{
									Map.Coordinate c = coord.Move(dir, i);
									if (map[c].ID != 0)
									{
										shortestMap = map;
										shortestBuildDirection = dir;
										shortestWallDirection = relativeWallDir;
										shortestDistance = i;
										shortestPlayerCoord = coord;
									}
								}
							}
						}
					}
				}

				if (shortestMap != null)
				{
					// Found something to build a wall on.
					Direction dirU = shortestBuildDirection;
					Direction dirV = dirU.Cross(shortestWallDirection);
					Map.Coordinate startCoord = shortestPlayerCoord.Move(dirU, shortestDistance).Move(dirV, additionalDistance);
					Map.Coordinate endCoord = shortestPlayerCoord.Move(dirU, -additionalDistance).Move(dirV, -additionalDistance).Move(shortestWallDirection);
					return new BlockPossibility
					{
						Map = shortestMap,
						StartCoord = new Map.Coordinate { X = Math.Min(startCoord.X, endCoord.X), Y = Math.Min(startCoord.Y, endCoord.Y), Z = Math.Min(startCoord.Z, endCoord.Z) },
						EndCoord = new Map.Coordinate { X = Math.Max(startCoord.X, endCoord.X), Y = Math.Max(startCoord.Y, endCoord.Y), Z = Math.Max(startCoord.Z, endCoord.Z) },
					};
				}
				
				return null;
			};

			Updater vaultMover = null;

			float rollEnded = -1.0f;

			Action<Map, Map.Coordinate, Vector3> vault = delegate(Map map, Map.Coordinate coord, Vector3 forward)
			{
				const float vaultVerticalSpeed = 8.0f;
				const float maxVaultTime = 1.25f;

				player.LinearVelocity.Value = new Vector3(0, vaultVerticalSpeed, 0);
				player.IsSupported.Value = false;
				player.HasTraction.Value = false;
				rotationLocked.Value = true;
				player.EnableWalking.Value = false;
				player.Crouched.Value = true;
				player.AllowUncrouch.Value = false;

				float vaultTime = 0.0f;
				if (vaultMover != null)
					vaultMover.Delete.Execute(); // If we're already vaulting, start a new vault

				vaultMover = new Updater
				{
					delegate(float dt)
					{
						vaultTime += dt;

						bool delete = false;

						if (player.IsSupported || vaultTime > maxVaultTime || player.LinearVelocity.Value.Y < 0.0f) // Max vault time ensures we never get stuck
							delete = true;
						else if (transform.Position.Value.Y + (player.Height * -0.5f) - player.SupportHeight > map.GetAbsolutePosition(coord).Y + 0.1f) // Move forward
						{
							Vector3 velocity = player.Body.LinearVelocity; // Stop moving upward, start moving forward
							velocity.Y = 0.0f;
							velocity += forward * player.MaxSpeed;
							player.LinearVelocity.Value = velocity;
							player.Body.ActivityInformation.Activate();
							delete = true;
						}
						else
							player.LinearVelocity.Value = new Vector3(0, vaultVerticalSpeed, 0.0f);

						if (delete)
						{
							vaultMover.Delete.Execute(); // Make sure we get rid of this vault mover
							vaultMover = null;
							result.Add(new Animation
							(
								new Animation.Delay(0.25f),
								new Animation.Set<bool>(player.AllowUncrouch, true),
								new Animation.Set<bool>(rotationLocked, false),
								new Animation.Set<bool>(player.EnableWalking, true)
							));
						}
					}
				};
				result.RemoveComponent("VaultMover");
				result.Add("VaultMover", vaultMover);
			};

			jump = delegate(bool allowVault, bool onlyVault)
			{
				if (player.Crouched)
					return false;

				bool supported = player.IsSupported;

				// Check if we're vaulting
				Matrix rotationMatrix = Matrix.CreateRotationY(rotation);
				bool vaulting = false;
				if (allowVault)
				{
					foreach (Map map in Map.ActivePhysicsMaps)
					{
						Direction up = map.GetRelativeDirection(Direction.PositiveY);
						Direction right = map.GetRelativeDirection(Vector3.Cross(Vector3.Up, -rotationMatrix.Forward));
						Vector3 pos = transform.Position + rotationMatrix.Forward * -1.75f;
						Map.Coordinate baseCoord = map.GetCoordinate(pos).Move(up, 1);
						for (int x = -1; x < 2; x++)
						{
							Map.Coordinate coord = baseCoord.Move(right, x);
							for (int i = 0; i < 3; i++)
							{
								Map.Coordinate downCoord = coord.Move(up.GetReverse());

								if (map[coord].ID != 0)
									break;
								else if (map[downCoord].ID != 0)
								{
									// Vault
									vault(map, coord, -rotationMatrix.Forward);
									vaulting = true;
									break;
								}
								coord = coord.Move(up.GetReverse());
							}
							if (vaulting)
								break;
						}
						if (vaulting)
							break;
					}
				}

				Vector2 jumpDirection = player.MovementDirection;

				bool wallJumping = false;

				const float wallJumpHorizontalVelocityAmount = 0.75f;
				const float wallJumpDistance = 2.0f;

				Action<Map, Direction, Map.Coordinate> wallJump = delegate(Map wallJumpMap, Direction wallNormalDirection, Map.Coordinate wallCoordinate)
				{
					lastWallRunMap = wallJumpMap;
					lastWallDirection = wallNormalDirection.GetReverse();
					lastWallJump = main.TotalTime;

					Map.CellState wallType = wallJumpMap[wallCoordinate];
					if (wallType.ID == 0) // Empty. Must be a block possibility that hasn't been instantiated yet
						wallType = WorldFactory.StatesByName["Temporary"];
					footsteps.Cue.Value = wallType.FootstepCue;
					footsteps.Play.Execute();

					walkedOn.Execute(wallJumpMap, wallCoordinate);

					wallJumping = true;
					// Set up wall jump velocity
					Vector3 absoluteWallNormal = wallJumpMap.GetAbsoluteVector(wallNormalDirection.GetVector());
					Vector2 wallNormal2 = new Vector2(absoluteWallNormal.X, absoluteWallNormal.Z);
					wallNormal2.Normalize();

					bool wallRunningStraight = player.WallRunState.Value == Player.WallRun.Straight || player.WallRunState.Value == Player.WallRun.Down;
					if (wallRunningStraight)
						jumpDirection = new Vector2(main.Camera.Forward.Value.X, main.Camera.Forward.Value.Z);
					else
						jumpDirection = new Vector2(-rotationMatrix.Forward.X, -rotationMatrix.Forward.Z);

					jumpDirection.Normalize();

					float dot = Vector2.Dot(wallNormal2, jumpDirection);
					if (dot < 0)
						jumpDirection = jumpDirection - (2.0f * dot * wallNormal2);
					jumpDirection *= wallJumpHorizontalVelocityAmount;

					if (!wallRunningStraight && Math.Abs(dot) < 0.5f)
					{
						// If we're jumping perpendicular to the wall, add some velocity so we jump away from the wall a bit
						jumpDirection += wallJumpHorizontalVelocityAmount * 0.75f * wallNormal2;
					}

					if (dot < -0.8f)
					{
						// We're facing the wall and jumping backward away from it
						// Do a quick spin move

						const float spinDuration = 0.25f;

						float spinAmount = (-(float)Math.Atan2(jumpDirection.Y, jumpDirection.X) + (float)Math.PI * 0.5f).ClosestAngle(input.Mouse.Value.X) - input.Mouse.Value.X;

						result.Add(new Animation
						(
							new Animation.Custom(delegate(float x)
							{
								Vector2 oldMouse = input.Mouse;
								input.Mouse.Value = new Vector2((oldMouse.X + (main.ElapsedTime / spinDuration) * spinAmount).ToAngleRange(), oldMouse.Y);
							}, spinDuration)
						));
					}
				};

				if (!onlyVault && !vaulting && !supported && player.WallRunState.Value == Player.WallRun.None)
				{
					// We're not vaulting, not doing our normal jump, and not wall-walking
					// See if we can wall-jump
					Vector3 playerPos = transform.Position;
					Map.GlobalRaycastResult? wallRaycastHit = null;
					Vector3 wallRaycastDirection = Vector3.Zero;

					foreach (Vector3 dir in new[] { rotationMatrix.Left, rotationMatrix.Right, rotationMatrix.Backward, rotationMatrix.Forward })
					{
						Map.GlobalRaycastResult hit = Map.GlobalRaycast(playerPos, dir, wallJumpDistance);
						if (hit.Map != null)
						{
							wallRaycastDirection = dir;
							wallRaycastHit = hit;
							break;
						}
					}

					if (wallRaycastHit != null)
					{
						Map m = wallRaycastHit.Value.Map;
						wallJump(m, wallRaycastHit.Value.Normal, wallRaycastHit.Value.Coordinate.Value);
					}
				}

				// If we're wall-running, we can wall-jump
				// Add some velocity so we jump away from the wall a bit
				if (!onlyVault && player.WallRunState.Value != Player.WallRun.None)
				{
					Vector3 pos = transform.Position + new Vector3(0, (player.Height * -0.5f) - 0.5f, 0);
					Map.Coordinate coord = wallRunMap.GetCoordinate(pos);
					wallJump(wallRunMap, wallDirection.GetReverse(), coord.Move(wallDirection, 2));
				}

				bool go = vaulting || (!onlyVault && (supported || wallJumping));

				if (!go && !onlyVault)
				{
					// Check block possibilities beneath us
					Vector3 jumpPos = transform.Position + new Vector3(0, player.Height * -0.5f - player.SupportHeight - 1.0f, 0);
					foreach (BlockPossibility possibility in blockPossibilities.Values.SelectMany(x => x))
					{
						if (possibility.Map.GetCoordinate(jumpPos).Between(possibility.StartCoord, possibility.EndCoord)
							&& !possibility.Map.GetCoordinate(jumpPos + new Vector3(2.0f)).Between(possibility.StartCoord, possibility.EndCoord))
						{
							instantiateBlockPossibility(possibility);
							go = true;
							break;
						}
					}
				}

				if (!go && allowVault)
				{
					// Check block possibilities for vaulting
					foreach (BlockPossibility possibility in blockPossibilities.Values.SelectMany(x => x))
					{
						Direction up = possibility.Map.GetRelativeDirection(Direction.PositiveY);
						Vector3 pos = transform.Position + rotationMatrix.Forward * -1.75f;
						Map.Coordinate coord = possibility.Map.GetCoordinate(pos).Move(up, 1);
						for (int i = 0; i < 4; i++)
						{
							Map.Coordinate downCoord = coord.Move(up.GetReverse());
							if (!coord.Between(possibility.StartCoord, possibility.EndCoord) && downCoord.Between(possibility.StartCoord, possibility.EndCoord))
							{
								instantiateBlockPossibility(possibility);
								vault(possibility.Map, coord, -rotationMatrix.Forward);
								vaulting = true;
								go = true;
								break;
							}
							coord = coord.Move(up.GetReverse());
						}
						if (vaulting)
							break;
					}
				}

				if (!go && !onlyVault)
				{
					// Check block possibilities for wall jumping
					Vector3 playerPos = transform.Position;
					Vector3[] wallJumpDirections = new[] { rotationMatrix.Left, rotationMatrix.Right, rotationMatrix.Backward, rotationMatrix.Forward };
					foreach (BlockPossibility possibility in blockPossibilities.Values.SelectMany(x => x))
					{
						foreach (Vector3 dir in wallJumpDirections)
						{
							foreach (Map.Coordinate coord in possibility.Map.Rasterize(playerPos, playerPos + (dir * wallJumpDistance)))
							{
								if (coord.Between(possibility.StartCoord, possibility.EndCoord))
								{
									instantiateBlockPossibility(possibility);
									wallJump(possibility.Map, possibility.Map.GetRelativeDirection(dir).GetReverse(), coord);
									wallJumping = true;
									break;
								}
							}
							if (wallJumping)
								break;
						}
						if (wallJumping)
							break;
					}
				}

				if (go)
				{
					if (!supported && !wallJumping)
					{
						// We haven't hit the ground, so fall damage will not be handled by the physics system.
						// Need to do it manually here.
						fallDamage(player.LinearVelocity.Value.Y);
					}

					if (!vaulting)
					{
						// Just a normal jump.
						Vector3 velocity = player.LinearVelocity;
						float currentVerticalSpeed = velocity.Y;
						velocity.Y = 0.0f;
						float jumpSpeed = jumpDirection.Length();
						if (jumpSpeed > 0)
							jumpDirection *= (wallJumping ? player.MaxSpeed : velocity.Length()) / jumpSpeed;

						float totalMultiplier = 1.0f;
						float verticalMultiplier = 1.0f;

						if (main.TotalTime - rollEnded < 0.3f)
							totalMultiplier *= 1.5f;

						float verticalJumpSpeed = (player.JumpSpeed * verticalMultiplier) + (currentVerticalSpeed * 0.5f);

						player.LinearVelocity.Value = new Vector3(jumpDirection.X, verticalJumpSpeed, jumpDirection.Y) * totalMultiplier;

						if (supported && player.SupportEntity.Value != null)
						{
							Vector3 impulsePosition = transform.Position + new Vector3(0, player.Height * -0.5f - player.SupportHeight, 0);
							Vector3 impulse = player.LinearVelocity.Value * player.Body.Mass * -1.0f;
							player.SupportEntity.Value.ApplyImpulse(ref impulsePosition, ref impulse);
						}

						Session.Recorder.Event(main, "Jump");

						player.IsSupported.Value = false;
						player.SupportEntity.Value = null;
						player.HasTraction.Value = false;
					}

					Sound.PlayCue(main, vaulting ? "Vault" : "Jump", transform.Position);

					model.Stop("Vault", "Jump", "JumpLeft", "JumpRight", "JumpBackward");
					if (vaulting)
					{
						Session.Recorder.Event(main, "Vault");
						model.StartClip("Vault", 4, false, 0.1f);
					}
					else
					{
						Vector3 velocity = -Vector3.TransformNormal(player.LinearVelocity, Matrix.CreateRotationY(-rotation));
						velocity.Y = 0.0f;
						if (player.WallRunState.Value == Player.WallRun.Left || player.WallRunState.Value == Player.WallRun.Right)
							velocity.Z = 0.0f;
						else if (wallJumping)
							velocity.Z *= 0.5f;
						else
							velocity.X = 0.0f;
						Direction direction = DirectionExtensions.GetDirectionFromVector(velocity);
						string animation;
						switch (direction)
						{
							case Direction.NegativeX:
								animation = "JumpLeft";
								break;
							case Direction.PositiveX:
								animation = "JumpRight";
								break;
							case Direction.PositiveZ:
								animation = wallJumping ? "JumpBackward" : "Jump";
								break;
							default:
								animation = "Jump";
								break;
						}
						model.StartClip(animation, 4, false, 0.1f);
					}

					// Deactivate any wall walking we're doing
					deactivateWallRun();

					// Play a footstep sound since we're jumping off the ground
					footsteps.Play.Execute();

					return true;
				}

				return false;
			};

			Action<Queue<Prediction>, Vector3, Vector3, float, int> predictJump = delegate(Queue<Prediction> predictions, Vector3 start, Vector3 v, float interval, int level)
			{
				for (float time = interval; time < (level == 0 ? 1.5f : 1.0f); time += interval)
					predictions.Enqueue(new Prediction { Position = start + (v * time) + (time * time * 0.5f * main.Space.ForceUpdater.Gravity), Time = time, Level = level });
			};

			Func<Queue<Prediction>, float, Vector3> startSlowMo = delegate(Queue<Prediction> predictions, float interval)
			{
				// Go into slow-mo and show block possibilities
				player.SlowMotion.Value = true;

				clearBlockPossibilities();

				Vector3 startPosition = transform.Position + new Vector3(0, (player.Height * -0.5f) - player.SupportHeight, 0);

				Vector3 straightAhead = Matrix.CreateRotationY(rotation).Forward * -player.MaxSpeed;

				Vector3 velocity = player.LinearVelocity;
				if (velocity.Length() < player.MaxSpeed * 0.25f)
					velocity += straightAhead * 0.5f;

				predictJump(predictions, startPosition, velocity, interval, 0);

				Vector3 jumpVelocity = velocity;
				jumpVelocity.Y = player.JumpSpeed;

				return jumpVelocity;
			};

			Func<float> getPredictionInterval = delegate()
			{
				// Interval is the time in seconds between locations where we will check for buildable platforms
				return 0.3f * (8.0f / Math.Max(5.0f, player.LinearVelocity.Value.Length()));
			};

			Action<Vector3> vaultDown = delegate(Vector3 forward)
			{
				const float vaultVerticalSpeed = 8.0f;
				const float maxVaultTime = 1.25f;

				Vector3 velocity = forward * player.MaxSpeed;
				velocity.Y = player.LinearVelocity.Value.Y;
				player.LinearVelocity.Value = velocity;
				rotationLocked.Value = true;
				player.EnableWalking.Value = false;
				player.Crouched.Value = true;
				player.AllowUncrouch.Value = false;

				float vaultTime = 0.0f;
				if (vaultMover != null)
					vaultMover.Delete.Execute(); // If we're already vaulting, start a new vault

				float walkOffEdgeTimer = 0.0f;
				Vector3 originalPosition = transform.Position;

				vaultMover = new Updater
				{
					delegate(float dt)
					{
						vaultTime += dt;

						bool delete = false;

						if (vaultTime > maxVaultTime) // Max vault time ensures we never get stuck
							delete = true;
						else if (walkOffEdgeTimer > 0.2f && player.IsSupported)
							delete = true; // We went over the edge and hit the ground. Stop.
						else if (!player.IsSupported) // We hit the edge, go down it
						{
							walkOffEdgeTimer += dt;

							if (walkOffEdgeTimer > 0.1f)
								player.LinearVelocity.Value = new Vector3(0, -vaultVerticalSpeed, 0);

							if (!input.GetInput(settings.Parkour))
							{
								delete = true;
								result.Add(new Animation
								(
									new Animation.Delay(0.25f),
									new Animation.Set<bool>(rotationLocked, false)
								));
							}
							else
							{
								if (transform.Position.Value.Y < originalPosition.Y - 1.0f && activateWallRun(Player.WallRun.Reverse))
									delete = true;
							}
						}
						else
						{
							velocity = forward * player.MaxSpeed;
							velocity.Y = player.LinearVelocity.Value.Y;
							player.LinearVelocity.Value = velocity;
						}

						if (delete)
						{
							player.AllowUncrouch.Value = true;
							player.EnableWalking.Value = true;
							vaultMover.Delete.Execute(); // Make sure we get rid of this vault mover
							vaultMover = null;
						}
					}
				};
				result.RemoveComponent("VaultMover");
				result.Add("VaultMover", vaultMover);
			};

			Func<bool> tryVaultDown = delegate()
			{
				if (player.Crouched || !player.IsSupported)
					return false;

				Matrix rotationMatrix = Matrix.CreateRotationY(rotation);
				bool foundObstacle = false;
				foreach (Map map in Map.ActivePhysicsMaps)
				{
					Direction down = map.GetRelativeDirection(Direction.NegativeY);
					Vector3 pos = transform.Position + rotationMatrix.Forward * -1.75f;
					Map.Coordinate coord = map.GetCoordinate(pos);

					for (int i = 0; i < 5; i++)
					{
						if (map[coord].ID != 0)
						{
							foundObstacle = true;
							break;
						}
						coord = coord.Move(down);
					}

					if (foundObstacle)
						break;
				}

				if (!foundObstacle)
				{
					// Vault
					vaultDown(-rotationMatrix.Forward);
				}
				return !foundObstacle;
			};

			// Jumping
			input.Bind(settings.Jump, PCInput.InputState.Down, delegate()
			{
				if (!player.EnableMoves)
					return;

				// Don't allow vaulting
				// Also don't try anything if we're crouched or in the middle of vaulting
				if (vaultMover == null && !jump(false, false) && player.EnableSlowMotion && (!player.Crouched || !player.IsSupported))
				{
					float interval = getPredictionInterval();

					Queue<Prediction> predictions = new Queue<Prediction>();
					Vector3 jumpVelocity = startSlowMo(predictions, interval);

					float[] lastPredictionHit = new float[] { 0.0f, 0.0f };

					while (predictions.Count > 0)
					{
						Prediction prediction = predictions.Dequeue();

						if (prediction.Time > lastPredictionHit[prediction.Level] + (interval * 1.5f))
						{
							BlockPossibility possibility = findPlatform(prediction.Position);
							if (possibility != null)
							{
								lastPredictionHit[prediction.Level] = prediction.Time;
								addBlockPossibility(possibility);
								if (prediction.Level == 0)
									predictJump(predictions, prediction.Position, jumpVelocity, interval, prediction.Level + 1);
							}
						}
					}
				}
			});

			input.Bind(settings.Jump, player.Jumping);

			input.Bind(settings.Jump, PCInput.InputState.Up, delegate()
			{
				player.SlowMotion.Value = false;
			});

			// Wall-run, vault, predictive
			input.Bind(settings.Parkour, PCInput.InputState.Down, delegate()
			{
				if (!player.EnableMoves || (player.Crouched && player.IsSupported))
					return;

				bool vaulted = jump(true, true); // Try vaulting first

				bool wallRan = false;
				if (!vaulted && player.EnableWallRun && vaultMover == null)
				{
					// Try to wall-run
					if (!(wallRan = activateWallRun(Player.WallRun.Straight)) && player.EnableWallRunHorizontal)
						if (!(wallRan = activateWallRun(Player.WallRun.Left)))
							if (!(wallRan = activateWallRun(Player.WallRun.Right)))
								wallRan = activateWallRun(Player.WallRun.Reverse);
				}

				if (!vaulted)
					vaulted = tryVaultDown();

				if (!vaulted && !wallRan && !player.IsSupported && player.EnableSlowMotion)
				{
					// Predict block possibilities
					Queue<Prediction> predictions = new Queue<Prediction>();
					Vector3 jumpVelocity = startSlowMo(predictions, getPredictionInterval());
					Vector2 direction = Vector2.Normalize(new Vector2(jumpVelocity.X, jumpVelocity.Z));

					while (predictions.Count > 0)
					{
						Prediction prediction = predictions.Dequeue();
						BlockPossibility possibility = findWall(prediction.Position, direction);
						if (possibility != null)
						{
							addBlockPossibility(possibility);
							break;
						}
					}
				}
			});

			input.Bind(settings.Parkour, PCInput.InputState.Up, delegate()
			{
				deactivateWallRun();
				player.SlowMotion.Value = false;
			});

			Action<Map, Map.Coordinate, Direction, Direction> buildFloor = delegate(Map floorMap, Map.Coordinate floorCoordinate, Direction forwardDir, Direction rightDir)
			{
				List<BlockBuildOrder> buildCoords = new List<BlockBuildOrder>();

				Map.Coordinate newFloorCoordinate = floorMap.GetCoordinate(transform.Position);

				floorCoordinate.SetComponent(rightDir, newFloorCoordinate.GetComponent(rightDir));
				floorCoordinate.SetComponent(forwardDir, newFloorCoordinate.GetComponent(forwardDir));

				Map.CellState fillState = WorldFactory.StatesByName["Temporary"];

				const int radius = 3;
				for (Map.Coordinate x = floorCoordinate.Move(rightDir, -radius); x.GetComponent(rightDir) < floorCoordinate.GetComponent(rightDir) + radius; x = x.Move(rightDir))
				{
					int dx = x.GetComponent(rightDir) - floorCoordinate.GetComponent(rightDir);
					for (Map.Coordinate y = x.Move(forwardDir, -radius); y.GetComponent(forwardDir) < floorCoordinate.GetComponent(forwardDir) + radius; y = y.Move(forwardDir))
					{
						int dy = y.GetComponent(forwardDir) - floorCoordinate.GetComponent(forwardDir);
						if ((float)Math.Sqrt(dx * dx + dy * dy) < radius && floorMap[y].ID == 0)
						{
							buildCoords.Add(new BlockBuildOrder
							{
								Map = floorMap,
								Coordinate = y,
								State = fillState,
							});
						}
					}
				}
				buildBlocks(buildCoords, false);
			};

			Updater kickUpdate = null;
			stopKick = delegate()
			{
				if (kickUpdate != null)
				{
					kickUpdate.Delete.Execute();
					kickUpdate = null;
					model.Stop("Kick");
					player.EnableWalking.Value = true;
					player.AllowUncrouch.Value = true;
					rotationLocked.Value = false;
				}
			};
			input.Bind(settings.Kick, PCInput.InputState.Down, delegate()
			{
				if (!player.EnableMoves)
					return;

				Matrix rotationMatrix = Matrix.CreateRotationY(rotation);
				Vector3 forward = -rotationMatrix.Forward;
				Vector3 right = rotationMatrix.Right;

				if (!model.IsPlaying("PlayerReload") && !model.IsPlaying("Roll") && player.EnableKick && canKick && Vector3.Dot(player.LinearVelocity, forward) > 1.0f && kickUpdate == null)
				{
					// Kick
					canKick = false;

					Session.Recorder.Event(main, "Kick");

					deactivateWallRun();

					model.Stop
					(
						"CrouchWalkBackwards",
						"CrouchWalk",
						"CrouchStrafeRight",
						"CrouchStrafeLeft",
						"Idle",
						"WalkBackwards",
						"Walk",
						"StrafeRight",
						"StrafeLeft",
						"Jump",
						"JumpLeft",
						"JumpRight",
						"JumpBackward"
					);
					model.StartClip("CrouchIdle", 2, true);

					player.EnableWalking.Value = false;
					rotationLocked.Value = true;

					player.Crouched.Value = true;
					player.AllowUncrouch.Value = false;


					player.LinearVelocity.Value += forward * player.LinearVelocity.Value.Length() * 0.5f + new Vector3(0, player.JumpSpeed * 0.25f, 0);

					Vector3 kickVelocity = player.LinearVelocity;

					result.Add(new Animation
					(
						new Animation.Delay(0.25f),
						new Animation.Execute(delegate() { Sound.PlayCue(main, "Kick", transform.Position); })
					));
					model.StartClip("Kick", 5, false);

					Vector3 playerPos = transform.Position + new Vector3(0, (player.Height * -0.5f) - player.SupportHeight, 0);

					Map.GlobalRaycastResult floorRaycast = new Map.GlobalRaycastResult();

					bool shouldBuildFloor = !player.IsSupported && player.EnableEnhancedWallRun && (floorRaycast = Map.GlobalRaycast(playerPos, Vector3.Down, player.Height)).Map != null;

					Direction forwardDir = Direction.None;
					Direction rightDir = Direction.None;

					if (shouldBuildFloor)
					{
						forwardDir = floorRaycast.Map.GetRelativeDirection(forward);
						rightDir = floorRaycast.Map.GetRelativeDirection(right);
					}

					float kickTime = 0.0f;
					kickUpdate = new Updater
					{
						delegate(float dt)
						{
							kickTime += dt;
							if (kickTime > 0.75f || player.LinearVelocity.Value.Length() < 0.1f)
								stopKick();
							else
							{
								player.LinearVelocity.Value = new Vector3(kickVelocity.X, player.LinearVelocity.Value.Y, kickVelocity.Z);
								breakWalls(forward, right, false);
								if (shouldBuildFloor)
									buildFloor(floorRaycast.Map, floorRaycast.Coordinate.Value, forwardDir, rightDir);
							}
						}
					};
					result.Add(kickUpdate);
				}
			});

			bool rolling = false;
			input.Bind(settings.Roll, PCInput.InputState.Down, delegate()
			{
				if (!player.EnableMoves)
					return;

				if (!model.IsPlaying("PlayerReload") && kickUpdate == null && !rolling && player.EnableRoll && !player.IsSwimming)
				{
					// Try to roll
					Vector3 playerPos = transform.Position + new Vector3(0, (player.Height * -0.5f) - player.SupportHeight, 0);

					Matrix rotationMatrix = Matrix.CreateRotationY(rotation);
					Vector3 forward = -rotationMatrix.Forward;
					Vector3 right = rotationMatrix.Right;

					Map.GlobalRaycastResult floorRaycast = Map.GlobalRaycast(playerPos, Vector3.Down, player.Height + 1.0f);

					bool nearGround = (player.IsSupported || player.LinearVelocity.Value.Y <= 0.0f) && floorRaycast.Map != null;

					bool instantiatedBlockPossibility = false;

					Map.Coordinate floorCoordinate = new Map.Coordinate();
					Map floorMap = null;

					if (nearGround)
					{
						floorMap = floorRaycast.Map;
						floorCoordinate = floorRaycast.Coordinate.Value;
					}
					else
					{
						// Check for block possibilities
						foreach (BlockPossibility block in blockPossibilities.Values.SelectMany(x => x))
						{
							bool first = true;
							foreach (Map.Coordinate coord in block.Map.Rasterize(playerPos + Vector3.Up * 2.0f, playerPos + (Vector3.Down * (player.Height + 3.0f))))
							{
								if (coord.Between(block.StartCoord, block.EndCoord))
								{
									if (first)
										break; // If the top coord is intersecting the possible block, we're too far down into the block. Need to be at the top.
									instantiateBlockPossibility(block);
									instantiatedBlockPossibility = true;
									floorMap = block.Map;
									floorCoordinate = coord;
									nearGround = true;
									break;
								}
								first = false;
							}
							if (nearGround)
								break;
						}
					}

					if (nearGround)
					{
						// We're rolling.
						rolling = true;

						Session.Recorder.Event(main, "Roll");

						deactivateWallRun();

						model.Stop
						(
							"CrouchWalkBackwards",
							"CrouchWalk",
							"CrouchStrafeRight",
							"CrouchStrafeLeft",
							"Idle",
							"WalkBackwards",
							"Walk",
							"StrafeRight",
							"StrafeLeft",
							"Jump",
							"JumpLeft",
							"JumpRight",
							"JumpBackward"
						);
						model.StartClip("CrouchIdle", 2, true);

						player.EnableWalking.Value = false;
						rotationLocked.Value = true;

						footstepTimer.Command.Execute(); // We just landed; play a footstep sound
						Sound.PlayCue(main, "Skill Roll", playerPos);

						model.StartClip("Roll", 5, false);

						bool shouldBuildFloor = !player.IsSupported && player.EnableEnhancedWallRun;

						// If the player is not yet supported, that means they're just about to land.
						// So give them a little speed boost for having such good timing.
						Vector3 velocity = forward * player.MaxSpeed * (player.IsSupported ? 0.75f : 1.25f);
						player.LinearVelocity.Value = new Vector3(velocity.X, player.LinearVelocity.Value.Y, velocity.Z);

						// Crouch
						player.Crouched.Value = true;
						player.AllowUncrouch.Value = false;

						Direction rightDir = floorMap.GetRelativeDirection(right);
						Direction forwardDir = floorMap.GetRelativeDirection(forward);

						Updater rollUpdate = null;
						float rollTime = 0.0f;
						rollUpdate = new Updater
						{
							delegate(float dt)
							{
								rollTime += dt;

								// Stop if we're about to roll off the edge of an instantiated block possibility.
								bool stop = instantiatedBlockPossibility && !shouldBuildFloor && rollTime > 0.1f && Map.GlobalRaycast(transform.Position + forward * 0.5f, Vector3.Down, player.Height * 0.5f + player.SupportHeight + 1.1f).Map != null;

								if (stop || rollTime > 1.0f || Vector3.Dot(player.LinearVelocity, forward) < 0.1f)
								{
									rollUpdate.Delete.Execute();
									player.EnableWalking.Value = true;
									if (!input.GetInput(settings.Roll))
										player.AllowUncrouch.Value = true;
									rotationLocked.Value = false;
									rollEnded = main.TotalTime;
									rolling = false;

									if (stop) // Stop from rolling off the edge
										player.LinearVelocity.Value = new Vector3(0, player.LinearVelocity.Value.Y, 0);
								}
								else
								{
									player.LinearVelocity.Value = new Vector3(velocity.X, player.LinearVelocity.Value.Y, velocity.Z);
									breakWalls(forward, right, false);
									if (shouldBuildFloor)
										buildFloor(floorMap, floorCoordinate, forwardDir, rightDir);
								}
							}
						};
						result.Add(rollUpdate);
						return;
					}
				}
			});

			input.Bind(settings.Roll, PCInput.InputState.Up, delegate()
			{
				if (!rolling)
					player.AllowUncrouch.Value = true;
			});
		}

		public override void AttachEditorComponents(Entity result, Main main)
		{

		}
	}
}
