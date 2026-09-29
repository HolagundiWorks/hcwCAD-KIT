# hcwCAD-KIT

Holgundi Consulting Works drawing tools for AutoCAD, BricsCAD, and ZWCAD. One set of commands — layers, building-permission sheets, room labels, measure, area fields, and text — loaded from `hcwCAD-KIT.dll`. The ribbon has the same two tabs in each host.

Version 1.0.0. Author: Holgundi Consulting Works.

On load the plugin adds two ribbon tabs and switches to **hcwCAD-KIT**:

| Tab | Use it for |
|---|---|
| **hcwCAD-KIT** | Drawing and take-off: layers, building-permission setup, room labels, MEASURE, area and text |
| **hcwCAD-KIT Settings** | Checks, resets, reports, room-tool options, text styles |

Every button sends its command as if it were typed (`_COMMAND`). You can type any command at the command line without using the ribbon.

## Requirements

| Host | Versions | Plugin runtime |
|---|---|---|
| AutoCAD | 2021–2024 (Autoloader R24.0–R24.3) | .NET Framework 4.8 |
| BricsCAD | V26 | .NET 8 |
| ZWCAD | 2024–2026 | .NET Framework 4.8 |

Windows 64-bit. The host’s API DLLs are referenced at build time and are not copied into the plugin.

## Install

**Try it in the current session.** Type `NETLOAD` and pick the `hcwCAD-KIT.dll` built for that host. The **hcwCAD-KIT** and **hcwCAD-KIT Settings** tabs appear. Loading the DLL a second time does not add a second copy of the tabs. Do not NETLOAD the AutoCAD DLL into BricsCAD or ZWCAD; each host needs its own build.

**Load it every time the host starts**

1. Copy `hcwCAD-KIT.dll` into the bundle’s `Contents` folder.
2. Copy the bundle folder to the host’s ApplicationPlugins directory.
3. Restart the host. `PackageContents.xml` sets `LoadOnAutoCADStartup="true"`.

| Host | Bundle | ApplicationPlugins folder |
|---|---|---|
| AutoCAD | `src/HCW.AutoCAD.Plugin.bundle` | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| BricsCAD | `src/HCW.BricsCAD.Plugin.bundle` | `%APPDATA%\Bricsys\ApplicationPlugins\` |
| ZWCAD | `src/HCW.ZWCAD.Plugin.bundle` | `%APPDATA%\ZWSOFT\ApplicationPlugins\` |

## Build

From the `HCW-AutoCAD-Plugin` folder. `dotnet build` is the command that works with the .NET SDK on this project (the Visual Studio Build Tools MSBuild does not resolve `Microsoft.NET.Sdk`).

AutoCAD:

```
dotnet build src\HCW.AutoCAD.Plugin\HCW.AutoCAD.Plugin.csproj -c Release -p:Platform=x64 -p:AutoCADInstallDir="C:\Program Files\Autodesk\AutoCAD 2024\"
```

If you omit `AutoCADInstallDir`, the project uses `AUTOCAD_INSTALL_DIR`, then `C:\Program Files\Autodesk\AutoCAD 2024\`. Output: `src\HCW.AutoCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`.

BricsCAD V26 (this machine builds against `C:\Program Files\Bricsys\BricsCAD V26 en_US\`):

```
dotnet build src\HCW.BricsCAD.Plugin\HCW.BricsCAD.Plugin.csproj -c Release -p:Platform=x64
```

Override the install with `-p:BricscadInstallDir="..."`. Output: `src\HCW.BricsCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`. The BricsCAD project compiles the same command source, with BricsCAD and Teigha namespaces substituted at build time.

ZWCAD, when it is installed:

```
dotnet build src\HCW.ZWCAD.Plugin\HCW.ZWCAD.Plugin.csproj -c Release -p:Platform=x64 -p:ZwcadInstallDir="C:\Program Files\ZWSOFT\ZWCAD 2026\"
```

The default path is `C:\Program Files\ZWSOFT\ZWCAD 2026\`, or the `ZWCAD_INSTALL_DIR` environment variable. The project expects `ZwManaged.dll` and `ZwDatabaseMgd.dll` in that folder, and `ZwWindows.dll` when the ribbon API is shipped as its own file. ZWCAD is not part of the default solution build, so a machine without ZWCAD can still build AutoCAD and BricsCAD. Output: `src\HCW.ZWCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`.

Building `src\HCW.AutoCAD.Plugin.sln` builds the AutoCAD and BricsCAD projects.

Icons are IBM Carbon Design System PNGs (Apache-2.0), embedded in the DLL. The command-to-icon map is `src/HCW.AutoCAD.Plugin/Resources/icon_map.json`. No separate icon files need to be shipped. Build folders (`bin`, `obj`) are not part of the repository.

## Drawing units

Several tools read the drawing’s insertion units (`INSUNITS`, the UNITS command).

| Tool | How units work |
|---|---|
| `ROOM`, `ROOMC`, `RAREA` | Read `INSUNITS`. Millimetres, centimetres, metres, feet, and inches are scaled. If units are unset, the command asks millimetres or metres and stores that on the drawing. Areas are square metres, or square feet when the drawing is in feet or inches. |
| `MSETUP` (MEASURE) | Chosen once per AutoCAD session, stored in memory only. Metric: 1 unit = 1 metre, lengths round to 1 cm. Imperial: 1 unit = 1 inch, lengths round to 1/8 in. This choice is independent of `INSUNITS`. |
| `POLYAREA` | Converts from `INSUNITS` into square metres or square feet. |
| `HCWSTYLES` | Sizes text from a 2.5 mm plotted height at a typical sheet scale, converted into the drawing’s units. |
| `BPLTSTART` | Sets the drawing to metres, decimal length units, 3 decimal places. |

Room-label text height, floor prefix, and the session log live in memory. They reset when AutoCAD closes or when you run the matching reset command. They are not saved in the DWG.

---

## hcwCAD-KIT

### Layers

The **Layers** panel has one dropdown and one **Create Layers** button. Choose the set, then click the button. Typed commands do the same job:

| Command | What it does |
|---|---|
| `HCWLAYERS` | Creates or verifies the 36-layer HCW Layer Standard v4.0 (colour, linetype, lineweight). Existing layers are left in place; missing ones are added. |
| `VHLAYERS` | Creates the legacy underscore layer set (`A_WALL_CUT`, `S_COLUMN`, and so on) and makes `A_WALL_CUT` current. Use one layer standard per drawing. |
| `BPLTLAYERS` | Creates the BP- drafting layers and the AP- marking layers, without changing drawing units. |

### Building Permission (BPLT)

Used for BBMP / AutoPlan submission drawings. `BPLTSTART` creates every BP- and AP- layer, then sets metres, decimal units, and default dimension text and arrow size. `TITLEBLOCK` (`BPLTTITLEBLOCK`) inserts the A3 building-permit sheet: project title, drawing title, drawing number, size, plot use, owner, architect, PID, site area, area statement, F.A.R., ground cover, and the stability certificate. `TITLEFIELDS` edits those boxes. `TITLENOTES` opens the saved note sets (Construction, Electric, Stability, General, and any you add). Place Notes drops the set chosen in the dropdown; move it with the MOVE command. `TITLENOTESAVE` stores the selected notes under a name.

`BPLTCOPY` (small button) copies selected objects that sit on a `BP-…` layer onto `AP-` plus the rest of that name. Objects that are not on a BP- layer, or whose matching AP- layer does not exist, are skipped. Names are not always a pair: `BP-SITE-BOUNDARY` looks for `AP-SITE-BOUNDARY`, which is not in the standard list, so that copy is skipped.

### Room labels

The ribbon uses one panel. It does not ask you to pick Metric, Feet, or Inches — the label follows the drawing’s units. `ROOM` opens a list of the 29 room types. `ROOMC` asks you to type a name. (`HCWROOM` and `HCWCUSTOMROOM` are the same commands.) Both then:

1. Create layers `ROOM-LABELS` (green) and `ROOM-RECT` (grey) if needed.
2. Ask for the first corner and the opposite corner.
3. Use the current text height (about 125 mm in real size, converted to drawing units). Change it with **Text Height** on the same panel (`HCWROOMTH`). The command does not ask every time.
4. Draw a closed rectangle on the corners you picked (unless rectangle drawing is off) and three centred text lines: room name, width × height, and `Area: …`.
5. Remember the room in a session log used by the schedule and total commands.

Text height, floor prefix, and the rectangle toggle are on the Room Labels panel. `RAREA` labels the area of a selected polyline at its centre. `RTAG` replaces the text of one existing TEXT object. The new name is upper case, and includes the floor prefix when one is set.

The old per-unit commands (`MBR`, `FBR`, `IBR`, and the rest) are removed. Use `ROOM`.

### Measure (manual take-off)

The Measure panel has one dropdown (Linear, Brickwork, Beams and lintels) and one **Measure** button, plus Columns, Area, and Slab. Run `MSETUP` on that panel first so lengths and areas use the unit system you intend. Each take-off creates its layers if they are missing, isolates those layers while you select, then restores the layers it turned off. It labels the geometry, prints a summary, writes a CSV next to the drawing (or in the temp folder if the drawing has no path), and draws a summary table on `MEASURE-TABLE`.

Draw the geometry on the layers below before you run the command. Deduction geometry must be close to the line it belongs to (you are asked for a match tolerance; default 0.01 drawing units).

| Command | Draw on | Result |
|---|---|---|
| `MLIN` | `MEASURE-LINEAR` and deductions on `MEASURE-DEDUCT` | Gross, deduction, and net length. Labels `L1`, `L1-D1`, … |
| `MBRK` | `MEASURE-FULLBRICK`, `MEASURE-HALFBRICK`, deductions on `MEASURE-DEDUCT` | Same, split by full brick and half brick (`FB`, `HB`) |
| `MBML` | `MEASURE-BEAM`, `MEASURE-LINTEL`, deductions on `MEASURE-DEDUCT` | Same, split by beam and lintel (`BM`, `LT`) |
| `MREC` | Closed 4-sided polylines on `MEASURE-COUNT` | Numbered rectangles `R1`… with length, breadth, area, perimeter |
| `MAREA` (`MARE` still works) | Closed polylines, circles, ellipses, or splines on `MEASURE-AREA` | Numbered areas `A1`… with area and perimeter |
| `MSLAB` (`MSLB` still works) | Slab outlines on `MEASURE-SLAB`, openings on `MEASURE-SLAB-DEDUCT` | Each opening is deducted from the slab whose outline contains the opening’s centroid |

A deduction that does not match a parent is labelled `D? NO LINE` and is not included in the total. Labels go on `MEASURE-LABELS`. `MCLEAR` erases objects on `MEASURE-LABELS` and `MEASURE-TABLE` only. It does not erase the measured lines.

### Area and text

| Command | What it does |
|---|---|
| `POLYAREA` | Reads `INSUNITS`, asks for square metres or square feet, an optional number prefix, and a text height. Select polylines. Open ones are closed. Each gets a number on the current layer. You then pick a point for a running-total table on layer `AREA_TABLE`. |
| `DELETEAREATEXT` | Deletes TEXT and MTEXT whose content starts with `Area:` followed by more text. Choose All (the whole drawing) or Selection. This matches room-tool and similar area notes. A note you typed that starts with `Area:` is deleted too. |
| `INCARRAY` (`TextIncrement` still works) | Asks for an increment, a selection (anything except viewports), a base point, a spacing vector, and an end point. Copies the selection along that vector. Every number in text, MText, block attributes, attribute definitions, multileaders and dimension overrides is increased by the increment on each copy. Leading zeros stay only when the original number had them (`01` becomes `02`). |
| `RENUMBERLAYOUTS` (`RL` still works) | Renumbers paper-space layouts in tab order. You set a prefix, an optional suffix, the starting number and how many digits to pad (`2` makes `01`). Tick the layouts to include. Names that would clash with a layout you left out are skipped. |
| `WinLabel` | Select window blocks. For each dynamic block that has a `WNAME` property, writes the first two characters of that value on layer `WIN_LABELS`, stacked down from the point you pick. Set the height first with `WinLabelHeight` (default 0.15). |
| `TXTALIGN` | Select TEXT, choose Left, Right, CentreX, Top, Middle, or Bottom, then a reference point. Left / Right / CentreX share the reference X. Top / Middle / Bottom share the reference Y. |
| `TXTDUP` | Finds TEXT with the same content and the same position (to 0.001). Asks whether to delete the extras. |
| `DBCOUNT` | Counts blocks in the current layout. Press Enter to count every block, or select a few. Dynamic blocks are split by visibility state. You can write the same list to a TXT or CSV file beside the drawing. |
| `DGRID` | Asks for rows and columns, a base point, and the opposite corner. Draws the grid on the current layer, aligned to the current UCS. |
| `AUTOLABEL` | Numbers one attribute tag on matching blocks (and multileader blocks) in the current layout. You set the block name, tag, prefix, suffix, start number, and digits. `*` is a wildcard. Run it again after you copy or erase blocks. It does not renumber on its own in the background. |
| `AREAFIELD` (`A2F`) | Select closed shapes, a hatch, or a region. Pick a point for an MText field, or pick inside a table cell. The field stays linked to the geometry. Millimetre drawings show square metres; inch drawings show square feet. |
| `AREALABEL` (`AT`) | Builds a numbered area list. Pick inside a closed area, or switch to Object and select a closed shape. Each pick drops a number and adds a live row to an AutoCAD table. Undo removes the last one. Choose File instead of Table to write CSV or text. |

---

## hcwCAD-KIT Settings

### Layer maintenance

These commands work on the HCW v4.0 names (`AN-`, `A-`, `I-`, `E-`, `P-`, `S-`, `PR-`), except where noted.

| Command | What it does |
|---|---|
| `HCWRESET` | Puts every existing standard layer back to its colour, linetype, and lineweight, and turns it on, thaws it, and unlocks it. |
| `HCWPURGE` | Runs the host `PURGE` All, twice, so nested empty blocks can be removed. |
| `HCWAUDIT` | Lists any of the 36 standard layers that are missing. |
| `HCWINFO` | Prints every standard layer with ACI colour, linetype, lineweight, and description. |
| `HCWLOCK` / `HCWUNLOCK` | Locks or unlocks the layers of the objects you select. |
| `HCWAUDIT2` | Counts objects in the current space whose colour or linetype is not ByLayer. |
| `HCWBYBLOCK` | Sets the selected objects’ colour and linetype back to ByLayer. |
| `HCWMOVE` | Moves the selection onto a layer that already exists. |
| `HCWSCHEDULE` | Prints how many objects sit on each layer in the current space. |
| `HCWLAYERSTATE` | Save or Restore a named layer state (on/off, freeze, lock, plot, colour, linetype, lineweight, and related flags). |
| `HCWLEGEND` | At a picked top-left point, draws a swatch and a name/description line for every standard layer. Text goes on `AN-TEXT`. |

### Room tool settings

These buttons drive the same auto-unit engine as the **hcwCAD-KIT** room panel. Typed `MTH`, `FTH`, `ITH`, and the other prefixed commands change only that prefix’s engine.

| Command | What it does |
|---|---|
| `HCWROOMTH` | Sets label text height, in the drawing’s length unit. |
| `HCWROOMSET` | Prints text height, whether rectangles are drawn, label layer, rectangle layer, floor prefix, and the detected unit. |
| `HCWROOMRESET` | Restores defaults: rectangles on, layers `ROOM-LABELS` / `ROOM-RECT`, no floor prefix, default text height. |
| `HCWROOMRECT` | Toggles the bounding rectangle on or off for the next labels. |
| `HCWROOMHIDERECT` | Freezes or thaws `ROOM-RECT`. |
| `HCWROOMFLOOR` | Sets a prefix such as `GF` or `FF`. Press Enter with an empty string to clear it. New labels become `GF BEDROOM`. |
| `HCWROOMAUDIT` | Counts TEXT on the label layer and objects on the rectangle layer. Three text objects are expected per rectangle. |
| `HCWROOMCHECK` | Reports whether rectangle-layer polylines are closed. |
| `HCWROOMSCHEDULE` | Writes `RoomSchedule-<unit>.csv` beside the drawing from rooms labelled **in this session**. Restarting AutoCAD clears that log. |
| `HCWROOMTOTAL` | Sums session-log areas. You can filter by a fragment of the room name (`BEDROOM` matches `GF BEDROOM`). |
| `HCWROOMHELP` | Prints the detected unit and reminds you that UNITS is how you change it. |

`HCWROOMLAYER` is registered and can be typed. It is not on the ribbon. It changes the label layer name for later labels.

### Measure settings

| Command | What it does |
|---|---|
| `MSETUP` | Metric or Imperial for the rest of the session, and creates the MEASURE layers. |
| `MSHOW` | Turns back on only layers that a Measure command hid and did not restore. Layers you turned off yourself stay off. |
| `MCLEAR` | Erases MEASURE label and table objects. |

### BPLT reports

| Command | What it does |
|---|---|
| `BPLTCHECK` | For each AP- layer, reports OK (has a closed polyline), EMPTY, NOT CLOSED, or MISSING LAYER. |
| `BPLTAREA` | Sums closed polyline area on each AP- layer and prints it. The heading says square metres; the number is the raw drawing-unit area, so the drawing should already be in metres (`BPLTSTART` sets that). |
| `BPLTREPORT` | Writes `BPLT_Report.csv` beside the drawing: layer name, colour, linetype, lineweight, and whether the layer exists. |

### Text settings

| Command | What it does |
|---|---|
| `HCWSTYLES` | Creates or updates text styles and matching dimension styles `HCW-SITE` (1:200), `HCW-WORKING` (1:50), and `HCW-DETAIL` (1:10). Text height is 2.5 mm on paper times that scale, in the drawing’s units. Font file is `romans.shx`. Dimension scale is 1 (annotative scaling is not applied; set the viewport scale to match the tier). |
| `WinLabelHeight` | Text height used by the next `WinLabel`. |
| `FIXTXT` | Select TEXT/MTEXT. Sets them all to the tallest height in the selection and pushes them apart vertically so centre-to-centre spacing is at least 1.5 × that height. |
| `FIXTXTH` | Same, pushed apart horizontally. |
| `TXTAUDIT` | Lists TEXT/MTEXT whose colour is not ByLayer, counted by layer. |
| `TXTEXPORT` | Writes `TextExport.csv` (layer, type, position, height, style, content) beside the drawing. |
| `TXTSTYLE` | Lists text styles and sets the current style by name or by the printed index. |

---

## Room names

`ROOM` lists these types. `ROOMC` is any other name.

Bedroom, Master Bedroom, Guest Room, Living Room, Dining Room, Kitchen, Pantry, Bathroom, Attached Toilet, Common Toilet, Study Room, Office, Store Room, Pooja Room, Balcony, Terrace, Staircase, Corridor, Entrance, Lobby, Utility, Laundry, Garage, Garden, Courtyard, Wardrobe, Dressing Room, Home Theater, Gym.

Labels go on `ROOM-LABELS`. Rectangles go on `ROOM-RECT`. The default text height is about 125 mm at real size, converted into the drawing’s units.

---

## Layer standards (short)

**HCW v4.0** — 36 layers in seven groups:

| Prefix | Contents |
|---|---|
| `AN-` | Annotation: reference, grid, symbols, text, dimensions, hatch |
| `A-` | Architecture: walls, demo, columns, doors, windows, glazing, stairs, rail, roof, floor, ceiling, details |
| `I-` | Interiors: furniture, joinery, finishes, decoration |
| `E-` | Electrical: ceiling fittings, points, wiring |
| `P-` | Plumbing: fixtures, pipes |
| `S-` | Site: boundary, road, landscape, trees, levels, drainage, fence |
| `PR-` | Presentation: tone, revision clouds |

`HCWINFO` prints the full colour, linetype, lineweight, and description list. The data lives in `LayerData.Hcw`.

**VHLAYERS** — older names with underscores (`A_WALL_CUT`, `S_COLUMN`, `E_LIGHT`, `P_WATER_SUPPLY`, `AN_TEXT`, `X_GUIDE`, …). Guide layers `X_*` are created non-plotting. Do not mix this set with HCW v4.0 in the same drawing if both are meant to be the office standard.

**BPLT** — `BP-` layers for the drawing (site, building, parking, services, text, title block) and `AP-` layers for AutoPlan closed-polyline marks (site, building area, rooms, stairs, parking, and similar). `BPLTREPORT` lists every name and whether it exists.

---

## Project layout

```
build/Rewrite-CadUsings.ps1       BricsCAD and ZWCAD namespace substitution
src/
  HCW.AutoCAD.Plugin.sln           AutoCAD + BricsCAD; ZWCAD is loaded but not built by default
  HCW.AutoCAD.Plugin.bundle/
  HCW.BricsCAD.Plugin.bundle/
  HCW.ZWCAD.Plugin.bundle/
  HCW.AutoCAD.Plugin/              command source, shared by all three hosts
  HCW.BricsCAD.Plugin/             .NET 8 project, references BrxMgd.dll and TD_Mgd.dll
  HCW.ZWCAD.Plugin/                .NET Framework 4.8 project, references ZwManaged.dll
```

`AssemblyInfo.cs` lists every command class the host should register, and points startup at `HcwRibbonApplication`. The assembly file name is `hcwCAD-KIT.dll` for every host. Command names are the same. Saved note sets live in `%AppData%\hcwCAD-KIT\title-notes.txt` and are shared across hosts.
