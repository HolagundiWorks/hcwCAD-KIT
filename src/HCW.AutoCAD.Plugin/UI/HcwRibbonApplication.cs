using System;
using System.Windows.Media.Imaging;
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
            toolsTab.Panels.Add(BuildSetupPanel());
            toolsTab.Panels.Add(BuildBpltPanel());
            toolsTab.Panels.Add(BuildRoomToolsPanel());
            toolsTab.Panels.Add(BuildMeasurePanel());
            toolsTab.Panels.Add(BuildAreaTextPanel());

            var settingsTab = new RibbonTab { Title = "hcwCAD-KIT Settings", Id = SettingsTabId };
            rc.Tabs.Add(settingsTab);
            settingsTab.Panels.Add(BuildLayerMaintenancePanel());
            settingsTab.Panels.Add(BuildRoomSettingsPanel());
            settingsTab.Panels.Add(BuildMeasureSettingsPanel());
            settingsTab.Panels.Add(BuildBpltReportsPanel());
            settingsTab.Panels.Add(BuildTextSettingsPanel());

            rc.ActiveTab = toolsTab;
        }

        // ==================== hcwCAD-KIT (commands) ====================

        private RibbonPanel BuildSetupPanel()
        {
            var src = NewSource("Setup");
            AddLarge(src, "HCWLAYERS", "HCW\nLayers", "layers", "Create/verify the 36-layer HCW Layer Standard v4.0");
            AddLarge(src, "VHLAYERS", "VH\nLayers", "layers--external", "Create the legacy VHLAYERS underscore-style layer set");
            AddLarge(src, "BPLTSTART", "BPLT\nStart", "flag", "Set up the drawing for Building Permission submission");
            return Wrap(src);
        }

        private RibbonPanel BuildBpltPanel()
        {
            var src = NewSource("Building Permission (BPLT)");
            AddLarge(src, "BPLTLAYERS", "BP/AP\nLayers", "layers", "Create/verify all BP- and AP- submission layers");
            AddLarge(src, "BPLTTITLEBLOCK", "Title\nBlock", "document--horizontal", "Insert an A1/A2/A3/A4 sheet border and title block");
            AddSmallGroup(src,
                ("BPLTCOPY", "Copy to AP-", "copy", "Duplicate selected BP- entities onto their matching AP- layer"));
            return Wrap(src);
        }

        /// <summary>
        /// One Room Labels panel. Buttons run the HCWROOM* commands, which
        /// read the drawing's INSUNITS. Typed M-/F-/I- commands are unchanged.
        /// </summary>
        private RibbonPanel BuildRoomToolsPanel()
        {
            var src = NewSource("Room Labels");
            AddLarge(src, "HCWROOM", "Pick Room\nType…", "home", "Pick a room type from a list and label the drawn rectangle (auto-detects the drawing's unit)");
            AddLarge(src, "HCWCUSTOMROOM", "Custom\nRoom", "tag--edit", "Type a custom room name and label the drawn rectangle (auto-detects the drawing's unit)");
            AddSmallGroup(src,
                ("HCWROOMAREA", "Area Label", "area", "Add an area label at the centroid of a selected polyline"),
                ("HCWROOMRELABEL", "Relabel", "tag--edit", "Change the room type of an existing label"));
            return Wrap(src);
        }

        private RibbonPanel BuildMeasurePanel()
        {
            var src = NewSource("Measure (Manual Take-off)");
            AddLarge(src, "MLIN", "Linear +\nDeduct", "ruler", "Linear take-off with deduction-line matching");
            AddLarge(src, "MBRK", "Brickwork", "grid", "Full/Half brick linear take-off with deductions");
            AddLarge(src, "MBML", "Beams &\nLintels", "horizontal-line--solid", "Beam/Lintel linear take-off with deductions");
            AddLarge(src, "MREC", "Columns\n(Rect)", "column", "Number and dimension rectangular columns");
            AddLarge(src, "MARE", "Area", "area", "Generic closed-shape area and perimeter take-off");
            AddLarge(src, "MSLB", "Slab +\nDeduct", "floorplan", "Slab area take-off with opening deductions");
            return Wrap(src);
        }

        private RibbonPanel BuildAreaTextPanel()
        {
            var src = NewSource("Area & Text Tools");
            AddLarge(src, "POLYAREA", "Poly\nArea", "area--custom", "Number selected polylines and draw a running-total area table");
            AddLarge(src, "DELETEAREATEXT", "Delete Area\nText", "trash-can", "Bulk-delete \"Area: ...\" text objects");
            AddSmallGroup(src,
                ("TextIncrement", "Increment Copy", "add--alt", "Copy text to picked points, auto-incrementing the trailing number"),
                ("WinLabel", "Window Label", "tag", "Label window blocks from their WNAME dynamic property"),
                ("TXTALIGN", "Align Text", "text--align--left", "Align selected TEXT objects to a reference point/axis"),
                ("TXTDUP", "Find Duplicates", "copy--file", "Find/remove TEXT objects with identical content and position"));
            return Wrap(src);
        }

        // ==================== hcwCAD-KIT Settings ====================

        private RibbonPanel BuildLayerMaintenancePanel()
        {
            var src = NewSource("Layer Maintenance");
            AddSmallGroup(src,
                ("HCWRESET", "Reset Layers", "reset", "Reset all HCW standard layers to their defined colour/linetype/lineweight"),
                ("HCWPURGE", "Purge All", "clean", "Run PURGE ALL twice to remove unused named objects"),
                ("HCWAUDIT", "Audit Layers", "checkmark--outline", "Check that all 36 standard layers are present"),
                ("HCWINFO", "Layer Info", "document--view", "List all 36 standard layers with colour/linetype/lineweight"),
                ("HCWLOCK", "Lock", "locked", "Lock the layer(s) of the selected objects"),
                ("HCWUNLOCK", "Unlock", "unlocked", "Unlock the layer(s) of the selected objects"),
                ("HCWAUDIT2", "Overrides", "rule--data-quality", "Report objects with explicit (non-ByLayer) colour/linetype"),
                ("HCWBYBLOCK", "Set ByLayer", "box", "Reset selected objects' colour/linetype back to ByLayer"),
                ("HCWMOVE", "Move to Layer", "move", "Move selected objects to a named layer"),
                ("HCWSCHEDULE", "Layer Schedule", "calendar", "Report object counts by layer"),
                ("HCWLAYERSTATE", "Layer State", "save", "Save or restore a named layer state"),
                ("HCWLEGEND", "Draw Legend", "table-of-contents", "Draw a full layer-standard legend/swatch table"));
            return Wrap(src);
        }

        private RibbonPanel BuildRoomSettingsPanel()
        {
            var src = NewSource("Room Tool Settings");
            AddSmallGroup(src,
                ("HCWROOMTH", "Text Height", "text--scale", "Set the room label text height"),
                ("HCWROOMSET", "Settings", "settings", "Show current room-tool settings, including the detected unit"),
                ("HCWROOMRESET", "Reset", "reset", "Reset room-tool settings to defaults"),
                ("HCWROOMRECT", "Toggle Rect", "square--outline", "Toggle whether a bounding rectangle is drawn with each label"),
                ("HCWROOMHIDERECT", "Hide Rects", "view--off", "Freeze/thaw the rectangle layer"),
                ("HCWROOMFLOOR", "Floor Prefix", "floorplan", "Set/clear a floor prefix (e.g. GF, FF) added to every label"),
                ("HCWROOMAUDIT", "Audit", "checkmark--outline", "Compare label and rectangle counts on the room-label layers"),
                ("HCWROOMCHECK", "Check Rects", "rule", "Verify all rectangles are closed"),
                ("HCWROOMSCHEDULE", "Export CSV", "calendar", "Export this session's room labels to CSV"),
                ("HCWROOMTOTAL", "Total Area", "report--data", "Total area of logged rooms, optionally filtered by type"),
                ("HCWROOMHELP", "Help", "help", "Show the detected drawing unit and how this panel works"));
            return Wrap(src);
        }

        private RibbonPanel BuildMeasureSettingsPanel()
        {
            var src = NewSource("Measure Settings");
            AddLarge(src, "MSETUP", "Unit\nSetup", "settings", "Choose Metric or Imperial for MEASURE (once per session)");
            AddSmallGroup(src,
                ("MSHOW", "Show All", "view", "Turn all layers back on"),
                ("MCLEAR", "Clear Labels", "clean", "Erase MEASURE label/table objects"));
            return Wrap(src);
        }

        private RibbonPanel BuildBpltReportsPanel()
        {
            var src = NewSource("BPLT Reports");
            AddSmallGroup(src,
                ("BPLTCHECK", "Check AP-", "rule", "Verify every required AP- layer has a closed polyline"),
                ("BPLTAREA", "Area Summary", "area", "Report gross area per AP- layer"),
                ("BPLTREPORT", "Export Report", "report", "Export the full BPLT layer report as CSV"));
            return Wrap(src);
        }

        private RibbonPanel BuildTextSettingsPanel()
        {
            var src = NewSource("Text Settings");
            AddLarge(src, "HCWSTYLES", "Create Std\nStyles", "text--font", "Create/update the HCW-SITE, HCW-WORKING and HCW-DETAIL text + dimension style tiers, sized to this drawing's real unit");
            AddSmallGroup(src,
                ("WinLabelHeight", "Label Height", "text--scale", "Set the window-label text height"),
                ("FIXTXT", "Fix Overlap (V)", "text--vertical-alignment", "Fix vertically overlapping text"),
                ("FIXTXTH", "Fix Overlap (H)", "text--align--justify", "Fix horizontally overlapping text"),
                ("TXTAUDIT", "Audit Overrides", "rule--data-quality", "Report TEXT/MTEXT with explicit colour overrides"),
                ("TXTEXPORT", "Export CSV", "document--export", "Export every TEXT/MTEXT object's content to CSV"),
                ("TXTSTYLE", "Set Style", "text--font", "List and switch the current text style"));
            return Wrap(src);
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
                Orientation = System.Windows.Controls.Orientation.Vertical,
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
                Orientation = System.Windows.Controls.Orientation.Horizontal,
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
