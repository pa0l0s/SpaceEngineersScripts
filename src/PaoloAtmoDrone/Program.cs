using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
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
        /// PaoloAtmoDrone — bottom-thruster atmospheric combat & docking drone (by Paolo).
        ///
        /// HARDWARE (all on the drone's own single small grid; tag any block "Ignore" to skip):
        ///   * Remote Control (forward), Gyroscope(s), 2x Atmospheric Thrusters pointing DOWN,
        ///     2x forward Gatling Guns, forward Sensor, forward Camera, Antenna,
        ///     downward Small Connector, Reactor(s) + Batteries.
        ///
        /// BEHAVIOR:
        ///   Guard  - loiters within GuardRadius of its OWNER (forward Sensor detects the owner),
        ///            hovering at HoverAltitude above the surface.
        ///   Attack - a hostile that enters the guard bubble is engaged; the drone yaws to face it
        ///            and fires the gatlings ONLY when the forward camera confirms a hostile in the
        ///            aim cone. It stays tethered inside the bubble (never chases far from the owner).
        ///   RTB    - triggered when (batteries < RtbBatteryPct AND no Uranium) OR gatlings out of
        ///            ammo. Flies to the saved dock and connects.
        ///   Docked - thrust cut, gyros free, batteries recharge. Resume with "guard"/"undock".
        ///
        /// COMMANDS (run the PB with an argument):
        ///   setdock - fly the drone onto the base connector (Connected), then run this to save the
        ///             docking pose. Saves the ABSOLUTE pose plus, if a beacon named containing
        ///             BaseBeaconTag is visible, a beacon-relative offset.
        ///   guard / rtb / undock / stop / reset
        ///
        /// NOTE: the PB API cannot read a remote beacon's live position, so a base that moves out of
        /// view is homed via its last saved ABSOLUTE pose. A static home base docks exactly.
        ///
        /// IN-GAME TUNING: gyro sign conventions and PID gains are the two things to verify first.
        /// If the drone rolls/pitches the WRONG way, flip the sign on the offending axis in
        /// ApplyAttitude. If hover is sluggish or oscillates, adjust AltK*/TransK* and MaxTiltDegrees.
        /// </summary>

        // ------------------------------- CONFIG -------------------------------
        const string IgnoreNameTag   = "Ignore";     // repo convention: excludes a block
        const string BaseBeaconTag   = "[Base]";     // beacon/antenna name substring at base
        const string StatusLcdName    = "Drone LCD"; // optional status surface; falls back to RC/cockpit
        const int    StatusSurfaceIndex = 0;
        const string IgcChannel      = "PaoloAtmoDrone";

        const double HoverAltitude   = 30.0;   // m above surface while guarding
        const double CruiseAltitude  = 40.0;   // m above surface while transiting to base
        const double GuardRadius     = 50.0;   // m loiter bubble around the player
        const double EngageMargin    = 15.0;   // m past GuardRadius a hostile may be & still engage
        const double AttackExitMargin = 10.0;  // extra m before Attack releases (Guard<->Attack hysteresis)
        const double MinStandoff     = 25.0;   // m; back off if a hostile crowds closer than this
        const double WeaponRange     = 600.0;  // m max gatling engagement / raycast-confirm range
        const double AimConeDegrees  = 5.0;    // gun-fire aim tolerance (camera-confirmed)
        const double MaxTiltDegrees  = 25.0;   // cap on translation tilt

        const double PlayerLostSeconds  = 5.0; // hold last-known player pos this long, then station-keep
        const double HostileLostSeconds = 4.0; // drop a hostile after this long unseen
        const double RtbBatteryPct      = 0.15;// battery fraction that (with no uranium) triggers RTB

        const double ApproachHeight  = 15.0;   // m offset above the dock along the mating axis
        const double ApproachPosTol  = 2.0;    // m tolerance to switch approach->descend
        const double CruiseHorTol    = 8.0;    // m horizontal tolerance to switch cruise->approach

        // Controller gains (tune in-game).
        const double AltKp = 2.0, AltKi = 0.4, AltKd = 3.0;   // AGL altitude PID -> climb accel
        const double AltKpAbs = 2.0, AltKdAbs = 3.0;          // absolute-vertical mode (docking)
        const double TransKp = 0.6, TransKd = 1.8;            // horizontal PD -> accel
        const double AttGain = 6.0;                           // attitude error -> gyro rad/s
        const double MaxAngularSpeed = 6.0;                   // rad/s clamp per gyro axis
        const double MaxClimbAccel = 20.0, MaxDescendAccel = 12.0; // m/s^2 clamp on climb term
        const double DockDescendSpeedFactor = 0.4;            // scales AltKpAbs during terminal descent

        const int TelemetryEveryTicks = 10;
        // ---------------------------------------------------------------------

        enum State { Init, Guard, Attack, Rtb, Docked, Error }
        enum RtbPhase { Cruise, Approach, Descend, Docked }

        // Cached blocks
        IMyRemoteControl _rc;
        readonly List<IMyThrust> _downThrusters = new List<IMyThrust>();
        readonly List<IMyGyro> _gyros = new List<IMyGyro>();
        readonly List<IMyUserControllableGun> _guns = new List<IMyUserControllableGun>();
        readonly List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
        readonly List<IMyReactor> _reactors = new List<IMyReactor>();
        IMySensorBlock _sensor;
        IMyCameraBlock _camera;
        IMyRadioAntenna _antenna;
        IMyShipConnector _connector;
        IMyBeacon _baseBeacon;               // only non-null while the base is visible (connected)
        IMyTextSurface _surface;

        // State
        State _state = State.Init;
        RtbPhase _rtbPhase = RtbPhase.Cruise;
        DateTime _now;
        int _tick;
        string _status = "starting";
        bool _needScan = true;

        // Player / hostile tracking
        Vector3D _lastPlayerPos; bool _hasPlayer; DateTime _playerLastSeen = DateTime.MinValue;
        Vector3D _hostilePos, _hostileVel; long _hostileId; bool _hasHostile; DateTime _hostileLastSeen = DateTime.MinValue;
        bool _cameraConfirmed;

        // Hold point (station-keep / stop)
        Vector3D _holdPos; bool _hasHold; bool _manualHold;

        // Docking persistence
        bool _hasDock; MatrixD _dockAbs;          // connector world pose when docked (absolute)
        bool _hasDockRel; MatrixD _dockRel;       // connector pose relative to base beacon

        // PID state
        readonly Pid _altPid = new Pid(AltKp, AltKi, AltKd);
        double _lastClimbAccel;

        // Scratch
        readonly List<IMyTerminalBlock> _scratch = new List<IMyTerminalBlock>();
        readonly StringBuilder _sb = new StringBuilder(256);

        class Pid
        {
            public double Kp, Ki, Kd;
            double _integral, _prevErr; bool _init;
            public Pid(double kp, double ki, double kd) { Kp = kp; Ki = ki; Kd = kd; }
            public double Update(double err, double dt)
            {
                if (dt <= 0) dt = 1.0 / 60.0;
                _integral += err * dt;
                double deriv = _init ? (err - _prevErr) / dt : 0.0;
                _prevErr = err; _init = true;
                return Kp * err + Ki * _integral + Kd * deriv;
            }
            public void Reset() { _integral = 0; _prevErr = 0; _init = false; }
        }

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            LoadState();
        }

        public void Save()
        {
            _sb.Clear();
            _sb.Append(_hasDock ? "1" : "0").Append(';').Append(MatrixToStorage(_dockAbs)).Append(';');
            _sb.Append(_hasDockRel ? "1" : "0").Append(';').Append(MatrixToStorage(_dockRel));
            Storage = _sb.ToString();
        }

        void LoadState()
        {
            if (string.IsNullOrEmpty(Storage)) return;
            string[] p = Storage.Split(';');
            if (p.Length >= 4)
            {
                _hasDock = p[0] == "1"; _dockAbs = StorageToMatrix(p[1]);
                _hasDockRel = p[2] == "1"; _dockRel = StorageToMatrix(p[3]);
            }
        }

        static string MatrixToStorage(MatrixD m)
        {
            double[] v = new double[] {
                m.M11,m.M12,m.M13,m.M14, m.M21,m.M22,m.M23,m.M24,
                m.M31,m.M32,m.M33,m.M34, m.M41,m.M42,m.M43,m.M44 };
            string[] s = new string[16];
            for (int i = 0; i < 16; i++) s[i] = v[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join(",", s);
        }

        static MatrixD StorageToMatrix(string s)
        {
            string[] p = (s ?? "").Split(',');
            if (p.Length < 16) return MatrixD.Identity;
            double[] v = new double[16];
            for (int i = 0; i < 16; i++)
                double.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
            return new MatrixD(v[0],v[1],v[2],v[3], v[4],v[5],v[6],v[7], v[8],v[9],v[10],v[11], v[12],v[13],v[14],v[15]);
        }

        public void Main(string argument, UpdateType updateSource)
        {
            _now = DateTime.UtcNow;
            try
            {
                if (_needScan || _rc == null) ScanBlocks();

                HandleCommand((argument ?? "").Trim());

                if (!EssentialsOk())
                {
                    _state = State.Error;
                    _status = "MISSING: " + MissingEssentials();
                    SafeStop();
                }
                else
                {
                    switch (_state)
                    {
                        case State.Init:   _state = State.Guard; RunGuard(); break;
                        case State.Guard:  RunGuard();  break;
                        case State.Attack: RunAttack(); break;
                        case State.Rtb:    RunRtb();    break;
                        case State.Docked: RunDocked(); break;
                        case State.Error:  _state = State.Guard; RunGuard(); break; // recovered, resume control
                    }

                    if (_state == State.Guard || _state == State.Attack)
                        if (ShouldRtb()) EnterRtb();
                }

                // Update1 while flying, Update10 when idle/docked/error.
                Runtime.UpdateFrequency =
                    (_state == State.Guard || _state == State.Attack || _state == State.Rtb)
                    ? UpdateFrequency.Update1 : UpdateFrequency.Update10;
            }
            catch (Exception ex)
            {
                _status = "ERR: " + ex.Message;
                SafeStop();
            }

            if (_tick % TelemetryEveryTicks == 0) Telemetry();
            _tick++;
        }

        void HandleCommand(string arg)
        {
            if (arg.Length == 0) return;
            string a = arg.ToLowerInvariant();

            if (a == "setdock") { DoSetDock(); return; }
            if (a == "reset") { _needScan = true; _altPid.Reset(); _hasHold = false; _manualHold = false; _state = State.Guard; _status = "reset"; return; }
            if (a == "guard") { if (_state == State.Docked) DoUndock(); _state = State.Guard; _hasHold = false; _manualHold = false; _status = "guard"; return; }
            if (a == "rtb") { EnterRtb(); return; }
            if (a == "undock") { DoUndock(); _state = State.Guard; _hasHold = false; _manualHold = false; _status = "undock"; return; }
            if (a == "stop" || a == "hold")
            {
                if (_state == State.Docked) DoUndock();
                if (_rc != null) { _holdPos = _rc.GetPosition(); _hasHold = true; }
                _manualHold = true;
                _state = State.Guard;
                _status = "hold";
                return;
            }
        }

        void EnterRtb()
        {
            _state = State.Rtb;
            _rtbPhase = RtbPhase.Cruise;
            _altPid.Reset();
            _hasHold = false; _manualHold = false;
            CeaseFire();
            _status = "RTB";
        }

        void EnterDocked()
        {
            _state = State.Docked;
            _rtbPhase = RtbPhase.Docked;
            for (int i = 0; i < _downThrusters.Count; i++)
                try { _downThrusters[i].ThrustOverridePercentage = 0f; } catch { }
            ReleaseGyros();
            for (int i = 0; i < _batteries.Count; i++)
                try { _batteries[i].ChargeMode = ChargeMode.Recharge; } catch { }
            CeaseFire();
            _status = "Docked";
        }

        void DoUndock()
        {
            if (_connector != null) try { _connector.Disconnect(); } catch { }
            for (int i = 0; i < _batteries.Count; i++)
                try { _batteries[i].ChargeMode = ChargeMode.Auto; } catch { }
            _altPid.Reset();
        }

        void Telemetry()
        {
            _sb.Clear();
            _sb.Append("PaoloAtmoDrone\n");
            _sb.Append("State: ").Append(_state.ToString());
            if (_state == State.Rtb) _sb.Append(" (").Append(_rtbPhase.ToString()).Append(')');
            _sb.Append('\n');
            _sb.Append("Batt:  ").Append((int)(BatteryFraction() * 100)).Append("%\n");
            _sb.Append("Ammo:  ").Append(AmmoOut() ? "OUT" : "OK").Append('\n');
            _sb.Append("Fuel:  ").Append(HasUranium() ? "U-OK" : "NO-U").Append('\n');
            _sb.Append("Msg:   ").Append(_status);

            string text = _sb.ToString();
            if (_surface != null)
            {
                try { _surface.ContentType = ContentType.TEXT_AND_IMAGE; _surface.FontSize = 0.8f; _surface.WriteText(text); } catch { }
            }
            if (_antenna != null)
            {
                try { _antenna.EnableBroadcasting = true; _antenna.HudText = "Drone:" + _state.ToString(); } catch { }
            }
            try { IGC.SendBroadcastMessage(IgcChannel, text); } catch { }
            Echo(text);
        }

        void RunGuard()
        {
            ReadSensor();

            // Manual station-keep (from the "stop"/"hold" command): hold the latched point and
            // ignore player-follow until a guard/undock/reset command clears it. Still defend if
            // a hostile enters the bubble around the hold point.
            if (_manualHold && _hasHold)
            {
                if (HostileInBubble()) { _state = State.Attack; _status = "Attack!"; return; }
                ControlFlight(_holdPos, true, HoverAltitude, Vector3D.Zero, false);
                _status = "Holding position";
                return;
            }

            // Expire a stale player track (keep last-known pos, but stop trusting it as "live").
            if (_hasPlayer && (_now - _playerLastSeen).TotalSeconds > PlayerLostSeconds)
                _hasPlayer = false; // GuardCenter() then falls back to hold/current pos

            if (HostileInBubble()) { _state = State.Attack; _status = "Attack!"; return; }

            Vector3D center = GuardCenter();
            Vector3D pos = _rc.GetPosition();
            Vector3D up = UpVec();
            Vector3D horOff = Reject(pos - center, up);

            Vector3D target;
            if (horOff.Length() > GuardRadius && horOff.Length() > 1e-6)
                target = center + Vector3D.Normalize(horOff) * GuardRadius; // return to bubble edge
            else
                target = pos; // inside bubble -> station-keep (PD damping settles it)

            ControlFlight(target, true, HoverAltitude, center, _hasPlayer || _hasHold);
            _status = _hasPlayer ? "Guarding player" : "Guarding (no player seen)";
        }

        void RunAttack()
        {
            ReadSensor();
            RaycastConfirmHostile();

            // Hysteresis: release only when the hostile leaves the bubble by a margin (or goes
            // stale), so a contact hovering at the edge doesn't flip Guard<->Attack every tick.
            bool stillEngage = HostileFresh() &&
                Vector3D.Distance(_hostilePos, GuardCenter()) <= (GuardRadius + EngageMargin + AttackExitMargin);
            if (!stillEngage) { CeaseFire(); _state = State.Guard; _status = "target lost -> Guard"; return; }

            Vector3D center = GuardCenter();
            Vector3D pos = _rc.GetPosition();
            Vector3D up = UpVec();

            // Positioning: stay inside the bubble; only back off if the hostile crowds < MinStandoff.
            Vector3D toHostile = Reject(_hostilePos - pos, up);
            double dist = toHostile.Length();
            Vector3D target = pos;
            if (dist < MinStandoff && dist > 1e-3)
                target = pos - Vector3D.Normalize(toHostile) * (MinStandoff - dist);

            Vector3D off = Reject(target - center, up);
            if (off.Length() > GuardRadius && off.Length() > 1e-6)
                target = center + Vector3D.Normalize(off) * GuardRadius;

            ControlFlight(target, true, HoverAltitude, _hostilePos, true);

            if (_cameraConfirmed) OpenFire(); else CeaseFire();
            _status = "Attacking " + (_cameraConfirmed ? "(firing)" : "(aiming)");
        }

        // Confirm a hostile is within the forward camera's aim cone and range before firing.
        // Also refines the hostile track from the raycast hit.
        void RaycastConfirmHostile()
        {
            _cameraConfirmed = false;
            if (_camera == null || !HostileFresh()) return;
            try { _camera.EnableRaycast = true; } catch { }

            Vector3D dir = _hostilePos - _camera.GetPosition();
            double d = dir.Length();
            if (d < 1.0 || d > WeaponRange) return;

            MatrixD cm = _camera.WorldMatrix;
            Vector3D local = Vector3D.TransformNormal(dir, MatrixD.Transpose(cm));
            double yaw = MathHelper.ToDegrees(Math.Atan2(local.X, -local.Z));
            double pitch = MathHelper.ToDegrees(Math.Atan2(local.Y, -local.Z));
            if (Math.Abs(yaw) > AimConeDegrees || Math.Abs(pitch) > AimConeDegrees) return;

            if (!_camera.CanScan(d)) return;
            MyDetectedEntityInfo info = _camera.Raycast(d, 0f, 0f);
            if (!info.IsEmpty() && info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
            {
                _hostilePos = info.Position; _hostileVel = info.Velocity;
                _hostileId = info.EntityId; _hostileLastSeen = _now; _hasHostile = true;
                _cameraConfirmed = true;
            }
        }

        void OpenFire()  { for (int i = 0; i < _guns.Count; i++) try { _guns[i].SetValueBool("Shoot", true);  } catch { } }

        bool ShouldRtb()
        {
            bool powerOut = BatteryFraction() < RtbBatteryPct && !HasUranium();
            bool ammoOut = AmmoOut();
            return powerOut || ammoOut;
        }

        // The base beacon is only visible in GTS while the drone is on/connected to the base
        // construct. We capture it opportunistically; RTB uses the relative offset when the
        // beacon is visible, else the saved absolute pose.
        void FindBaseBeacon()
        {
            _baseBeacon = null;
            _scratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyBeacon>(_scratch,
                b => b.CustomName.IndexOf(BaseBeaconTag, StringComparison.OrdinalIgnoreCase) >= 0);
            if (_scratch.Count > 0) _baseBeacon = _scratch[0] as IMyBeacon;
        }

        void DoSetDock()
        {
            if (_connector == null) { _status = "setdock: no connector"; return; }
            MyShipConnectorStatus st; try { st = _connector.Status; } catch { st = MyShipConnectorStatus.Unconnected; }
            if (st != MyShipConnectorStatus.Connected) { _status = "setdock: connect to base first"; return; }

            _dockAbs = _connector.WorldMatrix;   // absolute pose while docked
            _hasDock = true;

            FindBaseBeacon();
            if (_baseBeacon != null)
            {
                _dockRel = MatrixD.Multiply(_connector.WorldMatrix, MatrixD.Invert(_baseBeacon.WorldMatrix));
                _hasDockRel = true;
            }
            Save();
            _status = "dock saved" + (_hasDockRel ? " (+beacon)" : " (absolute)");
        }

        // The world pose our connector should reach when docked. Prefer beacon-relative when the
        // beacon is currently visible (base may have moved); otherwise the saved absolute pose.
        MatrixD DockWorld()
        {
            FindBaseBeacon();
            if (_hasDockRel && _baseBeacon != null)
                return MatrixD.Multiply(_dockRel, _baseBeacon.WorldMatrix);
            return _dockAbs;
        }

        void RunRtb()
        {
            if (!_hasDock) { _status = "RTB: no dock saved (run setdock)"; HoverHold(); return; }
            if (_connector == null) { _status = "RTB: no connector"; HoverHold(); return; }

            MyShipConnectorStatus st; try { st = _connector.Status; } catch { st = MyShipConnectorStatus.Unconnected; }
            if (st == MyShipConnectorStatus.Connected) { EnterDocked(); return; }
            if (st == MyShipConnectorStatus.Connectable) { try { _connector.Connect(); } catch { } }

            MatrixD dp = DockWorld();
            Vector3D dockPos = dp.Translation;
            // Approach along the mating axis. A belly-down connector's Forward points into the
            // base connector (~downward), so back off along -Forward to sit above the pad.
            Vector3D approachConn = dockPos - dp.Forward * ApproachHeight;

            // Convert connector-space targets to Remote-Control targets via the live world offset.
            Vector3D rcPos = _rc.GetPosition();
            Vector3D offset = rcPos - _connector.GetPosition();
            Vector3D rcApproach = approachConn + offset;
            Vector3D rcDock = dockPos + offset;

            Vector3D up = UpVec();
            double horToApproach = Reject(rcApproach - rcPos, up).Length();

            switch (_rtbPhase)
            {
                case RtbPhase.Cruise:
                    // transit at a safe AGL altitude until roughly above the approach point
                    ControlFlight(rcApproach, true, CruiseAltitude, dockPos, true);
                    if (horToApproach < CruiseHorTol) _rtbPhase = RtbPhase.Approach;
                    _status = "RTB: cruise";
                    break;

                case RtbPhase.Approach:
                    // absolute 3D move to the approach point above the pad
                    ControlFlight(rcApproach, false, 0, dockPos, true);
                    if (Vector3D.Distance(rcPos, rcApproach) < ApproachPosTol) _rtbPhase = RtbPhase.Descend;
                    if (horToApproach > CruiseHorTol * 2) _rtbPhase = RtbPhase.Cruise; // drifted off, retry
                    _status = "RTB: approach";
                    break;

                case RtbPhase.Descend:
                    // slow descent onto the pad; softer vertical gain for a gentle touch. If we
                    // drift horizontally off the pad, climb back and re-approach so we never keep
                    // pushing down while mis-aligned. (Actual docking happens via the connector
                    // status check at the top of RunRtb: Connectable -> Connect -> Docked.)
                    if (horToApproach > CruiseHorTol) { _rtbPhase = RtbPhase.Approach; _status = "RTB: re-approach"; break; }
                    _altPid.Reset();
                    ControlFlightDescend(rcDock, dockPos);
                    _status = "RTB: descend";
                    break;
            }
        }

        // Terminal descent: absolute-vertical control with reduced climb authority so it settles
        // gently onto the connector instead of overshooting.
        void ControlFlightDescend(Vector3D rcDock, Vector3D facePoint)
        {
            // Temporarily soften absolute vertical response by nudging the target only slightly
            // below current, letting gravity + damping bring it down.
            Vector3D up = UpVec();
            Vector3D pos = _rc.GetPosition();
            Vector3D horTarget = Reject(rcDock - pos, up) + pos;               // full horizontal correction
            double downStep = 1.5;                                              // m step target below current
            Vector3D vTarget = pos - up * downStep;
            Vector3D target = Reject(horTarget - pos, up) + vTarget;           // combine horizontal + gentle down
            ControlFlight(target, false, 0, facePoint, true);
        }

        void RunDocked()
        {
            // Stay parked: keep thrust cut and gyros free. Recharge continues. Leave via
            // guard/undock command. (No auto-relaunch by design.)
            for (int i = 0; i < _downThrusters.Count; i++)
                try { _downThrusters[i].ThrustOverridePercentage = 0f; } catch { }
            _status = "Docked (recharging)";
        }

        bool _sensorConfigured;

        void ConfigureSensor()
        {
            if (_sensor == null) return;
            try
            {
                _sensor.Enabled = true;
                float r = (float)Math.Min(GuardRadius, 50.0); // clamp to the 50m sensor max extent
                _sensor.FrontExtend = r; _sensor.BackExtend = r; _sensor.LeftExtend = r;
                _sensor.RightExtend = r;  _sensor.TopExtend = r;  _sensor.BottomExtend = r;
                _sensor.DetectOwner = true;
                _sensor.DetectFriendly = true;
                _sensor.DetectPlayers = true;
                _sensor.DetectEnemy = true;
                _sensor.DetectNeutral = false;
                _sensor.DetectSmallShips = true;
                _sensor.DetectLargeShips = true;
                _sensor.DetectStations = false;
                _sensor.DetectSubgrids = false;
                _sensor.DetectAsteroids = false;
                _sensor.DetectFloatingObjects = false;
                _sensorConfigured = true;
            }
            catch { }
        }

        // One sensor reports one LastDetectedEntity. Classify it: the OWNER character updates
        // the player track; an ENEMY updates the hostile track. (Faction-mates are not treated
        // as the guarded player -- the drone guards its owner.)
        void ReadSensor()
        {
            if (!_sensorConfigured) ConfigureSensor();
            if (_sensor == null) return;
            MyDetectedEntityInfo info; try { info = _sensor.LastDetectedEntity; } catch { return; }
            if (info.IsEmpty()) return;

            if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies)
            {
                _hostilePos = info.Position; _hostileVel = info.Velocity;
                _hostileId = info.EntityId; _hasHostile = true; _hostileLastSeen = _now;
            }
            else if (info.Type == MyDetectedEntityType.CharacterHuman &&
                     info.Relationship == MyRelationsBetweenPlayerAndBlock.Owner)
            {
                _lastPlayerPos = info.Position; _hasPlayer = true; _playerLastSeen = _now;
            }
        }

        bool HostileFresh() { return _hasHostile && (_now - _hostileLastSeen).TotalSeconds <= HostileLostSeconds; }

        bool HostileInBubble()
        {
            if (!HostileFresh()) return false;
            Vector3D center = GuardCenter();
            return Vector3D.Distance(_hostilePos, center) <= (GuardRadius + EngageMargin);
        }

        double BatteryFraction()
        {
            double cur = 0, max = 0;
            for (int i = 0; i < _batteries.Count; i++)
                try { cur += _batteries[i].CurrentStoredPower; max += _batteries[i].MaxStoredPower; } catch { }
            return max > 1e-6 ? cur / max : 1.0;
        }

        bool HasUranium()
        {
            for (int i = 0; i < _reactors.Count; i++)
            {
                IMyInventory inv; try { inv = _reactors[i].GetInventory(0); } catch { continue; }
                if (inv == null) continue;
                var items = new List<MyInventoryItem>();
                try { inv.GetItems(items); } catch { continue; }
                for (int j = 0; j < items.Count; j++)
                    if (items[j].Type.SubtypeId.IndexOf("Uranium", StringComparison.OrdinalIgnoreCase) >= 0 && items[j].Amount.RawValue > 0)
                        return true;
            }
            return false;
        }

        bool AmmoOut()
        {
            if (_guns.Count == 0) return false;
            for (int i = 0; i < _guns.Count; i++)
            {
                IMyInventory inv; try { inv = _guns[i].GetInventory(0); } catch { continue; }
                if (inv == null) continue;
                var items = new List<MyInventoryItem>();
                try { inv.GetItems(items); } catch { continue; }
                for (int j = 0; j < items.Count; j++)
                    if (items[j].Amount.RawValue > 0) return false; // any magazine anywhere => not out
            }
            return true;
        }

        void ScanBlocks()
        {
            _rc = FirstOwn<IMyRemoteControl>();
            _sensor = FirstOwn<IMySensorBlock>();
            _camera = FirstOwn<IMyCameraBlock>();
            _connector = FirstOwn<IMyShipConnector>();
            _antenna = FirstOwn<IMyRadioAntenna>();

            FillOwn(_downThrusters);
            FillOwn(_gyros);
            FillOwn(_guns);
            FillOwn(_batteries);
            FillOwn(_reactors);

            if (_camera != null) { try { _camera.EnableRaycast = true; } catch { } }
            _surface = ResolveSurface(StatusLcdName);
            _needScan = false;
        }

        T FirstOwn<T>() where T : class
        {
            _scratch.Clear();
            GridTerminalSystem.GetBlocksOfType<T>(_scratch, b => OnOwnGrid(b) && !IsIgnored(b));
            return _scratch.Count > 0 ? _scratch[0] as T : null;
        }

        void FillOwn<T>(List<T> list) where T : class
        {
            list.Clear();
            _scratch.Clear();
            GridTerminalSystem.GetBlocksOfType<T>(_scratch, b => OnOwnGrid(b) && !IsIgnored(b));
            for (int i = 0; i < _scratch.Count; i++) { var t = _scratch[i] as T; if (t != null) list.Add(t); }
        }

        bool OnOwnGrid(IMyTerminalBlock b) { return b != null && Me != null && b.CubeGrid == Me.CubeGrid; }

        bool IsIgnored(IMyTerminalBlock b)
        {
            return b != null && b.CustomName.ToLower().Contains(IgnoreNameTag.ToLower());
        }

        // Status surface: named block first, else the Remote Control's screen, else first cockpit.
        IMyTextSurface ResolveSurface(string name)
        {
            var s = SurfaceFrom(GridTerminalSystem.GetBlockWithName(name) as IMyTextSurfaceProvider);
            if (s != null) return s;
            s = SurfaceFrom(_rc as IMyTextSurfaceProvider);
            if (s != null) return s;
            _scratch.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(_scratch, b => OnOwnGrid(b) && !IsIgnored(b));
            for (int i = 0; i < _scratch.Count; i++)
            {
                s = SurfaceFrom(_scratch[i] as IMyTextSurfaceProvider);
                if (s != null) return s;
            }
            return null;
        }

        IMyTextSurface SurfaceFrom(IMyTextSurfaceProvider p)
        {
            if (p == null || p.SurfaceCount <= 0) return null;
            int idx = StatusSurfaceIndex;
            if (idx < 0) idx = 0;
            if (idx >= p.SurfaceCount) idx = p.SurfaceCount - 1;
            return p.GetSurface(idx);
        }

        bool EssentialsOk()
        {
            return _rc != null && _gyros.Count > 0 && _downThrusters.Count > 0;
        }

        string MissingEssentials()
        {
            _sb.Clear();
            if (_rc == null) _sb.Append("RemoteControl ");
            if (_gyros.Count == 0) _sb.Append("Gyro ");
            if (_downThrusters.Count == 0) _sb.Append("Thruster ");
            if (_sensor == null) _sb.Append("(Sensor?) ");
            return _sb.ToString().Trim();
        }

        void SafeStop()
        {
            for (int i = 0; i < _downThrusters.Count; i++)
                try { _downThrusters[i].ThrustOverridePercentage = 0f; } catch { }
            for (int i = 0; i < _gyros.Count; i++)
                try { _gyros[i].GyroOverride = false; } catch { }
            CeaseFire();
        }

        Vector3D UpVec()
        {
            if (_rc == null) return Vector3D.Up;
            Vector3D g; try { g = _rc.GetNaturalGravity(); } catch { return _rc.WorldMatrix.Up; }
            return g.LengthSquared() > 1e-6 ? -Vector3D.Normalize(g) : _rc.WorldMatrix.Up;
        }

        Vector3D GuardCenter()
        {
            if (_manualHold && _hasHold) return _holdPos;
            if (_hasPlayer) return _lastPlayerPos;
            if (_hasHold) return _holdPos;
            return _rc != null ? _rc.GetPosition() : Vector3D.Zero;
        }

        static Vector3D Reject(Vector3D v, Vector3D unitN) { return v - unitN * Vector3D.Dot(v, unitN); }
        static double VDot(Vector3D a, Vector3D b) { return Vector3D.Dot(a, b); }
        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        void CeaseFire() { for (int i = 0; i < _guns.Count; i++) try { _guns[i].SetValueBool("Shoot", false); } catch { } }

        // Drive gyros so the drone's Up aligns to desiredUp (thrust direction) and its
        // Forward aligns to desiredForward (facing). Error is the sum of two cross products
        // (each an axis*sin(angle) that rotates a current axis onto its target), expressed
        // in the body frame, then handed to the proven ApplyGyroOverride transform.
        void ApplyAttitude(Vector3D desiredUp, Vector3D desiredForward)
        {
            if (_rc == null) return;
            MatrixD wm = _rc.WorldMatrix;
            Vector3D upErr = Vector3D.Normalize(wm.Up).Cross(Vector3D.Normalize(desiredUp));
            Vector3D fwdErr = Vector3D.Normalize(wm.Forward).Cross(Vector3D.Normalize(desiredForward));
            Vector3D errWorld = upErr + fwdErr;

            Vector3D errBody = Vector3D.TransformNormal(errWorld, MatrixD.Transpose(wm));
            // errBody.X along Right, .Y along Up, .Z along Forward. Map to ApplyGyroOverride's
            // (pitch about -Right, yaw about Up, roll about Forward) convention:
            double pitch = Clamp(-errBody.X * AttGain, -MaxAngularSpeed, MaxAngularSpeed);
            double yaw   = Clamp( errBody.Y * AttGain, -MaxAngularSpeed, MaxAngularSpeed);
            double roll  = Clamp( errBody.Z * AttGain, -MaxAngularSpeed, MaxAngularSpeed);

            for (int i = 0; i < _gyros.Count; i++)
            {
                try { _gyros[i].Enabled = true; _gyros[i].GyroPower = 1f; } catch { }
                ApplyGyroOverride(pitch, yaw, roll, _gyros[i], wm);
            }
        }

        // Whip-style override: angular velocity given as (pitch,yaw,roll) about the reference
        // frame's axes, converted into each gyro's own local axes. Ported from MissileGuidance.
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

        void ReleaseGyros()
        {
            for (int i = 0; i < _gyros.Count; i++)
                try { _gyros[i].GyroOverride = false; _gyros[i].Pitch = 0f; _gyros[i].Yaw = 0f; _gyros[i].Roll = 0f; } catch { }
        }

        // The one flight primitive. Horizontal position is always driven toward worldTarget.
        // Vertical: aglMode=true holds aglAlt metres above the surface (PID on elevation);
        // aglMode=false drives the drone to worldTarget's absolute vertical position (docking).
        void ControlFlight(Vector3D worldTarget, bool aglMode, double aglAlt, Vector3D facePoint, bool hasFace)
        {
            if (_rc == null) return;
            double dt = Runtime.TimeSinceLastRun.TotalSeconds;
            if (dt <= 0 || dt > 0.5) dt = 1.0 / 60.0;

            MatrixD wm = _rc.WorldMatrix;
            Vector3D pos = _rc.GetPosition();
            Vector3D g; try { g = _rc.GetNaturalGravity(); } catch { g = Vector3D.Zero; }
            double gMag = g.Length();
            Vector3D up = gMag > 1e-3 ? -Vector3D.Normalize(g) : wm.Up;

            MyShipVelocities sv; try { sv = _rc.GetShipVelocities(); } catch { return; }
            Vector3D vel = sv.LinearVelocity;
            double mass; try { mass = _rc.CalculateShipMass().PhysicalMass; } catch { return; }

            // --- vertical term -> climb acceleration ---
            double climbAccel;
            if (aglMode)
            {
                double elev;
                if (_rc.TryGetPlanetElevation(MyPlanetElevation.Surface, out elev))
                    climbAccel = _altPid.Update(aglAlt - elev, dt);
                else
                    climbAccel = -VDot(vel, up) * 2.0; // no surface: just damp vertical speed
            }
            else
            {
                double upErr = VDot(worldTarget - pos, up);
                double upVel = VDot(vel, up);
                climbAccel = upErr * AltKpAbs - upVel * AltKdAbs;
            }
            climbAccel = Clamp(climbAccel, -MaxDescendAccel, MaxClimbAccel);

            // --- horizontal term -> desired horizontal acceleration (tilt) ---
            Vector3D horErr = Reject(worldTarget - pos, up);
            Vector3D horVel = Reject(vel, up);
            Vector3D aHor = horErr * TransKp - horVel * TransKd;
            double aHorMax = (gMag > 1e-3 ? gMag : 9.81) * Math.Tan(MathHelper.ToRadians((float)MaxTiltDegrees));
            if (aHor.Length() > aHorMax && aHor.Length() > 1e-6) aHor = Vector3D.Normalize(aHor) * aHorMax;

            // --- combine -> desired thrust direction & magnitude ---
            Vector3D desiredAccel = up * (gMag + climbAccel) + aHor;
            double accelMag = desiredAccel.Length();
            Vector3D desiredUp = accelMag > 1e-6 ? Vector3D.Normalize(desiredAccel) : up;

            // --- facing (yaw) ---
            Vector3D desiredForward;
            if (hasFace)
            {
                Vector3D f = Reject(facePoint - pos, desiredUp);
                desiredForward = f.LengthSquared() > 1e-6 ? Vector3D.Normalize(f) : Reject(wm.Forward, desiredUp);
            }
            else desiredForward = Reject(wm.Forward, desiredUp);
            if (desiredForward.LengthSquared() < 1e-6) desiredForward = wm.Forward;
            desiredForward = Vector3D.Normalize(desiredForward);

            ApplyAttitude(desiredUp, desiredForward);
            ApplyThrust(mass, accelMag);
            _lastClimbAccel = climbAccel;
        }

        // Downward thrusters push the drone UP. Override = required up-thrust / max available.
        void ApplyThrust(double mass, double accelAlongUp)
        {
            double totalMax = 0;
            for (int i = 0; i < _downThrusters.Count; i++)
                try { totalMax += _downThrusters[i].MaxEffectiveThrust; } catch { }
            if (totalMax < 1.0) return;
            float ovr = (float)Clamp(mass * accelAlongUp / totalMax, 0.0, 1.0);
            for (int i = 0; i < _downThrusters.Count; i++)
                try { _downThrusters[i].Enabled = true; _downThrusters[i].ThrustOverridePercentage = ovr; } catch { }
        }

        // Station-keep: latch the current position the first time, then hold it at HoverAltitude.
        void HoverHold()
        {
            if (!_hasHold) { if (_rc != null) { _holdPos = _rc.GetPosition(); _hasHold = true; } }
            ControlFlight(_holdPos, true, HoverAltitude, Vector3D.Zero, false);
        }

        /// to here
    }
}
