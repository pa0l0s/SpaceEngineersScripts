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

		/// <summary>
		/// ////////////////
		/// </summary>
		/// TODO:
		/// - Select closest target from detected
		/// - Go back to station when cargo full
		/// - Manage grider on off
		/// - manage speed depand on distance


		Rotator rotator;

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


			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			rotator = new Rotator(this);

			GridTerminalSystem.GetBlocks(list); //get at start
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

		string[] statusChars = new string[] { "/", "-", @"\", "|" };
		int statusCharPosition = 0;

		List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();

		void Main(string argument)
		{
			rotator.DoTask();
			//DisplayStatus();
			//DisplayDetected();

			//inventory.Owner.EntityId).CubeGrid.EntityId == _me.CubeGrid.EntityId

			var missleElements = new List<IMyTerminalBlock>();



			missleElements = list.Where(x => x.CubeGrid.EntityId != Me.CubeGrid.EntityId).ToList();

			missleElements.ForEach(y => this.Echo(y.DisplayNameText));


		}

		void DisplayDetected()
		{
			Echo("DisplayDetected");
			GridTerminalSystem.GetBlocksOfType<IMySensorBlock>(list);
			if (list.Count > 0)
			{
				Echo("found sensor...");
				var sensor = list[0] as IMySensorBlock;

				Echo(sensor.DisplayNameText);


				var lastDetected = sensor.LastDetectedEntity;

				if (!lastDetected.IsEmpty())
				{
					var sb = new StringBuilder();
					sb.AppendLine(lastDetected.EntityId.ToString());
					sb.AppendLine(lastDetected.Name);
					sb.AppendLine(lastDetected.Type.ToString());
					sb.AppendLine(lastDetected.Relationship.ToString());
					sb.AppendLine(lastDetected.TimeStamp.ToString());
					sb.AppendLine(lastDetected.Position.ToString());

					Echo(sb.ToString());

					GoToPosition(lastDetected);
				}
				else
				{
					Echo("Last detected empty");
				}

			}
			else
			{
				Echo("Sensor not found.");
			}

			Echo("DisplayDetected end");

		}

		void GoToPosition(MyDetectedEntityInfo lastDetected)
		{
			GoToPosition(lastDetected.Position, lastDetected.Name);
		}
		void GoToPosition(Vector3D position, string waypointName="")
		{
			list = new List<IMyTerminalBlock>();

			GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(list);
			if (list.Count > 0)
			{
				Echo("Found remote");
				var remote = list[0] as IMyRemoteControl;

				Echo(remote.DisplayNameText);

				remote.ClearWaypoints();
				remote.AddWaypoint(position, "Origin");
				remote.SetAutoPilotEnabled(true);
			}
		}

		void Travel(string argument)
		{
			Vector3D origin = new Vector3D(0, 0, 0);
			if (argument == null || argument == "")
			{
				Echo("None");
			}
			else if (argument.ToLower() == "go")
			{
				Echo("Go");
				GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(list);
				if (list.Count > 0)
				{

					var remote = list[0] as IMyRemoteControl;
					origin = remote.GetPosition() + (300000 * remote.WorldMatrix.Forward);
					remote.ClearWaypoints();
					remote.AddWaypoint(origin, "Origin");
					remote.SetAutoPilotEnabled(true);
				}
			}
			else if (argument.ToLower() == "stop")
			{
				Echo("Stop");
				GridTerminalSystem.GetBlocksOfType<IMyRemoteControl>(list);
				if (list.Count > 0)
				{
					var remote = list[0] as IMyRemoteControl;
					remote.ClearWaypoints();
					remote.SetAutoPilotEnabled(false);
				}
			}
		}

		void DisplayStatus()
		{
			this.Echo(statusChars[statusCharPosition]);
			statusCharPosition++;
			if (statusCharPosition >= statusChars.Length)
			{
				statusCharPosition = 0;
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


		public class HydrogenMissle : IMissle
		{
			private List<IMyTerminalBlock> _missleElements;
			public HydrogenMissle(List<IMyTerminalBlock> missleElements)
			{
				_missleElements = missleElements;
			}

			public bool Initialize()
			{
				throw new NotImplementedException();
			}

			public bool IsValid()
			{
				throw new NotImplementedException();
			}

			public bool Lunch()
			{
				throw new NotImplementedException();
			}

			public bool Validate()
			{
				throw new NotImplementedException();
			}
		}

		public class SeparateGridMissleSelector: IMissleSelector
		{
			private Program _program;
			private List<IMyTerminalBlock> _misslesElements;

			public SeparateGridMissleSelector(Program program, List<IMyTerminalBlock> misslesElements)
			{
				_program = program;
				_misslesElements = misslesElements;
			}

			List<HydrogenMissle> IMissleSelector.GetMissles()
			{
				List<HydrogenMissle> missles = new List<HydrogenMissle>();
				List<long> CubeIds = _misslesElements.Select(x => x.CubeGrid.EntityId).Distinct().Where(y=>y!= _program.Me.CubeGrid.EntityId).ToList();

				foreach (var missleCubeId in CubeIds)
				{
					var missleCubeIdElements = _misslesElements.Where(x => x.CubeGrid.EntityId == missleCubeId).ToList();

					//get hydrogen engines

					var missle = new HydrogenMissle(missleCubeIdElements);
					if(missle.IsValid())
					{
						missles.Add(missle);
					}
					
				}


				return missles;
			}
		}

		//public static class MissleBlockHelper
		//{
		//	public static List<IMyTerminalBlock> GetHydrogenEngines(List<IMyTerminalBlock> blocks)
		//	{
		//		return blocks.Where(x=>x.GetType() )
		//	}
		//}

		public interface IMissle
		{
			bool Validate();

			bool IsValid();

			bool Initialize();

			bool Lunch();
		}

		public interface IMissleSelector
		{
			List<HydrogenMissle> GetMissles();
		}

		//public interface IMissleBlockHelper
		//{
		//	List<IMyTerminalBlock> GetHydrogenEngines(List<IMyTerminalBlock> blocks);
		//}


		///
	}
}
