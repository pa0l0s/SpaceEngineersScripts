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

        string status;
        StringBuilder sbHudText = new StringBuilder(AntennaAttentionText);
        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;



            status = ".";
        }

  
        void Main(string argument)
        {
            bool Attention = false;

            List<IMyTerminalBlock> Blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(Blocks);
            for (int i = 0; i < Blocks.Count; i++)
            {
                IMyTerminalBlock block = Blocks[i];
                if (IsBeingHacked(block) || IsBeingAttacked(block))
                    Attention = true;
            }
            GridTerminalSystem.GetBlocksOfType<IMyTimerBlock>(Blocks);
            if (Attention)
            {
                for (int a = 0; a < Blocks.Count; a++)
                {
                    if (Blocks[a].CustomName.Contains(Prefix))
                        Blocks[a].ApplyAction("TriggerNow");
                }
                AttentionAction();
            }

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
            foreach(var turret in turrets)
            {
                turret.ApplyAction("OnOff_On");
            }

            Runtime.UpdateFrequency = UpdateFrequency.None; //Stop on alarm
        }


        bool IsBeingHacked(IMyTerminalBlock _block)
        {
            return _block.IsBeingHacked;
        }

        bool IsBeingAttacked(IMyTerminalBlock _block)
        {
            IMySlimBlock _slimBlock = _block.CubeGrid.GetCubeBlock(_block.Position);
            if (_slimBlock.DamageRatio > 1 || _slimBlock.CurrentDamage > _slimBlock.MaxIntegrity * 0.1f)
            {
                return true;
            }
            else
                return false;
        }


    }
}
