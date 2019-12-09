using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace ScriptingClass
{
	class Program : MyGridProgram
	{

        /// Copy code from here


        /// <summary>
        /// Grid Manager
        /// By Paolo
        /// </summary>
        /// 
        /// 1. SimpleInventoryManager
        /// Moves resources to containers with tag in name. If more containers with same tag they will bee filled in alphabetical order.
        /// Available tags for containers:
        /// "Ores"
        /// "Ingots"
        /// "Tools"
        /// "Components"
        /// "Ignore" - this container will be ignored by script
        /// "ForceConnected" - will foce to manage inventorty in container on connected grid (ores by defaut are pulled also from connected inventories).
        /// 
        /// 2. DamageManager 
        /// Display damaged blocks on Hud if antenna available. If there is welder close by turns it on.
        /// 
        /// 3. DoorManager
        /// Simply closes all door once a while.
        /// 
        /// 4. Hydrogen Manager
        /// Turns on O2/H2 Generators when hydrogen level in tanks is below given level default 40%, turns of when above max level default 90%.
        /// 
        /// 5. Turn on block disabled by server
        /// On UD server refineries, assemblesr and some other blocks are disables bedore restart. This manager enshures that refineries and assemblers are turned on again.
        /// 
        /// More to be implemented...

        private Queue<IManagerTask> _taskQueue;
		private List<IManager> _managers;
		private Queue<IManager> _managersQueue;

		private Rotator _rotator;

		public Program()

		{
			// Configure this program to run the Main method every 100 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			_rotator = new Rotator(this);
			_managersQueue = new Queue<IManager>();
			_taskQueue = new Queue<IManagerTask>();
			_managers = new List<IManager>();
			_managers.Add(new TurnOnBlocksDisabledBySerwerManager(this, Me));
			_managers.Add(new DoorManager(this));
			_managers.Add(new SimpleInventoryManager(this, Me));
			//_managers.Add(new TestManager(this));
			_managers.Add(new DamageManager(this, Me));
			_managers.Add(new HydrogenManager(this, Me));
			_managers.Add(new DelyManager());
		}

		void Main()
		{
			_rotator.DoTask(); //rotator is done every step always.
			Echo(string.Format($"Tasks in queue: {_taskQueue.Count}"));

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
				Echo($"Getting tasks from: {manager.ToString()}");

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


		}

		public class DamageManager : IManager
		{
			private Program _program;
			private IMyProgrammableBlock _me;
			private List<MyRepairInfo> _activeRepairs;
			float minimumBlockHealth = 0.85f;
			float stopRepairHealth = 0.99f;
			TimeSpan repairTimeout = TimeSpan.FromSeconds(60);
			float maximumWelderDamagedDistance = 9.0f; //If lower has problems with distance to big blocks like big thrusters.
			TimeSpan timeoutDispose = TimeSpan.FromMinutes(5);
			bool showDamagedOnHud = true;
			int numberOfWeldersToFindNearest = 3; //Will search for 3 nearest to damaged block welders and try run them all to spped up repair.

			public DamageManager(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;

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
				blocksCube = blocksCube.Where(x => x.CubeGrid == _me.CubeGrid).ToList();

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

							//GetWelsersAndStartRepair(block);
						}

						else if (!block.IsFunctional)
						{
							ShowOnHud(block, true);

							//GetWelsersAndStartRepair(block);
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
				var doors = new List<IMyDoor>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);

				foreach (var door in doors)
				{
					if (door.Status == DoorStatus.Open)
					{
						yield return new DoorCloseTask(_program, door);
					}
				}
			}

			public class DoorCloseTask : IManagerTask
			{
				private Program _program;
				private IMyTerminalBlock _door;

				public DoorCloseTask(Program program, IMyDoor door)
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
			private const string defaultOreContainerNameTag = "Ores";
			private const string defaultIngotContainerNameTag = "Ingots";
			private const string defaultToolsContainerNameTag = "Tools";
			private const string defaultComponentsContainerNameTag = "Components";
			private const string defaultIgnoreContainerNameTag = "Ignore";
            private const string defaultForceMoveFromConnectedGridContainerNameTag = "ForceConnected";

            private Program _program;
			private IMyProgrammableBlock _me;
			private string _oreContainerNameTag;
			private string _ingotContainerNameTag;
			private string _toolsContainerNameTag;
			private string _componentsContainerNameTag;
			private string _ignoreContainerNameTag;
            private string _forceMoveFromConnectedGridContainerNameTag;

            public SimpleInventoryManager(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;
				_oreContainerNameTag = defaultOreContainerNameTag;
				_ingotContainerNameTag = defaultIngotContainerNameTag;
				_toolsContainerNameTag = defaultToolsContainerNameTag;
				_componentsContainerNameTag = defaultComponentsContainerNameTag;
				_ignoreContainerNameTag = defaultIgnoreContainerNameTag;
                _forceMoveFromConnectedGridContainerNameTag = defaultForceMoveFromConnectedGridContainerNameTag;


            }
			public IEnumerable<IManagerTask> GetTasks()
			{

				var tasks = new List<IManagerTask>();

				var blocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
				if (blocks == null) return tasks;

				var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.Contains(_ignoreContainerNameTag, StringComparison.InvariantCultureIgnoreCase)).ToList();


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
						if (HasTagInName(inventoryItem, "_CaracterTool"))
						{
							tasks.Add(new MoveItemTask(_program, inventoryItem, inventory, cargoContainers));
						}
					}
				}

				return tasks;
			}

			private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyInventory> inventories, List<IMyCargoContainer> destinationCargos, string itemTag)
			{
				if (itemTag == _toolsContainerNameTag || itemTag == _componentsContainerNameTag)
				{
					inventories = inventories.Where(x => _program.GridTerminalSystem.GetBlockWithId(x.Owner.EntityId).CubeGrid == _me.CubeGrid || _program.GridTerminalSystem.GetBlockWithId(x.Owner.EntityId).DisplayNameText.Contains(_forceMoveFromConnectedGridContainerNameTag,StringComparison.InvariantCultureIgnoreCase)).ToList(); //Tools and components are moved only from inventories in local grid to prevent moving stuff from connected ships 
				}
				foreach (var inventory in inventories)
				{
					var inventoryItems = new List<MyInventoryItem>();
					inventory.GetItems(inventoryItems);

					foreach (var inventoryItem in inventoryItems)
					{
						if (HasTagInName(inventoryItem, itemTag))
						{
							yield return new MoveItemTask(_program, inventoryItem, inventory, destinationCargos);
						}
					}
				}
			}

			private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyCargoContainer> cargoContainers, string containerTag, string itemTag, bool addAssemblers = false, bool addRefieries = false, bool addCockpit = true, bool addConnectors = true)
			{

				var tasks = new List<IManagerTask>();

				List<IMyCargoContainer> destinationCargos = cargoContainers.Where(x => x.DisplayNameText.Contains(containerTag, StringComparison.InvariantCultureIgnoreCase) && x.CubeGrid == _me.CubeGrid).OrderBy(x => x.DisplayNameText).ToList();// destinationCargos cargos only on current grid not connected
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
						if (WillItemFitToInventory(inventoryItem, destinationContainer.GetInventory(0)))
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

				private bool WillItemFitToInventory(MyInventoryItem inventoryItem, IMyInventory inventory)
				{
					float itemVolume = inventoryItem.Type.GetItemInfo().Volume;
					float inventoryFreeSpce = (float)inventory.MaxVolume - (float)inventory.CurrentVolume;

					return itemVolume < inventoryFreeSpce;
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

					var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.Contains("Tools", StringComparison.InvariantCultureIgnoreCase)).ToList();

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

		public class TurnOnBlocksDisabledBySerwerManager : IManager
		{
			private const string LargeStoneCrusherSubtypeName = "LargeStoneCrusher";
			private Program _program;
			private IMyProgrammableBlock _me;
			public TurnOnBlocksDisabledBySerwerManager(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;
			}

			public IEnumerable<IManagerTask> GetTasks()
			{
				var assemblerTypeBlocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(assemblerTypeBlocks);

				var assemblers = assemblerTypeBlocks.Where(x => !x.BlockDefinition.SubtypeName.Contains(LargeStoneCrusherSubtypeName, StringComparison.InvariantCultureIgnoreCase) && x.CubeGrid == _me.CubeGrid).ToList(); //Only assemblers from current grid

				var refineriesTypeBlocks = new List<IMyTerminalBlock>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyRefinery>(refineriesTypeBlocks);

				var refineries = refineriesTypeBlocks.Where(x => x.CubeGrid == _me.CubeGrid).ToList(); //Only from current grid

				var blocks = new List<IMyTerminalBlock>();
				blocks.AddRange(assemblers);
				blocks.AddRange(refineries);

				foreach (var block in blocks)
				{
					if (!block.IsWorking)
					{
						yield return new TurnOnTask(_program, block);
					}
				}
			}
		}

		public class HydrogenManager : IManager
		{
			private const double _DefaultTurnOnO2H2GeneratorLevel = 0.9;
			private const double _DefaultTurnOffO2H2GeneratorLevel = 0.99;
			private Program _program;
			private IMyProgrammableBlock _me;
			private double _turnOnO2H2GeneratorLevel;
			private double _turnOffO2H2GeneratorLevel;

			public HydrogenManager(Program program, IMyProgrammableBlock me)
			{
				_program = program;
				_me = me;
				_turnOnO2H2GeneratorLevel = _DefaultTurnOnO2H2GeneratorLevel;
				_turnOffO2H2GeneratorLevel = _DefaultTurnOffO2H2GeneratorLevel;
			}

			public HydrogenManager(Program program, IMyProgrammableBlock me, double turnOnO2H2GeneratorLevel, double turnOffO2H2GeneratorLevel)
			{
				_program = program;
				_me = me;
				_turnOnO2H2GeneratorLevel = turnOnO2H2GeneratorLevel;
				_turnOffO2H2GeneratorLevel = turnOffO2H2GeneratorLevel;
			}

			public IEnumerable<IManagerTask> GetTasks()
			{
				var generatorsTypeBlocks = new List<IMyGasGenerator>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyGasGenerator>(generatorsTypeBlocks);
				var generators = generatorsTypeBlocks.Where(x => x.CubeGrid == _me.CubeGrid).ToList();

				var avarangeTanksFillRatio = GetAvarangeTanksFillRatio();

				_program.Echo($"Avarange tanks fill ratio: {avarangeTanksFillRatio * 100:0.##}%");
				if (avarangeTanksFillRatio < _turnOnO2H2GeneratorLevel)
				{
					foreach (var generator in generators)
					{
						if (!generator.IsWorking)
						{
							yield return new TurnOnTask(_program, generator);
						}
					}
				}

				if (avarangeTanksFillRatio > _turnOffO2H2GeneratorLevel)
				{
					foreach (var generator in generators)
					{
						if (generator.IsWorking)
						{
							yield return new TurnOffTask(generator);
						}
					}
				}
			}

			private double GetAvarangeTanksFillRatio()
			{
				var hydrogenTanksTypeBlocks = new List<IMyGasTank>();
				_program.GridTerminalSystem.GetBlocksOfType<IMyGasTank>(hydrogenTanksTypeBlocks);
				var hydrogenTanks = hydrogenTanksTypeBlocks.Where(x => x.CubeGrid == _me.CubeGrid).ToList();

				double fillRatioSum = 0;
				int numberOfTanks = 0;
				foreach (var tank in hydrogenTanks)
				{
					fillRatioSum = fillRatioSum + tank.FilledRatio;
					numberOfTanks++;
				}

				if (numberOfTanks == 0)
				{
					return 0;
				}

				return fillRatioSum / numberOfTanks;

			}
		}

        public class AssemblerManager : IManager
        {
            private const string _componentsNameTag = "_Component";

            //defaut components quantity to produce
            private long _defautDesiredComponentQuantity = 1000;
            private Dictionary<string, long> _desiredComponentsQuantity; 

            private Program _program;
            private IMyProgrammableBlock _me;

            public AssemblerManager(Program program, IMyProgrammableBlock me)
            {
                _program = program;
                _me = me;

                _desiredComponentsQuantity = new Dictionary<string, long>();
                _desiredComponentsQuantity.Add("QuantumComponents", 0); //TODO: use proper component name for quantum computers.
                _desiredComponentsQuantity.Add("SteelPlates", 50000);
            }

            public IEnumerable<IManagerTask> GetTasks()
            {
                var countedComponents = CountComponents();
                var tasks = new List<IManagerTask>();

                tasks.AddRange(GetDisplayComponentsCountOnLCDTasks(countedComponents));

                var assembler = ConfigureAssembler();

                foreach (KeyValuePair<string, long> entry in countedComponents)
                {
                    // do something with entry.Value or entry.Key

                if(entry.Value< GetDesiredQuantity(entry.Key))
                    {
                        tasks.Add(new AddToAssemblerQueueTask(_program, assembler, entry.Key, GetDesiredQuantity(entry.Key)- entry.Value));
                    }

                }

                return tasks;
            }

            private Dictionary<string, long> CountComponents()
            {
                var result = new Dictionary<string, long>();


                var blocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
                if (blocks == null) return result;

                var localGridCargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => x.CubeGrid == _me.CubeGrid).ToList();

                var inventories = new List<IMyInventory>();

                foreach (var cargoContainer in localGridCargoContainers)
                {
                        var cargoContainerEntity = (IMyEntity)cargoContainer;

                        inventories.Add((IMyInventory)cargoContainerEntity.GetInventory(0));
                }

                foreach (var inventory in inventories)
                {
                    var inventoryItems = new List<MyInventoryItem>();
                    inventory.GetItems(inventoryItems);

                    foreach (var inventoryItem in inventoryItems)
                    {
                        if (HasTagInName(inventoryItem, _componentsNameTag))
                        {
                            if(!result.ContainsKey(inventoryItem.ToString()))
                            {
                                result.Add(inventoryItem.ToString(), inventoryItem.Amount.RawValue);
                            }
                            else
                            {
                                result[inventoryItem.ToString()] += inventoryItem.Amount.RawValue;
                            }
                        }
                    }
                }

                return result;
            }

            private IEnumerable<IManagerTask> GetDisplayComponentsCountOnLCDTasks(Dictionary<string, long> countedComponents)
            {
                //TODO: LCD display

                yield return new DisplayOnLCDTask(_program, null, countedComponents);

                yield break;
            }

            private IMyAssembler ConfigureAssembler()
            {
                var blocks = new List<IMyAssembler>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);
                if (blocks == null) throw new Exception("Assembler not found!");

                var assembler = blocks.OrderBy(x => x.DisplayNameText).FirstOrDefault(); //Select first assembler according to alphabetical order
                //throw new Exception(assembler.GetProperty("slaveMode").ToString()); //debug to check if there is property

                assembler.ApplyAction("slaveMode");

                return assembler;
            }

            private long GetDesiredQuantity(string key)
            {
                if(_desiredComponentsQuantity.ContainsKey(key))
                {
                    return _desiredComponentsQuantity[key];
                }
                return _defautDesiredComponentQuantity;
            }

            public class AddToAssemblerQueueTask : IManagerTask
            {
                Program _program;
                IMyAssembler _assembler;
                string _itemName;
                long _amount;

                public AddToAssemblerQueueTask(Program program, IMyAssembler assembler, string itemName,long amount)
                {
                    _program = program;
                    _assembler = assembler;
                    _itemName = itemName;
                    _amount = amount;
                }
                public void DoTask()
                {
                    var blueprint = MyDefinitionId.Parse($"MyObjectBuilder_BlueprintDefinition/{_itemName}"); //MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/Motor");

                    _assembler.AddQueueItem(blueprint, (double)_amount);
                }

                public int GetPriority()
                {
                    return 10;
                }
            }

            public class DisplayOnLCDTask : IManagerTask
            {
                Program _program;
                IMyTextPanel _textPanel;
                Dictionary<string, long> _countedComponents;


                public DisplayOnLCDTask(Program program, IMyTextPanel textPanel, Dictionary<string, long> countedComponents)
                {
                    _program = program;
                    _textPanel = textPanel;
                    _countedComponents = countedComponents;
                }
                public void DoTask()
                {
                    //TODO: Display on LCD
                }

                public int GetPriority()
                {
                    return 10;
                }
            }
        }

		public class TurnOnTask : IManagerTask
		{
			Program _program;
			IMyTerminalBlock _block;
			public TurnOnTask(Program program, IMyTerminalBlock block)
			{
				_program = program;
				_block = block;
			}
			public void DoTask()
			{
				_program.Echo($"Turn on: {_block.DefinitionDisplayNameText}");
				_block.ApplyAction("OnOff_On");
			}

			public int GetPriority()
			{
				return 1;
			}
		}

		public class TurnOffTask : IManagerTask
		{
			IMyTerminalBlock _block;
			public TurnOffTask(IMyTerminalBlock block)
			{
				_block = block;
			}
			public void DoTask()
			{
				_block.ApplyAction("OnOff_Off");
			}

			public int GetPriority()
			{
				return 1;
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

        public static bool HasTagInName(MyInventoryItem item, string itemTag)
        {
            return item.ToString().Contains(itemTag, StringComparison.InvariantCultureIgnoreCase);
        }

        //Copy to here


    }
}
