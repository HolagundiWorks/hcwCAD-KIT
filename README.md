# hcwCAD-KIT

Holgundi Consulting Works plugin for AutoCAD 2021–2024. It is a C# / .NET Framework 4.8 assembly (`hcwCAD-KIT.dll`) that loads a ribbon. Command names (`HCWLAYERS`, `MROOM`, `MLIN`, and the rest) are unchanged, so typed commands, scripts, and keyboard shortcuts keep working.

Version 1.0.0. Author: Holgundi Consulting Works.

On load the plugin adds two ribbon tabs and switches to **hcwCAD-KIT**:

| Tab | Use it for |
|---|---|
| **hcwCAD-KIT** | Drawing and take-off: layers, building-permission setup, room labels, MEASURE, area and text |
| **hcwCAD-KIT Settings** | Checks, resets, reports, room-tool options, text styles |

Every button sends its command as if it were typed (`_COMMAND`). You can type any command at the command line without using the ribbon.

## Requirements

- Windows, 64-bit AutoCAD **2021, 2022, 2023, or 2024** (Autoloader series R24.0–R24.3).
- .NET Framework 4.8 (included with those AutoCAD versions).
- To build: Visual Studio 2022 or MSBuild, plus a local AutoCAD install. The AutoCAD API DLLs are referenced only; they are not copied into the plugin output.

## Install

**Try it in the current session**

1. Build, or take a built `hcwCAD-KIT.dll`.
2. In AutoCAD type `NETLOAD` and pick that DLL.
3. The **hcwCAD-KIT** and **hcwCAD-KIT Settings** tabs appear. Loading the DLL a second time does not add a second copy of the tabs.

**Load it every time AutoCAD starts**

1. Create `HCW.AutoCAD.Plugin.bundle/Contents/` if it is not there.
2. Copy `hcwCAD-KIT.dll` into that `Contents` folder.
3. Copy the whole `HCW.AutoCAD.Plugin.bundle` folder to:

   `%APPDATA%\Autodesk\ApplicationPlugins\`

4. Restart AutoCAD. `PackageContents.xml` sets `LoadOnAutoCADStartup="true"`.

## Build

From a Developer Command Prompt, in the `HCW-AutoCAD-Plugin` folder:

```
msbuild src\HCW.AutoCAD.Plugin.sln /p:Configuration=Release /p:AutoCADInstallDir="C:\Program Files\Autodesk\AutoCAD 2024\"
```

If you omit `AutoCADInstallDir`, the project uses the `AUTOCAD_INSTALL_DIR` environment variable, then `C:\Program Files\Autodesk\AutoCAD 2024\`. The managed API is compatible across 2021–2024, so any of those installs can supply the reference DLLs.

Output: `src\HCW.AutoCAD.Plugin\bin\Release\hcwCAD-KIT.dll`. Build folders (`bin`, `obj`) are not part of the repository.

Icons are IBM Carbon Design System PNGs (Apache-2.0), embedded in the DLL. The command-to-icon map is `src/HCW.AutoCAD.Plugin/Resources/icon_map.json`. No separate icon files need to be shipped.

## Drawing units

Several tools read the drawing’s insertion units (`INSUNITS`, the UNITS command).

| Tool | How units work |
|---|---|
| Ribbon room labels (`HCWROOM*`) | Read `INSUNITS` on each use. Millimetres, centimetres, metres, feet, and inches are scaled. Anything else is treated as metres. Areas are square metres, or square feet when the drawing is in feet or inches. Imperial dimensions print as `ft'-in"`. |
| Typed room commands (`M*`, `F*`, `I*`) | Fixed systems. Metric assumes 1 drawing unit = 1 metre. Inches assumes 1 unit = 1 inch and reports area in square feet. Feet reports architectural feet-inches, and divides by 12 when `INSUNITS` is inches so an inches drawing is not inflated 12×. |
| `MSETUP` (MEASURE) | Chosen once per AutoCAD session, stored in memory only. Metric: 1 unit = 1 metre, lengths round to 1 cm. Imperial: 1 unit = 1 inch, lengths round to 1/8 in. This choice is independent of `INSUNITS`. |
| `POLYAREA` | Converts from `INSUNITS` into square metres or square feet. |
| `HCWSTYLES` | Sizes text from a 2.5 mm plotted height at a typical sheet scale, converted into the drawing’s units. |
| `BPLTSTART` | Sets the drawing to metres, decimal length units, 3 decimal places. |

Room-label text height, floor prefix, and the session log live in memory. They reset when AutoCAD closes or when you run the matching reset command. They are not saved in the DWG.

---

## hcwCAD-KIT

### Setup

| Command | What it does |
|---|---|
| `HCWLAYERS` | Creates or verifies the 36-layer HCW Layer Standard v4.0 (colour, linetype, lineweight). Existing layers are left in place; missing ones are added. |
| `VHLAYERS` | Creates the legacy underscore layer set (`A_WALL_CUT`, `S_COLUMN`, and so on) and makes `A_WALL_CUT` current. Use one layer standard per drawing. |
| `BPLTSTART` | Creates every BP- and AP- layer, then sets metres, decimal units, and default dimension text/arrow size for a building-permission drawing. |

### Building Permission (BPLT)

Used for BBMP / AutoPlan submission drawings. `BPLTLAYERS` creates the BP- drafting layers and the AP- marking layers. `BPLTTITLEBLOCK` asks for A1, A2, A3, or A4 (default A2) and a bottom-left point, then draws a sheet border and a title strip. Sheet sizes are in metres (A2 is 0.594 × 0.420).

`BPLTCOPY` (small button) copies selected objects that sit on a `BP-…` layer onto `AP-` plus the rest of that name. Objects that are not on a BP- layer, or whose matching AP- layer does not exist, are skipped. Names are not always a pair: `BP-SITE-BOUNDARY` looks for `AP-SITE-BOUNDARY`, which is not in the standard list, so that copy is skipped.

### Room labels

The ribbon uses one panel. It does not ask you to pick Metric, Feet, or Inches. `HCWROOM` opens a list of the 29 room types. `HCWCUSTOMROOM` asks you to type a name. Both then:

1. Create layers `ROOM-LABELS` (green) and `ROOM-RECT` (grey) if needed.
2. Ask for the first corner and the opposite corner.
3. On the first label of the session, ask for text height (default about 125 mm in real-world size, converted to drawing units).
4. Draw a closed rectangle (unless rectangle drawing is off) and three centred text lines: room name, width × height, and `Area: …`.
5. Remember the room in a session log used by the schedule and total commands.

`HCWROOMAREA` labels the area of a selected polyline at its vertex average. `HCWROOMRELABEL` replaces the text of one existing TEXT object. The new name is upper case, and includes the floor prefix when one is set.

### Measure (manual take-off)

Run `MSETUP` first (on **hcwCAD-KIT Settings**) so lengths and areas use the unit system you intend. Each take-off command creates its layers if they are missing, isolates those layers while you select, labels the geometry, prints a summary, writes a CSV next to the drawing (or in the temp folder if the drawing has no path), and draws a summary table on `MEASURE-TABLE`.

Draw the geometry on the layers below before you run the command. Deduction geometry must be close to the line it belongs to (you are asked for a match tolerance; default 0.01 drawing units).

| Command | Draw on | Result |
|---|---|---|
| `MLIN` | `MEASURE-LINEAR` and deductions on `MEASURE-DEDUCT` | Gross, deduction, and net length. Labels `L1`, `L1-D1`, … |
| `MBRK` | `MEASURE-FULLBRICK`, `MEASURE-HALFBRICK`, deductions on `MEASURE-DEDUCT` | Same, split by full brick and half brick (`FB`, `HB`) |
| `MBML` | `MEASURE-BEAM`, `MEASURE-LINTEL`, deductions on `MEASURE-DEDUCT` | Same, split by beam and lintel (`BM`, `LT`) |
| `MREC` | Closed 4-sided polylines on `MEASURE-COUNT` | Numbered rectangles `R1`… with length, breadth, area, perimeter |
| `MARE` | Closed polylines, circles, ellipses, or splines on `MEASURE-AREA` | Numbered areas `A1`… with area and perimeter |
| `MSLB` | Slab outlines on `MEASURE-SLAB`, openings on `MEASURE-SLAB-DEDUCT` | Each opening is deducted from the slab whose outline contains the opening’s centroid |

A deduction that does not match a parent is labelled `D? NO LINE` and is not included in the total. Labels go on `MEASURE-LABELS`. `MCLEAR` erases objects on `MEASURE-LABELS` and `MEASURE-TABLE` only. It does not erase the measured lines.

### Area and text

| Command | What it does |
|---|---|
| `POLYAREA` | Reads `INSUNITS`, asks for square metres or square feet, an optional number prefix, and a text height. Select polylines. Open ones are closed. Each gets a number on the current layer. You then pick a point for a running-total table on layer `AREA_TABLE`. |
| `DELETEAREATEXT` | Deletes TEXT and MTEXT whose content starts with `Area:` followed by more text. Choose All (the whole drawing) or Selection. This matches room-tool and similar area notes. A note you typed that starts with `Area:` is deleted too. |
| `TextIncrement` | Select TEXT that contains a number. Each click places a copy with the last number increased by 1, keeping leading zeros. Enter ends the command. |
| `WinLabel` | Select window blocks. For each dynamic block that has a `WNAME` property, writes the first two characters of that value on layer `WIN_LABELS`, stacked down from the point you pick. Set the height first with `WinLabelHeight` (default 0.15). |
| `TXTALIGN` | Select TEXT, choose Left, Right, CentreX, Top, Middle, or Bottom, then a reference point. Left / Right / CentreX share the reference X. Top / Middle / Bottom share the reference Y. |
| `TXTDUP` | Finds TEXT with the same content and the same position (to 0.001). Asks whether to delete the extras. |

---

## hcwCAD-KIT Settings

### Layer maintenance

These commands work on the HCW v4.0 names (`AN-`, `A-`, `I-`, `E-`, `P-`, `S-`, `PR-`), except where noted.

| Command | What it does |
|---|---|
| `HCWRESET` | Puts every existing standard layer back to its colour, linetype, and lineweight, and turns it on, thaws it, and unlocks it. |
| `HCWPURGE` | Runs AutoCAD `PURGE` All, twice, so nested empty blocks can be removed. |
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
| `HCWROOMRESET` | Restores defaults: rectangles on, layers `ROOM-LABELS` / `ROOM-RECT`, no floor prefix, default text height. Clears the remembered text height so the next label asks again. |
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
| `MSHOW` | Turns every layer in the drawing on (not only MEASURE layers). |
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

## Typed room commands

The ribbon does not show a button per room type. The old command names still work. Replace the leading letter: **M** metric, **F** feet, **I** inches. Example: `MBR`, `FBR`, and `IBR` all label a Bedroom in that unit system.

Each system keeps its own text height, layers, floor prefix, and log. Loading all three does not mix their settings.

| Suffix | Room | Suffix | Room |
|---|---|---|---|
| `BR` | Bedroom | `SC` | Staircase |
| `MB` | Master Bedroom | `CO` | Corridor |
| `GR` | Guest Room | `EN` | Entrance |
| `LI` | Living Room | `LO` | Lobby |
| `DI` | Dining Room | `UT` | Utility |
| `KI` | Kitchen | `LA` | Laundry |
| `PA` | Pantry | `GA` | Garage |
| `BA` | Bathroom | `GD` | Garden |
| `AT` | Attached Toilet | `CY` | Courtyard |
| `CT` | Common Toilet | `WR` | Wardrobe |
| `ST` | Study Room | `DR` | Dressing Room |
| `OF` | Office | `HT` | Home Theater |
| `SR` | Store Room | `GY` | Gym |
| `PR` | Pooja Room | `DO` | Custom name (typed) |
| `BC` | Balcony | | |
| `TE` | Terrace | | |

`MROOM`, `FROOM`, and `IROOM` open the same room list as `HCWROOM`, but they draw with that system’s engine and layers:

| System | Label layer | Rectangle layer | Default text height |
|---|---|---|---|
| Metric (`M`) | `ROOM-LABELS-M` | `ROOM-RECT-M` | 0.15 m |
| Feet (`F`) | `ROOM-LABELS-F` | `ROOM-RECT-F` | 0.5 ft |
| Inches (`I`) | `ROOM-LABELS-I` | `ROOM-RECT-I` | 6 in |

The other ribbon room commands have typed twins. Prefix **M**, **F**, or **I**:

| Typed | Same job as |
|---|---|
| `MAR` / `FAR` / `IAR` | `HCWROOMAREA` |
| `MRECT` / `FRECT` / `IRECT` | `HCWROOMRECT` |
| `MHIDERECT` / `FHIDERECT` / `IHIDERECT` | `HCWROOMHIDERECT` |
| `MTH` / `FTH` / `ITH` | `HCWROOMTH` |
| `MLAYER` / `FLAYER` / `ILAYER` | `HCWROOMLAYER` |
| `MSET` / `FSET` / `ISET` | `HCWROOMSET` |
| `MRESET` / `FRESET` / `IRESET` | `HCWROOMRESET` |
| `MFLOOR` / `FFLOOR` / `IFLOOR` | `HCWROOMFLOOR` |
| `MRELABEL` / `FRELABEL` / `IRELABEL` | `HCWROOMRELABEL` |
| `MAUDIT` / `FAUDIT` / `IAUDIT` | `HCWROOMAUDIT` |
| `MCHECK` / `FCHECK` / `ICHECK` | `HCWROOMCHECK` |
| `MSCHEDULE` / `FSCHEDULE` / `ISCHEDULE` | `HCWROOMSCHEDULE` |
| `MTOTAL` / `FTOTAL` / `ITOTAL` | `HCWROOMTOTAL` |
| `MHELP` / `FHELP` / `IHELP` | Command list for that prefix |

Feet command `FFLOOR` is the floor prefix. It is not a second copy of `FLAYER`.

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
src/
  HCW.AutoCAD.Plugin.sln
  HCW.AutoCAD.Plugin.bundle/PackageContents.xml
  HCW.AutoCAD.Plugin/
    HCW.AutoCAD.Plugin.csproj
    Properties/AssemblyInfo.cs    version, command-class registration, ribbon startup
    LayerData.cs                  BPLT, HCW v4.0, VHLAYERS, and the 29 room names
    RoomEngine.cs                 metric, feet, inches, and auto-unit label engines
    Utilities.cs                  layers, linetypes, CSV, unit conversion
    Commands/                     one class per command group
    UI/HcwRibbonApplication.cs    both ribbon tabs
    UI/RoomPickerForm.cs          room-type dialog
    UI/RoomUnitSelector.cs        ribbon room commands → RoomEngineAuto
    UI/RibbonCommandHandler.cs
    UI/IconLoader.cs
    Resources/Icons/              embedded 16×16 and 32×32 PNGs
```

`AssemblyInfo.cs` lists every command class AutoCAD should register, and points startup at `HcwRibbonApplication`. The assembly file name is `hcwCAD-KIT.dll`. Command names are unchanged.
