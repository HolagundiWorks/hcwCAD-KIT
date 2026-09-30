# hcwCAD-KIT

Holgundi Consulting Works drawing tools for AutoCAD, BricsCAD, and ZWCAD. One set of commands — layers, building-permission sheets, room labels, take-off, area fields, and text — loaded from `hcwCAD-KIT.dll`. The ribbon has the same two tabs in each host.

Version 1.0.0. Author: Holgundi Consulting Works. Licensed under the [MIT License](LICENSE).

On load the plugin adds two ribbon tabs and switches to **hcwCAD-KIT**:

| Tab | Use it for |
|---|---|
| **hcwCAD-KIT** | Notes and sheet fields, room labels, take-off, area and text |
| **hcwCAD-KIT Settings** | Layer creation, layer checks, BPLT setup, room checks, text checks |

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

**Installers.** Each host has its own setup program. Run the one that matches the program you use. It does not need administrator rights. It copies `hcwCAD-KIT.bundle` into that user’s ApplicationPlugins folder. Close the host, run the setup, then start the host again.

| Setup | Installs for | Folder |
|---|---|---|
| `hcwCAD-KIT-AutoCAD-1.0.0-Setup.exe` | AutoCAD 2021–2024 | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| `hcwCAD-KIT-BricsCAD-1.0.0-Setup.exe` | BricsCAD V26 | `%APPDATA%\Bricsys\ApplicationPlugins\` |
| `hcwCAD-KIT-ZWCAD-1.0.0-Setup.exe` | ZWCAD 2024–2026 | `%APPDATA%\ZWSOFT\ApplicationPlugins\` |

Build all three from this folder (Inno Setup 6 is required):

```
powershell -File build\Package-Installers.ps1
```

The setup programs are written to `dist\`. Uninstall one host from Windows Settings; the other hosts are separate programs and stay installed.

**Security software warnings.** The setup programs and `hcwCAD-KIT.dll` are not code-signed, and the plugin loads into the host when it starts and writes files (settings, exports) under your user folder. Some antivirus products flag that pattern as a generic "Trojan" or "ML" detection even though nothing in the source does anything of the kind. The code is all in this repository (no network calls, no registry writes, no launching of other programs), and every file the build script produces is listed with its SHA-256 in `dist\SHA256SUMS.txt`. If a scanner flags a build:

1. Check the file against `SHA256SUMS.txt` (`Get-FileHash <file> -Algorithm SHA256`).
2. Use the `hcwCAD-KIT-<host>-1.0.0-bundle.zip` that the build script also makes: unzip it into the host's ApplicationPlugins folder as `hcwCAD-KIT.bundle`. No installer is involved.
3. Report the file as a false positive to the vendor. For Microsoft Defender: https://www.microsoft.com/wdsi/filesubmission.
4. For a release you hand to others, sign `hcwCAD-KIT.dll` and the setup programs with a code-signing certificate. That is the fix that lasts; unsigned new files start with no reputation.

**Load it every time the host starts**, without the setup program:

1. Copy `hcwCAD-KIT.dll` into the bundle’s `Contents` folder.
2. Copy the bundle folder to the host’s ApplicationPlugins directory.
3. Restart the host. `PackageContents.xml` sets `LoadOnAutoCADStartup="true"`.

| Host | Bundle | ApplicationPlugins folder |
|---|---|---|
| AutoCAD | `src/HCW.AutoCAD.Plugin.bundle` | `%APPDATA%\Autodesk\ApplicationPlugins\` |
| BricsCAD | `src/HCW.BricsCAD.Plugin.bundle` | `%APPDATA%\Bricsys\ApplicationPlugins\` |
| ZWCAD | `src/HCW.ZWCAD.Plugin.bundle` | `%APPDATA%\ZWSOFT\ApplicationPlugins\` |

## Build

From the repository root. `dotnet build` is the command that works with the .NET SDK on this project (the Visual Studio Build Tools MSBuild does not resolve `Microsoft.NET.Sdk`).

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

The default path is `C:\Program Files\ZWSOFT\ZWCAD 2026\`, or the `ZWCAD_INSTALL_DIR` environment variable. The project expects `ZwManaged.dll`, `ZwDatabaseMgd.dll`, and `ZdWindows.dll` (the ribbon API) in that folder. Output: `src\HCW.ZWCAD.Plugin\bin\x64\Release\hcwCAD-KIT.dll`.

Building `src\HCW.AutoCAD.Plugin.sln` builds AutoCAD, BricsCAD, and ZWCAD. `build\Package-Installers.ps1` builds those three and compiles a setup program for each.

Checks that need no CAD install:

```
dotnet test tests\HCW.Logic.Tests\HCW.Logic.Tests.csproj
dotnet build build\CompileCheck.csproj
```

The first runs the unit tests for the CAD-free logic in `src\HCW.AutoCAD.Plugin\Logic`. The second compiles the plugin sources against the public AutoCAD.NET reference package. GitHub Actions runs both on every push and pull request. Neither replaces testing in the host.

Icons are IBM Carbon Design System PNGs (Apache-2.0), embedded in the DLL. They are tinted for the current ribbon theme so the glyphs stay visible on the light theme and the dark theme. Each ribbon button names its icon in `HcwRibbonApplication.cs`. No separate icon files need to be shipped. Build folders (`bin`, `obj`) and the compiled setup programs (`dist`) are not part of the repository.

## Drawing units

Several tools read the drawing’s insertion units (`INSUNITS`, the UNITS command).

| Tool | How units work |
|---|---|
| `ROOM`, `ROOMC`, `RAREA` | Read `INSUNITS`. Millimetres, centimetres, metres, feet, and inches are scaled. If units are unset, the command asks millimetres or metres and stores that on the drawing. Areas are square metres, or square feet when the drawing is in feet or inches. |
| Take-off (`TOSTART`) | Reads `INSUNITS`. Millimetres, centimetres, and metres round to 1 cm. Feet and inches round to 1/8 in. If units are unset, Start asks once and stores the answer on the drawing. It does not ask again. |
| `POLYAREA` | Converts from `INSUNITS` into square metres or square feet. |
| `HCWSTYLES` | Sizes text from a 2.5 mm plotted height at a typical sheet scale, converted into the drawing’s units. |
| `BPLTSTART` | Sets the drawing to metres, decimal length units, 3 decimal places. |

Room-label text height and floor prefix live in memory and reset when the host closes or when you run the matching reset command. The room log (used by the schedule, table and totals) is saved in the DWG.

---

## hcwCAD-KIT

### Notes and fields

Notes stay on the **hcwCAD-KIT** tab. The dropdown places a saved set. **Notes** opens the library: **New set** starts a blank set, **Add note** appends the next numbered line, **Save set** stores it, **Place** drops it on the sheet, and **Update** rewrites a notes block already on the drawing. Move a placed set with MOVE. `TITLENOTESAVE` stores a notes block already on the drawing under a name such as ELECTRIC NOTES.

**Fields** opens a list you can add to, edit, and save. **Place on sheet** drops the list as a movable block. **Update selected** rewrites a fields block already on the sheet. **Edit Field** picks that block and opens the same list. The title block is no longer on the ribbon. `TITLEBLOCK` still inserts the old A3 sheet if you type it.

Typed names: `TITLENOTES` (notes library), `TITLENOTE` (place the selected set), `TITLENOTESAVE`, `FIELDS` (field list), `FIELDSEDIT` (Edit Field), and `TITLEFIELDS` (fill the boxes of the old A3 title block; `BPLTTITLEBLOCK` is another name for `TITLEBLOCK`).

### Room labels

The ribbon uses one panel. It does not ask you to pick Metric, Feet, or Inches — the label follows the drawing’s units. `ROOM` opens a list of the 29 room types. `ROOMC` asks you to type a name. (`HCWROOM` and `HCWCUSTOMROOM` are the same commands.) Both then:

1. Create layers `ROOM-LABELS` (green) and `ROOM-RECT` (grey) if needed.
2. Ask for the first corner and the opposite corner.
3. Use the current text height (about 125 mm in real size, converted to drawing units). Change it with **Text Height** on the same panel (`HCWROOMTH`). The command does not ask every time.
4. Draw a closed rectangle on the corners you picked (unless rectangle drawing is off) and three centred text lines: room name, width × height, and `Area: …`.
5. Save the room in the drawing's room log, used by the schedule, table and total commands.

Text height, floor prefix, and the rectangle toggle are on the Room Labels panel. `RAREA` labels the area of a selected polyline at its centre. `RTAG` replaces the text of one existing TEXT object. The new name is upper case, and includes the floor prefix when one is set.

The old per-unit commands (`MBR`, `FBR`, `IBR`, and the rest) are removed. Use `ROOM`.

### Take-off

The tools were called Measure in earlier versions. Only the display names changed: the commands (`MLIN`, `MBRK`, `MPAINT`, `MSCHED`, …), the `MEASURE-*` layers and the data stored in drawings keep their names.

The **TakeOff** panel starts with **Start**, then one button for each element. There is no unit dropdown and no element dropdown.

1. **Start** (`TOSTART`, also registered as `MSETUP`) reads the drawing units and creates the take-off layers. It asks only when `INSUNITS` is unset, then stores millimetres or metres on the drawing. Later take-off commands use that and do not ask again. Label text is about 125 mm. An opening is matched to a wall when it lies within about 10 mm.
2. Draw each element on its layer. Draw openings on `MEASURE-DEDUCT`.
3. Click the element you measured: Linear, Brickwork, Beams, Columns, Wall paint, Ceiling, or Floor. Schedule, Insert Schedule, Area, and Slab are also available on the panel.

Each command isolates its layers while you select, then restores the layers it turned off. It labels the geometry, prints a summary, and draws a summary table on `MEASURE-TABLE`. Export is on demand: press **Export** (`MEXPORT`) to save the last take-off as CSV, or every take-off as one Excel workbook (see Excel export, below), next to the drawing (or in the temp folder if the drawing has no path). Layer names stay `MEASURE-…` so existing drawings keep working.

Walls, beams, and lintels of the same rounded length share one name (`FB-A`, `HB-A`, `BM-A`, `LT-A`, `L-A`). Each wall is numbered left to right, then bottom to top (`FB01`, `FB02`, …), and its openings are numbered along the wall in the same order: `FB01-D1`, `FB01-D2`, `FB02-D1`. Columns, doors, and windows of the same size share one schedule mark. Ceiling and floor outlines of the same area share one name.

`MSCHED` stores the schedule in the drawing: floors and ceiling heights, doors and windows, concrete columns, and a map from each measured deduction to a schedule name.

#### Floors

Each row on the **Floors** tab records one floor: **FFL to FFL height** (finished floor level to the next), **ceiling height** (wall paint uses it) and **lintel bottom height** (the underside of the lintel, measured up from the FFL). Apply warns when a ceiling is taller than its FFL to FFL height, when the lintel bottom is above the ceiling, and when a door or window is taller than its lintel bottom. The floor's lintel bottom applies to every opening unless the opening has its own value on the Doors and windows tab.

#### Schedule table

The schedule is drawn on `MEASURE-TABLE` as separate tables, each with a title: Floor heights, Door and window schedule (doors then windows, each with a total count), Column schedule (with total) and Deduction map. Tick **Draw schedule on the sheet** in `MSCHED`, or use the **Insert Schedule** button (`MSCHEDTABLE`) at any time to pick a top-left point and draw the saved schedule. It uses the take-off text height.

#### Door and window blocks (deduction lines in blocks)

Draw one line on `MEASURE-DEDUCT` inside each door or window block, along the opening length. It can be a dynamic block: the line is read as inserted, so a stretched door gives its own length.

1. `MLIN`, `MBRK`, `MBML` and `MPAINT` read every insert of such a block in the current space. Each inserted line is a deduction, matched to the nearest selected wall within the tolerance. A block that touches none of the selected walls is ignored (it belongs to another wall), with no `D? NO LINE` warning.
2. Put the block name in the **Block name** column of the schedule entry (several names separated by `;`). The name used is the dynamic block's own name, not its anonymous copy.
3. When a block deduction is measured, it is mapped to its schedule entry automatically (`FB01-D2` becomes `D1 Flush door`) and the map is saved. If the drawn length differs from the entry's length by more than 50 mm (2 in), a warning is printed.
4. `MPAINT` uses the entry's length × height for a block opening; lines drawn on `MEASURE-DEDUCT` with no block still use the label map.
5. **Add from blocks** in `MSCHED` creates one row per block name not yet linked: kind from the name (`win…` or `W…` is a window), a name (`D1`, `W1`), the line length of the first insert, and a default height you then correct. The dialog lists the blocks found and how many inserts each has.

#### Door and window schedule

Each row on the **Doors and windows** tab is one schedule entry:

| Column | Example | Meaning |
|---|---|---|
| Name | `W1` | The schedule name. Must be unique. |
| Door or window | `Window` | Drop-down. Filled from the name when you type it (`W…` is a window, otherwise a door). |
| Type | `UPVC` | Drop-down; the list follows the kind. Doors: Wood, Flush door, UPVC, Aluminium, WPC, Fabricated. Windows: Wood, UPVC, Aluminium, System aluminium. Changing the kind reloads the list. A type saved by an earlier version stays selectable. |
| Length | `1.2` | Size along the wall, in metres (feet when the drawing is in feet or inches). This is the deducted length. |
| Height | `1.2` | Opening height, in metres (feet when the drawing is in feet or inches). |
| Sill | `0.9` | Optional, for windows. When set, Apply checks that lintel bottom − sill equals the height (within 50 mm / 2 in). |
| Lintel bottom | `2.4` | Optional. Blank uses the floor's lintel bottom height; a value applies to this opening only and is shown in the schedule table. |
| Block name | `WIN-SLIDER` | Drawing block(s) that stand for this entry (see above). |
| Count | `3` | Set automatically from the map when you apply. |

Deduction map logic:

1. `MLIN`, `MBRK` and `MBML` label every deduction line as `<wall>-D<n>`: the wall it sits on (`FB01`) and its opening number on that wall (`FB01-D1`) and store its measured length on the label.
2. On the **Deduction map** tab, each deduction is one row with its measured length. The name of the schedule entry whose length is closest is pre-filled, provided it is within 50 mm (2 in when the drawing is in feet or inches): a deduction measured at `1.00` m suggests a `1.0` m entry, and so does `1.04`. If two entries are equally close, or none is in range, the cell stays empty and you choose. Type or change any name yourself.
3. Deduction names are matched loosely, so case, spaces, hyphens and leading zeros do not matter: `FB01-D1`, `fb01-d1` and `FB 01 D-1` are the same deduction. Drawings labelled with the older `FB D-01` style still map.
4. On **Apply**, each mapped label is rewritten to `<name> <type>` (`FB01-D1` becomes `W1 UPVC`). The count of each entry becomes the number of deductions mapped to it.
5. `MPAINT` then deducts the entry's length × height for every mapped opening. An unmapped deduction falls back to its measured length × the wall height.
6. Apply prints a warning when a name is used twice, when a map points to a name that is not in the schedule, or when the measured deduction length differs from the entry’s length by more than that tolerance.

**Group same size** merges entries with the same kind, type, length and height onto one row before you apply. Schedule sizes are metres, or feet when the drawing is in feet or inches.

Draw the geometry on the layers below before you run the command. Deduction geometry must be close to the line it belongs to (you are asked for a match tolerance; default 0.01 drawing units).

| Command | Draw on | Result |
|---|---|---|
| `MLIN` | `MEASURE-LINEAR` and deductions on `MEASURE-DEDUCT` | Same lengths share `L-A`. Walls `L01`, `L02`, …; deductions `L01-D1`, … |
| `MBRK` | `MEASURE-FULLBRICK`, `MEASURE-HALFBRICK`, deductions on `MEASURE-DEDUCT` | Full brick `FB-A` and half brick `HB-A`, walls `FB01`, deductions `FB01-D1` |
| `MBML` | `MEASURE-BEAM`, `MEASURE-LINTEL`, deductions on `MEASURE-DEDUCT` | Concrete beams `BM-A` and concrete lintels `LT-A` |
| `MCOL` | Closed 4-sided polylines on `MEASURE-COLUMN` | Equal column sizes share one mark. A size listed in the schedule uses that mark |
| `MREC` | Closed 4-sided polylines on `MEASURE-COUNT` | Equal rectangles share `R-A`, with length, breadth, count, area, perimeter |
| `MPAINT` | Full brick, half brick, or linear walls, plus `MEASURE-DEDUCT` | Wall length × ceiling height. A mapped opening uses its schedule width × height |
| `MCEIL` | Closed outlines on `MEASURE-CEILING` | Ceiling paint, grouped by equal area (`CP-A`) |
| `MFLOOR` | Closed outlines on `MEASURE-FLOOR` | Floor area, grouped by equal area (`FL-A`) |
| `MSCHEDTABLE` | The saved schedule | Draws the schedule as a table at a picked point |
| `MSCHED` | Labels already on `MEASURE-LABELS` | Edit the in-drawing schedule and rewrite mapped names |
| `MAREA` (`MARE` still works) | Closed polylines, circles, ellipses, or splines on `MEASURE-AREA` | Numbered areas `A1`… with area and perimeter |
| `MSLAB` (`MSLB` still works) | Slab outlines on `MEASURE-SLAB`, openings on `MEASURE-SLAB-DEDUCT` | Each opening is deducted from the slab whose outline contains the opening’s centroid |

A deduction that does not match a parent is labelled `D? NO LINE` and is not included in the total. Labels go on `MEASURE-LABELS`. `MCLEAR` erases objects on `MEASURE-LABELS` and `MEASURE-TABLE` only. It does not erase the measured lines or the stored schedule.

### Excel export, rates and bill

`MEXPORT` asks **Csv** or **Xlsx**. Csv saves the latest take-off, as before. **Xlsx** (also `MEXPORTX`) writes `<drawing>-Takeoffs.xlsx` beside the drawing (or in the temp folder), one sheet per saved take-off. Every take-off is saved in the drawing under its name (`Bricks`, `WallPaint`, `Areas` …); running it again replaces that one. Export works after restarting the host.

On the **Rates** tab of `MSCHED` give each take-off a unit and a price. A rated take-off adds a line to a **Bill** sheet: quantity, rate, and amount as an Excel formula (`Quantity × Rate`), with a total. The quantity is the last cell of the take-off's `GRAND TOTAL` row, or of its `TOTAL` row when it has no grand total.

### Wall numbering

Walls are numbered `FB01`, `FB02` … left to right (then bottom to top) by default. Set `WallNumbering` in the settings file to `TopBottom` (top to bottom, then left to right) or `Path`: the take-off then asks for a line on a visible layer and numbers the walls by their distance along it.

### Settings file

`HCWSETTINGS` creates `%APPDATA%\hcwCAD-KIT\settings.ini` if it is missing and prints its path; open it in any text editor. It holds the take-off label height (`TakeoffTextHeightMm`, default 125) and deduction tolerance (`DeductionToleranceMm`, default 10), the deduction-map suggestion tolerance, wall numbering, default floor and opening heights, and the `AUTODIM` distances, layers and text-fit factor. Each key has a comment. Restart the host after editing.

### Sheet set

`SHEETSET` makes numbered sheets from a template layout (title block, notes and a viewport). It asks for the template, the number of sheets, a prefix and first number, and the plot scale. Started from the Model tab it also asks for a model window per sheet (Enter uses the whole drawing); started from a layout it uses the drawing extents. Each new layout gets its viewport set to the scale and centred, and the title block's `DRAWING_NO` (and `DRAWING_TITLE`, when you type one). An existing layout name is never overwritten.

### Auto dimension (working drawings)

Four commands make the dimensions for a working drawing. Walls are read as faces: every vertex of a selected line or polyline is a point, so double-line walls give the wall thickness as well as the lengths. Openings come from the deduction lines: lines on `MEASURE-DEDUCT`, and the line inside each door or window block. Dimensions are created on `AN-DIMS` in the `HCW-WORKING` style (or the current style) and tagged. **Clear Auto Dims** (`AUTODIMCLEAR`) removes only those.

All distances are plotted millimetres in the settings file, so the drawing looks the same on paper at any scale. You give the plot scale each time.

**`AUTODIM`: outside chains.** Select the walls, then the sides (All, Top, Bottom, Left, Right) and the chains. Four chains per side, nearest the plan first:

| Chain | Points used |
|---|---|
| Openings | Door and window jambs in the side's outer band, with the wall pieces between them |
| Structure | Wall faces and ends, and column faces, in the outer band |
| Grid | Lines on the grid layers (`AN-GRID`, `A-GRID`) |
| Overall | First to last point on that side |

Points within 5 mm merge, dimensions shorter than `AutoDimMinMm` plotted are skipped, and a chain that repeats the one inside it is left out. Columns are closed polylines on `MEASURE-COLUMN` or `S-COLUMN`.

**`AUTODIMROOM`: inside each room.** Select the room outlines (closed polylines, such as `ROOM-RECT` or `MEASURE-FLOOR` outlines). For a rectangular room it draws, nearest each wall first, the wall's openings (corner, jamb, jamb, corner) where the wall has any, then the clear width along the bottom and the clear depth along the left. Other orthogonal outlines get one dimension per edge, inside the room. Angled edges are skipped and counted.

**`AUTODIMWALL`: any angle.** Select wall segments (lines, polyline segments, arcs). Each straight segment gets an aligned dimension offset outward (away from the middle of the selection) or inward; each arc gets a radius dimension.

**Short dimensions.** A dimension too short for its text has the text moved to a second or third row, with a leader, in the direction away from the plan (or into the room), so texts do not overprint. The width of a character is `AutoDimTextWidthFactor` of the text height.

Dimensions are plain, not associative: move the walls and re-run the command after `AUTODIMCLEAR`.

### Area and text

| Command | What it does |
|---|---|
| `POLYAREA` | Reads `INSUNITS`, asks for square metres or square feet, an optional number prefix, and a text height. Select polylines. Open ones are closed. Each gets a number on the current layer. You then pick a point for a running-total table on layer `AREA_TABLE`. |
| `INCARRAY` (`TextIncrement` still works) | Asks for an increment, a selection (anything except viewports), a base point, a spacing vector, and an end point. Copies the selection along that vector. Every number in text, MText, block attributes, attribute definitions, multileaders and dimension overrides is increased by the increment on each copy. Leading zeros stay only when the original number had them (`01` becomes `02`). |
| `INCCOPY` | Copy and paste with an incrementing number. Asks for an increment, a selection, and a base point, then paste points one after another (Enter to finish). Each copy adds the increment once more than the last, using the same number rules as `INCARRAY`. |
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

### Layers

The **Layers** panel is on this tab. It has one dropdown and one **Create Layers** button.

| Command | What it does |
|---|---|
| `HCWLAYERS` | Creates or verifies the 36-layer HCW Layer Standard v4.0 (colour, linetype, lineweight). Existing layers are left in place; missing ones are added. |
| `VHLAYERS` | Creates the legacy underscore layer set (`A_WALL_CUT`, `S_COLUMN`, and so on) and makes `A_WALL_CUT` current. Use one layer standard per drawing. |
| `BPLTLAYERS` | Creates the BP- drafting layers and the AP- marking layers, without changing drawing units. |

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

These buttons drive the same auto-unit engine as the **hcwCAD-KIT** room panel. 

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
| `HCWROOMSCHEDULE` | Writes `RoomSchedule-<unit>.csv` beside the drawing from the rooms labelled in this drawing. The room log is saved in the DWG. |
| `HCWROOMTABLE` | Draws the room schedule (number, room, size, area, total) as a table at a picked point. |
| `HCWROOMTOTAL` | Sums the areas of the rooms labelled in this drawing. You can filter by a fragment of the room name (`BEDROOM` matches `GF BEDROOM`). |
| `HCWROOMHELP` | Prints the detected unit and reminds you that UNITS is how you change it. |

`HCWROOMLAYER` is registered and can be typed. It is not on the ribbon. It changes the label layer name for later labels.

### Take-off settings

| Command | What it does |
|---|---|
| `TOSTART` (`MSETUP`) | Reads the drawing units and creates the take-off layers. Asks only when units are unset. Schedule heights and sizes are metres, or feet. |
| `MSHOW` | Turns back on only layers that a take-off command hid and did not restore. Layers you turned off yourself stay off. |
| `MEXPORT` | Saves the most recent take-off as a CSV beside the drawing. The take-off is stored in the drawing, so this still works after restarting the host. |
| `MCLEAR` | Erases take-off label and table objects (layers `MEASURE-LABELS` and `MEASURE-TABLE`). |

### BPLT

`BPLTSTART` and **Copy to AP-** are on this tab. `BPLTCOPY` copies selected objects that sit on a `BP-…` layer onto `AP-` plus the rest of that name. Objects that are not on a BP- layer, or whose matching AP- layer does not exist, are skipped.

| Command | What it does |
|---|---|
| `BPLTSTART` | Creates every BP- and AP- layer, then sets metres, decimal units, and default dimension text and arrow size. |
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
LICENSE                            MIT License
build/Rewrite-CadUsings.ps1        BricsCAD and ZWCAD namespace substitution
build/Package-Installers.ps1       builds each host and compiles its setup program
installer/hcwCAD-KIT.iss           shared Inno Setup script, one setup per host
src/
  HCW.AutoCAD.Plugin.sln           AutoCAD, BricsCAD, and ZWCAD
  HCW.AutoCAD.Plugin.bundle/
  HCW.BricsCAD.Plugin.bundle/
  HCW.ZWCAD.Plugin.bundle/
  HCW.AutoCAD.Plugin/              command source, shared by all three hosts
  HCW.BricsCAD.Plugin/             .NET 8 project, references BrxMgd.dll and TD_Mgd.dll
  HCW.ZWCAD.Plugin/                .NET Framework 4.8 project, references ZwManaged.dll and ZdWindows.dll
```

`AssemblyInfo.cs` lists every command class the host should register, and points startup at `HcwRibbonApplication`. The assembly file name is `hcwCAD-KIT.dll` for every host. Command names are the same. Saved note sets live in `%AppData%\hcwCAD-KIT\title-notes.txt`. Saved sheet fields live in `%AppData%\hcwCAD-KIT\sheet-fields.txt`. Both files are shared across hosts.

## License

hcwCAD-KIT is released under the MIT License. See [LICENSE](LICENSE).

The ribbon icons are IBM Carbon Design System artwork, included under the Apache License 2.0. That license applies to the icon files only. The rest of this repository is MIT.
