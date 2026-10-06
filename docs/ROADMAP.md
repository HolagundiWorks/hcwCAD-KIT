# Roadmap

What is not built yet. Everything else is described in the [README](../README.md). Nothing here has been run inside a CAD host: the commands compile against the AutoCAD .NET reference package and the CAD-free logic has unit tests, but each host needs its own testing before release.

## What was built from the old list

Every item that was on this roadmap has been built, in the form the README describes: rooms from walls (all rooms, gap bridging, column islands, arcs), openings from gaps, non-rectangular room openings, spacing between dimension chains, associative dimensions (the plugin's own tracking), opening tags and room names in the auto dimensions, the whole electrical list (switch control, per-point ratings, your own kinds, breakers, cables, phases, cable per circuit and the bill of quantities, pass-through wires), the stair list (headroom, railing, rebar drawing, winders, cut section, several floors, quantities without drawing, landing edges), curved walls, join on draw, wall objects with `HCWWALLEDIT`, opening types and attributes, multi-move, slide and replace for openings drawn other ways, polyline clean-up and corner, lift sizes, machine room and section, escalators, the balustrade, curved rails, grid add and remove, column options, the live update service, the section sheet number, live datum, colour-mapped deductions, wall numbering by room, the area statement reading layers with exemption rules, a permissible table and more than four floors, per-sheet scale, viewport layers and library fields for the sheet set, and openings tied to a floor.

## What is still open

These are limits of what was built, or items that need something the plugin cannot supply:

- **Everything is untested in a host.** Nothing has been run inside AutoCAD, BricsCAD or ZWCAD. The pure logic (geometry, tables, schedules) has unit tests; the command layer (prompts, entity creation, the live update events, jigs) is checked only by compiling against the AutoCAD .NET 24.3 package. Expect fixes once it meets a real drawing.
- **BricsCAD and ZWCAD** pick up the same source but are not built here (they need the host API DLLs, which are not on public runners). The live service, the slide jig and viewport layer freezing in particular may behave differently there.
- **Bylaw figures.** Nothing here carries a local rule. `AreaExemptRules`, `AreaPermTable`, `LiftTable`, the stair checks, the escalator limits, the breaker and cable tables and the balustrade gap are starting values or inputs. Set them from your own bylaws and design rules.
- **Associative dimensions** are the plugin's own tracking (wall, column and grid vertices, door and window points), not the host's dimension associativity. They re-tie to a vertex within `AutoDimReanchorMm` when a wall is redrawn, but not to a door or window block that is deleted and inserted again.
- **Wall objects** cover walls drawn by `HCWWALL` and walls adopted with `HCWWALLADOPT` (straight outlines). A joined outline with unusual junctions may not rebuild exactly as drawn, and curved hand-drawn outlines cannot be adopted.
- **Live updates** act when a command ends, not while you drag: the host does not let the drawing be edited from inside a change event, and how grips and undo report changes differs between hosts.
- **Copy/array.** `INCARRAY` and `INCCOPY` deep-clone the selection once per copy. Cloning entities directly would be lighter but can lose block attributes. This is not changed because it has not been measured on a large drawing in a host.
- **Single-line walls** are handled with `AutoDimCentreLineMm` for horizontal and vertical lines; angled centre lines are skipped.
- **Lift and escalator** are drawings from sizes, not a design: the lift pit, machine room and overhead, and the escalator limits, are inputs.
- **Reinforcement** has hooks, alternate cranks and top steel as options (a quantity check, one layer, no design); the drawing shows the main and distribution bars only.
- **Winders** are for L and U stairs and use their true plan areas; dog-leg stairs keep landings.
- **Deduction matching** already indexes walls by bounding box (`BoxIndex`), so a very large selection is not compared pair by pair. Nothing further is planned until a real drawing is slow.

## Quality

- An audit of every command found overlaps; all are now merged behind one command each (see the README table), and the stair, lift, escalator and balustrade drawings share one drawer (`GDrawer`).
- Move more pure logic (schedule maths, table layout) into `src/HCW.AutoCAD.Plugin/Logic` and cover it with tests.
- Build the BricsCAD and ZWCAD projects in CI. They need the host API DLLs, which are not on public runners.
