using System;
using System.Collections.Generic;
using System.IO;
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


        const string Prefix = "[Security]";        /*This is prefix for timer, which will be triggered*/
        const string AntennaAttentionText = "ATACKED!!!";
        const float AntennaAttentionRadius = 10000f;

        const string DecoyTimersGroupName = "DecoyTimers";
        private List<BlockStatus> _decoyTimers;
        private List<BlockStatus> ALLBlocks;

        bool Active = false;

        string status;
        StringBuilder sbHudText = new StringBuilder(AntennaAttentionText);
        public Program()
        {


            Init();
        }

        void Init()
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;


            ALLBlocks = new List<BlockStatus>();
            var blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);
            ALLBlocks = blocks.Select(x => new BlockStatus(x, true)).Where(x => !(IsBeingAttacked(x) || IsBeingHacked(x))).ToList();


            blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlockGroupWithName(DecoyTimersGroupName).GetBlocks(blocks);
            Echo(blocks.Count.ToString());
            foreach (var block in blocks)
            {
                Echo(block.BlockDefinition.SubtypeId);
            }


            //init static elements
            _decoyTimers = new List<BlockStatus>();
            var timerBlocks = new List<IMyTimerBlock>();
            GridTerminalSystem.GetBlockGroupWithName(DecoyTimersGroupName)?.GetBlocksOfType(timerBlocks);
            //if (!_decoyTimers.Any()) throw new Exception($"Error: No _decoyTimers in group {DecoyTimersGroupName}");
            _decoyTimers = timerBlocks.Select(x => new BlockStatus(x, true)).ToList();

            status = ".";
        }


        void Main(string argument)
        {
            bool Attention = false;


            if (!String.IsNullOrEmpty(argument))
            {
                float speedLimit;

                if (float.TryParse(argument, out speedLimit))
                {
                    var remotes = new List<IMyRemoteControl>();
                    GridTerminalSystem.GetBlocksOfType(remotes);
                    var remote = remotes.FirstOrDefault(x => x.IsFunctional);
                    remote.SpeedLimit = speedLimit;
                }
                else if (argument.ToLower() == "reset")
                {
                    Attention = false;
                    //Active = false;
                    Init();
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;

                }
                else if (argument.ToLower() == "active")
                {
                    Active = true;
                    Runtime.UpdateFrequency = UpdateFrequency.Update100;

                }
                else if (argument.ToLower() == "stop")
                {
                    Active = false;
                    Runtime.UpdateFrequency = UpdateFrequency.None;

                }
                else if (argument.ToLower() == "fire")
                {
                    FireDecoy();
                }
            }

            Echo("Active - " + Active.ToString());


            if (Active)
            {
                Echo("AllBlocksCount - " + ALLBlocks.Where(x => x.ready).Count().ToString());

                try
                {
                    foreach (var block in ALLBlocks.Where(x => x.ready).ToList())
                    {

                        if (IsBeingHacked(block) || IsBeingAttacked(block))
                            Attention = true;
                    }

                    Echo("Attention - " + Attention.ToString());

                    if (Attention)
                    {
                        foreach (IMyTerminalBlock block in ALLBlocks.Where(x => x.block.IsFunctional).Select(x => x.block))
                        {
                            if (block.CustomName.Contains(Prefix))
                                block.ApplyAction("TriggerNow");
                        }
                        AttentionAction();
                    }
                }
                catch (Exception ex)
                {
                    Echo(ex.ToString());
                }
            }

            Echo(status);
            if (status == ".") status = "|"; else status = ".";
        }

        void AttentionAction()
        {
            var antennas = new List<IMyRadioAntenna>();
            GridTerminalSystem.GetBlocksOfType(antennas);
            var antenna = antennas.FirstOrDefault(x => x.IsFunctional);
            if (antenna != null)
            {
                antenna.SetValue("HudText", sbHudText);
                antenna.Radius = AntennaAttentionRadius;
            }

            var turrets = new List<IMyLargeGatlingTurret>();
            GridTerminalSystem.GetBlocksOfType(turrets);
            foreach (var turret in turrets)
            {
                turret.ApplyAction("OnOff_On");
            }

            FireDecoy();

            Runtime.UpdateFrequency = UpdateFrequency.None; //Stop on alarm
        }

        void FireDecoy()
        {
            var timerStatus = _decoyTimers.FirstOrDefault(x => x.ready && x.block.IsFunctional);
            timerStatus.ready = false;
            timerStatus.block.ApplyAction("TriggerNow");
        }


        bool IsBeingHacked(BlockStatus _block)
        {
            if (_block != null)
            {
                if (_block.block.IsBeingHacked)
                {
                    _block.ready = false;
                    return true;
                }
            }
            return false;

        }

        bool IsBeingAttacked(BlockStatus _block)
        {
            if (_block != null)
            {
                IMySlimBlock _slimBlock = _block.block.CubeGrid.GetCubeBlock(_block.block.Position);
                if (_slimBlock.DamageRatio > 1 || _slimBlock.CurrentDamage > _slimBlock.MaxIntegrity * 0.1f)
                {
                    _block.ready = false;
                    return true;
                }
            }

                return false;
        }

        private class BlockStatus
        {
            public BlockStatus(IMyTerminalBlock block, bool ready)
            {
                this.block = block;
                this.ready = ready;
            }
            public IMyTerminalBlock block;
            public bool ready;
        }


    }
}
