# Roadmap

What is not built yet. Everything else is described in the [README](../README.md). Nothing here has been run inside a CAD host: the commands compile against the AutoCAD .NET reference package and the CAD-free logic has unit tests, but each host needs its own testing before release.

## Auto dimension next steps

`AUTODIM`, `AUTODIMROOM` and `AUTODIMWALL` are built (see the README). Still to do:

1. **Rooms from walls.** `AUTODIMROOM` needs room outlines. Deriving the clear size of each room from the wall faces would remove that step.
2. **Openings from wall gaps.** A break in a wall line is not yet labelled as an opening. **Non-rectangular rooms.** Openings along the edges of L-shaped and other outlines (today only rectangular rooms get them).
3. **Cross-chain collisions.** Rows stop a chain overprinting itself; a text moved to row 3 can still meet the next chain's text at some step and scale settings.
4. **Associative dimensions**, so moving a wall updates them.
5. **Levels and tags.** Level marks, opening tags (`D1`, `W1`) from the schedule, and room names beside the room dimensions.

Open questions: single-line (centreline) walls and mixed drawings are not handled: only faces (two lines or a closed outline).

## Electrical

`SBNUM`, `LPNUM`, `FPNUM`, `ELCONNECT`, `ELSCHEDULE` and `ELUPDATE` are built (see the README). Still to do:

- **Other points:** switches, sockets and fittings on the same pattern, with each switch tied to the lights it controls.
- **Circuits and loads:** number the circuits per board, give each point a wattage, and total the load per board and circuit.
- **Cable lengths:** the length of each run from the wiring polylines, for a cable schedule.
- **Live labels:** ID text follows a moved block only when a command runs; a reactor would move it at once.
- **Pass-through wires:** a wire that runs straight through a symbol does not connect to it today (only vertices do).

## Take-off

- **Copy/array.** `INCARRAY` and `INCCOPY` deep-clone the selection once per copy. Cloning entities directly would be lighter but can lose block attributes. Measure on a large drawing before changing it.
- **Colour mapped deductions.** Lines inside blocks cannot be recoloured per insert. Colouring the standalone deduction lines by schedule name is possible.
- **Wall numbering by room.** Today: left to right, top to bottom, or along a picked path.
- **Deduction matching.** Index walls by bounding box for very large selections (a box test already rejects most pairs).

## Schedules and sheets

- Sheet set: set viewport layers and per-sheet scales; fill more title-block fields (project, owner, architect) from the fields library.
- Openings on more than one floor: tie a schedule entry to a floor so the lintel-bottom check uses that floor's value.

## Quality

- Move more pure logic (schedule maths, table layout) into `src/HCW.AutoCAD.Plugin/Logic` and cover it with tests.
- Build the BricsCAD and ZWCAD projects in CI. They need the host API DLLs, which are not on public runners.
