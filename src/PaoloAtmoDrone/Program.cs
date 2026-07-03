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
        Vector3D _holdPos; bool _hasHold;

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
        }

        public void Save() { }

        public void Main(string argument, UpdateType updateSource)
        {
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
            if (_hasPlayer) return _lastPlayerPos;
            if (_hasHold) return _holdPos;
            return _rc != null ? _rc.GetPosition() : Vector3D.Zero;
        }

        static Vector3D Reject(Vector3D v, Vector3D unitN) { return v - unitN * Vector3D.Dot(v, unitN); }
        static double VDot(Vector3D a, Vector3D b) { return Vector3D.Dot(a, b); }
        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

        void CeaseFire() { for (int i = 0; i < _guns.Count; i++) try { _guns[i].SetValueBool("Shoot", false); } catch { } }

        /// to here
    }
}
