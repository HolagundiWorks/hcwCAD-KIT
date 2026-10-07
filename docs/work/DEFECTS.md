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
Status: Fixed, awaiting verification @ `f11f12f`

### D-002 · `HCWWALLREGEN` drew wall lines across the whole sheet
Opened: 2026-10-07 by Agent 1 (owner's screenshot) · Build: `5956903` · Command: `HCWWALLREGEN`
Steps: run on a plan whose walls are lines on `A-WALL`; drawing in inches or feet.
Expected: walls of 9 in and 4.5 in with clean junctions.
Actual: wall lines running far past the plan in both directions.
Evidence: the owner's screenshot (not saved in the repo).
Cause: *known* by reading the code: the corner extension was multiplied by the inverse of the unit scale (`1.0 / mm`), about 650 times too long in an inch drawing; unit tests only used millimetres.
Fix: `44dc5f6`.
Status: Fixed, awaiting verification @ `44dc5f6`

### D-003 · `HCWWALLREGEN` used both faces of a wall as centre lines
Opened: 2026-10-07 by Agent 1 (owner's report) · Build: `5956903` · Command: `HCWWALLREGEN`
Steps: run on a plan drawn as two face lines per wall.
Expected: one wall per pair of faces.
Actual: each face line became a wall.
Cause: *known*: the command only read centre lines.
Fix: `e5ed41c` (Faces/Centres choice; Faces pairs facing lines and draws the centre lines first).
Status: Fixed, awaiting verification @ `e5ed41c`

### D-004 · `HCWWALLREGEN` overlapped wall sections by 9 in at junctions
Opened: 2026-10-07 by Agent 1 (owner's report) · Build: `1ffa0c0` · Command: `HCWWALLREGEN`
Steps: regenerate a plan with a partition meeting a thicker wall.
Expected: outlines that touch and do not overlap.
Actual: wall outlines overlapping at junctions.
Cause: *known*: ends were pushed into the wall they met, and with merge-on-draw off the outlines stay separate.
Fix: `ca3acbd` (a wall stops at the other wall's face; at a corner of two thicknesses the thick wall runs out and the thin one stops at its face).
Status: Fixed, awaiting verification @ `ca3acbd`
