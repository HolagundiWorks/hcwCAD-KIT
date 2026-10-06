using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWCOLUMN puts a column of one size on every grid intersection (or those in a window): a closed polyline,
    /// or a circle for a round column, on A-COL, turned to follow the grid, with an optional solid fill.
    /// Intersections that already have a column are skipped, so it is safe to run again.
    /// HCWCOLSCHED marks every column on A-COL with its mark (C1, C2 …, the biggest section first) and draws the
    /// column schedule (mark, size, number). Sizes are in millimetres whatever the drawing units.
    /// </summary>
    public class ColumnCommands
    {
        internal static string LayerColumn => Util.Out("A-COL");
        private const string LayerFill = "AN-HATCH", LayerTag = "AN-TEXT";
        private const string KindTag = "COLTAG", KindTable = "COLTABLE";
        private const double TagHeightMm = 250, SkipRadiusMm = 50;

        private static string _size = "230x450";
        private static bool _fill = true;
        private static string _layer = LayerColumn;

        [CommandMethod("HCWCOLUMN")]
        public void PlaceColumns()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            ColumnSize size = null;
            while (size == null)
            {
                var r = ed.GetString(new PromptStringOptions("\nColumn size in mm (230x450, 300 for square, D450 for round) <" + _size + ">: ")
                    { AllowSpaces = true, DefaultValue = _size, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return;
                string error;
                size = ColumnSize.Parse(r.StringResult, out error);
                if (size == null) ed.WriteMessage("\n" + error + ".");
                else _size = r.StringResult.Trim();
            }

            var lo = new PromptKeywordOptions("\nColumn layer [" + LayerColumn + "/" + MeasureCommands.LayCol + "] <" + _layer + ">: ", LayerColumn + " " + MeasureCommands.LayCol) { AllowNone = true };
            lo.Keywords.Default = _layer;
            var lr = ed.GetKeywords(lo);
            if (lr.Status == PromptStatus.OK) _layer = lr.StringResult;
            else if (lr.Status != PromptStatus.None) return;

            var fo = new PromptKeywordOptions("\nSolid fill [Yes/No] <" + (_fill ? "Yes" : "No") + ">: ", "Yes No") { AllowNone = true };
            fo.Keywords.Default = _fill ? "Yes" : "No";
            var fr = ed.GetKeywords(fo);
            if (fr.Status == PromptStatus.OK) _fill = fr.StringResult == "Yes";
            else if (fr.Status != PromptStatus.None) return;

            // The grid: lines you select, or every line on the grid layers.
            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\nSelect the grid lines (Enter = every line on the grid layers): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE") }));
            if (psr.Status != PromptStatus.OK && psr.Status != PromptStatus.None) return;

            double mm = Util.MmToDrawingUnits(1.0);
            var lines = new List<Seg>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (psr.Status == PromptStatus.OK)
                {
                    foreach (var id in psr.Value.GetObjectIds())
                        AddLine(lines, (Line)tr.GetObject(id, OpenMode.ForRead));
                }
                else
                {
                    var gridLayers = new HashSet<string>(
                        Settings.Get("AutoDimGridLayers", "AN-GRID;A-GRID").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                        StringComparer.OrdinalIgnoreCase);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
                        if (ln != null && gridLayers.Contains(ln.Layer)) AddLine(lines, ln);
                    }
                }
                tr.Commit();
            }
            if (lines.Count < 2)
            {
                ed.WriteMessage("\nHCWCOLUMN: no grid lines found. Draw a grid with HCWAXIS, or select the lines.");
                return;
            }

            var points = GridIntersections.Find(lines, mm * 0.5);
            if (points.Count == 0)
            {
                ed.WriteMessage("\nHCWCOLUMN: the grid lines do not cross each other.");
                return;
            }

            var wo = new PromptKeywordOptions("\nColumns at [All intersections/Window] <All>: ", "All Window") { AllowNone = true };
            wo.Keywords.Default = "All";
            var wr = ed.GetKeywords(wo);
            if (wr.Status != PromptStatus.OK && wr.Status != PromptStatus.None) return;
            if (wr.Status == PromptStatus.OK && wr.StringResult == "Window")
            {
                var c1 = ed.GetPoint("\nFirst corner: ");
                if (c1.Status != PromptStatus.OK) return;
                var c2 = ed.GetCorner("\nOpposite corner: ", c1.Value);
                if (c2.Status != PromptStatus.OK) return;
                var ucs = ed.CurrentUserCoordinateSystem;
                var a = c1.Value.TransformBy(ucs); var b = c2.Value.TransformBy(ucs);
                double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
                points = points.Where(p => p.Pt.X >= x0 && p.Pt.X <= x1 && p.Pt.Y >= y0 && p.Pt.Y <= y1).ToList();
                if (points.Count == 0) { ed.WriteMessage("\nHCWCOLUMN: no grid intersection inside that window."); return; }
            }

            var sizes = points.Select(_ => size).ToList();
            var mo = new PromptKeywordOptions("\nDifferent sizes at chosen intersections [Yes/No] <No>: ", "Yes No") { AllowNone = true };
            mo.Keywords.Default = "No";
            var mr = ed.GetKeywords(mo);
            if (mr.Status != PromptStatus.OK && mr.Status != PromptStatus.None) return;
            if (mr.Status == PromptStatus.OK && mr.StringResult == "Yes" && !AskGroups(ed, points, sizes)) return;

            var edgeOpt = new PromptKeywordOptions("\nColumns on the edge of the grid [Centred/Flush] <" + (_flush ? "Flush" : "Centred") + ">: ", "Centred Flush") { AllowNone = true };
            edgeOpt.Keywords.Default = _flush ? "Flush" : "Centred";
            var edgeRes = ed.GetKeywords(edgeOpt);
            if (edgeRes.Status == PromptStatus.OK) _flush = edgeRes.StringResult == "Flush";
            else if (edgeRes.Status != PromptStatus.None) return;
            List<P2> shifts = null;
            if (_flush)
                shifts = ColumnEdges.Shifts(points, sizes.Select(z => z.W * mm).ToList(), sizes.Select(z => (z.Round ? z.W : z.D) * mm).ToList(),
                    Settings.GetDouble("ColumnEdgeProjectMm", 0) * mm, mm * 0.5);

            bool measureLayer = string.Equals(_layer, MeasureCommands.LayCol, StringComparison.OrdinalIgnoreCase);
            if (!measureLayer)
            {
                var bo = new PromptKeywordOptions("\nDraw each column as [Outline/Block with mark] <" + (_blocks ? "Block" : "Outline") + ">: ", "Outline Block") { AllowNone = true };
                bo.Keywords.Default = _blocks ? "Block" : "Outline";
                var br = ed.GetKeywords(bo);
                if (br.Status == PromptStatus.OK) _blocks = br.StringResult == "Block";
                else if (br.Status != PromptStatus.None) return;
            }
            bool asBlocks = _blocks && !measureLayer;

            int placed = 0, skipped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (measureLayer) Util.EnsureLayer(tr, db, MeasureCommands.LayCol, 2);
                else Util.EnsureHcwLayer(tr, db, LayerColumn);
                if (_fill) Util.EnsureHcwLayer(tr, db, LayerFill);
                if (asBlocks) Util.EnsureHcwLayer(tr, db, LayerTag);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var existing = ExistingCentres(tr, space);
                double skipR = SkipRadiusMm * mm;

                for (int k = 0; k < points.Count; k++)
                {
                    var gp = points[k];
                    var sz = sizes[k];
                    var centre = shifts == null ? gp.Pt : gp.Pt + shifts[k];
                    if (existing.Any(c => c.DistanceTo(centre) <= skipR)) { skipped++; continue; }
                    double w = sz.W * mm, d = sz.D * mm;
                    if (asBlocks)
                    {
                        var def = EnsureColumnBlock(tr, db, sz);
                        var br = new BlockReference(new Point3d(centre.X, centre.Y, 0), def) { Layer = _layer, Rotation = gp.Angle };
                        space.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                        var rec = (BlockTableRecord)tr.GetObject(def, OpenMode.ForRead);
                        foreach (ObjectId eid in rec)
                        {
                            var ad = tr.GetObject(eid, OpenMode.ForRead) as AttributeDefinition;
                            if (ad == null || ad.Constant) continue;
                            var ar = new AttributeReference();
                            ar.SetAttributeFromBlock(ad, br.BlockTransform);
                            ar.TextString = "C?";
                            br.AttributeCollection.AppendAttribute(ar);
                            tr.AddNewlyCreatedDBObject(ar, true);
                        }
                        existing.Add(centre);
                        placed++;
                        continue;
                    }
                    Entity outline;
                    if (sz.Round)
                        outline = new Circle(new Point3d(centre.X, centre.Y, 0), Vector3d.ZAxis, w / 2.0);
                    else
                    {
                        var pl = new Polyline();
                        double cos = Math.Cos(gp.Angle), sin = Math.Sin(gp.Angle);
                        var corners = new[] { new P2(-w / 2, -d / 2), new P2(w / 2, -d / 2), new P2(w / 2, d / 2), new P2(-w / 2, d / 2) };
                        for (int i = 0; i < 4; i++)
                        {
                            double x = centre.X + corners[i].X * cos - corners[i].Y * sin;
                            double y = centre.Y + corners[i].X * sin + corners[i].Y * cos;
                            pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0);
                        }
                        pl.Closed = true;
                        outline = pl;
                    }
                    outline.Layer = _layer;
                    space.AppendEntity(outline);
                    tr.AddNewlyCreatedDBObject(outline, true);
                    if (_fill) AddFill(tr, space, outline.ObjectId);
                    existing.Add(centre);
                    placed++;
                }
                tr.Commit();
            }
            var distinct = sizes.Select(z => z.Label).Distinct().ToList();
            ed.WriteMessage("\nHCWCOLUMN: " + placed + " column(s) " + string.Join(", ", distinct) + " mm on " + _layer
                + (asBlocks ? " as blocks (run HCWCOLSCHED to fill the marks)" : "")
                + (_flush ? ", edge columns flush" : "")
                + (skipped > 0 ? ", " + skipped + " intersection(s) already had one." : "."));
        }

        private static bool _flush, _blocks;

        /// <summary>Gives other sizes to the intersections inside windows you pick, one size after another until Enter.</summary>
        private static bool AskGroups(Editor ed, List<GridPoint> points, List<ColumnSize> sizes)
        {
            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var r = ed.GetString(new PromptStringOptions("\nSize for the next group of intersections (Enter to finish): ") { AllowSpaces = true });
                if (r.Status == PromptStatus.None || (r.Status == PromptStatus.OK && r.StringResult.Trim().Length == 0)) return true;
                if (r.Status != PromptStatus.OK) return false;
                string error;
                var sz = ColumnSize.Parse(r.StringResult, out error);
                if (sz == null) { ed.WriteMessage("\n" + error + "."); continue; }
                var c1 = ed.GetPoint("\nFirst corner of the window round those intersections: ");
                if (c1.Status != PromptStatus.OK) return false;
                var c2 = ed.GetCorner("\nOpposite corner: ", c1.Value);
                if (c2.Status != PromptStatus.OK) return false;
                var a = c1.Value.TransformBy(ucs); var b = c2.Value.TransformBy(ucs);
                double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
                int n = 0;
                for (int i = 0; i < points.Count; i++)
                    if (points[i].Pt.X >= x0 && points[i].Pt.X <= x1 && points[i].Pt.Y >= y0 && points[i].Pt.Y <= y1) { sizes[i] = sz; n++; }
                ed.WriteMessage("\n" + n + " intersection(s) now " + sz.Label + ".");
            }
        }

        /// <summary>The block for a column size: the outline centred on the origin on layer 0, an optional fill, and the MARK attribute above it.</summary>
        private static ObjectId EnsureColumnBlock(Transaction tr, Database db, ColumnSize size)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            string name = size.BlockName;
            if (bt.Has(name)) return bt[name];
            double mm = Util.MmToDrawingUnits(1.0);
            double w = size.W * mm, d = size.D * mm;
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = name };
            var id = bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            Entity outline;
            if (size.Round) outline = new Circle(Point3d.Origin, Vector3d.ZAxis, w / 2);
            else
            {
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(-w / 2, -d / 2), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(w / 2, -d / 2), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(w / 2, d / 2), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(-w / 2, d / 2), 0, 0, 0);
                pl.Closed = true;
                outline = pl;
            }
            def.AppendEntity(outline);
            tr.AddNewlyCreatedDBObject(outline, true);
            if (_fill)
            {
                try
                {
                    var hatch = new Hatch();
                    def.AppendEntity(hatch);
                    tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                    hatch.Associative = false;
                    hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { outline.ObjectId });
                    hatch.EvaluateHatch(true);
                }
                catch (System.Exception) { }
            }
            double h = TagHeightMm * mm;
            var att = new AttributeDefinition
            {
                Tag = "MARK", Prompt = "Column mark", TextString = "C?", Height = h, Layer = LayerTag,
                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                Position = new Point3d(0, (size.Round ? w : d) / 2 + 0.7 * h, 0),
            };
            att.AlignmentPoint = att.Position;
            def.AppendEntity(att);
            tr.AddNewlyCreatedDBObject(att, true);
            return id;
        }

        /// <summary>
        /// The column schedule as a table you fill in: Edit opens the schedule (MSCHED, Columns tab: mark, section, height, floor, number), and
        /// Quantities works out the concrete and shuttering of each line, saves them as the take-off "Columns" and can draw them as a table.
        /// A height of 0 is taken from the levels (floor to floor less the slab above).
        /// </summary>
        [CommandMethod("HCWCOLQTY")]
        public void ColumnQuantities()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            string job = Util.AskMode("Column schedule", "Edit", "Quantities");
            if (job == null) return;
            if (job == "Edit") { new MeasureCommands().MSched(); return; }
            if (!MeasureCommands.Prepare()) return;
            double k = LevelStore.UnitMm;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var book = MeasureBook.Load(tr, db);
                if (book.Columns.Count == 0)
                {
                    ed.WriteMessage("\nHCWCOLQTY: the column schedule is empty. Choose Edit to enter the columns, or run HCWCOLSCHED to fill the sizes from the drawing.");
                    return;
                }
                var levels = LevelStore.Load(tr, db);
                var lines = new List<ColumnLine>();
                int fromLevels = 0, noHeight = 0;
                foreach (var c in book.Columns)
                {
                    bool round = c.Depth <= 0 || (c.Name ?? "").IndexOf("round", StringComparison.OrdinalIgnoreCase) >= 0 || (c.Name ?? "").IndexOf("circ", StringComparison.OrdinalIgnoreCase) >= 0;
                    double h = c.Height * k;
                    if (h <= 0) { h = ColumnQuantity.HeightFromLevels(levels, c.Floor); if (h > 0) fromLevels++; else noHeight++; }
                    lines.Add(new ColumnLine { Mark = c.Mark, WidthMm = c.Width * k, DepthMm = round && c.Depth <= 0 ? 0 : c.Depth * k, Round = round, HeightMm = h, Count = Math.Max(1, c.Count), Floor = c.Floor ?? "" });
                }
                var rows = ColumnQuantity.Rows(lines);
                MeasureBook.SaveTakeoff(tr, db, "Columns", ColumnQuantity.Headers, rows);
                ed.WriteMessage("\n\nCOLUMN QUANTITIES");
                foreach (var r in rows)
                    ed.WriteMessage("\n  " + Util.Pad(r[0], 7) + Util.Pad(r[1], 14) + "h " + Util.Pad(r[2], 6) + "x" + Util.Pad(r[4], 4) + "  " + r[5] + " m3  " + r[6] + " m2");
                if (fromLevels > 0) ed.WriteMessage("\n  " + fromLevels + " height(s) taken from the levels.");
                if (noHeight > 0) ed.WriteMessage("\n  " + noHeight + " column line(s) have no height and no levels to take it from, so they count as zero.");
                ed.WriteMessage("\n  Saved as the take-off \"Columns\"; MEXPORT writes it with the others.");

                var at = ed.GetPoint("\nPick a point to draw the column quantity table (Enter to skip): ");
                if (at.Status == PromptStatus.OK)
                {
                    double h = TagHeightMm * Util.MmToDrawingUnits(1.0);
                    Util.EnsureHcwLayer(tr, db, LayerTag);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    MeasureCommands.DrawTable(tr, db, new Point3d(at.Value.X, at.Value.Y - 2.5 * h, 0), ColumnQuantity.Headers, rows, h, LayerTag);
                    var title = new DBText { Height = h * 1.2, TextString = "COLUMN QUANTITIES", Layer = LayerTag, Position = new Point3d(at.Value.X, at.Value.Y - 1.2 * h, 0) };
                    space.AppendEntity(title);
                    tr.AddNewlyCreatedDBObject(title, true);
                }
                tr.Commit();
            }
        }

        [CommandMethod("HCWCOLSCHED")]
        public void ColumnSchedule()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            double mm = Util.MmToDrawingUnits(1.0);
            double h = TagHeightMm * mm;

            // Where the table goes (Enter marks the columns only).
            var pr = ed.GetPoint(new PromptPointOptions("\nTop-left of the column schedule (Enter = mark the columns only): ") { AllowNone = true });
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
            Point3d? at = pr.Status == PromptStatus.OK ? pr.Value.TransformBy(ed.CurrentUserCoordinateSystem) : (Point3d?)null;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                TitleBlockCommands.EnsureRegApp(tr, db);

                // Earlier marks and tables go; they are redrawn from what is on A-COL now.
                int removed = 0;
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    if (TitleBlockCommands.IsKind(ent, KindTag) || TitleBlockCommands.IsKind(ent, KindTable))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                        removed++;
                    }
                }

                var cols = new List<KeyValuePair<Extents3d, ColumnSize>>();
                var blockOf = new Dictionary<int, BlockReference>();
                int skipped = 0;
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased || !IsColumnLayer(ent.Layer)) continue;
                    var circle = ent as Circle;
                    var pl = ent as Polyline;
                    var blk = ent as BlockReference;
                    var blkSize = blk == null ? null : ColumnSize.FromBlockName(BlockOpenings.EffectiveName(tr, blk));
                    if (blk != null)
                    {
                        if (blkSize == null) continue;
                        var half = new Vector3d((blkSize.W * mm) / 2, ((blkSize.Round ? blkSize.W : blkSize.D) * mm) / 2, 0);
                        blockOf[cols.Count] = blk;
                        cols.Add(new KeyValuePair<Extents3d, ColumnSize>(new Extents3d(blk.Position - half, blk.Position + half), blkSize));
                    }
                    else if (circle != null)
                        cols.Add(new KeyValuePair<Extents3d, ColumnSize>(circle.GeometricExtents, new ColumnSize { Round = true, W = 2 * circle.Radius / mm, D = 2 * circle.Radius / mm }));
                    else if (pl != null && pl.Closed && pl.NumberOfVertices == 4 && Rectangle(pl, out double a, out double b))
                        cols.Add(new KeyValuePair<Extents3d, ColumnSize>(pl.GeometricExtents, new ColumnSize { W = Math.Max(a, b) / mm, D = Math.Min(a, b) / mm }));
                    else if (pl != null || circle != null) skipped++;
                }
                if (cols.Count == 0)
                {
                    ed.WriteMessage("\nHCWCOLSCHED: no columns on " + LayerColumn + " or " + MeasureCommands.LayCol + ". Place them with HCWCOLUMN.");
                    return;
                }

                var rows = ColumnMarks.Assign(cols.Select(c => c.Value));
                var markOf = rows.ToDictionary(r => r.Label, r => r.Mark);
                Util.EnsureHcwLayer(tr, db, LayerTag);
                for (int ci = 0; ci < cols.Count; ci++)
                {
                    var c = cols[ci];
                    var rounded = new ColumnSize { Round = c.Value.Round, W = Math.Round(c.Value.W), D = c.Value.Round ? Math.Round(c.Value.W) : Math.Round(c.Value.D) };
                    BlockReference mark;
                    if (blockOf.TryGetValue(ci, out mark))
                    {
                        // A column block carries its own mark in the MARK attribute.
                        foreach (ObjectId aid in mark.AttributeCollection)
                        {
                            var att = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                            if (!string.Equals(att.Tag, "MARK", StringComparison.OrdinalIgnoreCase)) continue;
                            att.UpgradeOpen();
                            att.TextString = markOf[rounded.Label];
                        }
                        continue;
                    }
                    var e = c.Key;
                    var tagAt = new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2.0, e.MaxPoint.Y + 0.7 * h, 0);
                    var text = new DBText
                    {
                        Height = h, TextString = markOf[rounded.Label], Layer = LayerTag,
                        Position = tagAt, HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    text.AlignmentPoint = tagAt;
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                    TitleBlockCommands.Tag(text, KindTag);
                }

                if (at.HasValue)
                {
                    var table = new List<string[]>();
                    foreach (var r in rows) table.Add(new[] { r.Mark, r.Label, r.Count.ToString() });
                    table.Add(new[] { "TOTAL", "", cols.Count.ToString() });
                    var ids = MeasureCommands.DrawTable(tr, db, new Point3d(at.Value.X, at.Value.Y - 2.5 * h, 0),
                        new[] { "Mark", "Size (mm)", "Nos" }, table, h, LayerTag);
                    var title = new DBText { Height = h * 1.2, TextString = "COLUMN SCHEDULE", Layer = LayerTag, Position = new Point3d(at.Value.X, at.Value.Y - 1.2 * h, 0) };
                    ids.Add(space.AppendEntity(title));
                    tr.AddNewlyCreatedDBObject(title, true);
                    foreach (var id in ids)
                        TitleBlockCommands.Tag((Entity)tr.GetObject(id, OpenMode.ForWrite), KindTable);
                }
                // The same marks go into the take-off book, so MSCHED and MCOL see the columns without being typed again.
                {
                    double k = LevelStore.UnitMm;
                    var book = MeasureBook.Load(tr, db);
                    var old = book.Columns.ToList();
                    book.Columns.Clear();
                    foreach (var r in rows)
                    {
                        var sz = cols.Select(c => c.Value).First(c => string.Equals(new ColumnSize { Round = c.Round, W = Math.Round(c.W), D = c.Round ? Math.Round(c.W) : Math.Round(c.D) }.Label, r.Label, StringComparison.Ordinal));
                        book.Columns.Add(new MeasureBook.ColumnSpec { Mark = r.Mark, Width = Math.Round(sz.W) / k, Depth = (sz.Round ? Math.Round(sz.W) : Math.Round(sz.D)) / k, Name = "Column", Count = r.Count });
                        var last = book.Columns[book.Columns.Count - 1];
                        var was = old.FirstOrDefault(o => Math.Abs(o.Width - last.Width) < 0.5 / k && Math.Abs(o.Depth - last.Depth) < 0.5 / k);
                        if (was != null) { last.Height = was.Height; last.Floor = was.Floor; }
                    }
                    book.Save(tr, db);
                }
                tr.Commit();
                ed.WriteMessage("\nHCWCOLSCHED: " + cols.Count + " column(s) in " + rows.Count + " size(s): "
                    + string.Join(", ", rows.Select(r => r.Mark + " " + r.Label + " x" + r.Count)) + "."
                    + (removed > 0 ? " Earlier marks and table replaced." : "")
                    + (skipped > 0 ? " " + skipped + " object(s) on the column layers are not rectangles or circles and were skipped." : ""));
            }
        }

        /// <summary>A column layer: A-COL, or MEASURE-COLUMN where the take-off reads them.</summary>
        private static bool IsColumnLayer(string layer) =>
            string.Equals(layer, LayerColumn, StringComparison.OrdinalIgnoreCase) || string.Equals(layer, "A-COL", StringComparison.OrdinalIgnoreCase) || string.Equals(layer, MeasureCommands.LayCol, StringComparison.OrdinalIgnoreCase);

        private static void AddLine(List<Seg> into, Line ln) =>
            into.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)));

        /// <summary>Centres of the columns already on A-COL (closed polylines and circles).</summary>
        private static List<P2> ExistingCentres(Transaction tr, BlockTableRecord space)
        {
            var res = new List<P2>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !IsColumnLayer(ent.Layer)) continue;
                var blk = ent as BlockReference;
                if (blk != null && ColumnSize.FromBlockName(BlockOpenings.EffectiveName(tr, blk)) != null) { res.Add(new P2(blk.Position.X, blk.Position.Y)); continue; }
                if (!(ent is Circle) && !(ent is Polyline)) continue;
                try
                {
                    var e = ent.GeometricExtents;
                    res.Add(new P2((e.MinPoint.X + e.MaxPoint.X) / 2.0, (e.MinPoint.Y + e.MaxPoint.Y) / 2.0));
                }
                catch (System.Exception) { /* no extents: ignore */ }
            }
            return res;
        }

        /// <summary>True for a closed 4-vertex polyline with straight sides and right-angle corners; gives the two side lengths.</summary>
        private static bool Rectangle(Polyline pl, out double a, out double b)
        {
            a = b = 0;
            var v = new P2[4];
            for (int i = 0; i < 4; i++)
            {
                if (pl.GetSegmentType(i) != SegmentType.Line) return false;
                var p = pl.GetPoint2dAt(i);
                v[i] = new P2(p.X, p.Y);
            }
            var s1 = v[1] - v[0]; var s2 = v[2] - v[1]; var s3 = v[3] - v[2]; var s4 = v[0] - v[3];
            a = s1.Length; b = s2.Length;
            if (a < 1e-9 || b < 1e-9) return false;
            double tol = 1e-3 * Math.Max(a, b);
            return Math.Abs(P2.Dot(s1, s2)) <= tol * Math.Max(a, b)
                && Math.Abs(a - s3.Length) <= tol && Math.Abs(b - s4.Length) <= tol
                && Math.Abs(P2.Dot(s2, s3)) <= tol * Math.Max(a, b);
        }

        /// <summary>A solid fill inside the outline, on its own layer, not tied to the outline.</summary>
        private static void AddFill(Transaction tr, BlockTableRecord space, ObjectId outline)
        {
            var hatch = new Hatch();
            space.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);
            hatch.Layer = LayerFill;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = false;
            hatch.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { outline });
            hatch.EvaluateHatch(true);
        }
    }
}
