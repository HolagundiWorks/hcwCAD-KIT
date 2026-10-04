# Roadmap

What is not built yet. Everything else is described in the [README](../README.md). Nothing here has been run inside a CAD host: the commands compile against the AutoCAD .NET reference package and the CAD-free logic has unit tests, but each host needs its own testing before release.

## Auto dimension next steps

`AUTODIM`, `AUTODIMROOM` and `AUTODIMWALL` are built (see the README). Still to do:

1. **Rooms from walls.** `HCWROOMWALLS` finds each room outline from the wall faces (see the README). Still to do: finding every room in one go instead of by pick, rooms that close across a door or window gap, curved walls, free-standing columns cut out of the room, and `AUTODIMROOM` doing this step itself.
2. **Openings from wall gaps.** A break in a wall line is not yet labelled as an opening. **Non-rectangular rooms.** Openings along the edges of L-shaped and other outlines (today only rectangular rooms get them).
3. **Cross-chain collisions.** Rows stop a chain overprinting itself; a text moved to row 3 can still meet the next chain's text at some step and scale settings.
4. **Associative dimensions**, so moving a wall updates them.
5. **Levels and tags.** Level marks are built (`HCWLEVEL`). Still to do: opening tags (`D1`, `W1`) from the schedule, and room names beside the room dimensions.

Open questions: single-line (centreline) walls and mixed drawings are not handled: only faces (two lines or a closed outline).

## Electrical

`SBNUM`, `LPNUM`, `FPNUM`, `ELCONNECT`, `ELSCHEDULE` and `ELUPDATE` are built (see the README). Still to do:

- **Switch control:** tie each switch to the lights it controls (today a switch is counted on the board its wiring reaches).
- **More items:** exhaust fans and inverters are kinds now, and a point's load is set per kind in `ElectricalWatts`. Still to do: a rating per individual point (two geysers of different size), and any other item as a new kind in `ElectricalKinds`.
- **Circuits and loads:** circuits are numbered per board with the points on each and its load (`ELSCHEDULE`, **Circuit**), and demand uses diversity factors (**Load**). Still to do: cable and breaker sizes, and balancing the load across phases.
- **Cable lengths:** the cable schedule (`ELSCHEDULE`, **Cable**) totals the drawn wiring per switchboard with a drop and an allowance. Still to do: lengths per circuit, per cable size and conductor count, and a bill of quantities with the wire sizes.
- **Live labels:** ID text follows a moved block only when a command runs; a reactor would move it at once.
- **Pass-through wires:** a wire that runs straight through a symbol does not connect to it today (only vertices do).

## Stairs

`AECSTAIR` and `AECSTAIREDIT` are built (see the README). Still to do:

- **Winders and quarter-space landings** for L and U types, and a half-space landing that is not the full width of both flights.
- **Headroom and railing:** headroom line over the section, handrail height, balustrade and stringer detail.
- **Reinforcement:** an estimate and bar schedule are saved with the quantities. Still to do: drawing the bars in the section and plan, cranks, hooks and top steel, and bar bending shapes.
- **Levels and floors:** more than one storey in one command, with a stair per floor.
- **Section through an L or U as cut:** today an L is a developed section and a U returns over the first flight.
- **Quantity:** concrete, shuttering, finishes and skirting per stair are worked out and saved as a take-off for `MEXPORT`. Still to do: landing edge shuttering, and quantities for stairs not made by `AECSTAIR`.

## Walls, openings and grid

`HCWWALL`, `HCWWALLJOIN`, `HCWDOOR`, `HCWWINDOW` and `HCWAXIS` are built (see the README). Still to do:

- **Curved walls:** arcs in a centre line (today they are drawn straight).
- **Join on draw:** merge a new wall into the walls it touches as it is drawn, instead of a separate `HCWWALLJOIN`.
- **Wall objects:** keep the centre line and thickness with the wall so a wall can be edited and the openings follow it.
- **Opening types:** double leaf and sliding doors, door thickness and frame, sill and lintel heights as block attributes feeding the schedule.
- **Replace and move:** done for blocks made by `HCWDOOR` and `HCWWINDOW`. Still to do: openings drawn some other way, moving several at once, and dragging an opening along its wall with the cut following live.
- **Clean-up:** `HCWCLEAN` removes zero-length and duplicate lines and joins collinear ones. Still to do: polylines and arcs (joining end to end, removing duplicates), and a one-click corner that trims or extends two wall lines to meet.
- **Lift and escalator:** `HCWLIFT` draws the plan of a passenger lift shaft. Still to do: lift sizes by capacity from a table you set, a lift pit and machine room, a lift section, and escalators with an adjustable angle, length and landing.
- **Handrail:** `HCWRAIL` draws the plan. Still to do: a balustrade with balusters and a handrail height in elevation, rails that follow the stair flight, and curved rails.
- **Grid editing:** add or remove a grid line.
- **Columns:** `HCWCOLUMN` can put columns on `MEASURE-COLUMN` for the take-off. Still to do: different sizes at chosen intersections in one pass, grid-anchored offsets (edge columns flush with a wall face), and column blocks with a mark attribute.

## Symbols

`HCWLEVEL` (floor and ceiling marks), `HCWLEVELSCHED`, `HCWELEV`, `HCWNORTH`, `HCWSECTION` and `HCWSLOPE` are built (see the README). Still to do:

- **Section heads from the sheet set:** fill the sheet number in the section bubble from the layout it is drawn on.
- **Live datum:** level marks that update when the datum moves.

## Take-off

- **Copy/array.** `INCARRAY` and `INCCOPY` deep-clone the selection once per copy. Cloning entities directly would be lighter but can lose block attributes. Measure on a large drawing before changing it.
- **Colour mapped deductions.** Lines inside blocks cannot be recoloured per insert. Colouring the standalone deduction lines by schedule name is possible.
- **Wall numbering by room.** Today: left to right, top to bottom, or along a picked path.
- **Deduction matching.** Index walls by bounding box for very large selections (a box test already rejects most pairs).

## Area statement

`HCWAREASTMT` is built (see the README). Still to do:

- **Read floors from layers:** take each floor's outlines from layers (`BP-BUILDING-CUT` per floor) instead of selecting them.
- **Permissible values:** `FAR_PERM` and `GC_PERM` are filled from two settings and an excess is flagged. Still to do: a table by plot size and zone, which needs the local bylaw figures.
- **Exemptions by rule:** a bylaw table of what may be left out of the FAR (stair, lift, parking, balcony projections) so deductions are not selected by hand.
- **More than four floors** in the title block, and a statement that updates when the outlines change.

## Schedules and sheets

- Sheet set: set viewport layers and per-sheet scales; fill more title-block fields (project, owner, architect) from the fields library.
- Openings on more than one floor: tie a schedule entry to a floor so the lintel-bottom check uses that floor's value.

## What needs a CAD host or a design decision first

These are left because they cannot be built or checked without running inside AutoCAD, BricsCAD or ZWCAD, or need a decision about how the tool should behave:

- **Reactors:** associative dimensions, live label and datum updates and dragging an opening along its wall all depend on object reactors, which behave differently in each host.
- **Wall objects:** keeping a wall's centre line and thickness with it, so openings follow when it is edited, changes how every wall and opening tool stores its data.
- **Curved walls and rooms that close across a gap,** and single-line (centreline) walls, need decisions on how an arc offsets and where a gap becomes an opening.
- **Bylaw tables** (permissible FAR and ground cover by plot size, what is exempt from the FAR) need the figures for the local rules.
- **Sheet set viewport layers and scales,** and the section bubble taking its sheet number from the layout, need testing against real sheet sets.

## Quality

- Move more pure logic (schedule maths, table layout) into `src/HCW.AutoCAD.Plugin/Logic` and cover it with tests.
- Build the BricsCAD and ZWCAD projects in CI. They need the host API DLLs, which are not on public runners.
