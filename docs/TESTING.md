# Testing in a CAD program

Nothing in the command layer has been run in a CAD program. This is the order to try things in, what to expect, and what to send back. Report each result as: command, what you did, what happened, and the text on the command line. `%APPDATA%\hcwCAD-KIT\diag.log` holds what the ribbon loader and `HCWDIAG` printed.

## 1. Load and ribbon

1. Build (see [HANDOFF.md](HANDOFF.md)), close AutoCAD, open it, `NETLOAD` the DLL (or install the bundle).
2. Run `HCWDIAG`. Expect: plugin path, CAD version, `ribbon: ... ours: hcwCAD-KIT (9 panels), hcwCAD-KIT Settings (5 panels)`, a command count above 250.
3. No tabs? Run `HCWRIBBON` and send the line it prints.

## 2. Walls and openings (one new drawing, millimetres)

| Step | Expect |
|---|---|
| `HCWWALL`, 230, draw a closed room of four walls | Outlines on `A-WALL`, a hatch on `A-WALL-HATCH`, a line on `MEASURE-LINEAR` along each wall |
| `HCWDOOR` 900 on one wall | Gap cut, door block, tag `D2/1` (a 900 door is the standard mark D2; the tag is code/number); no lintel yet (they are made on request); the hatch is cut at the door |
| `HCWWINDOW` 1200 on another wall, then a second window of the same size | Tags `W3/1`, `W3/2` (1200 is the standard mark W3); same code for the same size |
| `HCWLINTEL` > Generate, select the door and a window | A dashed outline through the full wall thickness on `A-LINTEL` and a line on `MEASURE-LINTEL`; the wall is not cut; running it again does not double them |
| `MLINTEL` after the lintels | Marks LT1.. with opening, length, wall, depth (900 mm opening: 152.4; 1500: 228.6; 2400: 304.8), concrete and shuttering; take-off "Lintels" saved |
| `HCWOPENSCHED` | Schedule updated, table drawn where you pick |
| `HCWOPENMOVE` > Move the door | The lintel (if generated), hatch and tag follow |
| `HCWWALLEDIT` change thickness to 115 | Walls rebuilt, openings re-cut, hatch and measurement lines redrawn |
| `HCWWALLHATCH` | Hatch redrawn, message names the layer |

### Regenerate walls from lines

In a new drawing draw on `A-WALL`, with lines only: a 4000 x 3000 rectangle, one partition across the middle that stops 40 mm short of the top wall, and one corner with a 40 mm gap. Run `HCWWALLREGEN`, pick a line on `A-WALL`, choose Auto. Expect: the four outer lines 9 in (228.6) and the partition 4.5 in (114.3); the partition joined to the top wall and the gap corner closed; mitred corners where thicknesses match; a square outside corner where a 9 in and a 4.5 in wall meet; the hatch; the originals on `A-WALL-CL`; a junction report on the command line. Then try Outer and Inner, and the swap option.

### Project tab, standard marks, room text, wall junctions (added by Agent 2, queue item Q-005)

| Step | Expect |
|---|---|
| Open the **hcwCAD-KIT Project** tab; `HCWPROJECT`, fill the details, OK with "Fill the title block" ticked | Title block fields filled (project title, owner, architect, engineer, PID, site area, plot use); `HCWPROJECTTITLE` fills them again |
| `HCWFLOORS`: set 2 floors, heights 3150 / 3000 / 2100 / 150 mm, OK; then `HCWLEVELS` | Two floors listed with the same heights; `MSCHED` Floors tab shows them |
| `HCWBEAMS`: enter `230, 300, 375, 450, 600`, OK; reopen | The depths are kept, sorted |
| `HCWOPENSTD` | The schedule holds D1 800, D2 900, D3 1200, W1 600 ... W5 2000, V1 600 (check in `MSCHED`) |
| `HCWDOOR`, type `D3` at the width prompt | A 1200 door, tagged `D3/1`; a width of 1000 gets a new mark (D4) |
| `ROOMTEXTFIT`: a room rectangle with MTEXT "LIVING ROOM" inside; pick its two corners | The MTEXT becomes TEXT with the spaces removed, fitted inside the rectangle and centred; `ROOMTEXTFITSET` changes the height cap (mm) |
| `HCWWALL`, 230, then a 114 wall that meets it in a T | The two outlines touch and do not overlap; `WallJoinOnDraw` is 0 so each wall stays its own outline; `HCWWALLJOIN` merges them on request |
| `HCWWALLREGEN` on a plan drawn as **face lines** (two lines per wall): choose Faces | One wall per pair of faces, centre lines on `A-WALL-CL`, thickness snapped to 9 in or 4.5 in; the same plan in an inch drawing gives walls the same size (D-002) |

### Beam depth per floor

Run `HCWFLOORS`. The Floors page has a **Beam depth** column in millimetres. With nothing on the Beams page a new floor shows 450; put `300, 450` on the Beams page and add a floor, which should show 300. Type 375 for Ground, press OK, run `HCWFLOORS` again: Ground shows 375. Remove a floor with the count, OK, add it back: it shows the default again (depths of removed floors are dropped).

### Prompts in the drawing's units (Q-004)

In an inch drawing (units set with `UNITS`): run `HCWDOOR`; the prompt says `in`, offers `<41.3386>` and the standards as `D1 31.4961`. Press Enter for the default and place a door: the block should be `HCW_D_900x230`-style in millimetres (the name stays in mm). Type 36 for the width: the opening is 914.4 mm. Do the same for the height and `HCWWINDOW` sill, then `HCWOPENREPLACE`. Repeat once in a foot drawing and once in a millimetre drawing (prompts say `mm`, numbers as before). Also look at `HCWESCALATOR` (rise, landing), `HCWAXISADD` (distance), `ROOMTEXTFITSET` and the gap prompt of `HCWROOMWALLS`.

### AQC bridge export (`HCWBRIDGE`)

Needs a saved drawing with the small test plan from section 2 (walls, a door, two windows, a lintel), `HCWFLOORS` with two floors, a room (`HCWROOMWALLS`) and a column or two (`HCWCOLUMN`). Run `HCWBRIDGE`, answer 1 for the floor. Expect: a message naming `<drawing>.aqcbridge.json` next to the drawing with counts per section; open the file in a text editor: `"units": "mm"`, a `source.drawing_id` GUID, `levels` with `beam_depth_mm` (the value from `HCWFLOORS`), `walls` with the wall ids as `ref`, `openings` with marks `D2/D3/W3`, `wall_ref` set, an `opening_schedule`, `lintels`, `rooms` (each room once). Run it again: same `drawing_id` and the same refs. Answer 0 for the floor: rows repeated with `@Ground` / `@First` on the refs. In an inch or foot drawing the lengths are still millimetres (a 9 in wall shows 228.6). An unsaved drawing gets "save the drawing first".

### AQC bridge import (`HCWBRIDGEIMPORT`)

Use the file `HCWBRIDGE` just wrote, or a copy edited in a text editor (change `slab_thickness_mm` of the first level to 120, the `name` of the project, and add a new level `"Second"` with `height_mm` 3000). Run `HCWBRIDGEIMPORT`, press Enter for the default path. Expect: a list of changes with old and new values (slab 150 -> 120, the project title, `New floor Second: ...`), then "Apply these changes [Yes/No] <No>". Press Enter: "nothing was changed" (check `HCWFLOORS`: unchanged). Run again and answer Yes: `HCWFLOORS` shows the new slab and a Second floor; `HCWPROJECT` shows the new title; walls, doors and everything drawn are untouched; `U` undoes the change. Run it a third time: "the drawing already has these values". Give it a text file that is not JSON, and a file with `"version": 2`: each is refused with a message and nothing changes.

## 3. Levels, sections, columns, stairs

1. `MSCHED`, Floors tab: add Ground (3.15 / 3.0 / 2.1 / 0.15) and First. `HCWLEVELS` lists them.
2. `HCWSECTIONDRAW` across the room: walls, door void with a lintel band, slabs, level text.
3. `HCWAXIS` then `HCWCOLUMN`, `HCWCOLSCHED`, then `HCWCOLQTY` > Edit and Quantities: heights from the levels, a take-off "Columns".
4. `AECSTAIR` single flight: FFL to FFL height offered from the levels, plan and section drawn, quantities saved. `MQTYSUM`: stair and column totals.

## 4. Take-off, rooms, area

1. `TOSTART`, then `MLIN` on the wall lines and the deduction lines in the door blocks: lengths less the opening.
2. `HCWROOMWALLS` inside the room: outline, label, copies on `MEASURE-FLOOR` and `MEASURE-CEILING`; `MFLOOR` reads them.
3. `HCWAREASTMT` with floors from the levels.
4. `ELSCHEDULE` > Room after mapping blocks with `ELBLOCKS` (needs light blocks).

## 5. Speed and other items

- `INCARRAY` on a large selection (a few thousand objects): send the `Timing:` line.
- `SHEETSET` on a real template layout: viewports, scale, layers frozen per sheet.
- `HCWPERMITLAYERS` Report: every tool's output on a `BP-` layer.

## 6. If something throws

Send the full text of the AutoCAD dialog (Details expanded) or the command line, plus `diag.log`.
