# Handoff

> **Two agents work on this repo. Read [AGENTS.md](AGENTS.md) first** (who does what, and the rules), then [work/QUEUE.md](work/QUEUE.md), [work/DEFECTS.md](work/DEFECTS.md) and [work/TEST-LOG.md](work/TEST-LOG.md).

State of the work on 2026-10-07 at commit `dbcbd3a` (written by Agent 2), for whoever picks it up next. Read [ROADMAP.md](ROADMAP.md) for the list of known limits; this file covers what was verified, what was not, and what to do first.

## Verified (by Agent 2, rule B1: only what was run)

- L0 `dotnet test tests/HCW.Logic.Tests` on `dbcbd3a`: 643 passed, 0 failed, 0 skipped, no analyzer warnings.
- L1 `dotnet build build/CompileCheck.csproj` on `dbcbd3a`: 0 warnings, 0 errors.
- Agent 1's own L0 and L2 runs are in [work/TEST-LOG.md](work/TEST-LOG.md) (`c85216f`: 594 passed; Release x64 build against AutoCAD 2022, 0 warnings and 0 errors).

## Removed

The bylaw tables and limits: `AreaExemptRules`, `AreaPermTable`, `AreaZone`, `AreaFarPermittedPercent`, `AreaGroundCoverPermittedPercent` (the area statement no longer works out or fills permissible values, and exempt layers count in full), `LiftTable` (lift sizes are always typed), and `StairMaxRiseMm`, `StairMinRiseMm`, `StairMinGoingMm`, `Stair2RGMinMm`, `Stair2RGMaxMm` (the stair reports rise, going and 2R + G with no pass or fail). Old `settings.ini` files that still list them are harmless. The title block still has `FAR_PERM` and `GC_PERM` fields; the plugin leaves them for you to type.

## What has been run in AutoCAD

Nothing that is in the test log. The owner loaded builds in AutoCAD and reported the problems now in [work/DEFECTS.md](work/DEFECTS.md) (D-001 to D-004, all fixed and awaiting verification), and an earlier version of this file said "tested in AutoCAD with no errors" without a list of commands. By rule B2 that is **not tested**: there is no [TEST-LOG.md](work/TEST-LOG.md) entry with commands. Agent 1 verifies the commands below with the owner (queue item T-002).

## Not verified

- Every command, dialog, ribbon button and drawing change in the lists below has been compiled and (where it has pure logic) unit tested only. None has a test-log entry.
- The BricsCAD and ZWCAD projects were not built. The installers (`build\Package-Installers.ps1`) were not built (Inno Setup is not installed).
- The AQC bridge: the plan and contract exist (`AQC-BRIDGE-PLAN.md`, `bridge/`); the exporter logic (Q-002) and the `HCWBRIDGE` command (Q-003) are built but the command has never run; `HCWBRIDGEIMPORT` reads a file back; AQC has no importer and no "Export for CAD" yet and has not been run, so nothing has been imported into AQC.

## Built, not yet verified in a host (rule D5)

Newest first. "Logic" means unit tested; the command or dialog part is not.

- **Prompts in the drawing's units** (Q-004, HASH): door, window, replace, opening sync, escalator rise and landing, grid-line distance, room text height and the room-wall gap now ask in the drawing's units. Logic: `LengthInput` (12 tests). Column size, axis bays, lift, stair and rail still ask in mm.
- **`HCWBRIDGEIMPORT`** (Q-007, `dbcbd3a`; reader and comparison Q-006, `2d51fe9`): reads a bridge file back, lists changes, applies floors, project details and beam depths only on Yes. Logic: `JsonLite`, `BridgeImport` (22 tests). Never changes geometry. Steps in TESTING.md. **`docs/bridge/AQC-IMPORTER-SPEC.md`** (Q-008) specifies the importer for the AQC repo; nothing in it has met a running AQC.
- **`HCWBRIDGE`** (Q-003, `0e29e99`): reads the drawing into the bridge file next to the drawing. Logic: `BridgeMap` (marks, wall refs, schedule, project and level mapping, room de-duplication). Command: `Commands/BridgeCommands.cs`, Project tab panel "AQC Bridge", setting `BridgeFolder`. Riskiest: it reads every entity in the space, matches openings to walls by distance, and writes a file. Steps in TESTING.md.
- **Beam depth per floor** (Q-001, `aff3a61`): a Beam depth column on the Floors page of `HCWFLOORS`, kept with the project data by floor name (default: the first depth on the Beams page, else 450). Steps in [TESTING.md](TESTING.md).
- **Bridge exporter** (Q-002, `5b4b1c5`): `Logic/BridgeExport.cs`, the hcw-aqc-bridge v1 JSON writer. Logic only: no command, no CAD types. Output equals both sample files in `docs/bridge/fixtures` for drawings in mm, cm, m, in and ft.
- **Project tab** (`389613d`): `HCWPROJECT`, `HCWFLOORS`, `HCWBEAMS`, `HCWPROJECTTITLE`, `HCWOPENSTD`. Logic: `ProjectData`.
- **Standard door, window and ventilator marks** (`4a0d34a`): D1 800, D2 900, D3 1200; W1 600 to W5 2000; V1 600, accepted at the width prompt of `HCWDOOR` and `HCWWINDOW`; doors tagged code/number like windows (`DoorTagFormat`). Logic: `OpeningStandards`.
- **`ROOMTEXTFIT`, `ROOMTEXTFITSET`** (`03e3e1b`): MTEXT in a room to TEXT, stacked and centred, height capped (`RoomTextMaxMm`). Logic: the fit arithmetic.
- **Walls**: a wall stops at the other wall's face with no overlap at junctions, and wall thickness is asked in the drawing's units (`ca3acbd`); `WallJoinOnDraw` defaults to 0 and `LayerOutput` to `HCW` (`eb2e29b`); `HCWWALLREGEN` has a Faces mode (default) and a Centres mode (`e5ed41c`, `44dc5f6`). These are the fixes for D-002 to D-004.
- Room picker lists names alphabetically (`1ffa0c0`).

## Added earlier (all compiled and unit tested only)

- Levels kept in the drawing with slab thickness (`HCWLEVELS`), `HCWSECTIONDRAW`, `HCWOPENHEIGHT`, `HCWOPENCONVERT`; stairs, lift, doors and windows default their heights from the levels.
- Opening schedule on the Walls & Openings panel (`HCWOPENSCHED`), updated automatically after the door and window commands (`OpeningAutoSync`).
- Walls draw a take-off line (`WallMeasureLines`), a wall hatch (`HCWWALLHATCH`, `WallHatch`), and a lintel through the wall on request (`HCWLINTEL`; `LintelAuto` = 1 makes one for every opening). Windows are tagged `code/number`.
- Column table with height and floor, `HCWCOLQTY`; `MQTYSUM` adds stair and column concrete and shuttering; room outlines also go on the floor and ceiling take-off layers (`RoomMeasureOutlines`).
- `ELSCHEDULE` Room layout; `HCWLEVEL` Levels option; `HCWAREASTMT` takes floors from the levels and can reuse the outlines of the floor before.
- Auto dimension reads single-line walls at any angle (faces only reach the wall-segment dimensions; chains and gap openings stay horizontal and vertical).
- Ribbon regrouped (Walls & Openings, Structure, Levels & Sections, Rooms & Areas, Take-off, Electrical, Symbols, Drawing & Text Tools, Notes) and `HCWRIBBON` added.
- `HCWLEGEND` no longer throws `eKeyNotFound`.

Riskiest untested pieces: `HCWWALLREGEN` (the junction logic is unit tested, but the wall creation, join and hatch after it have not run), the wall hatch (region booleans on the joined walls, redrawn after every opening command), the lintel and window-tag changes inside `OpeningCommands.PlaceIn` (the path every door and window takes), and the section tool (crossings of the section line with the wall and column layers).

## Open problem: ribbon not loading (D-001)

The user reported "RIBBON NOT LOADING" after a `NETLOAD` of a build from this repo. It is not confirmed fixed.

What the code does now (`src/HCW.AutoCAD.Plugin/UI/HcwRibbonApplication.cs`): `Initialize()` subscribes to `Application.Idle`. The handler keeps trying on each idle until `ComponentManager.Ribbon` exists, then unsubscribes and calls `BuildRibbon()`. Both tabs are added only after every panel has been built, so a failing panel leaves no half-built tab. Any exception is written to the command line as `[hcwCAD-KIT] ribbon build error: <type>: <message>` (an alert if no drawing is open). `HCWRIBBON` rebuilds the tabs, replacing any of ours already there.

Things to check, in order (none confirmed):

0. Run `HCWDIAG` (works even with no ribbon) and read `%APPDATA%\hcwCAD-KIT\diag.log`: they show whether the DLL loaded, the CAD version, whether a ribbon exists and whether our tabs are on it.
1. Did the command line print `[hcwCAD-KIT] ribbon build error: ...`? If so, that message is the lead. Run `HCWRIBBON` to see it again.
2. If it printed "waiting for the ribbon", the workspace has no ribbon: switch to one (`RIBBON`), then run `HCWRIBBON`.
3. A tab with the same id already existing (second `NETLOAD`, or an old copy of the DLL loaded). Restart AutoCAD to rule this out.
4. The build used the AutoCAD 2022 reference DLLs; check the DLL was loaded into the same AutoCAD version.
5. `IconLoader` returns null for a missing icon (all icon names used were checked against the embedded resources); `TitleNoteLibrary.Ensure()` runs while the notes panel builds, and an exception from it lands in the same error message.

## Build on the dev machine used so far

- No system .NET SDK. A per-user SDK 8.0.425 is in `%LOCALAPPDATA%\Microsoft\dotnet` and is on the user PATH (new terminals only).
- `git` is not on PATH; the copy bundled with GitHub Desktop was used: `C:\Users\holag\AppData\Local\GitHubDesktop\app-3.6.6\resources\app\git\cmd\git.exe`.
- The only AutoCAD installed is 2022. Passing `-p:AutoCADInstallDir="...\"` fails in PowerShell (the trailing `\"` is mangled), so set the environment variable instead:

```
$env:AUTOCAD_INSTALL_DIR = "C:\Program Files\Autodesk\AutoCAD 2022\"
dotnet build src\HCW.AutoCAD.Plugin\HCW.AutoCAD.Plugin.csproj -c Release -p:Platform=x64
dotnet test tests\HCW.Logic.Tests
```

Output: `src\HCW.AutoCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`. Close and reopen AutoCAD before loading a new build, or the old DLL stays loaded.

## Git and push

- Upstream `main` can receive new commits quickly, so a local branch falls behind. Use `git pull --rebase` and push straight afterwards.
- Pushing from an agent shell can fail with `could not read Username for 'https://github.com'`: the Git Credential Manager has no saved sign-in there. Push from GitHub Desktop or a terminal that is signed in to GitHub.

## What to do next

Agent 1 and the owner own the host steps; the development queue is [work/QUEUE.md](work/QUEUE.md).

1. Load the build in AutoCAD; if the ribbon does not appear, read the command line message or run `HCWRIBBON` (above).
2. Follow [TESTING.md](TESTING.md) (an ordered checklist with what to expect and what to send back) and fix what breaks. Start with `SHEETFIT`, `HCWWALL` (hatch, measurement line), door and window insert (lintel, `W1/3` tags, schedule sync), `HCWAXIS`, `HCWCLEAN`, `HCWCORNER`, `HCWAUDIT`, `HCWSECTIONDRAW`, `HCWCOLQTY`, `MQTYSUM`. If the wall hatch or lintels cause trouble, settings `WallHatch`, `WallMeasureLines` and `OpeningAutoSync` can be set to 0 to switch each off.
3. Build and test the BricsCAD and ZWCAD projects (they need the host API DLLs).
4. Build the installers with `build\Package-Installers.ps1` (Inno Setup 6 required) and test one install.
5. Bylaw scrutiny is handled by separate software, so there are no bylaw figures to set: the plugin's job is to put items on the building permit layers (`HCWPERMITLAYERS`, `LayerOutput`). Check on a real drawing that each tool's output lands on the right `BP-` layer for that software to read.
