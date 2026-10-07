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
| `HCWDOOR` 900 on one wall | Gap cut, door block, tag `D1`; no lintel yet (they are made on request); the hatch is cut at the door |
| `HCWWINDOW` 1200 on another wall, then a second window of the same size | Tags `W1/1`, `W1/2`; same code for the same size |
| `HCWLINTEL` > Generate, select the door and a window | A dashed outline through the full wall thickness on `A-LINTEL` and a line on `MEASURE-LINTEL`; the wall is not cut; running it again does not double them |
| `MLINTEL` after the lintels | Marks LT1.. with opening, length, wall, depth (900 mm opening: 152.4; 1500: 228.6; 2400: 304.8), concrete and shuttering; take-off "Lintels" saved |
| `HCWOPENSCHED` | Schedule updated, table drawn where you pick |
| `HCWOPENMOVE` > Move the door | The lintel (if generated), hatch and tag follow |
| `HCWWALLEDIT` change thickness to 115 | Walls rebuilt, openings re-cut, hatch and measurement lines redrawn |
| `HCWWALLHATCH` | Hatch redrawn, message names the layer |

### Regenerate walls from lines

In a new drawing draw on `A-WALL`, with lines only: a 4000 x 3000 rectangle, one partition across the middle that stops 40 mm short of the top wall, and one corner with a 40 mm gap. Run `HCWWALLREGEN`, pick a line on `A-WALL`, choose Auto. Expect: the four outer lines 9 in (228.6) and the partition 4.5 in (114.3); the partition joined to the top wall and the gap corner closed; mitred corners where thicknesses match; a square outside corner where a 9 in and a 4.5 in wall meet; the hatch; the originals on `A-WALL-CL`; a junction report on the command line. Then try Outer and Inner, and the swap option.

### Beam depth per floor

Run `HCWFLOORS`. The Floors page has a **Beam depth** column in millimetres. With nothing on the Beams page a new floor shows 450; put `300, 450` on the Beams page and add a floor, which should show 300. Type 375 for Ground, press OK, run `HCWFLOORS` again: Ground shows 375. Remove a floor with the count, OK, add it back: it shows the default again (depths of removed floors are dropped).

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
