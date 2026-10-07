# Plan: bridge hcwCAD-KIT with AQC

Written 2026-10-07. **Status: Phase 0 (verify) and Phase 1 (contract) are done; nothing else is built.** The answers to the *to check* items are in [AQC-FINDINGS.md](AQC-FINDINGS.md) (from reading AQC's code; AQC itself was not built or run here), and the contract is in [bridge/](bridge/README.md). Where this plan and the findings differ, the findings are right: units are millimetres, a level's `height_mm` is slab-top to slab-top, beam depth per floor is required (it sets column heights), `Lvl0` is AQC's plinth, and partial project files load. Next is Phase 2, once the questions in section 8 are answered.

## 1. What each side is

**hcwCAD-KIT (this repo, MIT).** An AutoCAD plugin (BricsCAD and ZWCAD builds share the source) that draws and measures inside the drawing: walls as wall objects, doors and windows with standard marks, columns, lintels, rooms, stairs, electrical, a take-off book kept in the drawing (floors, door and window schedule, named take-off tables), and the new Project tab (project details, floors and heights, beam depths). It exports take-offs to CSV and Excel (`MEXPORT`, `MEXPORTX`). It has no link to any estimating program.

**AQC / AQC-Core (HolagundiWorks/AQC, AGPL-3.0 or commercial).** *Verified:* a Windows desktop app (WinUI 3 UI, C++ engine `bbs_engine.dll`, .NET 8) for quantity take-off, estimating and project management. It holds a project in a `.bbsproj` JSON file (format `bbsproj`, version 17). Its take-off today is **from PDF drawings only**; the roadmap says nothing about AutoCAD, DWG or DXF. It already has the pieces a CAD link needs: levels, element rows by trade, opening schedules that deduct from masonry, and link rules that derive plaster and paint from masonry. It also has a separate sync bridge to the AORMS hub (not needed here).

**The gap this plan closes:** AQC measures a PDF by hand; the plugin already knows the exact geometry. The bridge sends what the plugin knows into AQC as take-off rows, and sends project and level data back so both sides use the same floors.

## 2. What AQC expects (verified)

Levels, at `levels[]` in the project file:

```json
{ "id": "Lvl0", "name": "Plinth", "height_mm": 3200, "slab_thickness_mm": 150, "beam_depth_mm": 450 }
```

Element rows are dictionaries with a `mark` and a `level` (the level id), by trade:

| AQC trade (category) | Row fields |
|---|---|
| `masonry` | mark, level, length, height, mortar_mix, deduct_rule |
| `masonry_openings` | wall_mark, level, nos, opening_l, opening_h, opening_kind (Door, Window, Other), takeoff_id |
| `doors` | mark (D1), level, nos, width, height, door_type, frame_size, shutter_thick, shutter_type, wood_finish, wall_mark, takeoff_id, deduct_from_wall, notes |
| `windows` | mark (W1), level, nos, width, height, window_system, track, wood_opening, wood_finish, wall_mark, takeoff_id, deduct_from_wall, notes |
| `columns` | mark, level, width, depth, height, cover, concrete_grade, column_type, bars, steel_grade ... |
| `beams` | mark, level, span, width, depth, cover, concrete_grade, steel_grade |
| `lintels` | mark, level, opening, bearing, width, depth, cover, concrete_grade, steel_grade |
| `slabs` | mark, level, span_x, span_y, thickness, cover, slab_type |
| `flooring` | mark, level, length, breadth, finish_type, surface_kind, tile_size, deduct_rule |
| `plaster`, `painting`, `skirting`, `pcc`, `earthwork`, `waterproofing` ... | derived by AQC link rules, or entered |

Project info (`project`): name, location, client_name, prepared_by_name, prepared_by_role, company_name, address, contact_phone, contact_email, gstin, cin, pan. The database design also has `takeoff_item.source` and `source_mark`, which suit provenance. Marks are generated as prefix + level + number (for example `MW-Lvl0-001`). The 21 take-off profiles use mark prefixes C, B, P, L, S, F, MW, PL, PCC, EW, SSM, FL, PT, WP, DPC, SC, VDF, SK, PR, CP.

*To check:* the units of the element-row fields (metres or millimetres), what `height_mm` of a level means (floor to floor?), how `MasonryWallBuild` takes wall thickness, whether `ProjectStore.LoadFrom` tolerates a partial or foreign file, and what the plinth level (`Lvl0`) is in relation to our Ground floor.

## 3. Principles

1. **Exchange a file, share no code.** AQC is AGPL-3.0 or commercial; this plugin is MIT. A JSON contract keeps the licences apart. Do not copy AQC code into the plugin, and keep the contract text in this repo under MIT.
2. **The plugin sends geometry facts, AQC does the arithmetic.** AQC's engine is the single source of truth for derived quantities and money. The plugin does not compute rebar, rates or derived finishes. It sends lengths, heights, areas, sizes and counts.
3. **No double counting.** AQC derives plaster, paint, skirting and shuttering from masonry and RCC. The plugin sends masonry (with its openings), RCC members and room floor areas, not finishes that AQC derives.
4. **Stable identity.** Every row carries a source and a source mark (the plugin's wall id `W0001`, the block handle for an opening) so a second export updates rows instead of duplicating them.
5. **Millimetres on the wire.** The file states `"units": "mm"` and converts from the drawing units. AQC converts to its own units.
6. **Nothing changes silently.** AQC shows a preview of what an import will add, update and leave alone, and the whole import is one undo step.

## 4. Bridge contract v1 (the file)

`<drawing>.aqcbridge.json`, written by the plugin, read by AQC. Draft:

```json
{
  "format": "hcw-aqc-bridge", "version": 1,
  "source": { "app": "hcwCAD-KIT", "host": "AutoCAD 2022", "drawing": "building permission.dwg", "drawing_id": "<guid kept in the DWG>", "exported": "2026-10-07T10:30:00Z" },
  "units": "mm",
  "project": { "name": "", "client_name": "", "company_name": "", "location": "", "address": "", "pid": "", "site_area_m2": 0, "plot_use": "" },
  "levels": [ { "id": "Lvl1", "name": "Ground", "floor_to_floor_mm": 3150, "ceiling_mm": 3000, "lintel_bottom_mm": 2100, "slab_thickness_mm": 150, "beam_depth_mm": 450 } ],
  "beam_depths_mm": [ 300, 375, 450 ],
  "walls": [ { "ref": "W0001", "level": "Lvl1", "length_mm": 0, "thickness_mm": 230, "height_mm": 0, "layer": "A-WALL", "points": [] } ],
  "openings": [ { "ref": "<handle>", "kind": "Door", "mark": "D2", "type": "single", "level": "Lvl1", "width_mm": 900, "height_mm": 2100, "sill_mm": 0, "wall_ref": "W0001" } ],
  "opening_schedule": [ { "mark": "D2", "kind": "Door", "width_mm": 900, "height_mm": 2100, "sill_mm": 0, "nos": 6, "type": "" } ],
  "columns": [ { "ref": "<handle>", "mark": "C1", "level": "Lvl1", "width_mm": 230, "depth_mm": 450, "height_mm": 3000 } ],
  "lintels": [ { "ref": "<handle>", "mark": "L1", "level": "Lvl1", "opening_mm": 900, "bearing_mm": 150, "width_mm": 230, "depth_mm": 150 } ],
  "rooms": [ { "ref": "<handle>", "name": "KITCHEN", "level": "Lvl1", "area_m2": 0, "perimeter_mm": 0, "length_mm": 0, "breadth_mm": 0 } ],
  "slabs": [ { "ref": "<handle>", "level": "Lvl1", "area_m2": 0, "thickness_mm": 150 } ]
}
```

Each section is optional, so a first import can carry only levels. A JSON Schema file and three fixtures (empty, small house, full plan) go in `docs/bridge/`.

## 5. Mapping

| Plugin | AQC | Notes |
|---|---|---|
| Floors (HCWFLOORS, MeasureBook floors) | `levels[]` | `floor_to_floor_mm` to `height_mm` (to check); `slab_mm` to `slab_thickness_mm`; ceiling and lintel bottom have no AQC field, so they stay in the file for AQC to use later. Level ids: keep a name-to-id map saved in the drawing; propose Ground = `Lvl1` when AQC's `Lvl0` is the plinth (to check). |
| Beam depths (HCWBEAMS list) | `levels[].beam_depth_mm` | AQC has one depth per level; the plugin has a list. Add a per-floor beam depth to the Floors page (kept with the project data), default from `BeamDepthFor` or the first standard depth. |
| Project details | `project` | title to `name`; owner to `client_name`; architect to `prepared_by_name` or `company_name`; address and location as is; PID, site area, plot use travel in the file for AQC to show (no AQC field today). |
| Wall objects (W0001...) | `masonry` | Centre-line length and the wall height from the floor; thickness chooses AQC's brick or wall build (to check). Wall `ref` becomes `source_mark`. |
| Doors and windows in the plan | `masonry_openings` + `doors` / `windows` | Per opening: kind, width, height, level, the wall it is cut in. Standard marks D1 800 ... V1 600 match AQC's D and W marks; the ventilator is sent as a window with mark `V1`. Counts per mark from the opening schedule. |
| Columns (HCWCOLSCHED, column table) | `columns` | Section, height from the levels. |
| Lintels (HCWLINTEL) | `lintels` | Opening width, bearing, depth from the lintel depth rule. |
| Rooms (HCWROOMREPORT outlines) | `flooring` | Area, length and breadth from the outline; room name in notes. |
| Slabs | `slabs` | Needs span x and span y; for non-rectangular outlines send area and let AQC use an area-based row (to check). |
| Plaster, paint, skirting, shuttering | not sent | AQC derives them. |

Reverse direction, AQC to plugin (Phase 4): `levels`, `project`, and AQC beam depths fill the Project tab.

## 6. Phases

### Phase 0: verify (done 2026-10-07, by reading the code; AQC not built here)
Clone AQC, build and run it, open a sample `.bbsproj`. Answer every *to check* above and record the answers in `docs/AQC-FINDINGS.md`. Decide where in AQC an importer plugs in (candidates: `TakeoffPage`, `ProjectStore`, `OpeningScheduleLinker`, the AI assistant's command bus). Confirm whether AQC can load a file the plugin writes directly, as a fallback.
Done when: units, level meaning, wall build, partial-load behaviour and the import hook are written down.

### Phase 1: contract (done 2026-10-07)
Delivered: `docs/bridge/hcw-aqc-bridge.schema.json`, two sample files, `docs/bridge/README.md`, and `BridgeContractTests` (the samples have the right format, millimetres, levels, unique refs, openings that point at walls, and standard marks that match their widths). The contract text in section 4 below is the first draft; the schema file is the current one (levels carry `height_mm`, not `floor_to_floor_mm`; `site_area_m2`; columns have no height because AQC computes it).
Original goal:
Write the JSON Schema, the three fixtures and a short spec in `docs/bridge/`. Freeze v1.
Done when: the fixtures validate against the schema, and the AQC side agrees the field names.

### Phase 2: plugin exporter (about a week)
- CAD-free `Logic/BridgeExport.cs`: turns plain records (levels, walls, openings, columns, lintels, rooms) into the contract and back. Unit tested with the fixtures.
- Command `HCWBRIDGE` (export): reads the drawing, converts to millimetres, writes the file next to the drawing (or to a chosen folder, setting `BridgeFolder`). Reports counts per section and anything skipped (curved walls, openings with no wall).
- Stable ids: a drawing id (GUID) stored with the project data; wall ids and block handles as `ref`.
- Ribbon: a new "AQC Bridge" panel on the Project tab (Export to AQC, Import from AQC, Bridge settings).
- Add the per-floor beam depth to the Floors page.
Done when: a real plan exports a file that validates, with counts that match `HCWOPENSCHED`, `HCWLEVELS`, the column table and `MQTYSUM`.

### Phase 3: AQC importer (in the AQC repo, about a week)
"Import from CAD": reads the file, shows a preview (add, update, unchanged, skipped), merges levels, project info, masonry, openings and the door and window schedule, columns, lintels and room floors as AQC rows with `source` = `hcwCAD-KIT` and `source_mark` = `ref`. One undo step. Re-running updates by `source_mark` and does not touch rows the user edited in AQC (flag them).
Done when: the sample plan imports, AQC's own derivation produces plaster and paint from the imported masonry and openings, and the totals agree with hand checks (Phase 5).

### Phase 4: reverse link (3 to 4 days)
AQC "Export for CAD" writes `levels`, `project` and beam depths; the plugin gets `HCWBRIDGEIMPORT`, which fills the Project tab and the floors after showing what will change. Decide the authority per field (see open questions).

### Phase 5: round trip and checks (3 to 4 days)
Export, import, change the drawing, export again, import again: no duplicates, edits kept, removed items flagged. Hand-check one house plan: wall length and height, opening deductions, column and lintel counts against `MQTYSUM` and AQC's BOQ. Record the differences and fix the largest.

### Phase 6: optional live link
A local named pipe or localhost call so AQC can pull from an open drawing without a file, and AutoCAD can show AQC's rates. Only if the file flow proves too slow to use. The AORMS hub sync is a separate step and not needed for this bridge.

### Phase 7: finish
Docs in both repos, CI that validates the fixtures against the schema, version tag, and a note in `HANDOFF.md`.

## 7. Risks

| Risk | What to do |
|---|---|
| Units or level meaning differ from what I assumed | Phase 0 answers them before any code. |
| AQC cannot take a partial import | Fall back to writing a complete `.bbsproj` with the plugin, or add the importer first. |
| Double counting of finishes | Rule 3: send masonry and RCC, let AQC derive finishes; test with a plan. |
| Walls drawn by hand (not wall objects) have no thickness record | `HCWWALLADOPT` first; the export lists walls it could not read. |
| Curved walls and non-rectangular slabs | Send area and perimeter, mark the row as area-based, flag for review. |
| Licence mixing | File exchange only; no AQC code in this repo; the importer lives in AQC. |
| Neither host nor AQC has been tested end to end | The plugin has never run an export against real AQC; keep Phase 5 as a gate. |

## 8. Questions for the owner

1. **Which direction first?** Plugin to AQC (recommended), or AQC to plugin (levels and project info) first?
2. **Who is the authority for levels and project details?** The drawing, AQC, or "last write wins with a prompt"?
3. **May I change AQC?** The importer has to live in the AQC repo; the plugin side can be done alone but is only useful once AQC can read the file.
4. **Plinth.** Does the plugin's Ground floor become AQC's `Lvl1` with a plinth at `Lvl0`, or should the two use the same names?
5. **Rebar.** AQC's C++ engine owns the bar bending schedules. Confirm the plugin only sends member sizes and never bar data.
6. **Scope of v1.** Levels, project, masonry and openings first (recommended), then columns, lintels and rooms?
