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
        /// GridRadar — by Paolo
        ///
        /// Detects other grids/characters/floating objects and paints them on LCDs.
        /// The script does NOT use the radio antenna for detection (the PB API
        /// exposes only Radius/IsBroadcasting on antennas, not "scan nearby").
        /// Instead it aggregates three independent passive sources, all of which
        /// return MyDetectedEntityInfo:
        ///   - AI turrets       — IMyLargeTurretBase.GetTargetedEntity()
        ///   - Sensor blocks    — IMySensorBlock.DetectedEntities(...)
        ///   - Camera raycasts  — IMyCameraBlock.Raycast(range, pitch, yaw)
        ///
        /// All blocks live in one named group <see cref="GroupName"/>. Cameras in
        /// the group are auto-scanned with a pitch/yaw sweep — each tick advances
        /// the cursor by (YawDeltaDeg) and bumps pitch by (PitchDeltaDeg) when yaw
        /// hits the cone edge. Cameras are processed round-robin to spread the
        /// per-tick instruction budget.
        ///
        /// LCDs in the same group display tracks grouped by relationship
        /// (ENEMIES / NEUTRAL / FRIENDS). Each render advances one page (header is
        /// repeated on every page; category title sticks with its rows).
        ///
        /// Optional per-LCD filter — tag in CustomName (square brackets included):
        ///   [Enemies]   only enemies
        ///   [Neutral]   only neutrals (and unowned)
        ///   [Friends]   only friendly (owner / faction / friends)
        ///   no tag      full report (default)
        /// </summary>

        // ------- CONFIG -------
        const string GroupName = "GridRadar";
        const double RaycastRange = 5000;            // meters per camera scan
        const float YawDeltaDeg = 6f;                // sweep step in yaw
        const float PitchDeltaDeg = 12f;             // pitch bump when yaw hits edge
        const int CamerasPerTick = 1;                // round-robin scans / tick
        const int TrackTtlSeconds = 30;              // drop a track this long after last sight
        const int RenderEveryTicks = 6;              // ~1s at Update10
        const int RefreshEveryTicks = 60;            // ~10s at Update10
        const int BodyLinesPerPage = 16;
        const string LcdFont = "Monospace";
        const float LcdFontSize = 0.6f;
        const int NameColWidth = 18;
        // ----------------------

        enum Relation { Enemy, Neutral, Friend }

        class Track
        {
            public long EntityId;
            public string Name;
            public MyDetectedEntityType Type;
            public MyRelationsBetweenPlayerAndBlock Relationship;
            public Vector3D Position;
            public Vector3D Velocity;
            public BoundingBoxD BoundingBox;
            public DateTime LastSeenUtc;
            public string Source;
        }

        class CameraCursor
        {
            public IMyCameraBlock Camera;
            public float Pitch;
            public float Yaw;
            public int YawDir = 1;
            public int PitchDir = 1;
        }

        readonly List<IMyTerminalBlock> _groupBlocks = new List<IMyTerminalBlock>();
        readonly List<CameraCursor> _cameras = new List<CameraCursor>();
        readonly List<IMyLargeTurretBase> _turrets = new List<IMyLargeTurretBase>();
        readonly List<IMySensorBlock> _sensors = new List<IMySensorBlock>();
        readonly List<IMyTextSurface> _displays = new List<IMyTextSurface>();
        readonly List<string> _displayTags = new List<string>();
        readonly List<int> _displayPageIndex = new List<int>();
        readonly Dictionary<long, Track> _tracks = new Dictionary<long, Track>();
        readonly List<MyDetectedEntityInfo> _scratchInfos = new List<MyDetectedEntityInfo>();
        readonly List<long> _scratchPrune = new List<long>();
        readonly List<List<string>> _blocks = new List<List<string>>(4);
        readonly List<List<string>> _pages = new List<List<string>>(8);
        readonly StringBuilder _sb = new StringBuilder(2048);

        int _tickCounter;
        int _cameraIndex;
        int _scansThisTick;
        DateTime _lastRender = DateTime.MinValue;
        string _status = "starting...";

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                if (_cameras.Count == 0 && _turrets.Count == 0 && _sensors.Count == 0
                    || _tickCounter % RefreshEveryTicks == 0)
                {
                    DoRefresh();
                }

                DoDetect();
                PruneStaleTracks();

                if (_tickCounter % RenderEveryTicks == 0)
                    DoRender();
            }
            catch (Exception ex)
            {
                _status = "ERR: " + ex.Message;
            }

            int e = 0, n = 0, f = 0;
            foreach (var kvp in _tracks)
            {
                switch (Classify(kvp.Value.Relationship))
                {
                    case Relation.Enemy: e++; break;
                    case Relation.Neutral: n++; break;
                    case Relation.Friend: f++; break;
                }
            }

            Echo($"GridRadar | group '{GroupName}'");
            Echo($"Tick: {_tickCounter} | scans this tick: {_scansThisTick}");
            Echo($"Tracks: {_tracks.Count} (E:{e} N:{n} F:{f})");
            Echo($"Cams: {_cameras.Count} | Turrets: {_turrets.Count} | Sensors: {_sensors.Count} | Displays: {_displays.Count}");
            Echo($"Last run: {Runtime.LastRunTimeMs:0.00} ms | Instr: {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount}");
            Echo(_status);

            _tickCounter++;
        }

        void DoRefresh()
        {
            _groupBlocks.Clear();
            _turrets.Clear();
            _sensors.Clear();

            // Preserve camera sweep cursors when the camera survives a refresh.
            var oldCameras = new List<CameraCursor>(_cameras);
            _cameras.Clear();

            // Preserve LCD page index when the surface survives a refresh.
            var oldDisplays = new List<IMyTextSurface>(_displays);
            var oldPages = new List<int>(_displayPageIndex);
            _displays.Clear();
            _displayTags.Clear();
            _displayPageIndex.Clear();

            var group = GridTerminalSystem.GetBlockGroupWithName(GroupName);
            if (group == null)
            {
                _status = $"Group '{GroupName}' not found.";
                return;
            }

            group.GetBlocks(_groupBlocks);

            for (int i = 0; i < _groupBlocks.Count; i++)
            {
                var block = _groupBlocks[i];

                var cam = block as IMyCameraBlock;
                if (cam != null)
                {
                    cam.EnableRaycast = true;
                    CameraCursor existing = null;
                    for (int o = 0; o < oldCameras.Count; o++)
                    {
                        if (oldCameras[o].Camera == cam) { existing = oldCameras[o]; break; }
                    }
                    _cameras.Add(existing != null ? existing : new CameraCursor { Camera = cam });
                    continue;
                }

                var turret = block as IMyLargeTurretBase;
                if (turret != null) { _turrets.Add(turret); continue; }

                var sensor = block as IMySensorBlock;
                if (sensor != null) { _sensors.Add(sensor); continue; }

                var provider = block as IMyTextSurfaceProvider;
                if (provider != null && provider.SurfaceCount > 0)
                {
                    var surface = provider.GetSurface(0);
                    if (surface != null)
                    {
                        _displays.Add(surface);
                        _displayTags.Add(ExtractTag(block.CustomName));
                        int prevIdx = oldDisplays.IndexOf(surface);
                        _displayPageIndex.Add(prevIdx >= 0 ? oldPages[prevIdx] : 0);
                    }
                }
            }

            _status = $"OK: {_cameras.Count} cams, {_turrets.Count} turrets, {_sensors.Count} sensors, {_displays.Count} displays";
        }

        void DoDetect()
        {
            _scansThisTick = 0;

            // 1. Turrets — free, every tick, all turrets.
            for (int i = 0; i < _turrets.Count; i++)
            {
                var turret = _turrets[i];
                if (turret == null || !turret.IsFunctional) continue;
                if (!turret.HasTarget) continue;
                var info = turret.GetTargetedEntity();
                if (!info.IsEmpty()) Upsert(info, "turret");
            }

            // 2. Sensors — every tick, fills MyDetectedEntityInfo list per sensor.
            for (int i = 0; i < _sensors.Count; i++)
            {
                var sensor = _sensors[i];
                if (sensor == null || !sensor.IsFunctional || !sensor.Enabled) continue;
                _scratchInfos.Clear();
                sensor.DetectedEntities(_scratchInfos);
                for (int k = 0; k < _scratchInfos.Count; k++)
                    Upsert(_scratchInfos[k], "sensor");
            }

            // 3. Cameras — round-robin CamerasPerTick scans per tick. Each scan
            //    advances that camera's pitch/yaw cursor inside RaycastConeLimit.
            for (int i = 0; i < CamerasPerTick && _cameras.Count > 0; i++)
            {
                StepCameraSweep(_cameras[_cameraIndex]);
                _cameraIndex = (_cameraIndex + 1) % _cameras.Count;
            }
        }

        void StepCameraSweep(CameraCursor c)
        {
            var cam = c.Camera;
            if (cam == null || !cam.IsFunctional) return;
            if (!cam.EnableRaycast) cam.EnableRaycast = true;

            float cone = cam.RaycastConeLimit;
            if (cone <= 0f) cone = 45f; // safety fallback if API reports zero

            // Advance yaw, bouncing off the cone limit. When yaw hits the edge,
            // bump pitch by one step — together this rasters the full cone over
            // many ticks.
            c.Yaw += YawDeltaDeg * c.YawDir;
            if (c.Yaw > cone)
            {
                c.Yaw = cone;
                c.YawDir = -1;
                AdvancePitch(c, cone);
            }
            else if (c.Yaw < -cone)
            {
                c.Yaw = -cone;
                c.YawDir = 1;
                AdvancePitch(c, cone);
            }

            if (!cam.CanScan(RaycastRange)) return;

            var info = cam.Raycast(RaycastRange, c.Pitch, c.Yaw);
            _scansThisTick++;
            if (!info.IsEmpty()) Upsert(info, "camera");
        }

        void AdvancePitch(CameraCursor c, float cone)
        {
            c.Pitch += PitchDeltaDeg * c.PitchDir;
            if (c.Pitch > cone) { c.Pitch = cone; c.PitchDir = -1; }
            else if (c.Pitch < -cone) { c.Pitch = -cone; c.PitchDir = 1; }
        }

        void Upsert(MyDetectedEntityInfo info, string source)
        {
            if (info.IsEmpty()) return;
            // Filter our own grid out of the radar.
            if (Me != null && info.EntityId == Me.CubeGrid.EntityId) return;

            Track t;
            if (!_tracks.TryGetValue(info.EntityId, out t))
            {
                t = new Track { EntityId = info.EntityId };
                _tracks[info.EntityId] = t;
            }
            t.Name = info.Name;
            t.Type = info.Type;
            t.Relationship = info.Relationship;
            t.Position = info.Position;
            t.Velocity = info.Velocity;
            t.BoundingBox = info.BoundingBox;
            t.LastSeenUtc = DateTime.UtcNow;
            t.Source = source;
        }

        void PruneStaleTracks()
        {
            DateTime now = DateTime.UtcNow;
            _scratchPrune.Clear();
            foreach (var kvp in _tracks)
            {
                if ((now - kvp.Value.LastSeenUtc).TotalSeconds > TrackTtlSeconds)
                    _scratchPrune.Add(kvp.Key);
            }
            for (int i = 0; i < _scratchPrune.Count; i++)
                _tracks.Remove(_scratchPrune[i]);
        }

        void DoRender()
        {
            _lastRender = DateTime.UtcNow;

            for (int i = 0; i < _displays.Count; i++)
            {
                var surface = _displays[i];
                if (surface == null) continue;

                surface.ContentType = ContentType.TEXT_AND_IMAGE;
                surface.Font = LcdFont;
                surface.FontSize = LcdFontSize;

                int newPage;
                _sb.Clear();
                RenderTo(_sb, _displayTags[i], _displayPageIndex[i], out newPage);
                surface.WriteText(_sb);
                _displayPageIndex[i] = newPage;
            }
        }

        void RenderTo(StringBuilder sb, string tag, int pageIndex, out int newPageIndex)
        {
            bool showAll = string.IsNullOrEmpty(tag);

            _blocks.Clear();
            if (showAll || tag == "enemies")
                AppendRelationBlock(Relation.Enemy, "ENEMIES");
            if (showAll || tag == "neutral" || tag == "neutrals")
                AppendRelationBlock(Relation.Neutral, "NEUTRAL");
            if (showAll || tag == "friends" || tag == "friendly")
                AppendRelationBlock(Relation.Friend, "FRIENDS");

            PaginateBlocks();

            int pageCount = _pages.Count;
            int page = pageIndex % pageCount;
            if (page < 0) page += pageCount;

            int e = 0, n = 0, f = 0;
            foreach (var kvp in _tracks)
            {
                switch (Classify(kvp.Value.Relationship))
                {
                    case Relation.Enemy: e++; break;
                    case Relation.Neutral: n++; break;
                    case Relation.Friend: f++; break;
                }
            }

            sb.Append("=== GRID RADAR ===\n");
            sb.Append("Updated: ").Append(_lastRender.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
            sb.Append("Tracks: ").Append(_tracks.Count)
              .Append(" (E:").Append(e).Append(" N:").Append(n).Append(" F:").Append(f).Append(")\n");
            sb.Append("Cams: ").Append(_cameras.Count)
              .Append("  Range: ").Append(RaycastRange.ToString("0")).Append("m\n");
            sb.Append("Page:  ").Append(page + 1).Append('/').Append(pageCount).Append('\n');

            var lines = _pages[page];
            for (int i = 0; i < lines.Count; i++)
                sb.Append(lines[i]).Append('\n');

            newPageIndex = (page + 1) % pageCount;
        }

        void AppendRelationBlock(Relation rel, string title)
        {
            var rows = new List<Track>();
            foreach (var kvp in _tracks)
            {
                if (Classify(kvp.Value.Relationship) == rel) rows.Add(kvp.Value);
            }
            if (rows.Count == 0) return;

            Vector3D origin = Me.GetPosition();
            rows.Sort((a, b) =>
            {
                double da = Vector3D.DistanceSquared(origin, a.Position);
                double db = Vector3D.DistanceSquared(origin, b.Position);
                return da.CompareTo(db);
            });

            var block = new List<string>(rows.Count + 1);
            block.Add("--- " + title + " ---");

            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < rows.Count; i++)
            {
                var t = rows[i];
                string name = string.IsNullOrEmpty(t.Name) ? TypeShort(t.Type) : t.Name;
                if (name.Length > NameColWidth) name = name.Substring(0, NameColWidth);

                double dist = Vector3D.Distance(origin, t.Position);
                double vel = t.Velocity.Length();
                int age = (int)(now - t.LastSeenUtc).TotalSeconds;
                int ttl = TrackTtlSeconds - age;
                if (ttl < 0) ttl = 0;

                string distStr = FormatDistance(dist);
                string velStr = vel.ToString("0") + "m/s";
                string ttlStr = ttl.ToString() + "s";

                block.Add(name.PadRight(NameColWidth)
                    + " " + distStr.PadLeft(7)
                    + " " + velStr.PadLeft(6)
                    + " " + ttlStr.PadLeft(4));
            }
            _blocks.Add(block);
        }

        // Same pagination algorithm as CargoDisplay: blocks are kept atomic; a
        // block bigger than a page splits with "(cont.)" titles.
        void PaginateBlocks()
        {
            _pages.Clear();
            var current = new List<string>();

            for (int b = 0; b < _blocks.Count; b++)
            {
                var block = _blocks[b];
                int sep = current.Count > 0 ? 1 : 0;

                if (current.Count + sep + block.Count <= BodyLinesPerPage)
                {
                    if (sep > 0) current.Add("");
                    current.AddRange(block);
                    continue;
                }

                if (current.Count > 0)
                {
                    _pages.Add(current);
                    current = new List<string>();
                }

                if (block.Count <= BodyLinesPerPage)
                {
                    current.AddRange(block);
                }
                else
                {
                    string contTitle = block[0] + " (cont.)";
                    int idx = 0;
                    while (idx < block.Count)
                    {
                        int available = idx == 0 ? BodyLinesPerPage : BodyLinesPerPage - 1;
                        int take = Math.Min(available, block.Count - idx);
                        if (idx > 0) current.Add(contTitle);
                        current.AddRange(block.GetRange(idx, take));
                        idx += take;
                        if (idx < block.Count)
                        {
                            _pages.Add(current);
                            current = new List<string>();
                        }
                    }
                }
            }

            if (current.Count > 0) _pages.Add(current);
            if (_pages.Count == 0) _pages.Add(new List<string>());
        }

        static string FormatDistance(double m)
        {
            if (m >= 10000) return (m / 1000).ToString("0") + "km";
            if (m >= 1000) return (m / 1000).ToString("0.0") + "km";
            return m.ToString("0") + "m";
        }

        static Relation Classify(MyRelationsBetweenPlayerAndBlock r)
        {
            switch (r)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return Relation.Enemy;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                case MyRelationsBetweenPlayerAndBlock.Friends:
                    return Relation.Friend;
                default:
                    return Relation.Neutral;
            }
        }

        static string TypeShort(MyDetectedEntityType t)
        {
            switch (t)
            {
                case MyDetectedEntityType.SmallGrid: return "SmallGrid";
                case MyDetectedEntityType.LargeGrid: return "LargeGrid";
                case MyDetectedEntityType.CharacterHuman: return "Player";
                case MyDetectedEntityType.CharacterOther: return "Bot";
                case MyDetectedEntityType.FloatingObject: return "Float";
                case MyDetectedEntityType.Asteroid: return "Asteroid";
                case MyDetectedEntityType.Planet: return "Planet";
                case MyDetectedEntityType.Missile: return "Missile";
                default: return t.ToString();
            }
        }

        static string ExtractTag(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int open = name.IndexOf('[');
            if (open < 0) return null;
            int close = name.IndexOf(']', open + 1);
            if (close < 0) return null;
            return name.Substring(open + 1, close - open - 1).Trim().ToLowerInvariant();
        }

        //Copy to here
    }
}
