# hcw-aqc-bridge, version 1

The file hcwCAD-KIT writes for AQC (and reads back, in the other direction). One JSON document per drawing: `<drawing>.aqcbridge.json`.

* The schema is [hcw-aqc-bridge.schema.json](hcw-aqc-bridge.schema.json); two sample files are in [fixtures](fixtures): `levels-only.json` (project details and floors) and `small-house.json` (every section).
* **Millimetres on the wire** (`"units": "mm"`; areas in square metres). The plugin converts from the drawing's units; AQC's element rows are already millimetres.
* **Every section is optional**, so a first import can carry only the project and the levels.
* **Levels** are the floors above the plinth, in order, with `height_mm` (floor to floor), `slab_thickness_mm` and `beam_depth_mm`. AQC computes a column's clear height as `height - slab - beam depth`, so the beam depth matters. AQC matches levels by `name`, then by position, to `Lvl1`, `Lvl2` ...; `Lvl0` (plinth) is AQC's and an import does not change it.
* **Every row has a `ref`** (the plugin's wall id, or a block handle) and the file has a `drawing_id` kept in the drawing. AQC stores them as `source` and `source_ref`, so importing again updates rows and does not duplicate them.
* **The plugin sends measured facts, not derived quantities.** No plaster, paint, skirting, shuttering or rebar: AQC derives the first four from the masonry and RCC rows and owns the bar bending schedules.
* **Standard opening marks:** D1 800, D2 900, D3 1200; W1 600, W2 900, W3 1200, W4 1500, W5 2000; V1 600 (a ventilator is a window). A size not in the table is D4 and up, W6 and up.
* **Wall thickness:** 230 is a 230 mm brick wall; 114.3 (4.5 in) is read by AQC as its 110 mm wall (anything 120 mm or under).
* A change to a field name, a required field or a unit is a new `version`.

Status: draft, written before AQC has an importer. See [../AQC-BRIDGE-PLAN.md](../AQC-BRIDGE-PLAN.md) and [../AQC-FINDINGS.md](../AQC-FINDINGS.md).
