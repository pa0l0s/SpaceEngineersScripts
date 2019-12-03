using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Game;
using VRage.Library;
using System.Text;
using System.Linq;
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

		GravEngine engine;
		private Rotator _rotator;
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


			Runtime.UpdateFrequency = UpdateFrequency.Update1;

			engine = new GravEngine(this);
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



		public void Main(string argument, UpdateType updateSource)

		{

			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked,
			// or the script updates itself. The updateSource argument
			// describes where the update came from.
			// 
			// The method itself is required, but the arguments above
			// can be removed if not needed.

			//_rotator.DoTask();
			engine.MassControl();

		}

		public class GravEngine
		{

			private const string forwardNameTag = "forward";
			private const string backwardNameTag = "back";
			private const string leftNameTag = "left";
			private const string rightNameTag = "right";
			private const string upNameTag = "up";
			private const string downNameTag = "down";

			List<IMyThrust> forwardThrusters;
			List<IMyThrust> backThrusters;
			List<IMyThrust> leftThrusters;
			List<IMyThrust> rightThrusters;
			List<IMyThrust> upThrusters;
			List<IMyThrust> downThrusters;

			List<IMyArtificialMassBlock> forwardMass;
			List<IMyArtificialMassBlock> backMass;
			List<IMyArtificialMassBlock> leftMass;
			List<IMyArtificialMassBlock> rightMass;
			List<IMyArtificialMassBlock> upMass;
			List<IMyArtificialMassBlock> downMass;

			IMyTerminalBlock reference;

			Program _program;
			public GravEngine(Program program)
			{
				_program = program;

				var blocksCockpits = new List<IMyCockpit>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyCockpit>(blocksCockpits);

				reference = blocksCockpits.FirstOrDefault();

				if(reference==null)
				{
					var blocksRemotes = new List<IMyRemoteControl>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(blocksRemotes);

					 reference = blocksRemotes.FirstOrDefault();

					if (reference == null)
					{
						throw new Exception("No reference cockpit found");
					}
				}

				var thrusters = new List<IMyThrust>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyThrust>(thrusters);

				var thrustersOrganized = OrganizeThrusters(thrusters, reference);

				rightThrusters = thrustersOrganized[0];

				leftThrusters = thrustersOrganized[1];
				upThrusters = thrustersOrganized[2];
				downThrusters = thrustersOrganized[3];
				backThrusters = thrustersOrganized[4];
				forwardThrusters = thrustersOrganized[5];

				_program.Echo($"Right Thrusters : {rightThrusters.Count}");
				_program.Echo($"Left Thrusters : {leftThrusters.Count}");
				_program.Echo($"Up Thrusters : {upThrusters.Count}");
				_program.Echo($"Down Thrusters : {downThrusters.Count}");
				_program.Echo($"Forward Thrusters : {forwardThrusters.Count}");
				_program.Echo($"Back Thrusters : {backThrusters.Count}");

				var mass = new List<IMyArtificialMassBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyArtificialMassBlock>(mass);

				forwardMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(forwardNameTag)).ToList();
				backMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(backwardNameTag)).ToList();
				leftMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(leftNameTag)).ToList();
				rightMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(rightNameTag)).ToList();
				upMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(upNameTag)).ToList();
				downMass = mass.Where(x => x.DisplayNameText.ToLower().Contains(downNameTag)).ToList();

				_program.Echo($"Right Mass : {rightMass.Count}");
				_program.Echo($"Left Mass : {leftMass.Count}");
				_program.Echo($"Up Mass : {upMass.Count}");
				_program.Echo($"Down Mass : {downMass.Count}");
				_program.Echo($"Forward Mass : {forwardMass.Count}");
				_program.Echo($"Back Mass : {backMass.Count}");

			}

			public void MassControl()
			{
				foreach (var thrust in forwardThrusters)
				{
					if(thrust.CurrentThrust>1)
					{
						TurnOnMassList(forwardMass);
						break;
					}

					TurnOffMassList(forwardMass);
				}
				foreach (var thrust in backThrusters)
				{
					if (thrust.CurrentThrust > 1)
					{
						TurnOnMassList(backMass);
						break;
					}

					TurnOffMassList(backMass);
				}
				foreach (var thrust in upThrusters)
				{
					if (thrust.CurrentThrust > 1)
					{
						TurnOnMassList(upMass);
						break;
					}

					TurnOffMassList(upMass);
				}
				foreach (var thrust in downThrusters)
				{
					if (thrust.CurrentThrust > 1)
					{
						TurnOnMassList(downMass);
						break;
					}

					TurnOffMassList(downMass);
				}
				foreach (var thrust in leftThrusters)
				{
					if (thrust.CurrentThrust > 1)
					{
						TurnOnMassList(leftMass);
						break;
					}

					TurnOffMassList(leftMass);
				}
				foreach (var thrust in rightThrusters)
				{
					if (thrust.CurrentThrust > 1)
					{
						TurnOnMassList(rightMass);
						break;
					}

					TurnOffMassList(rightMass);
				}
			}

			private void TurnOnMassList(IEnumerable<IMyArtificialMassBlock> mass)
			{
				foreach (var massBlock in mass)
				{
					massBlock.ApplyAction("OnOff_On");
				}
			}

			private void TurnOffMassList(IEnumerable<IMyArtificialMassBlock> mass)
			{
				foreach (var massBlock in mass)
				{
					massBlock.ApplyAction("OnOff_Off");
				}
			}

			/// <summary>
			/// The indices for the lists are: 0) Right, 1) Left, 2) Up, 3) Down, 4) Backward and 5) Forward, with respect to the reference block provided.

			/// </summary>
			/// <param name="thrusters"></param>
			/// <param name="reference"></param>
			/// <returns></returns>
			List<IMyThrust>[] OrganizeThrusters(List<IMyThrust> thrusters, IMyTerminalBlock reference)
			{

				Matrix refm;
				reference.Orientation.GetMatrix(out refm);

				var org = new List<IMyThrust>[6];
				for (int dir = 0; dir < 6; ++dir) org[dir] = new List<IMyThrust>();
				for (int i = 0; i < thrusters.Count; ++i)
				{
					Matrix bmat;
					thrusters[i].Orientation.GetMatrix(out bmat);
					bmat = bmat * Matrix.Transpose(refm);
					int dir = (int)bmat.Forward.Dot(new Vector3(1, 2, 3));
					dir = (2 * Math.Abs(dir) - 2) + (Math.Sign(dir) + 1) / 2;
					org[dir].Add(thrusters[i] as IMyThrust);


				}
				return org;
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



		/////
		///

	}
}
