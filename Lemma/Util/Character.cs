﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BEPUphysics.UpdateableSystems;
using BEPUphysics.Entities.Prefabs;
using Microsoft.Xna.Framework;
using BEPUphysics.CollisionRuleManagement;
using BEPUphysics.Entities;
using BEPUphysics;
using BEPUphysics.BroadPhaseEntries;
using BEPUphysics.BroadPhaseEntries.MobileCollidables;
using Lemma.Components;
using BEPUphysics.CollisionTests;
using BEPUphysics.NarrowPhaseSystems.Pairs;
using System.Xml.Serialization;

namespace Lemma.Util
{
	public class Character : Updateable, IEndOfTimeStepUpdateable
	{
		/// <summary>
		/// A box positioned relative to the character's body used to identify collision pairs with nearby objects that could be possibly stood upon.
		/// </summary>
		private Box collisionPairCollector;

		/// <summary>
		/// The distance above the ground that the bottom of the character's body floats.
		/// </summary>
		public Property<float> SupportHeight = new Property<float>();

		/// <summary>
		/// Rate of increase in the character's speed in the movementDirection.
		/// </summary>
		public Property<float> Acceleration = new Property<float> { Value = 6.0f };

		public Property<float> InitialAcceleration = new Property<float> { Value = 25.0f };

		public const float InitialAccelerationSpeedThreshold = 3.0f;

		/// <summary>
		/// The character's physical representation that handles iteractions with the environment.
		/// </summary>
		[XmlIgnore]
		public Cylinder Body;

		/// <summary>
		/// Whether or not the character is currently standing on anything that can be walked upon.
		/// False if there exists no support or the support is too heavily sloped, otherwise true.
		/// </summary>
		public Property<bool> HasTraction = new Property<bool>();

		/// <summary>
		/// Whether or not the character is currently standing on anything.
		/// </summary>
		public Property<bool> IsSupported = new Property<bool>();

		public Property<bool> IsSwimming = new Property<bool>();

		/// <summary>
		/// Initial vertical speed when jumping.
		/// </summary>
		public Property<float> JumpSpeed = new Property<float>();

		public Property<Player.WallRun> WallRunState = new Property<Player.WallRun> { Value = Player.WallRun.None };

		/// <summary>
		/// The maximum slope under which walking forces can be applied.
		/// </summary>
		public Property<float> MaxSlope = new Property<float> { Value = (float)Math.PI * 0.3f };

		/// <summary>
		/// Maximum speed in the movementDirection that can be attained.
		/// </summary>
		public Property<float> MaxSpeed = new Property<float> { Value = 8 };

		/// <summary>
		/// Normalized direction which the character tries to move.
		/// </summary>
		public Property<Vector2> MovementDirection = new Property<Vector2> { Value = Vector2.Zero };

		/// <summary>
		/// Deceleration applied to oppose horizontal movement when the character does not have a steady foothold on the ground (hasTraction == false).
		/// </summary>
		public Property<float> SlidingDeceleration = new Property<float> { Value = 0.3f };

		/// <summary>
		/// Deceleration applied to oppose uncontrolled horizontal movement when the character has a steady foothold on the ground (hasTraction == true).
		/// </summary>
		public Property<float> TractionDeceleration = new Property<float> { Value = 100.0f };

		public Property<bool> EnableWalking = new Property<bool> { Value = true };

		public Property<bool> Jumping = new Property<bool>();

		/// <summary>
		/// The location of the player's feet.
		/// </summary>
		public Property<Vector3> SupportLocation = new Property<Vector3>();

		/// <summary>
		/// The physics entity the player is currently standing on.
		/// </summary>
		[XmlIgnore]
		public Property<BEPUphysics.Entities.Entity> SupportEntity = new Property<BEPUphysics.Entities.Entity>();

		[XmlIgnore]
		public Command<Collidable, ContactCollection> Collided = new Command<Collidable, ContactCollection>();

		private float defaultCharacterHeight;
		private float defaultSupportHeight;

		private float crouchedCharacterHeight;
		private float crouchedSupportHeight;

		public Property<bool> Crouched = new Property<bool>();
		public Property<bool> AllowUncrouch = new Property<bool>();

		private Vector3[] rayOffsets;

		public static readonly CollisionGroup NoCollideGroup = new CollisionGroup();
		public static readonly CollisionGroup CharacterGroup = new CollisionGroup();

		static Character()
		{
			CollisionRules.CollisionGroupRules.Add(new CollisionGroupPair(Character.NoCollideGroup, Character.CharacterGroup), CollisionRule.NoBroadPhase);
		}

		private Main main;

		/// <summary>
		/// Constructs a simple character controller.
		/// </summary>
		/// <param name="position">Location to initially place the character.</param>
		/// <param name="characterHeight">The height of the character.</param>
		/// <param name="characterWidth">The diameter of the character.</param>
		/// <param name="supportHeight">The distance above the ground that the bottom of the character's body floats.</param>
		/// <param name="mass">Total mass of the character.</param>
		public Character(Main main, Vector3 position, float characterHeight, float crouchedHeight, float characterWidth, float supportHeight, float crouchedSupportHeight, float mass)
		{
			this.main = main;
			this.Body = new Cylinder(position, characterHeight, characterWidth / 2, mass);
			this.Body.IgnoreShapeChanges = true;
			this.Body.LinearDamping = 0.0f;
			this.Body.CollisionInformation.CollisionRules.Group = Character.CharacterGroup;
			this.defaultCharacterHeight = characterHeight;
			this.crouchedCharacterHeight = crouchedHeight;
			this.Body.CollisionInformation.Events.ContactCreated += new BEPUphysics.BroadPhaseEntries.Events.ContactCreatedEventHandler<EntityCollidable>(Events_ContactCreated);
			this.collisionPairCollector = new Box(position + new Vector3(0, (characterHeight * -0.5f) - supportHeight, 0), characterWidth, supportHeight * 2, characterWidth, 1);
			this.collisionPairCollector.CollisionInformation.CollisionRules.Personal = CollisionRule.NoNarrowPhaseUpdate; //Prevents collision detection/contact generation from being run.
			this.collisionPairCollector.IsAffectedByGravity = false;
			this.collisionPairCollector.CollisionInformation.CollisionRules.Group = Character.CharacterGroup;
			CollisionRules.AddRule(this.collisionPairCollector, this.Body, CollisionRule.NoBroadPhase); //Prevents the creation of any collision pairs between the body and the collector.
			this.SupportHeight.Value = supportHeight;
			this.defaultSupportHeight = supportHeight;
			this.crouchedSupportHeight = crouchedSupportHeight;

			this.Body.LocalInertiaTensorInverse = new BEPUutilities.Matrix3x3();
			this.collisionPairCollector.LocalInertiaTensorInverse = new BEPUutilities.Matrix3x3();

			//Make the body slippery.
			//Note that this will not make all collisions have zero friction;
			//the friction coefficient between a pair of objects is based
			//on a blending of the two objects' materials.
			this.Body.Material.KineticFriction = 0.0f;
			this.Body.Material.StaticFriction = 0.0f;
			this.Body.Material.Bounciness = 0.0f;

			this.Crouched.Set = delegate(bool value)
			{
				bool oldValue = this.Crouched.InternalValue;
				if (value && !oldValue)
				{
					this.Body.Position += new Vector3(0, (this.crouchedSupportHeight - this.defaultSupportHeight) + 0.5f * (this.crouchedCharacterHeight - this.defaultCharacterHeight), 0);
					this.Body.Height = this.crouchedCharacterHeight;
					this.SupportHeight.Value = this.crouchedSupportHeight;
				}
				else if (!value && oldValue)
				{
					this.Body.Height = this.defaultCharacterHeight;
					this.Body.Position += new Vector3(0, (this.defaultSupportHeight - this.crouchedSupportHeight) + 0.5f * (this.defaultCharacterHeight - this.crouchedCharacterHeight), 0);
					this.SupportHeight.Value = this.defaultSupportHeight;
				}
				this.collisionPairCollector.Height = this.SupportHeight * 2;
				this.Crouched.InternalValue = value;
			};

			const int rayChecks = 4;
			float radius = this.Body.Radius - 0.1f;
			this.rayOffsets = new[] { Vector3.Zero }.Concat(Enumerable.Range(0, rayChecks).Select(
			delegate(int x)
			{
				float angle = x * ((2.0f * (float)Math.PI) / (float)rayChecks);
				return new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);
			})).ToArray();
		}

		private void Events_ContactCreated(EntityCollidable sender, Collidable other, BEPUphysics.NarrowPhaseSystems.Pairs.CollidablePairHandler pair, BEPUphysics.CollisionTests.ContactData contact)
		{
			this.Collided.Execute(other, pair.Contacts);
		}

		/// <summary>
		/// Handles the updating of the character.  Called by the owning space object when necessary.
		/// </summary>
		/// <param name="dt">Simulation seconds since the last update.</param>
		void IEndOfTimeStepUpdateable.Update(float dt)
		{
			BEPUphysics.Entities.Entity supportEntity;
			object supportEntityTag;
			Vector3 supportLocation, supportNormal;
			float supportDistance;

			bool foundSupport = this.findSupport(out supportEntityTag, out supportEntity, out supportLocation, out supportNormal, out supportDistance);

			// Support location only has velocity if we're actually sitting on an entity, as opposed to some static geometry.
			// linear velocity of point on body relative to center
			Vector3 supportLocationVelocity;
			if (supportEntity != null)
				supportLocationVelocity = supportEntity.LinearVelocity + //linear component
											Vector3.Cross(supportEntity.AngularVelocity, supportLocation - supportEntity.Position);
			else
				supportLocationVelocity = new Vector3();

			if (supportLocationVelocity.Y < this.Body.LinearVelocity.Y - 5.0f)
				foundSupport = false;

			if (!this.IsSwimming && foundSupport)
			{
				this.SupportEntity.Value = supportEntity;
				this.SupportLocation.Value = supportLocation;
				this.IsSupported.Value = true;
				this.support(supportLocationVelocity, supportNormal, supportDistance, dt);
				this.HasTraction.Value = this.isSupportSlopeWalkable(supportNormal);
				this.handleHorizontalMotion(supportLocationVelocity, supportNormal, dt);
			}
			else
			{
				this.SupportEntity.Value = null;
				this.IsSupported.Value = false;
				this.HasTraction.Value = false;
				if (this.EnableWalking)
				{
					if (this.IsSwimming)
						this.handleNoTraction(dt, 0.5f, 0.5f, 0.5f);
					else
						this.handleNoTraction(dt, 0.0f, 0.35f, -1.0f); // -1.0 = Infinite max speed
				}
			}

			if (this.Crouched && this.AllowUncrouch)
			{
				// Try to uncrouch

				Vector3 rayOrigin = this.Body.Position;
				rayOrigin.Y += 0.01f + this.Body.Height * 0.5f;

				bool foundCeiling = false;

				foreach (Vector3 rayStart in this.rayOffsets.Select(x => x + rayOrigin))
				{
					RayCastResult rayHit;
					//Fire a ray at the candidate and determine some details! 
					if (this.main.Space.RayCast(new Ray(rayStart, Vector3.Up), (this.defaultCharacterHeight - this.Body.Height) + (this.defaultSupportHeight - this.SupportHeight), out rayHit))
					{
						foundCeiling = true;
						break;
					}
				}

				if (!foundCeiling)
					this.Crouched.Value = false;
			}
			else if (!this.Crouched && this.IsSupported)
			{
				Vector3 pos = this.Body.Position;
				Vector2 offset = new Vector2(supportLocation.X - pos.X, supportLocation.Z - pos.Z);
				if (offset.LengthSquared() > 0)
				{
					RayCastResult rayHit;
					Vector3 rayStart = supportLocation;
					rayStart.Y = pos.Y + (this.Body.Height * 0.5f) - 1.0f;
					if (this.main.Space.RayCast(new Ray(rayStart, Vector3.Up), 1.0f, x => x.CollisionRules.Group != Character.CharacterGroup && x.CollisionRules.Group != Character.NoCollideGroup, out rayHit))
					{
						offset.Normalize();
						Vector2 velocity = new Vector2(this.Body.LinearVelocity.X, this.Body.LinearVelocity.Z);
						float speed = Vector2.Dot(velocity, offset);
						if (speed > 0)
						{
							velocity -= offset * speed * 1.5f;
							this.Body.LinearVelocity = new Vector3(velocity.X, this.Body.LinearVelocity.Y, velocity.Y);
						}
					}
				}
			}

			this.collisionPairCollector.LinearVelocity = this.Body.LinearVelocity;
			this.collisionPairCollector.Position = this.Body.Position + new Vector3(0, (this.Body.Height * -0.5f) - this.SupportHeight, 0);
		}

		/// <summary>
		/// Locates the closest support entity by performing a raycast at collected candidates.
		/// </summary>
		/// <param name="supportEntity">The closest supporting entity.</param>
		/// <param name="supportLocation">The support location where the ray hit the entity.</param>
		/// <param name="supportNormal">The normal at the surface where the ray hit the entity.</param>
		/// <param name="supportDistance">Distance from the character to the support location.</param>
		/// <returns>Whether or not a support was located.</returns>
		private bool findSupport(out object supportEntityTag, out BEPUphysics.Entities.Entity supportEntity, out Vector3 supportLocation, out Vector3 supportNormal, out float supportDistance)
		{
			supportEntity = null;
			supportEntityTag = null;
			supportLocation = BEPUutilities.Toolbox.NoVector;
			supportNormal = BEPUutilities.Toolbox.NoVector;
			supportDistance = float.MaxValue;

			const float fudgeFactor = 0.1f;
			Vector3 rayOrigin = this.Body.Position;
			rayOrigin.Y += fudgeFactor + this.Body.Height * -0.5f;

			for (int i = 0; i < this.collisionPairCollector.CollisionInformation.Pairs.Count; i++)
			{
				var pair = this.collisionPairCollector.CollisionInformation.Pairs[i];
				//Determine which member of the collision pair is the possible support.
				Collidable candidate = (pair.BroadPhaseOverlap.EntryA == collisionPairCollector.CollisionInformation ? pair.BroadPhaseOverlap.EntryB : pair.BroadPhaseOverlap.EntryA) as Collidable;
				//Ensure that the candidate is a valid supporting entity.
				if (candidate.CollisionRules.Personal >= CollisionRule.NoSolver)
					continue; //It is invalid!

				if (candidate.CollisionRules.Group == Character.NoCollideGroup)
					continue;

				//The maximum length is supportHeight * 2 instead of supportHeight alone because the character should be able to step downwards.
				//This acts like a sort of 'glue' to help the character stick on the ground in general.
				float maximumDistance;
				//The 'glue' effect should only occur if the character has a solid hold on the ground though.
				//Otherwise, the character is falling or sliding around uncontrollably.
				if (this.HasTraction && !this.IsSwimming)
					maximumDistance = fudgeFactor + (this.SupportHeight * 2.0f);
				else
					maximumDistance = fudgeFactor + this.SupportHeight;

				foreach (Vector3 rayStart in this.rayOffsets.Select(x => x + rayOrigin))
				{
					BEPUutilities.RayHit rayHit;
					// Fire a ray at the candidate and determine some details!
					if (candidate.RayCast(new Ray(rayStart, Vector3.Down), maximumDistance, out rayHit))
					{
						Vector3 n = Vector3.Normalize(rayHit.Normal);

						if (n.Y > supportNormal.Y)
							supportNormal = n;

						// We want to find the closest support, so compare it against the last closest support.
						if (rayHit.T < supportDistance && n.Y > 0.25f)
						{
							supportDistance = rayHit.T - fudgeFactor;
							supportLocation = rayHit.Location;
							if (rayHit.T < 0.0f)
								supportNormal = Vector3.Up;

							var entityInfo = candidate as EntityCollidable;
							if (entityInfo != null)
							{
								supportEntity = entityInfo.Entity;
								supportEntityTag = supportEntity != null ? supportEntity.Tag : candidate.Tag;
							}
							else
								supportEntityTag = candidate.Tag;
						}
					}
				}
			}

			bool isSupported = supportDistance < float.MaxValue;

			if (!isSupported && this.WallRunState.Value == Player.WallRun.None)
			{
				foreach (Contact contact in this.Body.CollisionInformation.Pairs.SelectMany(x => x.Contacts.Select(y => y.Contact)))
				{
					Vector3 normal = (contact.Position - this.Body.Position).SetComponent(Direction.PositiveY, 0);
					float length = normal.Length();
					if (length > 0.5f)
						this.Body.LinearVelocity += -0.1f * (normal / length);
				}
			}
			return isSupported;
		}

		/// <summary>
		/// Determines if the ground supporting the character is sloped gently enough to allow for normal walking.
		/// </summary>
		/// <param name="supportNormal">Normal of the surface being stood upon.</param>
		/// <returns>Whether or not the slope is walkable.</returns>
		private bool isSupportSlopeWalkable(Vector3 supportNormal)
		{
			//The following operation is equivalent to performing a dot product between the support normal and Vector3.Down and finding the angle it represents using Acos.
			return Math.Acos(Math.Abs(Math.Min(supportNormal.Y, 1))) <= this.MaxSlope;
		}

		/// <summary>
		/// Maintains the position of the character's body above the ground.
		/// </summary>
		/// <param name="supportLocationVelocity">Velocity of the support point connected to the supportEntity.</param>
		/// <param name="supportNormal">The normal at the surface where the ray hit the entity.</param>
		/// <param name="supportDistance">Distance from the character to the support location.</param>
		private void support(Vector3 supportLocationVelocity, Vector3 supportNormal, float supportDistance, float dt)
		{
			//Put the character at the right distance from the ground.
			float supportVerticalVelocity = Math.Max(supportLocationVelocity.Y, -0.1f);
			float heightDifference = this.SupportHeight - supportDistance;
			this.Body.Position += (new Vector3(0, MathHelper.Clamp(heightDifference, (supportVerticalVelocity - 10.0f) * dt, (supportVerticalVelocity + 10.0f) * dt), 0));

			//Remove from the character velocity which would push it toward or away from the surface.
			//This is a relative velocity, so the velocity of the body and the velocity of a point on the support entity must be found.
			float bodyNormalVelocity = Vector3.Dot(this.Body.LinearVelocity, supportNormal);
			float supportEntityNormalVelocity = Vector3.Dot(supportLocationVelocity, supportNormal);
			Vector3 diff = (bodyNormalVelocity - supportEntityNormalVelocity) * -supportNormal;
			diff.Y = Math.Max(diff.Y, 0);
			this.Body.LinearVelocity += diff;

			BEPUphysics.Entities.Entity supportEntity = this.SupportEntity;
			if (supportEntity != null && supportEntity.IsAffectedByGravity)
			{
				Vector3 supportLocation = this.SupportLocation;
				Vector3 impulse = (this.Body.Mass * 1.5f) * ((Space)this.Space).ForceUpdater.Gravity * dt;
				supportEntity.ApplyImpulse(ref supportLocation, ref impulse);
				supportEntity.ActivityInformation.Activate();
			}
		}

		/// <summary>
		/// Manages movement acceleration, deceleration, and sliding.
		/// </summary>
		/// <param name="supportLocationVelocity">Velocity of the support point connected to the supportEntity.</param>
		/// <param name="supportNormal">The normal at the surface where the ray hit the entity.</param>
		/// <param name="dt">Timestep of the simulation.</param>
		private void handleHorizontalMotion(Vector3 supportLocationVelocity, Vector3 supportNormal, float dt)
		{
			if (this.HasTraction && this.MovementDirection != Vector2.Zero && this.EnableWalking)
			{
				// Identify a coordinate system that uses the support normal as Y.
				// X is the axis point along the left (negative) and right (positive) relative to the movement direction.
				// Z points forward (positive) and backward (negative) in the movement direction modified to be parallel to the surface.
				Vector3 horizontal = new Vector3(this.MovementDirection.Value.X, 0, this.MovementDirection.Value.Y);
				horizontal.Normalize();
				Vector3 x = Vector3.Cross(horizontal, supportNormal);
				x.Normalize();
				Vector3 z = Vector3.Cross(supportNormal, x);
				z.Normalize();

				// Remove from the character a portion of velocity which pushes it horizontally off the desired movement track defined by the movementDirection.
				float bodyXVelocity = Vector3.Dot(this.Body.LinearVelocity, x);
				float supportEntityXVelocity = Vector3.Dot(supportLocationVelocity, x);
				float velocityChange = MathHelper.Clamp(bodyXVelocity - supportEntityXVelocity, -dt * this.TractionDeceleration, dt * this.TractionDeceleration);
				this.Body.LinearVelocity -= velocityChange * x;

				float bodyZVelocity = Vector3.Dot(this.Body.LinearVelocity, z);
				float supportEntityZVelocity = Vector3.Dot(supportLocationVelocity, z);
				float netZVelocity = bodyZVelocity - supportEntityZVelocity;
				// The velocity difference along the Z axis should accelerate/decelerate to match the goal velocity (max speed).
				float speed = this.Crouched ? this.MaxSpeed * 0.5f : this.MaxSpeed;
				if (netZVelocity > speed)
				{
					// Decelerate
					velocityChange = Math.Min(dt * this.TractionDeceleration, netZVelocity - speed);
					this.Body.LinearVelocity -= velocityChange * z;
				}
				else
				{
					// Accelerate
					float accel = netZVelocity < Character.InitialAccelerationSpeedThreshold ? this.InitialAcceleration : this.Acceleration;
					velocityChange = Math.Min(dt * accel, speed - netZVelocity);
					this.Body.LinearVelocity += velocityChange * z;
					if (z.Y > 0.0f)
						this.Body.LinearVelocity += new Vector3(0, z.Y * Math.Min(dt * this.Acceleration * 2.0f, speed - netZVelocity) * 2.0f, 0);
				}
			}
			else
			{
				float deceleration;
				if (this.HasTraction)
					deceleration = dt * this.TractionDeceleration;
				else
					deceleration = dt * this.SlidingDeceleration;
				//Remove from the character a portion of velocity defined by the deceleration.
				Vector3 bodyHorizontalVelocity = this.Body.LinearVelocity - Vector3.Dot(this.Body.LinearVelocity, supportNormal) * supportNormal;
				Vector3 supportHorizontalVelocity = supportLocationVelocity - Vector3.Dot(supportLocationVelocity, supportNormal) * supportNormal;
				Vector3 relativeVelocity = bodyHorizontalVelocity - supportHorizontalVelocity;
				float speed = relativeVelocity.Length();
				if (speed > 0)
				{
					Vector3 horizontalDirection = relativeVelocity / speed;
					float velocityChange = Math.Min(speed, deceleration);
					this.Body.LinearVelocity -= velocityChange * horizontalDirection;
				}
			}
		}

		private void handleNoTraction(float dt, float tractionDecelerationRatio, float accelerationRatio, float speedRatio)
		{
			float tractionDeceleration = this.TractionDeceleration * tractionDecelerationRatio;
			float acceleration = this.Acceleration * accelerationRatio;
			float maxSpeed = this.MaxSpeed * speedRatio;

			if (this.Jumping && !this.IsSwimming)
				this.Body.LinearVelocity += new Vector3(0, dt * this.JumpSpeed * accelerationRatio * 0.7f, 0);
			
			if (this.MovementDirection != Vector2.Zero)
			{
				//Identify a coordinate system that uses the support normal as Y.
				//X is the axis point along the left (negative) and right (positive) relative to the movement direction.
				//Z points forward (positive) and backward (negative) in the movement direction modified to be parallel to the surface.
				Vector3 horizontal = new Vector3(this.MovementDirection.Value.X, 0, this.MovementDirection.Value.Y);
				horizontal.Normalize();
				Vector3 x = Vector3.Cross(horizontal, Vector3.Up);
				Vector3 z = Vector3.Cross(Vector3.Up, x);

				//Remove from the character a portion of velocity which pushes it horizontally off the desired movement track defined by the movementDirection.
				float bodyXVelocity = Vector3.Dot(this.Body.LinearVelocity, x);
				float velocityChange = MathHelper.Clamp(bodyXVelocity, -dt * tractionDeceleration, dt * tractionDeceleration);
				this.Body.LinearVelocity -= velocityChange * x;

				float bodyZVelocity = Vector3.Dot(Body.LinearVelocity, z);
				//The velocity difference along the Z axis should accelerate/decelerate to match the goal velocity (max speed).
				if (maxSpeed > 0 && bodyZVelocity > maxSpeed)
				{
					//Decelerate
					velocityChange = Math.Min(dt * tractionDeceleration, bodyZVelocity - maxSpeed);
					this.Body.LinearVelocity -= velocityChange * z;
				}
				else
				{
					//Accelerate
					if (maxSpeed < -1.0f)
						maxSpeed = this.MaxSpeed;
					velocityChange = Math.Min(dt * acceleration, maxSpeed - bodyZVelocity);
					this.Body.LinearVelocity += velocityChange * z;
				}
			}
			else
			{
				float deceleration = dt * tractionDeceleration;
				//Remove from the character a portion of velocity defined by the deceleration.
				Vector3 bodyHorizontalVelocity = this.Body.LinearVelocity;
				bodyHorizontalVelocity.Y = 0.0f;
				float speed = bodyHorizontalVelocity.Length();
				if (speed > 0)
				{
					Vector3 horizontalDirection = bodyHorizontalVelocity / speed;
					float velocityChange = Math.Min(speed, deceleration);
					this.Body.LinearVelocity -= velocityChange * horizontalDirection;
				}
			}
		}

		/// <summary>
		/// Activates the character, adding its components to the space. 
		/// </summary>
		public void Activate()
		{
			if (!this.IsUpdating)
			{
				this.IsUpdating = true;
				if (this.Body.Space == null)
				{
					this.Space.Add(this.Body);
					this.Space.Add(this.collisionPairCollector);
				}
				this.HasTraction.Value = false;
				this.IsSupported.Value = false;
				this.Body.LinearVelocity = Vector3.Zero;
			}
		}

		/// <summary>
		/// Deactivates the character, removing its components from the space.
		/// </summary>
		public void Deactivate()
		{
			if (this.IsUpdating)
			{
				this.IsUpdating = false;
				this.Body.Position = new Vector3(10000, 0, 0);
				if (this.Body.Space != null)
				{
					this.Body.Space.Remove(this.Body);
					this.collisionPairCollector.Space.Remove(this.collisionPairCollector);
				}
			}
		}

		/// <summary>
		/// Called by the engine when the character is added to the space.
		/// Activates the character.
		/// </summary>
		/// <param name="newSpace">Space to which the character was added.</param>
		public override void OnAdditionToSpace(ISpace newSpace)
		{
			base.OnAdditionToSpace(newSpace); //sets this object's space to the newSpace.
			this.Activate();
		}

		/// <summary>
		/// Called by the engine when the character is removed from the space.
		/// Deactivates the character.
		/// </summary>
		public override void OnRemovalFromSpace(ISpace oldSpace)
		{
			this.Deactivate();
			base.OnRemovalFromSpace(oldSpace); //Sets this object's space to null.
		}
	}
}
