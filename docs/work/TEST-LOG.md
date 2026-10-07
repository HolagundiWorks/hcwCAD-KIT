# Test log

Written by **Agent 1** only. One entry per tested build, newest first. Rules: [../AGENTS.md](../AGENTS.md) (B1, B2, L1 to L3). Levels: L0 unit tests, L1 CAD-free compile check, L2 real build against AutoCAD 2022, L3 host test (AutoCAD), L4 install and uninstall, L5 AQC round trip. Every entry ends with **Not run**.

## Machine

(Agent 1 keeps this current, rule L9. Same facts as AGENTS.md section 8.)

- Windows 11; .NET SDK 8.0.425 (per-user); AutoCAD 2022; `accoreconsole.exe` present.
- Not installed: Inno Setup 6, BricsCAD, ZWCAD, CMake, MSVC, WinUI workload.

## 2026-10-07 · `888d27c` · PARTIAL (first automatic host run)

Source under test: `abedb4e` plus the hotfix `67b9bd8`; `888d27c` adds only the test harness. All levels run on the clean tree of `888d27c`.

| Level | Check | Result |
|---|---|---|
| L0 | `dotnet test tests/HCW.Logic.Tests` | PASS: 594 passed, 0 failed |
| L1 | `dotnet build build/CompileCheck.csproj` | PASS: 0 warnings, 0 errors |
| L2 | Release x64 build against AutoCAD 2022 (`--no-incremental`) | PASS: 0 warnings, 0 errors; DLL 864,256 bytes, built 22:05:19 |
| L3 automatic | `build\host-tests\Run-HostTests.ps1` in `accoreconsole.exe` | PASS: 6 of 6 |

The six automatic host tests (on blank drawings made from the AutoCAD templates, not the owner's drawings):

| Test | What it shows |
|---|---|
| 01-load | the DLL loads in the core console; `HCWDIAG` runs (163 commands registered; millimetre drawing) |
| 02-wall-from-lines | `HCWWALL` on four selected lines: 4 outlines on `A-WALL`, 1 hatch, 4 take-off lines |
| 03-wall-pick-points | `HCWWALL` with Enter to pick points: closed run gives 2 outlines, 1 hatch, 1 take-off line (**failed before hotfix `67b9bd8`**, D-005) |
| 04-wall-units-inches | in an inch drawing the prompt reads "Wall thickness in in <9.0551>" and the walls are 9.0551 in thick |
| 05-wallregen-faces | `HCWWALLREGEN` Faces: 4 centre lines at 228.6 mm, 4 corners, centre lines on `A-WALL-CL`, faces on `A-WALL-FACE` (D-003) |
| 06-wallregen-inches-no-runaway | the same in an inch drawing; the drawing extents stay inside the plan (D-002) |

Findings: **D-005** (Enter at a selection prompt is ignored in six commands; one fixed by hotfix, five open for Agent 2). Feasibility of T-003: **yes**, with limits: the console has no ribbon or dialogs, so the ribbon (D-001) and the WinForms dialogs (Project data, room picker) still need the interactive run; `NETLOAD` needs an unquoted path and `SECURELOAD` 0; a script must not end with a blank line (Enter repeats the last command). TESTING.md was out of date and is corrected in the same push (command count 163, not "above 250"; door and window tags follow the standard marks).

Not run: **L3 interactive** (no command was run in AutoCAD with the owner: D-001 ribbon, D-004 junction geometry, every command not in the six tests), **L4** (no installer; Inno Setup not installed), **L5**, BricsCAD, ZWCAD. The harness has no negative control beyond test 03 (which failed before the fix, in a manual run of the same input).

## 2026-10-07 · `c85216f` · PARTIAL

| Level | Check | Result |
|---|---|---|
| L0 | `dotnet test tests/HCW.Logic.Tests` | PASS: 594 passed, 0 failed, no analyzer warnings |
| L2 | Release x64 build against AutoCAD 2022 | PASS: 0 warnings, 0 errors. DLL `bin\x64\Release\hcwCAD-KIT.dll`, built 2026-10-07 21:24 from the source of `ca3acbd` (the commits after it changed only docs and tests) |

Not run: **L1** (no check of `build/CompileCheck.csproj` on this commit; CI runs it on push), **L3** (no command in a CAD program was run by Agent 1, so none of D-001 to D-004 is verified; the owner loaded earlier builds in AutoCAD and reported the problems now in DEFECTS.md, but no commands or results were recorded), **L4** (no installer built: Inno Setup is not installed; the DLL was loaded with `NETLOAD`, not installed), **L5** (AQC cannot be built here), BricsCAD and ZWCAD.

## 2026-10-07 (late) · merged tree with Agent 2's Q-001 to Q-008 · PARTIAL

L0 643 passed. L1 clean. L2 clean (DLL 924,160 bytes). L3 automatic: tests 01 to 06 pass; test 07 (HCWBRIDGEIMPORT, Q-007) passes: lists the changes, No changes nothing, Yes applies 10 changes, HCWLEVELS shows Ground and First, a second run finds nothing to change.

Open: test 08 (HCWBRIDGE export, Q-003) is unfinished and parked in build/host-tests/wip. HCWDOOR and HCWWINDOW answered "no opposite wall face found" on walls drawn by HCWWALL pick points, so no door or window was drawn and HCWBRIDGE was not reached. Unknown whether the script's pick points are wrong or there is a defect; try the same picks on walls made from selected lines (test 02's input).

Not run: L3 interactive (D-001, D-004, the Project and floors dialogs, the beam depth column, HCWBRIDGE export), L4, L5, BricsCAD, ZWCAD. Q-001, Q-002, Q-003 and Q-006 are verified only to L0 and L1; Q-007 through test 07.
