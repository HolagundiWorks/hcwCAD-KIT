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
            Entry("ElectricalBoardBlocks", "SB", "Electrical: block names that are switchboards when none were chosen with ELBLOCKS (separate with ;, * matches anything, for example SB*)."),
            Entry("ElectricalLightBlocks", "LP", "Electrical: block names that are light points."),
            Entry("ElectricalFanBlocks", "FP", "Electrical: block names that are fan points."),
            Entry("ElectricalBlocks_SW1", "SW1", "Electrical: default block names for a one way switch (used until blocks are mapped with ELBLOCKS)."),
            Entry("ElectricalBlocks_SW2", "SW2", "Electrical: default block names for a two way switch."),
            Entry("ElectricalBlocks_CB", "CB", "Electrical: default block names for a calling bell."),
            Entry("ElectricalBlocks_P5", "P5", "Electrical: default block names for a 5 amp socket."),
            Entry("ElectricalBlocks_P15", "P15", "Electrical: default block names for a 15 amp socket."),
            Entry("ElectricalBlocks_AC", "AC", "Electrical: default block names for an air conditioner."),
            Entry("ElectricalBlocks_WP", "WP", "Electrical: default block names for a water purifier."),
            Entry("ElectricalBlocks_GY", "GY", "Electrical: default block names for a geyser."),
            Entry("ElectricalBlocks_FR", "FR", "Electrical: default block names for a fridge."),
            Entry("ElectricalBlocks_OV", "OV", "Electrical: default block names for an oven."),
            Entry("ElectricalBlocks_WF", "WF", "Electrical: default block names for a WiFi router."),
            Entry("ElectricalBlocks_TV", "TV", "Electrical: default block names for a TV."),
            Entry("ElectricalBlocks_EF", "EF", "Electrical: default block names for an exhaust fan."),
            Entry("ElectricalBlocks_INV", "INV", "Electrical: default block names for an inverter."),
            Entry("ElectricalColumns", Logic.ElectricalMatrix.DefaultColumns, "Electrical schedule: the columns after SB no, as Heading=codes separated by ; (codes: LP FP EF SW1 SW2 CB P5 P15 AC WP GY FR OV WF TV INV). Reorder, rename or regroup them here."),
            Entry("ElectricalWatts", Logic.ElectricalLoad.DefaultWatts, "Electrical load schedule: connected load in watts for each kind of point, as CODE=watts separated by ; (codes: LP FP EF SW1 SW2 CB P5 P15 AC WP GY FR OV WF TV INV). The figures are typical; set the ones for your job. A kind at 0 is not counted as load."),
            Entry("ElectricalDiversity", Logic.ElectricalLoad.DefaultDiversity, "Electrical load schedule: diversity factors, the share of connected load taken as demand, for lighting (LT) and power (PW), each from 0 to 1, as LT=0.8;PW=0.6. 1 means no diversity. Set them from your own design rules."),
            Entry("ElectricalDedicated", Logic.ElectricalLoad.DefaultDedicated, "Electrical load schedule: kinds that each get their own power circuit (separate with ;). The rest of the power load shares circuits up to ElectricalPowerCircuitW."),
            Entry("ElectricalLightingCircuitW", "1000", "Electrical load schedule: most connected load on one lighting circuit, in watts (0 = one lighting circuit per board). Set it from your own design rules."),
            Entry("ElectricalPowerCircuitW", "3000", "Electrical load schedule: most connected load on one shared power circuit, in watts (0 = one shared power circuit per board)."),
            Entry("ElectricalCableAllowancePct", "10", "Electrical cable schedule: allowance added to the drawn wire length for bends, slack and waste, in percent. Set it from your own practice."),
            Entry("ElectricalDropMm", "0", "Electrical cable schedule: real-size length added for each point on a run for the drop or rise to it (a switch, a fitting), in millimetres. 0 leaves drops out."),
            Entry("ElectricalExtraKinds", "", "Electrical: kinds of your own, as CODE:Label:GROUP separated by ; (GROUP is LT for lighting wiring or PW for power wiring), for example HT:Heater:PW;MS:Motion sensor:LT. Give their block names in ElectricalBlocks_CODE (or ELBLOCKS), their load in ElectricalWatts, and add a column for them in ElectricalColumns. Restart the host after editing."),
            Entry("ElectricalVoltage", "230", "Electrical circuit schedule: supply voltage used to turn each circuit's load into a current."),
            Entry("ElectricalPowerFactor", "1", "Electrical circuit schedule: power factor used for the current (1 = watts over volts)."),
            Entry("ElectricalBreakerMargin", "1.25", "Electrical circuit schedule: the breaker is chosen for the current times this margin. Set it from your own design rules."),
            Entry("ElectricalBreakers", "6,10,16,20,25,32,40,50,63", "Electrical circuit schedule: the breaker ratings (amps) to choose from. Set them from your supplier's range."),
            Entry("ElectricalCableTable", "6=1.5;10=1.5;16=2.5;20=4;25=4;32=6;40=10;50=10;63=16", "Electrical circuit schedule: cable size in mm2 for each breaker rating, as amps=size separated by ;. These are typical figures for a template, not a design: take them from your cable tables."),
            Entry("ElectricalCores", "3", "Electrical cable bill of quantities: the number of cores written beside each cable size."),
            Entry("ElectricalPhases", "1", "Electrical circuit schedule: 1 leaves circuits unassigned, 3 spreads each board's circuits over phases R, Y and B so the load is as even as possible."),
            Entry("ElectricalSnapMm", "100", "Electrical: a wire reaches a block when one of its vertices is within this real-size distance (millimetres) of the block's extents."),
            Entry("ElectricalLabels", "1", "Electrical: 1 writes each block's ID beside it as text (blocks with an ID attribute show it there instead); 0 turns the text off."),
            Entry("ElectricalLabelLayer", "EL-LABELS", "Electrical: layer for the ID text."),
            Entry("ElectricalLabelHeightMm", "0", "Electrical: real-size height of the ID text in millimetres; 0 sizes it from the block (at least 150 mm)."),
            Entry("ElectricalTableTextMm", "2.5", "Electrical: plotted text height of the schedules, in millimetres."),
            Entry("AreaFarPermittedPercent", "0", "HCWAREASTMT: permissible floor area ratio as a percentage of the site (125 for an FAR of 1.25). Fills FAR_PERM and flags a statement that goes over it. 0 leaves it for you to type. Take it from the bylaws for the plot."),
            Entry("AreaGroundCoverPermittedPercent", "0", "HCWAREASTMT: permissible ground cover as a percentage of the site. Fills GC_PERM (in square metres) and flags a statement that goes over it. 0 leaves it for you to type."),
            Entry("StairInputUnit", "Auto", "AECSTAIR: unit of the numbers you type: Auto (mm in a metric drawing, in in a feet or inches drawing), mm, cm, m, in or ft."),
            Entry("StairPreferredRiseMm", "165", "AECSTAIR: the rise the automatic riser count aims for, in millimetres."),
            Entry("StairMaxRiseMm", "190", "AECSTAIR check: largest rise, in millimetres."),
            Entry("StairMinRiseMm", "100", "AECSTAIR check: smallest rise, in millimetres."),
            Entry("StairMinGoingMm", "250", "AECSTAIR check: smallest going (tread), in millimetres."),
            Entry("Stair2RGMinMm", "550", "AECSTAIR check: smallest value of 2 x rise + going, in millimetres."),
            Entry("Stair2RGMaxMm", "700", "AECSTAIR check: largest value of 2 x rise + going, in millimetres."),
            Entry("StairHeadroomLine", "1", "AECSTAIR section: 1 draws a headroom line over each flight, 0 leaves it out."),
            Entry("StairHeadroomMm", "2000", "AECSTAIR section: the headroom, measured vertically above the line through the nosings, in millimetres. Set it from your own code."),
            Entry("WallJoinOnDraw", "1", "HCWWALL: 1 merges a new wall with the wall outlines it touches, 0 leaves each wall as its own outline."),
            Entry("LiftTable", "6=1100x1400:1800x1900:800; 8=1350x1400:2000x1900:800; 10=1500x1500:2100x2000:900; 13=1800x1500:2400x2000:1000", "HCWLIFT: lift sizes by capacity, as persons=car:shaft:door in mm, separated by semicolons. The starting rows are typical; use your lift maker's."),
            Entry("LiftMachineMarginMm", "1000", "HCWLIFT: how far the machine room outline extends beyond the shaft wall on every side, in millimetres."),
            Entry("EscalatorAngleDeg", "30", "HCWESCALATOR: the angle offered first, in degrees (30, or 35 up to 6 m rise)."),
            Entry("EscalatorStepWidthMm", "1000", "HCWESCALATOR: the nominal step width offered first, in millimetres."),
            Entry("EscalatorLandingMm", "2500", "HCWESCALATOR: the flat landing length at each end, in millimetres."),
            Entry("EscalatorSideMm", "300", "HCWESCALATOR: balustrade and skirt width on each side of the steps, in millimetres."),
            Entry("StairRailing", "1", "AECSTAIR section: 1 draws the handrail, its end posts and balusters, 0 leaves them out."),
            Entry("StairHandrailMm", "900", "AECSTAIR section: handrail height above the nosing line, in millimetres."),
            Entry("StairPostMm", "50", "AECSTAIR section: size of the handrail posts, in millimetres."),
            Entry("StairBalustersPerTread", "2", "AECSTAIR section: balusters on each tread."),
            Entry("StairRebarDrawing", "0", "AECSTAIR section: 1 draws the main bars along each flight and the distribution bars as dots, using the bar settings below. 0 leaves them out."),
            Entry("StairLandingWallEdgeMm", "0", "AECSTAIR quantities: length of landing edge that is against a wall and so needs no edge shuttering, in millimetres (the landing perimeter less the two flight joins and this length is shuttered)."),
            Entry("DoorHeightMm", "2100", "HCWDOOR: the door height written in the HEIGHT attribute when none is typed, in millimetres."),
            Entry("WindowHeightMm", "1200", "HCWWINDOW: the window height written in the HEIGHT attribute when none is typed, in millimetres."),
            Entry("WindowSillMm", "900", "HCWWINDOW: the sill height above the floor, in millimetres."),
            Entry("DoorLeafMm", "40", "HCWDOOR: thickness of the door leaf drawn in plan, in millimetres (0 draws a single line)."),
            Entry("DoorFrameMm", "50", "HCWDOOR: width of the door frame drawn at each jamb, in millimetres (0 leaves it out). Existing door blocks keep the frame they were made with."),
            Entry("StairMainBarDia", "12", "AECSTAIR reinforcement estimate: diameter of the main bars, in millimetres. Set it from the structural drawing."),
            Entry("StairMainBarSpacing", "150", "AECSTAIR reinforcement estimate: spacing of the main bars, in millimetres."),
            Entry("StairDistBarDia", "8", "AECSTAIR reinforcement estimate: diameter of the distribution bars, in millimetres."),
            Entry("StairDistBarSpacing", "200", "AECSTAIR reinforcement estimate: spacing of the distribution bars, in millimetres."),
            Entry("StairCover", "25", "AECSTAIR reinforcement estimate: cover at each end of a bar, in millimetres."),
            Entry("StairAnchorageDia", "40", "AECSTAIR reinforcement estimate: length each main bar runs into its support at each end, as a multiple of its diameter."),
            Entry("StairTextMm", "2.5", "AECSTAIR: plotted text height, in millimetres."),
            Entry("StairDimOffsetMm", "10", "AECSTAIR: plotted distance from the drawing to its dimension lines, in millimetres."),
            Entry("StairHatch", "1", "AECSTAIR: 1 hatches the RCC in the section, 0 leaves it plain."),
            Entry("StairDimStyle", "HCW-WORKING", "AECSTAIR: dimension style; the current style is used when it does not exist."),
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
