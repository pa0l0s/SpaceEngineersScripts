using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI;
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
        /// 6. Assembler production manager
        /// Configures assemblers to work in cooperative mode.
        /// Displays components ammounts on LCD with Components tak in name.
        /// Adds components to build queue ammounts like below:
        /// 	{ "MyObjectBuilder_Component/SuperComputer", 0 },
        ///		{ "MyObjectBuilder_Component/SteelPlate", 50000},
        ///		{ "MyObjectBuilder_Component/MetalGrid", 25000},
        ///		{ "MyObjectBuilder_Component/InteriorPlate", 20000 },
        ///		{ "MyObjectBuilder_Component/Construction", 25000},
        ///		{ "MyObjectBuilder_Component/Computer", 15000},
        ///		{ "MyObjectBuilder_Component/Motor", 20000},
        ///		{ "MyObjectBuilder_Component/Thrust", 10000},
        ///		{ "MyObjectBuilder_Component/LargeTube", 15000},
        ///		{ "MyObjectBuilder_Component/SmallTube", 25000},
        ///		{ "MyObjectBuilder_Component/Reactor", 10000},
        ///		{ "MyObjectBuilder_Component/Superconductor", 10000},
        ///		{ "MyObjectBuilder_Component/GravityGenerator", 4000},
        ///		{ "MyObjectBuilder_Component/PowerCell", 5000}
        /// 
        /// More to be implemented...

        private Queue<IManagerTask> _taskQueue;
        private List<IManager> _managers;
        private Queue<IManager> _managersQueue;
        private bool HoldOnException = false;

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
            _managers.Add(new AssemblerManager(this, Me));
            _managers.Add(new DoorManager(this));
            _managers.Add(new SimpleInventoryManager(this, Me));
            //_managers.Add(new TestManager(this));
            _managers.Add(new DamageManager(this, Me));
            //_managers.Add(new HydrogenManager(this, Me));
            //_managers.Add(new DelyManager());

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

                    if (HoldOnException) throw ex;
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
                    if (HoldOnException) throw ex;
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
            private string _ignoreDoorNameTag = "Ignore";
            private Program _program;
            public DoorManager(Program program)
            {
                _program = program;
            }

            public IEnumerable<IManagerTask> GetTasks()
            {
                var doors = new List<IMyDoor>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyDoor>(doors);
                doors = doors.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreDoorNameTag.ToLower())).ToList();

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

        /*
		 * This list might also be helpful. This is the list of all the components that an Assembler can produce. It has the Item Name (not exact name, but not really needed), the TypeId, and SubTypeId. The TypeId and SubTypeId are useful for accessing items in scripts. This is the list used for the Visual Script Builder.

		Name, TypeId, SubTypeId
		"Construction Component", "Component", "Construction"
		"Metal Grid", "Component", "MetalGrid"
		"Interior Plate", "Component", "InteriorPlate"
		"Steel Plate", "Component", "SteelPlate"
		"Girder", "Component", "Girder"
		"Small Tube", "Component", "SmallTube"
		"Large Tube", "Component", "LargeTube"
		"Motor", "Component", "Motor"
		"Display", "Component", "Display"
		"Bulletproof Glass", "Component", "BulletproofGlass"
		"Superconductor Conduit", "Component", "Superconductor"
		"Computer", "Component", "Computer"
		"Reactor", "Component", "Reactor"
		"Thruster Component", "Component", "Thrust"
		"Gravity Generator Component", "Component", "GravityGenerator"
		"Medical Component", "Component", "Medical"
		"Radio-communication Component", "Component", "RadioCommunication"
		"Detector Component", "Component", "Detector"
		"Explosives", "Component", "Explosives"
		"Solar Cell", "Component", "SolarCell"
		"Power Cell", "Component", "PowerCell"
		"Welder", "PhysicalGunObject", "WelderItem"
		"Enhanced Welder", "PhysicalGunObject", "Welder2Item"
		"Proficient Welder", "PhysicalGunObject", "Welder3Item"
		"Elite Welder", "PhysicalGunObject", "Welder4Item"
		"Grinder", "PhysicalGunObject", "AngleGrinderItem"
		"Enhanced Grinder", "PhysicalGunObject", "AngleGrinder2Item"
		"Proficient Grinder", "PhysicalGunObject", "AngleGrinder3Item"
		"Elite Grinder", "PhysicalGunObject", "AngleGrinder4Item"
		"Drill", "PhysicalGunObject", "HandDrillItem"
		"Enhanced Drill", "PhysicalGunObject", "HandDrill2Item"
		"Proficient Drill", "PhysicalGunObject", "HandDrill3Item"
		"Elite Drill", "PhysicalGunObject", "HandDrill4Item"
		"Oxygen Bottle", "OxygenContainerObject", "OxygenBottle"
		"Hydrogen Bottle", "GasContainerObject", "HydrogenBottle"
		"Missile Container", "AmmoMagazine", "Missile200mm"
		"Ammo Container", "AmmoMagazine", "NATO_25x184mm"
		"Magazine", "AmmoMagazine", "NATO_5p56x45mm"
		 */

        public class SimpleInventoryManager : IManager
        {
            private const string defaultOreContainerNameTag = "Ores";
            private const string defaultIngotContainerNameTag = "Ingots";
            private const string defaultToolsContainerNameTag = "Tools";
            private const string defaultComponentsContainerNameTag = "Components";
            private const string defaultAmmunitionContainerNameTag = "Ammo";
            private const string defaultBottlesContainerNameTag = "Bottles";
            private const string defaultIgnoreContainerNameTag = "Ignore";
            private const string defaultForceMoveFromConnectedGridContainerNameTag = "ForceConnected";

            private const bool defaultIgnoreSorters = true;
            private const bool defaultIgnoreEjector = true;

            private Program _program;
            private IMyProgrammableBlock _me;
            private string _oreContainerNameTag;
            private string _ingotContainerNameTag;
            private string _toolsContainerNameTag;
            private string _componentsContainerNameTag;
            private string _ammunitionContainerNameTag;
            private string _bottlesContainerNameTag;
            private string _ignoreContainerNameTag;
            private string _forceMoveFromConnectedGridContainerNameTag;

            private bool _ignoreSorters;
            private bool _ignoreEjector;

            public SimpleInventoryManager(Program program, IMyProgrammableBlock me)
            {
                _program = program;
                _me = me;
                _oreContainerNameTag = defaultOreContainerNameTag;
                _ingotContainerNameTag = defaultIngotContainerNameTag;
                _toolsContainerNameTag = defaultToolsContainerNameTag;
                _componentsContainerNameTag = defaultComponentsContainerNameTag;
                _ammunitionContainerNameTag = defaultAmmunitionContainerNameTag;
                _bottlesContainerNameTag = defaultBottlesContainerNameTag;
                _ignoreContainerNameTag = defaultIgnoreContainerNameTag;
                _forceMoveFromConnectedGridContainerNameTag = defaultForceMoveFromConnectedGridContainerNameTag;

                _ignoreSorters = defaultIgnoreSorters;
                _ignoreEjector = defaultIgnoreEjector;
            }
            public IEnumerable<IManagerTask> GetTasks()
            {

                var tasks = new List<IManagerTask>();

                var blocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);
                if (blocks == null) return tasks;

                var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreContainerNameTag.ToLower())).ToList()
                    .Where(x => !(_ignoreSorters && x is IMyConveyorSorter))
                    .Where(x => !(_ignoreEjector && x.BlockDefinition.ToString().Contains("ConnectorSmall"))).ToList();

                tasks.AddRange(GetOreTasks(cargoContainers));
                tasks.AddRange(GetComponentsTasks(cargoContainers));
                tasks.AddRange(GetIngotsTasks(cargoContainers));
                tasks.AddRange(GetToolsTasks(cargoContainers));
                tasks.AddRange(GetAmmunitionTasks(cargoContainers));
                tasks.AddRange(GetBottlesTasks(cargoContainers));

                return tasks;

            }

            private IEnumerable<IManagerTask> GetOreTasks(List<IMyCargoContainer> cargoContainers)
            {
                return GetInventoryManagerTasks(cargoContainers, _oreContainerNameTag, "_Ore");
            }

            private IEnumerable<IManagerTask> GetComponentsTasks(List<IMyCargoContainer> cargoContainers)
            {
                return GetInventoryManagerTasks(cargoContainers, _componentsContainerNameTag, "_Component", addAssemblers: true, moveConnected: false);
            }

            private IEnumerable<IManagerTask> GetIngotsTasks(List<IMyCargoContainer> cargoContainers)
            {
                return GetInventoryManagerTasks(cargoContainers, _ingotContainerNameTag, "_Ingot", addRefieries: true);
            }

            private IEnumerable<IManagerTask> GetToolsTasks(List<IMyCargoContainer> cargoContainers)
            {
                var tasks = new List<IManagerTask>();

                tasks.AddRange(GetInventoryManagerTasks(cargoContainers, _toolsContainerNameTag, "_PhysicalGunObject", addAssemblers: true, addCockpit: false, moveConnected: false));
                tasks.AddRange(GetInventoryManagerTasks(cargoContainers, _toolsContainerNameTag, "_CaracterTool", addAssemblers: true, addCockpit: false, moveConnected: false));

                return tasks;
            }

            private IEnumerable<IManagerTask> GetBottlesTasks(List<IMyCargoContainer> cargoContainers)
            {
                var tasks = new List<IManagerTask>();

                tasks.AddRange(GetInventoryManagerTasks(cargoContainers, _bottlesContainerNameTag, "GasContainerObject", addAssemblers: true, addCockpit: false, moveConnected: false));
                tasks.AddRange(GetInventoryManagerTasks(cargoContainers, _bottlesContainerNameTag, "OxygenContainerObject", addAssemblers: true, addCockpit: false, moveConnected: false));

                return tasks;
            }

            private IEnumerable<IManagerTask> GetAmmunitionTasks(List<IMyCargoContainer> cargoContainers)
            {
                return GetInventoryManagerTasks(cargoContainers, _ammunitionContainerNameTag, "AmmoMagazine", addAssemblers: true, addCockpit: false, moveConnected: false);
            }

            private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyInventory> inventories, List<IMyCargoContainer> destinationCargos, string itemTag, bool moveConnected = true)
            {
                foreach (var inventory in inventories)
                {
                    if (inventory != null)
                    {
                        if (_program.GridTerminalSystem.GetBlockWithId(inventory.Owner.EntityId).CubeGrid.EntityId == _me.CubeGrid.EntityId || _program.GridTerminalSystem.GetBlockWithId(inventory.Owner.EntityId).DisplayNameText.ToLower().Contains(_forceMoveFromConnectedGridContainerNameTag.ToLower()) || moveConnected)
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
                }
            }

            private IEnumerable<IManagerTask> GetInventoryManagerTasks(List<IMyCargoContainer> cargoContainers, string containerTag, string itemTag, bool addAssemblers = false, bool addRefieries = false, bool addCockpit = true, bool addConnectors = true, bool moveConnected = true)
            {

                var tasks = new List<IManagerTask>();

                List<IMyCargoContainer> destinationCargos = cargoContainers.Where(x => x.DisplayNameText.ToLower().Contains(containerTag.ToLower()) && x.CubeGrid == _me.CubeGrid).OrderBy(x => x.DisplayNameText).ToList();// destinationCargos cargos only on current grid not connected
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

                    inventories.AddRange(blocks.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreContainerNameTag.ToLower())).Select(x => ((IMyAssembler)x).OutputInventory).ToList());

                }

                if (addRefieries)
                {
                    var blocks = new List<IMyTerminalBlock>();
                    _program.GridTerminalSystem.GetBlocksOfType<IMyRefinery>(blocks);
                    if (blocks == null) return tasks;

                    inventories.AddRange(blocks.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreContainerNameTag.ToLower())).Select(x => ((IMyRefinery)x).OutputInventory).ToList());

                }

                if (addCockpit)
                {
                    var blocks = new List<IMyTerminalBlock>();
                    _program.GridTerminalSystem.GetBlocksOfType<IMyCockpit>(blocks);
                    if (blocks == null) return tasks;

                    inventories.AddRange(blocks.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreContainerNameTag.ToLower())).Select(x => ((IMyCockpit)x).GetInventory()).ToList());
                }

                if (addConnectors)
                {
                    var blocks = new List<IMyTerminalBlock>();
                    _program.GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(blocks);
                    if (blocks == null) return tasks;

                    inventories.AddRange(blocks.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreContainerNameTag.ToLower())).Select(x => ((IMyShipConnector)x).GetInventory()).ToList());
                }

                try
                {
                    tasks.AddRange(GetInventoryManagerTasks(inventories, destinationCargos, itemTag, moveConnected: moveConnected));
                }
                catch (Exception ex)
                {
                    _program.Echo(ex.Message);
                }

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
                            _program.Echo($"{_program.GridTerminalSystem.GetBlockWithId(sourceInventory.Owner.EntityId).CubeGrid.EntityId} = {destinationContainer.CubeGrid.EntityId}");
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

                    var cargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => !x.DisplayNameText.ToLower().Contains("Tools".ToLower())).ToList();

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

                var assemblers = assemblerTypeBlocks.Where(x => !x.BlockDefinition.SubtypeName.ToLower().Contains(LargeStoneCrusherSubtypeName.ToLower()) && x.CubeGrid == _me.CubeGrid).ToList(); //Only assemblers from current grid

                var refineriesTypeBlocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyRefinery>(refineriesTypeBlocks);

                var refineries = refineriesTypeBlocks.Where(x => x.CubeGrid == _me.CubeGrid).ToList(); //Only from current grid

                var gravityGeneratorTypeBlocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyGravityGenerator>(gravityGeneratorTypeBlocks);

                var gravityGenerators = gravityGeneratorTypeBlocks.Where(x => x.CubeGrid == _me.CubeGrid).ToList(); //Only from current grid

                var blocks = new List<IMyTerminalBlock>();
                blocks.AddRange(assemblers);
                blocks.AddRange(refineries);
                blocks.AddRange(gravityGenerators);

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
            private const string _lcdNameTag = "ComponentsLCD";
            private const string _ignoreAssemblerNameTag = "Ignore";

            private static Dictionary<string, long> _desiredComponentsQuantity = new Dictionary<string, long>
            {
                //{ "MyObjectBuilder_Component/SuperComputer", 0 },
                //{ "MyObjectBuilder_Component/SteelPlate", 50000},
                //{ "MyObjectBuilder_Component/MetalGrid", 25000},
                //{ "MyObjectBuilder_Component/InteriorPlate", 20000 },
                //{ "MyObjectBuilder_Component/Construction", 25000},
                //{ "MyObjectBuilder_Component/Computer", 15000},
                //{ "MyObjectBuilder_Component/Motor", 20000},
                //{ "MyObjectBuilder_Component/Thrust", 10000},
                //{ "MyObjectBuilder_Component/LargeTube", 15000},
                //{ "MyObjectBuilder_Component/SmallTube", 25000},
                //{ "MyObjectBuilder_Component/Reactor", 10000},
                //{ "MyObjectBuilder_Component/Superconductor", 10000},
                //{ "MyObjectBuilder_Component/GravityGenerator", 4000},
                //{ "MyObjectBuilder_Component/PowerCell", 5000}

                //{ "MyObjectBuilder_Component/SuperComputer", 0 },
				{ "MyObjectBuilder_Component/SteelPlate", 5000},
				{ "MyObjectBuilder_Component/MetalGrid", 750},
				{ "MyObjectBuilder_Component/InteriorPlate", 2000 },
				{ "MyObjectBuilder_Component/Construction", 2000},
				{ "MyObjectBuilder_Component/Computer", 500},
				{ "MyObjectBuilder_Component/Motor", 750},
				{ "MyObjectBuilder_Component/Thrust", 1000},
				{ "MyObjectBuilder_Component/LargeTube", 500},
				{ "MyObjectBuilder_Component/SmallTube", 1000},
				{ "MyObjectBuilder_Component/Reactor", 500},
				{ "MyObjectBuilder_Component/Superconductor", 1000},
				{ "MyObjectBuilder_Component/GravityGenerator", 50},
				{ "MyObjectBuilder_Component/PowerCell", 500},
                { "MyObjectBuilder_Component/SolarCell", 200},
                { "MyObjectBuilder_Component/BulletproofGlass", 200},
                { "MyObjectBuilder_Component/Girder", 200},
                { "MyObjectBuilder_Component/Medical", 10},
                { "MyObjectBuilder_Component/Display", 50}
            };

            //defaut components quantity to produce
            private const long _defautDesiredComponentQuantity = 100;
            private const long _defaultMaxSingleItemAddToQueueAmmount = 100;

            private Program _program;
            private IMyProgrammableBlock _me;
            private IMyAssembler _mainAssembler;

            public AssemblerManager(Program program, IMyProgrammableBlock me)
            {
                _program = program;
                _me = me;
                _mainAssembler = ConfigureAssemblers();
            }

            public IEnumerable<IManagerTask> GetTasks()
            {
                var countedComponents = CountComponents();
                var tasks = new List<IManagerTask>();

                tasks.AddRange(GetDisplayComponentsCountOnLCDTasks(countedComponents));

                foreach (KeyValuePair<string, long> entry in countedComponents)
                {
                    // do something with entry.Value or entry.Key
                    var queueProductionItems = new List<MyProductionItem>();
                    _mainAssembler.GetQueue(queueProductionItems);

                    var queueProductionItemsDefinitions = queueProductionItems.Select(x => x.BlueprintId).ToList();
                    var blueprintNullable = GetItemDefinition(_program, entry.Key);
                    if (blueprintNullable.HasValue)
                    {
                        var blueprint = blueprintNullable.Value;
                        if (!queueProductionItemsDefinitions.Contains(blueprint))
                        {
                            if (entry.Value < GetDesiredQuantity(entry.Key))
                            {
                                var ammoutToBuild = GetDesiredQuantity(entry.Key) - entry.Value;
                                if (ammoutToBuild > _defaultMaxSingleItemAddToQueueAmmount) { ammoutToBuild = _defaultMaxSingleItemAddToQueueAmmount; }
                                tasks.Add(new AddToAssemblerQueueTask(_program, _mainAssembler, blueprint, ammoutToBuild));
                            }
                        }
                    }

                }

                return tasks;
            }

            private Dictionary<string, long> CountComponents()
            {
                var result = new Dictionary<string, long>();


                var blocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyCargoContainer>(blocks);

                var localGridCargoContainers = blocks.Select(x => (IMyCargoContainer)x).Where(x => x.CubeGrid == _me.CubeGrid).ToList();

                var inventories = new List<IMyInventory>();

                foreach (var cargoContainer in localGridCargoContainers)
                {
                    var cargoContainerEntity = (IMyEntity)cargoContainer;

                    inventories.Add((IMyInventory)cargoContainerEntity.GetInventory(0));
                }

                //Add assemblers
                blocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);
                inventories.AddRange(blocks.Select(x => ((IMyAssembler)x).OutputInventory).ToList());


                foreach (var inventory in inventories)
                {
                    var inventoryItems = new List<MyInventoryItem>();
                    inventory.GetItems(inventoryItems);

                    foreach (var inventoryItem in inventoryItems)
                    {
                        if (HasTagInName(inventoryItem, _componentsNameTag) && _desiredComponentsQuantity.ContainsKey(GetInventoryTypeName(inventoryItem)))
                        {
                            if (!result.ContainsKey(GetInventoryTypeName(inventoryItem)))
                            {
                              
                                result.Add(GetInventoryTypeName(inventoryItem), inventoryItem.Amount.ToIntSafe());
                                //_program.Echo($"added: {GetInventoryTypeName(inventoryItem)} - {inventoryItem.Amount.ToIntSafe()}"); //debug info
                            }
                            else
                            {
                                result[GetInventoryTypeName(inventoryItem)] += inventoryItem.Amount.ToIntSafe();
                            }
                        }
                    }
                }

                foreach (KeyValuePair<string, long> entry in _desiredComponentsQuantity)
                {
                    if (!result.ContainsKey(entry.Key))
                    {
                        result.Add(entry.Key, 0);
                    }
                }

                return result;
            }

            private IEnumerable<IManagerTask> GetDisplayComponentsCountOnLCDTasks(Dictionary<string, long> countedComponents)
            {
                //TODO: LCD display

                var blocks = new List<IMyTextPanel>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(blocks);
                if (blocks == null) throw new Exception("LCD display not found!");

                var lcds = blocks.Where(x => x.DisplayNameText.ToLower().Contains(_lcdNameTag.ToLower())).ToList();

                foreach (var lcd in lcds)
                {
                    yield return new DisplayOnLCDTask(_program, lcd, countedComponents);
                }

                yield break;
            }

            private IMyAssembler ConfigureAssemblers()
            {
                var blocks = new List<IMyAssembler>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(blocks);

                if (blocks == null) throw new Exception("Assembler not found!");

                var assemblers = blocks.Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreAssemblerNameTag.ToLower())).ToList();

                if (!assemblers.Any()) throw new Exception("Assembler not found!");

                var mainAssembler = assemblers.OrderBy(x => x.DisplayNameText).FirstOrDefault(x => x.BlockDefinition.TypeIdString == "MyObjectBuilder_Assembler" && x.CubeGrid == _me.CubeGrid); //Select first assembler according to alphabetical order
                assemblers.Remove(mainAssembler);

                foreach (var assembler in assemblers)
                {
                    assembler.Mode = MyAssemblerMode.Assembly;
                    assembler.CooperativeMode = true;
                    assembler.Repeating = false;
                }


                //_program.Echo($"assembler.Name: {assembler.Name} assembler.BlockDefinition: {assembler.BlockDefinition}");

                _program.Echo($"Main assembler name: {mainAssembler.DisplayNameText}");

                mainAssembler.CooperativeMode = false;
                mainAssembler.Mode = MyAssemblerMode.Assembly;
                mainAssembler.Repeating = false;

                return mainAssembler;
            }

            private static long GetDesiredQuantity(string key)
            {
                if (_desiredComponentsQuantity.ContainsKey(key))
                {
                    return _desiredComponentsQuantity[key];
                }
                return _defautDesiredComponentQuantity;
            }

            private static string GetInventoryTypeName(MyInventoryItem inventoryItem)
            {
                return $"{inventoryItem.Type.TypeId}/{inventoryItem.Type.SubtypeId}";

                //return inventoryItem.Type.SubtypeId;
            }

            private static MyDefinitionId? GetItemDefinition(Program program, string itemTypeName)
            {
                string blueprintIdString;
                MyDefinitionId blueprint;

                if (ItemNameToBlueprintDefinitionDictionary.ContainsKey(itemTypeName))
                {
                    blueprintIdString = ItemNameToBlueprintDefinitionDictionary[itemTypeName];
                    if (MyDefinitionId.TryParse(blueprintIdString, out blueprint))
                    {
                        return blueprint;
                    }
                }

                blueprintIdString = "MyObjectBuilder_BlueprintDefinition" + itemTypeName.Split('/')[1];

                if (MyDefinitionId.TryParse(blueprintIdString, out blueprint))
                {
                    return blueprint;
                }
                else if (MyDefinitionId.TryParse(blueprintIdString + "Component", out blueprint))
                {
                    return blueprint;
                }

                program.Echo($"No blueprint definition for {itemTypeName}");

                return null;
                //throw new Exception($"No blueprint definition for {itemTypeName}");
            }

            public class AddToAssemblerQueueTask : IManagerTask
            {
                Program _program;
                IMyAssembler _assembler;
                MyDefinitionId _itemTypeDefinition;
                long _amount;

                public AddToAssemblerQueueTask(Program program, IMyAssembler assembler, MyDefinitionId itemTypeDefinition, long amount)
                {
                    _program = program;
                    _assembler = assembler;
                    _itemTypeDefinition = itemTypeDefinition;
                    _amount = amount;
                }
                public void DoTask()
                {
                    var amount = (VRage.MyFixedPoint)(double)_amount;

                    if (_assembler.CanUseBlueprint(_itemTypeDefinition))
                    {
                        _assembler.AddQueueItem(_itemTypeDefinition, amount);
                        _program.Echo($"Add to prtoduction queue:{amount} x {_itemTypeDefinition} ");
                    }
                    else
                    {
                        _program.Echo($"Unable to build {_itemTypeDefinition} on assembler {_assembler.DisplayNameText}");
                    }
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

                    var sb = new StringBuilder();
                    sb.AppendLine($"Last update: {DateTime.Now}");

                    foreach (KeyValuePair<string, long> entry in _countedComponents)
                    {
                        sb.AppendLine($"{entry.Key.Split('/')[1]} - {entry.Value} - {((double)entry.Value) / ((double)GetDesiredQuantity(entry.Key)) * 100:0.##}%");

                        _textPanel.WriteText(sb.ToString());
                        _textPanel.ContentType = VRage.Game.GUI.TextPanel.ContentType.TEXT_AND_IMAGE;
                        //_textPanel.WritePublicText(sb.ToString());
                        //_textPanel.ShowPublicTextOnScreen();
                    }
                }

                public int GetPriority()
                {
                    return 10;
                }
            }

            public static Dictionary<string, string> ItemNameToBlueprintDefinitionDictionary = new Dictionary<string, string> {
                { "MyObjectBuilder_Component/Superconductor","MyObjectBuilder_BlueprintDefinition/Superconductor" },
                {"MyObjectBuilder_Component/BulletproofGlass", "MyObjectBuilder_BlueprintDefinition/BulletproofGlass" },
                {"MyObjectBuilder_Component/LargeTube", "MyObjectBuilder_BlueprintDefinition/LargeTube" },
                {"MyObjectBuilder_Component/Explosives","MyObjectBuilder_BlueprintDefinition/ExplosivesComponent" },
                { "MyObjectBuilder_Component/Thrust","MyObjectBuilder_BlueprintDefinition/ThrustComponent"},
                { "MyObjectBuilder_Component/Detector","MyObjectBuilder_BlueprintDefinition/DetectorComponent"},
                { "MyObjectBuilder_Component/Display","MyObjectBuilder_BlueprintDefinition/Display"},
                { "MyObjectBuilder_Component/Girder","MyObjectBuilder_BlueprintDefinition/GirderComponent"},
                {"MyObjectBuilder_Component/Medical", "MyObjectBuilder_BlueprintDefinition/MedicalComponent"},
                {"MyObjectBuilder_Component/InteriorPlate", "MyObjectBuilder_BlueprintDefinition/InteriorPlate"},
                { "MyObjectBuilder_Component/RadioCommunication","MyObjectBuilder_BlueprintDefinition/RadioCommunicationComponent"},
				//{"MyObjectBuilder_Component/Canvas", }
				{ "MyObjectBuilder_Component/SolarCell","MyObjectBuilder_BlueprintDefinition/SolarCell"},
                {"MyObjectBuilder_Component/Motor", "MyObjectBuilder_BlueprintDefinition/MotorComponent"},
                {"MyObjectBuilder_Component/GravityGenerator", "MyObjectBuilder_BlueprintDefinition/GravityGeneratorComponent"},
                { "MyObjectBuilder_Component/Computer","MyObjectBuilder_BlueprintDefinition/ComputerComponent" },
                { "MyObjectBuilder_Component/SmallTube","MyObjectBuilder_BlueprintDefinition/SmallTube"},
                { "MyObjectBuilder_Component/SteelPlate","MyObjectBuilder_BlueprintDefinition/SteelPlate"},
                { "MyObjectBuilder_Component/PowerCell", "MyObjectBuilder_BlueprintDefinition/PowerCell"},
                { "MyObjectBuilder_Component/Construction", "MyObjectBuilder_BlueprintDefinition/ConstructionComponent"},
                {"MyObjectBuilder_Component/Reactor", "MyObjectBuilder_BlueprintDefinition/ReactorComponent"},
                {"MyObjectBuilder_Component/MetalGrid", "MyObjectBuilder_BlueprintDefinition/MetalGrid" }


   };
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
            if (item == null) return false;

            return item.ToString().ToLower().Contains(itemTag.ToLower());
        }

        //Copy to here


    }
}
