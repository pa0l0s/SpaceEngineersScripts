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
            Echo($"Tasks in queue: {_taskQueue.Count} | Managers pending: {_managersQueue.Count}");

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

            EchoRuntimeLoad();
        }

        // Logs computer (programmable block) load metrics. Useful in multiplayer
        // to verify that we are keeping the per-tick budget low.
        void EchoRuntimeLoad()
        {
            double instrPct = Runtime.MaxInstructionCount > 0
                ? 100.0 * Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount
                : 0.0;
            double depthPct = Runtime.MaxCallChainDepth > 0
                ? 100.0 * Runtime.CurrentCallChainDepth / Runtime.MaxCallChainDepth
                : 0.0;

            Echo($"Load: last {Runtime.LastRunTimeMs:0.00} ms, gap {Runtime.TimeSinceLastRun.TotalMilliseconds:0} ms");
            Echo($"Instr: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount} ({instrPct:0.0}%)");
            Echo($"Depth: {Runtime.CurrentCallChainDepth}/{Runtime.MaxCallChainDepth} ({depthPct:0.0}%)");
        }

        /// <summary>
        /// Scans local grid for damaged blocks and runs the closest welders.
        ///
        /// Designed for low per-tick load: GetTasks() only yields cheap descriptor
        /// objects, then the Main loop dequeues ONE subtask per tick (sorted by
        /// GetPriority(), lower first):
        ///   1. UpdateRepairTask        (one per active repair — turns welder off when target is healed/timed out)
        ///   2. CleanupRepairsTask      (drop finished/expired repairs)
        ///   3. RefreshWeldersTask      (re-fetch welders, skipping Ignored)
        ///   3. RefreshGridBlocksTask   (re-fetch local grid blocks)
        ///   4. CheckBlockTask          (batches of CheckBlocksPerTick blocks)
        ///   5. TurnOffIdleWeldersTask  (off any cached welder not in an active repair)
        ///
        /// Caches survive between rounds, so the per-block tasks emitted in round N
        /// operate on the snapshot populated by the refresh tasks of round N-1.
        /// Welders with IgnoreNameTag in CustomName are never touched by this
        /// manager (same convention as SimpleInventoryManager).
        /// </summary>
        public class DamageManager : IManager
        {
            private Program _program;
            private IMyProgrammableBlock _me;

            public List<IMyCubeBlock> CachedGridBlocks { get; private set; }
            public List<IMyTerminalBlock> CachedWelders { get; private set; }
            public List<MyRepairInfo> ActiveRepairs { get; private set; }

            public float MinimumBlockHealth = 0.85f;
            public float StopRepairHealth = 0.99f;
            public TimeSpan RepairTimeout = TimeSpan.FromSeconds(60);
            // If lower the script struggles with the size of big blocks like large thrusters.
            public float MaximumWelderDamagedDistance = 9.0f;
            public TimeSpan TimeoutDispose = TimeSpan.FromMinutes(5);
            public bool ShowDamagedOnHud = true;
            // For each damaged block we will pick this many nearest welders and turn them on.
            public int NumberOfWeldersToFindNearest = 3;
            // How many blocks one CheckBlockTask processes per tick. Tune to balance
            // per-tick instruction budget vs scan latency on large grids.
            public int CheckBlocksPerTick = 50;
            // Welders (and any blocks) whose CustomName contains this tag are left
            // alone, matching the convention used in SimpleInventoryManager.
            public string IgnoreNameTag = "Ignore";

            public DamageManager(Program program, IMyProgrammableBlock me)
            {
                _program = program;
                _me = me;

                CachedGridBlocks = new List<IMyCubeBlock>();
                CachedWelders = new List<IMyTerminalBlock>();
                ActiveRepairs = new List<MyRepairInfo>();
            }

            public IEnumerable<IManagerTask> GetTasks()
            {
                // 1. First re-check every remembered repair: if the block is back
                // to health (or the repair timed out / block is gone) the welder
                // is switched off here, before we do any new scanning work.
                var repairsSnapshot = new List<MyRepairInfo>(ActiveRepairs);
                foreach (var repair in repairsSnapshot)
                {
                    yield return new UpdateRepairTask(_program, this, repair);
                }

                // 2. Drop entries that UpdateRepairTask marked Done/Timeout so the
                // scan and the idle-shutoff below operate on a fresh set.
                yield return new CleanupRepairsTask(this);

                // 3. Refresh caches for the upcoming scan / for next round.
                yield return new RefreshWeldersTask(_program, this);
                yield return new RefreshGridBlocksTask(_program, _me, this);

                // 4. Scan blocks in batches. CheckBlockTask adds new entries to
                // ActiveRepairs when it finds damage and turns on the nearest
                // welder(s) — those welders are then "busy" for step 5.
                var blocksSnapshot = new List<IMyCubeBlock>(CachedGridBlocks);
                int batchSize = CheckBlocksPerTick > 0 ? CheckBlocksPerTick : 1;
                for (int start = 0; start < blocksSnapshot.Count; start += batchSize)
                {
                    int count = Math.Min(batchSize, blocksSnapshot.Count - start);
                    var batch = blocksSnapshot.GetRange(start, count);
                    yield return new CheckBlockTask(_program, this, batch);
                }

                // 5. Final safety pass: any cached (non-Ignored) welder that is
                // not tied to an active repair gets turned off. This guarantees
                // welders go OFF once no damaged blocks remain.
                yield return new TurnOffIdleWeldersTask(_program, this);
            }

            // Dr. Novikov snippet: http://forums.keenswh.com/threads/snippet-get-block-health.7368130/
            public float GetBlockHealth(IMyCubeBlock block)
            {
                IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
                return (slim.BuildIntegrity - slim.CurrentDamage) / slim.MaxIntegrity;
            }

            public void ShowOnHud(IMyCubeBlock block, bool show)
            {
                if (!ShowDamagedOnHud) return;
                var terminal = block as IMyTerminalBlock;
                if (terminal == null || terminal is IMyRadioAntenna) return;

                try
                {
                    terminal.ApplyAction(show ? "ShowOnHUD_On" : "ShowOnHUD_Off");
                }
                catch (Exception ex)
                {
                    // Some exceptions are caused by grids connected via connector.
                    _program.Echo(ex.Message);
                }
            }

            public class RefreshGridBlocksTask : IManagerTask
            {
                Program _program;
                IMyProgrammableBlock _me;
                DamageManager _mgr;

                public RefreshGridBlocksTask(Program program, IMyProgrammableBlock me, DamageManager mgr)
                {
                    _program = program;
                    _me = me;
                    _mgr = mgr;
                }

                public void DoTask()
                {
                    var blocks = new List<IMyCubeBlock>();
                    _program.GridTerminalSystem.GetBlocksOfType<IMyCubeBlock>(blocks);

                    _mgr.CachedGridBlocks.Clear();
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        if (blocks[i].CubeGrid == _me.CubeGrid)
                            _mgr.CachedGridBlocks.Add(blocks[i]);
                    }

                    _program.Echo($"Damage scan cached {_mgr.CachedGridBlocks.Count} grid blocks, {_mgr.ActiveRepairs.Count} active repairs");
                }

                public int GetPriority() { return 3; }
            }

            public class RefreshWeldersTask : IManagerTask
            {
                Program _program;
                DamageManager _mgr;

                public RefreshWeldersTask(Program program, DamageManager mgr)
                {
                    _program = program;
                    _mgr = mgr;
                }

                public void DoTask()
                {
                    var welders = new List<IMyTerminalBlock>();
                    _program.GridTerminalSystem.GetBlocksOfType<IMyShipWelder>(welders);

                    string ignore = _mgr.IgnoreNameTag.ToLower();
                    _mgr.CachedWelders.Clear();
                    for (int i = 0; i < welders.Count; i++)
                    {
                        if (!welders[i].CustomName.ToLower().Contains(ignore))
                            _mgr.CachedWelders.Add(welders[i]);
                    }
                }

                public int GetPriority() { return 3; }
            }

            public class CheckBlockTask : IManagerTask
            {
                Program _program;
                DamageManager _mgr;
                List<IMyCubeBlock> _batch;

                public CheckBlockTask(Program program, DamageManager mgr, List<IMyCubeBlock> batch)
                {
                    _program = program;
                    _mgr = mgr;
                    _batch = batch;
                }

                public void DoTask()
                {
                    if (_batch == null) return;

                    for (int i = 0; i < _batch.Count; i++)
                    {
                        var block = _batch[i];
                        if (block == null) continue;

                        float health;
                        try { health = _mgr.GetBlockHealth(block); }
                        catch { continue; } // block was likely destroyed between rounds

                        bool damaged = health < _mgr.MinimumBlockHealth || !block.IsFunctional;
                        _mgr.ShowOnHud(block, damaged);

                        if (damaged) StartRepair(block);
                    }
                }

                private void StartRepair(IMyCubeBlock block)
                {
                    Vector3D blockPos = block.GetPosition();

                    var nearest = _mgr.CachedWelders
                        .OrderBy(w => Vector3D.DistanceSquared(blockPos, w.GetPosition()))
                        .Take(_mgr.NumberOfWeldersToFindNearest);

                    foreach (var welder in nearest)
                    {
                        bool already = false;
                        for (int i = 0; i < _mgr.ActiveRepairs.Count; i++)
                        {
                            var r = _mgr.ActiveRepairs[i];
                            if (r.Welder == welder && r.DamagedBlock == block) { already = true; break; }
                        }
                        if (already) continue;

                        double distance = Vector3D.Distance(welder.GetPosition(), blockPos);
                        if (distance > _mgr.MaximumWelderDamagedDistance) continue;

                        try { welder.ApplyAction("OnOff_On"); }
                        catch (Exception ex) { _program.Echo(ex.Message); }

                        _mgr.ActiveRepairs.Add(new MyRepairInfo(welder, block, distance));
                        _program.Echo($"Repair: {welder.CustomName} -> {block.DisplayNameText}");
                    }
                }

                public int GetPriority() { return 4; }
            }

            public class UpdateRepairTask : IManagerTask
            {
                Program _program;
                DamageManager _mgr;
                MyRepairInfo _repair;

                public UpdateRepairTask(Program program, DamageManager mgr, MyRepairInfo repair)
                {
                    _program = program;
                    _mgr = mgr;
                    _repair = repair;
                }

                public void DoTask()
                {
                    if (_repair.Done || _repair.Timeout) return;

                    float health;
                    try { health = _mgr.GetBlockHealth(_repair.DamagedBlock); }
                    catch
                    {
                        // damaged block went away — stop the welder and mark done
                        TurnOffWelder();
                        _repair.Done = true;
                        return;
                    }

                    if (health > _mgr.StopRepairHealth)
                    {
                        TurnOffWelder();
                        _repair.Done = true;
                        _program.Echo($"{_repair.Welder.CustomName} done");
                    }
                    else if ((DateTime.UtcNow - _repair.CreateDate) > _mgr.RepairTimeout)
                    {
                        TurnOffWelder();
                        _repair.Timeout = true;
                        _program.Echo($"{_repair.Welder.CustomName} timeout");
                    }
                }

                private void TurnOffWelder()
                {
                    try { _repair.Welder.ApplyAction("OnOff_Off"); }
                    catch (Exception ex) { _program.Echo(ex.Message); }
                }

                public int GetPriority() { return 1; }
            }

            public class CleanupRepairsTask : IManagerTask
            {
                DamageManager _mgr;

                public CleanupRepairsTask(DamageManager mgr) { _mgr = mgr; }

                public void DoTask()
                {
                    _mgr.ActiveRepairs.RemoveAll(r => r.Done);
                    _mgr.ActiveRepairs.RemoveAll(r =>
                        r.Timeout && (DateTime.UtcNow - r.CreateDate) > _mgr.TimeoutDispose);
                }

                public int GetPriority() { return 2; }
            }

            public class TurnOffIdleWeldersTask : IManagerTask
            {
                Program _program;
                DamageManager _mgr;

                public TurnOffIdleWeldersTask(Program program, DamageManager mgr)
                {
                    _program = program;
                    _mgr = mgr;
                }

                public void DoTask()
                {
                    for (int i = 0; i < _mgr.CachedWelders.Count; i++)
                    {
                        var welder = _mgr.CachedWelders[i];

                        bool busy = false;
                        for (int j = 0; j < _mgr.ActiveRepairs.Count; j++)
                        {
                            if (_mgr.ActiveRepairs[j].Welder == welder) { busy = true; break; }
                        }
                        if (busy) continue;

                        // Skip if already off — avoids paying ApplyAction cost every round.
                        var functional = welder as IMyFunctionalBlock;
                        if (functional == null || !functional.Enabled) continue;

                        try { welder.ApplyAction("OnOff_Off"); }
                        catch (Exception ex) { _program.Echo(ex.Message); }
                    }
                }

                public int GetPriority() { return 5; }
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
            private const string defaultIgnoreNameTag = "Ignore";

            private string _ignoreNameTag;

            private const string LargeStoneCrusherSubtypeName = "LargeStoneCrusher";
            private Program _program;
            private IMyProgrammableBlock _me;
            public TurnOnBlocksDisabledBySerwerManager(Program program, IMyProgrammableBlock me)
            {
                _program = program;
                _me = me;

                _ignoreNameTag = defaultIgnoreNameTag;
            }

            public IEnumerable<IManagerTask> GetTasks()
            {
                var assemblerTypeBlocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyAssembler>(assemblerTypeBlocks);

                var assemblers = assemblerTypeBlocks
                    .Where(x => !x.BlockDefinition.SubtypeName.ToLower().Contains(LargeStoneCrusherSubtypeName.ToLower()) && x.CubeGrid == _me.CubeGrid)
                    .Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreNameTag.ToLower())).ToList(); //Only assemblers from current grid

                var refineriesTypeBlocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyRefinery>(refineriesTypeBlocks);

                var refineries = refineriesTypeBlocks
                    .Where(x => x.CubeGrid == _me.CubeGrid).Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreNameTag.ToLower())).ToList(); //Only from current grid

                var gravityGeneratorTypeBlocks = new List<IMyTerminalBlock>();
                _program.GridTerminalSystem.GetBlocksOfType<IMyGravityGenerator>(gravityGeneratorTypeBlocks);

                var gravityGenerators = gravityGeneratorTypeBlocks
                    .Where(x => x.CubeGrid == _me.CubeGrid).Where(x => !x.DisplayNameText.ToLower().Contains(_ignoreNameTag.ToLower())).ToList(); //Only from current grid

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
