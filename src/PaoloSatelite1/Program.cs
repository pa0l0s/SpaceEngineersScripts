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



		List<IMyTerminalBlock> list = new List<IMyTerminalBlock>();
		void Main(string argument)
		{
			Vector3D origin = new Vector3D(0, 0, 0);
			if (argument == null || argument == "")
			{
				Echo("None");
			}
			else if (argument.ToLower()=="go")
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



		///
	}
}
