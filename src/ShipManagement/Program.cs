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

            // Set the update frequency to every 10 seconds
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
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

            if (argument == "SETNAMES")
            {
                Echo($"Phase {ShortGridName}. will be added in front of functional blocks.");
                SetNames(NamingFunctionSetNames);
            }
            else if (argument == "UNSETNAMES")
            {
                Echo($"Remove front namepart with dot for functional blocks.");
                SetNames(NamingFunctionUnsetNames);
            }
            else if (argument == "SETHOME")
            {
                SetHome();
            }
            else if (argument == "SETAI")
            {
                SetAi();
            }
            else
            {
                ShipManagement();
            }

        }

        private void SetAi()
        {
            try
            {
                var aiBlocks = new List<IMyTerminalBlock>();
                GridTerminalSystem.SearchBlocksOfName("SWFISD2.AI Recorder (Task) Dock",aiBlocks);
                if(aiBlocks.Count < 1) { throw new Exception("No \"SWFISD2.AI Recorder (Task) Dock\" block"); }
                var aiBlock = aiBlocks.FirstOrDefault();
                Echo($"{aiBlock.GetType()}");

                var recorders = new List<IMyPathRecorderBlock>();
                GridTerminalSystem.GetBlocksOfType<IMyPathRecorderBlock>(recorders);

                //throw new NotImplementedException();
            }
            catch(Exception ex) {
                Echo($"Error in SetAi: {ex.Message}");
            }
        }

        private string _shortGridName;

        public string ShortGridName
        {
            get
            {
                if (string.IsNullOrEmpty(_shortGridName))
                {
                    _shortGridName = GetShortGridName();
                }

                return _shortGridName;
            }
        }

        private void ShipManagement()
        {
            // Get the connector block
            var connector = GetConnector("Dock");
            if (connector == null)
            {
                Echo("Connector not found");
                return;
            }
            //Echo($"Connector found {connector.CustomName}");

            // Get all the battery blocks on the grid
            var batteries = new List<IMyBatteryBlock>();
            GridTerminalSystem.GetBlocksOfType(batteries);
            batteries = batteries.Where(x => x.IsSameConstructAs(Me)).ToList();
            batteries.RemoveAt(0); //One battery remain ucontrolled to support programming block work

            // Check if the connector is locked
            if (connector.Status == MyShipConnectorStatus.Connected)
            {
                // Set the charge mode to "Recharge" if any battery has a level below 90%
                foreach (var battery in batteries)
                {
                    if (battery.CurrentStoredPower / battery.MaxStoredPower < 0.9)
                    {
                        battery.ChargeMode = ChargeMode.Recharge;
                    }
                    else if (battery.CurrentStoredPower / battery.MaxStoredPower > 0.95)
                    {
                        battery.ChargeMode = ChargeMode.Auto;
                    }
                }
            }
            // If the connector is not locked, set the charge mode to "Auto"
            else
            {
                foreach (var battery in batteries)
                {
                    battery.ChargeMode = ChargeMode.Auto;
                }
            }

            // Check if the battery level is below 50%
            var lowPower = false;
            foreach (var battery in batteries)
            {
                if (battery.CurrentStoredPower / battery.MaxStoredPower < 0.5)
                {
                    lowPower = true;
                    break;
                }
            }

            // If the battery level is below 50%, turn on the hydrogen engines
            if (lowPower)
            {
                // Get all the hydrogen engine blocks on the grid
                var hydrogenEngines = new List<IMyPowerProducer>();
                GridTerminalSystem.GetBlocksOfType(hydrogenEngines);
                hydrogenEngines = hydrogenEngines.Where(x => x.IsSameConstructAs(Me)).ToList();
                // Turn on the hydrogen engines
                foreach (var hydrogenEngine in hydrogenEngines)
                {
                    hydrogenEngine.Enabled = true;
                }
            }

            // Check if the battery level is below 20%
            if (batteries.Any(battery => battery.CurrentStoredPower / battery.MaxStoredPower < 0.2))
            {

                // Get the Timer Block named "AFV2.Timer Block Dock"
                var timerBlock = GridTerminalSystem.GetBlockWithName("AFV2.Timer Block Dock") as IMyTimerBlock;

                // Check if the Timer Block is active and counting
                if (timerBlock != null && timerBlock.IsCountingDown)
                {
                    // Do not turn on the autopilot
                    return;
                }

                // If the connector is not locked, turn on the autopilot
                if (connector.Status != MyShipConnectorStatus.Connected)
                {

                    // Get the remote controller
                    var remoteController = GetRemoteControl();
                    if (remoteController != null)
                    {
                        // Turn on the autopilot
                        remoteController.SetAutoPilotEnabled(true);
                    }
                    else
                    {
                        Echo("No Remote Control");
                    }
                }
            }
        }

        private void SetNames(Func<string, string> namingFunction)
        {
            //rename all terminal blocks
            RenameBlocksByType<IMyTerminalBlock>(namingFunction, true);
        }

        private void SetHome()
        {
            // Code for setting home goes here


            // Get all the remote controller on the grid
            var remoteController = GetRemoteControl();

            if (remoteController != null)
            {
                Echo($"Using remote controller: {remoteController.CustomName} to set docking waypoints. Additionnal actions net to be set manually. Torn off Collision Avoidance on Approach 40 and start timmer block Dock at final waypoit Dock.");


                if (!remoteController.CustomName.EndsWith("Dock"))
                {
                    // Rename the remoteController block
                    remoteController.CustomName = $"{remoteController.CustomName}Dock";

                    // Setup remoteController
                    remoteController.SetCollisionAvoidance(true);
                    remoteController.SetDockingMode(true);
                    remoteController.FlightMode = FlightMode.OneWay;
                    remoteController.SpeedLimit = 100;
                }

                var gridNameCapitalLetters = GetShortGridName();

                remoteController.ClearWaypoints();

                // Add a waypoint 300m above the current position of the remote controller
                var waypoint1 = new MyWaypointInfo(gridNameCapitalLetters + ".Appropach 300",
                    remoteController.GetPosition() + remoteController.WorldMatrix.Up * 300);

                //waypoint1.Actions.Add(new MyWaypointAction("CollisionAvoidance_On", remoteController.EntityId));
                remoteController.AddWaypoint(waypoint1);

                // Add a waypoint 20m above the current position of the remote controller
                var waypoint2 = new MyWaypointInfo(gridNameCapitalLetters + ".Approach 40",
                    remoteController.GetPosition() + remoteController.WorldMatrix.Up * 40);
                //waypoint2.Actions.Add(new MyWaypointAction("CollisionAvoidance_Off", remoteController.EntityId));
                remoteController.AddWaypoint(waypoint2);

                // Add a waypoint at the current position of the remote controller
                var waypoint3 = new MyWaypointInfo(gridNameCapitalLetters + ".Dock",
                    remoteController.GetPosition());

                // Get the first timer block on the grid
                var timerBlocks = new List<IMyTimerBlock>();
                GridTerminalSystem.GetBlocksOfType(timerBlocks);
                var timerBlockDock = timerBlocks.FirstOrDefault();

                // Check if the timer block's name does not already end with "Dock"
                if (timerBlockDock != null)
                {
                    if (!timerBlockDock.CustomName.EndsWith("Dock"))
                    {
                        // Rename the timer block
                        timerBlockDock.CustomName = $"{timerBlockDock.CustomName}Dock";

                        //Configure timmer block here
                        timerBlockDock.Silent = true;
                        timerBlockDock.TriggerDelay = 1; //1s

                        Echo($"{timerBlockDock.InventoryCount}");

                        // Get the first connector on the grid
                        var connectors = new List<IMyShipConnector>();
                        GridTerminalSystem.GetBlocksOfType(connectors);
                        var connectorDock = connectors.FirstOrDefault();

                        // Check if the connector's name does not already end with "Dock"
                        if (connectorDock != null)
                        {
                            if (!connectorDock.CustomName.EndsWith("Dock"))
                            {
                                // Rename the connector
                                connectorDock.CustomName = $"{connectorDock.CustomName}Dock";


                                // Configure the connector here
                            }


                        }

                        // Add an action to lock the connector when the timer block's timer runs out
                        //timerBlockDock.AddAction("Lock", connectorDock);

                        //waypoint3.Actions.Add(new MyWaypointAction("Start", timerBlockDock.EntityId));

                        // Add the action to the waypoint
                        //waypoint3.AddAction("Activate Timer", timerBlockDock.CustomName);
                    }
                }


                remoteController.AddWaypoint(waypoint3);
            }
            else
            {
                Echo("No remote controllers found");
            }
        }

        private string GetShortGridName()
        {
            // Get the grid name
            var gridName = Me.CubeGrid.CustomName;

            // Initialize an empty string to store the capital letters and numbers
            string gridNameCapitalLetters = "";

            // Loop through each character in the grid name
            foreach (char c in gridName)
            {
                // If the character is a capital letter or a number, add it to the string
                if (char.IsUpper(c) || char.IsNumber(c))
                {
                    gridNameCapitalLetters += c;
                }
            }

            // If the string of capital letters is empty, use the original grid name
            if (string.IsNullOrEmpty(gridNameCapitalLetters))
            {
                gridNameCapitalLetters = gridName;
            }

            return gridNameCapitalLetters;
        }

        // Find the connector with "Dock" in its name or the first connector in the current construct
        private IMyShipConnector GetConnector(string nameEnd = "Dock")
        {
            // Get all the connector blocks on the grid
            var connectors = new List<IMyShipConnector>();
            GridTerminalSystem.GetBlocksOfType(connectors);

            // Filter the connectors by the current construct and the specified name end
            connectors = connectors.Where(x => x.IsSameConstructAs(Me)).ToList();

            if (connectors.Any(x => x.CustomName.EndsWith(nameEnd)))
            {
                return connectors.Where(x => x.CustomName.EndsWith(nameEnd)).FirstOrDefault();
            }

            return connectors.FirstOrDefault();
        }

        // Find the remote control with the specified name end in its name or the first remote control in the current construct
        private IMyRemoteControl GetRemoteControl(string nameEnd = "Dock")
        {
            // Get all the remote control blocks on the grid
            var remoteControls = new List<IMyRemoteControl>();
            GridTerminalSystem.GetBlocksOfType(remoteControls);

            // Filter the remote controls by the current construct and the specified name end
            remoteControls = remoteControls.Where(x => x.IsSameConstructAs(Me)).ToList();

            if (remoteControls.Any(x => x.CustomName.EndsWith(nameEnd)))
            {
                return remoteControls.Where(x => x.CustomName.EndsWith(nameEnd)).FirstOrDefault();
            }

            return remoteControls.FirstOrDefault();
        }

        public void RenameBlocksByType<T>(Func<string, string> namingFunction, bool isFunctional = true) where T : class, IMyTerminalBlock
        {
            List<T> blocks = new List<T>();
            GridTerminalSystem.GetBlocksOfType(blocks);
            blocks = blocks.Where(x => x.IsSameConstructAs(Me) && x.IsFunctional == isFunctional).ToList();

            foreach (T block in blocks)
            {
                block.CustomName = namingFunction(block.CustomName);
            }
        }

        private string NamingFunctionSetNames(string oldCustomName)
        {
            if (!oldCustomName.Contains("."))
            {
                var sortName = ShortGridName;
                return $"{sortName}.{oldCustomName}";
            }
            return oldCustomName;
        }

        private string NamingFunctionUnsetNames(string oldCustomName)
        {
            if (oldCustomName.Contains("."))
            {
                return oldCustomName.Split('.')[1];
            }
            return oldCustomName;
        }

    }
}
