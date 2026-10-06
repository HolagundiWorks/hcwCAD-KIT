using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Drawing symbols sized for the sheet: HCWLEVEL (level mark with its value), HCWNORTH (north arrow),
    /// HCWSECTION (section line with lettered heads) and HCWSLOPE (slope arrow with its text).
    /// Symbols are drawn in millimetres on the sheet and scaled by the plot scale you give, so they plot the same
    /// size at any scale. They go on AN-SYMB, their text on the same layer through the block, or AN-TEXT for loose text.
    /// </summary>
    public class SymbolCommands
    {
        private const string LayerSymbol = "AN-SYMB", LayerText = "AN-TEXT";
        /// <summary>Level marks and section markers go on their building permit layers for scrutiny; the north arrow and the other symbols stay on AN-SYMB.</summary>
        private static string LevelLayer => Util.Out("AN-SYMB", "BP-LEVEL");
        private static string SectionLayer => Util.Out("AN-SYMB", "BP-SECTION");
        private static string ElevationLayer => Util.Out("AN-SYMB", "BP-ELEVATION");
        private const string BlockLevel = "HCW_LEVEL", BlockLevelUp = "HCW_LEVEL_UP", BlockNorth = "HCW_NORTH", BlockElev = "HCW_ELEV";

        private static double _scale = 100;
        private static bool _hasDatum;
        private static double _datumY, _datumLevel;
        private static int _sectionIndex;
        private static bool _ceiling;
        private static int _elevIndex = 1;
        private static string _slopeText = "1:100";

        /// <summary>Size of one sheet millimetre in drawing units at the current plot scale.</summary>
        private static double Mm => _scale * Util.MmToDrawingUnits(1.0);

        private static bool AskScale(Editor ed)
        {
            var r = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return false;
            _scale = r.Value;
            return true;
        }

        /// <summary>The sheet number a symbol drawn here belongs to: the name of the layout you are on (layouts are numbered by RENUMBERLAYOUTS), or - in model space.</summary>
        internal static string CurrentSheetNumber()
        {
            try
            {
                var name = Autodesk.AutoCAD.DatabaseServices.LayoutManager.Current.CurrentLayout;
                return string.IsNullOrEmpty(name) || string.Equals(name, "Model", StringComparison.OrdinalIgnoreCase) ? "-" : name;
            }
            catch (System.Exception) { return "-"; }
        }

        // ---- level mark ----

        private const string KindDatum = "DATUM", KindLevelLive = "LEVEL-LIVE", KindLevelFixed = "LEVEL-FIXED";

        /// <summary>Reads the datum marker (a circle on AN-SYMB tagged DATUM) into the datum fields; false when the drawing has none.</summary>
        private static bool LoadDatum(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var c = tr.GetObject(id, OpenMode.ForRead) as Circle;
                    double level;
                    if (c == null || !TitleBlockCommands.IsKind(c, KindDatum)) continue;
                    if (!double.TryParse(TitleBlockCommands.ReadExtra(c), NumberStyles.Float, CultureInfo.InvariantCulture, out level)) level = 0;
                    _datumY = c.Center.Y; _datumLevel = level; _hasDatum = true;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Draws the datum marker where you picked it and removes the earlier one, so marks follow the marker when it is moved.</summary>
        private static void PlaceDatumMarker(Database db, Point3d at, double level)
        {
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LevelLayer);
                TitleBlockCommands.EnsureRegApp(tr, db);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (ObjectId id in space)
                {
                    var old = tr.GetObject(id, OpenMode.ForRead) as Circle;
                    if (old == null || old.IsErased || !TitleBlockCommands.IsKind(old, KindDatum)) continue;
                    old.UpgradeOpen(); old.Erase();
                }
                var marker = new Circle(at, Vector3d.ZAxis, 1.5 * Mm) { Layer = LevelLayer };
                TitleBlockCommands.Tag(marker, KindDatum, level.ToString("R", CultureInfo.InvariantCulture));
                space.AppendEntity(marker);
                tr.AddNewlyCreatedDBObject(marker, true);
                tr.Commit();
            }
        }

        /// <summary>
        /// Brings every level mark that took its value from the datum up to date with where it is now and where the datum marker is.
        /// Returns how many marks changed. Used by the live update service, and safe to call on its own.
        /// </summary>
        internal static int RefreshLevels(Database db)
        {
            int changed = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                Circle datum = null; double level = 0;
                var marks = new List<BlockReference>();
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var c = ent as Circle;
                    if (c != null && TitleBlockCommands.IsKind(c, KindDatum))
                    {
                        datum = c;
                        if (!double.TryParse(TitleBlockCommands.ReadExtra(c), NumberStyles.Float, CultureInfo.InvariantCulture, out level)) level = 0;
                    }
                    var br = ent as BlockReference;
                    if (br != null && TitleBlockCommands.IsKind(br, KindLevelLive)) marks.Add(br);
                }
                if (datum == null) return 0;
                double perMetre = Util.MmToDrawingUnits(1000.0);
                foreach (var br in marks)
                {
                    string text = SymbolMath.LevelText(SymbolMath.LevelFromDatum(level, datum.Center.Y, br.Position.Y, perMetre));
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (!string.Equals(att.Tag, "LEVEL", StringComparison.OrdinalIgnoreCase) || att.TextString == text) continue;
                        att.UpgradeOpen();
                        att.TextString = text;
                        changed++;
                    }
                }
                if (changed > 0) tr.Commit();
            }
            return changed;
        }

        [CommandMethod("HCWLEVEL")]
        public void LevelMark()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            double unitsPerMetre = Util.MmToDrawingUnits(1000.0);
            if (!_hasDatum && LoadDatum(db)) ed.WriteMessage("\nUsing the datum marker already in the drawing: " + SymbolMath.LevelText(_datumLevel) + ".");
            string typed = null;
            var ucs = ed.CurrentUserCoordinateSystem;

            while (true)
            {
                var o = new PromptPointOptions("\nPick the level point [Value/Datum/Ceiling/Floor/Levels]" + (_ceiling ? " (ceiling marks)" : "") + (_hasDatum ? " <level from the datum>" : "") + ": ", "Value Datum Ceiling Floor Levels") { AllowNone = true };
                var r = ed.GetPoint(o);
                if (r.Status == PromptStatus.None || r.Status == PromptStatus.Cancel) return;
                if (r.Status == PromptStatus.Keyword)
                {
                    if (r.StringResult == "Ceiling" || r.StringResult == "Floor")
                    {
                        _ceiling = r.StringResult == "Ceiling";
                        ed.WriteMessage(_ceiling ? "\nCeiling marks: the triangle points up at the level." : "\nFloor marks: the triangle points down at the level.");
                        continue;
                    }
                    if (r.StringResult == "Levels")
                    {
                        // the finished floor level of a floor in the levels kept in the drawing
                        var lv = LevelStore.Load();
                        if (lv.Count == 0) { ed.WriteMessage("\nNo floors are defined in the drawing (MSCHED, Floors tab)."); continue; }
                        var elev = SectionBuilder.Elevations(lv);
                        ed.WriteMessage("\n" + string.Join(", ", lv.Select((l, i) => (i + 1) + " " + l.Name + " " + SymbolMath.LevelText(elev[i] / 1000.0))));
                        var fi = ed.GetInteger(new PromptIntegerOptions("\nFloor number: ") { LowerLimit = 1, UpperLimit = lv.Count, AllowNone = true });
                        if (fi.Status == PromptStatus.OK) typed = SymbolMath.LevelText(elev[fi.Value - 1] / 1000.0);
                        continue;
                    }
                    if (r.StringResult == "Value")
                    {
                        var v = ed.GetDouble(new PromptDoubleOptions("\nLevel in metres for the next mark (+3.150 is typed 3.15): ") { AllowNone = false });
                        if (v.Status == PromptStatus.OK) typed = SymbolMath.LevelText(v.Value);
                    }
                    else
                    {
                        var dp = ed.GetPoint("\nPick the datum point: ");
                        if (dp.Status != PromptStatus.OK) continue;
                        var dv = ed.GetDouble(new PromptDoubleOptions("\nLevel of that point in metres <0>: ")
                            { AllowNone = true, DefaultValue = 0, UseDefaultValue = true });
                        if (dv.Status != PromptStatus.OK && dv.Status != PromptStatus.None) continue;
                        var dw = dp.Value.TransformBy(ucs);
                        _datumY = dw.Y;
                        _datumLevel = dv.Status == PromptStatus.OK ? dv.Value : 0;
                        _hasDatum = true;
                        PlaceDatumMarker(db, dw, _datumLevel);
                        ed.WriteMessage("\nDatum: " + SymbolMath.LevelText(_datumLevel) + ". Later marks take their level from their height, and follow the datum marker if you move it (live updates must be on: HCWLIVE).");
                    }
                    continue;
                }
                if (r.Status != PromptStatus.OK) return;

                var at = r.Value.TransformBy(ucs);
                string text = typed;
                bool fromDatum = false;
                typed = null;
                if (text == null)
                {
                    if (_hasDatum) fromDatum = true;
                    if (_hasDatum) text = SymbolMath.LevelText(SymbolMath.LevelFromDatum(_datumLevel, _datumY, at.Y, unitsPerMetre));
                    else
                    {
                        var v = ed.GetDouble(new PromptDoubleOptions("\nLevel in metres (no datum set; use Datum to set one): ") { AllowNone = false });
                        if (v.Status != PromptStatus.OK) continue;
                        text = SymbolMath.LevelText(v.Value);
                    }
                }

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Util.EnsureHcwLayer(tr, db, LevelLayer);
                    EnsureLevelBlock(tr, db, _ceiling);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var br = new BlockReference(at, bt[_ceiling ? BlockLevelUp : BlockLevel]) { Layer = LevelLayer, ScaleFactors = new Scale3d(Mm) };
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    TitleBlockCommands.EnsureRegApp(tr, db);
                    TitleBlockCommands.Tag(br, fromDatum ? KindLevelLive : KindLevelFixed);
                    TitleBlockCommands.AddAttributes(tr, br);
                    foreach (ObjectId id in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                        if (string.Equals(att.Tag, "LEVEL", StringComparison.OrdinalIgnoreCase)) att.TextString = text;
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\nHCWLEVEL: " + text);
            }
        }

        /// <summary>
        /// Level mark in sheet millimetres: a triangle on the level point, a line to the right, the value beside the line.
        /// Floor mark: triangle pointing down, value above the line. Ceiling mark: triangle pointing up, value below the line.
        /// </summary>
        private static void EnsureLevelBlock(Transaction tr, Database db, bool up)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            string name = up ? BlockLevelUp : BlockLevel;
            if (bt.Has(name)) return;
            var def = NewBlock(tr, bt, name);
            double s = up ? -1 : 1;                       // the up mark is the down mark turned over, with its text on the other side of the line
            var tri = new Polyline();
            tri.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            tri.AddVertexAt(1, new Point2d(-1.5, 2.6 * s), 0, 0, 0);
            tri.AddVertexAt(2, new Point2d(1.5, 2.6 * s), 0, 0, 0);
            tri.Closed = true;
            Add(tr, def, tri);
            Add(tr, def, new Line(new Point3d(-1.5, 2.6 * s, 0), new Point3d(16, 2.6 * s, 0)));
            var ad = new AttributeDefinition
            {
                Position = new Point3d(2.2, up ? -5.6 : 3.3, 0), Height = 2.5, Tag = "LEVEL", Prompt = "Level (m)", TextString = "±0.000",
            };
            Add(tr, def, ad);
        }

        // ---- north arrow ----

        [CommandMethod("HCWNORTH")]
        public void NorthArrow()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            var pr = ed.GetPoint("\nPick the position of the north arrow: ");
            if (pr.Status != PromptStatus.OK) return;
            var ucs = ed.CurrentUserCoordinateSystem;
            var at = pr.Value.TransformBy(ucs);

            // Straight up the screen unless you pick where north is.
            double rotation = Math.Atan2(ucs.CoordinateSystem3d.Yaxis.Y, ucs.CoordinateSystem3d.Yaxis.X) - Math.PI / 2;
            var dr = ed.GetPoint(new PromptPointOptions("\nPick a point in the direction of north (Enter = up the screen): ")
                { AllowNone = true, UseBasePoint = true, BasePoint = pr.Value, UseDashedLine = true });
            if (dr.Status == PromptStatus.OK)
            {
                var to = dr.Value.TransformBy(ucs);
                if (to.DistanceTo(at) > 1e-9) rotation = Math.Atan2(to.Y - at.Y, to.X - at.X) - Math.PI / 2;
            }
            else if (dr.Status != PromptStatus.None) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LayerSymbol);
                EnsureNorthBlock(tr, db);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var br = new BlockReference(at, bt[BlockNorth]) { Layer = LayerSymbol, Rotation = rotation, ScaleFactors = new Scale3d(Mm) };
                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWNORTH: placed at 1:" + _scale + ".");
        }

        /// <summary>North arrow in sheet millimetres: a 16 mm circle, a filled arrow pointing up inside it, and N above.</summary>
        private static void EnsureNorthBlock(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockNorth)) return;
            var def = NewBlock(tr, bt, BlockNorth);
            Add(tr, def, new Circle(Point3d.Origin, Vector3d.ZAxis, 8));
            // Half of the arrow is filled, half open, as on a standard north point.
            Add(tr, def, new Solid(new Point3d(0, 7, 0), new Point3d(0, -6, 0), new Point3d(-3.5, -6, 0)));
            var outline = new Polyline();
            outline.AddVertexAt(0, new Point2d(0, 7), 0, 0, 0);
            outline.AddVertexAt(1, new Point2d(3.5, -6), 0, 0, 0);
            outline.AddVertexAt(2, new Point2d(0, -3.5), 0, 0, 0);
            outline.AddVertexAt(3, new Point2d(-3.5, -6), 0, 0, 0);
            outline.Closed = true;
            Add(tr, def, outline);
            var n = new DBText
            {
                Height = 3.5, TextString = "N", Position = new Point3d(0, 10, 0),
                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextBase,
            };
            n.AlignmentPoint = new Point3d(0, 10, 0);
            Add(tr, def, n);
        }

        // ---- section marker ----

        [CommandMethod("HCWSECTION")]
        public void SectionMarker()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            var p1 = ed.GetPoint("\nStart of the section line: ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetPoint(new PromptPointOptions("\nEnd of the section line: ") { UseBasePoint = true, BasePoint = p1.Value, UseDashedLine = true });
            if (p2.Status != PromptStatus.OK) return;
            var sp = ed.GetPoint("\nPick the side you look toward: ");
            if (sp.Status != PromptStatus.OK) return;

            string def = GridModel.Letter(_sectionIndex);
            var lr = ed.GetString(new PromptStringOptions("\nSection label <" + def + ">: ") { AllowSpaces = false, DefaultValue = def, UseDefaultValue = true });
            if (lr.Status != PromptStatus.OK) return;
            string label = lr.StringResult.Trim();
            if (label.Length == 0) label = def;
            string sheetDefault = CurrentSheetNumber();
            var shr = ed.GetString(new PromptStringOptions("\nSheet number the section is drawn on (type none to leave it off) <" + sheetDefault + ">: ")
                { AllowSpaces = false, DefaultValue = sheetDefault, UseDefaultValue = true });
            if (shr.Status != PromptStatus.OK) return;
            string sheet = shr.StringResult.Trim().Length > 0 ? shr.StringResult.Trim() : sheetDefault;
            if (string.Equals(sheet, "none", StringComparison.OrdinalIgnoreCase)) sheet = null;

            var ucs = ed.CurrentUserCoordinateSystem;
            var a3 = p1.Value.TransformBy(ucs); var b3 = p2.Value.TransformBy(ucs); var s3 = sp.Value.TransformBy(ucs);
            var a = new P2(a3.X, a3.Y); var b = new P2(b3.X, b3.Y);
            if (a.DistanceTo(b) < 1e-9) { ed.WriteMessage("\nHCWSECTION: the two points are the same."); return; }
            var look = SymbolMath.LookNormal(a, b, new P2(s3.X, s3.Y));
            double mm = Mm, z = a3.Z;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, SectionLayer);
                Util.EnsureHcwLayer(tr, db, LayerText);
                string ltype = "CENTER2";
                bool haveLt = Util.LoadLinetype(tr, db, ltype) != ObjectId.Null;
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var cut = new Line(new Point3d(a.X, a.Y, z), new Point3d(b.X, b.Y, z)) { Layer = SectionLayer };
                if (haveLt) cut.Linetype = ltype;
                Put(tr, space, cut);

                foreach (var end in new[] { a, b })
                {
                    // Thick shaft along the look direction, then the bubble with the label.
                    var shaftEnd = end + look * (10 * mm);
                    var shaft = new Polyline { ConstantWidth = 0.8 * mm, Layer = SectionLayer };
                    shaft.AddVertexAt(0, new Point2d(end.X, end.Y), 0, 0, 0);
                    shaft.AddVertexAt(1, new Point2d(shaftEnd.X, shaftEnd.Y), 0, 0, 0);
                    shaft.Elevation = z;
                    Put(tr, space, shaft);

                    var centre = shaftEnd + look * (4 * mm);
                    Put(tr, space, new Circle(new Point3d(centre.X, centre.Y, z), Vector3d.ZAxis, 4 * mm) { Layer = SectionLayer });
                    // With a sheet number the bubble is split by a line: the letter above it, the sheet below.
                    var labelAt = sheet == null ? centre : centre + new P2(0, 1.7 * mm);
                    var t = new DBText
                    {
                        Height = 3.5 * mm, TextString = label, Layer = LayerText,
                        Position = new Point3d(labelAt.X, labelAt.Y, z),
                        HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    t.AlignmentPoint = new Point3d(labelAt.X, labelAt.Y, z);
                    Put(tr, space, t);
                    if (sheet != null)
                    {
                        Put(tr, space, new Line(new Point3d(centre.X - 4 * mm, centre.Y, z), new Point3d(centre.X + 4 * mm, centre.Y, z)) { Layer = SectionLayer });
                        var st = new DBText
                        {
                            Height = 2.5 * mm, TextString = sheet, Layer = LayerText,
                            Position = new Point3d(centre.X, centre.Y - 2 * mm, z),
                            HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                        };
                        st.AlignmentPoint = new Point3d(centre.X, centre.Y - 2 * mm, z);
                        Put(tr, space, st);
                    }
                }
                tr.Commit();
            }
            _sectionIndex++;
            ed.WriteMessage("\nHCWSECTION: section " + label + ".");
        }

        // ---- slope arrow ----

        [CommandMethod("HCWSLOPE")]
        public void SlopeArrow()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            var tr0 = ed.GetString(new PromptStringOptions("\nSlope text, such as 1:100 or 2% <" + _slopeText + ">: ")
                { AllowSpaces = true, DefaultValue = _slopeText, UseDefaultValue = true });
            if (tr0.Status != PromptStatus.OK) return;
            string text = tr0.StringResult.Trim();
            if (text.Length == 0) text = _slopeText;
            _slopeText = text;

            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var p1 = ed.GetPoint(new PromptPointOptions("\nStart of the arrow, the high end (Enter to finish): ") { AllowNone = true });
                if (p1.Status != PromptStatus.OK) return;
                var p2 = ed.GetPoint(new PromptPointOptions("\nEnd of the arrow, the low end: ") { UseBasePoint = true, BasePoint = p1.Value, UseDashedLine = true });
                if (p2.Status != PromptStatus.OK) return;
                var a = p1.Value.TransformBy(ucs); var b = p2.Value.TransformBy(ucs);
                var d = new P2(b.X - a.X, b.Y - a.Y);
                double len = d.Length;
                if (len < 1e-9) continue;
                var u = d * (1.0 / len);
                var n = new P2(-u.Y, u.X);
                double mm = Mm, z = a.Z;

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Util.EnsureHcwLayer(tr, db, LayerSymbol);
                    Util.EnsureHcwLayer(tr, db, LayerText);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    Put(tr, space, new Line(a, b) { Layer = LayerSymbol });
                    // Arrow head at the low end.
                    double head = Math.Min(3 * mm, len / 3.0), half = head / 3.0;
                    var tip = new P2(b.X, b.Y); var baseC = tip - u * head;
                    Put(tr, space, new Solid(new Point3d(tip.X, tip.Y, z),
                        new Point3d(baseC.X + n.X * half, baseC.Y + n.Y * half, z),
                        new Point3d(baseC.X - n.X * half, baseC.Y - n.Y * half, z)) { Layer = LayerSymbol });
                    // Text above the middle of the line, turned to read left to right.
                    double ang = SymbolMath.ReadableAngle(Math.Atan2(d.Y, d.X));
                    var mid = new P2((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                    var up = new P2(-Math.Sin(ang), Math.Cos(ang));
                    var at = mid + up * (1.5 * mm);
                    var t = new DBText
                    {
                        Height = 2.5 * mm, TextString = text, Layer = LayerText, Rotation = ang,
                        Position = new Point3d(at.X, at.Y, z),
                        HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextBase,
                    };
                    t.AlignmentPoint = new Point3d(at.X, at.Y, z);
                    Put(tr, space, t);
                    tr.Commit();
                }
                ed.WriteMessage("\nHCWSLOPE: " + text);
            }
        }

        // ---- level schedule ----

        [CommandMethod("HCWLEVELSCHED")]
        public void LevelScheduleCommand()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            var pr = ed.GetPoint("\nPick the top-left corner of the level schedule: ");
            if (pr.Status != PromptStatus.OK) return;
            var at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var texts = new List<string>();
                foreach (ObjectId id in space)
                {
                    if (id.ObjectClass.DxfName != "INSERT") continue;
                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                    string name = BlockOpenings.EffectiveName(tr, br);
                    if (!string.Equals(name, BlockLevel, StringComparison.OrdinalIgnoreCase) && !string.Equals(name, BlockLevelUp, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                        if (string.Equals(att.Tag, "LEVEL", StringComparison.OrdinalIgnoreCase)) texts.Add(att.TextString);
                    }
                }
                var rows = LevelSchedule.Build(texts);
                if (rows.Count == 0)
                {
                    ed.WriteMessage("\nHCWLEVELSCHED: no level marks (HCWLEVEL) in this space.");
                    return;
                }
                Util.EnsureHcwLayer(tr, db, LayerText);
                double h = 2.5 * Mm;
                MeasureCommands.DrawTable(tr, db, new Point3d(at.X, at.Y - 2.5 * h, 0), new[] { "Level (m)", "Marks" },
                    rows.Select(r => new[] { r.Level, r.Marks.ToString() }).ToList(), h, LayerText);
                var title = new DBText { Height = h * 1.2, TextString = "LEVEL SCHEDULE", Layer = LayerText, Position = new Point3d(at.X, at.Y - 1.2 * h, 0) };
                space.AppendEntity(title);
                tr.AddNewlyCreatedDBObject(title, true);
                tr.Commit();
                ed.WriteMessage("\nHCWLEVELSCHED: " + rows.Count + " level(s), " + rows.Sum(r => r.Marks) + " mark(s).");
            }
        }

        // ---- elevation marker ----

        [CommandMethod("HCWELEV")]
        public void ElevationMarker()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            var ucs = ed.CurrentUserCoordinateSystem;

            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the position of the elevation marker (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var at = pr.Value.TransformBy(ucs);
                var dr = ed.GetPoint(new PromptPointOptions("\nPick a point in the direction you look: ") { UseBasePoint = true, BasePoint = pr.Value, UseDashedLine = true });
                if (dr.Status != PromptStatus.OK) return;
                var to = dr.Value.TransformBy(ucs);
                if (to.DistanceTo(at) < 1e-9) { ed.WriteMessage("\nPick a point away from the marker."); continue; }
                double rotation = Math.Atan2(to.Y - at.Y, to.X - at.X);

                var nr = ed.GetString(new PromptStringOptions("\nElevation number <" + _elevIndex + ">: ") { AllowSpaces = false, DefaultValue = _elevIndex.ToString(), UseDefaultValue = true });
                if (nr.Status != PromptStatus.OK) return;
                string number = nr.StringResult.Trim().Length > 0 ? nr.StringResult.Trim() : _elevIndex.ToString();
                var sr = ed.GetString(new PromptStringOptions("\nSheet number the elevation is drawn on <" + CurrentSheetNumber() + ">: ") { AllowSpaces = false, DefaultValue = CurrentSheetNumber(), UseDefaultValue = true });
                if (sr.Status != PromptStatus.OK) return;
                string sheet = sr.StringResult.Trim().Length > 0 ? sr.StringResult.Trim() : CurrentSheetNumber();

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Util.EnsureHcwLayer(tr, db, ElevationLayer);
                    EnsureElevationBlock(tr, db);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var br = new BlockReference(at, bt[BlockElev]) { Layer = ElevationLayer, Rotation = rotation, ScaleFactors = new Scale3d(Mm) };
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
                    TitleBlockCommands.AddAttributes(tr, br);
                    foreach (ObjectId id in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                        string tag = att.Tag ?? "";
                        if (string.Equals(tag, "ELEV", StringComparison.OrdinalIgnoreCase)) att.TextString = number;
                        else if (string.Equals(tag, "SHEET", StringComparison.OrdinalIgnoreCase)) att.TextString = sheet;
                        att.Rotation = 0;                       // the numbers stay upright whichever way the marker points
                    }
                    tr.Commit();
                }
                int n;
                if (int.TryParse(number, out n)) _elevIndex = n + 1; else _elevIndex++;
                ed.WriteMessage("\nHCWELEV: elevation " + number + " on sheet " + sheet + ".");
            }
        }

        /// <summary>Elevation marker in sheet millimetres: a 10 mm circle split by a line (elevation number above, sheet number below) and a pointer toward +x, the way you look.</summary>
        private static void EnsureElevationBlock(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockElev)) return;
            var def = NewBlock(tr, bt, BlockElev);
            Add(tr, def, new Circle(Point3d.Origin, Vector3d.ZAxis, 5));
            Add(tr, def, new Line(new Point3d(-5, 0, 0), new Point3d(5, 0, 0)));
            Add(tr, def, new Solid(new Point3d(5, 0, 0), new Point3d(9, 2, 0), new Point3d(9, -2, 0)));
            Add(tr, def, new AttributeDefinition
            {
                Position = new Point3d(0, 2.6, 0), Height = 2.5, Tag = "ELEV", Prompt = "Elevation number", TextString = "1",
                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = new Point3d(0, 2.6, 0),
            });
            Add(tr, def, new AttributeDefinition
            {
                Position = new Point3d(0, -2.6, 0), Height = 2.5, Tag = "SHEET", Prompt = "Sheet number", TextString = "-",
                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid, AlignmentPoint = new Point3d(0, -2.6, 0),
            });
        }

        // ---- helpers ----

        private static BlockTableRecord NewBlock(Transaction tr, BlockTable bt, string name)
        {
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            return def;
        }

        /// <summary>Adds geometry to a block on layer 0, so it takes the layer of the insert (a new entity would otherwise take the current layer).</summary>
        private static void Add(Transaction tr, BlockTableRecord def, Entity ent)
        {
            ent.Layer = "0";
            def.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        /// <summary>Adds an entity to the current space with the layer it already carries.</summary>
        private static void Put(Transaction tr, BlockTableRecord space, Entity ent)
        {
            space.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }
    }
}
