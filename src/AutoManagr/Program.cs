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
		/// Grid Manager
		/// </summary>
		/// 
		/// 1. SimpleInventoryManager
		/// Moves resources to containers with tag in name. If more containers with same tag they will bee filled in alphabetical order.
		/// Available tags for containers:
		/// "Ore"
		/// "Ingot"
		/// "Tools"
		/// "Components"
		/// "Ignor" - this container will be ignored by script
		/// 
		/// 2. DamageManager 
		/// Display damaged blocks on Hud if antenna available. If there is welder close by turns it on.
		/// 
		/// 3. DoorManager
		/// Simply closes all dorr once a while.
		/// 
		/// 4. More to be implemented
		/// Copy code from here



		private Queue<IManagerTask> _taskQueue;
		private List<IManager> _managers;
		private Queue<IManager> _managersQueue;

		private Rotator _rotator;

		public Program()

		{
			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			_rotator = new Rotator(this);
			_taskQueue = new Queue<IManagerTask>();

			_managers = new List<IManager>();
			_managersQueue = new Queue<IManager>();

			_managers.Add(new DoorManager(this));
			_managers.Add(new SimpleInventoryManager(this));
			//_managers.Add(new TestManager(this));
			_managers.Add(new DamageManager(this));
			_managers.Add(new DelyManager());
		}

		void Main()
		{
			_rotator.DoTask(); //rotator is done every step always.

			if (_managersQueue.Count == 0)
			{
				foreach (var manager in _managers)
				{
					_managersQueue.Enqueue(manager);
				}
			}

			else if (_taskQueue.Count == 0)
			{
				var manager = _managersQueue.Dequeue();
				var tasks = new List<IManagerTask>();
				try
				{
					tasks = manager.GetTasks().OrderBy(t => t.GetPriority()).ToList();
				}
				catch (Exception ex)
				{
					Echo(ex.Message);
				}

				foreach (var task in tasks)
				{
					_taskQueue.Enqueue(task);
				}
			}
			else
			{
				var currentTask = _taskQueue.Dequeue();

				Echo(string.Format($"Executing task: {currentTask.ToString()}"));
				try
				{
					currentTask.DoTask();
				}
				catch (Exception ex)
				{
					Echo(ex.Message);
				}
			}

			Echo(string.Format($"Tasks in queue: {_taskQueue.Count}"));
		}

		public class DamageManager : IManager
		{
			private Program _program;
			private List<MyRepairInfo> _activeRepairs;
			float minimumBlockHealth = 0.85f;
			float stopRepairHealth = 0.99f;
			TimeSpan repairTimeout = TimeSpan.FromSeconds(60);
			float maximumWelderDamagedDistance = 9.0f; //If lower has problems with distance to big blocks like big thrusters.
			TimeSpan timeoutDispose = TimeSpan.FromMinutes(5);
			bool showDamagedOnHud = true;
			int numberOfWeldersToFindNearest = 3; //Will search for 3 nearest to damaged block welders and try run them all to spped up repair.

			public DamageManager(Program program)
			{
				_program = program;

				_activeRepairs = new List<MyRepairInfo>();
			}

			public IEnumerable<IManagerTask> GetTasks()
			{
				var tasks = new List<IManagerTask>();
				GetDamagedBlocks();
				DisableWeldersWhenRapairDone();
				if (showDamagedOnHud)
				{
					//HighlightDamaged();
				}
				CleanupTimeouts();
				return tasks;
			}

			void GetDamagedBlocks()
			{
				//List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
				//this.GridTerminalSystem.GetBlocks(blocks);

				List<IMyCubeBlock> blocksCube = new List<IMyCubeBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyCubeBlock>(blocksCube);

				var textSb = new StringBuilder();
				textSb.AppendFormat($"Serching in {blocksCube.Count} blocks...");
				textSb.AppendFormat($"Repairs: {_activeRepairs.Count} Timeout:{_activeRepairs.Count(x => x.Timeout)}");

				_program.Echo(textSb.ToString());

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
				_program.GridTerminalSystem.GetBlocksOfType<IMyShipWelder>(allWelders);

				_program.Echo(string.Format($"Damaged block: {block.DisplayNameText}, {block.EntityId}"));
				var nearestWelders = allWelders.OrderBy(welder => Vector3D.Distance(block.GetPosition(), welder.GetPosition())).Take(numberOfWeldersToFindNearest).ToList();
				nearestWelders.ForEach(welder =>
				{
					RepairDamaged(block, welder);
				}
				);

			}

			void RepairDamaged(IMyCubeBlock block, IMyTerminalBlock nearestWelder)
			{
				if (_activeRepairs.FirstOrDefault(x => x.Welder == nearestWelder && x.DamagedBlock == block) == null) //&& DistanceSquared(nearestWelder, block)<maximumWelderDamagedDistance
				{
					_program.Echo(string.Format($"Welder: {nearestWelder.DisplayNameText}, {nearestWelder.EntityId}"));

					if (Vector3D.Distance(nearestWelder.GetPosition(), block.GetPosition()) < maximumWelderDamagedDistance)
					{

						try
						{
							nearestWelder.ApplyAction("OnOff_On");
						}
						catch (Exception ex)
						{
							_program.Echo(ex.Message);
						}

						_activeRepairs.Add(new MyRepairInfo(nearestWelder, block, DistanceSquared(nearestWelder, block)));
					}
					else
					{
						_program.Echo("Distance to big " + nearestWelder.DisplayNameText + ":" + DistanceSquared(nearestWelder, block).ToString());
					}
				}
			}

			void DisableWeldersWhenRapairDone()
			{
				_activeRepairs.ForEach(activeRepair =>
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
							_program.Echo(ex.Message);
						}
						activeRepair.Done = true;
						var text = activeRepair.Welder.CustomName + " Done";
						_program.Echo(text);
					}
					else if ((DateTime.UtcNow.Ticks - activeRepair.CreateDate.Ticks - repairTimeout.Ticks) > 0)
					{
						try
						{
							activeRepair.Welder.ApplyAction("OnOff_Off");
						}
						catch (Exception ex)
						{
							_program.Echo(ex.Message);
						}
						activeRepair.Timeout = true;
						var text = activeRepair.Welder.CustomName + " Timeout" + (DateTime.UtcNow.Ticks - activeRepair.CreateDate.Ticks - repairTimeout.Ticks).ToString();
						_program.Echo(text);
					}
				});

				_activeRepairs.RemoveAll(x => x.Done);
			}

			void HighlightDamaged()
			{
				List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);

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
								_program.Echo(ex.Message + blocks[i].DisplayNameText);
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
								_program.Echo(ex.Message + blocks[i].DisplayNameText);

							}
						}
					}

				}

			}

			void CleanupTimeouts()
			{
				_activeRepairs.RemoveAll(x => x.Timeout && (DateTime.UtcNow.Ticks - x.CreateDate.Ticks - timeoutDispose.Ticks) > 0);
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
							_program.Echo(ex.Message + block.DisplayNameText);
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
							_program.Echo(ex.Message + block.DisplayNameText);
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

			public double DistanceSquared(IMyCubeBlock block1, IMyCubeBlock block2)
			{
				return Vector3D.Distance(block1.GetPosition(), block2.GetPosition());
			}

			public class MyRepairInfo
			{
				public MyRepairInfo(IMyTerminalBlock welder, IMyCubeBlock damagedBlock, double welderDamagedDistance)
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
				public double WelderDamagedDistance { get; set; }
			}

			public class RepairManagerTask : IManagerTask
			{
				private Program _program;
				private IMyCubeBlock _block;
				private IMyTerminalBlock _nearestWelder;
				private List<MyRepairInfo> _activeRepairs;
				private float _maximumWelderDamagedDistance;

				public RepairManagerTask(Program program, IMyCubeBlock block, IMyTerminalBlock nearestWelder, ref List<MyRepairInfo> activeRepairs, float maximumWelderDamagedDistance)
				{
					_program = program;
					_block = block;
					_nearestWelder = nearestWelder;
					_activeRepairs = activeRepairs;
					_maximumWelderDamagedDistance = maximumWelderDamagedDistance;
				}
				public void DoTask()
				{
					RepairDamaged(_block, _nearestWelder);
				}

				public int GetPriority()
				{
					return 2;
				}

				void RepairDamaged(IMyCubeBlock block, IMyTerminalBlock nearestWelder)
				{
					if (_activeRepairs.FirstOrDefault(x => x.Welder == nearestWelder && x.DamagedBlock == block) == null) //&& DistanceSquared(nearestWelder, block)<maximumWelderDamagedDistance
					{
						_program.Echo(string.Format($"Welder: {nearestWelder.DisplayNameText}, {nearestWelder.EntityId}"));

						if (DistanceSquared(nearestWelder.GetPosition(), block.GetPosition()) < _maximumWelderDamagedDistance)
						{

							try
							{
								nearestWelder.ApplyAction("OnOff_On");
							}
							catch (Exception ex)
							{
								_program.Echo(ex.Message);
							}

							_activeRepairs.Add(new MyRepairInfo(nearestWelder, block, DistanceSquared(nearestWelder, block)));
						}
						else
						{
							_program.Echo("Distance to big " + nearestWelder.DisplayNameText + ":" + DistanceSquared(nearestWelder, block).ToString());
						}
					}
				}
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

			}
		}

		public class DoorManager : IManager
		{
			private Program _program;
			public DoorManager(Program program)
			{
				_program = program;
			}

			public IEnumerable<IManagerTask> GetTasks()
			{
				List<IMyTerminalBlock> doors = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);

				foreach (var door in doors)
				{
					yield return new DoorCloseTask(_program, door);
				}
			}

			public class DoorCloseTask : IManagerTask
			{
				private Program _program;
				private IMyTerminalBlock _door;

				public DoorCloseTask(Program program, IMyTerminalBlock door)
				{
					_program = program;
					_door = door;
				}
				public void DoTask()
				{
					_door.ApplyAction("Open_Off");
				}

				public int GetPriority()
				{
					return 1;
				}
			}
		}

		public class Rotator : IManagerTask
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

		public class SimpleInventoryManager : IManager
		{
			private const string defaultOreContainerNameTag = "Ore";
			private const string defaultIngotContainerNameTag = "Ingot";
			private const string defaultToolsContainerNameTag = "Tools";
			private const string defaultComponentsContainerNameTag = "Components";
			private const string defaultIgnoreContainerNameTag = "Ignor";

			private Program _program;
			private string _oreContainerNameTag;
			private string _ingotContainerNameTag;
			private string _toolsContainerNameTag;
			private string _componentsContainerNameTag;
			private string _ignoreContainerNameTag;

			public SimpleInventoryManager(Program program)
			{
				_program = program;
				_oreContainerNameTag = defaultOreContainerNameTag;
				_ingotContainerNameTag = defaultIngotContainerNameTag;
				_toolsContainerNameTag = defaultToolsContainerNameTag;
				_componentsContainerNameTag = defaultComponentsContainerNameTag;
				_ignoreContainerNameTag = defaultIgnoreContainerNameTag;

			}
			public IEnumerable<IManagerTask> GetTasks()
			{

				var tasks = new List<IManagerTask>();

				var blocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
				if (blocks == null) return tasks;

				var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.Contains(_ignoreContainerNameTag)).ToList();


				tasks.AddRange(GetOreTasks(cargoContainers));
				tasks.AddRange(GetComponentsTasks(cargoContainers));
				tasks.AddRange(GetIngotsTasks(cargoContainers));
				tasks.AddRange(GetToolsTasks(cargoContainers));

				return tasks;

			}

			private IEnumerable<IManagerTask> GetOreTasks(List<IMyCargoContainer> cargoContainers)
			{
				return GetInventoryManagerTasks(cargoContainers, _oreContainerNameTag, "_Ore");
			}

			private IEnumerable<IManagerTask> GetComponentsTasks(List<IMyCargoContainer> cargoContainers)
			{
				return GetInventoryManagerTasks(cargoContainers, _componentsContainerNameTag, "_Component", addAssemblers: true);
			}

			private IEnumerable<IManagerTask> GetIngotsTasks(List<IMyCargoContainer> cargoContainers)
			{
				return GetInventoryManagerTasks(cargoContainers, _ingotContainerNameTag, "_Ingot", addRefieries: true);
			}

			private IEnumerable<IManagerTask> GetToolsTasks(List<IMyCargoContainer> cargoContainers)
			{

				var tasks = new List<IManagerTask>();

				tasks.AddRange(GetInventoryManagerTasks(cargoContainers, _toolsContainerNameTag, "_PhysicalGunObject", addAssemblers: true));

				//add assemblers inventory

				var blocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);
				if (blocks == null) return tasks;

				List<IMyInventory> inventories = blocks.Select(x => ((IMyAssembler)x).OutputInventory).ToList();

				foreach (var inventory in inventories)
				{
					var inventoryItems = new List<MyInventoryItem>();
					inventory.GetItems(inventoryItems);
					foreach (var inventoryItem in inventoryItems)
					{
						if (HasItemTagInName(inventoryItem, "_CaracterTool"))
						{
							tasks.Add(new MoveItemTask(_program, inventoryItem, inventory, cargoContainers));
						}
					}
				}

				return tasks;
			}

			private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyInventory> inventories, List<IMyCargoContainer> destinationCargos, string itemTag)
			{
				foreach (var inventory in inventories)
				{
					var inventoryItems = new List<MyInventoryItem>();
					inventory.GetItems(inventoryItems);

					foreach (var inventoryItem in inventoryItems)
					{
						if (HasItemTagInName(inventoryItem, itemTag))
						{
							yield return new MoveItemTask(_program, inventoryItem, inventory, destinationCargos);
						}
					}
				}
			}

			private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyCargoContainer> cargoContainers, string containerTag, string itemTag, bool addAssemblers = false, bool addRefieries = false, bool addCockpit = true, bool addConnectors = true)
			{

				var tasks = new List<IManagerTask>();

				List<IMyCargoContainer> destinationCargos = cargoContainers.Where(x => x.DisplayNameText.Contains(containerTag)).OrderBy(x => x.DisplayNameText).ToList();
				if (destinationCargos == null || destinationCargos.Count == 0)
				{
					_program.Echo(string.Format($"No cargo conteiner with {containerTag} in name."));
					return tasks;
				}

				_program.Echo(String.Format($"Found {containerTag} containers count: {destinationCargos.Count}"));

				var inventories = new List<IMyInventory>();

				foreach (var cargoContainer in cargoContainers)
				{
					if (!destinationCargos.Contains(cargoContainer))
					{
						var cargoContainerEntity = (IMyEntity)cargoContainer;

						inventories.Add((IMyInventory)cargoContainerEntity.GetInventory(0));

					}

				}

				if (addAssemblers)
				{
					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);
					if (blocks == null) return tasks;

					inventories.AddRange(blocks.Select(x => ((IMyAssembler)x).OutputInventory).ToList());

				}

				if (addRefieries)
				{
					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyRefinery>(blocks);
					if (blocks == null) return tasks;

					inventories.AddRange(blocks.Select(x => ((IMyRefinery)x).OutputInventory).ToList());

				}

				if (addCockpit)
				{
					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyCockpit>(blocks);
					if (blocks == null) return tasks;

					inventories.AddRange(blocks.Select(x => ((IMyCockpit)x).GetInventory()).ToList());
				}

				if (addConnectors)
				{
					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(blocks);
					if (blocks == null) return tasks;

					inventories.AddRange(blocks.Select(x => ((IMyShipConnector)x).GetInventory()).ToList());
				}

				tasks.AddRange(GetInventoryManagerTasks(inventories, destinationCargos, itemTag));

				return tasks;

			}

			private bool IsItemOre(MyInventoryItem item)
			{

				return item.ToString().Contains("_Ore");
			}

			private bool IsItemComponent(MyInventoryItem item)
			{

				return item.ToString().Contains("_Component");
			}

			private bool IsItemTool(MyInventoryItem item)
			{

				return item.ToString().Contains("_CaracterTool");
			}

			private bool IsItemIngot(MyInventoryItem item)
			{

				return item.ToString().Contains("_Ingot");
			}

			private bool HasItemTagInName(MyInventoryItem item, string itemTag)
			{

				return item.ToString().Contains(itemTag);
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

			public class MoveItemTask : IManagerTask
			{
				private Program _program;

				private MyInventoryItem _inventoryItem;
				private IMyInventory _sourceInventory;
				private List<IMyCargoContainer> _destinationContainersList;

				public MoveItemTask(Program program, MyInventoryItem inventoryItem, IMyInventory sourceInventory, List<IMyCargoContainer> destinationContainersList)
				{

					_program = program;
					_inventoryItem = inventoryItem;
					_sourceInventory = sourceInventory;
					_destinationContainersList = destinationContainersList;

				}
				public void DoTask()
				{
					MoveItemToFirstNotFullContainer(_inventoryItem, _sourceInventory, _destinationContainersList);
				}

				public int GetPriority()
				{
					return 1;
				}

				private void MoveItemToFirstNotFullContainer(MyInventoryItem inventoryItem, IMyInventory sourceInventory, List<IMyCargoContainer> destinationContainersList)
				{

					foreach (var destinationContainer in destinationContainersList)
					{
						if (!((IMyEntity)destinationContainer).GetInventory(0).IsFull)
						{
							_program.Echo(String.Format($"Transfering {inventoryItem.ToString()} from: { _program.GridTerminalSystem.GetBlockWithId(sourceInventory.Owner.EntityId).DisplayNameText} to container {destinationContainer.DisplayNameText}"));
							sourceInventory.TransferItemTo(((IMyEntity)destinationContainer).GetInventory(0), inventoryItem);
							//throw new Exception("test");
							return;
						}
					}

					_program.Echo("Cargo is full.");
					return;
				}

			}

		}

		public class TestManager : IManager
		{
			private Program _program;
			public TestManager(Program program)
			{
				_program = program;
			}

			public IEnumerable<IManagerTask> GetTasks()
			{
				yield return new TestTask(_program);
			}

			public class TestTask : IManagerTask
			{
				private Program _program;
				public TestTask(Program program)
				{
					_program = program;
				}
				public void DoTask()
				{
					var blocks = new List<IMyTerminalBlock>();
					_program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
					if (blocks == null) return;

					var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.Contains("Tools")).ToList();

					foreach (var cargoContainer in cargoContainers)
					{
						var inventory = ((IMyEntity)cargoContainer).GetInventory(0);

						var inventoryItems = new List<MyInventoryItem>();
						inventory.GetItems(inventoryItems);

						foreach (var inventoryItem in inventoryItems)
						{
							_program.Echo(string.Format($"I: {inventoryItem.ToString()}"));
						}

						throw new Exception("test");

					}


				}

				public int GetPriority()
				{
					return 1;
				}
			}
		}

		public class DelyManager : IManager
		{
			public IEnumerable<IManagerTask> GetTasks()
			{
				yield return new DelayTask();
			}

			public class DelayTask : IManagerTask
			{
				public void DoTask()
				{
					//nothing to do

				}

				public int GetPriority()
				{
					return 1;
				}
			}
		}

		public interface IManager
		{
			IEnumerable<IManagerTask> GetTasks();
		}

		public interface IManagerTask
		{
			void DoTask();

			int GetPriority();
		}

		public abstract class BaseManagerTask : IManagerTask
		{
			protected Program _program;
			BaseManagerTask(Program program)
			{
				_program = program;
			}

			public abstract void DoTask();


			public abstract int GetPriority();

		}

		//CUT HERE


	}
}
