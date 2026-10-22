# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Space Engineers Programmable Block (PB) scripts. Each folder in `src/` is one script, set up as a separate .NET Framework 4.7.2 class-library project in `src/SpaceEngineersScripts.sln`. The local build only exists to catch compile errors before a script is pasted into the game. The game compiles the source itself; the DLL from the build is never used.

- Every script is a `Program : MyGridProgram` class in `namespace ScriptingClass`. Most projects use `Program.cs`. Older ones use `Class1.cs`.
- The text between the `/// Copy code from here` and `//Copy to here` markers is what goes into the PB. The `using` lines and the namespace/class wrapper stay outside those markers, because the game provides them.
- `lib/` holds the game DLLs and their XML docs (`Sandbox.Common`, `Sandbox.Game`, `SpaceEngineers.Game`, `VRage.*`). Projects reference them with `..\..\lib\*.dll`. To check whether an ingame API member exists, grep the `lib/*.xml` docs, e.g. `grep -n "IMyShipController.HandBrake" lib/*.xml`.

## Build

Build one project (this is the only check available; there are no tests or linter):

```
cd src/<Project> && dotnet build <Project>.csproj -v minimal -nologo -p:LangVersion=6
```

- MSBuild prints its output in Polish: "Kompilacja powiodła się" means build succeeded, "Liczba błędów" is the error count.
- The 4 `MSB3270` architecture-mismatch warnings are harmless.
- `AutoManagr` already fails with one `CS0012` error about `MyStringHash` (missing VRage reference). Treat that build as clean as long as that is the only error.

## PB compiler constraints

- **The in-game compiler is C# 6.** A normal `dotnet build` uses a newer language version and accepts syntax the game rejects, which is why the build command above pins `-p:LangVersion=6`.
- Not allowed: digit separators, tuples, `is X x` patterns, local functions, `default` literal, throw expressions, target-typed `new()`.
- Allowed: `$""` interpolation, `?.`, `??`, `nameof`, expression-bodied members.
- Only `Sandbox.ModAPI.Ingame` and `VRage.Game.ModAPI.Ingame` APIs exist in the game (block groups are read-only; `MyIni` is available in `VRage.Game.ModAPI.Ingame.Utilities`).
- `IMyInventory.CurrentVolume` and `MaxVolume` are in m³, not liters.

## Adding a new script

1. Copy `src/EmptyTemplate/` to `src/<Name>/`.
2. In the `.csproj`, give the project a new `ProjectGuid` (uppercase) and change `RootNamespace` and `AssemblyName` to the new name.
3. In `Properties/AssemblyInfo.cs`, set the same GUID in lowercase in `Guid(...)`, and update `AssemblyTitle` and `AssemblyProduct`.
4. Register the project in `src/SpaceEngineersScripts.sln`: add a `Project(...)` / `EndProject` pair and 4 entries under `ProjectConfigurationPlatforms` (Debug/Release × ActiveCfg/Build.0).

## Architecture patterns used across scripts

- **One small task per run, to keep multiplayer servers from lagging** (from `src/AutoManagr/Program.cs`):
  - The code is built around `IManager.GetTasks()` and `IManagerTask.DoTask()/GetPriority()`.
  - Each `Main()` run executes at most one task. When the task queue is empty, it pulls the next manager's tasks, sorted by priority. When every manager has had a turn, the next round starts.
  - `GetTasks()` must stay cheap; the block API calls happen inside the tasks.
  - Large scans are split into fixed-size chunks, controlled by a const at the top of the file.
  - Every script ends `Main()` by printing `Runtime.LastRunTimeMs` and the instruction count with `Echo`.
  - `src/RoverHUD` is a newer, smaller example of the same pattern, using lambda `ActionTask`s.
- **Scripts that write status to LCDs** (`CargoDisplay`, `GridRadar`, `RoverHUD`):
  - The script reads a refresh / scan / render cycle of data from blocks and writes it to text surfaces.
  - It finds those surfaces through `IMyTextSurfaceProvider`, which covers both LCD panels and cockpit screens.
  - A `[Tag]` in a block's CustomName chooses which view that LCD shows.
  - The output is monospace text with `█`/`░` bars.
  - CargoDisplay splits long output into pages and never separates a section title from its items.
- **Configuration:** each script has a `// ------- CONFIG -------` block of consts at the top of the class. Newer scripts can also be configured through INI in the PB's CustomData. Blocks with `Ignore` in their name are skipped by automatic managers.

## Repo quirks

- Some projects have `bin/` and `obj/` committed even though `.gitignore` excludes them. Don't commit rebuilt binaries unless asked.
