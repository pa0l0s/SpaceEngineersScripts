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
			var orePositions = new List<OrePosition>();

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

			// The main entry point of the script, invoked every time
			// one of the programmable block's Run actions are invoked,
			// or the script updates itself. The updateSource argument
			// describes where the update came from.
			// 
			// The method itself is required, but the arguments above
			// can be removed if not needed.

		}


		class OrePosition
		{
			public string OreName { get; set; }
			public Vector3D Position { get; set; }
		}

	}
}
