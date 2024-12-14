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
using Sandbox.Game.GameSystems.Electricity;
using System.Security.Cryptography.X509Certificates;
using Sandbox.Game.Entities.Interfaces;
using IMyGasTank = Sandbox.ModAPI.Ingame.IMyGasTank;

namespace ScriptingClass
{
    public class Program : MyGridProgram
    {
        //AI block Combat Drone Script  
        //By Paolo 

        //################################## 
        //modify these 
        private const double WEAPON_ENGAGE_DIST = 500;          //fire static weapons when player gets this close 
        private const double WEAPON_DISENGAGE_DIST = 700;       //disable static weapons after this distance 
        private const bool USE_STATIC_GUNS = true;              //use static weapons? 
        private const int WEAPON_ANGLE_LIMIT = 30;              //activate weapons if player is within this many degrees in front of drone 
        private const double PATROL_RADIUS = 2000;             //engage any player inside a sphere with this radius 
        private const double BREAKAWAY_DISTANCE = 2000;         //stop chasing a player outside the patrol sphere if they get this far away from drone 

        private const float ANTENNA_RANGE = 50000.0f;        //Default anthenna range will be set at recompile.
        //################################## 


        private List<IMyUserControllableGun> _guns;
        private Vector3D _origin;
        private List<IMyRemoteControl> _controllers;
        private IMyRemoteControl _currentControl;
        private double _weaponAngle;
        private bool _shooting;

        private float _batteryChargeLevel;
        private double _hydrogenFilledRatio;

        Program()

        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            /*
            //_guns = new List<IMyUserControllableGun>();
            //_controllers = new List<IMyRemoteControl>();
            //GridTerminalSystem.GetBlocksOfType(_guns);
            //GridTerminalSystem.GetBlocksOfType(_controllers);
            //_currentControl = _controllers.FirstOrDefault(c => c.IsFunctional);

            //if (_currentControl == null)
            //    return;

            //if (string.IsNullOrEmpty(Storage))
            //{
            //    if (!Vector3D.TryParse(Storage, out _origin))
            //        _origin = _currentControl.GetPosition();
            //}
            //else
            //{
            //    _origin = _currentControl.GetPosition();
            //    Storage = _origin.ToString();
            //}

            //_weaponAngle = Math.Cos(MathHelperD.ToRadians(WEAPON_ANGLE_LIMIT));
            */

            InitDroneBlocks();
        }


        void Main(string argument, UpdateType updateSource)
        {
            /*
            ////check if current RC is damaged, look for a replacement 
            //if (_currentControl == null || !_currentControl.IsFunctional)
            //{
            //    _currentControl = _controllers.FirstOrDefault(c => c.IsFunctional);
            //}

            //if (_currentControl == null)
            //    return; //no controls left :( 


            ////Check Player Distance From Origin  
            //_currentControl.ClearWaypoints();
            //Vector3D currentPos = _currentControl.GetPosition();
            //Vector3D closestPlayer = new Vector3D();
            ////_currentControl.   .GetNearestPlayer(out closestPlayer);
            //double playerDistanceOrigin = Vector3D.DistanceSquared(closestPlayer, _origin);
            //double playerDistanceDrone = Vector3D.DistanceSquared(currentPos, closestPlayer);
            //if (playerDistanceDrone < BREAKAWAY_DISTANCE * BREAKAWAY_DISTANCE || playerDistanceOrigin < PATROL_RADIUS * PATROL_RADIUS)
            //{
            //    //Chase Player  
            //    _currentControl.AddWaypoint(closestPlayer, "Player");

            //    //update guns 
            //    if (USE_STATIC_GUNS)
            //    {
            //        if (playerDistanceDrone <= WEAPON_ENGAGE_DIST * WEAPON_ENGAGE_DIST)
            //        {
            //            Vector3D playerDir = closestPlayer - currentPos;
            //            playerDir.Normalize();
            //            double dot = Vector3D.Dot(_currentControl.WorldMatrix.Forward, playerDir);
            //            //check if player is inside our target cone 
            //            if (dot > _weaponAngle)
            //            {
            //                StartShooting();
            //                _shooting = true;
            //            }
            //            else
            //            {
            //                StopShooting();
            //                _shooting = false;
            //            }
            //        }
            //        else if (playerDistanceDrone > WEAPON_DISENGAGE_DIST * WEAPON_DISENGAGE_DIST && _shooting)
            //        {
            //            StopShooting();
            //            _shooting = false;
            //        }
            //    }
            //}
            //else
            //{
            //    //Go To Origin  
            //    _currentControl.AddWaypoint(_origin, "Origin");
            //    if (_shooting && USE_STATIC_GUNS)
            //    {
            //        _shooting = false;
            //        StopShooting();
            //    }
            //}

            //_currentControl.SetAutoPilotEnabled(true);
            */


            // Get all the battery blocks on the grid
            var batteries = GetFunctionalBlocksOfType<IMyBatteryBlock>();
            _batteryChargeLevel = GetBatteryChargeLevel(batteries);


            var gassTanks = GetFunctionalBlocksOfType<IMyGasTank>();
            _hydrogenFilledRatio = GetHydrogenFilledRatio(gassTanks);

            if (_batteryChargeLevel > 0) { Echo($"Battery level: {_batteryChargeLevel}"); }
            if (_hydrogenFilledRatio > 0) { Echo($"Gass fill ratio: {_hydrogenFilledRatio}"); }


            DroneAi();

            var antennas = GetFunctionalBlocksOfType<IMyRadioAntenna>();
            var antenna = antennas.FirstOrDefault();
            //var info = antenna.

        }

        void DroneAi()
        {
            //Echo($"Connector found {connector.CustomName}");


            //Self destruct if no power or fuel left and not docked
            var connectors = GetFunctionalBlocksOfType<IMyShipConnector>();

            //first check if not docked
            if (!connectors.Any(x => x.Status == MyShipConnectorStatus.Connected))
            {
                if (_batteryChargeLevel > 0) //Has working batteries
                {
                    if (_batteryChargeLevel < 0.02) //Batteries charge level critical
                    {
                        TriggerSelfdestruct();
                    }


                }

                //No hydrogen left if using hydrogen tanks
                if (_hydrogenFilledRatio > 0) //Has tamks
                {
                    if (_hydrogenFilledRatio < 0.05)
                    {
                        TriggerSelfdestruct();
                    }
                }
            }
        }

        void TriggerSelfdestruct()
        {
            //Trigger blocks with tag (timmer blocks optional actions)

            var warheads = GetFunctionalBlocksOfType<IMyWarhead>();
            if (warheads.Count > 0)
            {
                //detonate warheads
                foreach (var warhead in warheads)
                {
                    if (warhead.IsFunctional)
                    {
                        warhead.StartCountdown();
                    }
                }
            }
        }

        void StopShooting()
        {
            foreach (var gun in _guns)
                gun.SetValueBool("Shoot", false);
        }

        void StartShooting()
        {
            foreach (var gun in _guns)
            {
                bool shoot = gun.Orientation.Forward == _currentControl.Orientation.Forward;
                gun.SetValueBool("Shoot", shoot);
            }
        }

        private float GetBatteryChargeLevel(IList<IMyBatteryBlock> batteries)
        {
            if (batteries == null || batteries.Count == 0)
                return 0; // Return 0 if the list is null or empty to avoid division by zero.

            float totalCharge = 0;
            foreach (var battery in batteries)
            {
                totalCharge += (float)(battery.CurrentStoredPower / battery.MaxStoredPower);
            }

            return totalCharge / batteries.Count; // Return the average charge level.
        }

        private void InitDroneBlocks()
        {
            //Set Antennas range to ANTENNA_RANGE setting.
            var antennas = GetFunctionalBlocksOfType<IMyRadioAntenna>();
            foreach(IMyRadioAntenna ant in antennas)
            {
                ant.Radius = ANTENNA_RANGE;
            }

            //Arm warheads.
            var warheads = GetFunctionalBlocksOfType<IMyWarhead>();
            foreach(IMyWarhead warhead in warheads)
            {
                warhead.IsArmed = true;
            }
        }

        private IList<T> GetFunctionalBlocksOfType<T>() where T : class, IMyTerminalBlock 
        {
            var blocks = new List<T>();
            GridTerminalSystem.GetBlocksOfType(blocks);
            blocks = blocks.Where(x => x.IsSameConstructAs(Me) & x.IsFunctional).ToList();
            return blocks;  
        }

        private double GetHydrogenFilledRatio(IList<IMyGasTank> gassTanks)
        {
            if (gassTanks == null || gassTanks.Count == 0)
                return 0; // Return 0 if the list is null or empty to avoid division by zero.

            double totalFilledRatio = 0;
            foreach (var gassTank in gassTanks)
            {
                totalFilledRatio += gassTank.FilledRatio;
            }

            return totalFilledRatio / gassTanks.Count; // Return the average filled ratio.
        }
    }
}

