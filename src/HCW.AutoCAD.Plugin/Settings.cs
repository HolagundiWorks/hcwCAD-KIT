using System;
using System.Collections.Generic;
using System.IO;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>
    /// User settings kept in %APPDATA%\hcwCAD-KIT\settings.ini so text heights, tolerances and defaults
    /// are the same in every drawing and every host. The file is read once per session.
    /// </summary>
    public static class Settings
    {
        private static IniFile _ini;

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hcwCAD-KIT", "settings.ini");

        private static IniFile Ini
        {
            get
            {
                if (_ini != null) return _ini;
                try { _ini = File.Exists(FilePath) ? IniFile.Parse(File.ReadAllText(FilePath)) : new IniFile(); }
                catch { _ini = new IniFile(); }
                return _ini;
            }
        }

        public static string Get(string key, string fallback) => Ini.Get(key, fallback);
        public static double GetDouble(string key, double fallback) => Ini.GetDouble(key, fallback);
        public static int GetInt(string key, int fallback) => Ini.GetInt(key, fallback);

        /// <summary>Every key with its default value and a help line, used to write the file.</summary>
        private static readonly KeyValuePair<string, string[]>[] Known =
        {
            Entry("TakeoffTextHeightMm", "125", "Take-off label text height, in real-size millimetres (converted to the drawing's units)."),
            Entry("DeductionToleranceMm", "10", "How close a deduction line must be to its wall, in real-size millimetres."),
            Entry("SuggestToleranceCm", "5", "Deduction-map suggestions: allowed length difference in centimetres (Metric). 5 = 50 mm."),
            Entry("SuggestToleranceEighths", "16", "Deduction-map suggestions: allowed difference in eighths of an inch (Imperial). 16 = 2 in."),
            Entry("WallNumbering", "LeftRight", "Wall numbers FB01, FB02...: LeftRight, TopBottom or Path (asks for a path line)."),
            Entry("DefaultDoorHeight", "2.1", "Height given to a door row made by Add from blocks."),
            Entry("DefaultWindowHeight", "1.2", "Height given to a window row made by Add from blocks."),
            Entry("DefaultFflHeight", "3.15", "New floor: FFL to FFL height."),
            Entry("DefaultCeilingHeight", "3.0", "New floor: ceiling height."),
            Entry("DefaultLintelBottom", "2.1", "New floor: lintel bottom height."),
            Entry("AutoDimStepMm", "10", "AUTODIM: plotted distance between dimension chains, in millimetres."),
            Entry("AutoDimGapMm", "12", "AUTODIM: plotted distance from the plan to the first chain, in millimetres."),
            Entry("AutoDimMinMm", "3", "AUTODIM: dimensions shorter than this plotted length are skipped, in millimetres."),
            Entry("AutoDimBandM", "0.6", "AUTODIM: depth of the outer band (drawing metres) whose openings go on the outside chain."),
            Entry("AutoDimOpeningJoinMm", "20", "AUTODIM: window and door geometry pieces closer than this (real-size millimetres) are one opening."),
            Entry("AutoDimRoomInsetMm", "8", "AUTODIMROOM: plotted distance from the room's walls to its first dimension line, in millimetres."),
            Entry("AutoDimTextWidthFactor", "0.75", "AUTODIM: width of one character as a fraction of the text height, used to decide when a dimension is too short for its text."),
            Entry("AutoDimGridLayers", "AN-GRID;A-GRID", "AUTODIM: layers whose horizontal and vertical lines are read as the structural grid (separate with ;). Walls, windows, columns and furniture layers are chosen in the dialog."),
            Entry("AutoDimFurnitureOffsetMm", "6", "AUTODIMROOM: plotted distance from a furniture block to its width and depth dimensions, in millimetres."),
            Entry("ElectricalBoardBlocks", "SB", "Electrical: block names that are switchboards (separate with ;, * matches anything, for example SB*)."),
            Entry("ElectricalLightBlocks", "LP", "Electrical: block names that are light points."),
            Entry("ElectricalFanBlocks", "FP", "Electrical: block names that are fan points."),
            Entry("ElectricalSnapMm", "100", "Electrical: a wire reaches a block when one of its vertices is within this real-size distance (millimetres) of the block's extents."),
            Entry("ElectricalLabels", "1", "Electrical: 1 writes each block's ID beside it as text (blocks with an ID attribute show it there instead); 0 turns the text off."),
            Entry("ElectricalLabelLayer", "EL-LABELS", "Electrical: layer for the ID text."),
            Entry("ElectricalTableTextMm", "2.5", "Electrical: plotted text height of the schedules, in millimetres."),
            Entry("AutoDimStyle", "HCW-WORKING", "AUTODIM: dimension style used; the current style is used when it does not exist.")
        };

        private static KeyValuePair<string, string[]> Entry(string key, string value, string help)
            => new KeyValuePair<string, string[]>(key, new[] { value, help });

        /// <summary>Writes the settings file with every default if it does not exist yet.</summary>
        public static string EnsureFile()
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, IniFile.Format(Known));
            }
            return FilePath;
        }
    }
}
