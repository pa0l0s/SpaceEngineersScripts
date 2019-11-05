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

namespace ScriptingClass
{
    public class Program : MyGridProgram
	{
		IMyMotorStator rotor;
		IMyProgrammableBlock me;
		IMyTimerBlock timer;
		List<RotorControlData> rotorsControl;

		string[] statusChars = new[] { "|", "/", "-", @"\" };
		int statusPos;

		float gain = 0.5f;
		float margin = 5f;

		public class RotorControlData
		{
			public string RotorNameFromArg { get; set; }
			public IMyMotorStator RotorObject { get; set; }
			public float TargetAngle { get; set; }
			public string RotorCommand { get; set; }
			public float AngleRenge { get; set; }
			public bool Finished { get; set; }
		}

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

			rotor = GridTerminalSystem.GetBlockWithName("Rotor spotligt") as IMyMotorStator;
			me = GridTerminalSystem.GetBlockWithName("Programmable block Spotlight") as IMyProgrammableBlock;
			timer = GridTerminalSystem.GetBlockWithName("Timer Block spotlight") as IMyTimerBlock;
			//timer.ApplyAction("Start");

			rotorsControl = new List<RotorControlData>();
			statusPos = 0;
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

			StatusChar();

			ParseArgument(argument);

			MoveRotors();

			//timer.ApplyAction("Start");

			DisplayRotrosControlList();

			CleanUpControlList();

		}

		public void ParseArgument(string argument)
		{
			//Argument decode
			string[] args = argument.Split(' ');
			if (args.Length == 2)
			{
				var rotorName = args[0].Trim();
				var rotorTargetAngle = args[1];

				string output = $"rotorName: {rotorName},  rotorTargetAngle: {rotorTargetAngle}";
				Echo(output);

				var _rotor = GridTerminalSystem.GetBlockWithName(rotorName) as IMyMotorStator;
				if (_rotor == null)
				{
					Echo("error no rotor from arg.");
				}
				else
				{
					float rotorTargetAngleF;
					bool isNumeric = Single.TryParse(rotorTargetAngle, out rotorTargetAngleF);
					if (!isNumeric)
					{
						Echo("rotorTargetAngle is not numeric.");
					}
					else
					{
						RotorControlData rotorControl = new RotorControlData() { RotorNameFromArg = rotorName, RotorObject = _rotor, TargetAngle = rotorTargetAngleF, Finished = false };

						var existing = rotorsControl.Where(x => x.RotorNameFromArg == rotorName).SingleOrDefault();

						if (existing != null)
						{
							rotorsControl.Remove(existing);
						}

						rotorsControl.Add(rotorControl);
					}
				}


			}
		}

		public void MoveRotors()
		{
			foreach (var rotorControl in rotorsControl)
			{
				if (!rotorControl.Finished)
					MoveMotor(rotorControl);
				//rotorsControl.Remove(rotorControl);
			}
		}

		public void MoveMotor(RotorControlData rotorControl)
		{
			if (rotorControl != null)
			{
				var rotor = rotorControl.RotorObject;
				var targetAngleD = rotorControl.TargetAngle;

				if (rotor != null)
				{
					float angleD = radiansToDegre(rotor.Angle);

					if (angleD - targetAngleD < -margin)
					{
						rotor.SetValueFloat("Velocity", gain);
						Echo("+1");
					}
					else if (angleD - targetAngleD > margin)
					{
						rotor.SetValueFloat("Velocity", gain * -1f);

						Echo("-1");
					}
					else
					{
						rotor.SetValueFloat("Velocity", 0f);
						//lose rotor control

						rotorControl.Finished = true;
						Echo("0");
					}
				}
			}
		}

		public void DisplayRotrosControlList()
		{
			StringBuilder sb = new StringBuilder();
			foreach (var rotorControl in rotorsControl)
			{
				if (rotorControl != null)
				{
					if (!rotorControl.Finished)
					{
						string output = $"Rotor: {rotorControl.RotorObject}, currentAngle: {radiansToDegre(rotorControl.RotorObject.Angle)}, targetAngle {rotorControl.TargetAngle}, rotorCommand:{rotorControl.RotorCommand}, angleRenge:{rotorControl.AngleRenge} ";
						sb.Append(output);
					}
				}
			}


			Echo(sb.ToString());
		}

		public void CleanUpControlList()
		{
			if (rotorsControl.Count > 0)
			{
				if (rotorsControl[0].Finished)
				{
					rotorsControl.RemoveAt(0);
				}
			}
		}

		public void StatusChar()
		{
			Echo(statusChars[statusPos]);
			statusPos++;
			if (statusPos >= statusChars.Length)
			{
				statusPos = 0;
			}
		}

		public float radiansToDegre(float radians)
		{
			return radians / (float)Math.PI * 180f;
		}



	}
}
