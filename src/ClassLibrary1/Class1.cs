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
		/// to kopiuje do bloku programowalnego
		/// </summary>
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



		public void Main(string argument, UpdateType updateSource)

		{

			List<IMyBatteryBlock> baterie = new List<IMyBatteryBlock>();
			GridTerminalSystem.GetBlocksOfType(baterie);

			foreach (var bateria in baterie)
			{
				bateria.ApplyAction("OnOff_On");
			}
		}

		///tu koniec kopiowania
	}
}
