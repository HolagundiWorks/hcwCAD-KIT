# hcwCAD-KIT

Holgundi Consulting Works drawing tools for AutoCAD, BricsCAD, and ZWCAD. One set of commands — layers, building-permission sheets, room labels, take-off, area fields, and text — loaded from `hcwCAD-KIT.dll`. The ribbon has the same two tabs in each host.

Version 1.0.0. Author: Holgundi Consulting Works. Licensed under the [MIT License](LICENSE).

On load the plugin adds two ribbon tabs and switches to **hcwCAD-KIT**:

| Tab | Use it for |
|---|---|
| **hcwCAD-KIT** | Notes and sheet fields, room labels, take-off, electrical layout, stairs, area and text |
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
| Floor | `First` | Optional. Blank means every floor. Name a floor to tie the entry to it, and its lintel-bottom check uses that floor's lintel bottom height (an opening's own lintel bottom still wins). It shows in the schedule table. |

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

Walls are numbered `FB01`, `FB02` … left to right (then bottom to top) by default. Set `WallNumbering` in the settings file to `TopBottom` (top to bottom, then left to right), `Path` (the take-off then asks for a line on a visible layer and numbers the walls by their distance along it) or `Room`: the take-off asks you to select room outlines (closed polylines; Enter falls back to left to right) and numbers the walls room by room, rooms left to right then bottom to top, each wall going to the room whose outline is nearest its middle (a wall shared by two rooms goes to the first), and the walls of a room anticlockwise from the bottom.

**Colour-mapped deductions.** A standalone deduction line (not inside a door or window block) that is mapped to a schedule name takes a colour for that name, the same name always the same colour, so you can see on the drawing which openings are which. Lines inside blocks cannot be coloured per insert. `DeductionColours` 0 turns it off.

### Live updates (`HCWLIVE`)

While live updates are on, the plugin notes which objects a command changed and, when the command ends, refreshes what depends on them. Four things are watched:

| Watched | What follows |
|---|---|
| Electrical blocks | The ID text beside each block moves with it (and text for a deleted block goes), as `ELUPDATE` does for labels. Numbering and schedules still need `ELUPDATE`. |
| Level marks and the datum marker | Marks made from a datum re-read their level when they or the marker move. |
| Auto dimensions | Dimensions tied to walls follow the walls when they move. |
| The area statement | The statement, title block fields and table are worked out again when an outline they read changes, appears or is erased. |

`HCWLIVE` shows whether the service is on and offers **On**, **Off** and **Refresh** (refresh everything now). The setting `LiveUpdate` (1 to start with) decides whether it starts with the plugin. It reads changes through the host's object events, so it needs a command to end before it acts: it does not move things while you drag. It was written against the AutoCAD .NET interface; how BricsCAD and ZWCAD report grip edits and undo has not been tested, so on those use **Refresh** if something does not follow.

### One command for related jobs

Commands that did closely related jobs share a button, and the command asks which job first (Enter takes the first). The old command names still work and go straight to their job, so scripts and habits are not broken.

| Button command | Asks | Also reachable as |
|---|---|---|
| `HCWAXIS` | Grid [New/Add/Remove] | `HCWAXISADD`, `HCWAXISDEL` |
| `HCWLIFT` | Lift drawing [Plan/Section] | `HCWLIFTSECTION` |
| `HCWRAIL` | Handrail view [Plan/Elevation] | `HCWBALUSTRADE` |
| `HCWOPENMOVE` | Edit openings [Move/Slide/Replace/Sync] | `HCWOPENSLIDE`, `HCWOPENREPLACE`, `HCWOPENSYNC` |
| `FIXTXT` | Separate overlapping text [Vertical/Horizontal] | `FIXTXTH` |
| `HCWAUDIT` | Check [Layers/Overrides/Info/Counts] | `HCWAUDIT2`, `HCWINFO`, `HCWSCHEDULE` |
| `MEXPORT` | Export as [Csv/Xlsx] | `MEXPORTX` |
| `HCWROOMREPORT` | Read the rooms from [Outlines/Labels]; for Labels, [Table/Csv/Total] | `HCWROOMTABLE`, `HCWROOMSCHEDULE`, `HCWROOMTOTAL` |
| `AUTODIM` | Dimension [Outside/Rooms/Walls/Clear] | `AUTODIMROOM`, `AUTODIMWALL`, `AUTODIMCLEAR` |

### Settings file

`HCWSETTINGS` creates `%APPDATA%\hcwCAD-KIT\settings.ini` if it is missing and prints its path; open it in any text editor. It holds the take-off label height (`TakeoffTextHeightMm`, default 125) and deduction tolerance (`DeductionToleranceMm`, default 10), the deduction-map suggestion tolerance, wall numbering, default floor and opening heights, and the `AUTODIM` distances, grid layers and text-fit factor. Each key has a comment. Restart the host after editing.

### Sheet set

`SHEETSET` makes numbered sheets from a template layout (title block, notes and a viewport). It asks for the template, the number of sheets, a prefix and first number, and the plot scale. Started from the Model tab it also asks for a model window per sheet (Enter uses the whole drawing); started from a layout it uses the drawing extents. For each sheet it also asks its **own scale** (Enter takes the common one) and which **layers to show in its viewport**: names or patterns such as `A-*;AN-DIMS` separated by `;` (Enter repeats the last answer, a dot shows every layer); every other layer is frozen in that viewport only, except layer 0 and Defpoints. Each new layout gets its viewport set to the scale, centred and filtered, and the title block's `DRAWING_NO` (and `DRAWING_TITLE`, when you type one). Title block fields the template left blank (project title, owner, architect, plot use, PID …) are filled from the fields library (`FIELDS`) when the label matches the field (`Consulting architect` fills `ARCHITECT`); `SheetSetFillFields` 0 turns that off. An existing layout name is never overwritten.

`SHEETFIT` (**Fit to Sheet**, on the **Tools** tab) readies one layout. Run it on the layout, or from the Model tab and type the layout name. It asks you to select the drawing in model space (Enter takes everything that is visible: objects on switched-off or frozen layers are left out), then **Standard** or **Exact**. If the layout has no hcwCAD-KIT title plate it places the A3 plate on the paper corner, scaled to the sheet size. The paper-space area above the plate's data panel gets a locked viewport (on layer `AN-REF`, non-plotting) centred on the selection. **Standard** picks the smallest standard scale (1:50, 1:100, 1:200 …) that shows the whole selection; **Exact** fits it to about 95% of the area. A plate or viewport already on the layout is reused, and the viewport is resized to the area. Plates on sheets turned 90° are placed at the layout origin.

### Walls, doors, windows, axis grid and columns

These tools are on the **Walls & Openings** panel of the hcwCAD-KIT tab. They draw plan geometry that the auto-dimension and take-off tools already read. Draw in a UCS whose Z axis is the world Z.

| Command | What it does |
|---|---|
| `HCWWALL` | Asks for the thickness in mm (default 230) and where the line sits (**Centre**, **Left** or **Right** face). Then either select lines and polylines to turn into walls, or press Enter and pick points (**Close** ends a closed run, **Undo** removes the last point; Enter ends the run). Corners are mitred, and a very sharp corner is bevelled. An open run makes one closed outline; a closed run makes an outer and an inner outline. Outlines are closed polylines on `A-WALL`. Arcs and the curved segments of polylines are walls too: they are cut into straight pieces no more than 2 mm from the true curve, so the walls come out as many short mitred pieces. **Each wall is a wall object**: its centre line, thickness and line position are saved in the drawing (record `HCW_WALLS`, IDs `W0001`, `W0002` …) and the outlines carry the IDs of their walls, so `HCWWALLEDIT` can change it later. **Join on draw**: a new wall is merged with the closed `A-WALL` outlines it touches as it is drawn (setting `WallJoinOnDraw`, 1 to start with; 0 keeps each wall its own outline). Walls already cut by a door or window (which are loose lines) are not merged. |
| `HCWCLEAN` | Tidies lines. Choose whether to also join lines, then select lines (Enter takes every line in the current space). Zero-length lines and exact duplicates (same ends, either way round) are erased. With joining on, lines that lie on one straight line and touch or overlap are joined into one, which keeps the first line's properties; a line lying inside another counts as a duplicate. Lines only join when layer, linetype, colour, lineweight and elevation match. Lines on locked layers and lines that are not flat are left alone and counted. It reports what it erased. **Polylines and arcs** are cleaned too: exact duplicates (same points and bulges, either way round) are erased, and open polylines and arcs whose ends meet are joined end to end into one polyline (a ring of pieces becomes a closed polyline), keeping the first piece's properties. They only join when layer, linetype, colour, lineweight and elevation match; polylines with widths are left alone. To square up a corner between two lines use `HCWCORNER`. |
| `HCWCORNER` | Trims or extends two lines to meet at their corner. Click the first line, then the second, each on the part to keep. A line that stopped short is extended to the corner and one that ran past it is trimmed back, so it squares up an L or a T in a few clicks. It repeats until Enter. Lines and polylines, on layers that are not locked; parallel lines have no corner and are refused. On a polyline it uses the straight segment nearest your click, and that segment must end the polyline (an open polyline's first or last segment): the corner of a vertex in the middle would bend the next segment, so it is refused with a message. It is `FILLET` with radius 0 for wall lines. |
| `HCWWALLJOIN` | Select closed wall outlines that touch or overlap. They are merged into one outline per connected group, so T and L junctions have no lines inside the wall. Collinear points are removed and the originals are erased. The merged outline keeps the wall IDs of everything in it. Undo restores them. |
| `HCWWALLEDIT` | Pick a wall outline drawn by `HCWWALL` and change the thickness or the line position (Centre, Left, Right) of the wall nearest your pick. Every wall joined to it is rebuilt with it, and the doors and windows in those walls are cut again at the same places, with the same width, type, heights and tag, so they follow the new thickness. If one will not fit (for example a window that now runs past the end of a wall), nothing changes. One Undo reverses the whole edit. Outlines drawn before wall objects existed have no record and are refused. |
| `HCWROOMWALLS` | Finds rooms from the wall faces. Select a wall line, arc or polyline to say which layer(s) are the walls (Enter uses `A-WALL`), type the largest gap to bridge in mm (0 for none), then pick a point inside a room, one room after another, or choose **All** to find every room in the drawing in one go (labelled R1, R2 …). The wall lines are cut where they cross and joined (ends within 1 mm count as touching), and the smallest closed region around your point is the clear room: a closed polyline is drawn on `ROOM-RECT`, which `AUTODIMROOM` reads. Optionally type a name to get a name and area label on `ROOM-LABELS`. It works for any shape, not just rectangles, ignores stubs of wall inside the room, and reads arcs and polyline curves (cut to 2 mm of the true curve). **Gaps:** an unframed door or window opening up to the size you give is bridged where two loose wall ends face each other across it. **Columns:** a free-standing closed outline inside a room comes off its net area (the label shows the net). The room must otherwise be closed; if it is not you get the larger space around it, so check the area it reports. Areas are in square metres. |
| `HCWROOMREPORT` | Reads the rooms in a selection into a table. Select room outlines (closed polylines), the text that names them, area labels and dimensions together (or press Enter to take everything in the space, when only the outlines on the `RoomTableLayers` layers, `ROOM-RECT;MEASURE-FLOOR;A-ROOM`, count as rooms). For each outline it gives the **name** (the first line of text inside it that is not a number, an area, or a tag such as `D1` or `R2`; the one nearest the middle wins), the **length and width** (the sides of its bounding rectangle turned to its longest wall, so a tilted room gives its real sides), the **area** (the host's own area, curves included) and perimeter, the **dimensions** that lie in it (the ones `AUTODIMROOM` draws, or any other, largest first, each once), and a note when an area label written in the room disagrees with the outline. Rooms are ordered by **Position** (top to bottom, then left to right) or **Name**. It prints the table, can draw it at a point you pick on `AN-TEXT` (running it again replaces that table), always saves it as the take-off `Rooms` (so `MEXPORT` and `MEXPORTX` include it) and can write `<drawing>-Rooms.csv` and/or `<drawing>-Rooms.xlsx` beside the drawing. Lengths are metres to 2 places, or whole millimetres with `RoomTableUnit` set to `mm`; areas are square metres whatever the drawing units. The older `HCWROOMTABLE` in the room checks panel draws a schedule from the room labels instead. |
| `HCWDOOR`, `HCWWINDOW` | Type the width in mm, then pick the position on the wall (pick on a face line or between the faces). The tool finds the wall's two faces (parallel lines 60 to 600 mm apart on the same layer as the line you picked near), cuts both for the opening and closes it with jamb lines. A polyline outline is exploded to lines where it is cut. A door also asks which side it opens to and has **Flip hinge**. A door also asks its **type** (Single, Double leaf, Sliding), then door and window ask the **height** and (windows) the **sill** in mm (the settings `DoorHeightMm` 2100, `WindowHeightMm` 1200, `WindowSillMm` 900 are offered). The opening is a block named `HCW_D_900x230` (single door), `HCW_DD_1500x230` (double leaf), `HCW_DS_1800x230` (sliding) or `HCW_W_1200x230` (window) (width x wall thickness, in mm) on `A-DOOR` or `A-WIND`, with a frame at each jamb (`DoorFrameMm`, 50 to start with) and, for a door, the leaf or leaves of `DoorLeafMm` (40) with their swing arcs. It has a line on `MEASURE-DEDUCT`, so the take-off and its schedule read it like any other door or window block, and three invisible attributes `SILL`, `HEIGHT` and `LINTEL` (lintel bottom = sill + height) that `HCWOPENSYNC` reads. A tag `D1`, `W1` … (next free number) goes on `AN-TEXT`, 250 mm high. Enter ends the command. |
| `HCWAXIS` | Type the bay widths in mm for X (left to right) and Y (bottom to top), such as `4000 4500 3*3600` (three bays of 3600). Then the plot scale, bubbles at both ends or the start only, and the lower-left intersection. It draws the grid on `AN-GRID`, bubbles 8 mm across on `AN-SYMB` and labels 3.5 mm high on `AN-TEXT` at that scale: numbers 1, 2, 3 … along the bottom, letters A, B, C … up the left (I and O are skipped; after Z comes AA). Bay widths are always in millimetres, whatever the drawing units. |
| `HCWAXISADD` | Adds a grid line parallel to one already drawn: select it, then pick a point on the new line or press Enter and type a distance in mm (positive to the left of the line looking from its start to its end). The new line is as long as the old one, the bubbles at its ends are copied, and the label is the next number or letter (1 gives 2, H gives J, Z gives AA) unless you type another. |
| `HCWAXISDEL` | Select a grid line and it is erased with its bubbles and their labels. The other lines keep their labels, so renumber by hand if you want the sequence closed up. |
| `HCWLIFT` | Draws a lift shaft in plan. First it can take the sizes from the **lift table** (setting `LiftTable`, rows `persons=car:shaft:door` in mm; the rows it starts with are typical, so replace them with your lift maker's): give the capacity in persons and the smallest lift that carries it fills the size prompts, or press Enter to type sizes. It asks the clear shaft size (`1800x2000`, width x depth, mm), the shaft wall thickness (230), the car size (`1100x1400`), the door width (800), which side the door is on (Bottom, Right, Top or Left) and whether to draw a **machine room** outline (a dashed rectangle `LiftMachineMarginMm` beyond the shaft wall on every side, on `A-LIFT-MR`). Then pick the centre of each shaft; Enter ends. It draws the wall as one outline with the landing door opening cut through it (`A-WALL`), the clear shaft as a closed polyline on `BP-LIFT`, the car (30 mm back from the front wall) and the landing and car doors on `A-DOOR`, and a `LIFT` label with the clear size. Select the `BP-LIFT` outlines as a deduction in `HCWAREASTMT`. It refuses sizes that do not work (car wider than the shaft, door wider than the car, less than 100 mm of wall beside the door) and says why. |
| `HCWLIFTSECTION` | Draws a section through a lift shaft from the same sizes: the pit, the front wall with a door opening at each landing, the back wall, the car at the lowest landing, the overhead to the underside of the top slab and, if asked, a machine room above. Asks the number of floors, the floor-to-floor height, the pit depth (1400) and the overhead (4200) in mm, and picks the inside face of the front wall at the lowest landing. Pit, overhead and travel are dimensioned. Pit, overhead and the other figures are inputs, not code values. |
| `HCWESCALATOR` | Draws an escalator in plan and, if you like, in side elevation. Asks the rise (floor to floor) in mm, the angle (30, or 35 up to 6 m rise; settings `EscalatorAngleDeg` and others offer the first values), the nominal step width (600, 800 or 1000), and the flat landing at each end (at least 1200, default 2500). It refuses an angle outside 20 to 35 degrees or a 35 degree escalator over 6 m rise. Plan: the truss outline with a balustrade on each side, the steps as riser lines at a 400 mm tread, comb plates, an UP arrow and a label. Elevation: the truss, the handrail line, the levels and the rise and run dimensioned. Pick the lower corner and the direction it runs up. |
| `HCWRAIL` | Asks the handrail width in mm (default 50), the largest distance between posts in mm (default 1200) and the post size in mm (default 50; 0 for no posts). Then select lines or polylines for the handrail to follow, or press Enter and pick points as for `HCWWALL`. The rail is a closed outline of that width (two outlines for a closed run) and the posts are squares at every corner and end, with extra posts so each straight stretch is divided equally and no gap is wider than the spacing. All on `A-RAIL`. Arcs and the curved segments of polylines are followed (cut into short pieces); posts go only at the real corners and ends and at the spacing along each curve, not at the cut points. This is the plan view; `HCWBALUSTRADE` draws the elevation. |
| `HCWBALUSTRADE` | Draws a balustrade in elevation along the same kind of line or polyline as `HCWRAIL`. Asks the handrail height (900), the largest distance between posts (1200), the post size (50), the baluster size (12), the **largest clear gap** between balusters (100) and the slope in degrees (0 for a level run; a flight's angle makes the rail follow it). Each line or polyline is drawn as one run opened out flat, one below the other, from the point you pick: the floor or flight line, the handrail, a post at every corner and end and at the spacing between, and the fewest balusters that keep every gap within the limit. It reports the posts, balusters and the largest gap. The 100 mm gap is a common rule, not a code value for your job. |
| `HCWOPENMOVE` | Select one or more doors and windows, then (one opening) pick its new position on a wall and, for a door, confirm or change the side it opens to (Enter keeps it; **Flip hinge** is offered), or (several openings) pick two points and every opening moves by that displacement. The old position is closed (jamb lines and tag erased, both faces bridged) and the opening is cut at the new one, keeping its width, type, height, sill and tag number. If any new place is refused (no wall, no opposite face, past the end of a face), nothing changes. One Undo reverses the whole move. **Openings drawn some other way** work too: press Enter at the selection and pick a point inside a break in the wall (a break at the same place in both faces, 300 mm to 6 m wide) and say door or window; a block of another make standing in such a gap is read the same way. A gap has no swing, so a door asks for its side. |
| `HCWOPENSLIDE` | Select an opening (or choose **Gap**) and drag it along its wall: a ghost of the opening follows the cursor along the wall's direction and the move is made where you click. The cut is made on the click, not while dragging (the wall is not edited live); the opening keeps its width, type, heights and tag. |
| `HCWOPENREPLACE` | Select an opening, then choose **Door** or **Window**, a width in mm, and (for a door) its type, then the height and sill (all default to what is there). The old opening is closed and the new one is cut in the same place, at the same wall thickness. A door asks the side it opens to (Enter keeps the old one when the old opening was a door made by the tools). The same kind keeps its tag number; a change of kind gets the next free `D` or `W` number. Refused changes leave the opening as it was. |
| `HCWOPENSYNC` | Reads the doors and windows made by the tools in the current space (their block names and `SILL` and `HEIGHT` attributes) and puts them in the opening schedule of the take-off (`MSCHED`): one entry for each kind, width, height and sill, with its count, block names and lintel bottom (sill plus height). Entries already in the schedule for the same blocks and sizes keep their mark and type; new ones get the next free `D` or `W` mark. Sizes are written in the schedule's unit (metres, or feet in an imperial drawing). It does not set the schedule's Floor column. |
| `HCWCOLUMN` | Type the column size in mm: `230x450`, a single number for a square (`300`), or `D450` for a round column. Choose the layer (`A-COL`, or `MEASURE-COLUMN` so `MCOL` reads them without moving them), a solid fill (yes or no), then select the grid lines, or press Enter to use every line on the grid layers (`AutoDimGridLayers`, default `AN-GRID;A-GRID`). Columns go on every intersection, or only those inside a window you pick. Each is a closed polyline (a circle for a round one) on that layer, turned to follow the grid, with a solid fill on `AN-HATCH`. An intersection that already has a column is skipped, so it is safe to run again. More options: **Different sizes at chosen intersections** (type a size, then window the intersections that get it, repeat; Enter ends), **Columns on the edge of the grid [Centred/Flush]** (flush moves each column on the lowest or highest grid line in a direction inwards, so its outer face sits on the line; the setting `ColumnEdgeProjectMm` lets it stand that far outside the line, for example half a wall thickness to be flush with the wall face), and **Draw each column as [Outline/Block with mark]** (a block `HCW_COL_230x450` or `HCW_COL_R450` with the outline, an optional fill and an editable `MARK` attribute; not available on `MEASURE-COLUMN`, where the take-off reads outlines). |
| `HCWCOLSCHED` | Marks every column on `A-COL` (rectangles and circles) with its mark `C1`, `C2` … on `AN-TEXT`, biggest section first, equal sizes sharing a mark. Pick a point to draw the column schedule (mark, size in mm, number, with a total); press Enter to mark only. Column blocks from `HCWCOLUMN` get their mark in the `MARK` attribute instead of loose text. Run it again after any change: earlier marks and the table are replaced. |

An opening is refused, with the reason, when the pick is not near a wall, there is no opposite face, or the opening would run past the end of a face. Columns are on `A-COL`, which `AUTODIM` can read as its column layer; to measure them with `MCOL`, move them to `MEASURE-COLUMN` (the rectangles are plain closed polylines). Run `HCWWALLJOIN` before cutting openings if junctions need to be clean, since a cut works on the faces as they are.

### Symbols

The **Symbols** panel (hcwCAD-KIT tab) draws symbols sized for the sheet. Each command asks the plot scale once, remembers it, and draws the symbol in sheet millimetres times that scale, so it plots at the same size at 1:50 or 1:200. Symbols go on `AN-SYMB`; loose text goes on `AN-TEXT`.

| Command | What it does |
|---|---|
| `HCWLEVEL` | Places level marks (a triangle on the level point, a line, and the value above it) as the block `HCW_LEVEL` with an editable `LEVEL` attribute. Choose **Ceiling** for marks whose triangle points up with the value below the line (block `HCW_LEVEL_UP`) and **Floor** to go back. Pick the point. The value comes from the datum: choose **Datum**, pick a point and give its level in metres, and every later mark takes its level from its height above or below that point. The datum is drawn as a small circle (the **datum marker**, on `AN-SYMB`) and later marks made from it are *live*: while live updates are on (`HCWLIVE`), moving the marker or a mark re-reads the levels of every mark that took its value from the datum. Or choose **Value** and type the level for the next mark (a typed mark stays as typed). With no datum it asks each time. Values read `+3.150`, `-0.450`, `±0.000`. Enter ends the command. |
| `HCWNORTH` | Places the north arrow (block `HCW_NORTH`: circle, half-filled arrow, N). Pick the position, then a point in the direction of north; Enter points it up the screen. |
| `HCWSECTION` | Pick the start and end of the section line and the side you look toward, then the label (default A, then B, C … without I and O). Then the **sheet number** the section is drawn on (the name of the layout you are on, since `RENUMBERLAYOUTS` numbers layouts; `-` in model space; type `none` to leave it off). Draws the section line in centre linetype, a thick shaft at each end pointing the way you look, and a bubble on each split by a line with the letter above and the sheet number below. |
| `HCWSLOPE` | Type the slope text (`1:100`, `2%`), then pick the high end and the low end. Draws the line, an arrow head at the low end and the text above the middle, turned so it reads left to right. Enter ends the command. |
| `HCWLEVELSCHED` | Draws a schedule of the levels marked with `HCWLEVEL` in the current space: each distinct level, highest first, with the number of marks. |
| `HCWELEV` | Places an elevation marker (block `HCW_ELEV`): a circle split by a line with the elevation number above and the sheet number below, and a pointer the way you look. Pick the position and a point in the viewing direction, then the elevation number (the next number is offered) and the sheet number (the layout you are on is offered). The numbers stay upright. Enter ends the command. |

### Area statement (`HCWAREASTMT`)

The **Area Statement** button (Area & Text panel) builds the building permit area statement and fills the title block. Run it from any tab; it switches to Model to select and switches back. Areas are in square metres whatever the drawing units.

1. Select the **site boundary** (a closed polyline; Enter skips it, and then the floor area ratio and ground cover are not worked out).
2. Give the **number of floors** (1 to 12) and a name for each (defaults GROUND, FIRST, SECOND …).
3. For each floor, select its **built-up outlines** (closed polylines; Enter if none), then the **areas left out of the FAR** such as shafts, ducts and the lift (Enter if none). Open polylines are skipped and counted.

It prints a table: gross, deduction and net per floor, the totals, the site area, the F.A.R. (total net area as a percentage of the site) and ground cover (the first floor's gross area, and its percentage of the site). It warns when a floor's deductions exceed its area, or ground cover is more than the site.

Every hcwCAD-KIT title block in the drawing, in model space or on any layout, can then be filled: `SITE_AREA`, `FL1`–`FL4` with their `DED`, `NET` and `GROSS`, `TOT_DED`, `TOT_NET`, `TOT_GROSS`, `FAR_ACH`, `GC_ACH` and `GC_PCT`. Unused floor rows read `--`. The title block has four floor rows (see **More than four floors** below). The permissible values come from the settings `AreaFarPermittedPercent` (125 for an FAR of 1.25) and `AreaGroundCoverPermittedPercent` (both 0 to start with, which leaves `FAR_PERM` and `GC_PERM` for you to type): when set, `FAR_PERM` is filled in percent, `GC_PERM` in square metres (site area × the percentage), and a statement over either is flagged. Take them from the bylaws for the plot. Finally you can pick a point to draw the same figures as a table on `AN-TEXT`.

**Reading from layers.** At the first prompt choose **Selection** (as above) or **Layers**. With layers you give, for the site and for each floor, the layer(s) its closed polylines are on, separated by `;` (offered first: `AreaSiteLayer`, `BP-SITE-BOUNDARY`, and `AreaGrossLayer`, `BP-BUILDING-CUT`; a floor needs its own layer, for example one per floor). The exempt areas of a floor are the closed polylines on the layers you name for it. **Exemptions by rule:** set `AreaExemptRules` to what the bylaws allow, as `LAYER=percent[:limit sq m per floor]` separated by `;` (for example `BP-LIFT=100; BP-BALCONY=50:10`): each layer's area counts for that percentage, up to the limit. A layer with no rule counts in full. The figures are yours to take from the bylaws; none are built in.

**Permissible values by plot size and zone.** `AreaPermTable` holds rows `zone|min plot sq m|max plot sq m (0 = no limit)|FAR percent|ground cover percent` separated by `;` (use `*` as the zone for any zone), and `AreaZone` names your zone. When the table has a row for the plot area, it replaces `AreaFarPermittedPercent` and `AreaGroundCoverPermittedPercent`; if it has no row a warning says so and the two settings are used. Again, the figures come from your bylaws.

**More than four floors.** The title block has four floor rows. With more floors the last row holds the rest together, named `FOURTH TO SIXTH` for example, with their areas summed, so the rows add up to the totals. The table lists every floor.

**It updates itself.** What the statement was made from (the outlines by handle, or the layers), whether the title blocks are filled and where the table is are saved in the drawing. While live updates are on (`HCWLIVE`), changing, adding or erasing an outline it reads works the statement out again, rewrites the title block fields and redraws the table (when you are on the Model tab; on a layout the table waits). `HCWAREASTMT` again replaces the saved statement.

### Auto dimension (working drawings)

Four commands make the dimensions for a working drawing. **`AUTODIM`** and **`AUTODIMROOM`** open a layer dialog first; you tick the layers that hold each part of the plan:

| Column | What goes on these layers |
|---|---|
| Walls | Lines and polylines. Walls are read as faces: every vertex is a point, so double-line walls give the wall thickness as well as the lengths. |
| Windows and doors | Blocks (dynamic blocks work) with a line on `MEASURE-DEDUCT` inside them: that line is the opening. A block with no such line uses its own extents (its longer side). Plain geometry (lines, polylines, arcs, circles) is grouped into openings: pieces within `AutoDimOpeningJoinMm` (20 mm) of each other are one opening, and only its two outer edges along the wall become jambs, so frame lines, sills, leaves and swing arcs do not add extra points. Which way the wall runs is taken from the nearest wall segment. |
| Columns | Closed polylines, or blocks (their extents). |
| Furniture | Blocks. Switched off with the other layers; **`AUTODIMROOM`** dimensions their width and depth. |

Below each list, **Pick from drawing…** closes the dialog, asks you to select objects on that kind of layer (a wall, a window block, a column, a chair), ticks the layers of what you picked, and reopens the dialog; the layer names do not have to follow any standard. A layer belongs to one role, so picking it for one column unticks it from the others. The first time, layers are guessed from their names (`WALL`, `WIND`/`DOOR`/`OPEN`, `COL`, `FURN`); after that the last choice for the drawing is remembered in the drawing. The tick box at the bottom leaves only the wall, window, column and dimension layers on, so you see just the plan and its dimensions; **Restore Layers** (`MSHOW`) brings the rest back. A row of openings that touch (for example two windows with no gap) counts as one. Standalone lines on `MEASURE-DEDUCT` and lines on the grid layers (`AutoDimGridLayers`, default `AN-GRID;A-GRID`) are read whatever you choose.

Dimensions are created on `AN-DIMS` in the `HCW-WORKING` style (or the current style) and tagged. **Clear Auto Dims** (`AUTODIMCLEAR`) removes only those. All distances are plotted millimetres in the settings file, so the drawing looks the same on paper at any scale. You give the plot scale each time.

Each command reports what it read (wall points, opening points, grid lines, plan size) and, if it makes nothing, why. Sizes come from the drawing's units setting; when that would make the plan an implausible size (for example a drawing in metres marked as millimetres, or units unset) the command says so and asks which unit the drawing is really in, without changing the drawing. Walls may be lines, lightweight polylines or old 2D polylines.

**`AUTODIM`: outside chains.** Choose the layers, then the sides (All, Top, Bottom, Left, Right), the chains, and the plot scale. Four chains per side, nearest the plan first:

| Chain | Points used |
|---|---|
| Openings | Door and window jambs in the side's outer band, with the wall pieces between them |
| Structure | Wall faces and ends, and column faces, in the outer band |
| Grid | Lines on the grid layers |
| Overall | First to last point on that side |

Points within 5 mm merge, dimensions shorter than `AutoDimMinMm` plotted are skipped, and a chain that repeats the one inside it is left out.

**`AUTODIMROOM`: inside each room.** Choose the layers, then select the room outlines (closed polylines, such as `ROOM-RECT` or `MEASURE-FLOOR` outlines). For a rectangular room it draws, nearest each wall first, the wall's openings (corner, jamb, jamb, corner) where the wall has any, then the clear width along the bottom and the clear depth along the left. Other orthogonal outlines get one dimension per edge, inside the room. Angled edges are skipped and counted. Furniture blocks inside the room get their width (under the block) and depth (beside it).

**`AUTODIMWALL`: any angle.** Select wall segments (lines, polyline segments, arcs). Each straight segment gets an aligned dimension offset outward (away from the middle of the selection) or inward; each arc gets a radius dimension.

**Short dimensions.** A dimension too short for its text has the text moved to a second or third row, with a leader, in the direction away from the plan (or into the room), so texts do not overprint. The width of a character is `AutoDimTextWidthFactor` of the text height.

**Associative dimensions.** Each dimension that `AUTODIM`, `AUTODIMROOM` and `AUTODIMWALL` make is tied at both ends to a wall vertex: an end of a dimension along x hangs on a wall vertex with the same x (nearest the dimension), along y on one with the same y, and an angled dimension end on the vertex it sits on. While live updates are on (`HCWLIVE`), moving or stretching a wall moves the ends of its dimensions (and the measured value follows) when the command ends. Ends that match no wall vertex (an opening's jamb, a grid line, a column corner) stay where they are, and the command reports how many dimensions are tied. This is the plugin's own tracking, not the host's associative dimensioning, so it follows only changes to the lines and polylines it tied to, and breaks if you erase and redraw them. `AUTODIMCLEAR` removes the dimensions and room names.

**Openings from gaps.** A break at the same place in both faces of a straight wall, `AutoDimGapMinMm` (400) to `AutoDimGapMaxMm` (4000) wide and with the faces no more than `AutoDimWallMaxMm` (600) apart, is read as an opening, so a door drawn as a gap is dimensioned like a block (`AutoDimGapOpenings` 0 turns it off). A gap already given by a door or window block is not counted twice.

**Opening tags.** The tag text `HCWDOOR` and `HCWWINDOW` draw (`D1`, `W2` …) is written above the dimension of its opening, with the measurement below (`AutoDimOpeningTags` 0 turns it off).

**Rooms found for you.** At the room prompt of `AUTODIMROOM` press Enter instead of selecting outlines: every room is found from the wall lines on the wall layers (as `HCWROOMWALLS` does, bridging gaps up to `AutoDimRoomGapMm`, 1200). Orthogonal rooms that are not rectangles (an L, a room with an alcove) now get their openings along each edge, not just the edge length. Each room's name, read from the text inside it on the `AutoDimRoomNameLayers` layers (`ROOM-LABELS;A-ROOM-NAME`), is written under its dimensions (`AutoDimRoomNames` 0 turns it off).

**Spacing between chains.** The text rows of one chain now stop a text height short of the next chain's dimension line, so moved text no longer meets the chain outside it. When the chains are too close for even one row, the command says so (raise `AutoDimStepMm`).

### RCC staircase (`AECSTAIR`)

A parametric staircase generator, not a drawing macro: one set of inputs makes the plan and the section together, every dimension, count and level is worked out from them, and the inputs are kept in the drawing so one number can be changed later and both views redrawn.

`AECSTAIR` asks, with the last values as defaults:

| Input | Notes |
|---|---|
| Staircase type | **Single** flight, **Dog** (dog-legged), **U** (parallel flights with a well between), **L** (quarter turn) |
| Stair width, FFL to FFL height | |
| Total risers | Automatic (the count whose rise is nearest `StairPreferredRiseMm`, 165) or typed |
| Risers in the first flight | The second flight gets the rest. Risers and treads are separate: a flight of *n* risers has *n − 1* treads, because the top step is the landing or floor |
| Tread / going, landing length | The landing width is derived (two widths for dog-leg, two widths plus the well for U, one width for L) |
| Waist slab thickness, landing / floor slab thickness | Drawn as actual concrete in the section |
| Starting level, nosing | Starting level makes the section show real levels |
| U only: well width, **Open** or **Closed** well | An open well is drawn with the void cross |
| Second flight turns **Left** or **Right** | |
| Plot scale | Text, dimension distances and hatch follow it |

**Units.** You type lengths in the drawing's own units: millimetres in a metric drawing, inches in a feet or inches drawing (`StairInputUnit` in the settings can say `mm`, `cm`, `m`, `in` or `ft`). Everything is calculated in real millimetres and drawn in whatever units the drawing uses. Dimension text is written in the same way: whole millimetres, or feet and inches to 1/8 in. Levels read `+1.650` in a metric drawing and `+5'-5"` in an imperial one.

**The calculation and the checks** are printed before anything is drawn:

```
STAIRCASE CALCULATION (DogLeg)
  FFL to FFL            3300 mm
  Total risers          20
  Rise                  165 mm
  Tread (going)         270 mm
  2R + G                600 mm
  Flight 1              10 risers, 9 treads, length 2430 mm
  Flight 2              10 risers, 9 treads, length 2430 mm
  Intermediate level    +1.650
CHECKS
  Rise                  165 mm         OK
  2R + G                600 mm         OK
  Riser consistency     20 equal risers   OK
  Flight distribution   10 / 10        OK
  FFL closure           3300 = 3300    OK
  Landing length        1200 mm (width 1200 mm)   OK
```

**Quantities.** Whenever a staircase is generated or changed (`AECSTAIR`, `AECSTAIREDIT`) its quantities are worked out, listed on the command line and saved as the take-off `Stair ST-01`, so `MEXPORT` (CSV, or Xlsx with the other take-offs) writes them. They are: concrete in m3 (waist slab, steps, landing), shuttering in m2 (soffit, both sides of each flight, risers, landing soffit), finishes in m2 (treads, risers, landing) and skirting in metres. The method is stated in the Basis column of the export:

- Each flight is a waist slab, measured square to the slope, with a triangular step on every riser. The slope length is worked from the flight's run (treads × going) and its height (risers × rise).
- The landing is a flat slab: landing width × landing length × landing thickness. A single flight has none.
- Shuttering leaves out the free edges of the landing. Treads are one fewer than risers in each flight, the landing top being the last tread. These are quantities from the inputs, so check them against your own method before pricing.

**Reinforcement estimate.** With the quantities, an estimate of the steel is saved as the take-off `Stair ST-01 bars`: a bar schedule (mark, description, diameter, number, length each, total length, weight) with the total weight and the steel per m3 of concrete. Each flight has main bars along the slope (the slope length plus anchorage at each end) spaced across the width, and distribution bars across it; the landing has main bars along its length and distribution bars across it. Weight is diameter squared over 162 per metre. Bar sizes and spacings are inputs, not design: `StairMainBarDia` (12), `StairMainBarSpacing` (150), `StairDistBarDia` (8), `StairDistBarSpacing` (200), `StairCover` (25 mm at each end) and `StairAnchorageDia` (40 diameters), all in the settings file. It is one layer with no cranks, hooks or top steel, so use it as a quantity check against the structural drawing.

The intermediate level is `start level + risers in the first flight × rise`. The tool reports OK or CHECK against limits held in the settings (`StairMaxRiseMm`, `StairMinGoingMm`, `Stair2RGMinMm`, `Stair2RGMaxMm` …); it never decides a stair is acceptable, so put your office's code criteria there. A stair that cannot be drawn (fewer than 2 risers in a flight) is refused. Choose **Generate** or **Change**.

**What is drawn.** You pick the first riser (right-hand corner looking up the stair), the direction of the first flight, and the foot of the first riser for the section.
- **Plan:** flight outlines, riser lines, nosing lines, landing, the well of a U, direction arrows marked UP, flight and landing labels with levels, and dimensions: width, "9 x 270 = 2430" for each flight, landing length and width.
- **Section:** the RCC as real geometry: the steps, the soffit (the waist thickness measured perpendicular to the flight, so parallel to the nosing line), the landing slab, the upper floor slab and the lower floor slab, hatched. FFL, intermediate and upper level marks, and dimensions for one riser, one tread, the flight, the landing and floor to floor. The waist and landing thicknesses are labelled and the pitch is shown. A dog-leg or U section returns over the first flight; an L is developed in a straight line.

Everything is on `AECSTAIR-*` layers (`AN-DIMS` for dimensions) and tagged with the stair's ID (`ST-01`).

**More in the section and the plan.** A **headroom line** over each flight (setting `StairHeadroomLine`; `StairHeadroomMm`, 2000, measured vertically above the line through the nosings) with its dimension; a **handrail, end posts and balusters** (`StairRailing`, `StairHandrailMm` 900, `StairPostMm` 50, `StairBalustersPerTread` 2); and, with `StairRebarDrawing` set to 1, the **main bars along each flight and the distribution bars as dots**, using the bar sizes and spacings of the estimate. The headroom, handrail height and bar figures are inputs, not code values.

**Winders.** **Landing or winders** is asked for L and U stairs. An L's quarter landing becomes three kite winders in the corner (lines at 30 and 60 degrees from the inner corner; the walkline going is the quarter circle on a half-width radius divided by three). A U's landing becomes **six winders**: three in each corner of a zone one stair width long, with the walkline running a quarter circle round the first flight's inner corner, straight across the well, and a quarter circle round the second flight's inner corner, divided into six equal goings (so the going is (half a circle's quarter-turns plus the well) over six; with no well it is the same as an L's). The plan draws the five division lines (diagonals in the bends, square to the walkline across the well), and the winders take five risers from the flights (an L's take two). The section is developed in a straight line, as for an L. The checks, quantities and bar estimate use the winder going; they work the winders as a slab one stair width wide along the walkline, which is an approximation at the inner corners. Dog-leg stairs keep landings; for dog-leg and U landings you can type a **landing width** other than the width of both flights (not with winders).

**Section as cut or developed.** For an L stair, **Section [Developed/Cut]**: Developed is the straight developed section; Cut shows the first flight as cut and the second flight beyond it, outlined and labelled BEYOND.

**Several floors.** `AECSTAIR` asks the number of floors and draws one stair per floor, the level rising by the floor-to-floor height each time, the plans stepping up the sheet and the sections stacked, each with its own ID. Quantities are saved for each.

**`AECSTAIRQTY`.** Works out the quantities and the bar estimate from the inputs alone, for a stair drawn some other way: it asks the same questions as `AECSTAIR`, then a name, and saves the take-offs `Stair NAME` and `Stair NAME bars` for `MEXPORT`. The shuttering now includes the edges of the landing; `StairLandingWallEdgeMm` removes the length that is against a wall.

**`AECSTAIREDIT`** — select any part of a stair, change any input (for example FFL 3300 to 3600), and both views are erased and redrawn in the same place, with the same ID.

### Electrical layout

Number the electrical blocks, draw the wiring as ordinary lines and polylines, and the tool works out which points are wired to which board and draws the connection schedule. It is part of the plugin (not a LISP routine), so it works the same in AutoCAD, BricsCAD and ZWCAD.

**Blocks.** `ELBLOCKS` opens a dialog with the items on the left and every block in the drawing on the right. Select an item, tick the blocks that are it, or use **Pick from drawing…** to select a block in the drawing and have its name ticked for you. The items, and the prefix of their IDs:

| Item | ID | Item | ID | Item | ID |
|---|---|---|---|---|---|
| Switchboard | `SB` | One way switch | `SW1` | Air conditioner | `AC` |
| Light point | `LP` | Two way switch | `SW2` | Water purifier | `WP` |
| Fan point | `FP` | Calling bell | `CB` | Geyser | `GY` |
| 5 amp socket | `P5` | 15 amp socket | `P15` | Fridge, Oven, WiFi router, TV | `FR`, `OV`, `WF`, `TV` |

A block has one role. The choice is saved in the drawing, so the blocks can have any names. Until you choose, the codes themselves are the block names (`SB`, `LP`, `GY` …; settings `ElectricalBoardBlocks`, `ElectricalLightBlocks`, `ElectricalFanBlocks`, `ElectricalBlocks_<code>`; `;` separates several, `*` matches anything). If the drawing has no block by those names and nothing is mapped, the commands below open the dialog themselves. Dynamic blocks are matched on their own name.

| Command | What it does |
|---|---|
| `ELBLOCKS` | Map which blocks are switchboards, light points and fan points (see above). |
| `ELNUM` (and `SBNUM`, `LPNUM`, `FPNUM` for one item) | Give every block an ID and write it beside the block as text: `SB-01`, `LP-01`, `GY-01`. `ELNUM` numbers every item that has blocks. Choose **Keep** (blocks keep the ID they have, new blocks get the next numbers) or **All** (start again from 1, left to right then bottom to top). |
| `ELLAYERS` | Choose the layers the wiring is drawn on: one list for lighting wires (lights, fans, switches, calling bell), one for power wires (sockets and appliances). **Pick from drawing…** takes the layers of objects you select. Saved in the drawing. Asked automatically the first time it is needed. |
| `ELCONNECT` | Works out the connections and prints them: per wiring group, the items, boards and circuits; items wired to more than one board; items not wired to any board; boards with nothing wired to them. Circles on `EL-CHECK` flag what is not wired. |
| `ELSCHEDULE` | Draws the schedule as a table. Choose the layout (**Matrix**, **Board**, **Point**, **Load**, **Circuit**, **Cable**, **Boq** or **Switch**), for the matrix whether cells show **Numbers** or **Counts**, the plot scale, and pick the top-left corner. |
| `ELUPDATE` | After you change the drawing: numbers new blocks, moves the ID text, re-checks the wiring and redraws every schedule already in the drawing, in place and at the same text size. |

**The ID stays with the block.** It is stored on the block itself (extended data), not only as text, so it follows the block when it is moved or the wiring is redrawn. If the block has attributes `ID`, `NUMBER` and `TAG`, they are filled in too (`LP-01`, `01`, `LP`). The ID is also written as text beside every block on `EL-LABELS`, so each one can be identified on the drawing (unless the block already shows a visible `ID` attribute); the text follows the block when you run any of the commands above. A **copied block carries its old ID with it**, so a repeated ID is renumbered (the one first in reading order keeps it).

**How a connection is found.** From geometry only, no reading of the picture:

1. A wire (line, polyline, arc, spline) reaches a block when one of its vertices is within `ElectricalSnapMm` (default 100 mm real size) of the block's extents.
2. Wires that meet end to end, or in a T, are one run. Wires that only cross are not joined.
3. A **point is a junction**: all wires that reach the same light or fan point are one circuit, so a run from one light to the next carries on to the board.
4. A **board is a terminal**: two runs that only meet at a board are separate circuits.

So `LP-01 ── LP-02 ── SB-01` gives both lights on `SB-01`, and a light wired to two boards is a 2 way point without joining the two boards' other lights.

**The schedules.** The default **Matrix** layout is the typical electrical table: a row per switchboard, a column per kind of point, each cell the numbers of the points wired to that board, and a total row (each point counted once, even if it is wired to two boards):

| SB no | 5 Amp | 15 Amp | One way switches | 2 way switches | AC | Water purifier | Geyser | Fridge | Oven | WiFi router | Calling bell | TV |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| SB-01 | LP-01, LP-02, FP-01 | P15-01 | SW1-01, SW1-02 | SW2-01 | AC-01 | | GY-01 | | | WF-01 | CB-01 | TV-01 |
| SB-02 | LP-03, FP-02 | | SW1-03 | SW2-02 | | WP-01 | | FR-01 | OV-01 | | | |
| TOTAL | 5 | 1 | 3 | 2 | 1 | 1 | 1 | 1 | 1 | 1 | 1 | 1 |

The **5 Amp** column lists the light points, fan points and 5 amp sockets. The columns are the setting `ElectricalColumns` (`Heading=codes;…`), so you can reorder, rename, or add a column such as `Light points=LP`. A board with nothing wired to it is still listed. **Board** layout: one row per board and point (`SB no`, `Point`, `Type`, `Connection`), where the connection is `Direct` for a point wired to one board and `2 Way`, `3 Way` … for more. **Point** layout: one row per point with its type and its boards. A point, switch or appliance is a junction and a board a terminal, so a switch wired to a light that is wired to a board is counted on that board. Lighting and power wiring are read from their own layers. Tables are drawn on `EL-TABLE`.

**The load schedule.** Choosing **Load** in `ELSCHEDULE` draws the connected load per switchboard from the wiring: lighting points and watts, power points and watts, total watts and kilowatts, and the number of circuits (lighting + power). `ELUPDATE` redraws it like the other schedules.

- The watts per point come from `ElectricalWatts` (`LP=15;FP=60;P5=100;P15=1000;AC=1500;…`). The figures it starts with are typical; set the ones for your job. A kind at 0 (the switches) is not counted as a load point.
- **Circuits** are numbered per board. Lighting points are packed in point order into `L1`, `L2` … each holding at most `ElectricalLightingCircuitW` watts (1000 to start with; a single point over the limit has a circuit to itself). Every point of a kind in `ElectricalDedicated` (`AC;GY;OV` to start with) gets its own power circuit, `P1`, `P2` …, then the rest of the power points are packed into shared circuits up to `ElectricalPowerCircuitW` (3000). Both limits are only starting values: use your own design rules. A limit of 0 puts the group on one circuit.
- **Demand** applies diversity factors to the connected load: `ElectricalDiversity` (`LT=1;PW=1` to start with, meaning none; set for example `LT=0.8;PW=0.6` from your own design rules). The Demand kW column shows it.
- A point wired to two boards counts in the load of both (as in the matrix); the TOTAL row counts each point once.
- **Circuit** layout: choosing **Circuit** in `ELSCHEDULE` draws one row per circuit with its board, name (`SB-01/L1`), type, the points on it (count and IDs) and its load in watts.
- Exhaust fans (`EF`, lighting group) and inverters (`INV`, power group) are kinds like the others: map their blocks with `ELBLOCKS` or set `ElectricalBlocks_EF` / `ElectricalBlocks_INV`; they have their own columns in the matrix. The inverter starts at 0 W (it is a source, not a load); set what you need in `ElectricalWatts`.
- Per-point rating: a block with a `WATTS` attribute uses that value for itself instead of its kind's figure (two geysers of different size).
- Breakers, cables and phases are in the Circuit layout below.

**Circuit sizing.** The **Circuit** layout also gives, for each circuit, the current (watts over `ElectricalVoltage`, 230, and `ElectricalPowerFactor`, 1), the breaker (the smallest rating in `ElectricalBreakers` that carries the current times `ElectricalBreakerMargin`, 1.25; a circuit over the largest rating is flagged), the cable size in mm2 from `ElectricalCableTable` (`amps=mm2` pairs), the **phase** (with `ElectricalPhases` set to 3, each board's circuits are spread over R, Y and B so the load is as even as possible) and the cable length of the circuit in metres (its own wiring, with the drop and allowance below). The breaker ratings, cable sizes, margin and voltage are starting values for a template: they are not a design, so set them from your own rules and check them.

**Switch control.** Choosing **Switch** in `ELSCHEDULE` lists each one way and two way switch with its type and the lights and fans it controls: the points on the same wiring run as the switch, without passing through a board.

**The cable bill of quantities.** Choosing **Boq** in `ELSCHEDULE` totals the cable by size: for each cable size (from the circuit schedule), the number of cores (`ElectricalCores`, 3), the number of circuits and the length in metres.

**Your own kinds.** `ElectricalExtraKinds` adds kinds of your own as `CODE:Label:GROUP` separated by `;` (GROUP is `LT` for lighting wiring or `PW` for power), for example `HT:Heater:PW;MS:Motion sensor:LT`. Give their block names in `ElectricalBlocks_<code>`, their watts in `ElectricalWatts`, and add a column in `ElectricalColumns` if you want one in the matrix.

**Wires through a symbol.** A wire that runs straight through a symbol's extents (not only one with a vertex near it) now connects to it.

**The cable schedule.** Choosing **Cable** in `ELSCHEDULE` draws the length of wiring for each switchboard, in metres, as lighting wiring, power wiring and total, with a TOTAL row. `ELUPDATE` redraws it. The length of each run comes from the wiring lines themselves (arcs by their true length), so it is only as good as the wiring you drew.

- For each run of connected wires: its drawn length, plus `ElectricalDropMm` (millimetres, 0 to start with) for every point on it, to cover the drop to a switch or the rise to a fitting; then `ElectricalCableAllowancePct` (10 % to start with) for bends, slack and waste. Both are only starting values: set them from your own practice. With the drop at 0, vertical lengths are not in the figure.
- A run that reaches two boards is shared equally between them. Wiring that reaches no board is left out and its length is shown in the title, so you can see what is missing.
- This is the length of wire drawn in plan; the cable size and conductor count come from the circuit schedule and the bill of quantities.

Settings (see `HCWSETTINGS`): the default block names, `ElectricalColumns`, the cable schedule settings (`ElectricalCableAllowancePct`, `ElectricalDropMm`), the load schedule settings (`ElectricalWatts`, `ElectricalDedicated`, `ElectricalLightingCircuitW`, `ElectricalPowerCircuitW`), `ElectricalSnapMm`, `ElectricalLabels` (0 turns the ID text off), `ElectricalLabelLayer`, `ElectricalLabelHeightMm` (0 sizes the text from the block, at least 150 mm real size), `ElectricalTableTextMm`.

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
| `BPLTLAYERS` | The **HCW + Building Permit** choice in the dropdown. Creates the HCW standard layers plus the BP- drafting layers, without changing drawing units. Text, dimensions, north point and revisions use the `AN-` layers. The title block commands make their own `BP-SHEET-BORDER`, `BP-TITLE-BLOCK`, `BP-NOTES` and `BP-FIELDS` layers when they run. |

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

`BPLTSTART` and **Export Report** are on this tab.

| Command | What it does |
|---|---|
| `BPLTSTART` | Creates every BP- layer, then sets metres, decimal units, and default dimension text and arrow size. |
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

**BPLT** — `BP-` layers for the drawing (site, building, parking, services; text and dimensions use the `AN-` layers; the title block makes its own sheet layers). `BPLTREPORT` lists every name and whether it exists.

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
