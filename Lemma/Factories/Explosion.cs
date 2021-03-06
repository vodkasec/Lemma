﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lemma.Components;
using Microsoft.Xna.Framework;
using Lemma.Util;

namespace Lemma.Factories
{
	public static class Explosion
	{
		public static void Explode(Main main, Map map, Map.Coordinate coord, int radius = 8, float physicsRadius = 12.0f)
		{
			// Kaboom
			Vector3 pos = map.GetAbsolutePosition(coord);
			Sound.PlayCue(main, "Explosion", pos, 1.0f, 0.0f);

			Entity lightEntity = Factory.Get<PointLightFactory>().CreateAndBind(main);
			lightEntity.Serialize = false;
			PointLight light = lightEntity.Get<PointLight>();
			light.Color.Value = new Vector3(1.3f, 1.1f, 0.9f);
			light.Attenuation.Value = 20.0f;
			light.Position.Value = pos;
			lightEntity.Add(new Animation
			(
				new Animation.FloatMoveTo(light.Attenuation, 0.0f, 1.0f),
				new Animation.Execute(light.Delete)
			));
			main.Add(lightEntity);

			Entity player = PlayerFactory.Instance;
			if (player != null && player.Active)
				player.GetCommand<Vector3, float>("ShakeCamera").Execute(pos, 50.0f);
		
			Random random = new Random();
		
			const float physicsImpulse = 70.0f;
			const float minPlayerDamage = 0.2f;
			const float playerDamageMultiplier = 2.0f;
		
			// Remove the cells
			BlockFactory blockFactory = Factory.Get<BlockFactory>();
			
			foreach (Map m in Map.ActiveMaps.ToList())
			{
				List<Map.Coordinate> removals = new List<Map.Coordinate>();
			
				Map.Coordinate c = m.GetCoordinate(pos);
				Vector3 relativePos = m.GetRelativePosition(c);
				
				Quaternion quat = m.Entity.Get<Transform>().Quaternion;
			
				for (Map.Coordinate x = c.Move(Direction.NegativeX, radius - 1); x.X < c.X + radius; x.X++)
				{
					for (Map.Coordinate y = x.Move(Direction.NegativeY, radius - 1); y.Y < c.Y + radius; y.Y++)
					{
						for (Map.Coordinate z = y.Move(Direction.NegativeZ, radius - 1); z.Z < c.Z + radius; z.Z++)
						{
							if (m == map && z.Equivalent(coord))
								continue;
						
							Map.CellState s = m[z];
							if (s.ID == 0 || s.Permanent)
								continue;
							
							Vector3 cellPos = m.GetRelativePosition(z);
							if ((cellPos - relativePos).Length() < radius - 1)
							{
								removals.Add(z);
								if (random.NextDouble() > 0.5)
								{
									Entity block = blockFactory.CreateAndBind(main);
									Transform blockTransform = block.Get<Transform>();
									blockTransform.Position.Value = m.GetAbsolutePosition(cellPos);
									blockTransform.Quaternion.Value = quat;
									s.ApplyToBlock(block);
									main.Add(block);
								}
							}
						}
					}
				}
				if (removals.Count > 0)
				{
					m.Empty(removals);
					m.Regenerate();
				}
			}
		
			// Damage the player
			if (player != null && player.Active)
			{
				float d = (player.Get<Transform>().Position - pos).Length();
				if (d < physicsRadius)
					player.Get<Player>().Health.Value -= minPlayerDamage + (1.0f - (d / physicsRadius)) * playerDamageMultiplier;
			}
		
			// Apply impulse to dynamic maps
			foreach (Map m in Map.ActiveMaps)
			{
				DynamicMap dm = m as DynamicMap;
				if (dm == null)
					continue;
			
				Vector3 toMap = dm.Transform.Value.Translation - pos;
				float distanceToMap = toMap.Length();
				toMap /= distanceToMap;
			
				toMap *= Math.Max(0.0f, 1.0f - (distanceToMap / physicsRadius)) * dm.PhysicsEntity.Mass * physicsImpulse;
			
				dm.PhysicsEntity.ApplyImpulse(dm.Transform.Value.Translation + new Vector3(((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f), toMap);
			}
		
			// Apply impulse to physics blocks
			foreach (Entity b in main.Get("Block"))
			{
				PhysicsBlock block = b.Get<PhysicsBlock>();
				Vector3 fromExplosion = b.Get<Transform>().Position.Value - pos;
				float distance = fromExplosion.Length();
				if (distance > 0.0f && distance < physicsRadius)
				{
					float blend = 1.0f - (distance / physicsRadius);
					block.LinearVelocity.Value += fromExplosion * blend * 10.0f / distance;
					block.AngularVelocity.Value += new Vector3(((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f, ((float)random.NextDouble() - 0.5f) * 2.0f) * blend;
				}
			}
		}
	}
}
