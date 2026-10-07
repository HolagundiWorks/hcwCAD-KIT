# Work queue

Kept by **Agent 2** (development queue and questions for the owner) and **Agent 1** (local queue). Formats and rules: [../AGENTS.md](../AGENTS.md) sections 5 and 6. Status words are exactly: Todo, In progress, Ready for verification @ \<hash\>, Done, Blocked (on what).

Last updated 2026-10-07 by Agent 2 (development section: Q-001 to Q-003, Q-005 to Q-008 worked; Local queue untouched).

## Development queue (Agent 2)

### Q-001 · Beam depth per floor on the Floors page
Priority: P1 · Status: Ready for verification @ aff3a61
Why: AQC computes a column's clear height as `height - slab - beam depth`, so the bridge needs a beam depth for every floor (see [../AQC-FINDINGS.md](../AQC-FINDINGS.md)).
Acceptance: the Floors page of `HCWFLOORS` has a "Beam depth" column (mm); it is saved with the project data in the drawing; `ProjectData` has the per-floor depth with tests; the default is the first standard depth in the Beams page, else 450. README updated.
Notes: floors live in the take-off book (`MeasureBook.FloorSpec`, which has no beam field), so keep the depths in the project data, keyed by floor name.
Done by Agent 2: the Floors page has a Beam depth column; `ProjectData.FloorBeamMm` / `SetFloorBeam`, saved as `BEAMFLOOR|name|depth` lines; four tests. L0 (614 passed) and L1 clean on `5b4b1c5`. **To verify in AutoCAD** (steps in TESTING.md, "Beam depth per floor"): the column is there, shows 450 or the first Beams depth for a new floor, keeps a typed value after OK and reopen, and drops the depth of a removed floor.

### Q-002 · Bridge exporter logic (CAD-free)
Priority: P1 · Status: Ready for verification @ 5b4b1c5
Why: first half of the AQC bridge ([../AQC-BRIDGE-PLAN.md](../AQC-BRIDGE-PLAN.md), Phase 2).
Acceptance: `Logic/BridgeExport.cs` turns plain records (levels, walls, openings, schedule, columns, lintels, rooms, project) into the `hcw-aqc-bridge` v1 JSON with a hand-written JSON writer (no new dependency, rule D3); output for the sample inputs equals `docs/bridge/fixtures/small-house.json`; tests cover millimetre conversion from inches, feet, centimetres and metres.
Notes: no CAD types in this class.
Done by Agent 2 on the recommended answers: `Logic/BridgeExport.cs` with plain records; the output equals `small-house.json` and `levels-only.json` byte for byte, and a small house given in mm, cm, m, in or ft gives the same file (11 tests). Nothing to try in a CAD program; Agent 1 only needs L0 on the commit. If the owner answers the questions differently, the field names change with a new `version`.

### Q-003 · `HCWBRIDGE` command and ribbon panel
Priority: P2 · Status: Ready for verification @ 0e29e99
Why: second half of Phase 2.
Done by Agent 2: `Commands/BridgeCommands.cs`, `Logic/BridgeMap.cs` (7 tests), "AQC Bridge" panel on the Project tab, setting `BridgeFolder`; slabs are not sent (the plugin does not draw them). L0 (621 passed) and L1 clean on `0e29e99`. **To verify in AutoCAD**: the steps in TESTING.md "AQC bridge export" (file written next to the drawing, same drawing id on a second run, `@floor` refs for 0, millimetres in an inch drawing, a clear message for an unsaved drawing).
Acceptance: `HCWBRIDGE` reads the drawing (floors, wall objects, openings, columns, lintels, rooms, project data), writes `<drawing>.aqcbridge.json` next to the drawing (or into setting `BridgeFolder`), and reports counts per section and what it skipped; a drawing id (GUID) is stored in the drawing on first export; an "AQC Bridge" panel on the Project tab. Added to TESTING.md (rule D6).

### Q-006 · Bridge file reader and import comparison (CAD-free)
Priority: P1 · Status: Ready for verification @ 2d51fe9
Why: Phase 4 of the plan (AQC to plugin: levels, project details, beam depths), and the exporter's round-trip check.
Acceptance: `Logic/JsonLite.cs` (no JSON library, rule D3) and `Logic/BridgeImport.cs`; the sample files are read; exporter output reads back; other formats, versions and units are refused with a reason; the comparison lists old and new values, matches floors by name then by position, never imports the plinth, adds new floors, and leaves absent values alone.
Notes: done by Agent 2, 22 tests. Logic only: Agent 1 needs L0 on the commit.

### Q-007 · `HCWBRIDGEIMPORT` command
Priority: P2 · Status: Ready for verification @ dbcbd3a
Why: the plugin side of Phase 4.
Acceptance: asks for the file (default next to the drawing), lists the changes, applies only on Yes (default No), one undo step, changes only floors, project details and beam depths. Steps in TESTING.md "AQC bridge import". Done by Agent 2; to verify in AutoCAD.
Notes: owner question 2 (authority) is answered here by the recommended rule: the drawing keeps the last word because nothing is applied without a Yes.

### Q-008 · Specification of the AQC importer
Priority: P2 · Status: Done (documentation, `dbcbd3a`)
Why: Phase 3 happens in the AQC repo, which Agent 2 cannot reach and which needs the owner's yes (question 3).
Acceptance: `docs/bridge/AQC-IMPORTER-SPEC.md` says, row by row, what each file section becomes in AQC, the idempotence and re-import rules, what is never imported, and the tests the importer library needs. It is built from AQC-FINDINGS.md (read from the code, never run), so *check* items remain.

### Q-004 · Door, window and other prompts in the drawing's units
Priority: P3 · Status: Todo (needs the owner's yes: see the questions)
Why: walls now ask in the drawing's units (`Util.DrawingUnitName`, `Util.UnitsToMm`); doors, windows, columns and the rest still ask in mm.

### Q-005 · README and HANDOFF catch-up
Priority: P2 · Status: Done (documentation only, `efda0bd`)
Why: the Project tab, `ROOMTEXTFIT`, the standard opening marks, wall junctions without overlap and `HCWWALLREGEN` Faces mode were documented in the README by the local agent; `HANDOFF.md` does not list them under "built, not yet verified in a host".
Acceptance: `HANDOFF.md` lists each; `docs/TESTING.md` has steps for each (rule D6).

### Questions for the owner
1. **Direction first for the AQC bridge.** Recommended: plugin to AQC.
2. **Authority for levels and project details** once both sides hold them: the drawing, AQC, or a prompt on each change. Recommended: the drawing for what the drawing measures, AQC for rates and the plinth; a prompt for floors.
3. **May the AQC repo be changed** (the importer must live there)? Recommended: yes, as a small plain `net8.0` library plus one menu item, built and tested separately.
4. **Plinth.** Recommended: plugin floors map to `Lvl1` onward; AQC keeps `Lvl0`.
5. **Rebar.** Recommended: the plugin never sends bar data; AQC's engine owns it.
6. **Version 1 scope.** Recommended: levels, project, masonry and openings; columns, lintels and rooms next.
7. **Q-004:** convert the door, window and other prompts to the drawing's units too? Recommended: yes.

8. **The file AQC writes for the plugin** (Phase 4): name and who writes it. Recommended: AQC's "Export for CAD" writes `<project>.aqcbridge.json` in the same v1 format (project, levels, beam depths); the plugin's `HCWBRIDGEIMPORT` already reads it.
9. **Who builds the AQC importer**, and where. The spec is in `docs/bridge/AQC-IMPORTER-SPEC.md`. Agent 2 cannot reach the AQC repo (rule section 3); Agent 1 has a read-only clone. Recommended: a separate session on the AQC repo, with the owner's yes (question 3).

Agent 2 has carried on with the recommended answers to questions 1, 2, 4, 5 and 6 (QUEUE items Q-001 to Q-008), as rule X4 allows. None of them is recorded as an owner decision until the owner says so.

## Local queue (Agent 1)

### T-001 · Verify each release candidate (standing)
Priority: P1 · Status: In progress
Every RC Agent 2 names: pull, build that hash, L0, L2, install, host checklist with the owner; write the TEST-LOG entry; open or close defects.

### T-002 · Walk TESTING.md sections 1 to 5 in AutoCAD with the owner
Priority: P1 · Status: Todo (needs the owner present)
Why: nothing in the command layer has a TEST-LOG entry yet; "tested in AutoCAD" in `HANDOFF.md` has no list of commands. First verify the four defects in [DEFECTS.md](DEFECTS.md).

### T-003 · Feasibility: automated command tests with `accoreconsole.exe`
Priority: P2 · Status: Todo
Why: `accoreconsole.exe` is installed. If it can `NETLOAD` the plugin and run commands from a script on a throwaway drawing, L3 can be partly automatic.
Acceptance: a written yes or no with evidence (what loaded, what failed). If yes: a script and a test drawing under `tests/host/`, and the steps in TEST-LOG.
Notes: the plugin references the UI assemblies (`AcMgd`, `AdWindows`); the core console may refuse to load them. Unknown until tried.

### T-004 · Installers
Priority: P2 · Status: Blocked (Inno Setup 6 is not installed; needs the owner's yes, rule L5)
Acceptance: `build\Package-Installers.ps1 -Only AutoCAD` builds a setup program; install it, confirm the ribbon, uninstall it; TEST-LOG entry.

### T-005 · Tools to build and run AQC
Priority: P3 · Status: Blocked (CMake, MSVC and the WinUI workload are not installed; needs the owner's yes, rule L5)
Why: Phase 5 of the bridge plan needs a running AQC to check an import. Until then AQC facts come from reading its code only.

### T-006 · BricsCAD and ZWCAD builds
Priority: P3 · Status: Blocked (neither is installed; the host API DLLs are needed)
