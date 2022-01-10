using System;
using System.Collections.Generic;
using System.Linq;
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
        //Pirate Drone Script (Encounter)  
        //By Rexxar 

        //################################## 
        //modify these 
        private const double WEAPON_ENGAGE_DIST = 100;          //fire static weapons when player gets this close 
        private const double WEAPON_DISENGAGE_DIST = 50;       //disable static weapons after this distance 
        private const bool USE_STATIC_GUNS = false;              //use static weapons? 
        private const int WEAPON_ANGLE_LIMIT = 15;              //activate weapons if player is within this many degrees in front of drone 
        private const double PATROL_RADIUS = 30000;             //engage any player inside a sphere with this radius 
        private const double BREAKAWAY_DISTANCE = 100;         //stop chasing a player outside the patrol sphere if they get this far away from drone 
                                                               //################################## 


        private List<IMyUserControllableGun> _guns;
        private Vector3D _origin;
        private List<IMyRemoteControl> _controllers;
        private IMyRemoteControl _currentControl;
        private double _weaponAngle;
        private bool _shooting;
        private List<IMySensorBlock> _sensors;
        private IMySensorBlock _currentSensor;

        Program()
        {
            _guns = new List<IMyUserControllableGun>();
            _controllers = new List<IMyRemoteControl>();
            GridTerminalSystem.GetBlocksOfType(_guns);
            GridTerminalSystem.GetBlocksOfType(_controllers);
            _currentControl = _controllers.FirstOrDefault(c => c.IsFunctional);

            if (_currentControl == null)
                return;

            if (string.IsNullOrEmpty(Storage))
            {
                if (!Vector3D.TryParse(Storage, out _origin))
                    _origin = _currentControl.GetPosition();
            }
            else
            {
                _origin = _currentControl.GetPosition();
                Storage = _origin.ToString();
            }

            _weaponAngle = Math.Cos(MathHelperD.ToRadians(WEAPON_ANGLE_LIMIT));

            _sensors = new List<IMySensorBlock>();
            GridTerminalSystem.GetBlocksOfType(_sensors);
            _currentSensor = _sensors.FirstOrDefault(c => c.IsFunctional);
        }

        void Main()
        {

            //check if current RC is damaged, look for a replacement 
            if (_currentControl == null || !_currentControl.IsFunctional)
            {
                _currentControl = _controllers.FirstOrDefault(c => c.IsFunctional);
            }

            if (_currentControl == null)
                return; //no controls left :( 

            //check if current RC is damaged, look for a replacement 
            if (_currentSensor == null || !_currentSensor.IsFunctional)
            {
                _currentSensor = _sensors.FirstOrDefault(c => c.IsFunctional);
            }

            //Check Player Distance From Origin  
            _currentControl.ClearWaypoints();
            Vector3D currentPos = _currentControl.GetPosition();
            Vector3D closestPlayer;
            _currentControl.GetNearestPlayer(out closestPlayer);

            double playerDistanceOrigin = Vector3D.DistanceSquared(closestPlayer, _origin);
            double playerDistanceDrone = Vector3D.DistanceSquared(currentPos, closestPlayer);
            if (playerDistanceDrone < BREAKAWAY_DISTANCE * BREAKAWAY_DISTANCE || playerDistanceOrigin < PATROL_RADIUS * PATROL_RADIUS)
            {
                //Chase Player  
                _currentControl.AddWaypoint(closestPlayer, "Player");

                //update guns 
                if (USE_STATIC_GUNS)
                {
                    if (playerDistanceDrone <= WEAPON_ENGAGE_DIST * WEAPON_ENGAGE_DIST)
                    {
                        Vector3D playerDir = closestPlayer - currentPos;
                        playerDir.Normalize();
                        double dot = Vector3D.Dot(_currentControl.WorldMatrix.Forward, playerDir);
                        //check if player is inside our target cone 
                        if (dot > _weaponAngle)
                        {
                            StartShooting();
                            _shooting = true;
                        }
                        else
                        {
                            StopShooting();
                            _shooting = false;
                        }
                    }
                    else if (playerDistanceDrone > WEAPON_DISENGAGE_DIST * WEAPON_DISENGAGE_DIST && _shooting)
                    {
                        StopShooting();
                        _shooting = false;
                    }
                }
            }
            else
            {
                //Go To Origin  
                _currentControl.AddWaypoint(_origin, "Origin");
                if (_shooting && USE_STATIC_GUNS)
                {
                    _shooting = false;
                    StopShooting();
                }
            }

            _currentControl.SetAutoPilotEnabled(true);

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



    }
}
