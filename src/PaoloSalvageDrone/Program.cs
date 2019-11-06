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
			DisplayStatus();
			DisplayDetected();

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

		///
	}
}
