# Defects

Opened and closed by **Agent 1**; answered by **Agent 2** (fills *Fix* and sets "Fixed, awaiting verification"). Format and rules: [../AGENTS.md](../AGENTS.md) sections 4 (L8) and 6. Statuses: Open, Fixed (awaiting verification) @ \<hash\>, Closed, Won't fix (owner only).

These four came from the owner's reports in the local session on 2026-10-06 and 2026-10-07. They are entered here as **Fixed, awaiting verification** because a fix was committed for each but none has been seen working in AutoCAD.

### D-001 · Ribbon not loading
Opened: 2026-10-06 by Agent 1 (owner's report) · Build: before `f11f12f` · Command: `NETLOAD`, ribbon
Steps: `NETLOAD` the plugin DLL in AutoCAD 2022; look for the hcwCAD-KIT tabs.
Expected: the tabs hcwCAD-KIT, hcwCAD-KIT Project and hcwCAD-KIT Settings.
Actual: "RIBBON NOT LOADING" (no command line text recorded).
Evidence: none recorded. `HCWDIAG` and `%APPDATA%\hcwCAD-KIT\diag.log` now exist to capture it.
Cause: *guess*: the old code gave up if the ribbon did not exist at the first idle.
Fix: `f11f12f` (retries until a ribbon exists, shows errors, adds `HCWRIBBON`); `HCWDIAG` `829b00c`; the Project tab was added later in `389613d`.
Verification note (Agent 1, 2026-10-07): cannot be checked headlessly, because the AutoCAD core console has no ribbon (`HCWDIAG` there reports "ribbon: none"). Needs the interactive check with the owner (TESTING.md section 1).
Status: Fixed, awaiting verification @ `f11f12f`

### D-002 · `HCWWALLREGEN` drew wall lines across the whole sheet
Opened: 2026-10-07 by Agent 1 (owner's screenshot) · Build: `5956903` · Command: `HCWWALLREGEN`
Steps: run on a plan whose walls are lines on `A-WALL`; drawing in inches or feet.
Expected: walls of 9 in and 4.5 in with clean junctions.
Actual: wall lines running far past the plan in both directions.
Evidence: the owner's screenshot (not saved in the repo).
Cause: *known* by reading the code: the corner extension was multiplied by the inverse of the unit scale (`1.0 / mm`), about 650 times too long in an inch drawing; unit tests only used millimetres.
Fix: `44dc5f6`.
Verification (Agent 1, 2026-10-07, build `888d27c`): `build/host-tests` test 06 runs `HCWWALLREGEN` in the core console on a synthetic 120 x 96 in plan in an inch drawing; the drawing extents stay inside the plan (`RESULT-EXT OK=1`). Not run on the owner's own drawing.
Status: Closed (verified headless on a synthetic plan @ `888d27c`; reopen if the owner's drawing still shows it)

### D-003 · `HCWWALLREGEN` used both faces of a wall as centre lines
Opened: 2026-10-07 by Agent 1 (owner's report) · Build: `5956903` · Command: `HCWWALLREGEN`
Steps: run on a plan drawn as two face lines per wall.
Expected: one wall per pair of faces.
Actual: each face line became a wall.
Cause: *known*: the command only read centre lines.
Fix: `e5ed41c` (Faces/Centres choice; Faces pairs facing lines and draws the centre lines first).
Verification (Agent 1, 2026-10-07, build `888d27c`): `build/host-tests` test 05, a 4000 x 3000 outer rectangle with a second one 230 inside it, answered Faces: "4 centre line(s) found between the faces: 4 at 228.6 mm", 4 corners, 4 centre lines on `A-WALL-CL`, 8 originals on `A-WALL-FACE`. Test 06 does the same in inches. Not run on the owner's own drawing.
Status: Closed (verified headless on a synthetic plan @ `888d27c`)

### D-004 · `HCWWALLREGEN` overlapped wall sections by 9 in at junctions
Opened: 2026-10-07 by Agent 1 (owner's report) · Build: `1ffa0c0` · Command: `HCWWALLREGEN`
Steps: regenerate a plan with a partition meeting a thicker wall.
Expected: outlines that touch and do not overlap.
Actual: wall outlines overlapping at junctions.
Cause: *known*: ends were pushed into the wall they met, and with merge-on-draw off the outlines stay separate.
Fix: `ca3acbd` (a wall stops at the other wall's face; at a corner of two thicknesses the thick wall runs out and the thin one stops at its face).
Verification note (Agent 1, 2026-10-07): the junction logic is covered by unit tests (L0), but no host test yet checks the geometry (that a partition outline stops at the wall face). Queued as T-007.
Status: Fixed, awaiting verification @ `ca3acbd`

### D-005 · Enter at a selection prompt is ignored in six commands (GetSelection returns Error, not None)
Opened: 2026-10-07 by Agent 1 · Build: `abedb4e` · Command: `HCWWALL` (found), and five more
Steps: run `HCWWALL`, press Enter at "Select centre lines to turn into walls (Enter to pick points)".
Expected: the prompt "Start of wall (Enter to finish)" (pick points).
Actual: the command ended at once. In the headless run the typed points were then read as commands (`Unknown command "0,0"`).
Evidence: a throwaway probe command in the core console printed `SELSTATUS=Error` for `GetSelection` + Enter and `POINTSTATUS=None` for `GetPoint` + Enter. `build/host-tests` test 03 failed before the fix and passes after it.
Cause: *known*: the code tests `psr.Status == PromptStatus.None` for "the user pressed Enter"; that is right for `GetPoint`, not for `GetSelection`, which returns `Error`.
Fix: `HCWWALL` (`CentreLines.cs`) hotfixed by Agent 1 in `67b9bd8` (accepts Error as well). **The same test is in five more places, left for Agent 2:**
- `AutoDimCommands.cs` ~169: `findRooms = psr.Status == PromptStatus.None` (Enter = find every room from the wall lines)
- `ColumnCommands.cs` ~64: Enter = every line on the grid layers
- `CleanCommands.cs` ~226: Enter = every line in this space
- `AreaStatementCommands.cs` ~315: `if (psr.Status == PromptStatus.None) return new Sum();`
- `OpeningCommands.cs` ~412: Enter = an opening drawn as a break in the wall (`HCWOPENMOVE` and friends)
Suggested: one helper (for example `Util.NoSelection(PromptStatus s)` meaning None or Error) used in all six places, and a host test for each (Agent 1 will add them to `build/host-tests` once the fix is in).
Fix (Agent 2): `Util.NoSelection(status)` (None or Error) now used in `AutoDimCommands`, `ColumnCommands`, `CleanCommands`, `AreaStatementCommands` and `OpeningCommands`, and also in `RoomTableCommands` and `RoomWallCommands`, which had the same test; `CentreLines` uses it too. `22009f2`.
Status: Fixed, awaiting verification @ `22009f2` (all six); `HCWWALL` itself was `67b9bd8`

### D-006 · Door and window fail with "no opposite wall face found" when picked in the middle of a wall drawn by HCWWALL
Opened: 2026-10-08 by Agent 1 · Build: `d584099` · Command: `HCWDOOR`, `HCWWINDOW`
Steps: `HCWWALL` (any thickness, setting `WallMeasureLines` at its default of 1); `HCWDOOR`, then pick the door on the wall's centre line (or anywhere closer to the centre line than to a face, about the middle half of the thickness).
Expected: the door is placed, as it is when picked nearer a face.
Actual: `HCWDOOR: no opposite wall face found.` (same for `HCWWINDOW`).
Evidence: `build/host-tests/wip/11-door-window-centre-pick.scr` (fails); the same picks 100 mm off the centre line pass (`tests/09-door-window-on-line-walls.scr`: tags D2/1 and W3/1).
Cause: *guess from reading `OpeningCommands.PlaceIn`*: the nearest segment to the pick is chosen first, and the take-off line `HCWWALL` draws down the centre line (layer `MEASURE-LINEAR`) is nearer than either face; the faces are then taken from that layer only, so only one line is found.
Fix: for Agent 2: leave the take-off layers out when looking for the wall faces (a segment on a `MEASURE-` layer is never a wall face). Then move test 11 from `wip/` to `tests/`.
Fix (Agent 2): `OpeningCommands.PlaceIn` skips segments on `MEASURE-*` layers (`Logic/WallFaceLayers.IsTakeOff`, 8 tests). `22009f2`. Agent 1: move test 11 from `wip/` to `tests/` when it passes.
Status: Fixed, awaiting verification @ `22009f2`
