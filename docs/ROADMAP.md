# Roadmap

What is not built yet. Everything else is described in the [README](../README.md). Nothing here has been run inside a CAD host: the commands compile against the AutoCAD .NET reference package and the CAD-free logic has unit tests, but each host needs its own testing before release.

## What was built from the old list

Every item that was on this roadmap has been built, in the form the README describes: rooms from walls (all rooms, gap bridging, column islands, arcs), openings from gaps, non-rectangular room openings, spacing between dimension chains, associative dimensions (the plugin's own tracking), opening tags and room names in the auto dimensions, the whole electrical list (switch control, per-point ratings, your own kinds, breakers, cables, phases, cable per circuit and the bill of quantities, pass-through wires), the stair list (headroom, railing, rebar drawing, winders, cut section, several floors, quantities without drawing, landing edges), curved walls, join on draw, wall objects with `HCWWALLEDIT`, opening types and attributes, multi-move, slide and replace for openings drawn other ways, polyline clean-up and corner, lift sizes, machine room and section, escalators, the balustrade, curved rails, grid add and remove, column options, the live update service, the section sheet number, live datum, colour-mapped deductions, wall numbering by room, the area statement reading layers with exemption rules, a permissible table and more than four floors, per-sheet scale, viewport layers and library fields for the sheet set, and openings tied to a floor.

## What is still open

These are limits of what was built, or items that need something the plugin cannot supply:

- **Everything is untested in a host.** Nothing has been run inside AutoCAD, BricsCAD or ZWCAD. The pure logic (geometry, tables, schedules) has unit tests; the command layer (prompts, entity creation, the live update events, jigs) is checked only by compiling against the AutoCAD .NET 24.3 package. Expect fixes once it meets a real drawing.
- **BricsCAD and ZWCAD** pick up the same source but are not built here (they need the host API DLLs, which are not on public runners). The live service, the slide jig and viewport layer freezing in particular may behave differently there.
- **Bylaw figures.** Nothing here carries a local rule. `AreaExemptRules`, `AreaPermTable`, `LiftTable`, the stair checks, the escalator limits, the breaker and cable tables and the balustrade gap are starting values or inputs. Set them from your own bylaws and design rules.
- **Associative dimensions** are the plugin's own tracking of wall vertices (see the README), not the host's dimension associativity. They do not follow a wall that is erased and redrawn, and dimensions on door, window, grid and column points stay where they are.
- **Wall objects** cover walls drawn by `HCWWALL`. Walls drawn by hand or before this version have no record and cannot be edited with `HCWWALLEDIT`. A wall that a door or window has cut is loose lines, so join on draw does not merge new walls into it.
- **Live updates** act when a command ends, not while you drag, and the area table redraws only on the Model tab.
- **Copy/array.** `INCARRAY` and `INCCOPY` deep-clone the selection once per copy. Cloning entities directly would be lighter but can lose block attributes. Measure on a large drawing before changing it.
- **Single-line (centreline) walls** and mixed drawings are not handled by the auto dimensions: only faces (two lines or a closed outline).
- **Lift and escalator** are drawings from sizes, not a design; a lift pit, machine room and overhead are inputs.
- **Winders** are for L and U stairs. Dog-leg stairs keep landings, and the winder slab is worked along the walkline, not as exact kite areas.
- **Deduction matching.** Index walls by bounding box for very large selections (a box test already rejects most pairs).

## Overlaps still worth merging

Found in an audit of every command; each needs a decision on behaviour before it is merged:

- **Drawing a result in the drawing:** the stair drawing code in `StairCommands` repeats what `GDrawer` does for lifts, escalators and balustrades (layers by role, text, dimensions, hatch); the stair version also tags every object with its ID, which `GDrawer` would need a hook for.
- **Number increment:** `INCARRAY` and `INCCOPY` differ only in how the copies are placed.

## Quality

- Move more pure logic (schedule maths, table layout) into `src/HCW.AutoCAD.Plugin/Logic` and cover it with tests.
- Build the BricsCAD and ZWCAD projects in CI. They need the host API DLLs, which are not on public runners.
