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
            Entry("WallNumbering", "LeftRight", "Wall numbers FB01, FB02...: LeftRight, TopBottom, Path (asks for a path line) or Room (asks for room outlines and numbers the walls room by room)."),
            Entry("DeductionColours", "1", "MLIN, MBRK and MBML: 1 colours each standalone deduction line by the schedule name it is mapped to (the same name, the same colour); 0 leaves colours alone."),
            Entry("DefaultDoorHeight", "2.1", "Height given to a door row made by Add from blocks."),
            Entry("DefaultWindowHeight", "1.2", "Height given to a window row made by Add from blocks."),
            Entry("DefaultFflHeight", "3.15", "New floor: FFL to FFL height."),
            Entry("DefaultCeilingHeight", "3.0", "New floor: ceiling height."),
            Entry("DefaultSlabThickness", "0.15", "New floor: slab thickness."),
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
            Entry("StairInputUnit", "Auto", "AECSTAIR: unit of the numbers you type: Auto (mm in a metric drawing, in in a feet or inches drawing), mm, cm, m, in or ft."),
            Entry("StairPreferredRiseMm", "165", "AECSTAIR: the rise the automatic riser count aims for, in millimetres."),
            Entry("StairHeadroomLine", "1", "AECSTAIR section: 1 draws a headroom line over each flight, 0 leaves it out."),
            Entry("StairHeadroomMm", "2000", "AECSTAIR section: the headroom, measured vertically above the line through the nosings, in millimetres. Set it from your own code."),
            Entry("WallMeasureLines", "1", "HCWWALL: 1 also draws each wall's centre line on the take-off layer, so MLIN, MBRK and the wall paint see it; 0 leaves it out."),
            Entry("WallMeasureLayer", "MEASURE-LINEAR", "HCWWALL: the take-off layer the wall's measurement line goes on (MEASURE-LINEAR, MEASURE-FULLBRICK, MEASURE-HALFBRICK, MEASURE-BEAM or MEASURE-LINTEL)."),
            Entry("OpeningAutoSync", "1", "Doors and windows: 1 updates the opening schedule (the one MSCHED and the take-off use) after HCWDOOR, HCWWINDOW and the opening edit commands; 0 leaves it to HCWOPENSYNC."),
            Entry("RoomMeasureOutlines", "1", "Rooms: 1 also draws each room outline on MEASURE-FLOOR and MEASURE-CEILING, so MFLOOR and MCEIL read the rooms; 0 leaves them out."),
            Entry("LintelAuto", "1", "Doors and windows: 1 draws a lintel over each opening (a line on MEASURE-LINTEL for MBML and a dashed outline on A-LINTEL); 0 leaves it out."),
            Entry("LintelBearingMm", "230", "Lintels: the bearing on the wall at each end of the opening, in millimetres."),
            Entry("WallHatch", "1", "HCWWALL: 1 hatches the walls (all walls joined, doors and windows cut out) on A-WALL-HATCH and redraws it as walls and openings change; 0 turns it off."),
            Entry("WallHatchPattern", "ANSI31", "Wall hatch: the pattern name (ANSI31, ANSI37, SOLID ...)."),
            Entry("WallHatchSpacingMm", "60", "Wall hatch: line spacing on the drawing in millimetres (the pattern scale is worked out from it)."),
            Entry("WallJoinOnDraw", "1", "HCWWALL: 1 merges a new wall with the wall outlines it touches, 0 leaves each wall as its own outline."),
            Entry("LiftMachineMarginMm", "1000", "HCWLIFT: how far the machine room outline extends beyond the shaft wall on every side, in millimetres."),
            Entry("EscalatorAngleDeg", "30", "HCWESCALATOR: the angle offered first, in degrees (30, or 35 up to 6 m rise)."),
            Entry("EscalatorStepWidthMm", "1000", "HCWESCALATOR: the nominal step width offered first, in millimetres."),
            Entry("EscalatorLandingMm", "2500", "HCWESCALATOR: the flat landing length at each end, in millimetres."),
            Entry("EscalatorSideMm", "300", "HCWESCALATOR: balustrade and skirt width on each side of the steps, in millimetres."),
            Entry("ColumnEdgeProjectMm", "0", "HCWCOLUMN: with edge columns set to Flush, how far the outer face stands outside the grid line, in millimetres (0 = on the line; half a wall thickness = flush with the wall face)."),
            Entry("LiveUpdate", "1", "1 starts the live update service with the plugin (labels, level marks, associative dimensions and the area statement refresh after the command that changed them); 0 leaves it off until HCWLIVE On."),
            Entry("AreaSiteLayer", "BP-SITE-BOUNDARY", "HCWAREASTMT when reading from layers: the layer offered first for the site boundary."),
            Entry("AreaGrossLayer", "BP-BUILDING-CUT", "HCWAREASTMT when reading from layers: the layer offered first for each floor's built-up outlines."),
            Entry("AutoDimOpeningTags", "1", "AUTODIM and AUTODIMROOM: 1 puts an opening's tag (D1, W2 ...), read from the tag text HCWDOOR and HCWWINDOW draw, above its dimension; 0 leaves the tags off."),
            Entry("AutoDimGapOpenings", "1", "AUTODIM and AUTODIMROOM: 1 reads a break at the same place in both faces of a straight wall as an opening; 0 reads only door and window blocks and geometry."),
            Entry("AutoDimGapMinMm", "400", "AUTODIM: the narrowest gap in a wall that counts as an opening, in millimetres."),
            Entry("AutoDimGapMaxMm", "4000", "AUTODIM: the widest gap in a wall that counts as an opening, in millimetres."),
            Entry("AutoDimWallMaxMm", "600", "AUTODIM: the two faces of a wall are at most this far apart when a gap is matched across them, in millimetres."),
            Entry("AutoDimRoomGapMm", "1200", "AUTODIMROOM when it finds the rooms itself: gaps up to this wide in the walls (unframed doors) are bridged, in millimetres."),
            Entry("AutoDimRoomNames", "1", "AUTODIMROOM: 1 writes each room's name (read from the text inside it) under its dimensions; 0 leaves it off."),
            Entry("AutoDimRoomNameLayers", "ROOM-LABELS;BP-ROOM;A-ROOM-NAME", "AUTODIMROOM: the layers whose text gives the room names, separated by semicolons."),
            Entry("SheetSetFillFields", "1", "SHEETSET: 1 fills blank title block fields (project, owner, architect, plot use ...) from the fields library (FIELDS); 0 leaves them as the template has them."),
            Entry("RoomTableLayers", "ROOM-RECT;BP-ROOM;MEASURE-FLOOR;A-ROOM", "HCWROOMREPORT: when you press Enter to take everything in the space, the closed polylines on these layers (separated by semicolons) are the rooms. Outlines you select yourself count whatever their layer."),
            Entry("RoomTableUnit", "m", "HCWROOMREPORT: m writes lengths in metres to 2 places, mm in whole millimetres. Areas are always square metres."),
            Entry("RoomTableTextMm", "250", "HCWROOMREPORT: real-size text height of the table drawn in the drawing, in millimetres."),
            Entry("AutoDimReanchorMm", "50", "Associative dimensions: when the wall (or other line) a dimension hangs on is erased and drawn again, its end is tied to the vertex standing within this distance of where it was, in millimetres. 0 turns re-tying off."),
            Entry("WallMinMm", "60", "HCWWALLADOPT: the thinnest wall it will find between two faces, in millimetres."),
            Entry("WallMaxMm", "600", "HCWWALLADOPT: the thickest wall it will find between two faces, in millimetres."),
            Entry("AutoDimCentreLineMm", "0", "AUTODIM and AUTODIMROOM: 0 reads walls as faces (two lines or a closed outline). Set the wall thickness in millimetres to read single wall lines as the centres of walls of that thickness: the two faces are dimensioned and gaps in the lines are read as openings."),
            Entry("AutoDimCentreLineLayers", "", "AUTODIM: with AutoDimCentreLineMm set, the wall layers (separated by semicolons) that hold single wall lines. Empty means every wall layer. Wall layers not listed are read as faces, so a drawing can mix both."),
            Entry("StairHookDia", "0", "AECSTAIR reinforcement estimate: hook at each end of a main bar, as a multiple of its diameter (0 = straight ends). Take it from your structural drawing or code."),
            Entry("StairCrank", "0", "AECSTAIR reinforcement estimate: 1 cranks every second main bar of a flight up at both supports (45 degrees, 0.42 x the lift longer each); 0 leaves them straight."),
            Entry("StairTopBarDia", "0", "AECSTAIR reinforcement estimate: diameter of the top steel over the supports at both ends of each flight, in millimetres (0 = none)."),
            Entry("StairTopBarSpacing", "200", "AECSTAIR reinforcement estimate: spacing of the top steel, in millimetres."),
            Entry("StairTopBarSpanPct", "30", "AECSTAIR reinforcement estimate: how far the top steel reaches along the flight from each support, as a percentage of the slope length."),
            Entry("LayerOutput", "BP", "Which layers the drawing tools draw on: BP puts walls, doors, windows, columns, rooms, stairs, lifts, escalators, rails, level marks and sections on the building permit layers (BP-BUILDING-CUT, BP-DOOR ...) so a scrutiny program can pick them up by layer; HCW keeps the HCW layer names (A-WALL, A-DOOR ...). Text, dimensions, grid, take-off and electrical layers do not change."),
            Entry("BpLayerMap", Logic.LayerRoles.DefaultMap, "Which building permit layer each HCW layer becomes when LayerOutput is BP, as HCWLAYER=BPLAYER separated by semicolons. Change an entry to send that item to another layer."),
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
