using System;
using System.Collections.Generic;
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
        private const string BlockLevel = "HCW_LEVEL", BlockNorth = "HCW_NORTH";

        private static double _scale = 100;
        private static bool _hasDatum;
        private static double _datumY, _datumLevel;
        private static int _sectionIndex;
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

        // ---- level mark ----

        [CommandMethod("HCWLEVEL")]
        public void LevelMark()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskScale(ed)) return;
            double unitsPerMetre = Util.MmToDrawingUnits(1000.0);
            string typed = null;
            var ucs = ed.CurrentUserCoordinateSystem;

            while (true)
            {
                var o = new PromptPointOptions("\nPick the level point [Value/Datum]" + (_hasDatum ? " <level from the datum>" : "") + ": ", "Value Datum") { AllowNone = true };
                var r = ed.GetPoint(o);
                if (r.Status == PromptStatus.None || r.Status == PromptStatus.Cancel) return;
                if (r.Status == PromptStatus.Keyword)
                {
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
                        _datumY = dp.Value.TransformBy(ucs).Y;
                        _datumLevel = dv.Status == PromptStatus.OK ? dv.Value : 0;
                        _hasDatum = true;
                        ed.WriteMessage("\nDatum: " + SymbolMath.LevelText(_datumLevel) + ". Later marks take their level from their height.");
                    }
                    continue;
                }
                if (r.Status != PromptStatus.OK) return;

                var at = r.Value.TransformBy(ucs);
                string text = typed;
                typed = null;
                if (text == null)
                {
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
                    Util.EnsureHcwLayer(tr, db, LayerSymbol);
                    EnsureLevelBlock(tr, db);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var br = new BlockReference(at, bt[BlockLevel]) { Layer = LayerSymbol, ScaleFactors = new Scale3d(Mm) };
                    space.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);
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

        /// <summary>Level mark in sheet millimetres: a triangle on the level point, a line to the right, the value above it.</summary>
        private static void EnsureLevelBlock(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockLevel)) return;
            var def = NewBlock(tr, bt, BlockLevel);
            var tri = new Polyline();
            tri.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            tri.AddVertexAt(1, new Point2d(-1.5, 2.6), 0, 0, 0);
            tri.AddVertexAt(2, new Point2d(1.5, 2.6), 0, 0, 0);
            tri.Closed = true;
            Add(tr, def, tri);
            Add(tr, def, new Line(new Point3d(-1.5, 2.6, 0), new Point3d(16, 2.6, 0)));
            var ad = new AttributeDefinition
            {
                Position = new Point3d(2.2, 3.3, 0), Height = 2.5, Tag = "LEVEL", Prompt = "Level (m)", TextString = "±0.000",
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

            var ucs = ed.CurrentUserCoordinateSystem;
            var a3 = p1.Value.TransformBy(ucs); var b3 = p2.Value.TransformBy(ucs); var s3 = sp.Value.TransformBy(ucs);
            var a = new P2(a3.X, a3.Y); var b = new P2(b3.X, b3.Y);
            if (a.DistanceTo(b) < 1e-9) { ed.WriteMessage("\nHCWSECTION: the two points are the same."); return; }
            var look = SymbolMath.LookNormal(a, b, new P2(s3.X, s3.Y));
            double mm = Mm, z = a3.Z;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LayerSymbol);
                Util.EnsureHcwLayer(tr, db, LayerText);
                string ltype = "CENTER2";
                bool haveLt = Util.LoadLinetype(tr, db, ltype) != ObjectId.Null;
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var cut = new Line(new Point3d(a.X, a.Y, z), new Point3d(b.X, b.Y, z)) { Layer = LayerSymbol };
                if (haveLt) cut.Linetype = ltype;
                Put(tr, space, cut);

                foreach (var end in new[] { a, b })
                {
                    // Thick shaft along the look direction, then the bubble with the label.
                    var shaftEnd = end + look * (10 * mm);
                    var shaft = new Polyline { ConstantWidth = 0.8 * mm, Layer = LayerSymbol };
                    shaft.AddVertexAt(0, new Point2d(end.X, end.Y), 0, 0, 0);
                    shaft.AddVertexAt(1, new Point2d(shaftEnd.X, shaftEnd.Y), 0, 0, 0);
                    shaft.Elevation = z;
                    Put(tr, space, shaft);

                    var centre = shaftEnd + look * (4 * mm);
                    Put(tr, space, new Circle(new Point3d(centre.X, centre.Y, z), Vector3d.ZAxis, 4 * mm) { Layer = LayerSymbol });
                    var t = new DBText
                    {
                        Height = 3.5 * mm, TextString = label, Layer = LayerText,
                        Position = new Point3d(centre.X, centre.Y, z),
                        HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    t.AlignmentPoint = new Point3d(centre.X, centre.Y, z);
                    Put(tr, space, t);
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
