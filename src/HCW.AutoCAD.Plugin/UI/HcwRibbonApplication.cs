using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.UI
{
    /// <summary>
    /// Builds the hcwCAD-KIT ribbon tabs on load:
    ///  - "hcwCAD-KIT"          - drawing and take-off commands
    ///  - "hcwCAD-KIT Settings" - setup, checks and reports
    /// Every button runs its AutoCAD command via a "_COMMAND " string sent as
    /// if typed, and uses the matching Carbon Design System icon loaded by
    /// <see cref="IconLoader"/>.
    /// </summary>
    public class HcwRibbonApplication : IExtensionApplication
    {
        private const string ToolsTabId = "HCW_TOOLS_TAB";
        private const string SettingsTabId = "HCW_SETTINGS_TAB";

        public void Initialize()
        {
            // The ribbon may not exist yet this early in AutoCAD startup -
            // build it once idle, same pattern Autodesk's own samples use.
            AcAp.Idle += BuildRibbonOnce;
        }

        public void Terminate() { }

        private void BuildRibbonOnce(object sender, EventArgs e)
        {
            AcAp.Idle -= BuildRibbonOnce;
            try { BuildRibbon(); }
            catch (System.Exception ex)
            {
                AcAp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\n[hcwCAD-KIT] ribbon build error: " + ex.Message);
            }
        }

        private void BuildRibbon()
        {
            var rc = ComponentManager.Ribbon;
            if (rc == null) return;

            foreach (RibbonTab existing in rc.Tabs)
                if (existing.Id == ToolsTabId) return; // already built (e.g. NETLOAD run twice)

            var toolsTab = new RibbonTab { Title = "hcwCAD-KIT", Id = ToolsTabId };
            rc.Tabs.Add(toolsTab);
            toolsTab.Panels.Add(BuildNotesPanel());
            toolsTab.Panels.Add(BuildRoomToolsPanel());
            toolsTab.Panels.Add(BuildMeasurePanel());
            toolsTab.Panels.Add(BuildAreaTextPanel());

            var settingsTab = new RibbonTab { Title = "hcwCAD-KIT Settings", Id = SettingsTabId };
            rc.Tabs.Add(settingsTab);
            settingsTab.Panels.Add(BuildLayerPanel());
            settingsTab.Panels.Add(BuildLayerChecksPanel());
            settingsTab.Panels.Add(BuildBpltSetupPanel());
            settingsTab.Panels.Add(BuildRoomChecksPanel());
            settingsTab.Panels.Add(BuildTextChecksPanel());
            settingsTab.Panels.Add(BuildPluginPanel());

            rc.ActiveTab = toolsTab;
        }

        // ==================== hcwCAD-KIT (commands) ====================

        /// <summary>
        /// One layer-set dropdown and one Create button. The dropdown only
        /// chooses which existing command the button will run.
        /// </summary>
        private RibbonPanel BuildLayerPanel()
        {
            var src = NewSource("Layers");
            AddSetButton(src, "HCW_LAYER_SET", "Create\nLayers", "layers",
                "Create or verify the layer set selected in the dropdown",
                new (string label, string command)[]
                {
                    ("HCW Standard", "HCWLAYERS"),
                    ("VH Layers", "VHLAYERS"),
                    ("BPLT Layers", "BPLTLAYERS")
                });
            AddSmallGroup(src,
                ("HCWRESET", "Reset Layers", "reset", "Reset HCW standard layers to their colour, linetype and lineweight"),
                ("HCWLOCK", "Lock", "locked", "Lock the layers of the selected objects"),
                ("HCWUNLOCK", "Unlock", "unlocked", "Unlock the layers of the selected objects"),
                ("HCWMOVE", "Move to Layer", "move", "Move selected objects to a named layer"),
                ("HCWBYBLOCK", "Set ByLayer", "box", "Set selected objects back to ByLayer colour and linetype"),
                ("HCWLEGEND", "Draw Legend", "table-of-contents", "Draw the HCW layer legend"));
            return Wrap(src);
        }

        private RibbonPanel BuildNotesPanel()
        {
            var src = NewSource("Notes");
            AddNotePicker(src);
            AddLarge(src, "FIELDS", "Fields", "tag--edit", "Create fields, edit a field, place them, or update a fields block on the sheet");
            AddSmallGroup(src,
                ("TITLENOTES", "Notes", "document--view", "Add a note set, add a note line, place or update notes"),
                ("TITLENOTESAVE", "Save Notes", "save", "Save the selected notes block under a name such as ELECTRIC NOTES"),
                ("FIELDSEDIT", "Edit Field", "tag--edit", "Edit the fields block already on the sheet"));
            return Wrap(src);
        }

        private static void AddNotePicker(RibbonPanelSource src)
        {
            TitleNoteLibrary.Ensure();
            var names = TitleNoteLibrary.Names();
            var combo = new RibbonCombo { Id = "HCW_NOTE_SET", Width = 160, ToolTip = "Saved note set to place" };
            foreach (var name in names)
                combo.Items.Add(new RibbonButton { Text = name, ShowText = true, CommandParameter = name });
            if (combo.Items.Count > 0)
            {
                combo.Current = combo.Items[0];
                TitleNoteLibrary.Current = names[0];
            }
            var place = new RibbonButton
            {
                Text = "Place\nNotes",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
#if !BRX
                Orientation = System.Windows.Controls.Orientation.Vertical,
#endif
                LargeImage = IconLoader.Large("document--view"),
                Image = IconLoader.Small("document--view"),
                ToolTip = "Drop the selected note set on the sheet. Move it afterwards with MOVE.",
                CommandParameter = "_TITLENOTE ",
                CommandHandler = RibbonCommandHandler.Instance
            };
            combo.CurrentChanged += (s, e) =>
            {
                if (combo.Current is RibbonButton item)
                    TitleNoteLibrary.Current = item.Text;
            };
            src.Items.Add(combo);
            src.Items.Add(place);
        }

        /// <summary>
        /// Room labels. Units come from the drawing. Text height, floor prefix and the rectangle toggle sit on this panel.
        /// </summary>
        private RibbonPanel BuildRoomToolsPanel()
        {
            var src = NewSource("Room Labels");
            AddLarge(src, "ROOM", "Room", "home", "Pick a room type, draw its rectangle from two corners, and place the label in the centre. Units follow the drawing.");
            AddLarge(src, "ROOMC", "Custom\nRoom", "tag--edit", "Type a room name, then pick two corners. Units follow the drawing.");
            AddSmallGroup(src,
                ("RAREA", "Area Label", "area", "Add an area label at the centre of a selected polyline"),
                ("RTAG", "Relabel", "tag--edit", "Change the room type of an existing label"),
                ("HCWROOMTH", "Text Height", "text--scale", "Set the room label text height"),
                ("HCWROOMFLOOR", "Floor Prefix", "floorplan", "Set or clear a floor prefix such as GF or FF"),
                ("HCWROOMRECT", "Toggle Rect", "square--outline", "Draw or skip the room rectangle"),
                ("HCWROOMHIDERECT", "Hide Rects", "view--off", "Freeze or thaw the rectangle layer"),
                ("HCWROOMRESET", "Reset", "reset", "Reset room label settings to defaults"));
            return Wrap(src);
        }

        private RibbonPanel BuildMeasurePanel()
        {
            var src = NewSource("TakeOff");
            AddLarge(src, "TOSTART", "Start", "flag", "Read the drawing units and create the take-off layers");
            AddSmallGroup(src,
                ("MLIN", "Linear", "ruler", "Length of lines on MEASURE-LINEAR, less openings on MEASURE-DEDUCT"),
                ("MBRK", "Brickwork", "box", "Full brick and half brick, grouped by equal length"),
                ("MBML", "Beams", "column", "Concrete beams and lintels, grouped by equal length"),
                ("MCOL", "Columns", "column", "Concrete columns of the same size share one mark"),
                ("MPAINT", "Wall paint", "area--custom", "Wall length times the floor height, less openings"),
                ("MCEIL", "Ceiling", "floorplan", "Ceiling paint, grouped by equal area"),
                ("MFLOOR", "Floor", "area", "Floor area, grouped by equal area"),
                ("MSCHED", "Schedule", "report", "Floors, doors, windows, columns, and the deduction name map"),
                ("MSCHEDTABLE", "Insert\nSchedule", "table-of-contents", "Draw the saved schedule as a table in the drawing"),
                ("MAREA", "Area", "area", "Closed-shape area and perimeter"),
                ("MSLAB", "Slab", "floorplan", "Slab area with opening deductions"),
                ("MSHOW", "Restore Layers", "view", "Turn back on only the layers a take-off command hid"),
                ("MEXPORT", "Export", "document--export", "Save the last take-off as CSV, or every take-off as one Excel workbook"),
                ("MEXPORTX", "Export Excel", "document--export", "Save every take-off as one Excel workbook, with a Bill sheet for rated take-offs"),
                ("MCLEAR", "Clear Labels", "clean", "Erase take-off label and table objects"));
            return Wrap(src);
        }

        private RibbonPanel BuildAreaTextPanel()
        {
            var src = NewSource("Area & Text Tools");
            AddLarge(src, "POLYAREA", "Poly\nArea", "area--custom", "Number selected polylines and draw a running-total area table");
            AddLarge(src, "HCWSTYLES", "Text\nStyles", "text--font", "Create HCW-SITE, HCW-WORKING and HCW-DETAIL text and dimension styles");
            AddSmallGroup(src,
                ("INCARRAY", "Inc Array", "add--alt", "Array the selection and increment every number in the copied text, attributes and dimensions"),
                ("INCCOPY", "Inc Copy", "copy", "Copy the selection to picked points, adding the increment to every number in each copy"),
                ("RENUMBERLAYOUTS", "Renumber Layouts", "table-of-contents", "Renumber paper layouts in tab order, with a prefix, suffix and digit padding"),
                ("AUTODIM", "Auto Dimension", "ruler", "Dimension chains around the plan: openings, structure, grid and overall"),
                ("AUTODIMROOM", "Room Dimensions", "ruler", "Clear width and depth of each room, and door and window positions along its walls"),
                ("AUTODIMWALL", "Wall Dimensions", "ruler", "Aligned dimension on each wall segment at any angle, and radius on arcs"),
                ("AUTODIMCLEAR", "Clear Auto Dims", "clean", "Remove the dimensions the auto dimension tools made"),
                ("SHEETSET", "Sheet Set", "document--horizontal", "Make numbered sheets from a template layout, with each viewport at a scale and centred on a window"),
                ("WinLabel", "Window Label", "tag", "Label window blocks from their WNAME property"),
                ("WinLabelHeight", "Label Height", "text--scale", "Set the window-label text height"),
                ("TXTALIGN", "Align Text", "text--align--left", "Align selected TEXT to a reference point"),
                ("FIXTXT", "Fix Overlap (V)", "text--vertical-alignment", "Separate text that overlaps vertically"),
                ("FIXTXTH", "Fix Overlap (H)", "text--align--justify", "Separate text that overlaps horizontally"),
                ("TXTSTYLE", "Set Style", "text--font", "List text styles and set the current one"),
                ("TXTDUP", "Find Duplicates", "copy--file", "Find or remove TEXT with the same content and position"),
                ("DBCOUNT", "Count Blocks", "report", "Count blocks in this layout, including dynamic-block visibility states"),
                ("DGRID", "Draw Grid", "grid", "Draw a row and column grid between two corners"),
                ("AUTOLABEL", "Label Blocks", "tag--edit", "Number a chosen attribute on matching blocks in this layout"),
                ("AREAFIELD", "Area Field", "area", "Place a live area field, or drop it into a table cell"),
                ("AREALABEL", "Area Labels", "area--custom", "Number picked areas and list them in a live table or a file"));
            return Wrap(src);
        }

        // ==================== hcwCAD-KIT Settings ====================

        private RibbonPanel BuildLayerChecksPanel()
        {
            var src = NewSource("Layer Checks");
            AddSmallGroup(src,
                ("HCWAUDIT", "Audit Layers", "checkmark--outline", "Check that all 36 standard layers are present"),
                ("HCWAUDIT2", "Overrides", "rule--data-quality", "Report objects whose colour or linetype is not ByLayer"),
                ("HCWINFO", "Layer Info", "document--view", "List the standard layers with colour, linetype and lineweight"),
                ("HCWSCHEDULE", "Layer Schedule", "calendar", "Count objects on each layer"),
                ("HCWLAYERSTATE", "Layer State", "save", "Save or restore a named layer state"),
                ("HCWPURGE", "Purge All", "clean", "Run PURGE All twice"));
            return Wrap(src);
        }

        private RibbonPanel BuildRoomChecksPanel()
        {
            var src = NewSource("Room Checks");
            AddSmallGroup(src,
                ("HCWROOMSET", "Settings", "settings", "Show text height, layers, floor prefix and the detected unit"),
                ("HCWROOMAUDIT", "Audit", "checkmark--outline", "Compare label and rectangle counts"),
                ("HCWROOMCHECK", "Check Rects", "rule", "Verify room rectangles are closed"),
                ("HCWROOMSCHEDULE", "Export CSV", "calendar", "Export the room labels in this drawing to CSV"),
                ("HCWROOMTABLE", "Room Table", "table-of-contents", "Draw the room schedule as a table"),
                ("HCWROOMTOTAL", "Total Area", "report--data", "Total area of the rooms labelled in this drawing"),
                ("HCWROOMHELP", "Help", "help", "How ROOM reads the drawing units"));
            return Wrap(src);
        }

        private RibbonPanel BuildBpltSetupPanel()
        {
            var src = NewSource("BPLT");
            AddLarge(src, "BPLTSTART", "BPLT\nStart", "flag", "Set metres and create the BP- and AP- submission layers");
            AddSmallGroup(src,
                ("BPLTCOPY", "Copy to AP-", "copy", "Duplicate selected BP- entities onto their matching AP- layer"),
                ("BPLTCHECK", "Check AP-", "rule", "Verify every required AP- layer has a closed polyline"),
                ("BPLTAREA", "Area Summary", "area", "Report gross area per AP- layer"),
                ("BPLTREPORT", "Export Report", "report", "Export the full BPLT layer report as CSV"));
            return Wrap(src);
        }

        private RibbonPanel BuildPluginPanel()
        {
            var src = NewSource("Plugin");
            AddLarge(src, "HCWSETTINGS", "Settings\nFile", "settings", "Open settings.ini: text heights, tolerances, numbering order and defaults");
            return Wrap(src);
        }

        private RibbonPanel BuildTextChecksPanel()
        {
            var src = NewSource("Text Checks");
            AddSmallGroup(src,
                ("TXTAUDIT", "Audit Overrides", "rule--data-quality", "Report TEXT and MTEXT with an explicit colour"),
                ("TXTEXPORT", "Export CSV", "document--export", "Export every TEXT and MTEXT object to CSV"));
            return Wrap(src);
        }

        /// <summary>Dropdown of named sets plus one button that runs the selected command.</summary>
        private static void AddSetButton(RibbonPanelSource src, string comboId, string buttonText, string icon, string tooltip, (string label, string command)[] sets)
        {
            var combo = new RibbonCombo { Id = comboId, Width = 150, ToolTip = "Choose which command the button runs" };
            foreach (var set in sets)
                combo.Items.Add(new RibbonButton { Text = set.label, ShowText = true, CommandParameter = set.command });
            combo.Current = combo.Items[0];

            var button = new RibbonButton
            {
                Text = buttonText,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
#if !BRX
                Orientation = System.Windows.Controls.Orientation.Vertical,
#endif
                LargeImage = IconLoader.Large(icon),
                Image = IconLoader.Small(icon),
                ToolTip = tooltip,
                CommandParameter = "_" + sets[0].command + " ",
                CommandHandler = RibbonCommandHandler.Instance
            };
            combo.CurrentChanged += (s, e) =>
            {
                if (combo.Current is RibbonButton item && item.CommandParameter is string command)
                    button.CommandParameter = "_" + command + " ";
            };

            src.Items.Add(combo);
            src.Items.Add(button);
        }

        // ---- low-level helpers ----

        private static RibbonPanelSource NewSource(string title) => new RibbonPanelSource { Title = title };

        private static RibbonPanel Wrap(RibbonPanelSource src) => new RibbonPanel { Source = src };

        private static void AddLarge(RibbonPanelSource src, string command, string text, string icon, string tooltip)
        {
            var btn = new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
#if !BRX
                Orientation = System.Windows.Controls.Orientation.Vertical,
#endif
                LargeImage = IconLoader.Large(icon),
                Image = IconLoader.Small(icon),
                ToolTip = tooltip,
                CommandParameter = $"_{command} ",
                CommandHandler = RibbonCommandHandler.Instance
            };
            src.Items.Add(btn);
        }

        private static RibbonButton MakeSmallButton(string command, string text, string icon, string tooltip)
        {
            return new RibbonButton
            {
                Text = text,
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
#if !BRX
                Orientation = System.Windows.Controls.Orientation.Horizontal,
#endif
                Image = IconLoader.Small(icon),
                ToolTip = tooltip,
                CommandParameter = $"_{command} ",
                CommandHandler = RibbonCommandHandler.Instance
            };
        }

        /// <summary>
        /// Stacks small buttons 3-per-column in RibbonRowPanels, the same
        /// layout convention AutoCAD's own panels use (e.g. Home &gt; Modify),
        /// instead of one full-height button per row.
        /// </summary>
        private static void AddSmallGroup(RibbonPanelSource src, params (string command, string text, string icon, string tooltip)[] items)
        {
            for (int i = 0; i < items.Length; i += 3)
            {
                var row = new RibbonRowPanel();
                int end = Math.Min(i + 3, items.Length);
                for (int j = i; j < end; j++)
                {
                    if (j > i) row.Items.Add(new RibbonRowBreak());
                    var (command, text, icon, tooltip) = items[j];
                    row.Items.Add(MakeSmallButton(command, text, icon, tooltip));
                }
                src.Items.Add(row);
            }
        }
    }
}
