# PaoloAtmoDrone — Atmospheric Combat & Docking Drone (Design)

Date: 2026-07-03
Status: Approved (pending spec review)

## Summary

A single-file Space Engineers Programmable Block script that flies a minimal
atmospheric **quadcopter-style** drone: 2x downward-only atmospheric thrusters,
tilted by a gyroscope to translate horizontally. It guards the owning player,
engages hostiles that enter the guard bubble with 2x forward gatling guns, and
returns to base to dock on a saved connector when out of power/fuel or ammo.

Lives in a new standalone project `src/PaoloAtmoDrone` (copied structure from
`EmptyTemplate`), single-file `Program` in `namespace ScriptingClass`.

## Hardware assumptions (drone grid)

- 2x Atmospheric Thrusters pointing strictly **DOWN** (only propulsion).
- 1x Small Reactor (conveyor-fed Uranium Ingot) + 2x Small Batteries.
- 2x Fixed Gatling Guns pointing **FORWARD** (conveyor-fed ammo).
- 1x Remote Control (forward), 1x Sensor (forward), 1x Camera (forward, aligned
  with guns), 1x Antenna, 1x Gyroscope, 1x Small Connector pointing **DOWN**.

## Constraints

- **PB compiler is C# 6**: no `?.`, no tuples, no digit separators, no string
  interpolation with `$`. (Repo convention: `feedback_csharp6`.)
- Single file; all logic inside `public class Program : MyGridProgram`.
- Blocks fetched **by type** on `Me.CubeGrid` (single small grid — no groups
  required), honoring the repo `Ignore` CustomName tag.
- `UpdateFrequency.Update10` for Guard/RTB cruise; `Update1` while actively
  steering at a target or in terminal docking, to stay under instruction limits.
- All post-fetch block access wrapped so a destroyed/closed block reports an
  error via `Echo` rather than throwing.

## Flight controller (Approach A: tilt-to-translate PID)

The drone can only thrust down, so horizontal motion requires tilting.

Three nested control loops, computed each control tick from the Remote Control's
world matrix, velocity, gravity, and mass:

1. **Altitude loop** → thruster override.
   - Read surface elevation via `IMyRemoteControl.TryGetPlanetElevation(
     MyPlanetElevation.Surface, out elev)`.
   - PID on `(targetAltitude - elev)` produces a desired vertical acceleration.
   - Thruster override = `mass * (gravityMag + climbAccel) / maxEffectiveThrust`,
     divided by the tilt cosine so tilt doesn't sag altitude. Clamped [0,1].

2. **Translation loop** → desired tilt (pitch/roll).
   - Horizontal position error (target loiter/standoff point minus current pos,
     projected onto the local ground plane) through a PD controller yields a
     desired horizontal acceleration vector, damped by horizontal velocity.
   - Convert desired horizontal accel into a desired tilt angle about the
     gravity-aligned frame, **capped at `MaxTiltDegrees`** (e.g. 25°) so the
     drone never flips or loses lift.
   - Produces a desired "up" (thrust) direction = gravity-up tilted toward the
     target.

3. **Attitude loop** → gyro override.
   - Build a desired orientation: drone belly-down aligned to the desired thrust
     direction, **yaw** set to face the loiter point (Guard) or the enemy
     (Attack) so the forward guns/camera point at the target.
   - Reuse the proven Whip-style `ApplyGyroOverride(pitch, yaw, roll, gyro,
     refMatrix)` and finite-difference approach from `MissileGuidance`.

Velocity and mass come from `IMyShipController.GetShipVelocities()`,
`CalculateShipMass()`, `GetNaturalGravity()`.

## State machine

`Init → Guard ⇄ Attack → RTB → Docked`, plus `Error`. Guard is default.

### Guard (default)
- Forward **Sensor** detects the owning player (`DetectOwner = true`,
  `DetectPlayers = true`, friendly on). Store last-known player world position
  and a timestamp.
- Loiter: hold position within `GuardRadius` (50 m) of last-known player pos, at
  `HoverAltitude` above surface. If inside the radius, the translation target is
  the current position (station-keep); if outside, target a point on the bubble
  edge nearest the drone. Yaw faces the player point.
- If the Sensor hasn't seen the player for `PlayerLostSeconds`, hold the
  last-known point (do not wander).

### Attack (preempts Guard, tethered to the bubble)
- Entered when a **hostile** is detected (Sensor hostile-in-box and/or forward
  camera raycast) **AND** the hostile is within `GuardRadius + EngageMargin` of
  the last-known player position.
- Behavior: yaw to face the hostile, hold `StandoffRange` from it (translation
  target = point `StandoffRange` from hostile along the drone→hostile line, but
  **clamped to stay inside the guard bubble** so the drone never strays far from
  the player), maintain altitude.
- **Fire** the vanilla gatlings (`IMySmallGatlingGun`/`IMyUserControllableGun`
  `Shoot` action or `ShootOnce`) only when a **forward camera raycast confirms a
  hostile entity within a small aim cone and within `WeaponRange`**. Cease fire
  otherwise.
- Return to Guard when no valid hostile remains in the bubble.

### RTB (Return to Base) + Docking
- **Triggers** (checked every tick, latched once RTB starts):
  1. Batteries critically low (`avg StoredPower/MaxStoredPower < RtbBatteryPct`)
     **AND** reactor inventory has no Uranium Ingot, OR
  2. All gatling guns out of ammo (no ammo magazine items in any gun inventory).
- **Navigation**: find the base beacon (`IMyRadioAntenna`/`IMyBeacon` whose name
  contains `BaseBeaconTag`, default `"[Base]"`). Reconstruct the world docking
  matrix from the saved relative offset (see `setdock`). Fly to an **approach
  point** `ApproachHeight` above the connector (aligned over it), then descend
  slowly along the connector's up-axis.
- **Dock**: when the ship connector is `Connectable`, `Connect()`. On successful
  connect → **Docked**.

### Docked
- Thrusters override 0 + disabled, gyros override off, batteries → Recharge.
- Stay docked until the player runs `undock`/`guard`, or (optional) auto-undock
  when recharged and re-armed. Default: stay docked until commanded.

### Error
- Any essential block (Remote, Gyro, both Thrusters, Sensor) missing/destroyed →
  cut thrust safe, Echo the missing block, broadcast warning.

## Commands (PB argument)

- `setdock` — save the docking offset. Requires the ship connector currently
  `IsConnected` to the base connector and the base beacon present. Stores
  `beacon.WorldMatrix^-1 * shipConnector.WorldMatrix` (a `MatrixD`) serialized
  into `Storage` (and mirrored to CustomData for visibility). Lets the drone
  recover the exact port even if the base moves.
- `guard` — force Guard mode / resume from Docked (undock first).
- `rtb` — force Return-to-Base.
- `undock` — release connector and hover in place, enter Guard.
- `stop` / `hold` — station-keep hover at current position.
- `reset` — re-scan blocks and reset state.
- (no argument) — run the current state's per-tick logic.

## Telemetry

Every `TelemetryEveryTicks`, write to the status surface (Remote Control screen
or first cockpit/LCD, resolved like `MissileGuidance.ResolveSurface`) and set the
Antenna `HudText`/`CustomName` + broadcast a short status string via `IGC` on a
configurable channel. Fields:

- State (Guarding / Attacking / RTB / Docked / ERROR)
- Battery %
- Ammo status (rounds/magazines remaining, or "OUT")
- Uranium present (yes/no)
- Warnings (RTB reason, no player seen, target locked, low altitude)

## Configuration constants (initial defaults)

```
BaseBeaconTag        = "[Base]"
IgnoreNameTag        = "Ignore"
HoverAltitude        = 30    // m above surface (Guard)
GuardRadius          = 50    // m loiter bubble around player
EngageMargin         = 15    // m past GuardRadius a hostile may be to still engage
StandoffRange        = 120   // m preferred distance to hostile when attacking
WeaponRange          = 600   // m max gatling engagement / raycast confirm range
AimConeDegrees       = 5     // gun-fire aim tolerance (camera-confirmed)
MaxTiltDegrees       = 25    // cap on translation tilt
RtbBatteryPct        = 0.15  // battery fraction that (with no uranium) triggers RTB
PlayerLostSeconds    = 5     // hold last-known player pos this long, then station-keep
ApproachHeight       = 15    // m above connector for the docking approach point
DockDescendSpeed     = 1.5   // m/s terminal descent
TelemetryEveryTicks  = 30
IgcChannel           = "PaoloAtmoDrone"
// PID gains: altitude (Kp,Ki,Kd), translation (Kp,Kd) — tuned in-game.
```

## Files

- `src/PaoloAtmoDrone/PaoloAtmoDrone.csproj` — copy of `EmptyTemplate.csproj`
  with new GUID / RootNamespace / AssemblyName.
- `src/PaoloAtmoDrone/Properties/AssemblyInfo.cs` — copy, renamed.
- `src/PaoloAtmoDrone/Program.cs` — the script.
- Add project to `SpaceEngineersScripts.sln`.

## Testing / verification

- Build the project (repo `project_build` instructions) — must compile clean
  under the SE reference DLLs; watch for accidental C# 7+ syntax.
- Static review of instruction-count hot paths (avoid per-tick GTS rescans while
  flying; cache blocks after `reset`).
- In-game behavior is validated by the user (guard hover stability, tilt cap,
  engage-within-bubble, RTB dock alignment). The script must Echo clear setup
  errors so misconfiguration is obvious before flight.

## Reuse from existing repo code

- Whip-style `ApplyGyroOverride` + reference-frame math — from `MissileGuidance`.
- `ResolveSurface` / status-LCD pattern, `Ignore`-tag filtering, safe try/catch
  block access — from `MissileGuidance`.
- Group/type block fetch idioms — from `CargoDisplay`/`GridRadar`.
