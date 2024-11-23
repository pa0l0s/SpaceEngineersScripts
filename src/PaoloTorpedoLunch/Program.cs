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




        private const string groupName = "TorpedoSystem";
        private const int shootTickDelay = 100;

        private int shootTick = 0;

        private List<IMyGasTank> _snallHydrogenTanks;
        private List<IMyThrust> _snallHydrogenThrusters;
        private List<IMyPistonBase> _pistons;
        private List<IMyMotorStator> _hinges;
        private List<IMySmallGatlingGun> _gatlingGuns;
        private List<IMyGyro> _gyros;
        private List<IMyShipWelder> _welders;
        private List<IMyWarhead> _warheads;


        Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Once;
            _snallHydrogenTanks = new List<IMyGasTank>();
            _snallHydrogenThrusters = new List<IMyThrust>();
            _pistons = new List<IMyPistonBase>();
            _hinges = new List<IMyMotorStator>();
            _gatlingGuns = new List<IMySmallGatlingGun>();
            _gyros = new List<IMyGyro>();
            _welders = new List<IMyShipWelder>();
            _warheads = new List<IMyWarhead>();

            //_railguns = new List<IMySmallGatlingGun();
            //debug
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlockGroupWithName(groupName).GetBlocks(blocks);
            Echo(blocks.Count.ToString());
            foreach (var block in blocks)
            {
                Echo(block.BlockDefinition.SubtypeId);
            }

            //init static elements
            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_gatlingGuns);
            if (!_gatlingGuns.Any()) throw new Exception($"Error: No _gatlingGuns in group {groupName}");
            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_welders);
            if (!_welders.Any()) throw new Exception($"Error: No _welders in group {groupName}");
        }

        void Main(string argument, UpdateType updateSource)
        {
            this.Runtime.UpdateFrequency = UpdateFrequency.Once;

            try
            {

                //if is shooting stop an reset
                if (ChcekIfShooting()) return;

                if (string.IsNullOrEmpty(argument)) return;

                ConfigureBlocksCollections();

                switch (argument.ToLower())
                {
                    case "lunch":
                        Lunch();
                        break;

                    case "reset":
                        Reset();
                        break;
                    default:
                        Reset();
                        return;
                }
            }
            catch (Exception ex)
            {
                Echo(ex.ToString());
            }

        }

        void ConfigureBlocksCollections()
        {
            //Get torpedo system hydrogen tanks 
            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_snallHydrogenTanks);

            _snallHydrogenTanks = _snallHydrogenTanks.Where(x => x.BlockDefinition.SubtypeId.Contains("SmallHydrogenTank")).ToList();
            if (!_snallHydrogenTanks.Any()) throw new Exception($"Error: No SmallHydrogenTanks in group {groupName}");
            //debug
            Echo(_snallHydrogenTanks.FirstOrDefault().BlockDefinition.SubtypeId);

            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_snallHydrogenThrusters);
            _snallHydrogenThrusters = _snallHydrogenThrusters.Where(x => x.BlockDefinition.SubtypeId.Contains("SmallHydrogenThrust")).ToList();
            if (!_snallHydrogenThrusters.Any()) throw new Exception($"Error: No SmallHydrogenThrusts in group {groupName}");

            //debug
            Echo(_snallHydrogenThrusters.FirstOrDefault().BlockDefinition.SubtypeId);

            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_gyros);
            if (!_gyros.Any()) throw new Exception($"Error: No _gyros in group {groupName}");
            //debug
            Echo(_gyros.FirstOrDefault().BlockDefinition.SubtypeId);


            //Not required
            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_pistons);
            //debug
            //Echo(_pistons.FirstOrDefault().BlockDefinition.SubtypeId);

            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_hinges);
            //debug
            //Echo(_hinges.FirstOrDefault().BlockDefinition.SubtypeId);

            GridTerminalSystem.GetBlockGroupWithName(groupName)?.GetBlocksOfType(_warheads);
        }

        void Lunch()
        {
            foreach (var _warhead in _warheads)
            {
                _warhead.IsArmed = true;
                _warhead.DetonationTime = 60f;
                _warhead.StartCountdown();
            }

            foreach (var _smallHydrogenTank in _snallHydrogenTanks)
            {
                _smallHydrogenTank.Stockpile = false;
            }

            foreach (var _snallHydrogenThrust in _snallHydrogenThrusters)
            {
                _snallHydrogenThrust.ThrustOverridePercentage = 1f;
                _snallHydrogenThrust.ApplyAction("OnOff_On");
            }

            foreach (var _gyro in _gyros)
            {
                _gyro.GyroOverride = true;
                //_gyro.Roll = 10f;
                //_gyro.SetValue("Roll", 10f);
            }

            //ADITIONAL

            if (_pistons.Any())
            {
                foreach (var _piston in _pistons)
                {
                    _piston.ApplyAction("OnOff_On");
                    _piston.Reverse();
                }

            }

            if (_hinges.Any())
            {
                foreach (var _hinge in _hinges)
                {
                    _hinge.ApplyAction("OnOff_On");
                    Single v = _hinge.GetValueFloat("Velocity");
                    _hinge.SetValue("Velocity", v*-1);
                }
            }

            //detach missles
            foreach (var _welder in _welders)
            {
                _welder.ApplyAction("OnOff_Off");
            }


            foreach (var _gatlingGun in _gatlingGuns)
            {
                _gatlingGun.ApplyAction("OnOff_On");
                _gatlingGun.ApplyAction("Shoot_On");
            }
            this.Runtime.UpdateFrequency = UpdateFrequency.Update100; //for next main run

        }

        void Reset()
        {
            foreach (var _smallHydrogenTank in _snallHydrogenTanks)
            {
                _smallHydrogenTank.Stockpile = true;
            }

            foreach (var _snallHydrogenThrust in _snallHydrogenThrusters)
            {
                _snallHydrogenThrust.ApplyAction("OnOff_Off");
            }

            foreach (var _gyro in _gyros)
            {
                _gyro.GyroOverride = false;
                _gyro.Roll = 0f;
                //_gyro.SetValue("Roll", 10f);
            }

            //ADITIONAL

            if (_pistons.Any())
            {
                foreach (var _piston in _pistons)
                {
                    _piston.ApplyAction("OnOff_On");
                    _piston.Reverse();
                }

            }

            if (_hinges.Any())
            {
                foreach (var _hinge in _hinges)
                {
                    _hinge.ApplyAction("OnOff_On");
                    Single v = _hinge.GetValueFloat("Velocity");
                    _hinge.SetValue("Velocity", v * -1);
                }
            }
        }

        bool ChcekIfShooting()
        {


            if (!_gatlingGuns.Any()) throw new Exception($"Error: No _gatlingGuns in group {groupName}");

            if (_gatlingGuns.FirstOrDefault().IsShooting)
            {
                shootTick++;
                if (shootTick < shootTickDelay) return false;
                shootTick = 0;

                foreach (var _gatlingGun in _gatlingGuns)
                {
                    _gatlingGun.ApplyAction("Shoot_Off");
                    _gatlingGun.ApplyAction("OnOff_Off");
                }

                foreach (var _welder in _welders)
                {
                    _welder.ApplyAction("OnOff_On");
                }

                foreach (var _gyro in _gyros)
                {
                    //_gyro.GyroOverride = false;
                    _gyro.Roll = 60f;
                    //_gyro.SetValue("Roll", 10f);
                }

                return true;
            }
            return false;
        }





    }
}
