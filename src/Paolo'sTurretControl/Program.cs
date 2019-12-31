using System;
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
using System.Linq;
using VRage.Game.GUI.TextPanel;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{

		/// Paolo's Turret Control Script
		/// Designed to work with UD moded wepons 300mm Auto Canon
		/// Moded wepons: Weapons Mod V2 - Upside Down https://steamcommunity.com/sharedfiles/filedetails/?id=1428060367
		/// 
		/// Script uses camera raycast to target and fire 300mm turrets at 5km distance. Targeting point calculates target velocity and own grid velocity for precise aiming in dynamic situations.
		/// 
		///Thx for JTurp without his help this would not work!
		///
		///Setup:
		///Add all cammeras, lcds, ship controller (cockpit, remote, flight seat), turrets (300 and 900) you wont to use to group with name "TargetingSystem".

		TargetingSystem system;

		public Program()
		{
			///Disable Echoing for performance impovement on MP servers
			//Echo = text => { };

			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			system = new TargetingSystem(this, Me);
		}
		public void Save()
		{
		}

		public void Main(string argument, UpdateType updateSource)
		{
			try
			{
				//_rotator.DoTask(); // Disabled for performance

				if (updateSource ==  UpdateType.Update10)
				{
					system.Scan();
				}
				else if (argument.ToLower().Contains("setrange"))
				{
					system.SetRange(argument);
				}
				else if (argument.ToLower().Contains("stop"))
				{
					system.UnAim();
				}

			}
			catch (Exception ex)
			{
				Echo(ex.Message);
			}
		}

		public class TargetingSystem
		{
			const string TARGETING_SYSTEM_GROUP = "TargetingSystem";

			//bool turretTargetNeutrals = false;

			double RANGE = 20000;
			float PITCH = 0;
			float YAW = 0;
			float raycastAreaSize = 0.1f; //0 - 1 cannnot be greater than 1 less- faster detecting but requires more accurate targeting
			bool autoShoot = true;

			private List<IMyTimerBlock> triggers;
			//private bool triggerBlockOnContact = false;

			private Program _program;
			public List<IMyCameraBlock> cameras;
			private List<IMyTextPanel> displays;

			Dictionary<long, MyDetectedEntityInfo> detectedList;

			List<IMyLargeTurretBase> turrets;
			Random random = new Random(DateTime.Now.Second);
			MyDetectedEntityInfo lastDetected;
			//int currentCameraScan = 0;
			bool enemyUpdated = false;
			IMyShipController cockpit;
			IMyProgrammableBlock _me;
			float raycastConeLimit;

			int camIndex = 0;
			int displayIndex = 1; //0 na enemy

			public TargetingSystem(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;

				detectedList = new Dictionary<long, MyDetectedEntityInfo>();

				IMyBlockGroup targetingSystemGroup = _program.GridTerminalSystem.GetBlockGroupWithName(TARGETING_SYSTEM_GROUP);
				if (targetingSystemGroup == null)
				{
					throw new Exception($"No {TARGETING_SYSTEM_GROUP} group defined.");
				}

				_program.Echo("Configure cameras...");
				cameras = new List<IMyCameraBlock>();
				targetingSystemGroup.GetBlocksOfType(cameras);
				if (cameras.Count == 0)
				{
					throw new Exception("No camera found in group.");
				}

				raycastConeLimit = cameras[0].RaycastConeLimit;

				_program.Echo("Configure displays...");
				displays = new List<IMyTextPanel>();
				targetingSystemGroup.GetBlocksOfType(displays);
				displays = displays.OrderBy(x => x.DisplayNameText).ToList();
				if (displays.Count == 0)
				{
					_program.Echo("No text pannel found in group.");
				}
				else
				{
					foreach (var display in displays)
					{
						display.ContentType = ContentType.TEXT_AND_IMAGE;
						display.Font = "DEBUG";
						display.FontSize = 0.9f;
						display.ApplyAction(Actions.TURN_ON);
					}
				}

				_program.Echo("Configure ShipController...");
				var cockpits = new List<IMyShipController>();
				targetingSystemGroup.GetBlocksOfType(cockpits);
				if (cockpits.Count == 0)
				{
					throw new Exception("No ship controller found in group.");
				}
				cockpit = cockpits.First();

				_program.Echo("Configure triggers...");
				triggers = new List<IMyTimerBlock>();
				targetingSystemGroup.GetBlocksOfType(triggers);
				if (triggers.Count == 0)
				{
					_program.Echo($"No Timmer block found in group {TARGETING_SYSTEM_GROUP}");
				}

				_program.Echo("Configure turrets...");
				turrets = new List<IMyLargeTurretBase>();
				targetingSystemGroup.GetBlocksOfType(turrets);
				foreach (var turret in turrets)
				{
					turret.ApplyAction(Actions.TURN_ON);
				}

				//Enable raycast for cameras
				foreach (var camera in cameras)
				{
					camera.EnableRaycast = true;
					camera.ApplyAction(Actions.TURN_ON);
				}

				if (RANGE > cameras.FirstOrDefault().RaycastDistanceLimit)
				{
					//If camera scan range exeeds server setting limit it to server raycast distance limit.
					RANGE = cameras.FirstOrDefault().RaycastDistanceLimit;
				}

				_program.Echo("Init OK.");
			}

			public void SetRange(string setRangeValue)
			{
				if (!string.IsNullOrEmpty(setRangeValue))
				{
					RANGE = Convert.ToDouble(setRangeValue.Trim());
				}
			}
			public void Scan()
			{
				DoScan();
				if (!lastDetected.IsEmpty() && enemyUpdated)
				{
					TargetTurrets(lastDetected);
				}

				if (enemyUpdated && autoShoot)
				{
					foreach (var turret in turrets)
					{
						if (turret.IsAimed)
						{
							turret.ApplyAction(Actions.SHOOT_ONCE);
						}
					}

					_program.Echo("Shoot");
				}
			}
			private void DoScan()
			{

				//_program.Echo($"Prepare scan for {cameras.Count} cameras ");
				//_program.Echo($"Controll {turrets.Count} turrets");

				enemyUpdated = false;

				_program.Echo($"CQ: {camIndex}");

				if (cameras[camIndex].CanScan(RANGE))
				{
					var info = cameras[camIndex].Raycast(RANGE, PITCH, YAW);

					if (!info.IsEmpty())
					{
						if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
						{
							enemyUpdated = true;
						}

						bool update = false;
						if (lastDetected.EntityId == info.EntityId)
						{
							update = true;
						}



						lastDetected = info;
						DisplayNonEmptyInfo(update);
					}
				}

				camIndex++;
				if (camIndex >= cameras.Count)
				{
					camIndex = 0;
					PITCH = 0;
					YAW = 0;
				}
				else
				{
					PITCH = RandomizePitchYaw(raycastConeLimit);
					YAW = RandomizePitchYaw(raycastConeLimit);
				}
			}

			private void TargetTurrets(MyDetectedEntityInfo info)
			{
				if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
				{

					foreach (var turret in turrets)
					{
						var targetVector = CalculateTargetVector(info, turret);
						//split turret fire on grid size
						//targetVector = targetVector + (turret.Position - cockpit.Position) * info.BoundingBox.Size.Length() / 100;
						//turret.TrackTarget(targetVector, info.Velocity);    broken crash client
						turret.SetTarget(targetVector);
					}
				}
			}

			private Vector3D CalculateTargetVector(MyDetectedEntityInfo info, IMyLargeTurretBase turret, double ammoSpeed = 1000d)//projectile speed 1000m/s (300 auto canon)
			{
				///prediction = position + velocity * time + 0.5 * acceleration * time * time
				///time ~ distance(m)/1000 (1000m/s 300 AC ammmo speed
				///

				Vector3D shotOrigin = turret.GetPosition();

				Vector3D directionToTarget = Vector3D.Normalize(info.Position - shotOrigin);

				double targetSpeedInLineOfFire = Vector3D.Dot(directionToTarget, info.Velocity);
				double relativeSpeed = ammoSpeed - targetSpeedInLineOfFire;

				double distance = Vector3D.Distance(turret.GetPosition(), info.Position);
				double time = distance / relativeSpeed;
				Vector3D displacement = ToVector3D(info.Velocity) * time;
				Vector3D targetVector = info.Position + displacement;
				//Vector3D targetVector = info.Position;

				/// Own velocity correction TESTED ON 300
				var velocities = cockpit.GetShipVelocities();
				Vector3D mySpeed = velocities.LinearVelocity;
				//targetVector = targetVector + mySpeed * -1 * distance / 2500;
				targetVector = targetVector + mySpeed * -1 * time / 2;

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

			private string GetDisplayText()
			{
				var sb = new StringBuilder();

				if (lastDetected.HitPosition.HasValue)
				{
					sb.AppendLine();
					sb.AppendLine($"Time: {DateTime.Now.ToString()}");
					sb.Append($"Target found at {(Vector3D.Distance(cameras[camIndex].GetPosition(), lastDetected.HitPosition.Value)):0.00}m range");

					String size = lastDetected.BoundingBox.Size.ToString("0.000");
					String printSize = string.Empty;
					double volume = 1;
					String unitName = "m3";
					if (size != null || size != string.Empty)
					{
						size = size.Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty);
						size = size.Trim();
						String[] sizes = size.Split(':');

						double factor = 1;

						if (lastDetected.Type == MyDetectedEntityType.SmallGrid)
						{
							factor = 4;
							unitName = "blocks";
						}
						else if (lastDetected.Type == MyDetectedEntityType.LargeGrid)
						{
							factor = 16;
							unitName = "blocks";
						}
						foreach (String measure in sizes)
						{
							if (measure == string.Empty)
							{
								//first value
								continue;
							}

							volume = volume * Math.Round(Convert.ToDouble(measure), 0);

							printSize += $"{Convert.ToDouble(measure):0.00} X ";
						}
						volume = volume / factor;
					}
					printSize = printSize.Remove(printSize.Length - 2);
					printSize += "m";

					//X:5.302 Y:3.072 Z:4.252

					//sb.AppendLine($"Found grid ID: {info.EntityId}");
					//sb.AppendLine();
					sb.AppendLine($"Name of object: {lastDetected.Name}");
					sb.AppendLine($"Type of object: {lastDetected.Type}");
					sb.AppendLine($"Current velocity: {lastDetected.Velocity.ToString("0.000")}");
					sb.AppendLine($"Relationship: {lastDetected.Relationship}");
					sb.AppendLine($"Size: {printSize}");
					sb.AppendLine($"Estimated volume: {volume} {unitName}");
					sb.AppendLine($"Position: {lastDetected.Position.ToString("0.000")}");
					sb.AppendLine($"GPS:object{(lastDetected.Position.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");



					//if (info.HitPosition.HasValue)
					//{
					//	sb.AppendLine();
					//	sb.Append($"Hit point: {info.HitPosition.Value.ToString("0.000")}");
					//	sb.AppendLine();
					//	sb.Append($"GPS:objectHit{(info.HitPosition.Value.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");
					//}


				}
				return sb.ToString();
			}

			private Color GetDisplayColor()
			{
				switch (lastDetected.Relationship)
				{
					case MyRelationsBetweenPlayerAndBlock.Owner:
					case MyRelationsBetweenPlayerAndBlock.Friends:
					case MyRelationsBetweenPlayerAndBlock.FactionShare:
						return Color.Green;
					case MyRelationsBetweenPlayerAndBlock.Neutral:
					case MyRelationsBetweenPlayerAndBlock.NoOwnership:
						return Color.Yellow;
					case MyRelationsBetweenPlayerAndBlock.Enemies:
						return Color.Red;
					default:
						return Color.White;
				}
			}

			private void DisplayNonEmptyInfo(bool update)
			{
				Color color = GetDisplayColor();
				

				if (enemyUpdated)
				{
					_program.Echo($"enemy");
					//Enemy na pierwszy ekran
					displays[0].SetValue("FontColor", color);
					displays[0].WriteText(GetDisplayText(), false);
				}
				else
				{
					_program.Echo($"di {displayIndex}");
					displays[displayIndex].SetValue("FontColor", color);
					displays[displayIndex].WriteText(GetDisplayText(), false);

					displayIndex++;
					if (displayIndex >= displays.Count)
					{
						displayIndex = 1;
					}
				}

			}

			internal void UnAim()
			{
				_program.Echo("UnAim");
				foreach (var turret in turrets)
				{
					turret.ResetTargetingToDefault();
					_me.ApplyAction(Actions.TURN_OFF);
				}
			}
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

		//CUT HERE


	}

}


///Thx for JTurp without his help this would not work
/*
I'll have to see which one the script claims is happening - either an obstruction or below elevation
Might be running into a partial block and the game still returns that a block is in that space or something
meanwhile
void GetPredictedTargetPosition(Entity target, Turret turret, out Vector3D interceptPosition)
{
	double ammoSpeed = turret.MuzzleVelocity; var myPos = turret.Position; var myVel = _program._wInfo.Velocity; var tgtPos = http://target.Info.Position;  Vector3D tgtVel = http://target.Info.Velocity;  interceptPosition = tgtPos;  double timeToTarget;  if (Vector3D.IsZero(tgtVel))  {    //if (_turretDebug)    //  Echo("Target Velocity is Zero.");    timeToTarget = Vector3D.Distance(myPos, interceptPosition) / ammoSpeed;  }  else  {    double elapsedTime = target.ElapsedScanTime;    timeToTarget = double.PositiveInfinity;    double timeToMaxVelocity = 0;    double targetAccel = target.Acceleration.Length() * Math.Sign(tgtVel.LengthSquared() - target.LastVelocity.LengthSquared());    Vector3D newTargetPosition = tgtPos + (tgtVel * elapsedTime) + (0.5 * target.Acceleration * elapsedTime * elapsedTime);    Vector3D displacementVector = newTargetPosition - myPos;    Vector3D relativeVelocity = tgtVel - myVel;    if (turret.Type == TurretType.Missile)    {      // Time to reach max velocity: t = (Vf - Vi) / a      // Distance traveled to reach max velocity: d = Vi*t + (a*t^2)/2      // Vanilla missiles start with a velocity of 100 m/s and an acceleration of 600 m/s      timeToMaxVelocity = (ammoSpeed - turret.InitialSpeed) / turret.Acceleration;      if (double.IsNaN(timeToMaxVelocity))        timeToMaxVelocity = 0;      else      {        var distanceTraveledToMaxVel = 100 * timeToMaxVelocity + (300 * timeToMaxVelocity * timeToMaxVelocity);        var distanceAtMaxVelocity = displacementVector.Normalize() - distanceTraveledToMaxVel;        displacementVector *= distanceAtMaxVelocity;      }    }    timeToTarget = RunQuartic(0, targetAccel, ammoSpeed, relativeVelocity, displacementVector, target.Acceleration);    //if (_turretDebug)    //  Echo($"Turret type = {turret.Base.BlockDefinition.TypeIdString.Split('_')[1]}\nElapsed Time = {Math.Round(target.ElapsedScanTime, 6)}\nAmmo speed = {ammoSpeed}\nDistance = {Math.Round(displacementVector.Length(), 2)}\nRelVel = {Math.Round(relativeVelocity.Length(), 2)}\ntAccel = {Math.Round(targetAccel, 2)}\nTime = {Math.Round(timeToTarget, 2)}");    if (double.IsInfinity(timeToTarget))    {      if (Vector3D.IsZero(turret.LastInterceptPosition))        return;      interceptPosition = turret.LastInterceptPosition + (tgtVel * elapsedTime) + (0.5 * target.Acceleration * elapsedTime * elapsedTime);    }    else    {      timeToTarget += timeToMaxVelocity;      interceptPosition = newTargetPosition + (tgtVel * timeToTarget) + (0.5 * target.Acceleration * timeToTarget * timeToTarget);      turret.LastInterceptPosition = interceptPosition;    }  }  interceptPosition -= timeToTarget * myVel;  if ((_usePrecision || target.CenterMissing) && target.LocalOffset.HasValue)    interceptPosition += Vector3D.TransformNormal(target.LocalOffset.Value, http://target.Info.Orientation);}double RunQuartic(double ammoAccel, double targetAccel, double ammoSpeed, Vector3D relativeVelocity, Vector3D displacementVector, Vector3D targetAccelVector){  // Setup the quartic  double a = (0.25 * (ammoAccel * ammoAccel)) - (0.25 * (targetAccel * targetAccel));  double b = (ammoSpeed * ammoAccel) - (relativeVelocity.Length() * targetAccel);  double c = (ammoSpeed * ammoSpeed) - relativeVelocity.LengthSquared() - displacementVector.Dot(targetAccelVector);  double d = -2 * relativeVelocity.Dot(displacementVector);  double e = -displacementVector.LengthSquared();  //if (_turretDebug)  //  Echo($"Quartic Coeffs:\n  a = {a}\n  b = {b}\n  c = {c}\n  d = {d}\n  e = {e}");  double time;  _solver.SolveQuartic(a, b, c, d, e, out time);  return time;}
That's my prediction algo
well, the setup for it

}
*/



/*
 * MatrixD GetGrid2WorldTransform(IMyCubeGrid grid)
{
    Vector3D origin=grid.GridIntegerToWorld(new Vector3I(0,0,0));
    Vector3D plusY=grid.GridIntegerToWorld(new Vector3I(0,1,0))-origin;
    Vector3D plusZ=grid.GridIntegerToWorld(new Vector3I(0,0,1))-origin;
    return MatrixD.CreateScale(grid.GridSize)*MatrixD.CreateWorld(origin,-plusZ,plusY);
}

MatrixD GetBlock2WorldTransform(IMyCubeBlock blk)
{
    Matrix blk2grid;
    blk.Orientation.GetMatrix(out blk2grid);
    return blk2grid*
           MatrixD.CreateTranslation(((Vector3D)new Vector3D(blk.Min+blk.Max))/2.0)*
           GetGrid2WorldTransform(blk.CubeGrid);
}

//Example
void Main(string argument)
{
    var l=new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(l);
    var lcd=l[0] as IMyTextPanel;

    lcd.WritePublicText("");
    GridTerminalSystem.GetBlocks(l);
    for(int i=0;i<l.Count;++i)
    {
        //Calculate error between our world matrix and the game's GetPosition()
        MatrixD g2w=GetGrid2WorldTransform(l[i].CubeGrid);
        Vector3D gridPos=(new Vector3D(l[i].Min+l[i].Max))/2.0; //( .Position is a problem for even size blocks)
        Vector3D calcPos=Vector3D.Transform(gridPos,ref g2w);
        double err=(l[i].GetPosition()-calcPos).Length();
 
        //Find the world "forward" vector for the block
        MatrixD b2w=GetBlock2WorldTransform(l[i]);
        Vector3D fwd=b2w.Forward;
        fwd.Normalize(); //(Need to normalize because the above matrices are scaled by grid size)

        lcd.WritePublicText(String.Format("{0}: Error={1}\n    fwd={2}\n",l[i].CustomName,err,fwd),true);
    }
} 
*/
