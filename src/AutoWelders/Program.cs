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
	class Program : MyGridProgram
	{
		string[] statusChars = new string[] { "/", "-", @"\", "|" };
		int statusCharPosition = 0;


		/// <summary>
		/// Configuration
		/// </summary>
		float minimumBlockHealth = 0.85f;
		float stopRepairHealth = 0.99f;
		TimeSpan repairTimeout = TimeSpan.FromSeconds(60);
		float maximumWelderDamagedDistance = 9.0f; //If lower has problems with distance to big blocks like big thrusters.
		TimeSpan timeoutDispose = TimeSpan.FromMinutes(5);
		bool showDamagedOnHud = true;
		int numberOfWeldersToFindNearest = 3; //Will search for 3 nearest to damaged block welders and try run them all to spped up repair.

		List<MyRepairInfo> activeRepairs;

		public Program()

		{
			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			activeRepairs = new List<MyRepairInfo>();
		}

		void Main()
		{
			DisplayStatus();
			GetDamagedBlocks();
			DisableWeldersWhenRapairDone();
			if (showDamagedOnHud)
			{
				HighlightDamaged();
			}
			CleanupTimeouts();
		}

		void DisplayStatus()
		{
			this.Echo(statusChars[statusCharPosition]);
			statusCharPosition++;
			if (statusCharPosition >= statusChars.Length)
			{
				statusCharPosition = 0;
			}
		}

		void GetDamagedBlocks()
		{
			//List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			//this.GridTerminalSystem.GetBlocks(blocks);

			List<IMyCubeBlock> blocksCube = new List<IMyCubeBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyCubeBlock>(blocksCube);

			var textSb = new StringBuilder();
			textSb.AppendFormat($"Serching in {blocksCube.Count} blocks...");
			textSb.AppendFormat($"Repairs: {activeRepairs.Count} Timeout:{activeRepairs.Count(x => x.Timeout)}");

			Echo(textSb.ToString());

			blocksCube.ForEach(block =>
			{
				if (block != null)
				{
					if (GetMyTerminalBlockHealth(ref block) < this.minimumBlockHealth)
					{
						if (showDamagedOnHud)
						{
							if (block is IMyTerminalBlock)
							{

								((IMyTerminalBlock)block).ApplyAction("ShowOnHUD_On");
							}
						}

						GetWelsersAndStartRepair(block);
					}

					if (!block.IsFunctional)
					{
						GetWelsersAndStartRepair(block);
					}
				}
			});
		}

		void GetWelsersAndStartRepair(IMyCubeBlock block)
		{
			List<IMyTerminalBlock> allWelders = new List<IMyTerminalBlock>();
			this.GridTerminalSystem.GetBlocksOfType<IMyShipWelder>(allWelders);

			this.Echo(block.DisplayName);
			var nearestWelders = allWelders.OrderBy(welder => DistanceSquared(block.GetPosition(), welder.GetPosition())).Take(numberOfWeldersToFindNearest).ToList();
			nearestWelders.ForEach(welder =>
			{
				RepairDamaged(block, welder);
			}
			);

		}

		void RepairDamaged(IMyCubeBlock block, IMyTerminalBlock nearestWelder)
		{
			if (activeRepairs.FirstOrDefault(x => x.Welder == nearestWelder && x.DamagedBlock == block) == null) //&& DistanceSquared(nearestWelder, block)<maximumWelderDamagedDistance
			{
				this.Echo(nearestWelder.CustomName);

				if (DistanceSquared(nearestWelder.GetPosition(), block.GetPosition()) < maximumWelderDamagedDistance)
				{
					nearestWelder.ApplyAction("OnOff_On");

					activeRepairs.Add(new MyRepairInfo(nearestWelder, block, DistanceSquared(nearestWelder, block)));
				}
				else
				{
					Echo("Distance to big " + nearestWelder.CustomName + ":" + DistanceSquared(nearestWelder, block).ToString());
				}
			}
		}

		void DisableWeldersWhenRapairDone()
		{
			activeRepairs.ForEach(activeRepair =>
			{
				var damagedBlock = activeRepair.DamagedBlock;
				if (GetMyTerminalBlockHealth(ref damagedBlock) > this.stopRepairHealth)
				{
					activeRepair.Welder.ApplyAction("OnOff_Off");
					activeRepair.Done = true;
					var text = activeRepair.Welder.CustomName + " Done";
					this.Echo(text);
				}
				else if ((DateTime.UtcNow.Ticks - activeRepair.CreateDate.Ticks - repairTimeout.Ticks) > 0)
				{
					activeRepair.Welder.ApplyAction("OnOff_Off");
					activeRepair.Timeout = true;
					var text = activeRepair.Welder.CustomName + " Timeout" + (DateTime.UtcNow.Ticks - activeRepair.CreateDate.Ticks - repairTimeout.Ticks).ToString();
					this.Echo(text);
				}
			});

			activeRepairs.RemoveAll(x => x.Done);
		}

		void HighlightDamaged()
		{
			List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);

			for (int i = 0; i < blocks.Count; i++)
			{
				if (!blocks[i]?.IsFunctional ?? false)
				{

					try
					{
						if (blocks[i] != null)
						{
							blocks[i].ApplyAction("ShowOnHUD_On");
						}
					}
					catch (Exception ex)
					{
						//Some exceptions are caused by grids connected via connector
						//Echo(ex.Message + blocks[i].CustomName);
					}
				}

				else
				{
					try
					{
						//blocks[i].RequestShowOnHUD(false); 
						//blocks[i]?.ApplyAction("ShowOnHUD_Off");
						if (blocks[i] != null)
						{
							blocks[i].ApplyAction("ShowOnHUD_Off");
						}
					}
					catch (Exception ex)
					{
						//Some exceptions are caused by grids connected via connector
						//Echo(ex.Message + blocks[i].CustomName);

					}
				}

			}

		}

		void CleanupTimeouts()
		{
			activeRepairs.RemoveAll(x => x.Timeout && (DateTime.UtcNow.Ticks - x.CreateDate.Ticks - timeoutDispose.Ticks) > 0);
		}

		//Dr. Novikov snippet: http://forums.keenswh.com/threads/snippet-get-block-health.7368130/ 
		public float GetMyTerminalBlockHealth(ref IMyCubeBlock block)
		{
			IMySlimBlock slimblock = block.CubeGrid.GetCubeBlock(block.Position);
			float MaxIntegrity = slimblock.MaxIntegrity;
			float BuildIntegrity = slimblock.BuildIntegrity;
			float CurrentDamage = slimblock.CurrentDamage;

			return (BuildIntegrity - CurrentDamage) / MaxIntegrity;
		}
		//--------------- 

		public float DistanceSquared(IMyCubeBlock block1, IMyCubeBlock block2)
		{
			return DistanceSquared(block1.GetPosition(), block2.GetPosition());
		}
		public float DistanceSquared(Vector3D point1, Vector3D point2)
		{
			//var dist = Math.Sqrt(point1.Zip(point2, (a, b) => (a - b) * (a - b)).Sum());
			float distance = (float)Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2) + Math.Pow(point1.Z - point2.Z, 2));
			return distance;
		}

		public class MyRepairInfo
		{
			public MyRepairInfo(IMyTerminalBlock welder, IMyCubeBlock damagedBlock, float welderDamagedDistance)
			{
				Welder = welder;
				DamagedBlock = damagedBlock;
				CreateDate = DateTime.UtcNow;
				Timeout = false;
				Done = false;
				WelderDamagedDistance = welderDamagedDistance;
			}

			public IMyTerminalBlock Welder { get; set; }
			public IMyCubeBlock DamagedBlock { get; set; }
			public DateTime CreateDate { get; set; }
			public bool Timeout { get; set; }
			public bool Done { get; set; }
			public float WelderDamagedDistance { get; set; }
		}



	}
}
