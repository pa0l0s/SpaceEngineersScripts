﻿using System;
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
		/// Auto Welders Configuration
		/// </summary>
		float minimumBlockHealth = 0.85f;
		float stopRepairHealth = 0.99f;
		TimeSpan repairTimeout = TimeSpan.FromSeconds(60);
		float maximumWelderDamagedDistance = 9.0f; //If lower has problems with distance to big blocks like big thrusters.
		TimeSpan timeoutDispose = TimeSpan.FromMinutes(5);
		bool showDamagedOnHud = true;
		int numberOfWeldersToFindNearest = 3; //Will search for 3 nearest to damaged block welders and try run them all to spped up repair.

		List<MyRepairInfo> activeRepairs;

		DoorManager doorManager;

		public Program()

		{
			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			activeRepairs = new List<MyRepairInfo>();

			doorManager = new DoorManager(this);
		}

		void Main()
		{
			DisplayStatus();
			GetDamagedBlocks();
			DisableWeldersWhenRapairDone();
			if (showDamagedOnHud)
			{
				//HighlightDamaged();
			}
			CleanupTimeouts();

			doorManager.ManageDoors();
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
						ShowOnHud(block, true);

						GetWelsersAndStartRepair(block);
					}

					else if (!block.IsFunctional)
					{
						ShowOnHud(block, true);

						GetWelsersAndStartRepair(block);
					}

					else
					{
						ShowOnHud(block, false);
					}
				}
			});
		}

		void GetWelsersAndStartRepair(IMyCubeBlock block)
		{
			List<IMyTerminalBlock> allWelders = new List<IMyTerminalBlock>();
			this.GridTerminalSystem.GetBlocksOfType<IMyShipWelder>(allWelders);

			this.Echo(string.Format($"Damaged block: {block.DisplayNameText}, {block.EntityId}"));
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
				this.Echo(string.Format($"Welder: {nearestWelder.DisplayNameText}, {nearestWelder.EntityId}"));

				if (DistanceSquared(nearestWelder.GetPosition(), block.GetPosition()) < maximumWelderDamagedDistance)
				{

					try
					{
						nearestWelder.ApplyAction("OnOff_On");
					}
					catch (Exception ex)
					{
						Echo(ex.Message);
					}

					activeRepairs.Add(new MyRepairInfo(nearestWelder, block, DistanceSquared(nearestWelder, block)));
				}
				else
				{
					Echo("Distance to big " + nearestWelder.DisplayNameText + ":" + DistanceSquared(nearestWelder, block).ToString());
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
					try
					{
						activeRepair.Welder.ApplyAction("OnOff_Off");
					}
					catch (Exception ex)
					{
						Echo(ex.Message);
					}
					activeRepair.Done = true;
					var text = activeRepair.Welder.CustomName + " Done";
					this.Echo(text);
				}
				else if ((DateTime.UtcNow.Ticks - activeRepair.CreateDate.Ticks - repairTimeout.Ticks) > 0)
				{
					try
					{
						activeRepair.Welder.ApplyAction("OnOff_Off");
					}
					catch (Exception ex)
					{
						Echo(ex.Message);
					}
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
				if (blocks[i] != null && !(blocks[i] is IMyRadioAntenna))
				{
					if (!blocks[i].IsFunctional)
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
							Echo(ex.Message + blocks[i].DisplayNameText);
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
							Echo(ex.Message + blocks[i].DisplayNameText);

						}
					}
				}

			}

		}

		void CleanupTimeouts()
		{
			activeRepairs.RemoveAll(x => x.Timeout && (DateTime.UtcNow.Ticks - x.CreateDate.Ticks - timeoutDispose.Ticks) > 0);
		}



		private void ShowOnHud(IMyCubeBlock block, bool show)
		{
			if (block is IMyTerminalBlock)
			{
				ShowOnHud((IMyTerminalBlock)block, show);

			}
		}

		private void ShowOnHud(IMyTerminalBlock block, bool show)
		{
			if (showDamagedOnHud && !(block is IMyRadioAntenna))
			{
				if (show)
				{

					try
					{
						if (block != null)
						{
							block.ApplyAction("ShowOnHUD_On");
						}
					}
					catch (Exception ex)
					{
						//Some exceptions are caused by grids connected via connector
						Echo(ex.Message + block.DisplayNameText);
					}

				}
				else
				{

					try
					{
						if (block != null)
						{
							block.ApplyAction("ShowOnHUD_Off");
						}
					}
					catch (Exception ex)
					{
						//Some exceptions are caused by grids connected via connector
						Echo(ex.Message + block.DisplayNameText);
					}

				}
			}
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

		public class DoorManager
		{
			private Program _program;
			public DoorManager(Program program)
			{
				_program = program;
			}

			public void ManageDoors()
			{
				List<IMyTerminalBlock> doors = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);
				for (int i = 0; i < doors.Count; i++)
				{
					doors[i].ApplyAction("Open_Off");
				}
			}
		}

	}
}
