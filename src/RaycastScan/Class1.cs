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
		///Thx for JTurp without his help this would not work!


		PinpointScanSystem system;
		Rotator _rotator;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			system = new PinpointScanSystem(this);

			_rotator = new Rotator(this);
		}
		public void Save()

		{

			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means. 
			// 
			// This method is optional and can be removed if not
			// needed.

		}

		// Camera Scan script
		//Add all cammeras, lcds, cockpit, turrets (300 and 900) you wont to use to group with name "ScanSystem"

		public void Main(string argument, UpdateType updateSource)

		{

			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked,
			// or the script updates itself. The updateSource argument
			// describes where the update came from.
			// 
			// The method itself is required, but the arguments above
			// can be removed if not needed.

			try
			{
				_rotator.DoTask();

				if (string.IsNullOrEmpty(argument))
				{
					//Echo("Do scan");
					system.DoScan();
				}
				system.SetRange(argument);
			}
			catch (Exception ex)
			{
				Echo(ex.Message);
			}

			//Echo($"Noumber of cameras: { system.cameras.Count }");
		}

		public class PinpointScanSystem
		{
			const string SCAN_SYSTEM_GROUP = "ScanSystem";

			bool turretTargetNeutrals = false;

			//Trigger block on detection
			String TIMERBLOCKTRIGGER = string.Empty;

			double RANGE = 20000;
			float PITCH = 0;
			float YAW = 0;
			float raycastAreaSize = 0.1f; //0 - 1 cannnot be greater than 1 less- faster detecting but requires more accurate targeting
			int maxScansPerCycle = 8;
			bool autoShoot = true;

			private IMyTimerBlock trigger;
			private bool triggerBlockOnContact = false;

			private Program _program;
			public List<IMyCameraBlock> cameras;
			private List<IMyTextPanel> displays;

			Dictionary<long, MyDetectedEntityInfo> detectedList;
			Dictionary<int, IMyCameraBlock> camerasDictionary;
			List<IMyLargeTurretBase> turrets;
			Random random = new Random(DateTime.Now.Second);
			MyDetectedEntityInfo lastDetected;
			MyDetectedEntityInfo currentEnemy;
			int currentCameraScan = 0;

			IMyShipController cockpit;

			public PinpointScanSystem(Program program)
			{
				_program = program;

				detectedList = new Dictionary<long, MyDetectedEntityInfo>();

				IMyBlockGroup scanSystemGroup = _program.GridTerminalSystem.GetBlockGroupWithName(SCAN_SYSTEM_GROUP);
				if (scanSystemGroup == null)
				{
					throw new Exception($"No {SCAN_SYSTEM_GROUP} group defined.");
				}
				cameras = new List<IMyCameraBlock>();
				scanSystemGroup.GetBlocksOfType<IMyCameraBlock>(cameras);
				if (cameras.Count == 0)
				{
					throw new Exception("No camera found in group.");
				}
				displays = new List<IMyTextPanel>();
				scanSystemGroup.GetBlocksOfType<IMyTextPanel>(displays);
				if (displays.Count == 0)
				{
					throw new Exception("No text pannel found in group.");
				}
				else
				{
					foreach (var display  in displays)
					{
						display.ContentType = ContentType.TEXT_AND_IMAGE;
						display.Font = "DEBUG";
						display.FontSize = 0.6f;
					}
				}

				var cockpits = new List<IMyShipController>();
				scanSystemGroup.GetBlocksOfType<IMyShipController>(cockpits);
				if (cockpits.Count == 0)
				{
					throw new Exception("No cockpit found in group.");
				}
				cockpit = cockpits.First();

				if (TIMERBLOCKTRIGGER != string.Empty)
				{
					//we are to trigger a block on contact
					_program.Echo("Searching timmer block...");
					trigger = _program.GridTerminalSystem.GetBlockWithName(TIMERBLOCKTRIGGER) as IMyTimerBlock;
					if (trigger != null)
					{
						_program.Echo($"Timer {trigger.CustomName} found.");
						triggerBlockOnContact = true;
					}
					else
					{
						_program.Echo($"Timer {TIMERBLOCKTRIGGER} not found.");
					}

				}

				turrets = new List<IMyLargeTurretBase>();
				scanSystemGroup.GetBlocksOfType<IMyLargeTurretBase>(turrets);

				camerasDictionary = new Dictionary<int, IMyCameraBlock>();

				//Enable raycast for cameras
				foreach (var camera in cameras)
				{
					camera.EnableRaycast = true;
					camerasDictionary.Add(currentCameraScan, camera);
					currentCameraScan++;
				}
				currentCameraScan = 0;

				if (RANGE > cameras.FirstOrDefault().RaycastDistanceLimit)
				{
					RANGE = cameras.FirstOrDefault().RaycastDistanceLimit;
				}
			}

			public void SetRange(string setRangeValue)
			{
				if (!string.IsNullOrEmpty(setRangeValue))
				{
					RANGE = Convert.ToDouble(setRangeValue.Trim());
				}
			}
			public void DoScan()
			{
				_program.Echo($"Prepare scan for {cameras.Count} cameras ");
				_program.Echo($"Controll {turrets.Count} turrets");

				bool enemyUpdated = false;

				for (int i = 0; i < maxScansPerCycle; i++)
				{
					//_program.Echo($"currentCameraScan: {currentCameraScan}");
					var camera = camerasDictionary[currentCameraScan];
					currentCameraScan++;
					if (currentCameraScan >= (camerasDictionary.Count)) currentCameraScan = 0;

					if (camera.CanScan(RANGE))
					{
						if (currentCameraScan != 0) //first camers raycast always strait forward
						{
							PITCH = RandomizePitchYaw(camera.RaycastConeLimit);
							YAW = RandomizePitchYaw(camera.RaycastConeLimit);
						}
						else
						{
							PITCH = 0;
							YAW = 0;
						}

						//_program.Echo($"{camera.DisplayNameText} r:{RANGE} p:{PITCH} y:{YAW}");

						var info = camera.Raycast(RANGE, PITCH, YAW);

						if (!info.IsEmpty())
						{
							if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || (info.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral & turretTargetNeutrals))
							{
								currentEnemy = info;
								enemyUpdated = true;
							}

								if (lastDetected.EntityId == info.EntityId)
							{
								UpdateInfo(info, camera);
							}

							if (!detectedList.ContainsKey(info.EntityId))
							{
								DisplayNonEmptyInfo(info, camera);
								detectedList.Add(info.EntityId, info);
							}

							lastDetected = info;
						}
						//DisplayEmptyInfo(info);
					}
				}

				if (!currentEnemy.IsEmpty())
				{
					TargetTurrets(currentEnemy);
				}

				if (enemyUpdated && autoShoot)
				{
					foreach (var turret in turrets)
					{
						turret.ApplyAction("ShootOnce");
					}

				}

				//var cam = cameras.FirstOrDefault();
				//_program.Echo($"cam.EnableRaycast {cam.EnableRaycast}, {cam.RaycastDistanceLimit}, {random.NextDouble()}");
			}

			private void UpdateInfo(MyDetectedEntityInfo info, IMyCameraBlock camera)
			{
				detectedList[info.EntityId] = info;

				UpdateNonEmptyInfo(info, camera);
			}

			private void TargetTurrets(MyDetectedEntityInfo info)
			{
				if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies || (info.Relationship == MyRelationsBetweenPlayerAndBlock.Neutral & turretTargetNeutrals))
				{
					var targetVector = CalculateTargetVector(info);
					foreach (var turret in turrets)
					{
						//split turret fire on grid size
						//targetVector = targetVector + (turret.Position - cockpit.Position) * info.BoundingBox.Size.Length() / 100;
						//turret.TrackTarget(targetVector, info.Velocity);    broken crash client
						turret.SetTarget(targetVector);
					}
				}
			}

			private Vector3D CalculateTargetVector(MyDetectedEntityInfo info)
			{
				///prediction = position + velocity * time + 0.5 * acceleration * time * time
				///time ~ 1.5(s)*distance(m)/1000 
				///

				float distance = (float)Vector3D.Distance(cockpit.GetPosition(), info.Position);
				float time = 1.5f * distance / 1000;
				Vector3D displacement = ToVector3D(info.Velocity) * time;
				Vector3D targetVector = info.Position + displacement;
				//Vector3D targetVector = info.Position;

				/// Own velocity correction TESTED ON 300
				var velocities = cockpit.GetShipVelocities();
				Vector3D mySpeed = velocities.LinearVelocity;
				targetVector = targetVector + mySpeed*-1* distance/2500;

				//_program.Echo($"T:{time}");
				return targetVector;
			}

			float RandomizePitchYaw(float raycastConeLimit)
			{
				float result = (float)random.NextDouble() * (raycastConeLimit * raycastAreaSize);
				bool isPositive = random.NextDouble() < 0.5;

				if (!isPositive)
				{
					result = result * -1;
				}

				return result;

			}

			private void DisplayEmptyInfo(MyDetectedEntityInfo info)
			{
				var sb = new StringBuilder();
				sb.Append($"No target found within  {RANGE} m");
				var firstDisplay = displays.OrderBy(x => x.DisplayNameText).FirstOrDefault();
				firstDisplay.SetValue("FontColor", Color.White);
				firstDisplay.WritePublicText(sb.ToString());
				firstDisplay.ShowPublicTextOnScreen();
				return;
			}

			private string GetDisplayText(MyDetectedEntityInfo info, IMyCameraBlock camera)
			{
				var sb = new StringBuilder();

				if (info.HitPosition.HasValue)
				{
					if (triggerBlockOnContact)
					{
						trigger.Trigger();
					}
					sb.AppendLine();
					sb.AppendLine($"Time: {DateTime.Now.ToString()}");
					sb.Append($"Target found at {(Vector3D.Distance(camera.GetPosition(), info.HitPosition.Value)):0.00}m range");

					String size = info.BoundingBox.Size.ToString("0.000");
					String printSize = string.Empty;
					double volume = 1;
					String unitName = "m3";
					if (size != null || size != string.Empty)
					{
						size = size.Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty);
						size = size.Trim();
						String[] sizes = size.Split(':');

						double factor = 1;

						if (info.Type.ToString() == "SmallGrid")
						{
							factor = 4;
							unitName = "blocks";
						}
						else if (info.Type.ToString() == "LargeGrid")
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

					sb.AppendLine();
					sb.Append($"Found grid ID: {info.EntityId}");
					sb.AppendLine();
					sb.Append($"Name of object: {info.Name}");
					sb.AppendLine();
					sb.Append($"Type of object: {info.Type}");
					sb.AppendLine();
					sb.Append($"Current velocity: {info.Velocity.ToString("0.000")}");
					sb.AppendLine();
					sb.Append($"Relationship: {info.Relationship}");
					sb.AppendLine();
					sb.Append($"Size: {printSize}");
					sb.AppendLine();
					sb.Append($"Estimated volume: {volume} {unitName}");
					sb.AppendLine();
					sb.Append($"Position: {info.Position.ToString("0.000")}");
					sb.AppendLine();
					sb.Append($"GPS:object{(info.Position.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");



					if (info.HitPosition.HasValue)
					{
						sb.AppendLine();
						sb.Append($"Hit point: {info.HitPosition.Value.ToString("0.000")}");
						sb.AppendLine();
						sb.Append($"GPS:objectHit{(info.HitPosition.Value.ToString("0.000").Replace("{", string.Empty).Replace("}", string.Empty).Replace("X", string.Empty).Replace("Y", string.Empty).Replace("Z", string.Empty).Replace(" ", string.Empty))}:");
					}


				}
				return sb.ToString();
			}

			private Color GetDisplayColor(MyDetectedEntityInfo info)
			{
				Color color = Color.White;

				if (info.Relationship.ToString() == "Owner")
				{
					color = Color.Green;
				}
				else if (info.Relationship.ToString() == "Friendly")
				{
					color = Color.Green;
				}
				else if (info.Relationship.ToString() == "FactionShare")
				{
					color = Color.Green;
				}
				else if (info.Relationship.ToString() == "Floating")
				{
					color = Color.Yellow;
				}
				else if (info.Relationship.ToString() == "NoOwnership")
				{
					color = Color.Yellow;
				}
				else if (info.Relationship.ToString() == "Enemies")
				{
					color = Color.Red;
				}

				return color;
			}

			private void DisplayNonEmptyInfo(MyDetectedEntityInfo info, IMyCameraBlock camera)
			{
				DisplayNonEmptyInfo(GetDisplayText(info, camera), GetDisplayColor(info));
			}

			private void DisplayNonEmptyInfo(string text, Color color)
			{
				var displaysOrdered = displays.OrderBy(x => x.DisplayNameText).ToList();

				for (int i = displaysOrdered.Count - 1; i > 0; i--)
				{

					DisplayNonEmptyInfo(displaysOrdered[i], displaysOrdered[i - 1].GetPublicText(), displaysOrdered[i - 1].GetValueColor("FontColor"));
				}
				DisplayNonEmptyInfo(displaysOrdered[0], text, color);
			}
			private void DisplayNonEmptyInfo(IMyTextPanel display, string text, Color color)
			{
				display.SetValue("FontColor", color);
				display.WritePublicText(text);
				display.ShowPublicTextOnScreen();
			}

			private void UpdateNonEmptyInfo(MyDetectedEntityInfo info, IMyCameraBlock camera)
			{
				UpdateNonEmptyInfo(GetDisplayText(info, camera), GetDisplayColor(info));
			}
			private void UpdateNonEmptyInfo(string text, Color color)
			{
				var firstDisplay = displays.OrderBy(x => x.DisplayNameText).First();
				DisplayNonEmptyInfo(firstDisplay, text, color);
			}
		}

		public class Rotator
		{
			private string[] statusChars = new string[] { "/", "-", @"\", "|" };
			private int statusCharPosition = 0;
			private Program _program;
			public Rotator(Program program)
			{
				_program = program;
				statusCharPosition = 0;
			}

			public void DoTask()
			{

				_program.Echo(statusChars[statusCharPosition]);
				statusCharPosition++;
				if (statusCharPosition >= statusChars.Length)
				{
					statusCharPosition = 0;
				}

			}

			public int GetPriority()
			{
				return 100;
			}

		}

		public static Vector3D ToVector3D(Vector3 input)
		{
			return new Vector3D((float)input.X, (float)input.Y, (float)input.Z);
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