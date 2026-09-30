# Roadmap

What is not built yet. Everything else is described in the [README](../README.md). Nothing here has been run inside a CAD host: the commands compile against the AutoCAD .NET reference package and the CAD-free logic has unit tests, but each host needs its own testing before release.

## Auto dimension (`AUTODIM`) next steps

`AUTODIM` v1 dimensions the outside of axis-aligned plans (openings, structure and overall chains). Still to do, in this order:

1. **Grid lines and columns** as structure points, so a structural grid drives the middle chain.
2. **Interior rooms.** Dimension each room's clear width and depth once, just inside the walls, using `ROOM` labels or closed polylines.
3. **Collision handling.** Move a chain out by a step when its text would overlap another dimension's text.
4. **Angled and curved walls.** Aligned dimensions along the wall direction, and radius dimensions for arcs. Today they are skipped and counted.
5. **Associative dimensions**, so moving a wall updates them.

Open questions: dimension to wall faces or centrelines by default when only one face is drawn, and which plotted scales matter most (1:50, 1:100).

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
