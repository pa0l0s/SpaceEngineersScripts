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
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Game.EntityComponents;
using System.Linq;
using VRage.Game.GUI.TextPanel;
using VRage;
using System.Collections.Immutable;

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{
		// v6.4.2, 3 December 2019

		//========================================================================================//
		// User Settings
		//========================================================================================//

		// If using the LiDAR Mapping Script, enter the name of the LiDAR_Block_Group here
		const string LiDAR_Block_Group = "Lidar System";

		// The prefix can be whatever you want
		const string missilePrefix = "Missile";

		// Place this tag in the name or custom data of any blocks on the missile you DON'T want the script to use
		const string excludeTag = "[Exclude]";

		// The three 'Type' items below are what the script uses to ensure proper missile launch.
		// NOTE: You MUST set them accordingly

		// Thruster Types: ThrusterType.Ion, ThrusterType.Hydro, or ThrusterType.Atmo
		// You can have more than one thruster type, just separate them with a '|' (pipe)
		// Example: ThrusterType.Atmo | ThrusterType.Ion
		ThrusterType thrusterType = ThrusterType.Ion;

		// Connector Types:ConnectorType.Merge, ConnectorType.Connector, or ConnectorType.Rotor
		ConnectorType connectorType = ConnectorType.Connector;

		// Missile Types: MissileType.Kinetic, or MissileType.Explosive (if using warheads)
		MissileType missileType = MissileType.Explosive;

		// If you wish to fire missiles in sequence, add the tag below to the CUSTOM DATA of the missile's PB (DO NOT add it to the custom name!)
		// Add the sequence number the missile should have to the end of the tag.
		// Example: Missile 1 has '[M:1]', Missile 2 has '[M:2]', ... Missile n has '[M:n]'
		const string firingOrderTag = "[M:";

		// How many ticks before impact to detonate warheads (60 = 1 second)
		const int airburstDeviationTime = 5;

		// How many ticks the missile should wait (once fired) before turning towards target (60 = 1 second). Default = 60.
		// The distance from the center of the firing platform will also be calculated to allow the missile to take aim sooner if possible
		// NOTE: whichever comes first between clearanceTicks and clearanceMeters will be used.
		int clearanceTicks = 60;

		// How much power the missile should use during the initial launch procedure (while gaining clearance)
		// Between 0.1f and 1.0f (10 - 100%). 'Atmo' is for atmospheric missiles.
		float clearancePowerSpace = 0.50f;
		float clearancePowerAtmo = 1.0f;

		// Used to calculate time to target - adjust to 1.0 below your max speed (double.MaxValue for unlimited speed)
		const double maxSpeed = 210.0;

		// Rotation speed - spins the missile
		double rollSpeed = 1; // maximum of 3

		// Spiral control - attempts to avoid enemy fire
		bool useSpiral = true;

		//========================================================================================//
		// Advanced Settings:
		//========================================================================================//

		// If 'useSpiral' is set to true, adjust the variables below to your liking
		const float distanceBeforeSpiral = 1000f;    // distance to target at which missile starts to spiral
		const double maxSpiralTime = 3;              // # seconds for 1 full rotation
		const double spiralAngle = 5;                // deviation from aim vector

		// Rotation control system (PID)
		const double proportionalConstant = 50; // proportional gain of gyroscopes
		const double integralConstant = 0;      // integral gain of gyroscopes
		const double derivativeConstant = 30;   // derivative gain of gyroscopes

		// DEBUG - if set to TRUE, output will be displayed on a text panel (only after launch)
		bool DEBUG = false;

		// Name of the LCD to send the output to (must be attached to the missile)
		const string DEBUG_LCD = "Missile 4 Debug LCD";

		//========================================================================================//
		// End of User Settings, don't change anything below this line!
		//========================================================================================//

		// Lists
		List<IMyGyro> _gyros = new List<IMyGyro>();
		List<IMyThrust> _thrusters = new List<IMyThrust>();
		List<IMyWarhead> _warheads = new List<IMyWarhead>();
		List<IMyCameraBlock> _cameras = new List<IMyCameraBlock>();
		List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
		List<MyDetectedEntityInfo> _sensorInfoList = new List<MyDetectedEntityInfo>();

		// Other items
		IMyRadioAntenna _ant;
		IMyRemoteControl _remote;
		IMyProjector _projector;
		IMyFunctionalBlock _conBlock;
		IMyBatteryBlock _startupBattery;
		string _projName = null;
		StringBuilder _debug = new StringBuilder();

		// Target information
		Target _target;
		double _timeToTarget = double.PositiveInfinity;

		// My information
		WorldInfo _missileInfo;
		MatrixD _platformMatrix = new MatrixD();
		IMyBlockGroup _lidarGroup;
		BoundingBoxD _bBox;

		IMyUnicastListener _uListener;
		IMyBroadcastListener _bListener;
		long _baseID = 0;
		string _myID = null;

		PID _yawPID;
		PID _pitchPID;

		// Booleans
		bool isClear = false;
		bool useRoll = false;
		bool firstRun = true;
		bool inFlight = false;
		bool msgPending = false;
		bool readyToLaunch = false;
		bool startKillCount = false;
		bool killSignalRcvd = false;
		bool sendSpiralInfo = false;
		bool activateSpiral = false;
		bool tryStartSpiral = false;
		bool launchReplySent = false;
		bool sendUpdateReply = false;
		bool updateSpiralMode = false;
		bool continueToTarget = false;
		bool sendRollSpeedInfo = false;
		bool updateRollAndSpiral = false;
		bool startLaunchSequence = false;

		// Constants
		const double _eps = 1E-4;
		const float _run = 1.0f / 60.0f;
		const double _toRadians = Math.PI / 180.0;

		// Runtime variables
		double _spiralTime = 0;
		int _replyCounter = 0;
		int _tickCounter = 0;
		int _clearCount = 0;
		int _debugCount = 0;
		int _killCount = 0;
		int _replyTick = 0;
		int _ticks = 0;
		Random rnd = new Random();

		// Debug stuff
		double _timeSinceLastUpdate = 0;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
			_bListener = IGC.RegisterBroadcastListener("[MISSILE]");
			_uListener = IGC.UnicastListener;

			_bListener.SetMessageCallback();
			_uListener.SetMessageCallback();
		}

		public void Main(string argument, UpdateType updateSource)
		{
			try
			{
				SubMain(argument, updateSource);
			}
			catch (Exception e)
			{
				var sb = new StringBuilder();
				sb.AppendLine("Exception Message:");
				sb.Append(_debug);
				sb.AppendLine($"   {e.Message}");
				sb.AppendLine();
				sb.AppendLine("Stack trace:");
				sb.AppendLine(e.StackTrace);
				sb.AppendLine();
				var exceptionDump = sb.ToString();
				var lcd = GridTerminalSystem.GetBlockWithName(DEBUG_LCD) as IMyTextPanel;
				Echo(exceptionDump);
				Me.CustomData = exceptionDump;
				if (lcd != null)
				{
					lcd.WriteText(exceptionDump, append: false);
					lcd.ContentType = ContentType.TEXT_AND_IMAGE;
					lcd.TextPadding = 0;
				}
				throw;
			}
		}

		public void SubMain(string argument, UpdateType updateSource)
		{
			CalculateAverageRuntime();

			msgPending = _uListener.HasPendingMessage || _bListener.HasPendingMessage;

			if ((updateSource & UpdateType.Mod) > 0)
			{
				if (DEBUG)
				{
					_debug.Append("Returned from Main. UpdateType == MOD..\n");
					WriteDebug();
				}
				return;
			}

			if (!firstRun && !msgPending && Runtime.TimeSinceLastRun.TotalSeconds == 0)
			{
				if (DEBUG)
				{
					_debug.Append("Returned from Main. !firstRun and TimeSinceLastRun = 0..\n");
					WriteDebug();
				}
				return;
			}

			_debug.Clear();

			if (_missileInfo != null)
			{
				_missileInfo.Update();

				if (DEBUG)
					_debug.Append($"My Vel = {_missileInfo.Speed}\nMy Accel = {_missileInfo.Acceleration}\n");
			}

			if (DEBUG)
			{
				_debug.Append($"LastRuntimeMS: {Math.Round(_expAverage, 4)}\n")
				  .Append($"KillSigRcvd = {killSignalRcvd}\n");

				Echo($"Debug Count is {++_debugCount}");
				Echo($"Udpate freq = {Runtime.UpdateFrequency}");
				Echo($"Proj Name = {_projName}");
				if (_projector != null)
					Echo($"Proj ID = {_projector.EntityId}");
				Echo($"IGC rcvd = {msgPending}");
			}

			if (killSignalRcvd)
			{
				if (_missileInfo.Speed > 1)
					ApplyRotation(-_missileInfo.Velocity);
				else
					KillGuidance();

				return;
			}

			if (firstRun)
			{
				if (DEBUG)
					Echo("Executing 'firstRun'");

				if (!InitialSetup())
					return;

				firstRun = false;
				return;
			}
			else if (_conBlock.Enabled && argument.Length == 0 && !msgPending && !startLaunchSequence && !readyToLaunch && !inFlight && !sendRollSpeedInfo && !sendSpiralInfo && !updateRollAndSpiral)
			{
				var con = _conBlock as IMyShipConnector;
				if (con != null && con.Status == MyShipConnectorStatus.Connectable)
					con.Connect();

				if (DEBUG)
				{
					_debug.Append($"Returning due to bools all FALSE:\n  conBlock Enabled: {_conBlock.Enabled}\n  arg.Len: {argument.Length}\n  startLaunchSeq: {startLaunchSequence}\n  readyToLaunch: {readyToLaunch}\n  inFlight: {inFlight}\n  sendRoll: {sendRollSpeedInfo}\n  sendSpiral: {sendSpiralInfo}\n  updateRS: {updateRollAndSpiral}\n");
					WriteDebug();
				}

				return;
			}

			if (DEBUG)
				_debug.Append($"SendUpdateReply = {sendUpdateReply}\n  Reply Counter = {_replyCounter}\n Reply Tick = {_replyTick}\n");

			if (sendUpdateReply)
			{
				if (++_replyCounter >= _replyTick && IGC.SendUnicastMessage(_baseID, "Update", _myID))
				{
					sendUpdateReply = false;
					_replyCounter = 0;
				}
			}

			if (inFlight)
			{
				if (DEBUG)
					_debug.Append($"Start Kill Count = {startKillCount}\nKill Count = {_killCount + 1}\n");

				if (startKillCount && ++_killCount > 120)
					killSignalRcvd = true;

				_timeToTarget -= _run;

				if (_warheads.Count > 0)
				{
					double radius = Me.CubeGrid.WorldVolume.Radius + 1;
					double radiusSqd = radius * radius;
					bool detonate = false;

					if (_sensors.Count > 0)
					{
						foreach (var s in _sensors)
						{
							s.DetectedEntities(_sensorInfoList);

							foreach (var entity in _sensorInfoList)
							{
								if (entity.EntityId == _target.TargetID)
								{
									detonate = true;
									break;
								}
							}

							if (detonate)
								break;
						}
					}

					if (!detonate && _cameras.Count > 0)
					{
						foreach (var cam in _cameras)
						{
							if (cam.CanScan(radius))
							{
								var info = cam.Raycast(radius);

								if (!info.IsEmpty() && info.EntityId == _target.TargetID)
								{
									if (info.HitPosition.HasValue && Vector3D.DistanceSquared(info.HitPosition.Value, Me.CubeGrid.WorldVolume.Center) <= radiusSqd)
										detonate = true;

									break;
								}
							}
						}
					}

					if (!detonate && _timeToTarget <= airburstDeviationTime * _run)
						detonate = true;

					if (DEBUG)
						_debug.Append($"Warheads set to Detonate = {detonate}\nAirBurst Deviation = {airburstDeviationTime * _run}\n");

					if (detonate)
					{
						_warheads.ForEach(x => x.IsArmed = true);
						_warheads.ForEach(x => x.Detonate());

						killSignalRcvd = true;
					}
				}
			}

			if (DEBUG)
			{
				_timeSinceLastUpdate += _run;
				_debug.Append($"Argument = '{argument}'\nLast Updated {_timeSinceLastUpdate.ToString("0.0000")} s\n");
			}

			if (msgPending)
			{
				if (!HandleMessages())
					return;
			}
			else if (!string.IsNullOrEmpty(argument))
			{
				if (!HandleArgument(argument))
					return;
			}
			else if (!Vector3D.IsZero(_target.InterceptVector, _eps))
				continueToTarget = true;

			if (inFlight && continueToTarget)
			{
				// Continue to last known target location
				if (DEBUG)
					_debug.Append("Automatic update\n")
					  .Append($"Last Intercept Vec = {Vector3D.Round(_target.InterceptVector, 2)}\n");

				_target.AddRun();
				Intercept();
			}

			if (startLaunchSequence)
			{
				if (DEBUG)
					_debug.Append("Entering 'startLaunchSequence' code\n");

				if (++_ticks < _tickCounter)
				{
					if (DEBUG)
					{
						_debug.Append($"Need to wait {_tickCounter - _ticks} more ticks\n");
						WriteDebug();
					}
					return;
				}

				var rotorCon = _conBlock as IMyMotorStator;
				if (rotorCon != null)
					rotorCon.Detach();
				else
					_conBlock.Enabled = false;

				if (Me.CubeGrid?.GetCubeBlock(_projector.Position)?.FatBlock != null)
				{
					if (DEBUG)
					{
						_debug.Append("Projector still found. Returning...\n");
						WriteDebug();
					}
					return;
				}

				if (!Init())
				{
					if (DEBUG)
					{
						_debug.Append("Failed to INIT!\n");
						WriteDebug();
					}
					return;
				}

				startLaunchSequence = false;
				readyToLaunch = true;
				if (DEBUG)
					WriteDebug();
				return;
			}

			if (readyToLaunch)
			{
				if (DEBUG)
					_debug.Append("Entering 'readyToLaunch' code\n");

				isClear = GetClearance();

				if (!isClear)
				{
					if (DEBUG)
						WriteDebug();
					return;
				}

				if (DEBUG)
					_debug.Append("Clearance is good!\n");

				if (!launchReplySent)
				{
					launchReplySent = IGC.SendUnicastMessage(_baseID, "Launch", MyTuple.Create(_myID, _target.TargetID));

					if (DEBUG)
					{
						_debug.Append($"Debug count: {_debugCount}\n");
						_debug.Append("Launch reply NOT sent - returning...\n");
						_debug.Append($"BaseID = {_baseID}\nMyId = {_myID}\n");
						WriteDebug();
					}
					return;
				}

				if (DEBUG)
					_debug.Append($"Launch reply sent = {launchReplySent}\n");

				_thrusters.ForEach(x => x.ThrustOverridePercentage = 1);

				inFlight = true;
				readyToLaunch = false;
				tryStartSpiral = useSpiral;
			}

			if (sendRollSpeedInfo)
				sendRollSpeedInfo = !IGC.SendUnicastMessage(_baseID, "Roll", rollSpeed);
			else if (sendSpiralInfo)
				sendSpiralInfo = !IGC.SendUnicastMessage(_baseID, "Spiral", useSpiral);
			else if (updateRollAndSpiral)
				updateRollAndSpiral = !IGC.SendUnicastMessage(_baseID, "Roll and Spiral", new MyTuple<double, bool>(rollSpeed, useSpiral));

			if (updateSpiralMode && _thrusters[0].ThrustOverridePercentage >= 0.9f)
			{
				updateSpiralMode = false;
				tryStartSpiral = true;
			}

			if (tryStartSpiral)
			{
				activateSpiral = Vector3D.DistanceSquared(_missileInfo.Position, _target.Position) < distanceBeforeSpiral * distanceBeforeSpiral;
				useRoll = rollSpeed > 0 && activateSpiral;
				tryStartSpiral = !activateSpiral;
			}

			continueToTarget = false;

			if (DEBUG)
			{
				_debug
				  .Append($"Ready To Launch? {readyToLaunch}\n")
				  .Append($"In flight? {inFlight}\n")
				  .Append($"Dot Prod: {_missileInfo.Forward.Dot(_target.InterceptVector)}\n")
				  .Append($"Angle Between: {VectorUtils.GetAngleBetween(_missileInfo.Forward, _target.InterceptVector) * 180 / Math.PI}°\n"); // in degrees

				WriteDebug();
			}
		}

		void UpdateBroadcastRange(Vector3D platformPosition)
		{
			if (_ant == null)
				return;

			var distance = Vector3.Distance(platformPosition, _remote.GetPosition());
			_ant.Radius = distance + 100;
		}

		const double _tickSignificance = 0.005;
		double _prevAverage = 0;
		double _expAverage = 0;
		int _runtimeCtr = 0;
		void CalculateAverageRuntime()
		{
			if (++_runtimeCtr < 21)
				return;

			_expAverage = (1 - _tickSignificance) * _prevAverage + _tickSignificance * Runtime.LastRunTimeMs;
			_prevAverage = _expAverage;
		}

		bool HandleMessages()
		{
			if (DEBUG)
				_debug.Append($"Message Handler: Broadcast? {_bListener.HasPendingMessage} Unicast? {_uListener.HasPendingMessage}\n")
				  .Append($"My ID = {_myID}\n");

			var msg = _uListener.HasPendingMessage ? _uListener.AcceptMessage() : _bListener.AcceptMessage();

			double timeStamp = 0;
			string command = null;

			if (msg.Data is ImmutableArray<MyTuple<MyTuple<string, int, long, double, string>, MyTuple<Vector3D, Vector3D, Vector3D, MatrixD, BoundingBoxD>>>)
			{
				var data = (ImmutableArray<MyTuple<MyTuple<string, int, long, double, string>, MyTuple<Vector3D, Vector3D, Vector3D, MatrixD, BoundingBoxD>>>)msg.Data;

				if (DEBUG)
					_debug.Append($"Msg.Data is an Array with {data.Length} items\n");

				bool shouldReturn = true;

				for (int i = 0; i < data.Length; i++)
				{
					var temp = data[i];

					var tup1 = temp.Item1;
					var tup2 = temp.Item2;

					if (DEBUG)
						_debug.Append($"Message is for {tup1.Item1}\n");

					if (tup1.Item1 != _myID)
					{
						if (DEBUG)
							_debug.Append($"Message not for me.. continuing\n");
						continue;
					}

					if (DEBUG)
						_debug.Append("Message received!!\n");

					shouldReturn = false;

					_tickCounter = tup1.Item2;
					var targetID = tup1.Item3;
					timeStamp = tup1.Item4;
					command = tup1.Item5;

					var targetPosition = tup2.Item1;
					var targetVelocity = Vector3D.IsZero(tup2.Item2, _eps) ? Vector3D.Zero : tup2.Item2;
					var targetAccel = tup2.Item3;
					_platformMatrix = tup2.Item4;
					_bBox = tup2.Item5;

					if (DEBUG)
						_debug.Append($"Command = {command}\nTgtId = {targetID}\nTimeStamp = {timeStamp}\nTicsToWait = {_tickCounter}\n");

					if (_target == null)
						_target = new Target(targetID, timeStamp, targetVelocity, targetAccel, targetPosition, DEBUG, _debug);
					else
						_target.Update(targetID, timeStamp, targetVelocity, targetAccel, targetPosition);

					break;
				}

				if (shouldReturn)
					return false;

				UpdateBroadcastRange(_platformMatrix.Translation);
			}
			else if (msg.Data is MyTuple<MyTuple<string, int, long, double, string>, MyTuple<Vector3D, Vector3D, Vector3D, MatrixD, BoundingBoxD>>)
			{
				if (DEBUG)
					_debug.Append("Msg.Data is a Tuple\n");

				var data = (MyTuple<MyTuple<string, int, long, double, string>, MyTuple<Vector3D, Vector3D, Vector3D, MatrixD, BoundingBoxD>>)msg.Data;

				var tup1 = data.Item1;
				var tup2 = data.Item2;

				if (DEBUG)
					_debug.Append($"Message is for {tup1.Item1}\n");

				if (tup1.Item1 != _myID)
				{
					if (DEBUG)
						_debug.Append($"Message not for me.. returning\n");
					return false;
				}

				_tickCounter = tup1.Item2;
				var targetID = tup1.Item3;
				timeStamp = tup1.Item4;
				command = tup1.Item5;

				var targetPosition = tup2.Item1;
				var targetVelocity = Vector3D.IsZero(tup2.Item2, _eps) ? Vector3D.Zero : tup2.Item2;
				var targetAccel = tup2.Item3;
				_platformMatrix = tup2.Item4;
				_bBox = tup2.Item5;

				if (DEBUG)
					_debug.Append($"Command = {command}\n");

				if (_target == null)
					_target = new Target(targetID, timeStamp, targetVelocity, targetAccel, targetPosition, DEBUG, _debug);
				else
					_target.Update(targetID, timeStamp, targetVelocity, targetAccel, targetPosition);

				UpdateBroadcastRange(_platformMatrix.Translation);
			}
			else if (msg.Data is string)
			{
				return HandleArgument((string)msg.Data);
			}
			else
			{
				if (DEBUG)
				{
					_debug.Append($"Unknown data from {msg.Source} in HandleMessages():\n  TAG: {msg.Tag}\n  DATA: {msg.Data}\n");
					WriteDebug();
				}
				return false;
			}

			if (command == "Launch")
			{
				Runtime.UpdateFrequency = UpdateFrequency.Update1;
				_baseID = msg.Source;
				startLaunchSequence = true;

				if (_startupBattery != null)
				{
					_startupBattery.Enabled = true;
					_startupBattery.ChargeMode = ChargeMode.Discharge;
				}

				if (DEBUG)
					WriteDebug();
			}
			else if (command == "Update" && inFlight)
			{
				startKillCount = true;
				_killCount = 0;
				_timeSinceLastUpdate = 0;

				if (DEBUG)
				{
					_debug.Append($"\nUPDATE RECEIVED!\n tID = {_target.TargetID}\n tStamp = {timeStamp}\n");
					WriteDebug();
				}

				if (_target.Updated)
				{
					continueToTarget = false;

					sendUpdateReply = true;
					_replyTick = rnd.Next(1, 5);

					Intercept();
				}
				else
					continueToTarget = true;
			}
			else
			{
				if (DEBUG)
				{
					_debug.Append($"Unknown command: {command}\n");
					WriteDebug();
				}
				return false;
			}
			return true;
		}

		bool HandleArgument(string argument)
		{
			if (argument.Equals("roll speed", StringComparison.OrdinalIgnoreCase))
			{
				if (++rollSpeed > 3)
					rollSpeed = 0;

				sendRollSpeedInfo = true;
			}
			else if (argument.Equals("toggle spiral", StringComparison.OrdinalIgnoreCase))
			{
				useSpiral = !useSpiral;

				if (inFlight)
				{
					if (activateSpiral)
						activateSpiral = false;
					else if (_thrusters[0].ThrustOverridePercentage >= 0.9f)
						activateSpiral = true;
					else
						updateSpiralMode = true;
				}

				sendSpiralInfo = true;
			}
			else if (argument.Equals("update roll and spiral", StringComparison.OrdinalIgnoreCase))
			{
				updateRollAndSpiral = true;
			}
			else
			{
				if (DEBUG)
				{
					_debug.Append($"Unknown argument in HandleArgument: Arg: {argument}\n");
					WriteDebug();
				}
				return false;
			}

			return true;
		}

		IMyTextPanel lcd;
		void WriteDebug()
		{
			if (lcd == null)
				lcd = GridTerminalSystem.GetBlockWithName(DEBUG_LCD) as IMyTextPanel;

			if (lcd != null)
			{
				lcd.WriteText(_debug);
				lcd.ContentType = ContentType.TEXT_AND_IMAGE;
				lcd.TextPadding = 0;
			}
		}

		void KillGuidance()
		{
			_thrusters.ForEach(x => x.ThrustOverride = 0);
			_gyros.ForEach(x => x.GyroOverride = false);
			Me.Enabled = false;

			if (DEBUG)
			{
				_debug.Append($"KILLING MISSILE\n tID = {_target.TargetID}\n");
				WriteDebug();
			}
		}

		// Check whether the missile is still inside the firing platform's bounding box, or heading toward the firing platform
		public bool CheckRayCollisions(Vector3D curPos, RayD[] rays, out bool inBox)
		{
			inBox = false;

			if (Vector3D.DistanceSquared(curPos, _bBox.Center) > Vector3D.DistanceSquared(curPos, _target.Position))
				return false;

			var contains = _bBox.Contains(_remote.CubeGrid.WorldAABB);

			if (contains == ContainmentType.Contains || contains == ContainmentType.Intersects)
			{
				if (DEBUG)
				{
					_debug.Append($"The missile is INSIDE the bbox!\n");
					WriteDebug();
				}

				inBox = true;
				return true;
			}

			for (int i = 0; i < rays.Length; i++)
			{
				RayD r = rays[i];
				var num = _bBox.Intersects(r);

				if (DEBUG)
					_debug.Append($"Checking ray {i + 1} for collisions\n");

				if (num != null && num > 0)
				{
					if (DEBUG)
					{
						_debug.Append($"Ray intersects in {num} meters\n");
						WriteDebug();
					}

					return true;
				}
			}

			if (DEBUG)
			{
				_debug.Append("Ray(s) do not intersect!\n");
				WriteDebug();
			}

			return false;
		}

		// Whip's Spiral Trajectory Method
		public void GetSpiralHeading(double angle, Vector3D axisVec, Vector3D fwdVec, Vector3D upVec, out Vector3D rotVec)
		{
			if (DEBUG)
				_debug.Append($"Getting Spiral Heading\n");

			double radius = Math.Tan(angle * _toRadians);
			Vector3D axis_Norm = Vector3D.Normalize(axisVec);

			if ((_spiralTime += _run) > maxSpiralTime)
				_spiralTime = 0;

			double theta = MathHelper.TwoPi * _spiralTime / maxSpiralTime;

			if (fwdVec.Dot(axis_Norm) > 0)
			{
				Vector3D cross_X = Vector3D.Normalize(upVec.Cross(axis_Norm));
				Vector3D cross_Y = Vector3D.Normalize(cross_X.Cross(axis_Norm));
				Vector3D rotation = radius * (cross_X * Math.Cos(theta) + cross_Y * Math.Sin(theta));
				rotVec = axis_Norm + rotation;
			}
			else
				rotVec = axis_Norm;
		}

		public bool GetClearance()
		{
			if (isClear)
				return true;

			double clearanceMeters = BoundingSphereD.CreateFromBoundingBox(_bBox).Radius;

			if (DEBUG)
				_debug.Append($"BBOX SIZE = {_bBox.Size}\nClearanceMeters = {clearanceMeters}\nCurClearance = {Vector3D.Distance(_remote.GetPosition(), _bBox.Center)}\n");

			if (!_missileInfo.InGravity)
			{
				foreach (var thruster in _thrusters)
					thruster.ThrustOverridePercentage = clearancePowerSpace;

				if (++_clearCount >= clearanceTicks || Vector3D.DistanceSquared(_remote.GetPosition(), _bBox.Center) >= clearanceMeters * clearanceMeters)
					return true;

				return false;
			}
			else
			{
				var ticks = (float)clearanceTicks * 0.1f;

				foreach (var thruster in _thrusters)
					thruster.ThrustOverridePercentage = clearancePowerAtmo;

				if (++_clearCount >= ticks || Vector3D.DistanceSquared(_remote.GetPosition(), _bBox.Center) >= clearanceMeters * clearanceMeters)
					return true;

				return false;
			}
		}

		public void Intercept()
		{
			Vector3D headingVec = _target.CalculateInterceptVector(_missileInfo, out _timeToTarget);

			if (DEBUG)
				_debug.Append($"activateSpiral bool = {activateSpiral}\nThrustOverride% = {_thrusters[0].ThrustOverridePercentage}\n");

			if (activateSpiral && _thrusters[0].ThrustOverridePercentage >= 0.9f)
			{
				if (DEBUG)
					_debug.Append($"spiralTime = {_spiralTime}\n");

				GetSpiralHeading(spiralAngle, _target.InterceptVector, _missileInfo.Forward, _missileInfo.Up, out headingVec);
			}

			headingVec = CalculateHeadingVec(_missileInfo.Velocity, headingVec);
			ApplyRotation(headingVec);
		}

		void ApplyRotation(Vector3D headingVec)
		{
			var lftVector = _remote.WorldMatrix.Left;

			//---Get pitch and yaw angles
			double yawAngle;
			double pitchAngle;
			GetRotationAngles(headingVec, _remote, out yawAngle, out pitchAngle);

			//---Angle controller
			double yawSpeed = _yawPID.CorrectError(yawAngle, _run);
			double pitchSpeed = _pitchPID.CorrectError(pitchAngle, _run);

			//---Set appropriate gyro override
			ApplyGyroOverride(pitchSpeed, yawSpeed, useRoll ? rollSpeed : 0, _gyros, _remote);
		}

		public Vector3D CalculateHeadingVec(Vector3D myVelocity, Vector3D interceptVec)
		{
			var pos = _missileInfo.Position;

			// Check altitude vs distance to target - pull up if closer to ground than target, unless target is on the ground
			double altitude;
			if (_remote.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude))
			{
				if (DEBUG)
					_debug.Append($"Altitude = {altitude}\n");

				if (altitude < 50 && interceptVec.Dot(_missileInfo.Gravity) < 0)
				{
					var dSqd = interceptVec.LengthSquared();

					if (DEBUG)
						_debug.Append($"dst = {interceptVec.Length().ToString("0.000")}\ndSqd = {dSqd.ToString("0.000")}\nAltSqd = {(altitude * altitude).ToString("0.000")}\n");

					if (altitude * altitude < dSqd)
						return Vector3D.Normalize(-_missileInfo.Gravity) * Math.Min(Math.Sqrt(dSqd), 50) + interceptVec;
				}
			}

			RayD[] rays = new RayD[]
			{
new RayD(pos, Vector3D.Normalize(interceptVec)),
new RayD(pos, myVelocity)
			};

			bool inBox;
			if (CheckRayCollisions(pos, rays, out inBox))
				return GetAvoidanceVector(interceptVec, inBox);

			if (myVelocity.LengthSquared() < 625 || myVelocity.Dot(interceptVec) < 0)
				return interceptVec;

			return VectorUtils.ReflectVector(myVelocity, interceptVec);
		}

		public Vector3D GetAvoidanceVector(Vector3D headingVec, bool inBox)
		{
			var base2TargetVec = _target.Position - _platformMatrix.Translation;
			var base2MissileVec = _missileInfo.Position - _platformMatrix.Translation;

			var dir2Target = _platformMatrix.GetClosestDirection(base2TargetVec);
			var dir2Missile = _platformMatrix.GetClosestDirection(base2MissileVec);

			if (DEBUG)
				_debug.Append($"Dir2Tgt = {dir2Target}\nDir2Msl = {dir2Missile}\nInBox = {inBox}\n");

			if (dir2Target == dir2Missile) // missile and target are on same side of ship / station
				return headingVec;

			if (inBox)
				return _platformMatrix.GetDirectionVector(dir2Missile);

			switch (dir2Target)
			{
				case Base6Directions.Direction.Up:
					if (dir2Missile == Base6Directions.Direction.Down)
						return _platformMatrix.Left;

					return _platformMatrix.Up;

				case Base6Directions.Direction.Down:
					if (dir2Missile == Base6Directions.Direction.Up)
						return _platformMatrix.Left;

					return _platformMatrix.Down;

				case Base6Directions.Direction.Left:
					if (dir2Missile == Base6Directions.Direction.Right)
						return _platformMatrix.Down;

					return _platformMatrix.Left;

				case Base6Directions.Direction.Right:
					if (dir2Missile == Base6Directions.Direction.Left)
						return _platformMatrix.Down;

					return _platformMatrix.Right;

				case Base6Directions.Direction.Forward:
					if (dir2Missile == Base6Directions.Direction.Backward)
						return _platformMatrix.Down;

					return _platformMatrix.Forward;

				case Base6Directions.Direction.Backward:
					if (dir2Missile == Base6Directions.Direction.Forward)
						return _platformMatrix.Down;

					return _platformMatrix.Backward;

				default:
					Vector3D newVec = Vector3D.CalculatePerpendicularVector(headingVec);
					return newVec * 100 - Vector3D.Normalize(headingVec) * 10;
			}
		}

		// Whip's Get Rotation Angles Method v12 - 2/16/18
		public void GetRotationAngles(Vector3D targetVector, IMyTerminalBlock reference, out double yaw, out double pitch)
		{
			var localTargetVector = Vector3D.TransformNormal(targetVector, MatrixD.Transpose(reference.WorldMatrix));
			var flattenedTargetVector = new Vector3D(localTargetVector.X, 0, localTargetVector.Z);

			yaw = VectorUtils.GetAngleBetween(Vector3D.Forward, flattenedTargetVector) * Math.Sign(localTargetVector.X); //right is positive
			if (Math.Abs(yaw) < 1E-6 && localTargetVector.Z > 0) //check for straight back case
				yaw = Math.PI;

			if (Vector3D.IsZero(flattenedTargetVector)) //check for straight up case
				pitch = MathHelper.PiOver2 * Math.Sign(localTargetVector.Y);
			else
				pitch = VectorUtils.GetAngleBetween(localTargetVector, flattenedTargetVector) * Math.Sign(localTargetVector.Y); //up is positive
		}

		//Whip's ApplyGyroOverride Method v9 - 8/19/17
		void ApplyGyroOverride(double pitch_speed, double yaw_speed, double roll_speed, List<IMyGyro> gyroList, IMyRemoteControl reference)
		{
			var rotationVec = new Vector3D(-pitch_speed, yaw_speed, roll_speed); // -pitch because keen does some weird stuff with signs

			var shipMatrix = reference.WorldMatrix;
			var relativeRotationVec = Vector3D.TransformNormal(rotationVec, shipMatrix);

			foreach (IMyGyro g in gyroList)
			{
				var gyroMatrix = g.WorldMatrix;
				var transformedRotationVec = Vector3D.TransformNormal(relativeRotationVec, Matrix.Transpose(gyroMatrix));

				g.Pitch = (float)transformedRotationVec.X;
				g.Yaw = (float)transformedRotationVec.Y;
				g.Roll = (float)transformedRotationVec.Z;
				g.GyroOverride = true;
			}
		}

		List<IMyTerminalBlock> tempList = new List<IMyTerminalBlock>();
		StringBuilder sequenceNbr = new StringBuilder();
		bool InitialSetup()
		{
			if (DEBUG)
				Echo("Executing 'InitialSetup'");

			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			clearancePowerSpace = MathHelper.Clamp(clearancePowerSpace, 0.1f, 1.0f);
			clearancePowerAtmo = MathHelper.Clamp(clearancePowerAtmo, 0.1f, 1.0f);

			GridTerminalSystem.GetBlocks(tempList);

			_startupBattery = GetClosestBlock<IMyBatteryBlock>(tempList, Me);

			switch (connectorType)
			{
				case ConnectorType.Connector:
					_conBlock = GetClosestBlock<IMyShipConnector>(tempList, Me);

					var con = _conBlock as IMyShipConnector;
					if (con != null)
					{
						if (con.Status == MyShipConnectorStatus.Connectable)
							con.Connect();

						if (con.Status == MyShipConnectorStatus.Connected)
							GridTerminalSystem.GetBlocksOfType(tempList, x => x.CubeGrid == con.OtherConnector.CubeGrid);
					}
					else
					{
						Echo($"Connector type is invalid!");
						return false;
					}
					break;
				case ConnectorType.Rotor:
					_conBlock = GetClosestBlock<IMyMotorStator>(tempList, Me);
					break;
				default:
					_conBlock = GetClosestBlock<IMyShipMergeBlock>(tempList, Me);
					break;
			}

			_lidarGroup = GridTerminalSystem.GetBlockGroupWithName(LiDAR_Block_Group);
			if (_lidarGroup != null)
				_lidarGroup.GetBlocks(tempList);

			_projector = GetClosestBlock<IMyProjector>(tempList, Me);

			if (_projector == null || _conBlock == null)
			{
				if (_projector == null)
					Echo("No projector found!");
				if (_conBlock == null)
					Echo("No connector/merge/rotor block found!");
				return false;
			}

			_pitchPID = new PID(proportionalConstant, integralConstant, derivativeConstant, 0.75);
			_yawPID = new PID(proportionalConstant, integralConstant, derivativeConstant, 0.75);

			string toCheck = Me.CustomData;
			if (toCheck.Contains(firingOrderTag))
			{
				string tempStr = toCheck.Substring(toCheck.IndexOf(firingOrderTag)).Split(firingOrderTag.Last())[1].Trim();
				sequenceNbr.Clear();

				for (int i = 0; i < tempStr.Length; i++)
				{
					char c = tempStr[i];

					if (char.IsNumber(c))
						sequenceNbr.Append(c);
					else
						break;
				}
			}

			if (sequenceNbr.Length == 0)
				sequenceNbr.Append("-1");

			_myID = $"{missilePrefix}.{_projector.EntityId}.{Me.EntityId}.{thrusterType}.{connectorType}.{missileType}.{sequenceNbr}";
			_projName = _projector.CustomName;
			Me.CustomName = _myID;
			Echo("Initial Setup Complete!");
			return true;
		}

		bool Init()
		{
			tempList.Clear();
			GridTerminalSystem.GetBlocksOfType(tempList, x => x.CubeGrid == Me.CubeGrid);

			if (DEBUG)
				_debug.Append($"Entering Init() - Templist Item Ct: {tempList.Count}\n");

			foreach (var b in tempList)
			{
				if (b.CustomName.Contains(excludeTag) || b.CustomData.Contains(excludeTag))
					continue;

				var gyro = b as IMyGyro;
				if (gyro != null)
				{
					if (!gyro.Enabled)
						gyro.Enabled = true;

					gyro.Yaw = 0f;
					gyro.Pitch = 0f;
					gyro.Roll = 0f;
					gyro.GyroOverride = false;

					_gyros.Add(gyro);
					continue;
				}

				var thrust = b as IMyThrust;
				if (thrust != null)
				{
					if (!thrust.Enabled)
						thrust.Enabled = true;

					thrust.ThrustOverride = 0f;

					_thrusters.Add(thrust);
					continue;
				}

				var wh = b as IMyWarhead;
				if (wh != null)
				{
					_warheads.Add(wh);
					continue;
				}

				var ant = b as IMyRadioAntenna;
				if (ant != null)
				{
					if (_ant == null)
					{
						if (!ant.Enabled)
							ant.Enabled = true;

						if (!ant.EnableBroadcasting)
							ant.EnableBroadcasting = true;

						ant.Radius = float.MaxValue;
						_ant = ant;
					}
					continue;
				}

				var remote = b as IMyRemoteControl;
				if (remote != null)
				{
					if (_remote == null)
					{
						if (remote.IsAutoPilotEnabled)
							remote.SetAutoPilotEnabled(false);

						_remote = remote;
					}
					continue;
				}

				var bat = b as IMyBatteryBlock;
				if (bat != null)
				{
					if (!bat.Enabled)
						bat.Enabled = true;

					bat.ChargeMode = ChargeMode.Discharge;
					continue;
				}

				var cam = b as IMyCameraBlock;
				if (cam != null)
				{
					if (!cam.Enabled)
						cam.Enabled = true;

					if (!cam.EnableRaycast)
						cam.EnableRaycast = true;

					_cameras.Add(cam);
					continue;
				}

				var sensor = b as IMySensorBlock;
				if (sensor != null)
				{
					if (!sensor.Enabled)
						sensor.Enabled = true;

					sensor.DetectFriendly = false;
					sensor.DetectOwner = false;
					sensor.DetectEnemy = true;
					sensor.DetectNeutral = true;
					sensor.DetectLargeShips = true;
					sensor.DetectSmallShips = true;
					sensor.DetectStations = true;

					double radius = Me.CubeGrid.WorldVolume.Radius + 5;
					SetSensorArea(sensor, (float)radius);

					_sensors.Add(sensor);
				}
			}

			if (DEBUG)
			{
				_debug.Append($"INIT Items Found:\n  Gyros - {_gyros.Count}\n")
				  .Append($"  Thrusters - {_thrusters.Count}\n")
				  .Append($"  Warheads - {_warheads.Count}\n")
				  .Append($"  Antenna OK? {_ant != null}\n")
				  .Append($"  Remote OK? {_remote != null}\n");

				Me.CustomData = _debug.ToString();
			}

			if (_ant == null || _remote == null || _gyros.Count == 0)
			{
				if (_ant == null)
					Echo("No antenna found!");
				if (_gyros.Count == 0)
					Echo("No gyros found!");
				if (_remote == null)
					Echo("No remote control found!");
				return false;
			}

			for (int i = _thrusters.Count - 1; i >= 0; i--)
			{
				var t = _thrusters[i];

				if (Base6Directions.GetOppositeDirection(t.Orientation.Forward) != _remote.Orientation.Forward)
					_thrusters.RemoveAtFast(i);
			}

			if (_thrusters.Count == 0)
			{
				Echo("No forward thrusters found!");
				return false;
			}

			_missileInfo = new WorldInfo(_remote);
			Me.CubeGrid.CustomName = _myID;
			_ant.AttachedProgrammableBlock = Me.EntityId;
			Echo("Setup Complete!");
			if (DEBUG)
				_debug.Append($"Setup Complete!\n");
			return true;
		}

		public void SetSensorArea(IMySensorBlock s, float fixedDistance)
		{
			Vector3 min = s.CubeGrid.Min - s.Position;
			Vector3 max = s.CubeGrid.Max - s.Position;

			Matrix m;
			s.Orientation.GetMatrix(out m);

			float gSize = s.CubeGrid.GridSize;
			Vector3 Zoffset = new Vector3(0, 0, 0.35);

			Vector3 rel_Min = Vector3.Transform(min, Matrix.Transpose(m));
			Vector3 rel_Max = Vector3.Transform(max, Matrix.Transpose(m));

			Vector3 dim_Min = Vector3.Abs(Vector3.Min(rel_Min, rel_Max) * gSize - (0.5f * gSize) - Zoffset) + fixedDistance;
			Vector3 dim_Max = Vector3.Max(rel_Min, rel_Max) * gSize + (0.5f * gSize) - Zoffset + fixedDistance;

			s.RightExtend = dim_Max.X;
			s.LeftExtend = dim_Min.X;
			s.TopExtend = dim_Max.Y;
			s.BottomExtend = dim_Min.Y;
			s.BackExtend = dim_Max.Z;
			s.FrontExtend = dim_Min.Z;
		}

		T GetClosestBlock<T>(List<IMyTerminalBlock> blocks, IMyTerminalBlock referencePoint) where T : class, IMyTerminalBlock
		{
			var pos = referencePoint.GetPosition();
			T item = null;

			double curD, maxD = double.PositiveInfinity;

			foreach (var block in blocks)
			{
				var b = block as T;
				if (b != null)
				{
					curD = Vector3D.DistanceSquared(pos, b.GetPosition());

					if (curD < maxD)
					{
						item = b;
						maxD = curD;
					}
				}
			}

			if (item != null)
				blocks.Remove(item);

			return item;
		}

		[Flags]
		enum MissileType { Kinetic = 1, Explosive = 2 }

		[Flags]
		enum ThrusterType { None = 0, Atmo = 1, Hydro = 2, Ion = 4 }

		[Flags]
		enum ConnectorType { Merge = 1, Connector = 2, Rotor = 4 }

		MyDefinitionId HydroDef = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");

		ThrusterType GetThrusterType(IMyThrust thruster)
		{
			var subtype = thruster.BlockDefinition.SubtypeName;
			char ch = subtype.Length > 15 ? subtype[15] : subtype[0];

			switch (ch)
			{
				case 'A':
					if (subtype.Length == 32)
					{
						if (subtype == "SmallBlockSmallAtmosphericThrust"
						|| subtype == "SmallBlockLargeAtmosphericThrust"
						|| subtype == "LargeBlockSmallAtmosphericThrust"
						|| subtype == "LargeBlockLargeAtmosphericThrust")
							return ThrusterType.Atmo;
					}
					break;
				case 'H':
					if (subtype.Length == 29)
					{
						if (subtype == "SmallBlockSmallHydrogenThrust"
						|| subtype == "SmallBlockLargeHydrogenThrust"
						|| subtype == "LargeBlockSmallHydrogenThrust"
						|| subtype == "LargeBlockLargeHydrogenThrust")
							return ThrusterType.Hydro;
					}
					break;
				case 'T':
					if (subtype.Length == 21)
					{
						if (subtype == "SmallBlockSmallThrust"
						|| subtype == "SmallBlockLargeThrust"
						|| subtype == "LargeBlockSmallThrust"
						|| subtype == "LargeBlockLargeThrust")
							return ThrusterType.Ion;
					}
					break;
				default:
					if (subtype.Contains("Atmo")
					  || thruster.CustomName.Contains("Atmo")
					  || thruster.CustomData.Contains("Atmo"))
						return ThrusterType.Atmo;

					if (subtype.Contains("Hydro")
					  || thruster.CustomName.Contains("Hydro")
					  || thruster.CustomData.Contains("Hydro"))
						return ThrusterType.Hydro;

					if (subtype.Contains("Ion")
					  || thruster.CustomName.Contains("Ion")
					  || thruster.CustomData.Contains("Ion"))
						return ThrusterType.Ion;
					break;
			}

			MyResourceSinkComponent sink;
			thruster.Components.TryGet<MyResourceSinkComponent>(out sink);

			if (sink != null && sink.AcceptedResources.Contains(HydroDef))
				return ThrusterType.Hydro;

			var thrustRatio = thruster.MaxEffectiveThrust / thruster.MaxThrust;

			if (_missileInfo.Gravity.LengthSquared() < 96)
			{
				if (thrustRatio < 0.487f)
					return ThrusterType.Atmo;

				if (thrustRatio > 0.487f)
					return ThrusterType.Ion;
			}
			else
			{
				double alt;
				_remote.TryGetPlanetElevation(MyPlanetElevation.Sealevel, out alt);

				if (alt > 5170)
				{
					if (thrustRatio > 0.487f)
						return ThrusterType.Ion;

					if (thrustRatio < 0.487f)
						return ThrusterType.Atmo;
				}
				else
				{
					if (thrustRatio > 0.487f)
						return ThrusterType.Atmo;

					if (thrustRatio < 0.487f)
						return ThrusterType.Ion;
				}
			}

			return ThrusterType.Hydro;
		}

		public struct Complex
		{
			private const double LOG_10_INV = 0.43429448190325;
			private const double eps = 1e-6;
			public double Re;
			public double Im;

			public Complex(double re, double im)
			{
				Re = re;
				Im = im;
			}

			public static readonly Complex Zero = new Complex(0.0, 0.0);
			public static readonly Complex Re_One = new Complex(1.0, 0.0);
			public static readonly Complex Im_One = new Complex(0.0, 1.0);

			public static Complex operator +(Complex a, Complex b)
			{
				Complex result = new Complex(
					a.Re + b.Re,
					a.Im + b.Im);
				return result;
			}

			public static Complex operator -(Complex a, Complex b)
			{
				Complex result = new Complex(
					a.Re - b.Re,
					a.Im - b.Im);
				return result;
			}

			public static Complex operator *(Complex a, Complex b)
			{
				Complex result = new Complex(
					a.Re * b.Re - a.Im * b.Im,
					a.Im * b.Re + b.Im * a.Re);
				return result;
			}

			public static Complex operator /(Complex a, Complex b)
			{
				Complex result = new Complex(
					(a.Re * b.Re + a.Im * b.Im) / (b.Re * b.Re + b.Im * b.Im),
					(a.Im * b.Re - a.Re * b.Im) / (b.Re * b.Re + b.Im * b.Im));
				return result;
			}

			public static bool operator ==(Complex a, double b)
			{
				return (a.Re == b && a.Im == b);
			}

			public static bool operator !=(Complex a, double b)
			{
				return !(a.Re == b && a.Im == b);
			}

			public static bool operator >(Complex a, Complex b)
			{
				return (a.Re > b.Re && a.Im > b.Im);
			}

			public static bool operator <(Complex a, Complex b)
			{
				return (a.Re < b.Re && a.Im < b.Im);
			}

			public static bool operator >=(Complex a, Complex b)
			{
				return (a.Re >= b.Re && a.Im >= b.Im);
			}

			public static bool operator <=(Complex a, Complex b)
			{
				return (a.Re <= b.Re && a.Im <= b.Im);
			}

			public static bool operator >(Complex a, double b)
			{
				return (a.Re > b && a.Im > b);
			}

			public static bool operator <(Complex a, double b)
			{
				return (a.Re < b && a.Im < b);
			}

			public static bool operator >=(Complex a, double b)
			{
				return (a.Re >= b && a.Im >= b);
			}

			public static bool operator <=(Complex a, double b)
			{
				return (a.Re <= b && a.Im <= b);
			}
			public static Complex operator *(Complex a, double b)
			{
				Complex B = new Complex(b, 0);
				return a * B;
			}

			public static Complex operator /(Complex a, double b)
			{
				Complex B = new Complex(b, 0);
				return a / B;
			}

			public static Complex operator +(Complex a, double b)
			{
				Complex B = new Complex(b, 0);
				return a + B;
			}

			public static Complex operator -(Complex a, double b)
			{
				Complex B = new Complex(b, 0);
				return a - B;
			}

			public static Complex operator *(double a, Complex b)
			{
				Complex A = new Complex(a, 0);
				return A * b;
			}

			public static Complex operator /(double a, Complex b)
			{
				Complex A = new Complex(a, 0);
				return A / b;
			}

			public static Complex operator +(double a, Complex b)
			{
				Complex A = new Complex(a, 0);
				return A + b;
			}

			public static Complex operator -(double a, Complex b)
			{
				Complex A = new Complex(a, 0);
				return A - b;
			}

			public static Complex Sqrt(Complex a)
			{
				return Pow(a, 0.5f);
			}

			public static Complex Sqrt(double a)
			{
				return Pow(new Complex(a, 0), 0.5f);
			}

			public static Complex Pow(Complex a, double b)
			{
				// De Moivre's Theorem:  (r*(cos(x) + i*sin(x))^n = r^n*(cos(x*n) + i*sin(x*n))

				// First convert the complex number to polar form...
				double r = Math.Sqrt(a.Re * a.Re + a.Im * a.Im);
				double x = Math.Atan2(a.Im, a.Re);

				// And then rebuild the components according to De Moivre...
				double cos = Math.Pow(r, b) * Math.Cos(x * b);
				double sin = Math.Pow(r, b) * Math.Sin(x * b);
				return new Complex(cos, sin);
			}

			public static Complex Pow(double a, double b)
			{
				return Pow(new Complex(a, 0), b);
			}

			public static Complex operator -(Complex a)
			{
				return new Complex(-a.Re, -a.Im);
			}

			public static Complex Sin(Complex a)
			{
				double a_Re = a.Re;
				double a_Im = a.Im;
				return new Complex(Math.Sin(a_Re) * Math.Cosh(a_Im), Math.Cos(a_Re) * Math.Sinh(a_Im));
			}

			public static Complex Sinh(Complex a) /* Hyperbolic sin */
			{
				double a_Re = a.Re;
				double a_Im = a.Im;
				return new Complex(Math.Sinh(a_Re) * Math.Cos(a_Im), Math.Cosh(a_Re) * Math.Sin(a_Im));

			}
			public static Complex Asin(Complex a) /* Arcsin */
			{
				return (-Im_One) * Log(Im_One * a + Sqrt(Re_One - a * a));
			}

			public static Complex Cos(Complex a)
			{
				double a_Re = a.Re;
				double a_Im = a.Im;
				return new Complex(Math.Cos(a_Re) * Math.Cosh(a_Im), -(Math.Sin(a_Re) * Math.Sinh(a_Im)));
			}

			public static Complex Cosh(Complex a) /* Hyperbolic cos */
			{
				double a_Re = a.Re;
				double a_Im = a.Im;
				return new Complex(Math.Cosh(a_Re) * Math.Cos(a_Im), Math.Sinh(a_Re) * Math.Sin(a_Im));
			}
			public static Complex Acos(Complex a) /* Arccos */
			{
				return (-Im_One) * Log(a + Im_One * Sqrt(Re_One - (a * a)));
			}
			public static Complex Tan(Complex a)
			{
				return (Sin(a) / Cos(a));
			}

			public static Complex Tanh(Complex a) /* Hyperbolic tan */
			{
				return (Sinh(a) / Cosh(a));
			}
			public static Complex Atan(Complex a) /* Arctan */
			{
				Complex Two = new Complex(2.0, 0.0);
				return (Im_One / Two) * (Log(Re_One - Im_One * a) - Log(Re_One + Im_One * a));
			}

			public static Complex Log(Complex a) /* Log of the complex number value to the base of 'e' */
			{
				return (new Complex((Math.Log(Abs(a))), (Math.Atan2(a.Im, a.Re))));
			}
			public static Complex Log(Complex a, Double b) /* Log of the complex number to a the base of a double */
			{
				return (Log(a) / Math.Log(b));
			}
			public static Complex Log10(Complex a) /* Log to the base of 10 of the complex number */
			{
				Complex temp_log = Log(a);
				return (Scale(temp_log, (Double)LOG_10_INV));
			}

			public bool Equals(Complex value)
			{
				return Re == value.Re && Im == value.Im;
			}

			public static double Abs(Complex a)
			{
				return Math.Sqrt(a.Im * a.Im + a.Re * a.Re);
			}

			public static bool IsNaN(Complex a)
			{
				return double.IsNaN(a.Re) || double.IsNaN(a.Im);
			}

			public static bool IsReal(Complex a)
			{
				if (Math.Abs(a.Im) < eps)
				{ return true; }
				else
				{ return false; }
			}

			public override bool Equals(object obj)
			{
				return base.Equals(obj);
			}

			public override string ToString()
			{
				return $"({Re}, {Im})";
			}

			public override int GetHashCode()
			{
				int n1 = 99999997;
				int hash_real = this.Re.GetHashCode() % n1;
				int hash_imaginary = this.Im.GetHashCode();
				int final_hashcode = hash_real ^ hash_imaginary;
				return (final_hashcode);
			}

			private static Complex Scale(Complex a, double b)
			{
				double result_re = b * a.Re;
				double result_im = b * a.Im;
				return new Complex(result_re, result_im);
			}

			public static bool IsZero(Complex d)
			{
				return d > -eps && d < eps;
			}
		}

		public class PID
		{
			// u(t) = Kp*e(t) + Ki|e(t')dt' + Kd(de(t)/dt)

			double _Kp; // Proportional Gain
			double _Ki; // Integral Gain
			double _Kd; // Derivative Gain
			double _lastError = 0;
			double _errorIntegral = 0;
			double _integralDecay = 0;
			bool _firstPIDRun = true;

			public PID(double kP, double kI, double kD, double integralDecay)
			{
				_Kp = kP;
				_Ki = kI;
				_Kd = kD;
				_integralDecay = integralDecay;
			}

			/// <summary>
			/// Calculates the output value of a PID loop, given an error and a time step.
			/// </summary>
			/// <param name="error">The difference between the Desired and Actual measurement.</param>
			/// <param name="timeStep">How long it's been since this method has been called (in seconds).</param>
			/// <returns>The value needed to correct the difference in measurement.</returns>
			public double CorrectError(double error, double timeStep)
			{
				// Derivative term
				double dInput = error - _lastError;
				double errorDerivative = _firstPIDRun ? 0 : dInput / timeStep;

				// Integral term
				_errorIntegral = (_errorIntegral * _integralDecay) + (error * timeStep);

				// Compute the output
				double output = _Kp * error + _Ki * _errorIntegral + _Kd * errorDerivative;

				// Save the error for the next use
				_lastError = error;
				_firstPIDRun = false;
				return output;
			}

			public void Reset()
			{
				_errorIntegral = 0;
				_lastError = 0;
				_firstPIDRun = true;
			}
		}

		public class QuarticSolver
		{
			List<double> _realRoots = new List<double>();
			List<Complex> _complexRoots = new List<Complex>();
			StringBuilder _debug = new StringBuilder();
			double _eps = 0.001;
			public bool DEBUG { get; set; }

			public QuarticSolver(StringBuilder debugSB, bool debugMode)
			{
				_debug = debugSB;
				DEBUG = debugMode;
			}

			double FindRoot()
			{
				double t = double.PositiveInfinity;

				foreach (var root in _realRoots)
				{
					if (root >= 0 && root < t)
						t = root;
				}

				return t;
			}

			// MathForum.org solution for Quartic Polynomial, written by JTurp
			public void SolveQuartic(double a4, double a3, double a2, double a1, double a0, out double time)
			{
				// Starting equation: [a4]x^4 + [a3]x^3 + [a2]x^2 + [a1]x + [a0] = 0
				_realRoots.Clear();
				_complexRoots.Clear();

				if (Math.Abs(a4) < _eps && Math.Abs(a3) < _eps)
				{
					if (DEBUG)
						_debug.Append("A & B = 0. Using Quadratic\n");

					// Quadratic case - true if max speed has been reached
					SolveQuadratic(a2, a1, a0);

					foreach (var t in _complexRoots)
					{
						if (!Complex.IsNaN(t) && Complex.IsReal(t))
							_realRoots.Add(t.Re);
					}

					time = FindRoot();
					return;
				}
				else if (Math.Abs(a4) < _eps)
				{
					if (DEBUG)
						_debug.Append("A = 0. Using Cubic\n");

					// Cubic case - [a3]x^3 + [a2]x^2 + [a1]x + [a0] = 0
					// Roots are the roots of the cubic
					SolveCubic(a3, a2, a1, a0);

					foreach (var t in _complexRoots)
					{
						if (!Complex.IsNaN(t) && Complex.IsReal(t))
							_realRoots.Add(t.Re);
					}

					time = FindRoot();
					return;
				}
				else if (Math.Abs(a0) < _eps)
				{
					if (DEBUG)
						_debug.Append("E = 0. Using Cubic\n");

					// Another cubic case - x([a4]x^3 + [a3]x^2 + [a2]x + [a1]) = 0
					// Roots are x = 0 and the roots of the cubic
					SolveCubic(a4, a3, a2, a1);

					foreach (var t in _complexRoots)
					{
						if (!Complex.IsNaN(t) && Complex.IsReal(t))
							_realRoots.Add(t.Re);
					}

					time = FindRoot();
					return;
				}

				// Depress the quartic to x^4 + Ax^3 + Bx^2 + Cx + D = 0
				// Divide all coefficients by the leading coefficient (a4)
				double A = a3 / a4, B = a2 / a4, C = a1 / a4, D = a0 / a4;
				double aOver4 = A * 0.25;

				if (Math.Abs(D) < _eps)
				{
					if (DEBUG)
						_debug.Append("Depressed D = 0. Using Cubic\n");

					// Another cubic case - x(x^3 + Ax^2 + Bx + C) = 0
					// Roots are x = 0 and the roots of the cubic
					SolveCubic(1.0, A, B, C);

					foreach (var t in _complexRoots)
					{
						if (!Complex.IsNaN(t) && Complex.IsReal(t))
							_realRoots.Add(t.Re);
					}

					time = FindRoot();
					return;
				}

				// Eliminate cubic term to get y^4 + Ey^2 + Fy + G = 0
				// Substitute x = y - A/4
				double E = B - (3 * A * A) / 8, F = C + (A * A * A) / 8 - (A * B) / 2, G = D - (3 * A * A * A * A) / 256 + (A * A * B) / 16 - (A * C) / 4;

				// Check cases
				if (Math.Abs(G) < _eps)
				{
					if (DEBUG)
						_debug.Append("G = 0 after Substitution. Using Cubic\n");

					// Reduced Cubic case - y(y^3 + Ey + f) = 0
					// Roots are x = -A/4 and the roots of the cubic (minus A/4 each)
					SolveCubic(1.0, 0, E, F);

					_realRoots.Add(-A * 0.25);
					foreach (var t in _complexRoots)
					{
						if (!Complex.IsNaN(t) && Complex.IsReal(t))
							_realRoots.Add(t.Re - A * 0.25);
					}

					time = FindRoot();
					return;
				}
				else if (Math.Abs(F) < _eps)
				{
					if (DEBUG)
						_debug.Append("BiQuadratic Case\n");

					// BiQuadratic case - y^4 + Ey^2 + G = 0 - complete the square
					// Roots are the four roots of the bi-quadratic (minus A/4 each)
					Complex sqrt = Complex.Sqrt(-G + (E * E) * 0.25);
					Complex sqrtOne = Complex.Sqrt(sqrt - E * 0.5);
					Complex sqrtTwo = Complex.Sqrt(-sqrt - E * 0.5);

					Complex r1 = sqrtOne - aOver4;
					Complex r2 = sqrtTwo - aOver4;
					Complex r3 = -sqrtOne - aOver4;
					Complex r4 = -sqrtTwo - aOver4;

					if (!Complex.IsNaN(r1) && Complex.IsReal(r1))
						_realRoots.Add(r1.Re);
					if (!Complex.IsNaN(r2) && Complex.IsReal(r2))
						_realRoots.Add(r2.Re);
					if (!Complex.IsNaN(r3) && Complex.IsReal(r3))
						_realRoots.Add(r3.Re);
					if (!Complex.IsNaN(r4) && Complex.IsReal(r4))
						_realRoots.Add(r4.Re);

					time = FindRoot();
					return;
				}

				if (DEBUG)
					_debug.Append("Auxiliary Cubic\n");

				// Auxiliary Cubic z^3 + hz^2 + iz - j = 0
				double h = E / 2, i = (E * E - 4 * G) / 16, j = -(F * F) / 64;

				SolveCubic(1.0, h, i, j);

				if (_complexRoots.Count < 2)
				{
					time = FindRoot();
					return;
				}

				Complex p = Complex.Sqrt(_complexRoots[0]);
				Complex q = Complex.Sqrt(_complexRoots[1]);

				Complex r = -F / (8 * p * q);

				Complex x1 = p + q + r - aOver4;
				Complex x2 = p - q - r - aOver4;
				Complex x3 = -p + q - r - aOver4;
				Complex x4 = -p - q + r - aOver4;

				if (!Complex.IsNaN(x1) && Complex.IsReal(x1))
					_realRoots.Add(x1.Re);
				if (!Complex.IsNaN(x2) && Complex.IsReal(x2))
					_realRoots.Add(x2.Re);
				if (!Complex.IsNaN(x3) && Complex.IsReal(x3))
					_realRoots.Add(x3.Re);
				if (!Complex.IsNaN(x4) && Complex.IsReal(x4))
					_realRoots.Add(x4.Re);

				time = FindRoot();
			}

			// MathForum.org solution for cubic equation
			void SolveCubic(double a3, double a2, double a1, double a0)
			{

				if (Math.Abs(a3) < _eps)
				{
					// Quadratic case
					SolveQuadratic(a2, a1, a0);
					return;
				}

				// Divide all coefficients by leading coefficient (a3 coeff)
				double A = a2 / a3, B = a1 / a3, C = a0 / a3;

				// Substitute (t-A/3) for x to reduce the cubic to t^3 + Pt + Q = 0
				double P = ((3 * B) - (A * A)) / 3;
				double Q = ((2 * A * A * A) - (9 * A * B) + (27 * C)) / 27;

				// Cube roots of unity
				Complex unity_1 = (-1 + Complex.Sqrt(-3)) * 0.5;
				Complex unity_2 = (-1 - Complex.Sqrt(-3)) * 0.5;

				if (Math.Abs(P) < _eps && Math.Abs(Q) < _eps)
				{
					// t is 0 also
					Complex t1 = new Complex(-A / 3, 0);
					_complexRoots.Add(t1);
					return;
				}
				else if (Math.Abs(P) < _eps || Math.Abs(Q) < _eps)
				{
					// Immediately solvable
					if (Math.Abs(P) < _eps)
					{
						// Equation becomes t^3 + Q = 0, so t = cbrt(-Q)
						// Subtract A/3 from all answers
						double aOver3 = A / 3;
						Complex cPowQ = Complex.Pow(-Q, 1.0 / 3.0);
						Complex t1 = cPowQ - aOver3;
						Complex t2 = (cPowQ * unity_1) - aOver3;
						Complex t3 = (cPowQ * unity_2) - aOver3;

						_complexRoots.Add(t1);
						_complexRoots.Add(t2);
						_complexRoots.Add(t3);
						return;
					}
					else if (Math.Abs(Q) < _eps)
					{
						// Equation becomes t^3 + Pt = 0 --> factor out a t to get t(t^2 + P) = 0, so t = 0 and t = +/- sqrt(-P)
						// Subtract A/3 from all answers
						double aOver3 = A / 3;
						Complex cPowP = Complex.Pow(-P, 1.0 / 2.0);
						Complex t1 = new Complex(-aOver3, 0);
						Complex t2 = cPowP - aOver3;
						Complex t3 = -cPowP - aOver3;

						_complexRoots.Add(t1);
						_complexRoots.Add(t2);
						_complexRoots.Add(t3);
						return;
					}
				}

				if (Math.Abs((A * A) - (3 * B)) < _eps)
				{
					// Perfect cube, factors down to (x + A/3)^3 = A^3/27 - C
					// Subtract A/3 from all answers
					double aOver3 = A / 3;
					Complex cPowA = Complex.Pow(((A * A * A) / 27) - C, 1.0 / 3.0);
					Complex t1 = cPowA - aOver3;
					Complex t2 = (cPowA * unity_1) - aOver3;
					Complex t3 = (cPowA * unity_2) - aOver3;

					_complexRoots.Add(t1);
					_complexRoots.Add(t2);
					_complexRoots.Add(t3);
					return;
				}

				double g = (A * A) - (3 * B);
				double h = (A * B) - (9 * C);
				double i = (B * B) - (3 * A * C);

				Complex sqrt = Complex.Sqrt((h * h) - (4 * g * i));
				Complex z1 = (-h + sqrt) / (2 * g);
				Complex z2 = (-h - sqrt) / (2 * g);
				Complex z = z1.Re > 0 ? (z2.Re > 0 ? (z1.Re < z2.Re ? z1 : z2) : z1) : z2;

				Complex d = (3 * z) + A;
				Complex e = (3 * z * z) + (2 * A * z) + B;
				Complex f = (z * z * z) + (A * z * z) + (B * z) + C;
				Complex cPow = Complex.Pow((e * e * e) - (27 * f * f), 1.0 / 3.0);

				// The three roots are...
				Complex r1 = z + (3 * f) / (cPow - e);
				Complex r2 = z + (3 * f) / ((cPow * unity_1) - e);
				Complex r3 = z + (3 * f) / ((cPow * unity_2) - e);

				_complexRoots.Add(r1);
				_complexRoots.Add(r2);
				_complexRoots.Add(r3);
				return;
			}

			void SolveQuadratic(double a2, double a1, double a0)
			{
				if (a2 < _eps)
				{
					// Linear case, bx + c = 0, x = -c/b
					Complex t1 = new Complex(-a0 / a1, 0);

					_complexRoots.Add(t1);
					return;
				}

				// Divide by leading coefficient to eliminate issues
				double b = a1 / a2, c = a0 / a2;

				if (b < _eps)
				{
					// x^2 + c = 0, x = sqrt(-c)
					Complex t1 = Complex.Sqrt(-c);

					_complexRoots.Add(t1);
					return;
				}

				double D = (b * b) - (4 * c);
				Complex sqrtD = Complex.Sqrt(D);

				Complex r1 = (-b + sqrtD) * 0.5;
				Complex r2 = (-b - sqrtD) * 0.5;

				_complexRoots.Add(r1);
				_complexRoots.Add(r2);
			}
		}

		public class Target
		{
			public long TargetID;
			public double LastUpdateTime;
			public double Acceleration = 0;
			public double Speed = 0;
			double _lastSpeed = 0;
			public Vector3D Position;
			public Vector3D Velocity;
			public Vector3D AccelVector = Vector3D.Zero;
			public Vector3D InterceptVector = Vector3D.Zero;
			public Vector3D LastValidIntercept = Vector3D.Zero;

			public bool Updated = true;

			QuarticSolver _solver;
			StringBuilder _debug;
			bool DEBUG;

			public Target(long id, double timestamp, Vector3D velocity, Vector3D accel, Vector3D position, bool debug, StringBuilder sb)
			{
				TargetID = id;
				LastUpdateTime = timestamp;

				Position = position;
				Velocity = Vector3D.IsZero(velocity, 0.01) ? Vector3D.Zero : velocity;
				Speed = Vector3D.IsZero(velocity, 0.01) ? 0 : velocity.Length();
				AccelVector = Vector3D.IsZero(accel, 0.01) ? Vector3D.Zero : accel;
				Acceleration = Vector3D.IsZero(accel, 0.01) ? 0 : accel.Length();

				_solver = new QuarticSolver(sb, debug);
				DEBUG = debug;
				_debug = sb;
			}

			public void AddRun()
			{
				Position = Position + Velocity * _run + 0.5 * AccelVector * _run * _run;
			}

			public void Update(long id, double updateTime, Vector3D velocity, Vector3D accel, Vector3D position)
			{
				Updated = true;

				if (id == TargetID)
				{
					if (updateTime > LastUpdateTime)
					{
						var newSpeed = Vector3D.IsZero(velocity, 0.01) ? 0 : velocity.Length();

						if (newSpeed == 0 || newSpeed >= maxSpeed || Math.Abs(newSpeed - _lastSpeed) < 0.5)
						{
							AccelVector = Vector3D.Zero;
							Acceleration = 0;
						}
						else
						{
							AccelVector = accel;

							if (Vector3D.IsZero(AccelVector, 0.01))
							{
								AccelVector = Vector3D.Zero;
								Acceleration = 0;
							}
							else
								Acceleration = AccelVector.Length() * Math.Sign(AccelVector.Dot(velocity));
						}

						Position = position;
						Velocity = velocity;
						_lastSpeed = Speed;
						Speed = newSpeed;
						LastUpdateTime = updateTime;
					}
					else
						Updated = false;
				}
				else
				{
					TargetID = id;
					LastUpdateTime = updateTime;
					Acceleration = 0;
					_lastSpeed = 0;

					Position = position;
					Velocity = Vector3D.IsZero(velocity, 0.01) ? Vector3D.Zero : velocity;
					Speed = Vector3D.IsZero(velocity, 0.01) ? 0 : velocity.Length();
					AccelVector = Vector3D.Zero;
					InterceptVector = Vector3D.Zero;
				}
			}

			public Vector3D CalculateInterceptVector(WorldInfo wInfo, out double timeToTarget)
			{
				Vector3D displacementVector = Position - wInfo.Position;

				_debug.Clear();

				// Set velocity to zero if missile is heading in opposite direction
				Vector3D missileVelocity;
				double mySpeed;
				if (wInfo.Velocity.Dot(displacementVector) < 0 && wInfo.Velocity.Dot(Velocity) < 0)
				{
					missileVelocity = Vector3D.Zero;
					mySpeed = 0;
				}
				else
				{
					missileVelocity = wInfo.Velocity;
					mySpeed = wInfo.Speed;
				}

				// If the target is stationary, skip the quartic and travel direct
				if (Vector3D.IsZero(Velocity, _eps))
				{
					// Time to max velocity: (Vf - Vi) / acceleration
					double speedDiff = maxSpeed - mySpeed;

					Vector3D dispAtMaxVelocity;
					double timeToMaxVelocity = 0;

					if (speedDiff > _eps)
					{
						timeToMaxVelocity = speedDiff / wInfo.Acceleration;

						if (!double.IsInfinity(timeToMaxVelocity) && !double.IsNaN(timeToMaxVelocity) && timeToMaxVelocity > _eps)
						{
							// Distance traveled to reach max velocity: d = Vi*t + (a*t^2)/2
							var distanceToMaxVelocity = (mySpeed * timeToMaxVelocity) + (0.5 * wInfo.Acceleration * (timeToMaxVelocity * timeToMaxVelocity));

							// Calculate distance traveled at max speed: total distance - distance to max velocity
							var dispVecLength = displacementVector.Normalize();
							double distRemaining = dispVecLength - distanceToMaxVelocity;
							dispAtMaxVelocity = displacementVector * distRemaining;
						}
						else
							dispAtMaxVelocity = displacementVector;
					}
					else
						dispAtMaxVelocity = displacementVector;

					timeToTarget = dispAtMaxVelocity.Length() / Math.Max(maxSpeed, mySpeed) + timeToMaxVelocity;

					if (DEBUG)
						_debug.Append($"Going Direct\n\nTime to Tgt: {timeToTarget}\n");

					InterceptVector = displacementVector;
					LastValidIntercept = displacementVector;
					return InterceptVector;
				}

				//// Without target acceleration
				//double a = 0.25 * (missileAccel * missileAccel);
				//double b = mySpeed * missileAccel;
				//double c = mySpeed * mySpeed - Velocity.LengthSquared();
				//double d = -2 * Velocity.Dot(displacementVector);
				//double e = -displacementVector.LengthSquared();

				//// With target acceleration
				double a = (0.25 * (wInfo.Acceleration * wInfo.Acceleration)) - (0.25 * (Acceleration * Acceleration));
				double b = (mySpeed * wInfo.Acceleration) - (Speed * Acceleration);
				double c = (mySpeed * mySpeed) - (Speed * Speed) - displacementVector.Dot(AccelVector);
				double d = -2 * Velocity.Dot(displacementVector);
				double e = -displacementVector.LengthSquared();

				if (DEBUG)
				{
					_debug.Append($"Target Velocity: {Speed}\n")
					  .Append($"Target Acceleration: {Acceleration}\n")
					  .Append($"My Velocity: {mySpeed}\n")
					  .Append($"My Acceleration: {wInfo.Acceleration}\n\n")
					  .Append($"EQ Variables:\n  a = {a}\n  b = {b}\n  c = {c}\n  d = {d}\n  e = {e}\n\n");
				}

				_solver.SolveQuartic(a, b, c, d, e, out timeToTarget);

				if (DEBUG)
					_debug.Append($"Time to target: {timeToTarget}\n");

				if (double.IsPositiveInfinity(timeToTarget))
				{
					if (DEBUG)
						_debug.Append($"Time is too big\n");

					if (!Vector3D.IsZero(LastValidIntercept))
					{
						InterceptVector = LastValidIntercept;

						if (DEBUG)
							_debug.Append($"Returning Last Valid\n");
					}
					else
					{
						InterceptVector = displacementVector + Velocity * _run;

						if (DEBUG)
							_debug.Append($"Going direct\n");
					}
					return InterceptVector;
				}

				InterceptVector = displacementVector + (Velocity * timeToTarget) + (0.5 * AccelVector * timeToTarget * timeToTarget);
				LastValidIntercept = InterceptVector;
				return InterceptVector;
			}
		}

		public static class VectorUtils
		{
			public static Vector3D ProjectVector(Vector3D a, Vector3D b)
			{
				// Project a onto b
				return a.Dot(b) / b.LengthSquared() * b;
			}

			public static double GetAngleBetween(Vector3D a, Vector3D b)
			{
				if (Vector3D.IsZero(a) || Vector3D.IsZero(b))
					return 0.0;

				// Clamped due to floating point errors
				if (Vector3D.IsUnit(ref a) && Vector3D.IsUnit(ref b))
					return Math.Acos(MathHelper.Clamp(a.Dot(b), -1, 1));

				return Math.Acos(MathHelper.Clamp(a.Dot(b) / Math.Sqrt(a.LengthSquared() * b.LengthSquared()), -1, 1));
			}

			public static Vector3D ReflectVector(Vector3D a, Vector3D b, double reflectionFactor = 5)
			{
				Vector3D proj_a = ProjectVector(a, b);
				Vector3D aOrth_b = a - proj_a;
				return (proj_a - aOrth_b * reflectionFactor);
			}
		}

		public class WorldInfo
		{
			IMyShipController _controller;
			public Vector3D Position;
			public Vector3D Gravity;
			public Vector3D Velocity;
			public Vector3D AccelVector;
			public Vector3D Forward => _controller.WorldMatrix.Forward;
			public Vector3D Up => _controller.WorldMatrix.Up;
			public double Altitude;
			public double Speed;
			public double Acceleration;
			public bool InGravity;

			public WorldInfo(IMyShipController shipController)
			{
				_controller = shipController;
				Update();
			}

			public void Update()
			{
				var newVelocity = _controller.GetShipVelocities().LinearVelocity;

				if (Vector3D.IsZero(newVelocity, 0.01) || Vector3D.IsZero(newVelocity - Velocity, 0.01) || newVelocity.LengthSquared() >= maxSpeed * maxSpeed)
				{
					AccelVector = Vector3D.Zero;
					Acceleration = 0;
				}
				else
				{
					AccelVector = (newVelocity - Velocity) / _run;
					Acceleration = Vector3D.IsZero(AccelVector, 0.01) ? 0 : AccelVector.Length();
				}

				Position = _controller.CubeGrid.WorldVolume.Center;
				Gravity = _controller.GetNaturalGravity();
				Velocity = Vector3D.IsZero(newVelocity, 0.01) ? Vector3D.Zero : newVelocity;
				Speed = Vector3D.IsZero(Velocity) ? 0 : Velocity.Length();
				InGravity = Gravity.LengthSquared() > 0;

				if (!InGravity)
					Altitude = 0;
				else
				{
					_controller.TryGetPlanetElevation(MyPlanetElevation.Surface, out Altitude);
					if (double.IsInfinity(Altitude))
						Altitude = 0;
				}
			}
		}

	}
}
