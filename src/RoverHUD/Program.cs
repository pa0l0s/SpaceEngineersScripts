using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace ScriptingClass
{
    public class Program : MyGridProgram
    {
        /// Copy code from here

        /// <summary>
        /// RoverHUD - by Paolo
        ///
        /// Status screens for a small rover, written to every screen of the cockpit
        /// (plus optional extra LCDs). Shows what the vanilla HUD does not:
        ///   POWER   - battery %, stored MWh, in/out, net flow, time to empty/full,
        ///             solar / H2 engine / reactor output, total load
        ///   CARGO   - fill %, volume, cargo / ship mass, volume per category
        ///             (ice, ore, ingots, components, ammo, other)
        ///   DRIVE   - speed / speed limit, pitch/roll + tip-over warning, gravity,
        ///             altitude, wheels attached, power/strength/friction, gyro power
        ///   SYSTEMS - H2 / O2 tanks, connector, drills, damaged blocks
        ///   CYCLE   - rotates through all of the above (for cockpits with < 4 screens)
        ///
        /// Setup: paste into a PB on the rover, done. Uses the main cockpit (or the
        /// first cockpit found). Screen assignment lives in the PB CustomData
        /// (written with defaults on first run):
        ///   [RoverHUD]
        ///   Cockpit=          (cockpit name, empty = auto)
        ///   FontScale=1.9     (bigger = larger text; 1.0 = raw auto-fit)
        ///   Screen0=power
        ///   Screen1=cargo
        ///   Screen2=drive
        ///   Screen3=systems
        /// Valid values: power, cargo, drive, systems, cycle, off.
        ///
        /// Extra LCD: put "[Rover:cargo]" (or any screen name) in its CustomName.
        ///
        /// AUTO-TUNING (section [Tune] in CustomData, every round ~2 s):
        ///   wheel Strength ~ load^0.35, wheel Power ~ load, wheel Speed Limit
        ///   ~ gravity^0.6 and a bit lower when loaded, gyro power ~ mass. "Load" =
        ///   mass * gravity per wheel, so Moon / Mars / Earth adapt by themselves.
        ///   Blocks with "Ignore" in the name are left alone.
        ///   To teach it your taste: "tune off", set Strength/Power/Speed Limit/
        ///   gyro power by hand until it drives well, then "calibrate".
        ///
        /// Arguments: "refresh" - re-scan blocks + re-read CustomData now.
        ///            "tune off" / "tune on" - pause / resume auto-tuning.
        ///            "calibrate" - store the current hand-set values as [Tune].
        ///
        /// Server-friendly: AutoManagr-style task queue, ONE small task per run
        /// (Update10). A full round (scan + render all screens) takes ~10-15 runs.
        /// </summary>

        // ------- CONFIG -------
        const int ScreenCols = 18;          // font is auto-sized so this many chars fit...
        const int ScreenRows = 8;           // ...and this many lines
        // Auto-fit measured in-game comes out ~2x too small (text filled half the
        // cockpit screen), so the fitted size is multiplied by this. Overridable
        // per rover via FontScale in CustomData.
        const float DefaultFontScale = 1.9f;
        const int InventoriesPerTick = 8;
        const int DamageBlocksPerTick = 40;
        const int RefreshEveryRounds = 30;  // block list re-scan (~45 s)
        const int CycleEveryRounds = 3;     // "cycle" screen flips every N rounds
        const double TiltWarnDeg = 30;
        const double BatteryWarnPct = 20;
        const double CargoWarnPct = 95;
        const double GasWarnPct = 10;
        const char BarFill = '█';
        const char BarEmpty = '░';
        const string ExtraTag = "[Rover:";
        // Auto-tune clamps. The per-rover constants live in [Tune] in CustomData.
        const double StrengthMin = 5, PowerMin = 20;
        const double SpeedMin = 20, SpeedMax = 200;   // km/h
        const double GyroPowerMin = 0.1;
        const string SpeedLimitProp = "Speed Limit";  // wheel terminal property, km/h
        static readonly Color NormalColor = new Color(80, 230, 160);
        static readonly Color AlertColor = new Color(255, 80, 60);
        static readonly Color BgColor = new Color(0, 0, 0);
        // ----------------------

        enum Screen { Off, Power, Cargo, Drive, Systems, Cycle }

        class Display
        {
            public IMyTextSurface Surface;
            public Screen Kind;
        }

        // Cargo categories, each summed as volume in liters.
        const int CatIce = 0, CatOre = 1, CatIngot = 2, CatComp = 3, CatAmmo = 4, CatOther = 5;
        static readonly string[] CatNames = { "Ice", "Ore", "Ingots", "Components", "Ammo", "Other" };

        public interface IManager { IEnumerable<IManagerTask> GetTasks(); }

        public interface IManagerTask
        {
            void DoTask();
            int GetPriority();
        }

        class ActionTask : IManagerTask
        {
            readonly Action _action;
            readonly int _priority;
            public ActionTask(int priority, Action action) { _priority = priority; _action = action; }
            public void DoTask() { _action(); }
            public int GetPriority() { return _priority; }
        }

        // ---- task scheduling (AutoManagr pattern) ----
        readonly List<IManager> _managers = new List<IManager>();
        readonly Queue<IManager> _managersQueue = new Queue<IManager>();
        readonly Queue<IManagerTask> _taskQueue = new Queue<IManagerTask>();
        readonly List<IManagerTask> _scratchTasks = new List<IManagerTask>();
        int _round;
        bool _refreshRequested = true;

        // ---- block caches (filled by Refresh) ----
        IMyShipController _cockpit;
        readonly List<Display> _displays = new List<Display>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly List<IMyTerminalBlock> _inventoryBlocks = new List<IMyTerminalBlock>();
        readonly List<IMyGasTank> _tanks = new List<IMyGasTank>();
        readonly List<IMyMotorSuspension> _wheels = new List<IMyMotorSuspension>();
        readonly List<IMyShipConnector> _connectors = new List<IMyShipConnector>();
        readonly List<IMyShipDrill> _drills = new List<IMyShipDrill>();
        readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        readonly List<IMyTerminalBlock> _allBlocks = new List<IMyTerminalBlock>();

        // ---- published data (what the screens show) ----
        double _batStored, _batMax, _batIn, _batOut;
        int _batNonAuto;
        double _solarOut, _solarMax, _h2EngOut, _h2EngMax, _reactorOut, _reactorMax, _windOut, _windMax;
        double _loadOut, _loadMax;

        double _cargoVol, _cargoMaxVol, _cargoMass;
        readonly double[] _cat = new double[6];

        double _speed, _pitch, _roll, _tilt, _gravityG, _altitude, _baseMass, _totalMass, _physMass;
        bool _hasGravity, _hasAltitude, _flipped, _handBrake;
        int _wheelsAttached;
        double _wheelPower, _wheelFriction, _wheelStrength, _wheelSpeedLimit, _gyroPower;

        // ---- auto-tuning config (from [Tune] in CustomData) ----
        bool _autoSuspension = true, _autoSpeed = true, _autoGyro = true, _tunePaused, _hasSpeedLimit;
        double _strengthK, _strengthExp, _powerPerTon, _speedAt1g, _speedGravExp, _gyroTonsPerGyro;

        double _h2Fill, _o2Fill;
        int _h2Count, _o2Count;
        string _connectorStatus = "-";
        int _drillsOn;

        int _damagedCount, _brokenCount;
        readonly List<string> _damageList = new List<string>();

        // ---- in-progress scan state ----
        double _scanVol, _scanMaxVol, _scanMass;
        readonly double[] _scanCat = new double[6];
        readonly List<MyInventoryItem> _scratchItems = new List<MyInventoryItem>();
        int _scanDamaged, _scanBroken;
        readonly List<KeyValuePair<double, string>> _scanDamage = new List<KeyValuePair<double, string>>();

        readonly StringBuilder _sb = new StringBuilder(1024);
        readonly StringBuilder _measureSb = new StringBuilder("W");
        readonly MyIni _ini = new MyIni();
        string _status = "starting...";
        string _cockpitName = "";
        float _fontScale = DefaultFontScale;
        readonly string[] _screenConfig = { "power", "cargo", "drive", "systems" };

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _managers.Add(new RefreshManager(this));
            _managers.Add(new PowerManager(this));
            _managers.Add(new CargoManager(this));
            _managers.Add(new StatusManager(this));
            _managers.Add(new DamageManager(this));
            _managers.Add(new DisplayManager(this));
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
            switch (argument)
            {
                case "refresh": _refreshRequested = true; break;
                case "tune off": _tunePaused = true; _status = "Tuning paused - adjust by hand, then 'calibrate'"; break;
                case "tune on": _tunePaused = false; _status = "Tuning on"; break;
                case "calibrate": Calibrate(); break;
            }

            if (_taskQueue.Count == 0)
            {
                if (_managersQueue.Count == 0)
                {
                    _round++;
                    for (int i = 0; i < _managers.Count; i++) _managersQueue.Enqueue(_managers[i]);
                }

                var manager = _managersQueue.Dequeue();
                _scratchTasks.Clear();
                try
                {
                    _scratchTasks.AddRange(manager.GetTasks());
                }
                catch (Exception ex)
                {
                    _status = "ERR tasks: " + ex.Message;
                }
                _scratchTasks.Sort((a, b) => a.GetPriority().CompareTo(b.GetPriority()));
                for (int i = 0; i < _scratchTasks.Count; i++) _taskQueue.Enqueue(_scratchTasks[i]);
            }

            if (_taskQueue.Count > 0)
            {
                var task = _taskQueue.Dequeue();
                try
                {
                    task.DoTask();
                }
                catch (Exception ex)
                {
                    _status = "ERR: " + ex.Message;
                }
            }

            Echo("RoverHUD | round " + _round);
            Echo("Cockpit: " + (_cockpit != null ? _cockpit.CustomName : "NONE"));
            Echo("Displays: " + _displays.Count + " | Inv: " + _inventoryBlocks.Count + " | Blocks: " + _allBlocks.Count);
            Echo("Tasks: " + _taskQueue.Count + " | Managers: " + _managersQueue.Count);
            Echo($"Last run: {Runtime.LastRunTimeMs:0.000} ms | Instr: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount}");
            Echo(_status);
            for (int i = 0; i < _displays.Count; i++)
                Echo("Screen" + i + ": " + _displays[i].Kind + " font " + _displays[i].Surface.FontSize.ToString("0.00"));
        }

        // =====================================================================
        // Managers. GetTasks() is cheap - it only yields task descriptors.
        // =====================================================================

        class RefreshManager : IManager
        {
            readonly Program _p;
            public RefreshManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                bool cockpitGone = _p._cockpit == null || _p._cockpit.Closed;
                if (_p._refreshRequested || cockpitGone || _p._round % RefreshEveryRounds == 0)
                    yield return new ActionTask(0, _p.DoRefresh);
            }
        }

        class PowerManager : IManager
        {
            readonly Program _p;
            public PowerManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                yield return new ActionTask(0, _p.ScanPower);
            }
        }

        class CargoManager : IManager
        {
            readonly Program _p;
            public CargoManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                yield return new ActionTask(0, _p.BeginCargoScan);
                int count = _p._inventoryBlocks.Count;
                for (int start = 0; start < count; start += InventoriesPerTick)
                {
                    int s = start;
                    int e = Math.Min(start + InventoriesPerTick, count);
                    yield return new ActionTask(1, () => _p.ScanCargoChunk(s, e));
                }
                yield return new ActionTask(2, _p.FinishCargoScan);
            }
        }

        class StatusManager : IManager
        {
            readonly Program _p;
            public StatusManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                yield return new ActionTask(0, _p.ScanDrive);
                yield return new ActionTask(1, _p.ApplyTuning);
                yield return new ActionTask(2, _p.ScanSystems);
            }
        }

        class DamageManager : IManager
        {
            readonly Program _p;
            public DamageManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                // Only scan when someone actually shows the SYSTEMS screen.
                if (!_p.AnyDisplayShows(Screen.Systems)) yield break;

                yield return new ActionTask(0, _p.BeginDamageScan);
                int count = _p._allBlocks.Count;
                for (int start = 0; start < count; start += DamageBlocksPerTick)
                {
                    int s = start;
                    int e = Math.Min(start + DamageBlocksPerTick, count);
                    yield return new ActionTask(1, () => _p.ScanDamageChunk(s, e));
                }
                yield return new ActionTask(2, _p.FinishDamageScan);
            }
        }

        class DisplayManager : IManager
        {
            readonly Program _p;
            public DisplayManager(Program p) { _p = p; }
            public IEnumerable<IManagerTask> GetTasks()
            {
                // Snapshot - a refresh mid-round must not break the iteration.
                var snapshot = new List<Display>(_p._displays);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var d = snapshot[i];
                    if (d.Kind == Screen.Off) continue;
                    yield return new ActionTask(i, () => _p.RenderDisplay(d));
                }
            }
        }

        // =====================================================================
        // Refresh
        // =====================================================================

        void DoRefresh()
        {
            _refreshRequested = false;
            ReadConfig();

            _allBlocks.Clear();
            GridTerminalSystem.GetBlocksOfType(_allBlocks, b => b.IsSameConstructAs(Me));

            _batteries.Clear(); _producers.Clear(); _inventoryBlocks.Clear(); _tanks.Clear();
            _wheels.Clear(); _connectors.Clear(); _drills.Clear(); _gyros.Clear(); _displays.Clear();

            IMyShipController named = null, main = null, first = null;
            var extras = new List<IMyTerminalBlock>();

            for (int i = 0; i < _allBlocks.Count; i++)
            {
                var b = _allBlocks[i];

                if (b.HasInventory) _inventoryBlocks.Add(b);

                var bat = b as IMyBatteryBlock;
                if (bat != null) { _batteries.Add(bat); continue; }

                var prod = b as IMyPowerProducer;
                if (prod != null) _producers.Add(prod);

                var tank = b as IMyGasTank;
                if (tank != null) { _tanks.Add(tank); continue; }

                var wheel = b as IMyMotorSuspension;
                if (wheel != null) { _wheels.Add(wheel); continue; }

                var conn = b as IMyShipConnector;
                if (conn != null) { _connectors.Add(conn); continue; }

                var drill = b as IMyShipDrill;
                if (drill != null) { _drills.Add(drill); continue; }

                var gyro = b as IMyGyro;
                if (gyro != null) { _gyros.Add(gyro); continue; }

                var cockpit = b as IMyCockpit;
                if (cockpit != null && cockpit.SurfaceCount > 0)
                {
                    if (_cockpitName.Length > 0 && cockpit.CustomName == _cockpitName) named = cockpit;
                    if (cockpit.IsMainCockpit && main == null) main = cockpit;
                    if (first == null) first = cockpit;
                }

                if (b.CustomName.Contains(ExtraTag) && b is IMyTextSurfaceProvider) extras.Add(b);
            }

            _cockpit = named ?? main ?? first;
            _hasSpeedLimit = _wheels.Count > 0 && _wheels[0].GetProperty(SpeedLimitProp) != null;

            var provider = _cockpit as IMyTextSurfaceProvider;
            if (provider != null)
            {
                int n = provider.SurfaceCount;
                for (int i = 0; i < n; i++)
                {
                    Screen kind = ParseScreen(i < _screenConfig.Length ? _screenConfig[i] : "off");
                    // Fewer than 4 screens: the last one cycles through everything.
                    if (n < 4 && i == n - 1 && kind != Screen.Off) kind = Screen.Cycle;
                    AddDisplay(provider.GetSurface(i), kind);
                }
            }

            for (int i = 0; i < extras.Count; i++)
            {
                var p = extras[i] as IMyTextSurfaceProvider;
                if (extras[i] == _cockpit || p.SurfaceCount == 0) continue;
                AddDisplay(p.GetSurface(0), ParseScreen(ExtractExtraKind(extras[i].CustomName)));
            }

            _status = _cockpit == null ? "No cockpit with screens found!" : "OK";
        }

        void ReadConfig()
        {
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
            {
                // Not INI - do not overwrite whatever the user keeps there.
                // Tuning constants are unknown then - never drive blocks blind.
                _status = "CustomData is not INI - defaults, tuning off";
                _autoSuspension = _autoSpeed = _autoGyro = false;
                return;
            }
            // Missing keys (first run, or a config from an older version) are
            // written back with defaults so every knob is visible to tweak.
            // SetSectionComment throws on a missing section - comment only after
            // the first key has created it.
            _iniDirty = false;
            bool newHud = !_ini.ContainsSection("RoverHUD");
            bool newTune = !_ini.ContainsSection("Tune");
            _cockpitName = IniString("RoverHUD", "Cockpit", "");
            _fontScale = (float)MathHelper.Clamp(IniDouble("RoverHUD", "FontScale", DefaultFontScale), 0.2, 10.0);
            for (int i = 0; i < _screenConfig.Length; i++)
                _screenConfig[i] = IniString("RoverHUD", "Screen" + i, _screenConfig[i]).ToLowerInvariant();

            // Defaults differ per grid size (large wheels/gyros carry far more).
            bool large = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;
            _autoSuspension = IniBool("Tune", "AutoSuspension", true);
            _autoSpeed = IniBool("Tune", "AutoSpeed", true);
            _autoGyro = IniBool("Tune", "AutoGyro", true);
            // v1 config (sqrt strength, old speed default) -> migrate once: keep
            // the user's strength constant, bump the untouched old speed default.
            bool legacy = _ini.ContainsKey("Tune", "StrengthPerSqrtTon");
            double legacyK = _ini.Get("Tune", "StrengthPerSqrtTon").ToDouble(large ? 10 : 25);
            if (legacy)
            {
                _ini.Delete("Tune", "StrengthPerSqrtTon");
                if (!_ini.ContainsKey("Tune", "StrengthK")) _ini.Set("Tune", "StrengthK", legacyK);
                if (_ini.Get("Tune", "SpeedAt1g").ToDouble(0) == (large ? 90 : 110))
                    _ini.Set("Tune", "SpeedAt1g", large ? 110 : 130);
                _iniDirty = true;
            }
            _strengthK = IniDouble("Tune", "StrengthK", large ? 10 : 25);
            _strengthExp = MathHelper.Clamp(IniDouble("Tune", "StrengthExponent", 0.35), 0.0, 1.0);
            _powerPerTon = IniDouble("Tune", "PowerPerTon", large ? 6 : 40);
            _speedAt1g = IniDouble("Tune", "SpeedAt1g", large ? 110 : 130);
            _speedGravExp = MathHelper.Clamp(IniDouble("Tune", "SpeedGravityExponent", 0.6), 0.0, 2.0);
            _gyroTonsPerGyro = Math.Max(0.1, IniDouble("Tune", "GyroTonsPerGyro", large ? 400 : 15));

            if (newHud) _ini.SetSectionComment("RoverHUD", "Screens: power, cargo, drive, systems, cycle, off");
            if (newTune) _ini.SetSectionComment("Tune", "Auto-tuning. Tune by hand after 'tune off', then run 'calibrate'.");
            if (_iniDirty) Me.CustomData = _ini.ToString();
        }

        bool _iniDirty;

        string IniString(string section, string key, string def)
        {
            if (!_ini.ContainsKey(section, key)) { _ini.Set(section, key, def); _iniDirty = true; }
            return _ini.Get(section, key).ToString(def).Trim();
        }

        double IniDouble(string section, string key, double def)
        {
            if (!_ini.ContainsKey(section, key)) { _ini.Set(section, key, def); _iniDirty = true; }
            return _ini.Get(section, key).ToDouble(def);
        }

        bool IniBool(string section, string key, bool def)
        {
            if (!_ini.ContainsKey(section, key)) { _ini.Set(section, key, def); _iniDirty = true; }
            return _ini.Get(section, key).ToBoolean(def);
        }

        void AddDisplay(IMyTextSurface surface, Screen kind)
        {
            if (surface == null) return;
            _displays.Add(new Display { Surface = surface, Kind = kind });
            if (kind != Screen.Off) FitFont(surface);
        }

        // Pick a Monospace size so ScreenCols x ScreenRows fits on this surface.
        void FitFont(IMyTextSurface s)
        {
            s.ContentType = ContentType.TEXT_AND_IMAGE;
            s.Font = "Monospace";
            s.Alignment = TextAlignment.LEFT;
            s.TextPadding = 2f;
            s.BackgroundColor = BgColor;

            Vector2 one = s.MeasureStringInPixels(_measureSb, "Monospace", 1f);
            Vector2 area = s.SurfaceSize * 0.96f;
            if (one.X <= 0 || one.Y <= 0) return;

            float size = Math.Min(area.X / (ScreenCols * one.X), area.Y / (ScreenRows * one.Y));
            s.FontSize = MathHelper.Clamp(size * _fontScale, 0.3f, 10f);
        }

        bool AnyDisplayShows(Screen kind)
        {
            for (int i = 0; i < _displays.Count; i++)
                if (_displays[i].Kind == kind || _displays[i].Kind == Screen.Cycle) return true;
            return false;
        }

        // =====================================================================
        // Scans
        // =====================================================================

        void ScanPower()
        {
            _batStored = _batMax = _batIn = _batOut = 0;
            _batNonAuto = 0;
            for (int i = 0; i < _batteries.Count; i++)
            {
                var b = _batteries[i];
                if (b.Closed || !b.IsFunctional) continue;
                _batStored += b.CurrentStoredPower;
                _batMax += b.MaxStoredPower;
                _batIn += b.CurrentInput;
                _batOut += b.CurrentOutput;
                if (b.ChargeMode != ChargeMode.Auto) _batNonAuto++;
            }

            _solarOut = _solarMax = _h2EngOut = _h2EngMax = _reactorOut = _reactorMax = _windOut = _windMax = 0;
            _loadOut = _batOut;
            _loadMax = 0;
            for (int i = 0; i < _batteries.Count; i++)
                if (!_batteries[i].Closed && _batteries[i].IsFunctional) _loadMax += _batteries[i].MaxOutput;

            for (int i = 0; i < _producers.Count; i++)
            {
                var p = _producers[i];
                if (p.Closed || !p.IsFunctional) continue;
                double cur = p.CurrentOutput, max = p.MaxOutput;
                _loadOut += cur;
                _loadMax += max;

                string sub = p.BlockDefinition.SubtypeId.ToString();
                if (p is IMySolarPanel) { _solarOut += cur; _solarMax += max; }
                else if (p is IMyReactor) { _reactorOut += cur; _reactorMax += max; }
                else if (sub.Contains("Hydrogen")) { _h2EngOut += cur; _h2EngMax += max; }
                else if (sub.Contains("Wind")) { _windOut += cur; _windMax += max; }
            }
        }

        void BeginCargoScan()
        {
            _scanVol = _scanMaxVol = _scanMass = 0;
            Array.Clear(_scanCat, 0, _scanCat.Length);
        }

        void ScanCargoChunk(int start, int end)
        {
            end = Math.Min(end, _inventoryBlocks.Count);
            for (int b = start; b < end; b++)
            {
                var block = _inventoryBlocks[b];
                if (block == null || block.Closed) continue;

                // Every inventory counts: containers, cockpit, O2/H2 generator,
                // survival kit, connector, drills, reactors, H2 engines...
                for (int s = 0; s < block.InventoryCount; s++)
                {
                    var inv = block.GetInventory(s);
                    if (inv == null) continue;

                    _scanMass += (double)inv.CurrentMass;

                    // Ingame inventory volume is in m3.
                    _scanVol += (double)inv.CurrentVolume * 1000.0;
                    _scanMaxVol += (double)inv.MaxVolume * 1000.0;

                    _scratchItems.Clear();
                    inv.GetItems(_scratchItems);
                    for (int k = 0; k < _scratchItems.Count; k++)
                    {
                        var item = _scratchItems[k];
                        _scanCat[Classify(item.Type.TypeId, item.Type.SubtypeId)] += (double)item.Amount * ItemVolumeL(item.Type);
                    }
                }
            }
        }

        // Liters per unit (per kg for ore/ingots). Cached - GetItemInfo is a
        // definition lookup and the same few types repeat every scan.
        readonly Dictionary<MyItemType, double> _volumeCache = new Dictionary<MyItemType, double>();

        double ItemVolumeL(MyItemType type)
        {
            double v;
            if (!_volumeCache.TryGetValue(type, out v))
            {
                v = type.GetItemInfo().Volume * 1000.0;
                _volumeCache[type] = v;
            }
            return v;
        }

        static int Classify(string typeId, string subtypeId)
        {
            if (typeId.EndsWith("_Ore")) return subtypeId == "Ice" ? CatIce : CatOre;
            if (typeId.EndsWith("_Ingot")) return CatIngot;
            if (typeId.EndsWith("_Component")) return CatComp;
            if (typeId.EndsWith("_AmmoMagazine")) return CatAmmo;
            return CatOther;
        }

        void FinishCargoScan()
        {
            _cargoVol = _scanVol;
            _cargoMaxVol = _scanMaxVol;
            _cargoMass = _scanMass;
            Array.Copy(_scanCat, _cat, _cat.Length);
        }

        void ScanDrive()
        {
            var c = _cockpit;
            if (c == null || c.Closed) return;

            _speed = c.GetShipSpeed();
            _handBrake = c.HandBrake;

            var mass = c.CalculateShipMass();
            _baseMass = mass.BaseMass;
            _totalMass = mass.TotalMass;
            // PhysicalMass includes the inventory multiplier - what the wheels feel.
            _physMass = mass.PhysicalMass;

            Vector3D g = c.GetNaturalGravity();
            _hasGravity = g.LengthSquared() > 0.0001;
            if (_hasGravity)
            {
                _gravityG = g.Length() / 9.81;
                Vector3D up = -Vector3D.Normalize(g);
                MatrixD m = c.WorldMatrix;
                _pitch = AsinDeg(Vector3D.Dot(m.Forward, up));
                _roll = AsinDeg(Vector3D.Dot(m.Right, up));
                double upDot = MathHelper.Clamp(Vector3D.Dot(m.Up, up), -1.0, 1.0);
                _tilt = Math.Acos(upDot) * 180.0 / Math.PI;
                _flipped = upDot < 0;
            }
            else
            {
                _gravityG = _pitch = _roll = _tilt = 0;
                _flipped = false;
            }

            double elev;
            _hasAltitude = c.TryGetPlanetElevation(MyPlanetElevation.Surface, out elev);
            _altitude = _hasAltitude ? elev : 0;

            _wheelsAttached = 0;
            double pw = 0, fr = 0, st = 0, lim = 0;
            int n = 0;
            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (w.Closed) continue;
                if (w.IsAttached) _wheelsAttached++;
                pw += w.Power;
                fr += w.Friction;
                st += w.Strength;
                if (_hasSpeedLimit) lim += w.GetValueFloat(SpeedLimitProp);
                n++;
            }
            _wheelPower = n > 0 ? pw / n : 0;
            _wheelFriction = n > 0 ? fr / n : 0;
            _wheelStrength = n > 0 ? st / n : 0;
            _wheelSpeedLimit = n > 0 ? lim / n : 0;

            double gp = 0;
            int gn = 0;
            for (int i = 0; i < _gyros.Count; i++)
            {
                if (_gyros[i].Closed) continue;
                gp += _gyros[i].GyroPower;
                gn++;
            }
            _gyroPower = gn > 0 ? gp / gn : 0;
        }

        // =====================================================================
        // Auto-tuning (suspension strength/power, speed limit, gyro power).
        //
        // Load per wheel is "kg at 1 g": physical mass * gravity[g] / wheels, so
        // the same rover is ~4x lighter per wheel on the Moon than on Earth.
        //  - Strength ~ load^StrengthExponent. Keen squares strength internally,
        //    so 0.5 would hold ride height constant - but that tested too hard
        //    on Mars vs the Moon, so the default 0.35 lets heavy loads sag a bit.
        //    At 1 t per wheel (light rover on the Moon) the exponent cancels out.
        //  - Power: wheel force = PropulsionForce * Power, traction ~ load, so
        //    power ~ load stops a light rover in low gravity from wheel-spinning
        //    and popping wheelies.
        //  - Speed limit ~ g^SpeedGravityExponent: more gravity = more grip and
        //    less bouncing, so faster (Moon ~0.43x, Mars ~0.94x of SpeedAt1g),
        //    trimmed a little when heavily loaded (longer braking).
        //  - Gyro power ~ mass, so a light rover does not snap around.
        // Constants are starting points - tune by hand, then run "calibrate".
        // =====================================================================

        void ApplyTuning()
        {
            if (_tunePaused || _cockpit == null || _physMass <= 0) return;
            double tons = _physMass / 1000.0;

            if (_autoGyro && _gyros.Count > 0)
            {
                float target = (float)MathHelper.Clamp(tons / (_gyroTonsPerGyro * _gyros.Count), GyroPowerMin, 1.0);
                for (int i = 0; i < _gyros.Count; i++)
                {
                    var g = _gyros[i];
                    if (g.Closed || IsIgnored(g)) continue;
                    if (Math.Abs(g.GyroPower - target) >= 0.02f) g.GyroPower = target;
                }
            }

            // Wheels only matter on the ground in gravity.
            if (!_hasGravity || _wheelsAttached == 0) return;

            double loadT = tons * _gravityG / _wheelsAttached;
            float strength = (float)MathHelper.Clamp(_strengthK * Math.Pow(loadT, _strengthExp), StrengthMin, 100.0);
            float power = (float)MathHelper.Clamp(_powerPerTon * loadT, PowerMin, 100.0);
            float limit = (float)MathHelper.Clamp(_speedAt1g * Math.Pow(_gravityG, _speedGravExp) * LoadSpeedFactor(), SpeedMin, SpeedMax);

            for (int i = 0; i < _wheels.Count; i++)
            {
                var w = _wheels[i];
                if (w.Closed || IsIgnored(w)) continue;
                if (_autoSuspension)
                {
                    if (Math.Abs(w.Strength - strength) >= 1f) w.Strength = strength;
                    if (Math.Abs(w.Power - power) >= 1f) w.Power = power;
                }
                if (_autoSpeed && _hasSpeedLimit && Math.Abs(w.GetValueFloat(SpeedLimitProp) - limit) >= 2f)
                    w.SetValueFloat(SpeedLimitProp, limit);
            }
        }

        // 1.0 when empty, ~0.84 when the cargo equals the dry mass, min 0.75.
        double LoadSpeedFactor()
        {
            if (_physMass <= 0 || _baseMass <= 0) return 1.0;
            return MathHelper.Clamp(Math.Pow(_baseMass / _physMass, 0.25), 0.75, 1.0);
        }

        // Reverse of ApplyTuning: take the values currently set on the blocks
        // (tuned by hand while "tune off") and store the constants that produce
        // them at the current load + gravity.
        void Calibrate()
        {
            MyIniParseResult result;
            if (!_ini.TryParse(Me.CustomData, out result))
            {
                _status = "Calibrate: CustomData is not INI";
                return;
            }
            double tons = _physMass / 1000.0;
            var sb = new StringBuilder("Calibrated:");

            if (_hasGravity && _wheelsAttached > 0 && tons > 0)
            {
                double loadT = tons * _gravityG / _wheelsAttached;
                _strengthK = Math.Round(_wheelStrength / Math.Pow(loadT, _strengthExp), 1);
                _powerPerTon = Math.Round(_wheelPower / loadT, 1);
                _ini.Set("Tune", "StrengthK", _strengthK);
                _ini.Set("Tune", "PowerPerTon", _powerPerTon);
                sb.Append(" str/pwr");
                if (_hasSpeedLimit)
                {
                    _speedAt1g = Math.Round(_wheelSpeedLimit / (Math.Pow(_gravityG, _speedGravExp) * LoadSpeedFactor()));
                    _ini.Set("Tune", "SpeedAt1g", _speedAt1g);
                    sb.Append(" speed");
                }
            }
            if (_gyros.Count > 0 && _gyroPower > 0.001 && tons > 0)
            {
                _gyroTonsPerGyro = Math.Round(tons / (_gyroPower * _gyros.Count), 1);
                _ini.Set("Tune", "GyroTonsPerGyro", _gyroTonsPerGyro);
                sb.Append(" gyro");
            }
            Me.CustomData = _ini.ToString();
            _tunePaused = false;
            _status = sb.ToString();
        }

        static bool IsIgnored(IMyTerminalBlock b)
        {
            return b.CustomName.Contains("Ignore");
        }

        void ScanSystems()
        {
            double h2Cap = 0, h2Fill = 0, o2Cap = 0, o2Fill = 0;
            _h2Count = _o2Count = 0;
            for (int i = 0; i < _tanks.Count; i++)
            {
                var t = _tanks[i];
                if (t.Closed || !t.IsFunctional) continue;
                double cap = t.Capacity;
                if (t.BlockDefinition.SubtypeId.ToString().Contains("Hydrogen"))
                {
                    h2Cap += cap; h2Fill += cap * t.FilledRatio; _h2Count++;
                }
                else
                {
                    o2Cap += cap; o2Fill += cap * t.FilledRatio; _o2Count++;
                }
            }
            _h2Fill = h2Cap > 0 ? h2Fill / h2Cap : 0;
            _o2Fill = o2Cap > 0 ? o2Fill / o2Cap : 0;

            _connectorStatus = _connectors.Count == 0 ? "-" : "Free";
            for (int i = 0; i < _connectors.Count; i++)
            {
                var st = _connectors[i].Status;
                if (st == MyShipConnectorStatus.Connected) { _connectorStatus = "LOCKED"; break; }
                if (st == MyShipConnectorStatus.Connectable) _connectorStatus = "Ready";
            }

            _drillsOn = 0;
            for (int i = 0; i < _drills.Count; i++)
                if (!_drills[i].Closed && _drills[i].Enabled) _drillsOn++;
        }

        void BeginDamageScan()
        {
            _scanDamaged = _scanBroken = 0;
            _scanDamage.Clear();
        }

        void ScanDamageChunk(int start, int end)
        {
            end = Math.Min(end, _allBlocks.Count);
            for (int i = start; i < end; i++)
            {
                var b = _allBlocks[i];
                if (b == null || b.Closed) continue;
                var slim = b.CubeGrid.GetCubeBlock(b.Position);
                if (slim == null || slim.MaxIntegrity <= 0) continue;

                double hp = (slim.BuildIntegrity - slim.CurrentDamage) / slim.MaxIntegrity;
                if (hp >= 0.999) continue;

                _scanDamaged++;
                if (!b.IsFunctional) _scanBroken++;
                _scanDamage.Add(new KeyValuePair<double, string>(hp, b.CustomName));
            }
        }

        void FinishDamageScan()
        {
            _damagedCount = _scanDamaged;
            _brokenCount = _scanBroken;
            _scanDamage.Sort((a, b) => a.Key.CompareTo(b.Key));
            _damageList.Clear();
            int nameWidth = ScreenCols - 5;
            for (int i = 0; i < _scanDamage.Count && i < ScreenRows; i++)
            {
                string name = _scanDamage[i].Value;
                if (name.Length > nameWidth) name = name.Substring(0, nameWidth);
                _damageList.Add(name.PadRight(nameWidth) + Pct(_scanDamage[i].Key).PadLeft(5));
            }
        }

        // =====================================================================
        // Rendering. Every screen is laid out for ScreenCols x ScreenRows (18x8):
        // the less text, the bigger the auto-fitted font.
        // =====================================================================

        void RenderDisplay(Display d)
        {
            var s = d.Surface;
            if (s == null) return;

            Screen kind = d.Kind;
            if (kind == Screen.Cycle)
                kind = (Screen)((int)Screen.Power + (_round / CycleEveryRounds) % 4);

            _sb.Clear();
            bool alert;
            switch (kind)
            {
                case Screen.Power: alert = BuildPower(_sb); break;
                case Screen.Cargo: alert = BuildCargo(_sb); break;
                case Screen.Drive: alert = BuildDrive(_sb); break;
                default: alert = BuildSystems(_sb); break;
            }
            TrimToRows(_sb);

            // Cockpit screens get reset by the game when entering/leaving - re-assert.
            if (s.ContentType != ContentType.TEXT_AND_IMAGE || s.Font != "Monospace") FitFont(s);
            s.FontColor = alert ? AlertColor : NormalColor;
            s.WriteText(_sb);
        }

        // Lines past ScreenRows would be cut off by the screen anyway - drop them.
        static void TrimToRows(StringBuilder sb)
        {
            int lines = 0;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] != '\n') continue;
                if (++lines == ScreenRows) { sb.Length = i; return; }
            }
        }

        bool BuildPower(StringBuilder sb)
        {
            double frac = _batMax > 0 ? _batStored / _batMax : 0;
            double net = _batIn - _batOut;
            bool alert = _batteries.Count > 0 && frac * 100 < BatteryWarnPct;
            string timeLine = "IDLE";
            if (net < -0.0005)
            {
                double hours = _batStored / -net;
                timeLine = "EMPTY IN " + FormatHours(hours);
                if (hours < 0.25) alert = true;
            }
            else if (net > 0.0005 && _batStored < _batMax)
                timeLine = "FULL IN " + FormatHours((_batMax - _batStored) / net);

            Header(sb, "POWER", _batteries.Count > 0 ? Pct(frac) : "", alert);
            if (_batteries.Count == 0) sb.Append("NO BATTERIES\n");
            else
            {
                AppendBar(sb, frac, ScreenCols);
                sb.Append('\n');
                sb.Append(FormatEnergyPair(_batStored, _batMax)).Append("  x").Append(_batteries.Count).Append('\n');
                sb.Append("IN ").Append(FormatPower(_batIn).PadLeft(5))
                  .Append(" OUT ").Append(FormatPower(_batOut).PadLeft(5)).Append('\n');
                sb.Append("NET ").Append(FormatPower(net, true)).Append('\n');
                sb.Append(timeLine).Append('\n');
                if (_batNonAuto > 0) sb.Append("! ").Append(_batNonAuto).Append(" BAT NOT AUTO\n");
            }

            sb.Append("LOAD ").Append(FormatPower(_loadOut)).Append('/').Append(FormatPower(_loadMax)).Append('\n');
            if (_solarMax > 0) ProducerLine(sb, "SOLAR", _solarOut, _solarMax);
            if (_windMax > 0) ProducerLine(sb, "WIND", _windOut, _windMax);
            if (_h2EngMax > 0) ProducerLine(sb, "H2", _h2EngOut, _h2EngMax);
            if (_reactorMax > 0) ProducerLine(sb, "REACT", _reactorOut, _reactorMax);
            return alert;
        }

        static void ProducerLine(StringBuilder sb, string label, double cur, double max)
        {
            sb.Append(label.PadRight(6)).Append(FormatPower(cur)).Append('/').Append(FormatPower(max)).Append('\n');
        }

        bool BuildCargo(StringBuilder sb)
        {
            double frac = _cargoMaxVol > 0 ? _cargoVol / _cargoMaxVol : 0;
            bool alert = frac * 100 >= CargoWarnPct;

            Header(sb, "CARGO", Pct(frac), alert);
            AppendBar(sb, frac, ScreenCols);
            sb.Append('\n');
            sb.Append("VOL  ").Append(FormatVolume(_cargoVol)).Append('/').Append(FormatVolume(_cargoMaxVol)).Append('\n');
            sb.Append("MASS ").Append(FormatMass(_cargoMass)).Append('/').Append(FormatMass(_totalMass)).Append('\n');

            int shown = 0;
            for (int c = 0; c < _cat.Length; c++)
            {
                if (_cat[c] <= 0) continue;
                // Ice/ore/ingot amounts are kg, everything else is a piece count.
                sb.Append(CatNames[c].PadRight(ScreenCols - 7)).Append(FormatVolume(_cat[c]).PadLeft(7)).Append('\n');
                shown++;
            }
            if (shown == 0) sb.Append("EMPTY\n");
            return alert;
        }

        bool BuildDrive(StringBuilder sb)
        {
            int wheelsLost = _wheels.Count - _wheelsAttached;
            bool alert = _flipped || _tilt >= TiltWarnDeg || wheelsLost > 0;

            bool tuning = !_tunePaused && (_autoSuspension || _autoSpeed || _autoGyro);
            Header(sb, "DRIVE", (_handBrake ? "BRAKE " : "") + (tuning ? "AUTO" : _tunePaused ? "MAN" : ""), alert);
            sb.Append("SPD ").Append((_speed * 3.6).ToString("0.0"));
            if (_hasSpeedLimit && _wheels.Count > 0) sb.Append('/').Append(_wheelSpeedLimit.ToString("0"));
            sb.Append(" km/h\n");

            if (_hasGravity)
            {
                sb.Append('P').Append(Math.Abs(_pitch).ToString("0").PadLeft(3)).Append(_pitch >= 0 ? " UP" : " DN")
                  .Append("   R").Append(Math.Abs(_roll).ToString("0").PadLeft(3)).Append(_roll > 0 ? " L" : _roll < 0 ? " R" : "").Append('\n');
                if (_flipped) sb.Append("TILT  FLIPPED!\n");
                else
                {
                    sb.Append("TILT ");
                    AppendBar(sb, _tilt / 60.0, ScreenCols - 9);
                    sb.Append(_tilt.ToString("0").PadLeft(4)).Append('\n');
                }
                sb.Append("G ").Append(_gravityG.ToString("0.00"));
                if (_hasAltitude) sb.Append("  ALT ").Append(_altitude.ToString("0")).Append('m');
                sb.Append('\n');
            }
            else sb.Append("NO GRAVITY\n");

            if (_wheels.Count > 0)
            {
                sb.Append("WHEELS ").Append(_wheelsAttached).Append('/').Append(_wheels.Count)
                  .Append(wheelsLost > 0 ? " LOST!" : " OK").Append('\n');
                sb.Append("PWR ").Append(_wheelPower.ToString("0")).Append("% STR ").Append(_wheelStrength.ToString("0")).Append("%\n");
            }

            sb.Append("FRIC ").Append(_wheelFriction.ToString("0")).Append('%');
            if (_gyros.Count > 0) sb.Append(" GYRO ").Append((_gyroPower * 100).ToString("0")).Append('%');
            sb.Append('\n');
            return alert;
        }

        bool BuildSystems(StringBuilder sb)
        {
            bool alert = _brokenCount > 0
                || (_h2Count > 0 && _h2Fill * 100 < GasWarnPct)
                || (_o2Count > 0 && _o2Fill * 100 < GasWarnPct);

            Header(sb, "SYSTEMS", "", alert);
            if (_h2Count > 0) GasLine(sb, "H2", _h2Fill);
            if (_o2Count > 0) GasLine(sb, "O2", _o2Fill);
            sb.Append("CONN ").Append(_connectorStatus).Append('\n');
            if (_drills.Count > 0) sb.Append("DRILLS ").Append(_drillsOn).Append('/').Append(_drills.Count).Append(" ON\n");

            if (_damagedCount == 0) sb.Append("DMG none\n");
            else
            {
                sb.Append("DMG ").Append(_damagedCount);
                if (_brokenCount > 0) sb.Append("  BROKEN ").Append(_brokenCount);
                sb.Append('\n');
                for (int i = 0; i < _damageList.Count; i++) sb.Append(_damageList[i]).Append('\n');
            }
            return alert;
        }

        static void GasLine(StringBuilder sb, string label, double frac)
        {
            sb.Append(label).Append(' ');
            AppendBar(sb, frac, ScreenCols - 7);
            sb.Append(Pct(frac).PadLeft(4)).Append('\n');
        }

        // "TITLE        right" padded to ScreenCols; "!" marks an alert.
        static void Header(StringBuilder sb, string title, string right, bool alert)
        {
            if (alert) right = "! " + right;
            sb.Append(title).Append(right.PadLeft(Math.Max(1, ScreenCols - title.Length))).Append('\n');
        }

        // =====================================================================
        // Helpers - compact formats, no space before units.
        // =====================================================================

        static void AppendBar(StringBuilder sb, double frac, int width)
        {
            if (frac < 0) frac = 0;
            if (frac > 1) frac = 1;
            int filled = (int)Math.Round(frac * width);
            sb.Append(BarFill, filled).Append(BarEmpty, width - filled);
        }

        static double AsinDeg(double v)
        {
            return Math.Asin(MathHelper.Clamp(v, -1.0, 1.0)) * 180.0 / Math.PI;
        }

        static string Pct(double frac)
        {
            return (frac * 100).ToString("0") + "%";
        }

        // Power values from the API are in MW. "243kW", "4.5MW".
        static string FormatPower(double mw, bool signed = false)
        {
            string sign = signed && mw > 0 ? "+" : "";
            if (Math.Abs(mw) >= 1) return sign + mw.ToString("0.0#") + "MW";
            return sign + (mw * 1000).ToString("0") + "kW";
        }

        // "0.56/1.00MWh" or "558/900kWh"
        static string FormatEnergyPair(double cur, double max)
        {
            if (max >= 1) return cur.ToString("0.00") + "/" + max.ToString("0.00") + "MWh";
            return (cur * 1000).ToString("0") + "/" + (max * 1000).ToString("0") + "kWh";
        }

        static string FormatMass(double kg)
        {
            if (kg >= 1000000) return (kg / 1000000).ToString("0.0") + "kt";
            if (kg >= 100000) return (kg / 1000).ToString("0") + "t";
            if (kg >= 1000) return (kg / 1000).ToString("0.0") + "t";
            return kg.ToString("0") + "kg";
        }

        static string FormatVolume(double liters)
        {
            if (liters >= 1000) return (liters / 1000).ToString("0.0") + "kL";
            return liters.ToString("0") + "L";
        }

        static string FormatHours(double hours)
        {
            if (double.IsInfinity(hours) || double.IsNaN(hours) || hours > 999) return ">999h";
            int totalMin = (int)(hours * 60);
            if (totalMin < 60) return totalMin + "min";
            return (totalMin / 60) + "h " + (totalMin % 60) + "min";
        }

        static Screen ParseScreen(string s)
        {
            switch (s)
            {
                case "power": return Screen.Power;
                case "cargo": return Screen.Cargo;
                case "drive": return Screen.Drive;
                case "systems": return Screen.Systems;
                case "cycle": return Screen.Cycle;
                default: return Screen.Off;
            }
        }

        // "[Rover:cargo] LCD" -> "cargo"
        static string ExtractExtraKind(string name)
        {
            int open = name.IndexOf(ExtraTag);
            if (open < 0) return "off";
            int start = open + ExtraTag.Length;
            int close = name.IndexOf(']', start);
            if (close < 0) return "off";
            return name.Substring(start, close - start).Trim().ToLowerInvariant();
        }

        //Copy to here
    }
}
