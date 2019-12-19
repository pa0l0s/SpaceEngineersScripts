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

			if (argument.ToLower().StartsWith("gps:"))
			{
				SetTarget(argument);
			}
			else if (argument.ToLower().StartsWith("unlock"))
			{
				UnlockTarget();
			}
			else
			{
				FireRotor();
			}


		}

		private void UnlockTarget()
		{
			List<IMyLargeTurretBase> turrets = new List<IMyLargeTurretBase>();
			this.GridTerminalSystem.GetBlocksOfType(turrets);

			foreach (var turret in turrets)
			{
				//turret.TrackTarget
				//turret.AIEnabled = true;

				turret.ResetTargetingToDefault();
			}
		}

		private void SetTarget(string argument)
		{
			Vector3D target;

			if (TryParseVector3D(argument, out target))
			{


				List<IMyLargeTurretBase> turrets = new List<IMyLargeTurretBase>();
				this.GridTerminalSystem.GetBlocksOfType(turrets);

				foreach (var turret in turrets)
				{
					turret.SetTarget(target);

					Echo($"Turret: {turret.DisplayNameText} AI {turret.AIEnabled}");
				}
			}
		}

		void FireRotor()
		{
			switch (cykl)
			{
				case 0:
					foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", RetractionDistance);
					this.Runtime.UpdateFrequency = UpdateFrequency.Update1;
					break;
				case 1:
					foreach (IMyMotorStator accelerator in accelerators) accelerator.SetValue("Displacement", ExtensionDistance);
					foreach (IMyMotorStator generator in generators) generator.GetActionWithName("Add Top Part").Apply(generator);
					break;
				case 2:
					foreach (IMyMotorStator generator in generators) { generator.GetActionWithName("Detach").Apply(generator); }
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

		//GPS:Paolo #1:-113373.03:88155.88:81373.39:
		//GPS:Large Grid:157759.727203728:231485.308988417:5714590.54597619:
		//GPS:Thorium Lake:-249471.93:-166022.37:11030192.43:
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
