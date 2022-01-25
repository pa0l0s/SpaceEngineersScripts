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
		/// 1. 4 Rotory w grupie "Rotor Lance - Accelerators"
		/// 2. 1 Ostatni rotor w grupie "Rotor Lance - Generators"
		/// </summary>
		// -- BASIC SETTINGS -- //  
		string designator = "Rotor Lance";                            // prefix for this gun  
		string acceleratorGroupName = " - Accelerators";   // suffix for acceleration rotors  
		string generatorGroupName = " - Generators";        // suffix for generation rotors  

		List<IMyMotorStator> accelerators = new List<IMyMotorStator>();
		List<IMyMotorStator> generators = new List<IMyMotorStator>();

		float RetractionDistance = -0.4f;
		float ExtensionDistance = 0.2f;

		int cykl = 0;

		public Program()

		{
			GridTerminalSystem.GetBlockGroupWithName(designator + acceleratorGroupName)?.GetBlocksOfType<IMyMotorStator>(accelerators);
			if (!accelerators.Any()) throw new Exception($"Error: No rotors in group {designator}{acceleratorGroupName}");

			GridTerminalSystem.GetBlockGroupWithName(designator + generatorGroupName)?.GetBlocksOfType<IMyMotorStator>(generators);
			if (!generators.Any()) throw new Exception($"Error: No rotors in group {designator}{generatorGroupName}");

			for (int i = 0; i < accelerators.Count; i++)
			{
				accelerators[i].SetValue("Displacement", ExtensionDistance);
			}
		}

		public void Main(string argument, UpdateType updateSource)

		{
			FireRotors();
		}

		void FireRotors()
		{
			try
			{
				switch (cykl)
				{
					case 0:
						foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", RetractionDistance);
						this.Runtime.UpdateFrequency = UpdateFrequency.Update1;
						break;
					case 1:
						foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", ExtensionDistance);
						//foreach (IMyMotorStator generator in generators) generator.GetActionWithName("Add Top Part").Apply(generator);
						//foreach (IMyMotorStator generator in generators) generator.Attach();
						foreach (IMyMotorStator generator in generators) { generator.Detach(); }
						break;
					case 2:
						//foreach (IMyMotorStator generator in generators) { generator.GetActionWithName("Detach").Apply(generator); }
						foreach (IMyMotorStator generator in generators) { generator.Detach(); }
						this.Runtime.UpdateFrequency = UpdateFrequency.None;
						break;
					case 3:

						break;
				}
				cykl++;
				if (cykl >= 3)
				{
					cykl = 0;
				}
			}
			catch (Exception ex)
            {
				Echo(ex.ToString());
            }
		}

	}
}
