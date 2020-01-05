using System;
using System.Linq;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Library;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using Sandbox.ModAPI.Ingame;
using Sandbox.Common;
using Sandbox.Game;
using VRage.Collections;
using VRage.Game.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{



		public const string TARGETING_SYSTEM_GROUP = "TargetingSystem";
		public List<IMyCameraBlock> cameras;
		IMyTextPanel InfoDisplay;
		IMyTextPanel GPSLogDisplay;
		int camIndex = 0;
		double RANGE = 5000;
		float PITCH = 0;
		float YAW = 0;
		MyDetectedEntityInfo lastDetected;
		List<long> detectedEntityList = new List<long>();
		List<IMyLargeTurretBase> turrets;
		int turretIndex = 0;
		int maxTurretPerScan = 2;
		IMyShipController shipcontroller;

		int scriptPhaze = 0; //0 - scan, 1 - target, 2 - shoot, 3 - display, 4 - display gps;

		float raycastAreaSize = 0.1f; //0 - 1 cannnot be greater than 1 less- faster detecting but requires more accurate targeting
		float raycastConeLimit;

		int raycastTickSkip;

		Random random = new Random(DateTime.Now.Second);
		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.None;

			IMyBlockGroup targetingSystemGroup = GridTerminalSystem.GetBlockGroupWithName(TARGETING_SYSTEM_GROUP);
			if (targetingSystemGroup == null)
			{
				throw new Exception($"No {TARGETING_SYSTEM_GROUP} group defined.");
			}

			Echo("Configure cameras...");
			cameras = new List<IMyCameraBlock>();
			targetingSystemGroup.GetBlocksOfType(cameras);
			if (cameras.Count == 0)
			{
				throw new Exception("No camera found in group.");
			}
			//Enable raycast for cameras
			foreach (var camera in cameras)
			{
				camera.EnableRaycast = true;
				camera.ApplyAction(Actions.TURN_ON);
			}
			raycastConeLimit = cameras[0].RaycastConeLimit;
			if (RANGE > cameras[0].RaycastDistanceLimit)
			{
				//If camera scan range exeeds server setting limit it to server raycast distance limit.
				RANGE = cameras[0].RaycastDistanceLimit;
			}
			Echo($"Cameras {cameras.Count} OK at range {RANGE}");

			Echo("Configure displays...");
			var displays = new List<IMyTextPanel>();
			targetingSystemGroup.GetBlocksOfType(displays);
			displays = displays.OrderBy(x => x.DisplayNameText).ToList();
			if (displays.Count == 0)
			{
				Echo("No text pannel found in group.");
			}
			if (displays.Count > 0)
			{
				InfoDisplay = displays[0];
				InfoDisplay.ContentType = ContentType.TEXT_AND_IMAGE;
				InfoDisplay.Font = "DEBUG";
				InfoDisplay.FontSize = 0.9f;
				InfoDisplay.ApplyAction(Actions.TURN_ON);

				Echo($"Display {InfoDisplay.DisplayNameText} OK");
			}
			if (displays.Count > 1)
			{
				GPSLogDisplay = displays[1];
				GPSLogDisplay.ContentType = ContentType.TEXT_AND_IMAGE;
				GPSLogDisplay.Font = "DEBUG";
				GPSLogDisplay.FontSize = 0.9f;
				GPSLogDisplay.WriteText(string.Empty);
				GPSLogDisplay.ApplyAction(Actions.TURN_ON);

				Echo($"Display GPSLog {GPSLogDisplay.DisplayNameText} OK");
			}

			Echo("Configure turrets...");
			turrets = new List<IMyLargeTurretBase>();
			targetingSystemGroup.GetBlocksOfType(turrets);
			foreach (var turret in turrets)
			{
				turret.ApplyAction(Actions.TURN_ON);
			}
			Echo($"Turrets {turrets.Count} OK.");

			Echo("Configure ShipController...");
			var cockpits = new List<IMyShipController>();
			targetingSystemGroup.GetBlocksOfType(cockpits);
			if (cockpits.Count == 0)
			{
				throw new Exception("No ship controller found in group.");
			}
			shipcontroller = cockpits[0];

			Echo("Configuration OK.");
		}

		public void Save()
		{

		}

		public void Main(string argument, UpdateType updateSource)
		{
			if (updateSource == UpdateType.Update10 || updateSource == UpdateType.Update1)
			{
				if (raycastTickSkip > 0)
				{
					raycastTickSkip--;
					if (scriptPhaze == 0)
					{
						scriptPhaze = 6;
					}
				}

				if (scriptPhaze == 0)
				{
					if (cameras[camIndex].CanScan(RANGE))
					{
						raycastTickSkip = 5;
						var info = cameras[camIndex].Raycast(RANGE, PITCH, YAW);

						//Echo($"E:{info.EntityId}");

						if (!info.IsEmpty())
						{
							//Echo($"C: {camIndex}");
							lastDetected = info;
							scriptPhaze = 1;

						}
					}

					camIndex++;
					if (camIndex >= cameras.Count)
					{
						camIndex = 0;
						PITCH = 0;
						YAW = 0;
					}
				}
				else if (scriptPhaze == 1)
				{
					//TargetTurret
					if (lastDetected.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
					{
						foreach (var turret in turrets)
						{
							var targetVector = CalculateTargetVector(lastDetected, turret);
							//split turret fire on grid size
							//targetVector = targetVector + (turret.Position - cockpit.Position) * info.BoundingBox.Size.Length() / 100;
							//turret.TrackTarget(targetVector, info.Velocity);    broken crash client
							turret.SetTarget(targetVector);
						}
					}

					scriptPhaze = 2;
				}
				else if (scriptPhaze == 2)
				{
					if (lastDetected.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
					{
						for (int i = 0; i < maxTurretPerScan; i++)
						{
							//Shoot
							if (turrets[turretIndex].IsAimed)
							{
								turrets[turretIndex].ApplyAction(Actions.SHOOT_ONCE);
							}

							turretIndex++;
							if (turretIndex >= turrets.Count)
							{
								turretIndex = 0;
							}
						}
					}

					scriptPhaze = 3;

				}
				else if (scriptPhaze == 3)
				{
					#region Display
					Color color;
					switch (lastDetected.Relationship)
					{
						case MyRelationsBetweenPlayerAndBlock.Owner:
						case MyRelationsBetweenPlayerAndBlock.Friends:
						case MyRelationsBetweenPlayerAndBlock.FactionShare:
							color = Color.Green;
							break;
						case MyRelationsBetweenPlayerAndBlock.Neutral:
						case MyRelationsBetweenPlayerAndBlock.NoOwnership:
							color = Color.Yellow;
							break;
						case MyRelationsBetweenPlayerAndBlock.Enemies:
							color = Color.Red;
							break;
						default:
							color = Color.White;
							break;
					}
					InfoDisplay.FontColor = color;

					var sbdisplay = new StringBuilder();

					sbdisplay.AppendLine();
					sbdisplay.AppendLine($"Time: {DateTime.Now.ToString()}");
					sbdisplay.AppendLine($"Distance: {(Vector3D.Distance(cameras[camIndex].GetPosition(), lastDetected.HitPosition.Value)):0.00}m");
					sbdisplay.AppendLine($"Name: {lastDetected.Name}");
					sbdisplay.Append($"Type: {lastDetected.Type} ");
					sbdisplay.AppendLine($"Relation: {lastDetected.Relationship} ");
					sbdisplay.AppendLine($"Speed: {lastDetected.Velocity.Length().ToString("0.0")}m/s");

					InfoDisplay.WriteText(sbdisplay.ToString());


					#endregion Display

					scriptPhaze = 4;
				}
				else if (scriptPhaze == 4)
				{
					//LCD GPS log

					if (!detectedEntityList.Contains(lastDetected.EntityId))
					{
						detectedEntityList.Add(lastDetected.EntityId);

						var sbdisplay = new StringBuilder();

						//sbdisplay.AppendLine();
						//sbdisplay.AppendLine($"Time: {DateTime.Now.ToString()}");
						//sbdisplay.AppendLine($"Target found at {(Vector3D.Distance(cameras[camIndex].GetPosition(), lastDetected.HitPosition.Value)):0.00}m range");
						//String size = lastDetected.BoundingBox.Size.ToString("0.000");
						//String printSize = string.Empty;
						//double volume = 1;
						//String unitName = "m3";
						//if (size != null || size != string.Empty)
						//{
						//	size = size.Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty);
						//	size = size.Trim();
						//	String[] sizes = size.Split(':');
						//	double factor = 1;
						//	if (lastDetected.Type == MyDetectedEntityType.SmallGrid)
						//	{
						//		factor = 4;
						//		unitName = "blocks";
						//	}
						//	else if (lastDetected.Type == MyDetectedEntityType.LargeGrid)
						//	{
						//		factor = 16;
						//		unitName = "blocks";
						//	}
						//	foreach (String measure in sizes)
						//	{
						//		if (measure == string.Empty)
						//		{
						//			//first value
						//			continue;
						//		}
						//		volume = volume * Math.Round(Convert.ToDouble(measure), 0);
						//		printSize += $"{Convert.ToDouble(measure):0.00} X ";
						//	}
						//	volume = volume / factor;
						//}
						//printSize = printSize.Remove(printSize.Length - 2);
						//printSize += "m";
						//sbdisplay.AppendLine($"Name: {lastDetected.Name}");
						//sbdisplay.AppendLine($"Type: {lastDetected.Type}");
						//sbdisplay.AppendLine($"Velocity: {lastDetected.Velocity.ToString("0.000")}");
						//sbdisplay.AppendLine($"Relationship: {lastDetected.Relationship}");
						//sbdisplay.AppendLine($"Size: {printSize}");
						//sbdisplay.AppendLine($"Estimated volume: {volume} {unitName}");

						sbdisplay.AppendLine($"GPS:{lastDetected.Name}:{lastDetected.Position.X.ToString("0.00")}:{lastDetected.Position.Y.ToString("0.00")}:{lastDetected.Position.Z.ToString("0.00")}:");

						GPSLogDisplay.WriteText(sbdisplay.ToString(), true);
					}

					scriptPhaze = 5;
				}
				else if (scriptPhaze == 5)
				{
					PITCH = RandomizePitchYaw(raycastConeLimit);
					YAW = RandomizePitchYaw(raycastConeLimit);
					scriptPhaze = 0;
				}
				else if (scriptPhaze == 6)
				{
					//skip
					scriptPhaze = 0;
				}
			}
			else
			{
				if (argument.ToLower().Contains("stop"))
				{
					Echo("UnAim");
					foreach (var turret in turrets)
					{
						turret.ResetTargetingToDefault();
						Runtime.UpdateFrequency = UpdateFrequency.None;
					}

					scriptPhaze = 0;
					return;
				}
				else
				{

				}
			}

			if (Runtime.LastRunTimeMs > 2)
			{
				Runtime.UpdateFrequency = UpdateFrequency.Update100;
				raycastTickSkip = 10;
			}
			else
			{
				Runtime.UpdateFrequency = UpdateFrequency.Update1;
			}
			Echo($"I:{Runtime.CurrentInstructionCount}T:{Runtime.LastRunTimeMs.ToString("0.000")}");
		}

		private Vector3D CalculateTargetVector(MyDetectedEntityInfo info, IMyLargeTurretBase turret, double ammoSpeed = 1000d)//projectile speed 1000m/s (300 auto canon)
		{
			///prediction = position + velocity * time + 0.5 * acceleration * time * time
			///time ~ distance(m)/1000 (1000m/s 300 AC ammmo speed
			///

			Vector3D shotOrigin = turret.GetPosition();

			Vector3D directionToTarget = Vector3D.Normalize(info.Position - shotOrigin);

			//double targetSpeedInLineOfFire = Vector3D.Dot(directionToTarget, info.Velocity);
			double relativeSpeed = ammoSpeed;// - targetSpeedInLineOfFire;

			double distance = Vector3D.Distance(turret.GetPosition(), info.Position);
			double time = distance / relativeSpeed;
			Vector3D displacement = ToVector3D(info.Velocity) * time;
			Vector3D targetVector = info.Position + displacement;
			//Vector3D targetVector = info.Position;

			/// Own velocity correction TESTED ON 300
			Vector3D mySpeed = shipcontroller.GetShipVelocities().LinearVelocity;
			//targetVector = targetVector + mySpeed * -1 * distance / 2500;
			targetVector = targetVector + (mySpeed * -1) * time / 2d;

			//_program.Echo($"T:{time}");
			return targetVector;
		}

		private float RandomizePitchYaw(float raycastConeLimit)
		{
			float result = (float)random.NextDouble() * (raycastConeLimit * raycastAreaSize);
			bool isPositive = random.NextDouble() < 0.5;

			if (!isPositive)
			{
				result = result * -1;
			}

			return result;

		}

		public static Vector3D ToVector3D(Vector3 input)
		{
			return new Vector3D((float)input.X, (float)input.Y, (float)input.Z);
		}

		public static class Actions
		{
			public static string TURN_ON = "OnOff_On";
			public static string TURN_OFF = "OnOff_Off";
			public static string SHOOT_ONCE = "ShootOnce";
		}



	}
}
