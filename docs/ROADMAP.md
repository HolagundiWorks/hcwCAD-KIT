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

- **Switch control:** tie each switch to the lights it controls (today a switch is counted on the board its wiring reaches).
- **More items:** exhaust fans, inverters, geyser points with their own rating, and any other item as a new kind in `ElectricalKinds`.
- **Circuits and loads:** number the circuits per board, give each point a wattage, and total the load per board and circuit.
- **Cable lengths:** the length of each run from the wiring polylines, for a cable schedule.
- **Live labels:** ID text follows a moved block only when a command runs; a reactor would move it at once.
- **Pass-through wires:** a wire that runs straight through a symbol does not connect to it today (only vertices do).

## Stairs

`AECSTAIR` and `AECSTAIREDIT` are built (see the README). Still to do:

- **Winders and quarter-space landings** for L and U types, and a half-space landing that is not the full width of both flights.
- **Headroom and railing:** headroom line over the section, handrail height, balustrade and stringer detail.
- **Reinforcement:** main and distribution bars in the waist slab and landing, with a bar schedule.
- **Levels and floors:** more than one storey in one command, with a stair per floor.
- **Section through an L or U as cut:** today an L is a developed section and a U returns over the first flight.
- **Quantity:** concrete volume, shuttering area and finishes per stair, into the take-off export.

## Walls, openings and grid

`HCWWALL`, `HCWWALLJOIN`, `HCWDOOR`, `HCWWINDOW` and `HCWAXIS` are built (see the README). Still to do:

- **Curved walls:** arcs in a centre line (today they are drawn straight).
- **Join on draw:** merge a new wall into the walls it touches as it is drawn, instead of a separate `HCWWALLJOIN`.
- **Wall objects:** keep the centre line and thickness with the wall so a wall can be edited and the openings follow it.
- **Opening types:** double leaf and sliding doors, door thickness and frame, sill and lintel heights as block attributes feeding the schedule.
- **Replace and move:** swap a door for a window, and move an opening along its wall with the cut following.
- **Grid editing:** add or remove a grid line, and columns placed on the intersections with a size schedule.

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
