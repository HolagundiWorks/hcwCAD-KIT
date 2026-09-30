# hcwCAD-KIT roadmap: optimisations, enhancements, and auto-dimension design

Status: mostly implemented (see the table).

| Item | Status |
|---|---|
| Door and window blocks with a deduction line, mapped by block name | Implemented |
| Label index instead of a full scan per deduction (`MPAINT`); bounding-box reject before the distance test | Implemented |
| Take-offs saved in the drawing by name; `MEXPORT` after a restart | Implemented |
| Excel export (`.xlsx`, one sheet per take-off), rates and a Bill sheet | Implemented |
| Sill height and lintel − sill check | Implemented |
| One schedule table per group, with totals | Implemented |
| Wall numbering: left to right, top to bottom, or along a path | Implemented |
| Settings file (`settings.ini`) | Implemented |
| Room log saved in the drawing, room table | Implemented |
| Sheet set (`SHEETSET`) | Implemented |
| `AUTODIM` v1 (axis-aligned plans, outside chains) | Implemented |
| Unit tests (`tests/HCW.Logic.Tests`), CI, compile check against AutoCAD.NET | Implemented |
| Copy/array: clone once | Not done: the cost is per placement either way, and cloning entities directly can lose block attributes. Needs measuring in a large drawing first |
| Colouring mapped deduction lines | Not done: lines inside a block cannot be recoloured per insert |
| One-step undo | Nothing to do: each command is already one undo step |
| `AUTODIM` interior room dimensions, angled walls, collision handling, associative dimensions | Not done (see section 3, order of work) |

Everything marked Implemented compiles against the AutoCAD .NET reference package and the logic has unit tests, but the commands have not been run inside AutoCAD, BricsCAD or ZWCAD. Test them in each host before release.

## 1. Optimisations (code)

| Area | Finding | Change |
|---|---|---|
| Take-off | `OpeningArea` walks the whole space for every deduction to find the nearest label (O(deductions × entities)). | Build one list of deduction labels (position, raw name, length) per run and look up in it. Better: store the opening size on the deduction label when it is mapped, and read that. |
| Take-off | Deduction-to-wall matching is O(deductions × walls) with three `GetClosestPointTo` calls each. | Sort walls into a grid or bounding-box index; test only walls whose extents overlap the deduction. |
| Take-off | Layer isolate/restore, table drawing and CSV export are repeated in each `Run*` method. | One `TakeOff` helper that takes layers, grouping function and row builder. `RunTypedLinear` is about 130 lines and is the template. |
| Take-off | Take-off results live in memory (`MEXPORT`, session log). | Save the last result in the drawing (same Xrecord dictionary as the schedule) so export and re-insert survive a restart. |
| Take-off | Wall numbering (`FB01`) is by X then Y. | Offer numbering along a picked path or by room, so numbers follow the plan. |
| Copy/array | `PlaceCopies` reopens and deep-clones per placement. | Clone once, then transform a copy per placement. |
| Cross-host | BricsCAD and ZWCAD builds rewrite the AutoCAD sources at build time. | Add a CI job that builds all three so a host-only break shows up on the pull request. |
| Tests | No automated tests. | Move pure logic (`MeasureBook`, number increment, name matching, tolerance suggestion) into a class library with no CAD reference and unit-test it. |

## 2. Enhancements (features)

1. **Deduction map**: after Apply, colour each mapped deduction line by its schedule name and list unmapped ones.
2. **Schedule**: one table per group (floors, doors, windows, columns) with its own header, plus a total count per type and size. Optional area column for doors and windows.
3. **Lintel and sill**: sill height for windows, and a head-height check (`lintel bottom − sill = window height`).
4. **Take-off to Excel**: `.xlsx` with one sheet per take-off, instead of many CSV files.
5. **Rates and cost**: an optional rate column per group, with totals, for a bill of quantities.
6. **Room schedule**: persist room labels in the drawing (today they are lost when the host closes) and write the room table on the sheet.
7. **Sheet set**: create layouts from a template, place viewports at a chosen scale, and fill the title block from the sheet fields library.
8. **Settings file**: one JSON in `%APPDATA%\hcwCAD-KIT` for text heights, tolerances, layer names, and default types.
9. **Undo**: wrap every command in one undo group so a single `U` reverses a whole take-off.

## 3. Auto-dimension for working drawings

### What a working drawing needs

Chains of dimensions, in the order a site engineer reads them: overall, then grid or wall-to-wall, then openings, then details. Each chain sits outside the plan, on a fixed offset, and is repeated per side.

### Inputs

- A selection of the plan geometry: wall lines or polylines, openings, columns, and grid lines.
- Layers decide meaning: `A-WALL`, `A-DOOR`, `A-WIND`, `S-COLUMN`, `A-GRID` from the HCW layer standard. Measure layers (`MEASURE-*`) are also read.
- The scale and a dimension style (`HCW-WORKING` already exists from `HCWSTYLES`).

### Algorithm

1. **Collect points of interest per side.** For each side (top, bottom, left, right), project the plan onto that side's axis and gather:
   - wall ends and corners,
   - opening jambs (from door and window blocks or from the deduction lines),
   - column faces,
   - grid line positions.
2. **Snap and merge.** Sort along the axis. Merge points closer than a tolerance (5 mm) so a corner drawn twice yields one point.
3. **Build the chains.** Three levels per side:
   - Level 1, **overall**: first point to last point.
   - Level 2, **wall-to-wall or grid-to-grid**: one dimension between consecutive structural points.
   - Level 3, **openings**: jamb to jamb, with the opening width, and wall segments between openings.
4. **Place them.** Offset each level outward from the outermost geometry by a fixed step (level 1 furthest): `offset₁ = gap + 3·step`, `offset₂ = gap + 2·step`, `offset₃ = gap + step`, where `step` is about 8–10 mm on the plotted sheet, converted to drawing units by the scale.
5. **Skip what is unwanted.** Drop dimensions shorter than a minimum length (about 3 mm plotted), and drop a chain that would only repeat the level above it (for example a single wall with no openings).
6. **Create.** Aligned `AlignedDimension` objects with the working dimension style, on layer `AN-DIM`, associated to the geometry when the host supports it. Group each chain so it can be moved as one.
7. **Report.** Print counts per side and level, and list points skipped because they were too close.

### Interior dimensions

Do the outside first. For interiors, use rooms (from `ROOM` labels or closed polylines): dimension each room's clear width and depth once, on the axis that is not already covered, placed just inside the walls.

### Edge cases to decide

| Case | Proposed behaviour |
|---|---|
| Non-orthogonal walls | Dimension along the wall direction (aligned), offset perpendicular. A separate option for angled buildings. |
| Curved walls | Skip and list them; add radius dimensions as an option. |
| Wall thickness | Dimension to the wall face when both faces are drawn, to the centreline when only one is. |
| Overlapping chains | Increase the level offset by one step and re-run collision on the dimension text boxes. |
| Multi-storey files | Run per layout or per selection; never mix floors in one chain. |

### Command shape

`AUTODIM`: select plan geometry, choose Sides (All, Top, Bottom, Left, Right), Levels (Overall, Grid, Openings, or All), and scale. Then it creates the chains and a summary. `AUTODIMCLEAR` removes what `AUTODIM` made (tagged with the plugin's XData so hand-made dimensions are never touched).

### Suggested order of work

1. Orthogonal plans only: wall ends and openings, outside chains at levels 1 and 3.
2. Add grid and columns, and level 2.
3. Interior room dimensions.
4. Collision handling and non-orthogonal walls.

### Questions to settle

- ~~Are openings drawn as blocks, as gaps in the wall lines, or measured with the deduction lines?~~ Answered: dynamic door and window blocks with a line on `MEASURE-DEDUCT`. `AUTODIM` should use the same lines for jamb points, and the block name for opening labels.
- Do you dimension to wall faces or centrelines by default?
- What plotted scales do you use most (1:50, 1:100)?
- Should dimensions be associative, so a moved wall updates them?
