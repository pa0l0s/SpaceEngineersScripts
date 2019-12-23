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
		/// to kopiuje do bloku programowalnego
		/// </summary>
		/// 

		///Zmienne globalne muszą być tu. albo opakonwane w jakiś obiekt(bardziej skomplikowane musisz znać obiektowe programowanie)
		///

		List<IMyBatteryBlock> baterie = new List<IMyBatteryBlock>();
		List<IMyThrust> silniki = new List<IMyThrust>();
		List<IMyGyro> gyros = new List<IMyGyro>();
		List<IMyRemoteControl> remoteControllers = new List<IMyRemoteControl>();

		public Program()

		{

			/// To sie odpala jak zrobisz Recompile, wtedy trzeba pobrać obiekty z grida
			/// 

			GridTerminalSystem.GetBlocksOfType(baterie); // pobiera z grida wszystkie baterie
			GridTerminalSystem.GetBlocksOfType(silniki); // pobiera z grida wszystkie silniki
			GridTerminalSystem.GetBlocksOfType(gyros); // pobiera z grida wszystkie gyro
			GridTerminalSystem.GetBlocksOfType(remoteControllers); // pobiera z grida wszystkie remote controllers

			//teraz wybiore tylko te które w nazwie mają "#A#"

			baterie = baterie.Where(bateria => bateria.DisplayNameText.Contains("#A#")).ToList();
			silniki = silniki.Where(bateria => bateria.DisplayNameText.Contains("#A#")).ToList();
			gyros = gyros.Where(bateria => bateria.DisplayNameText.Contains("#A#")).ToList();
			remoteControllers = remoteControllers.Where(remote => remote.DisplayNameText.Contains("#A#")).ToList();

			///teraz mamy 3 listy obiektów rakiety

		}

		public void Main(string argument, UpdateType updateSource)

		{
			///mozemy teraz w main korzystać ze stworzonych list nawet jak odłaczymy rakiete
			foreach (var bateria in baterie)
			{
				bateria.ApplyAction("OnOff_On");
			}

			foreach (var silnik in silniki)
			{
				silnik.ThrustOverridePercentage = 0.9f;

			}

			foreach (var gyro in gyros)
			{
				gyro.GyroOverride = true; // włancza ręczną kontrole gyroskopu
				gyro.Pitch = 10f; // każe gyto obrócić sie o 10 stopni w osi Pitch czyli w poziomie.
			}


			var jednoGyro = gyros.First(); // wybiera z listy gyros pierwsze gyro i zapisuje je do zmiennej jednoGyro
			jednoGyro.GyroOverride = true; // włancza ręczną kontrole gyroskopu
			jednoGyro.Pitch = 10f; // każe gyto obrócić sie o 10 stopni w osi Pitch czyli w poziomie.


			//sterowanie autopilotem
			var remoteController = remoteControllers.First(); //bierzemy pierwszy remote z listy (i tak powinien byc tylko jeden ale SE zawsze operuje na listach.)
			var kierunek = remoteController.GetPosition() + (1000 * remoteController.WorldMatrix.Forward); //pobiera pozycje kontrolera i dodaje do niej 1000 do przodu to chyba w metrach jest
			remoteController.AddWaypoint(kierunek, "DoPrzodu1km");
			remoteController.SetAutoPilotEnabled(true);

		}

		///tu koniec kopiowania
	}
}
