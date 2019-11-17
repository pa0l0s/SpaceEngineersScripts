using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace ScriptingClass
{
	class Program : MyGridProgram
	{


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

		private List<IProgramTask> _tasks;
		private Queue<IProgramTask> _taskQueue;
		private Rotator _rotator;

		public Program()

		{
			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			activeRepairs = new List<MyRepairInfo>();
			_tasks = new List<IProgramTask>();
			_taskQueue = new Queue<IProgramTask>();

			_rotator = new Rotator(this);


			_tasks.Add(new DoorManager(this));
			_tasks.Add(new SimpleInventoryManager(this));
		}

		void Main()
		{
			_rotator.DoTask(); //rotator is done every step always.

			GetDamagedBlocks();
			DisableWeldersWhenRapairDone();
			if (showDamagedOnHud)
			{
				//HighlightDamaged();
			}
			CleanupTimeouts();


			var taskSorted = _tasks.OrderBy(t => t.GetPriority()).ToArray();

			if (_taskQueue.Count == 0)
			{
				foreach (var task in taskSorted)
				{
					_taskQueue.Enqueue(task);
				}
			}

			Echo(string.Format($"Tasks in queue: {_taskQueue.Count}"));

			var currentTask = _taskQueue.Dequeue();

			Echo(string.Format($"Executing task: {currentTask.ToString()}"));
			currentTask.DoTask();



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

		public class DoorManager : IProgramTask
		{
			private const int defautRunFrequency = 10;//runs every ten'th call
			private int runNo;
			private int runFrequency;
			private Program _program;
			public DoorManager(Program program)
			{
				_program = program;
				runFrequency = defautRunFrequency;
				runNo = 0;
			}

			public void DoTask()
			{
				ManageDoors();
			}

			public int GetPriority()
			{
				return 1;
			}

			public void ManageDoors()
			{
				runNo++;
				if (runNo >= runFrequency)
				{
					runNo = 0;

					List<IMyTerminalBlock> doors = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);
					for (int i = 0; i < doors.Count; i++)
					{
						doors[i].ApplyAction("Open_Off");
					}
				}
			}
		}

		public class Rotator : IProgramTask
		{
			private string[] statusChars = new string[] { "/", "-", @"\", "|" };
			private int statusCharPosition = 0;
			private Program _program;
			public Rotator(Program program)
			{
				_program = program;
				statusCharPosition = 0;
			}

			public void DoTask()
			{

				_program.Echo(statusChars[statusCharPosition]);
				statusCharPosition++;
				if (statusCharPosition >= statusChars.Length)
				{
					statusCharPosition = 0;
				}

			}

			public int GetPriority()
			{
				return 100;
			}

		}

		public class SimpleInventoryManager : IProgramTask
		{
			private const string defaultOreContainerNameTag = "Ore";
			private const string defaultComponentsContainerNameTag = "Components";
			private const string defaultIgnoreContainerNameTag = "Ignore";
			private const int defautRunFrequency = 10;//runs every ten'th call


			private int _runNo;
			private int _runFrequency;


			private Program _program;
			private string _oreContainerNameTag;
			private string _componentsContainerNameTag;
			public SimpleInventoryManager(Program program)
			{
				_program = program;
				_oreContainerNameTag = defaultOreContainerNameTag;
				_componentsContainerNameTag = defaultComponentsContainerNameTag;

				_runFrequency = defautRunFrequency;
				_runNo = 0;
			}
			public void DoTask()
			{
				_runNo++;
				if (_runNo >= _runFrequency)
				{
					_runNo = 0;

					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
					if (blocks == null) return;

					var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).ToList();

					ManageOre(cargoContainers);

					ManageComponents(cargoContainers);
				}
			}
			public void ManageOre(List<IMyCargoContainer> cargoContainers)
			{



				List<IMyCargoContainer> oreCargos = cargoContainers.Where(x => x.DisplayNameText.Contains(_oreContainerNameTag)).ToList();
				if (oreCargos == null || oreCargos.Count == 0)
				{
					_program.Echo(string.Format($"No cargo conteiner with {_oreContainerNameTag} in name."));
					return;
				}

				_program.Echo(String.Format($"Found ore containers count: {oreCargos.Count}"));

				foreach (var cargoContainer in cargoContainers)
				{
					if (!oreCargos.Contains(cargoContainer))
					{
						var cargoContainerOwner = (IMyInventoryOwner)cargoContainer;

						var inventoryItems = new List<MyInventoryItem>();
						var inventory = (IMyInventory)cargoContainerOwner.GetInventory(0);
						inventory.GetItems(inventoryItems);

						foreach (var inventoryItem in inventoryItems)
						{
							if (IsItemOre(inventoryItem))
							{
								_program.Echo(string.Format($"Moving items from: {cargoContainer.DisplayNameText}"));
								MoveItemToFirstNotFullContainer(inventoryItem, inventory, oreCargos);
							}
						}
					}

				}

			}

			private void ManageComponents(List<IMyCargoContainer> cargoContainers)
			{

				List<IMyCargoContainer> componentCargos = cargoContainers.Where(x => x.DisplayNameText.Contains(_componentsContainerNameTag)).Select(x => (IMyCargoContainer)x).ToList();
				if (componentCargos == null || componentCargos.Count == 0)
				{
					_program.Echo(string.Format($"No cargo conteiner with {_oreContainerNameTag} in name."));
					return;
				}



				_program.Echo(String.Format($"Found components containers count: {componentCargos.Count}"));

				foreach (var cargoContainer in cargoContainers)
				{
					if (!componentCargos.Contains(cargoContainer))
					{
						var cargoContainerOwner = (IMyInventoryOwner)cargoContainer;

						var inventoryItems = new List<MyInventoryItem>();
						var inventory = (IMyInventory)cargoContainerOwner.GetInventory(0);
						inventory.GetItems(inventoryItems);

						foreach (var inventoryItem in inventoryItems)
						{
							if (IsItemComponent(inventoryItem))
							{
								_program.Echo(string.Format($"Moving items from: {cargoContainer.DisplayNameText}"));
								MoveItemToFirstNotFullContainer(inventoryItem, inventory, componentCargos);
							}
						}
					}

				}

				//add assemblers inventory

				var blocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);
				if (blocks == null) return;

				List<IMyInventory> inventories = blocks.Select(x => ((IMyAssembler)x).OutputInventory).ToList();

				foreach (var inventory in inventories)
				{
					var inventoryItems = new List<MyInventoryItem>();
					inventory.GetItems(inventoryItems);
					foreach (var inventoryItem in inventoryItems)
					{
						if (IsItemComponent(inventoryItem))
						{
							MoveItemToFirstNotFullContainer(inventoryItem, inventory, componentCargos);
						}
					}
				}
				

			}

			public int GetPriority()
			{
				return 1;
			}

			private void MoveItemToFirstNotFullContainer(MyInventoryItem inventoryItem, IMyInventory sourceInventory, List<IMyCargoContainer> destinationContainersList)
			{

				foreach (var destinationContainer in destinationContainersList)
				{
					if (!((IMyInventoryOwner)destinationContainer).GetInventory(0).IsFull)
					{
						_program.Echo(String.Format($"Transfering {inventoryItem.ToString()} from: {sourceInventory.Owner.DisplayName} to container {destinationContainer.DisplayNameText}"));
						sourceInventory.TransferItemTo(((IMyInventoryOwner)destinationContainer).GetInventory(0), inventoryItem);
						//throw new Exception("test");
						return;
					}
				}

				_program.Echo("Cargo is full.");
				return;
			}

			private bool IsItemOre(MyInventoryItem item)
			{

				return item.ToString().Contains("_Ore");
			}


			private bool IsItemComponent(MyInventoryItem item)
			{

				return item.ToString().Contains("_Component");
			}


			

			private string GetItemType(MyInventoryItem item)
			{
				string typeOfItem = item.Type.SubtypeId.ToString();
				string contentDescr = item.ToString();
				if (contentDescr.Contains("_Ore"))
				{
					if (typeOfItem != "Stone" && typeOfItem != "Ice")
						typeOfItem = typeOfItem + " Ore";
				}
				if (typeOfItem == "Stone" && contentDescr.Contains("_Ingot"))
					typeOfItem = "Gravel";
				return typeOfItem;
			}
		}

		public interface IProgramTask
		{
			void DoTask();

			int GetPriority();
		}




	}
}
