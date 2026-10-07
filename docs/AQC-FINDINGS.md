# AQC findings (Phase 0)

Answers to the "to check" items in [AQC-BRIDGE-PLAN.md](AQC-BRIDGE-PLAN.md), from reading the AQC source (HolagundiWorks/AQC, `main` at `3667630`, cloned to `D:\Work Development\Repos\AQC`) on 2026-10-07. **AQC was not built or run**: this machine has no CMake or MSVC, and the app needs the C++ engine and the WinUI workload. Everything below is from the code, so anything about runtime behaviour is still unconfirmed. File paths are under `BBSDesktop/BBSApp/` in the AQC repo.

## Answers

| Question | Answer | Where |
|---|---|---|
| Units of element rows | **Millimetres** throughout. Masonry `length`, `height`, `thickness`, opening `opening_l`, `opening_h`, door and window `width`, `height`, and every column, beam and lintel dimension are mm. AQC converts to m² and m³ itself (`netMm2 / 1e6`, `Mm3ToM3`). | `Services/CivilBoqCalculator.cs` (`Mm()` is a plain number parse), `Models/ElementSpecs.cs` (all geometry labelled "(mm)") |
| Level `height_mm` | **Slab-top to the next slab-top** ("height = slab-top to next slab-top (mm)"). Clear column height is computed: `height_mm - slab_thickness_mm - beam_depth_mm`. So a level's beam depth directly sets the column height used for columns. | `Services/MaterialsCalculator.cs` (`LevelDef`) |
| Level ids and the plinth | Ids are positional, `Lvl0`, `Lvl1`, ... , renumbered after any add or delete. **`Lvl0` is the plinth** (default height 3200, plinth beams `PB`); `Lvl1` is the first storey above it (default named "First floor"). A project always keeps at least `Lvl0`. Row `level` values are these ids. | `Services/ProjectStore.cs` (`EnsureDefaultLevels`, `RenumberLevels`), `Views/LevelsPage.xaml.cs` |
| Partial file load | **Tolerated.** `LoadFrom` reads every section with a null check (`root["x"] as JsonArray`), a missing key leaves the section empty, and default levels are added when `levels` is absent. A file with only `name`, `project`, `levels` and a few arrays loads. Element rows are free-form string dictionaries; numbers may be strings or numbers. | `Services/ProjectStore.cs` `LoadFrom`, `LoadRows` |
| Wall thickness | A masonry row has `thickness` (mm, default 230), `unit_type` (`Brick`, `ACC Block`, `Cement Block`) and `block_size`. A thickness of **120 mm or less** is measured as a 110 mm wall by area (m²), anything thicker by volume (m³). The catalogue of builds is Brick 230 / 110, ACC 100 / 150 / 200, Cement block 100 / 150 / 200. | `Services/CivilBoqCalculator.cs` `MasonryLines`, `Services/MasonryWallBuild.cs` |
| How openings deduct | A masonry row's `mark` is matched by `wall_mark` to rows in `masonry_openings`, `doors` and `windows` (same level); doors and windows deduct only when `deduct_from_wall` is yes; plaster jambs add `2H + L` per opening. | `Services/CivilBoqCalculator.cs` |
| Where an import should plug in | `JointMeasurementPull` is the template: rows are added to a sheet (`ProjectStore.Current.<Sheet>.Add(row)`), tagged with a `source` (`jointMeasurement:{id}`) and a unique `mark`, **idempotent** (skips marks already present), then `ProjectStore.Current.Notify()`. | `Services/JointMeasurementPull.cs` |
| Marks | `{prefix}-{level}-{nnn}`, for example `MW-Lvl1-001` (prefixes C, B, P, L, S, F, MW, PL, PCC, EW, SSM, FL, PT, WP, DPC, SC, VDF, SK, PR, CP). | `Services/TakeoffModels.cs` `NextMark` |
| Derived rows | Finishes AQC derives carry `source` starting `auto_` (`auto_wall`, `auto_column`, `auto_slab` ...) and are regenerated; a row with another source is left alone. | `Services/FinishSurfacesCalculator.cs` |
| CAD hooks in AQC | **None.** No mention of AutoCAD, DWG, DXF or hcwCAD anywhere. Take-off is from PDF. | repo-wide search |
| Test setup | AQC has C++ engine tests only (`BBSDesktop/tests/test_engine.cpp`); no C# test project. | repo tree |

## What this changes in the plan

1. **Beam depth per floor is required**, not optional: AQC column heights are computed from it. The plugin's Floors page needs a per-floor beam depth, and the bridge sends `beam_depth_mm` for every level.
2. **Plugin floor to AQC level:** plugin floor-to-floor (finished floor to finished floor) is the closest thing to AQC's slab-top to slab-top height; send it as `height_mm` and flag the small difference (finishes). Plugin floors map to `Lvl1` ... `LvlN`; **`Lvl0` (plinth) stays AQC's**, and an import never changes it unless asked.
3. **Match levels by name, then by position**, because AQC ids are positional and change when levels are added.
4. **Rows are millimetres**: no unit conversion on the AQC side; the file states `"units": "mm"` and the plugin converts from the drawing.
5. **Importer shape:** model it on `JointMeasurementPull`, with `source = "hcwcad:{drawing_id}"` and the plugin's `ref` kept in a `source_ref` field, so a second import updates rows instead of duplicating them. Generate marks with AQC's own `NextMark` rule for new rows.
6. **Keep the mapping code out of the WinUI project.** Put the contract reader and the row mapping in a small plain `net8.0` class library with its own xUnit tests (AQC has none for C#), and let `BBSApp` call it from one menu item. That library can be built and tested on a machine without the WinUI workload.
7. **A minimal `.bbsproj` is a valid fallback:** the plugin could write a project file with levels and rows directly. It would only work for a new project, since it would replace an existing one, so it is not the main path.
8. **Wall thickness:** plugin 230 mm maps to Brick 230; the 4.5 in partition (114.3 mm) is at or under the 120 mm line, so AQC measures it as the 110 mm wall. Other thicknesses need a unit type (Brick by default).
9. **Plugin JSON writing:** the plugin targets .NET Framework 4.8, which has no built-in JSON writer; AutoCAD itself bundles its own Newtonsoft.Json, so adding a second JSON library risks a version clash. The exporter should use a small hand-written JSON writer.

## Still open (needs the owner, or running AQC)

- Whether AQC is built and running somewhere I can reach, to open a real `.bbsproj` and confirm the rows look right after an import.
- The six decisions at the end of the plan (direction first, authority for levels and project details, permission to change AQC, plinth mapping, rebar stays in AQC, scope of version 1). The findings support the recommended answer to most of them.
