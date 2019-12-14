using System;
using System.Collections.Generic;
using System.Linq;
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

namespace ScriptingClass
{
	public class Program : MyGridProgram
	{




		TurretControl turretControl;
		public Program()

		{

			// The constructor, called only once every session and
			// always before any other method is called. Use it to
			// initialize your script. 
			//     
			// The constructor is optional and can be removed if not
			// needed.
			// 
			// It's recommended to set RuntimeInfo.UpdateFrequency 
			// here, which will allow your script to run itself without a 
			// timer block.

			turretControl = new TurretControl(this, Me);
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

		public void Main(string argument, UpdateType updateSource)

		{

			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked,
			// or the script updates itself. The updateSource argument
			// describes where the update came from.
			// 
			// The method itself is required, but the arguments above
			// can be removed if not needed.


			turretControl.Run();

			turretControl.Aim900("GPS:Large Grid:157759.727203728:231485.308988417:5714590.54597619:");

		}

		public class TurretControl
		{
			private Program _program;
			private IMyProgrammableBlock _me;
			private IMyCockpit _cockpit;
			private IMyLargeTurretBase _turret900;

			public TurretControl(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;

				var cockpits = new List<IMyCockpit>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyCockpit>(cockpits);

				_cockpit = cockpits.FirstOrDefault();
			}

			public void Run()
			{
				var turrets = new List<IMyLargeTurretBase>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyLargeTurretBase>(turrets);

				foreach (var turret in turrets)
				{
					_program.Echo(turret.DisplayNameText);
				}


				var turret900 = turrets.FirstOrDefault(t => t.DisplayNameText.ToLower().Contains("900"));

				_program.Echo($"{turret900.Azimuth}");
				turret900.Azimuth = 100;
				turret900.SyncAzimuth();

				_turret900 = turret900;
			}
			//GPS:Large Grid:157759.727203728:231485.308988417:5714590.54597619:
			//GPS:Thorium Lake:-249471.93:-166022.37:11030192.43:
			public void Aim900(string GPSToAimAt)
			{
				var targetVector = new Vector3D();

				if(TryParseVector3D(GPSToAimAt,out targetVector))
				{
					_program.Echo($"{targetVector}");
				}
				else
				{
					throw new Exception("Invalid GPS data");
				}

				_turret900.SetTarget(targetVector);

				var cockpitVector = _cockpit.GetPosition();

				_program.Echo($"{cockpitVector}");
			}

			public bool TryParseVector3D(string vectorString, out Vector3D vector)
			{
				vector = new Vector3D(0, 0, 0);

				vectorString = vectorString.Replace(" ", "").Replace("{", "").Replace("}", "").Replace("X", "").Replace("Y", "").Replace("Z", "");
				var vectorStringSplit = vectorString.Split(':');

				double x, y, z;

				if (vectorStringSplit.Length < 5)
					return false;

				bool passX = double.TryParse(vectorStringSplit[2], out x);
				bool passY = double.TryParse(vectorStringSplit[3], out y);
				bool passZ = double.TryParse(vectorStringSplit[4], out z);

				if (passX && passY && passZ)
				{
					vector = new Vector3D(x, y, z);
					return true;
				}
				else
					return false;
			}
		}


	}
}
