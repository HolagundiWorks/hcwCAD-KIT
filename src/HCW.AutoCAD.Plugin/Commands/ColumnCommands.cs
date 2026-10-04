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
        internal const string LayerColumn = "A-COL";
        private const string LayerFill = "AN-HATCH", LayerTag = "AN-TEXT";
        private const string KindTag = "COLTAG", KindTable = "COLTABLE";
        private const double TagHeightMm = 250, SkipRadiusMm = 50;

        private static string _size = "230x450";
        private static bool _fill = true;

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

            int placed = 0, skipped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LayerColumn);
                if (_fill) Util.EnsureHcwLayer(tr, db, LayerFill);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var existing = ExistingCentres(tr, space);
                double skipR = SkipRadiusMm * mm;
                double w = size.W * mm, d = size.D * mm;

                foreach (var gp in points)
                {
                    if (existing.Any(c => c.DistanceTo(gp.Pt) <= skipR)) { skipped++; continue; }
                    Entity outline;
                    if (size.Round)
                        outline = new Circle(new Point3d(gp.Pt.X, gp.Pt.Y, 0), Vector3d.ZAxis, w / 2.0);
                    else
                    {
                        var pl = new Polyline();
                        double cos = Math.Cos(gp.Angle), sin = Math.Sin(gp.Angle);
                        var corners = new[] { new P2(-w / 2, -d / 2), new P2(w / 2, -d / 2), new P2(w / 2, d / 2), new P2(-w / 2, d / 2) };
                        for (int i = 0; i < 4; i++)
                        {
                            double x = gp.Pt.X + corners[i].X * cos - corners[i].Y * sin;
                            double y = gp.Pt.Y + corners[i].X * sin + corners[i].Y * cos;
                            pl.AddVertexAt(i, new Point2d(x, y), 0, 0, 0);
                        }
                        pl.Closed = true;
                        outline = pl;
                    }
                    outline.Layer = LayerColumn;
                    space.AppendEntity(outline);
                    tr.AddNewlyCreatedDBObject(outline, true);
                    if (_fill) AddFill(tr, space, outline.ObjectId);
                    existing.Add(gp.Pt);
                    placed++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWCOLUMN: " + placed + " column(s) " + size.Label + " mm on " + LayerColumn
                + (skipped > 0 ? ", " + skipped + " intersection(s) already had one." : "."));
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
                int skipped = 0;
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased || !string.Equals(ent.Layer, LayerColumn, StringComparison.OrdinalIgnoreCase)) continue;
                    var circle = ent as Circle;
                    var pl = ent as Polyline;
                    if (circle != null)
                        cols.Add(new KeyValuePair<Extents3d, ColumnSize>(circle.GeometricExtents, new ColumnSize { Round = true, W = 2 * circle.Radius / mm, D = 2 * circle.Radius / mm }));
                    else if (pl != null && pl.Closed && pl.NumberOfVertices == 4 && Rectangle(pl, out double a, out double b))
                        cols.Add(new KeyValuePair<Extents3d, ColumnSize>(pl.GeometricExtents, new ColumnSize { W = Math.Max(a, b) / mm, D = Math.Min(a, b) / mm }));
                    else if (pl != null || circle != null) skipped++;
                }
                if (cols.Count == 0)
                {
                    ed.WriteMessage("\nHCWCOLSCHED: no columns on " + LayerColumn + ". Place them with HCWCOLUMN.");
                    return;
                }

                var rows = ColumnMarks.Assign(cols.Select(c => c.Value));
                var markOf = rows.ToDictionary(r => r.Label, r => r.Mark);
                Util.EnsureHcwLayer(tr, db, LayerTag);
                foreach (var c in cols)
                {
                    var rounded = new ColumnSize { Round = c.Value.Round, W = Math.Round(c.Value.W), D = c.Value.Round ? Math.Round(c.Value.W) : Math.Round(c.Value.D) };
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
                tr.Commit();
                ed.WriteMessage("\nHCWCOLSCHED: " + cols.Count + " column(s) in " + rows.Count + " size(s): "
                    + string.Join(", ", rows.Select(r => r.Mark + " " + r.Label + " x" + r.Count)) + "."
                    + (removed > 0 ? " Earlier marks and table replaced." : "")
                    + (skipped > 0 ? " " + skipped + " object(s) on " + LayerColumn + " are not rectangles or circles and were skipped." : ""));
            }
        }

        private static void AddLine(List<Seg> into, Line ln) =>
            into.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)));

        /// <summary>Centres of the columns already on A-COL (closed polylines and circles).</summary>
        private static List<P2> ExistingCentres(Transaction tr, BlockTableRecord space)
        {
            var res = new List<P2>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !string.Equals(ent.Layer, LayerColumn, StringComparison.OrdinalIgnoreCase)) continue;
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
