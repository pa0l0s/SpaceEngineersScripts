using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace ScriptingClass
{
    public class Program : MyGridProgram
    {
        /// Copy code from here

        /// <summary>
        /// MissileGuidance — by Paolo
        ///
        /// Camera-raycast targeting + gyro-steered guided missiles, driven from a
        /// Programmable Block on the FIRING ship. Each missile rides on the firing
        /// ship pre-launch (Merge Block + Small Connector) and is cut loose on the
        /// "launch" command. Supports MULTIPLE missiles — one group per missile.
        ///
        /// ---------------------------------------------------------------------
        /// SETUP (everything is found by BLOCK GROUP, so no per-block naming):
        ///   FIRING SHIP:
        ///     * This Programmable Block (runs this script).
        ///     * Targeting sources, found via the  Missile_Cameras  block group:
        ///         - Camera blocks aimed FORWARD (precise, operator-aimed raycast —
        ///           you point the ship at the target), and/or
        ///         - AI Turrets (free per-tick detection of whatever they lock onto).
        ///       Cameras take priority; turrets acquire when no camera hit and keep
        ///       refreshing the SAME locked target. Either source alone is enough.
        ///       If the Missile_Cameras group does NOT exist, the script falls back to
        ///       EVERY camera + turret on the ship (tag any you want excluded "Ignore").
        ///     * A cockpit/seat or LCD named per LcdName (status readout). If none
        ///       matches that name, the FIRST cockpit/seat on the ship is used.
        ///   MISSILES — two ways to define them (groups win if present):
        ///     (a) ONE GROUP PER MISSILE named Missile1, Missile2, ... Put each
        ///         missile's blocks in its own group; the script finds them BY TYPE.
        ///     (b) AUTO-DISCOVERY: run "setup" with NO missile groups and the script
        ///         uses the separation trick to find each missile and tags its blocks
        ///         MGID=n in CustomData (a PB cannot create real groups). Tag any
        ///         structural (ship-holding) merge block "Ignore" first so it is not
        ///         opened. Either way the per-missile blocks are:
        ///       * Warhead(s)
        ///       * Thruster(s) — HYDROGEN, ION, or ATMOSPHERIC (auto-detected). Flame
        ///         out the TAIL pushes toward the nose. H2 tank is required ONLY for
        ///         hydrogen thrusters; ion/atmo missiles need no tank.
        ///       * Gyroscope(s)
        ///       * Small H2 Tank(s)   (only needed for hydrogen thrusters)
        ///       * Small Connector(s) — locked while parked, released on launch
        ///       * Merge Block(s)
        ///       * Battery(ies)       — set to Recharge while parked, Auto on launch
        ///   Missiles fire in name order (Missile1, Missile2, ...).
        ///
        /// ---------------------------------------------------------------------
        /// WHY CACHING THE MISSILE BLOCKS MATTERS (the trick you tested):
        ///   A Programmable Block can normally only see blocks on its OWN grid
        ///   (or grids connected to it). The instant a missile separates, the
        ///   GridTerminalSystem can no longer FIND that missile's blocks — so we
        ///   grab the references WHILE still merged and KEEP them. Those cached
        ///   references stay valid and controllable on the now-independent grid. So:
        ///     - We only (re)scan the GTS during Setup / Targeting.
        ///     - After launch we NEVER re-query the GTS for the active missile; we
        ///       drive the cached IMyGyro / IMyThrust / IMyWarhead instances directly.
        ///     - Every post-launch access is wrapped so a destroyed/closed block
        ///       (e.g. after detonation) ends guidance cleanly instead of throwing.
        ///
        /// ---------------------------------------------------------------------
        /// WORKFLOW / STATE MACHINE (guides ONE missile at a time):
        ///   Setup     -> cameras/missiles missing; idle, retries discovery.
        ///   Targeting -> Update10. Raycasts forward to lock a target; reports
        ///                No Target / Locking / Locked, range, target speed, and how
        ///                many missiles are ready. Keeps every un-launched missile
        ///                DISARMED and cold.
        ///   Guiding   -> Update1 (responsive). Entered by "launch" while Locked.
        ///                Fires the next ready missile, then steers its gyros toward
        ///                the predicted intercept point and arms past ArmDistance.
        ///                When that missile impacts/dies, returns to Targeting so you
        ///                can fire the next one.
        ///   Finished  -> all missiles spent. Idle.
        ///
        /// COMMANDS (run the PB with an argument):
        ///   setup           - re-scan; if no Missile# groups (and no MGID tags) exist,
        ///                     auto-discover missiles with the separation trick
        ///   launch          - fire the NEXT ready missile (only if a lock is held)
        ///   launch Missile2  - fire a SPECIFIC missile by name
        ///   abort           - cut thrust / disarm / stop steering the active missile
        ///   reset           - clear state and re-scan everything
        ///   cleartags       - wipe all MGID= auto-discovery tags from CustomData
        ///   screens         - list the screens on the LcdName block (find the index)
        ///
        /// ---------------------------------------------------------------------
        /// GUIDANCE MATH (lead pursuit ~ simplified proportional navigation):
        ///   We know the missile position (cached block) and derive its velocity by
        ///   finite-differencing that position each tick. We know the target
        ///   position + velocity from the camera raycast (MyDetectedEntityInfo).
        ///   Time-to-go  tgo = range / missileSpeed  (iterated a few times), then
        ///   aim at  predicted = targetPos + targetVel * tgo. Steering the nose at
        ///   the lead point drives the line-of-sight rate to zero — the same
        ///   collision-course condition PN enforces, without needing accel control.
        /// </summary>

        // ----------------------------- CONFIG ------------------------------
        // Firing-ship blocks
        const string CameraGroupName = "Missile_Cameras";
        const string MissilePrefix = "Missile";  // group names starting with this = one missile each
                                                 // (Missile1, Missile2, ...). The camera group is excluded.
        // Status readout. This can be a standalone LCD OR a cockpit/seat — any block
        // with built-in screens. If it is a cockpit, LcdSurfaceIndex picks WHICH
        // screen. Run the PB once with argument "screens" to list every screen on
        // this block (index + name) and find the one you want (e.g. top center).
        // If no block matches LcdName, the script falls back to the FIRST cockpit/seat
        // it finds on the grid and uses LcdSurfaceIndex on that.
        const string LcdName = "Cockpit";        // name of the LCD or cockpit block
        const int LcdSurfaceIndex = 0;           // which screen on a multi-screen block

        const string IgnoreNameTag = "Ignore";   // honoured on cameras and missile blocks (repo convention)

        // Auto-discovery: blocks belonging to a discovered missile get this key + an
        // integer written into their CustomData (a PB script CANNOT create real block
        // groups, so this tag is our group substitute). Re-runs are idempotent.
        const string MslTagKey = "MGID=";        // e.g. "MGID=2" => belongs to discovered missile #2
        const int DiscoveryWaitTicks = 2;        // Update10 ticks to wait for a grid split to settle

        // Targeting / raycast
        const double RaycastRange = 2000;             // m, acquisition + tracking range
        const int CamerasPerTick = 1;                 // raycasts attempted per tick
        const int LockHitsRequired = 3;               // consecutive hits to call it "Locked"
        const double LockTimeoutSeconds = 1.5;        // drop the lock after this long with no hit
        const int RefreshEveryTicks = 30;             // re-scan firing-ship blocks while targeting

        // Guidance / steering
        const bool ThrusterForwardIsNose = false;     // false: nose = -thruster.Forward (flame to tail)
                                                      // flip to true if the missile flies AWAY from target
        const double SteerGain = 8.0;                 // rad/s of gyro per rad of heading error
        const double MaxAngularSpeed = 6.0;           // rad/s clamp on gyro override
        const double EstimatedMissileSpeed = 100;     // m/s lead fallback before real speed is known
        const int LeadIterations = 3;                 // intercept-time refinement passes
        const double GravityCompAccel = 40;           // m/s^2, ~the missile's own thrust accel. In
                                                      // gravity the nose is tilted UP so thrust cancels
                                                      // gravity and still drives at the target. Set 0
                                                      // to disable. Raise if missiles still fall short,
                                                      // lower if they climb over the target.

        // Warhead safety / terminal
        const double ArmDistanceMeters = 50;          // arm only once this far from the firing ship
        const double ProximityDetonateMeters = 4;     // force-detonate inside this range to target
        const double TargetLostCoastSeconds = 4;      // dead-reckon a lost target this long, then give up

        // LCD
        const string LcdFont = "Monospace";
        const float LcdFontSize = 0.7f;
        // -------------------------------------------------------------------

        enum Phase { Setup, Targeting, Guiding, Finished, Discovering }
        enum LockState { NoTarget, Locking, Locked }

        // One missile = one block set (a Missile# group, or blocks tagged MGID=n by
        // auto-discovery). All component refs are cached while merged.
        class Missile
        {
            public string Name;
            public readonly List<IMyThrust> Thrusters = new List<IMyThrust>();
            public readonly List<IMyGyro> Gyros = new List<IMyGyro>();
            public readonly List<IMyWarhead> Warheads = new List<IMyWarhead>();
            public readonly List<IMyGasTank> Tanks = new List<IMyGasTank>();
            public readonly List<IMyShipConnector> Connectors = new List<IMyShipConnector>();
            public readonly List<IMyShipMergeBlock> Merges = new List<IMyShipMergeBlock>();
            public readonly List<IMyBatteryBlock> Batteries = new List<IMyBatteryBlock>();

            public bool Launched;
            public bool Armed;

            // Motion tracking (finite-difference velocity from a cached block).
            public Vector3D Pos;
            public Vector3D Vel;
            public Vector3D PrevPos;
            public bool MotionInit;

            public bool HasCore { get { return Gyros.Count > 0 && Thrusters.Count > 0 && Warheads.Count > 0; } }

            // Only HYDROGEN thrusters burn stored gas, so a missile that flies on ion
            // or atmospheric thrusters does not need an H2 tank to count as ready.
            public bool RequiresFuel
            {
                get
                {
                    for (int i = 0; i < Thrusters.Count; i++)
                        if (IsHydrogenThruster(Thrusters[i])) return true;
                    return false;
                }
            }
            public bool FuelReady { get { return !RequiresFuel || Tanks.Count > 0; } }

            // Ready to fire = has the steering/propulsion/warhead core AND, if it burns
            // hydrogen, has a tank to burn from.
            public bool Ready { get { return HasCore && FuelReady; } }
        }

        // Cached firing-ship blocks
        readonly List<IMyCameraBlock> _cameras = new List<IMyCameraBlock>();
        readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        IMyTextSurface _lcd;
        IMyShipController _shipController;   // any cockpit/remote on the firing ship, for gravity

        // Missile roster
        readonly List<Missile> _missiles = new List<Missile>();
        Missile _active;             // the one currently in flight (one at a time)

        // Target track (shared by all missiles)
        bool _hasTarget;
        long _targetId;
        Vector3D _targetPos;
        Vector3D _targetVel;
        DateTime _targetLastSeen = DateTime.MinValue;
        int _lockHits;
        LockState _lock = LockState.NoTarget;

        Phase _phase = Phase.Setup;
        int _tick;
        int _cameraIndex;
        int _scansThisTick;
        string _status = "starting...";
        readonly StringBuilder _sb = new StringBuilder(512);
        readonly List<IMyBlockGroup> _groupScratch = new List<IMyBlockGroup>();
        readonly List<IMyTerminalBlock> _blockScratch = new List<IMyTerminalBlock>();

        // Separation-trick auto-discovery (the "setup" fallback when no Missile# groups
        // exist). Probes one merge block per pass over several ticks.
        const int DiscSettle = 0, DiscDisable = 1, DiscRead = 2, DiscNext = 3;
        readonly List<IMyShipMergeBlock> _discMerges = new List<IMyShipMergeBlock>();
        readonly HashSet<long> _discTagged = new HashSet<long>();
        int _discStep;
        int _discIndex;
        int _discWait;
        int _discAssigned;
        long _mainGridId;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                // Only ever scan the GridTerminalSystem before launch. Once a missile
                // is away its blocks vanish from the GTS, so we never rescan while a
                // missile is in flight (Guiding).
                if (_phase == Phase.Setup || (_phase == Phase.Targeting && _tick % RefreshEveryTicks == 0))
                    SetupBlocks();

                string arg = (argument ?? "").Trim();
                string argLow = arg.ToLowerInvariant();
                if (argLow == "screens") { ListScreens(); return; }
                else if (argLow == "cleartags") { ClearAllMslTags(); _status = "MGID tags cleared"; }
                else if (argLow == "setup") DoSetupCommand();
                else if (argLow.StartsWith("launch")) TryLaunch(arg);
                else if (argLow == "abort") Abort();
                else if (argLow == "reset") ResetState();

                switch (_phase)
                {
                    case Phase.Setup: break;
                    case Phase.Targeting: RunTargeting(); break;
                    case Phase.Guiding: RunGuidance(); break;
                    case Phase.Discovering: RunDiscovery(); break;
                    case Phase.Finished: break;
                }
            }
            catch (Exception ex)
            {
                _status = "ERR: " + ex.Message;
            }

            WriteLcd();
            EchoStatus();
            _tick++;
        }

        // ---------------------------------------------------------------
        // SETUP / DISCOVERY  (firing ship + still-merged missiles)
        // ---------------------------------------------------------------
        void SetupBlocks()
        {
            CollectTargetingSources();

            // Status readout: the block named LcdName, else the first cockpit found.
            _lcd = ResolveSurface(LcdName);

            // A controller on the firing ship gives us the local gravity vector for
            // gravity-compensated guidance (the missile itself has no controller).
            _shipController = FindShipController();

            RebuildMissiles();

            int ready = ReadyCount();
            bool hasSource = _cameras.Count > 0 || _turrets.Count > 0;
            if (hasSource && ready > 0)
            {
                if (_phase == Phase.Setup)
                {
                    SafeAllMissiles();
                    _phase = Phase.Targeting;
                }
                _status = "Ready. Cams:" + _cameras.Count + " Turr:" + _turrets.Count + " Missiles:" + ready;
            }
            else
            {
                var missing = new StringBuilder();
                if (!hasSource) missing.Append("camera-or-turret ");
                if (ready == 0) missing.Append("missile(run 'setup' to auto-discover) ");
                _phase = Phase.Setup;
                _status = "Setup: missing " + missing.ToString().Trim();
            }
        }

        // Targeting sources: prefer the CameraGroupName group; if that group does not
        // exist, fall back to EVERY camera + AI turret on the construct. Honour Ignore.
        void CollectTargetingSources()
        {
            _cameras.Clear();
            _turrets.Clear();

            var camGroup = GridTerminalSystem.GetBlockGroupWithName(CameraGroupName);

            _blockScratch.Clear();
            if (camGroup != null) camGroup.GetBlocksOfType<IMyCameraBlock>(_blockScratch, b => !IsIgnored(b));
            else GridTerminalSystem.GetBlocksOfType<IMyCameraBlock>(_blockScratch, b => !IsIgnored(b));
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var cam = _blockScratch[i] as IMyCameraBlock;
                if (cam == null) continue;
                cam.EnableRaycast = true;
                _cameras.Add(cam);
            }

            _blockScratch.Clear();
            if (camGroup != null) camGroup.GetBlocksOfType<IMyLargeTurretBase>(_blockScratch, b => !IsIgnored(b));
            else GridTerminalSystem.GetBlocksOfType<IMyLargeTurretBase>(_blockScratch, b => !IsIgnored(b));
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var turret = _blockScratch[i] as IMyLargeTurretBase;
                if (turret != null) _turrets.Add(turret);
            }
        }

        // Missiles come from Missile# block groups if any exist; otherwise from blocks
        // tagged MGID=n by auto-discovery. Already-fired missiles have separated and no
        // longer appear (group emptied / tags gone with the grid), which is what we want.
        void RebuildMissiles()
        {
            _missiles.Clear();

            // 1) Named groups (Missile1, Missile2, ...), excluding the camera group.
            _groupScratch.Clear();
            GridTerminalSystem.GetBlockGroups(_groupScratch);
            for (int i = 0; i < _groupScratch.Count; i++)
            {
                var g = _groupScratch[i];
                if (g == null) continue;
                string gname = g.Name;
                if (gname == CameraGroupName) continue;
                if (!gname.StartsWith(MissilePrefix, StringComparison.OrdinalIgnoreCase)) continue;

                var m = new Missile { Name = gname };
                _blockScratch.Clear();
                g.GetBlocks(_blockScratch, b => !IsIgnored(b));
                for (int j = 0; j < _blockScratch.Count; j++) Classify(m, _blockScratch[j]);
                _missiles.Add(m);
            }

            // 2) No groups -> rebuild from MGID= CustomData tags written by discovery.
            if (_missiles.Count == 0)
            {
                var byId = new Dictionary<int, Missile>();
                _blockScratch.Clear();
                GridTerminalSystem.GetBlocks(_blockScratch);
                for (int i = 0; i < _blockScratch.Count; i++)
                {
                    var b = _blockScratch[i];
                    if (b == null || IsIgnored(b)) continue;
                    int id = GetMslId(b);
                    if (id < 0) continue;
                    Missile m;
                    if (!byId.TryGetValue(id, out m))
                    {
                        m = new Missile { Name = MissilePrefix + id };
                        byId[id] = m;
                    }
                    Classify(m, b);
                }
                foreach (var kv in byId) _missiles.Add(kv.Value);
            }

            // Fire in name order (Missile1, Missile2, ...).
            _missiles.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        // Sort one block into the right slot of a missile (used for both group blocks
        // and MGID-tagged blocks).
        void Classify(Missile m, IMyTerminalBlock b)
        {
            if (b == null) return;
            var thr = b as IMyThrust;         if (thr != null)  { m.Thrusters.Add(thr); return; }
            var gyro = b as IMyGyro;          if (gyro != null) { m.Gyros.Add(gyro); return; }
            var wh = b as IMyWarhead;         if (wh != null)   { m.Warheads.Add(wh); return; }
            var tank = b as IMyGasTank;       if (tank != null) { m.Tanks.Add(tank); return; }
            var conn = b as IMyShipConnector; if (conn != null) { m.Connectors.Add(conn); return; }
            var mrg = b as IMyShipMergeBlock; if (mrg != null)  { m.Merges.Add(mrg); return; }
            var bat = b as IMyBatteryBlock;   if (bat != null)  { m.Batteries.Add(bat); return; }
        }

        // Hydrogen thrusters are the only ones that burn stored gas. Vanilla ion
        // thrusters have no fuel keyword in their subtype; atmospheric ones say so.
        static bool IsHydrogenThruster(IMyThrust t)
        {
            if (t == null) return false;
            string sub;
            try { sub = t.BlockDefinition.SubtypeId.ToString(); } catch { return false; }
            return sub.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsIgnored(IMyTerminalBlock b)
        {
            return b != null && b.CustomName.ToLower().Contains(IgnoreNameTag.ToLower());
        }

        int ReadyCount()
        {
            int n = 0;
            for (int i = 0; i < _missiles.Count; i++)
                if (!_missiles[i].Launched && _missiles[i].Ready) n++;
            return n;
        }

        Missile NextReady()
        {
            for (int i = 0; i < _missiles.Count; i++)
                if (!_missiles[i].Launched && _missiles[i].Ready) return _missiles[i];
            return null;
        }

        Missile FindMissile(string name)
        {
            for (int i = 0; i < _missiles.Count; i++)
                if (string.Equals(_missiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _missiles[i];
            return null;
        }

        IMyTextSurface ResolveSurface(string name)
        {
            // Cockpits/seats AND standalone LCD panels all implement
            // IMyTextSurfaceProvider (a text panel is a 1-surface provider), so a
            // single path with a configurable index covers both.
            var surface = SurfaceFromProvider(GridTerminalSystem.GetBlockWithName(name) as IMyTextSurfaceProvider);
            if (surface != null) return surface;

            // Fallback: no block matched LcdName, so use the first cockpit/seat we find.
            _blockScratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(_blockScratch, b => !IsIgnored(b));
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                surface = SurfaceFromProvider(_blockScratch[i] as IMyTextSurfaceProvider);
                if (surface != null) return surface;
            }
            return null;
        }

        IMyTextSurface SurfaceFromProvider(IMyTextSurfaceProvider provider)
        {
            if (provider == null || provider.SurfaceCount <= 0) return null;
            int idx = LcdSurfaceIndex;
            if (idx < 0) idx = 0;
            if (idx >= provider.SurfaceCount) idx = provider.SurfaceCount - 1;
            return provider.GetSurface(idx);
        }

        // Any cockpit / remote / seat on the firing ship; preferred on Me's own grid.
        IMyShipController FindShipController()
        {
            _blockScratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyShipController>(_blockScratch);
            IMyShipController any = null;
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var c = _blockScratch[i] as IMyShipController;
                if (c == null) continue;
                if (any == null) any = c;
                if (Me != null && c.CubeGrid == Me.CubeGrid) return c;
            }
            return any;
        }

        // Local gravity (m/s^2). Zero in space or if no controller is available. The
        // firing ship's gravity is a fine stand-in for the missile's at engagement range.
        Vector3D GetGravity()
        {
            if (_shipController == null) return Vector3D.Zero;
            try { return _shipController.GetNaturalGravity(); } catch { return Vector3D.Zero; }
        }

        // Diagnostic: run the PB with argument "screens" to print every screen on
        // the LcdName block. Read the Echo and set LcdSurfaceIndex to the index of
        // the one you want (the top-center cockpit screen is usually named so).
        void ListScreens()
        {
            var block = GridTerminalSystem.GetBlockWithName(LcdName);
            if (block == null) { Echo("screens: no block named '" + LcdName + "'"); return; }
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null || provider.SurfaceCount == 0)
            {
                Echo("screens: '" + LcdName + "' has no text surfaces");
                return;
            }
            Echo("'" + LcdName + "' has " + provider.SurfaceCount + " screen(s):");
            for (int i = 0; i < provider.SurfaceCount; i++)
            {
                var s = provider.GetSurface(i);
                Echo("  [" + i + "] " + (s != null ? s.DisplayName : "?"));
            }
            Echo("Set LcdSurfaceIndex to the index you want.");
        }

        // Pre-launch safe state for every un-launched missile: warhead disarmed,
        // thrusters OFF and gyros FREE (so they never fight the ship's own steering),
        // tanks stockpiling, batteries recharging, and the connector LOCKED so the
        // missile stays parked on the rail.
        void SafeAllMissiles()
        {
            for (int i = 0; i < _missiles.Count; i++)
            {
                var m = _missiles[i];
                if (m.Launched) continue;
                SafeMissile(m);
            }
        }

        void SafeMissile(Missile m)
        {
            for (int i = 0; i < m.Warheads.Count; i++)
                try { m.Warheads[i].IsArmed = false; } catch { }
            // Thrusters OFF + zero override: a parked missile must not push the ship.
            for (int i = 0; i < m.Thrusters.Count; i++)
                try { m.Thrusters[i].ThrustOverridePercentage = 0f; m.Thrusters[i].Enabled = false; } catch { }
            // Gyros OFF while parked. A free (override-off) gyro still adds to the
            // SHIP's gyro pool and makes it spin too fast; an override-on gyro fights
            // ship rotation. Disabling the block entirely avoids both. DoLaunch
            // re-enables them. (Also stops the missile inheriting ship spin at launch.)
            for (int i = 0; i < m.Gyros.Count; i++)
                try { m.Gyros[i].GyroOverride = false; m.Gyros[i].Pitch = 0f; m.Gyros[i].Yaw = 0f; m.Gyros[i].Roll = 0f; m.Gyros[i].Enabled = false; } catch { }
            // Tanks stockpile so they fill and never bleed fuel while parked.
            for (int i = 0; i < m.Tanks.Count; i++)
                try { m.Tanks[i].Stockpile = true; } catch { }
            // Batteries recharge from the ship while docked.
            for (int i = 0; i < m.Batteries.Count; i++)
                try { m.Batteries[i].Enabled = true; m.Batteries[i].ChargeMode = ChargeMode.Recharge; } catch { }
            // Keep the small connector locked so the missile cannot drift off.
            for (int i = 0; i < m.Connectors.Count; i++)
                try { m.Connectors[i].Connect(); } catch { }
            m.Armed = false;
        }

        // ---------------------------------------------------------------
        // TARGETING PHASE  (Update10)
        // ---------------------------------------------------------------
        void RunTargeting()
        {
            // Keep every un-launched missile cold until the operator commits.
            SafeAllMissiles();

            _scansThisTick = 0;
            bool hitThisTick = false;

            for (int i = 0; i < CamerasPerTick && _cameras.Count > 0; i++)
            {
                var cam = _cameras[_cameraIndex];
                _cameraIndex = (_cameraIndex + 1) % _cameras.Count;
                if (cam == null || !cam.IsFunctional) continue;
                cam.EnableRaycast = true;

                MyDetectedEntityInfo info;
                bool scanned;
                if (_hasTarget)
                    scanned = RaycastToward(cam, PredictTargetNow(), RaycastRange, out info);
                else
                    scanned = RaycastAhead(cam, RaycastRange, out info);

                if (!scanned) continue;
                _scansThisTick++;

                if (!info.IsEmpty() && IsValidTarget(info))
                {
                    AcceptTarget(info);
                    hitThisTick = true;
                }
            }

            // Turrets back the cameras up. Polled AFTER the cameras so a fresh
            // operator-aimed camera hit keeps priority and turrets cannot hijack it.
            if (PollTurrets()) hitThisTick = true;

            double sinceSeen = (DateTime.UtcNow - _targetLastSeen).TotalSeconds;
            if (hitThisTick)
            {
                _lockHits++;
                _lock = _lockHits >= LockHitsRequired ? LockState.Locked : LockState.Locking;
            }
            else if (_hasTarget && sinceSeen <= LockTimeoutSeconds)
            {
                // brief miss within timeout — keep current lock state
            }
            else
            {
                _hasTarget = false;
                _lockHits = 0;
                _lock = LockState.NoTarget;
            }

            _status = "Targeting (" + _lock + ") ready:" + ReadyCount();
        }

        bool IsValidTarget(MyDetectedEntityInfo info)
        {
            if (info.IsEmpty()) return false;
            if (Me != null && info.EntityId == Me.CubeGrid.EntityId) return false;
            switch (info.Type)
            {
                case MyDetectedEntityType.SmallGrid:
                case MyDetectedEntityType.LargeGrid:
                case MyDetectedEntityType.CharacterHuman:
                case MyDetectedEntityType.CharacterOther:
                    return true;
                default:
                    return false; // ignore asteroids / planets / floating junk
            }
        }

        void AcceptTarget(MyDetectedEntityInfo info)
        {
            if (!_hasTarget || info.EntityId != _targetId) _lockHits = 0;
            _hasTarget = true;
            _targetId = info.EntityId;
            _targetPos = info.Position;          // bounding-box centre
            _targetVel = info.Velocity;
            _targetLastSeen = DateTime.UtcNow;
        }

        // Free per-tick detection from any AI turret in the camera group. A turret
        // acquires a target only when we have none; once a lock exists it just
        // refreshes the SAME entity (won't hijack the lock to a different contact).
        bool PollTurrets()
        {
            bool hit = false;
            for (int i = 0; i < _turrets.Count; i++)
            {
                var t = _turrets[i];
                if (t == null || !t.IsFunctional || !t.HasTarget) continue;
                var info = t.GetTargetedEntity();
                if (info.IsEmpty() || !IsValidTarget(info)) continue;
                if (_hasTarget && info.EntityId != _targetId) continue; // don't hijack an existing lock
                AcceptTarget(info);
                hit = true;
            }
            return hit;
        }

        // ---------------------------------------------------------------
        // LAUNCH
        // ---------------------------------------------------------------
        void TryLaunch(string arg)
        {
            if (_phase != Phase.Targeting)
            {
                _status = _phase == Phase.Guiding ? "launch ignored: missile already in flight"
                                                  : "launch ignored: not ready";
                return;
            }
            if (_lock != LockState.Locked)
            {
                _status = "launch ABORTED: no lock";
                return;
            }

            // Optional explicit target: "launch Missile2".
            Missile m;
            string[] parts = arg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                m = FindMissile(parts[1]);
                if (m == null) { _status = "launch: no missile '" + parts[1] + "'"; return; }
                if (m.Launched || !m.Ready) { _status = "launch: '" + parts[1] + "' not ready"; return; }
            }
            else
            {
                m = NextReady();
                if (m == null) { _status = "launch: no ready missile"; return; }
            }

            DoLaunch(m);
        }

        void DoLaunch(Missile m)
        {
            // 1) Fuel + power: stop stockpiling, switch batteries from Recharge to Auto
            //    so they actually deliver power once the missile is on its own.
            for (int i = 0; i < m.Tanks.Count; i++)
                try { m.Tanks[i].Stockpile = false; m.Tanks[i].Enabled = true; } catch { }
            for (int i = 0; i < m.Batteries.Count; i++)
                try { m.Batteries[i].Enabled = true; m.Batteries[i].ChargeMode = ChargeMode.Auto; } catch { }

            // 2) Thrusters: on + full override (push the missile off the rail).
            for (int i = 0; i < m.Thrusters.Count; i++)
                try { m.Thrusters[i].Enabled = true; m.Thrusters[i].ThrustOverridePercentage = 1f; } catch { }

            // 3) Release: open connectors and switch merge blocks off so the sub-grid
            //    becomes independent.
            for (int i = 0; i < m.Connectors.Count; i++)
                try { m.Connectors[i].Disconnect(); } catch { }
            for (int i = 0; i < m.Merges.Count; i++)
                try { m.Merges[i].Enabled = false; } catch { }

            // 4) Prime gyros for override control (they were turned OFF while parked,
            //    so re-enable them FIRST, then take override).
            for (int i = 0; i < m.Gyros.Count; i++)
                try { m.Gyros[i].Enabled = true; m.Gyros[i].GyroOverride = true; m.Gyros[i].GyroPower = 1f; } catch { }

            // 5) Seed motion tracking from the current cached position.
            m.MotionInit = false;
            m.Launched = true;
            _active = m;
            UpdateMissileMotion(m);

            // 6) Go responsive for guidance.
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            _phase = Phase.Guiding;
            _status = "LAUNCHED " + m.Name;
        }

        void Abort()
        {
            if (_phase == Phase.Discovering) RemergeAllProbed();
            if (_active != null) SafeMissile(_active);
            _active = null;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _phase = ReadyCount() > 0 ? Phase.Targeting : Phase.Setup;
            _status = "ABORTED";
        }

        void ResetState()
        {
            if (_phase == Phase.Discovering) RemergeAllProbed();
            _hasTarget = false;
            _lockHits = 0;
            _lock = LockState.NoTarget;
            _active = null;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _phase = Phase.Setup;
            _status = "reset";
        }

        // ---------------------------------------------------------------
        // GUIDANCE PHASE  (Update1) — drives the active missile only
        // ---------------------------------------------------------------
        void RunGuidance()
        {
            var m = _active;
            if (m == null) { BackToTargeting("no active missile"); return; }

            // The cached missile must still be alive (it dies on impact / destruction).
            if (!AnyAlive(m.Gyros) || !AnyAlive(m.Thrusters))
            {
                BackToTargeting(m.Name + " lost / detonated");
                return;
            }

            UpdateMissileMotion(m);
            RefreshTargetDuringFlight();

            double sinceSeen = (DateTime.UtcNow - _targetLastSeen).TotalSeconds;
            if (!_hasTarget || sinceSeen > TargetLostCoastSeconds)
            {
                StopSteering(m);
                MaybeArm(m);
                _status = "Guiding " + m.Name + ": NO TARGET (coasting)";
                return;
            }

            Vector3D targetNow = PredictTargetNow();
            Vector3D aimPoint = ComputeInterceptPoint(m.Pos, m.Vel, targetNow, _targetVel);
            Vector3D desiredForward = aimPoint - m.Pos;
            if (desiredForward.LengthSquared() < 1e-6) { StopSteering(m); return; }
            desiredForward = Vector3D.Normalize(desiredForward);

            // Gravity compensation: the missile only thrusts along its nose, so in a
            // gravity well it must aim partly UP or it curves into the ground. Tilt the
            // commanded nose so (thrust along nose) + gravity nets toward the aim point.
            if (GravityCompAccel > 0)
            {
                Vector3D g = GetGravity();
                if (g.LengthSquared() > 0.01)
                    desiredForward = Vector3D.Normalize(desiredForward * GravityCompAccel - g);
            }

            SteerTo(m, desiredForward);
            MaybeArm(m);

            double rangeToTarget = Vector3D.Distance(m.Pos, targetNow);
            if (m.Armed && rangeToTarget <= ProximityDetonateMeters)
            {
                for (int i = 0; i < m.Warheads.Count; i++)
                    try { m.Warheads[i].Detonate(); } catch { }
                BackToTargeting(m.Name + " proximity detonation");
                return;
            }

            _status = "Guiding " + m.Name + " -> tgt " + FormatDistance(rangeToTarget);
        }

        // Derive missile velocity by differencing a cached block's position.
        void UpdateMissileMotion(Missile m)
        {
            Vector3D pos;
            try { pos = m.Gyros.Count > 0 ? m.Gyros[0].GetPosition() : m.Thrusters[0].GetPosition(); }
            catch { return; }

            double dt = Runtime.TimeSinceLastRun.TotalSeconds;
            if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;

            if (!m.MotionInit)
            {
                m.PrevPos = pos;
                m.Pos = pos;
                m.Vel = Vector3D.Zero;
                m.MotionInit = true;
                return;
            }

            Vector3D rawVel = (pos - m.PrevPos) / dt;
            m.Vel = m.Vel * 0.5 + rawVel * 0.5;   // light low-pass over position quantisation
            m.PrevPos = pos;
            m.Pos = pos;
        }

        void RefreshTargetDuringFlight()
        {
            for (int i = 0; i < CamerasPerTick && _cameras.Count > 0; i++)
            {
                var cam = _cameras[_cameraIndex];
                _cameraIndex = (_cameraIndex + 1) % _cameras.Count;
                if (cam == null || !cam.IsFunctional) continue;
                cam.EnableRaycast = true;

                MyDetectedEntityInfo info;
                if (RaycastToward(cam, PredictTargetNow(), RaycastRange, out info))
                {
                    if (!info.IsEmpty() && IsValidTarget(info) && info.EntityId == _targetId)
                        AcceptTarget(info);
                }
            }

            // Turrets keep refreshing the locked target too (same-entity-only guard
            // lives in PollTurrets), which helps when the camera cone loses it.
            PollTurrets();
        }

        // Lead-pursuit intercept point. Iterates time-to-go so the predicted point
        // converges on a true collision course (simplified PN).
        Vector3D ComputeInterceptPoint(Vector3D mPos, Vector3D mVel, Vector3D tPos, Vector3D tVel)
        {
            double speed = mVel.Length();
            if (speed < 1.0) speed = EstimatedMissileSpeed;

            double tgo = (tPos - mPos).Length() / speed;
            Vector3D predicted = tPos;
            for (int i = 0; i < LeadIterations; i++)
            {
                predicted = tPos + tVel * tgo;
                tgo = (predicted - mPos).Length() / speed;
            }
            return predicted;
        }

        Vector3D PredictTargetNow()
        {
            double dt = (DateTime.UtcNow - _targetLastSeen).TotalSeconds;
            if (dt < 0) dt = 0;
            if (dt > TargetLostCoastSeconds) dt = TargetLostCoastSeconds;
            return _targetPos + _targetVel * dt;
        }

        // Rotate the missile so its nose points along desiredForward (world unit vec).
        void SteerTo(Missile m, Vector3D desiredForward)
        {
            MatrixD thrMat;
            try { thrMat = m.Thrusters[0].WorldMatrix; } catch { return; }

            // Missile nose = direction it accelerates. A thruster pushes OPPOSITE to
            // the face its flame exits, so a tail-mounted thruster has nose = -Forward.
            // ThrusterForwardIsNose flips this if your build differs.
            Vector3D nose = ThrusterForwardIsNose ? thrMat.Forward : -thrMat.Forward;

            // Missile reference frame (forward = nose, up borrowed from the thruster).
            MatrixD refMat = MatrixD.CreateWorld(m.Pos, Vector3D.Normalize(nose), thrMat.Up);

            // Heading error in that frame: forward is -Z, right is +X, up is +Y.
            Vector3D local = Vector3D.TransformNormal(desiredForward, MatrixD.Transpose(refMat));
            double yawErr = Math.Atan2(local.X, -local.Z);   // rotate about up   (turn left/right)
            double pitchErr = Math.Atan2(local.Y, -local.Z); // rotate about right (nose up/down)

            double yawCmd = Clamp(yawErr * SteerGain, -MaxAngularSpeed, MaxAngularSpeed);
            double pitchCmd = Clamp(pitchErr * SteerGain, -MaxAngularSpeed, MaxAngularSpeed);

            for (int i = 0; i < m.Gyros.Count; i++)
                ApplyGyroOverride(pitchCmd, yawCmd, 0.0, m.Gyros[i], refMat);
        }

        void StopSteering(Missile m)
        {
            for (int i = 0; i < m.Gyros.Count; i++)
            {
                try
                {
                    m.Gyros[i].GyroOverride = true;   // hold override but command zero spin
                    m.Gyros[i].Pitch = 0f; m.Gyros[i].Yaw = 0f; m.Gyros[i].Roll = 0f;
                }
                catch { }
            }
        }

        // Whip-style gyro override: take a desired angular velocity expressed in a
        // reference frame (pitch/yaw/roll about that frame's axes) and convert it
        // into the gyro's own local axes, accounting for how the gyro is mounted.
        void ApplyGyroOverride(double pitchSpeed, double yawSpeed, double rollSpeed, IMyGyro gyro, MatrixD refMatrix)
        {
            try
            {
                Vector3D rotationVec = new Vector3D(-pitchSpeed, yawSpeed, rollSpeed);
                Vector3D worldRot = Vector3D.TransformNormal(rotationVec, refMatrix);
                Vector3D gyroLocal = Vector3D.TransformNormal(worldRot, MatrixD.Transpose(gyro.WorldMatrix));
                gyro.Pitch = (float)gyroLocal.X;
                gyro.Yaw = (float)gyroLocal.Y;
                gyro.Roll = (float)gyroLocal.Z;
                gyro.GyroOverride = true;
            }
            catch { }
        }

        // Arm only once the missile has cleared the firing ship by ArmDistanceMeters.
        void MaybeArm(Missile m)
        {
            if (m.Armed || Me == null) return;
            double distFromShip = Vector3D.Distance(m.Pos, Me.GetPosition());
            if (distFromShip >= ArmDistanceMeters)
            {
                for (int i = 0; i < m.Warheads.Count; i++)
                    try { m.Warheads[i].IsArmed = true; } catch { }
                m.Armed = true;
            }
        }

        void BackToTargeting(string why)
        {
            _active = null;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _phase = ReadyCount() > 0 ? Phase.Targeting : Phase.Finished;
            _status = why + (ReadyCount() > 0 ? " | next missile ready" : " | all missiles spent");
        }

        // ---------------------------------------------------------------
        // RAYCAST HELPERS
        // ---------------------------------------------------------------
        bool RaycastAhead(IMyCameraBlock cam, double range, out MyDetectedEntityInfo info)
        {
            info = new MyDetectedEntityInfo();
            if (!cam.CanScan(range)) return false;
            info = cam.Raycast(range, 0f, 0f);
            return true;
        }

        bool RaycastToward(IMyCameraBlock cam, Vector3D worldPoint, double range, out MyDetectedEntityInfo info)
        {
            info = new MyDetectedEntityInfo();
            Vector3D dir = worldPoint - cam.GetPosition();
            double dist = dir.Length();
            if (dist < 1.0) return false;
            if (dist > range) dist = range;

            MatrixD m = cam.WorldMatrix;
            Vector3D local = Vector3D.TransformNormal(dir, MatrixD.Transpose(m));
            double yaw = MathHelper.ToDegrees(Math.Atan2(local.X, -local.Z));
            double pitch = MathHelper.ToDegrees(Math.Atan2(local.Y, -local.Z));

            float cone = cam.RaycastConeLimit;
            if (cone <= 0f) cone = 45f;
            if (Math.Abs(yaw) > cone || Math.Abs(pitch) > cone) return false;

            if (!cam.CanScan(dist)) return false;
            info = cam.Raycast(dist, (float)pitch, (float)yaw);
            return true;
        }

        // ---------------------------------------------------------------
        // BLOCK LIVENESS  (no IMyTerminalBlock.Closed in the ingame API)
        // ---------------------------------------------------------------
        bool AnyAlive<T>(List<T> blocks) where T : class, IMyTerminalBlock
        {
            for (int i = 0; i < blocks.Count; i++)
                if (BlockAlive(blocks[i])) return true;
            return false;
        }

        bool BlockAlive(IMyTerminalBlock b)
        {
            if (b == null) return false;
            try
            {
                if (b.CubeGrid == null) return false;
                return b.IsFunctional;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------
        // SETUP COMMAND + AUTO-DISCOVERY (the separation trick)
        // ---------------------------------------------------------------
        // "setup" rescans everything. If Missile# groups OR existing MGID tags already
        // define missiles, it just reports. If NOTHING does, it runs the separation
        // trick: lock all connectors, then open each merge block for a couple of ticks
        // and see which blocks split onto a new (still connector-held, so GTS-visible)
        // sub-grid — that block set is one missile. Tag it MGID=n and re-merge.
        // NOTE: a PB script cannot create real block groups, so the MGID CustomData tag
        // is the group substitute; later runs rebuild the roster from those tags.
        void DoSetupCommand()
        {
            if (_phase == Phase.Discovering) { _status = "discovery already running"; return; }
            if (_phase == Phase.Guiding) { _status = "setup ignored: missile in flight"; return; }

            SetupBlocks();
            if (_missiles.Count > 0)
            {
                _status = "setup: " + _missiles.Count + " missile(s) from "
                        + (HasMissileGroups() ? "groups" : "MGID tags")
                        + "; cams " + _cameras.Count + " turr " + _turrets.Count;
                return;
            }
            StartDiscovery();
        }

        bool HasMissileGroups()
        {
            _groupScratch.Clear();
            GridTerminalSystem.GetBlockGroups(_groupScratch);
            for (int i = 0; i < _groupScratch.Count; i++)
            {
                var g = _groupScratch[i];
                if (g == null || g.Name == CameraGroupName) continue;
                if (g.Name.StartsWith(MissilePrefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        void StartDiscovery()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _mainGridId = Me.CubeGrid.EntityId;
            _discAssigned = 0;
            _discIndex = 0;
            _discStep = DiscSettle;
            _discWait = 3;
            _discTagged.Clear();

            // Start clean so re-running discovery never double-tags.
            ClearAllMslTags();

            // Lock every connector so each missile stays attached (and GTS-visible)
            // when its merge block opens. Connectors with nothing to grab are no-ops.
            _blockScratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyShipConnector>(_blockScratch, b => !IsIgnored(b));
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var c = _blockScratch[i] as IMyShipConnector;
                if (c != null) try { c.Connect(); } catch { }
            }

            // Every non-Ignored merge block is a candidate missile mount. Tag structural
            // (ship-holding) merge blocks with "Ignore" so discovery never opens them.
            _discMerges.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyShipMergeBlock>(_discMerges, b => !IsIgnored(b));

            _phase = Phase.Discovering;
            _status = "Discovery: probing " + _discMerges.Count + " merge block(s)...";
        }

        void RunDiscovery()
        {
            switch (_discStep)
            {
                case DiscSettle:                       // let connector locks settle
                    if (--_discWait > 0) return;
                    _discStep = DiscDisable;
                    return;

                case DiscDisable:                      // open the next untested merge
                    while (_discIndex < _discMerges.Count &&
                           (_discMerges[_discIndex] == null || GetMslId(_discMerges[_discIndex]) >= 0))
                        _discIndex++;
                    if (_discIndex >= _discMerges.Count) { FinishDiscovery(); return; }
                    try { _discMerges[_discIndex].Enabled = false; } catch { }
                    _discWait = DiscoveryWaitTicks;
                    _discStep = DiscRead;
                    _status = "Discovery: merge " + (_discIndex + 1) + "/" + _discMerges.Count
                            + " (found " + _discAssigned + ")";
                    return;

                case DiscRead:                         // read the split-off grid, re-merge
                    if (--_discWait > 0) return;
                    ReadSplitMissile();
                    try { _discMerges[_discIndex].Enabled = true; } catch { }
                    _discWait = DiscoveryWaitTicks;
                    _discStep = DiscNext;
                    return;

                case DiscNext:                         // wait for re-merge, then advance
                    if (--_discWait > 0) return;
                    _discIndex++;
                    _discStep = DiscDisable;
                    return;
            }
        }

        // After a merge opened, blocks now on a grid other than the ship (kept visible
        // by the locked connector) are this missile. Need a full core to count as one.
        void ReadSplitMissile()
        {
            _blockScratch.Clear();
            GridTerminalSystem.GetBlocks(_blockScratch);

            var found = new List<IMyTerminalBlock>();
            bool hasWarhead = false, hasThruster = false, hasGyro = false;
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var b = _blockScratch[i];
                if (b == null || IsIgnored(b)) continue;
                IMyCubeGrid g;
                try { g = b.CubeGrid; } catch { continue; }
                if (g == null || g.EntityId == _mainGridId) continue;   // still on the ship
                if (_discTagged.Contains(b.EntityId)) continue;          // already another missile
                found.Add(b);
                if (b is IMyWarhead) hasWarhead = true;
                else if (b is IMyThrust) hasThruster = true;
                else if (b is IMyGyro) hasGyro = true;
            }

            if (!(hasWarhead && hasThruster && hasGyro)) return;          // not a missile -> skip

            _discAssigned++;
            for (int i = 0; i < found.Count; i++)
            {
                SetMslId(found[i], _discAssigned);
                _discTagged.Add(found[i].EntityId);
            }
        }

        void FinishDiscovery()
        {
            RemergeAllProbed();
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _phase = Phase.Setup;          // SetupBlocks promotes to Targeting if ready
            SetupBlocks();
            _status = _discAssigned > 0
                ? "Discovery done: tagged " + _discAssigned + " missile(s). " + _status
                : "Discovery: no missiles (connectors locked? warhead+thruster+gyro present?)";
        }

        // Safety: re-enable every merge block we may have opened during discovery.
        void RemergeAllProbed()
        {
            for (int i = 0; i < _discMerges.Count; i++)
                try { if (_discMerges[i] != null) _discMerges[i].Enabled = true; } catch { }
        }

        // ---- MGID CustomData tag (the block-group substitute) ----
        int GetMslId(IMyTerminalBlock b)
        {
            if (b == null) return -1;
            string cd;
            try { cd = b.CustomData ?? ""; } catch { return -1; }
            int idx = cd.IndexOf(MslTagKey, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return -1;
            int start = idx + MslTagKey.Length;
            int end = start;
            while (end < cd.Length && cd[end] >= '0' && cd[end] <= '9') end++;
            int val;
            if (end > start && int.TryParse(cd.Substring(start, end - start), out val)) return val;
            return -1;
        }

        void SetMslId(IMyTerminalBlock b, int id)
        {
            if (b == null) return;
            try
            {
                string cd = StripMslTag(b.CustomData ?? "");
                if (cd.Length > 0 && !cd.EndsWith("\n")) cd += "\n";
                b.CustomData = cd + MslTagKey + id;
            }
            catch { }
        }

        // Drop any line carrying the MGID key, keep the rest of the block's CustomData.
        string StripMslTag(string cd)
        {
            if (string.IsNullOrEmpty(cd) || cd.IndexOf(MslTagKey, StringComparison.OrdinalIgnoreCase) < 0)
                return cd ?? "";
            string[] lines = cd.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(MslTagKey, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(lines[i].TrimEnd('\r'));
            }
            return sb.ToString();
        }

        void ClearAllMslTags()
        {
            _blockScratch.Clear();
            GridTerminalSystem.GetBlocks(_blockScratch);
            for (int i = 0; i < _blockScratch.Count; i++)
            {
                var b = _blockScratch[i];
                if (b == null) continue;
                try
                {
                    string cd = b.CustomData ?? "";
                    if (cd.IndexOf(MslTagKey, StringComparison.OrdinalIgnoreCase) >= 0)
                        b.CustomData = StripMslTag(cd);
                }
                catch { }
            }
        }

        // ---------------------------------------------------------------
        // OUTPUT
        // ---------------------------------------------------------------
        void WriteLcd()
        {
            if (_lcd == null) return;
            try
            {
                _lcd.ContentType = ContentType.TEXT_AND_IMAGE;
                _lcd.Font = LcdFont;
                _lcd.FontSize = LcdFontSize;

                _sb.Clear();
                _sb.Append("=== MISSILE GUIDANCE ===\n");
                _sb.Append("Phase : ").Append(_phase).Append('\n');

                if (_phase == Phase.Targeting || _phase == Phase.Setup)
                {
                    _sb.Append("Lock  : ").Append(LockText()).Append('\n');
                    if (_hasTarget)
                    {
                        double rng = Vector3D.Distance(GetShipPos(), PredictTargetNow());
                        _sb.Append("Range : ").Append(FormatDistance(rng)).Append('\n');
                        _sb.Append("TgtVel: ").Append(_targetVel.Length().ToString("0")).Append(" m/s\n");
                    }
                    else
                    {
                        _sb.Append("Range : --\n");
                        _sb.Append("TgtVel: --\n");
                    }
                    _sb.Append("Cams  : ").Append(_cameras.Count).Append("  Turr: ").Append(_turrets.Count).Append('\n');
                    _sb.Append("Ready : ").Append(ReadyCount()).Append('/').Append(_missiles.Count).Append('\n');
                    if (_lock == LockState.Locked && ReadyCount() > 0)
                        _sb.Append("\n>> READY - run 'launch'\n");
                }
                else if (_phase == Phase.Guiding && _active != null)
                {
                    var m = _active;
                    double tgtRange = _hasTarget ? Vector3D.Distance(m.Pos, PredictTargetNow()) : -1;
                    double shipRange = Me != null ? Vector3D.Distance(m.Pos, Me.GetPosition()) : 0;
                    _sb.Append("Missile: ").Append(m.Name).Append('\n');
                    _sb.Append("Target: ").Append(_hasTarget ? "TRACK" : "LOST").Append('\n');
                    _sb.Append("ToTgt : ").Append(tgtRange < 0 ? "--" : FormatDistance(tgtRange)).Append('\n');
                    _sb.Append("MslVel: ").Append(m.Vel.Length().ToString("0")).Append(" m/s\n");
                    _sb.Append("FromShip: ").Append(FormatDistance(shipRange)).Append('\n');
                    _sb.Append("Warhead: ").Append(m.Armed ? "ARMED" : "safe").Append('\n');
                    _sb.Append("Left  : ").Append(ReadyCount()).Append('\n');
                }
                else
                {
                    _sb.Append(_status).Append('\n');
                }

                _lcd.WriteText(_sb);
            }
            catch { /* surface may be unavailable */ }
        }

        string LockText()
        {
            switch (_lock)
            {
                case LockState.Locked: return "LOCKED";
                case LockState.Locking: return "Locking... (" + _lockHits + "/" + LockHitsRequired + ")";
                default: return "No Target";
            }
        }

        Vector3D GetShipPos()
        {
            return Me != null ? Me.GetPosition() : Vector3D.Zero;
        }

        void EchoStatus()
        {
            Echo("MissileGuidance | " + _phase);
            Echo("Lock: " + LockText());
            Echo("Cams: " + _cameras.Count + " Turr: " + _turrets.Count + " | scans/tick: " + _scansThisTick);
            Echo("Missiles: " + ReadyCount() + " ready / " + _missiles.Count + " total");
            // Per-missile breakdown (helps confirm discovery + fuel-type detection).
            for (int i = 0; i < _missiles.Count; i++)
            {
                var m = _missiles[i];
                string fuel = !m.RequiresFuel ? "ion/atmo" : (m.Tanks.Count > 0 ? "H2" : "H2-NO-TANK");
                string state = m.Launched ? "away" : (m.Ready ? "READY" : "incomplete");
                Echo("  " + m.Name + ": " + state + " [" + fuel + " wh" + m.Warheads.Count
                     + " th" + m.Thrusters.Count + " gy" + m.Gyros.Count + " bat" + m.Batteries.Count + "]");
            }
            if (_phase == Phase.Guiding && _active != null)
                Echo("Active: " + _active.Name + " spd " + _active.Vel.Length().ToString("0")
                     + " m/s armed:" + _active.Armed);
            Echo("Run: " + Runtime.LastRunTimeMs.ToString("0.00") + " ms | Instr: "
                 + Runtime.CurrentInstructionCount + "/" + Runtime.MaxInstructionCount);
            Echo(_status);
        }

        // ---------------------------------------------------------------
        // SMALL UTILITIES
        // ---------------------------------------------------------------
        static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        static string FormatDistance(double m)
        {
            if (m >= 10000) return (m / 1000).ToString("0") + "km";
            if (m >= 1000) return (m / 1000).ToString("0.0") + "km";
            return m.ToString("0") + "m";
        }

        //Copy to here
    }
}
