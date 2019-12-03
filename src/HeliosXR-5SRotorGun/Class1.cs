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


		// Lightweight Rotor Displacement Gun Program v4.0 
		// edited by jonathan for the Helios XR-5S

		//         Accelerators        Generators  
		// (-ship-)[-]{[-]{[-]{[-]{[-]{[-]  

		// [-]{ <-- this is a rotor  

		// Guns with 4-6 acceleration rotors supported by default.  

		// -- BASIC SETTINGS -- //  
		string designator = "Rotor Lance";                            // prefix for this gun  
		string acceleratorGroupName = " - Accelerators";   // suffix for acceleration rotors  
		string generatorGroupName = " - Generators";        // suffix for generation rotors  
		bool safeMode = true;                                              // activate safety checks  

		// -- ADVANCED SETTINGS -- //  
		int salvoLengthLarge = 6;
		int salvoLengthSmall = 4;
		int cooldown = 0;

		////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////  
		// -- SCRIPT BODY - ALTER AT OWN RISK -- //  
		int firingTicks = 0;
		int salvoRounds = 0;
		bool fullAuto = false;
		bool smallGrid = false;

		bool initialised = false;

		List<IMyMotorStator> accelerators = new List<IMyMotorStator>();
		List<IMyMotorStator> generators = new List<IMyMotorStator>();

		float RetractionDistance = -0.4f;
		float ExtensionDistance = 0.2f;
		int salvoLength() { return smallGrid ? salvoLengthSmall : salvoLengthLarge; }

		public Program() { Runtime.UpdateFrequency = UpdateFrequency.Update1; }

		void Init()
		{
			GridTerminalSystem.GetBlockGroupWithName(designator + acceleratorGroupName)?.GetBlocksOfType<IMyMotorStator>(accelerators);
			if (!accelerators.Any()) throw new Exception($"Error: No rotors in group {designator}{acceleratorGroupName}");

			GridTerminalSystem.GetBlockGroupWithName(designator + generatorGroupName)?.GetBlocksOfType<IMyMotorStator>(generators);
			if (!generators.Any()) throw new Exception($"Error: No rotors in group {designator}{generatorGroupName}");

			for (int i = 0; i < accelerators.Count; i++)
			{
				if (safeMode)
				{
					if (accelerators[i].CubeGrid.GridSizeEnum == VRage.Game.MyCubeSize.Small) smallGrid = true;
				}
				accelerators[i].SetValue("Displacement", ExtensionDistance);
			}
			initialised = true;

		}

		void ArgumentHandler(string args)
		{
			args = args.ToLower();

			if (args == "fire") Fire();
			if (args == "salvo" && salvoRounds <= 0)
			{
				salvoRounds = salvoLength();
				Fire();
			}
			if (args == "auto")
			{
				fullAuto = !fullAuto;
				Fire();
			}

		}

		void Fire()
		{
			if (firingTicks == 0) firingTicks = 7 + cooldown + (smallGrid ? 1 : 0);
			if (salvoRounds > 0) salvoRounds--;
		}

		void ConsoleOutput()
		{

			Echo($"Displacement gun '{designator}' active...{RunningSymbol()}");
			Echo($"Accelerators: {accelerators.Count.ToString()}");
			Echo($"Generators: {generators.Count.ToString()}");
			Echo("");

		}

		void FiringProcess()
		{
			switch (firingTicks)
			{
				case 7:
				case 6:
				case 5:
				case 4:
				case 3:
					foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", accelerator.GetValue<float>("Displacement") + ((RetractionDistance - ExtensionDistance) / 5));
					Echo("[ FIRING ]");
					break;
				case 2:
					foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", ExtensionDistance);
					foreach (IMyMotorStator generator in generators) generator.GetActionWithName("Add Top Part").Apply(generator);
					Echo("[ FIRING ]");
					break;
				case 1:
					foreach (IMyMotorStator generator in generators) { generator.GetActionWithName("Detach").Apply(generator); }
					Echo("[ FIRING ]");
					break;
				case 0:
					Echo("[ IDLE ]");
					break;
				default:
					Echo("[ COOLING ]");
					break;
			}

			if (firingTicks > 0) firingTicks--;
			else if (salvoRounds > 0 || fullAuto) Fire();
		}

		public void Main(string arguments)
		{
			if (!initialised) Init();
			if (arguments != "")
			{
				ArgumentHandler(arguments);
				return;
			}
			ConsoleOutput();
			FiringProcess();

		}

		// Whip's Running Symbol Method v7  
		int symIndex = 0;
		string[] strRunningSymbol = { " | ", " / ", "-- ", " \\ " };
		string RunningSymbol()
		{
			symIndex += (symIndex < 3) ? 1 : -3;
			return strRunningSymbol[symIndex];
		}


	}
}
