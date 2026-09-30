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
    /// The auto dimension tool set for working drawings.
    ///
    /// AUTODIM      dimension chains around the outside of a plan: openings, structure, grid and overall.
    /// AUTODIMROOM  inside each room: clear width and depth, and door and window positions along each wall.
    /// AUTODIMWALL  one aligned dimension on each selected wall segment at any angle, and radius on arcs.
    /// AUTODIMCLEAR removes what these made (every dimension is tagged), and nothing drawn by hand.
    ///
    /// Walls are read as faces: every vertex of a selected line or polyline is a point. Openings come from
    /// deduction lines: the lines on MEASURE-DEDUCT and the line inside every door or window block.
    /// Distances are set in plotted millimetres (settings.ini), so the result is the same on paper at any scale.
    /// A dimension too short for its text is moved onto a second or third row instead of overprinting its neighbours.
    /// </summary>
    public class AutoDimCommands
    {
        private const string AppName = "HCW_AUTODIM";
        private const string DimLayer = "AN-DIMS";

        private static double _scale = 100;
        private static string _sides = "All";
        private static string _levels = "All";
        private static string _wallSide = "Outward";

        private enum Side { Bottom, Top, Left, Right }

        /// <summary>The geometry AUTODIM reads: wall points, opening jamb points, grid lines and columns.</summary>
        private class Plan
        {
            public readonly List<Point3d> Structural = new List<Point3d>();
            public readonly List<Point3d> Jambs = new List<Point3d>();
            public readonly List<double> GridX = new List<double>();
            public readonly List<double> GridY = new List<double>();
            public int Skipped;
        }

        // ------------------------------------------------------------------ AUTODIM

        [CommandMethod("AUTODIM")]
        public void AutoDim()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var filter = new SelectionFilter(new[] { new TypedValue(0, "LINE,LWPOLYLINE,POLYLINE") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the plan walls (lines and polylines): " }, filter);
            if (psr.Status != PromptStatus.OK) return;

            if (!AskKeyword(ed, "Sides", new[] { "All", "Top", "Bottom", "Left", "Right" }, ref _sides)) return;
            if (!AskKeyword(ed, "Chains", new[] { "All", "Overall", "Grid", "Structure", "Openings" }, ref _levels)) return;
            if (!AskScale(ed)) return;

            double mm = Util.MmToDrawingUnits(1.0);
            double step = Settings.GetDouble("AutoDimStepMm", 10) * _scale * mm;
            double gap = Settings.GetDouble("AutoDimGapMm", 12) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;
            double merge = 5 * mm;                                                // 5 mm of real size
            double band = Settings.GetDouble("AutoDimBandM", 0.6) * 1000 * mm;    // outer band, real size

            var plan = new Plan();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null) continue;
                    bool deduction = string.Equals(curve.Layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase);
                    Collect(curve, deduction ? plan.Jambs : plan.Structural, ref plan.Skipped);
                }
                GatherFromDrawing(tr, db, plan);
                tr.Commit();
            }
            if (plan.Structural.Count == 0)
            {
                ed.WriteMessage("\nAUTODIM: no axis-aligned wall geometry was selected.");
                return;
            }

            double minX = plan.Structural.Min(p => p.X), maxX = plan.Structural.Max(p => p.X);
            double minY = plan.Structural.Min(p => p.Y), maxY = plan.Structural.Max(p => p.Y);
            var sides = _sides == "All"
                ? new[] { Side.Bottom, Side.Top, Side.Left, Side.Right }
                : new[] { (Side)Enum.Parse(typeof(Side), _sides) };

            int dropped = 0, staggered = 0;
            int made;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sink = new Sink(tr, db);
                foreach (var side in sides)
                {
                    bool horizontal = side == Side.Bottom || side == Side.Top;
                    Func<Point3d, double> along = p => horizontal ? p.X : p.Y;
                    Func<Point3d, bool> inBand = p =>
                        side == Side.Bottom ? p.Y <= minY + band :
                        side == Side.Top ? p.Y >= maxY - band :
                        side == Side.Left ? p.X <= minX + band : p.X >= maxX - band;

                    double lo = horizontal ? minX : minY, hi = horizontal ? maxX : maxY;
                    var wall = DimChains.Merge(plan.Structural.Where(inBand).Select(along), merge);
                    var all = DimChains.Merge(plan.Structural.Where(inBand).Concat(plan.Jambs.Where(inBand)).Select(along), merge);
                    var overall = DimChains.Merge(plan.Structural.Select(along), merge);
                    var grid = DimChains.Merge((horizontal ? plan.GridX : plan.GridY).Where(v => v >= lo - band && v <= hi + band), merge);

                    // nearest the plan first: openings, structure, grid, overall
                    var chains = new List<List<KeyValuePair<double, double>>>();
                    if (_levels == "All" || _levels == "Openings")
                        if (all.Count > wall.Count) chains.Add(DimChains.Segments(all, minLen));
                    if (_levels == "All" || _levels == "Structure")
                        chains.Add(DimChains.Segments(wall, minLen));
                    if ((_levels == "All" || _levels == "Grid") && grid.Count >= 2)
                        chains.Add(DimChains.Segments(grid, minLen));
                    if (_levels == "All" || _levels == "Overall")
                        chains.Add(DimChains.Overall(overall, minLen));

                    // a chain that repeats the one inside it adds nothing
                    for (int i = chains.Count - 1; i > 0; i--)
                        if (DimChains.Same(chains[i], chains[i - 1], merge)) { dropped += chains[i].Count; chains.RemoveAt(i); }
                    chains.RemoveAll(c => c.Count == 0);

                    for (int level = 0; level < chains.Count; level++)
                    {
                        double offset = gap + level * step;
                        double edge = side == Side.Bottom ? minY : side == Side.Top ? maxY : side == Side.Left ? minX : maxX;
                        double outward = side == Side.Bottom || side == Side.Left ? -1 : 1;
                        staggered += DrawChain(sink, chains[level], horizontal, edge, outward * offset, step);
                    }
                }
                made = sink.Count;
                tr.Commit();
            }

            ed.WriteMessage("\nAUTODIM: " + made + " dimension(s) on " + DimLayer + " at 1:" + _scale
                + (staggered > 0 ? "; " + staggered + " short one(s) moved to a second row" : "")
                + (dropped > 0 ? "; " + dropped + " repeated dimension(s) left out" : "")
                + (plan.Skipped > 0 ? "; " + plan.Skipped + " angled or curved segment(s) skipped (use AUTODIMWALL)" : "") + ".");
        }

        // ------------------------------------------------------------------ AUTODIMROOM

        [CommandMethod("AUTODIMROOM")]
        public void AutoDimRoom()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var filter = new SelectionFilter(new[] { new TypedValue(0, "LWPOLYLINE") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the room outlines (closed polylines, such as ROOM-RECT or MEASURE-FLOOR): " }, filter);
            if (psr.Status != PromptStatus.OK) return;
            if (!AskScale(ed)) return;

            double mm = Util.MmToDrawingUnits(1.0);
            double step = Settings.GetDouble("AutoDimStepMm", 10) * _scale * mm;
            double inset = Settings.GetDouble("AutoDimRoomInsetMm", 8) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;
            double merge = 5 * mm;
            double band = Settings.GetDouble("AutoDimBandM", 0.6) * 1000 * mm;

            var plan = new Plan();
            var rooms = new List<List<Point3d>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var poly = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Polyline;
                    if (poly == null || !poly.Closed || poly.NumberOfVertices < 3) continue;
                    var pts = new List<Point3d>();
                    for (int i = 0; i < poly.NumberOfVertices; i++) pts.Add(poly.GetPoint3dAt(i));
                    rooms.Add(pts);
                }
                GatherFromDrawing(tr, db, plan);
                tr.Commit();
            }
            if (rooms.Count == 0)
            {
                ed.WriteMessage("\nAUTODIMROOM: select closed polylines.");
                return;
            }

            int made, staggered = 0, skipped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sink = new Sink(tr, db);
                foreach (var room in rooms)
                {
                    var cen = new Point3d(room.Average(p => p.X), room.Average(p => p.Y), 0);
                    double minX = room.Min(p => p.X), maxX = room.Max(p => p.X);
                    double minY = room.Min(p => p.Y), maxY = room.Max(p => p.Y);
                    bool rectangle = IsRectangle(room, merge);

                    if (rectangle)
                    {
                        // openings along each wall, nearest the wall; then the clear width and depth beyond them
                        int bottomLevels = 0, leftLevels = 0;
                        foreach (var e in new[] { Side.Bottom, Side.Top, Side.Left, Side.Right })
                        {
                            bool horizontal = e == Side.Bottom || e == Side.Top;
                            double edge = e == Side.Bottom ? minY : e == Side.Top ? maxY : e == Side.Left ? minX : maxX;
                            double lo = horizontal ? minX : minY, hi = horizontal ? maxX : maxY;
                            double inward = (e == Side.Bottom || e == Side.Left) ? 1 : -1;

                            var onEdge = plan.Jambs.Where(p => Math.Abs((horizontal ? p.Y : p.X) - edge) <= band
                                && (horizontal ? p.X : p.Y) >= lo - merge && (horizontal ? p.X : p.Y) <= hi + merge)
                                .Select(p => horizontal ? p.X : p.Y).ToList();
                            if (onEdge.Count == 0) continue;
                            var pts = DimChains.Merge(new[] { lo, hi }.Concat(onEdge), merge);
                            var segs = DimChains.Segments(pts, minLen);
                            if (segs.Count == 0) continue;
                            staggered += DrawChain(sink, segs, horizontal, edge, inward * inset, step);
                            if (e == Side.Bottom) bottomLevels = 1;
                            if (e == Side.Left) leftLevels = 1;
                        }
                        var width = DimChains.Overall(new[] { minX, maxX }, minLen);
                        var depth = DimChains.Overall(new[] { minY, maxY }, minLen);
                        staggered += DrawChain(sink, width, true, minY, inset + bottomLevels * step, step);
                        staggered += DrawChain(sink, depth, false, minX, inset + leftLevels * step, step);
                    }
                    else
                    {
                        // any other outline: each straight edge, dimensioned along its own direction inside the room
                        for (int i = 0; i < room.Count; i++)
                        {
                            var a = room[i];
                            var b = room[(i + 1) % room.Count];
                            if (a.DistanceTo(b) < minLen) continue;
                            if (!Orthogonal(a, b)) { skipped++; continue; }
                            bool horizontal = Math.Abs(a.Y - b.Y) <= Math.Abs(a.X - b.X);
                            double u1 = horizontal ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
                            double u2 = horizontal ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
                            double edge = horizontal ? a.Y : a.X;
                            double centreOnAxis = horizontal ? cen.Y : cen.X;
                            double inward = centreOnAxis >= edge ? 1 : -1;
                            var seg = new List<KeyValuePair<double, double>> { new KeyValuePair<double, double>(u1, u2) };
                            staggered += DrawChain(sink, seg, horizontal, edge, inward * inset, step);
                        }
                    }
                }
                made = sink.Count;
                tr.Commit();
            }
            ed.WriteMessage("\nAUTODIMROOM: " + made + " dimension(s) in " + rooms.Count + " room(s) at 1:" + _scale
                + (staggered > 0 ? "; " + staggered + " short one(s) moved to a second row" : "")
                + (skipped > 0 ? "; " + skipped + " angled edge(s) skipped" : "") + ".");
        }

        // ------------------------------------------------------------------ AUTODIMWALL

        [CommandMethod("AUTODIMWALL")]
        public void AutoDimWall()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var filter = new SelectionFilter(new[] { new TypedValue(0, "LINE,LWPOLYLINE,POLYLINE,ARC") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect wall segments at any angle: " }, filter);
            if (psr.Status != PromptStatus.OK) return;
            if (!AskKeyword(ed, "Offset side", new[] { "Outward", "Inward" }, ref _wallSide)) return;
            if (!AskScale(ed)) return;

            double mm = Util.MmToDrawingUnits(1.0);
            double offset = Settings.GetDouble("AutoDimGapMm", 12) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;

            var segments = new List<KeyValuePair<Point3d, Point3d>>();
            var arcs = new List<Arc>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    var line = ent as Line;
                    var poly = ent as Polyline;
                    var arc = ent as Arc;
                    if (line != null) segments.Add(new KeyValuePair<Point3d, Point3d>(line.StartPoint, line.EndPoint));
                    else if (arc != null)
                        arcs.Add((Arc)arc.Clone());
                    else if (poly != null)
                    {
                        int n = poly.NumberOfVertices;
                        int last = poly.Closed ? n : n - 1;
                        for (int i = 0; i < last; i++)
                        {
                            if (poly.GetSegmentType(i) != SegmentType.Line) continue;
                            segments.Add(new KeyValuePair<Point3d, Point3d>(poly.GetPoint3dAt(i), poly.GetPoint3dAt((i + 1) % n)));
                        }
                    }
                }
                tr.Commit();
            }
            if (segments.Count == 0 && arcs.Count == 0)
            {
                ed.WriteMessage("\nAUTODIMWALL: nothing to dimension.");
                return;
            }

            // "outward" is away from the middle of everything selected
            var mids = segments.Select(s => new Point3d((s.Key.X + s.Value.X) / 2, (s.Key.Y + s.Value.Y) / 2, 0))
                .Concat(arcs.Select(a => new Point3d(a.Center.X, a.Center.Y, 0))).ToList();
            var centre = new Point3d(mids.Average(p => p.X), mids.Average(p => p.Y), 0);
            double sign = _wallSide == "Inward" ? -1 : 1;

            int made, radii = 0, dropped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sink = new Sink(tr, db);
                foreach (var seg in segments)
                {
                    var d = seg.Value - seg.Key;
                    if (d.Length < minLen) { dropped++; continue; }
                    var n = new Vector3d(-d.Y, d.X, 0).GetNormal();
                    var mid = new Point3d((seg.Key.X + seg.Value.X) / 2, (seg.Key.Y + seg.Value.Y) / 2, 0);
                    double away = (mid - centre).DotProduct(n);
                    if (Math.Abs(away) < 1e-9) away = 1;
                    var line = mid + n * (Math.Sign(away) * sign * offset);
                    sink.Aligned(new Point3d(seg.Key.X, seg.Key.Y, 0), new Point3d(seg.Value.X, seg.Value.Y, 0), line);
                }
                foreach (var arc in arcs)
                {
                    var mid = arc.GetPointAtParameter((arc.StartParam + arc.EndParam) / 2);
                    sink.Radial(new Point3d(arc.Center.X, arc.Center.Y, 0), new Point3d(mid.X, mid.Y, 0), offset);
                    radii++;
                    arc.Dispose();
                }
                made = sink.Count;
                tr.Commit();
            }
            ed.WriteMessage("\nAUTODIMWALL: " + (made - radii) + " length and " + radii + " radius dimension(s) at 1:" + _scale
                + (dropped > 0 ? "; " + dropped + " too short" : "") + ".");
        }

        // ------------------------------------------------------------------ clear

        /// <summary>Deletes the dimensions these commands made, and only those.</summary>
        [CommandMethod("AUTODIMCLEAR")]
        public void Clear()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            int removed = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var dim = tr.GetObject(id, OpenMode.ForRead) as Dimension;
                    if (dim == null || dim.GetXDataForApplication(AppName) == null) continue;
                    dim.UpgradeOpen();
                    dim.Erase();
                    removed++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nAUTODIMCLEAR: " + removed + " dimension(s) removed.");
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Creates dimensions in the current space, tagged and on the dimension layer.</summary>
        private class Sink
        {
            private readonly Transaction _tr;
            private readonly BlockTableRecord _btr;
            private readonly ObjectId _style;
            public readonly double TextHeight;
            public int Count;

            public Sink(Transaction tr, Database db)
            {
                _tr = tr;
                Util.EnsureLayer(tr, db, DimLayer, 8);
                var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!apps.Has(AppName))
                {
                    apps.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = AppName };
                    apps.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                var styles = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                string wanted = Settings.Get("AutoDimStyle", "HCW-WORKING");
                _style = styles.Has(wanted) ? styles[wanted] : db.Dimstyle;
                var style = (DimStyleTableRecord)tr.GetObject(_style, OpenMode.ForRead);
                TextHeight = style.Dimtxt * (style.Dimscale > 0 ? style.Dimscale : 1.0);
                _btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            }

            public AlignedDimension Aligned(Point3d a, Point3d b, Point3d line)
            {
                var dim = new AlignedDimension(a, b, line, "", _style);
                Add(dim);
                return dim;
            }

            public void Radial(Point3d centre, Point3d onArc, double leader)
            {
                Add(new RadialDimension(centre, onArc, leader, "", _style));
            }

            private void Add(Dimension dim)
            {
                dim.Layer = DimLayer;
                dim.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "AUTODIM"));
                _btr.AppendEntity(dim);
                _tr.AddNewlyCreatedDBObject(dim, true);
                Count++;
            }
        }

        /// <summary>
        /// Draws one chain of segments along a horizontal or vertical side. <paramref name="edge"/> is where the extension
        /// lines start (the plan edge, on the perpendicular axis); the dimension line sits <paramref name="lineOffset"/> away
        /// from it (signed). Text of a segment too short for it moves to a further row, away from the plan in the same direction.
        /// Returns how many texts were moved.
        /// </summary>
        private static int DrawChain(Sink sink, List<KeyValuePair<double, double>> segments, bool horizontal,
            double edge, double lineOffset, double step)
        {
            if (segments.Count == 0) return 0;
            double textH = sink.TextHeight;
            // text width from the longest number in the chain; the text is about 0.75 of its height wide per character
            int chars = segments.Max(s => (s.Value - s.Key).ToString("0.###", CultureInfo.InvariantCulture).Length);
            double textWidth = chars * textH * Settings.GetDouble("AutoDimTextWidthFactor", 0.75);
            var rows = DimChains.Rows(segments, textWidth, 2);
            double rowStep = Math.Min(1.5 * textH, step / 2.0);

            int moved = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                double u1 = segments[i].Key, u2 = segments[i].Value;
                Point3d a, b, line;
                if (horizontal)
                {
                    a = new Point3d(u1, edge, 0); b = new Point3d(u2, edge, 0); line = new Point3d(u1, edge + lineOffset, 0);
                }
                else
                {
                    a = new Point3d(edge, u1, 0); b = new Point3d(edge, u2, 0); line = new Point3d(edge + lineOffset, u1, 0);
                }
                var dim = sink.Aligned(a, b, line);
                if (rows[i] > 0)
                {
                    double mid = (u1 + u2) / 2.0;
                    double away = Math.Sign(lineOffset) * rows[i] * rowStep;
                    double lineCoord = edge + lineOffset;
                    dim.Dimtmove = 1; // text moved off the line keeps a leader
                    dim.TextPosition = horizontal
                        ? new Point3d(mid, lineCoord + away, 0)
                        : new Point3d(lineCoord + away, mid, 0);
                    moved++;
                }
            }
            return moved;
        }

        /// <summary>Everything AUTODIM and AUTODIMROOM read without being asked: block and layer deduction lines, grid lines, columns.</summary>
        private static void GatherFromDrawing(Transaction tr, Database db, Plan plan)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (var found in BlockOpenings.Collect(tr, space))
                Collect(found.Curve, plan.Jambs, ref plan.Skipped);

            var gridLayers = Layers("AutoDimGridLayers", "AN-GRID;A-GRID");
            var columnLayers = Layers("AutoDimColumnLayers", "MEASURE-COLUMN;S-COLUMN");
            double tol = Util.MmToDrawingUnits(1.0);
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                if (ent is Curve && string.Equals(ent.Layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase))
                    Collect((Curve)ent, plan.Jambs, ref plan.Skipped);
                else if (ent is Line && gridLayers.Contains(ent.Layer))
                {
                    var line = (Line)ent;
                    if (Math.Abs(line.StartPoint.X - line.EndPoint.X) <= tol) plan.GridX.Add(line.StartPoint.X);
                    else if (Math.Abs(line.StartPoint.Y - line.EndPoint.Y) <= tol) plan.GridY.Add(line.StartPoint.Y);
                }
                else if (ent is Polyline && columnLayers.Contains(ent.Layer))
                {
                    var poly = (Polyline)ent;
                    if (!poly.Closed) continue;
                    for (int i = 0; i < poly.NumberOfVertices; i++) plan.Structural.Add(poly.GetPoint3dAt(i));
                }
            }
        }

        private static HashSet<string> Layers(string key, string fallback)
        {
            return new HashSet<string>(Settings.Get(key, fallback).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Adds the end points of a horizontal or vertical curve (every vertex for a polyline); counts angled segments.</summary>
        private static void Collect(Curve curve, List<Point3d> into, ref int skipped)
        {
            var poly = curve as Polyline;
            if (poly != null)
            {
                int n = poly.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    var p = poly.GetPoint3dAt(i);
                    into.Add(p);
                    int next = i + 1 < n ? i + 1 : (poly.Closed ? 0 : -1);
                    if (next >= 0 && !Orthogonal(p, poly.GetPoint3dAt(next))) skipped++;
                }
                return;
            }
            var line = curve as Line;
            if (line != null)
            {
                if (!Orthogonal(line.StartPoint, line.EndPoint)) { skipped++; return; }
                into.Add(line.StartPoint);
                into.Add(line.EndPoint);
                return;
            }
            skipped++;
        }

        private static bool Orthogonal(Point3d a, Point3d b)
        {
            double tol = Util.MmToDrawingUnits(1.0);
            return Math.Abs(a.X - b.X) <= tol || Math.Abs(a.Y - b.Y) <= tol;
        }

        /// <summary>Four corners, every edge horizontal or vertical.</summary>
        private static bool IsRectangle(List<Point3d> pts, double tol)
        {
            if (pts.Count != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % 4];
                if (Math.Abs(a.X - b.X) > tol && Math.Abs(a.Y - b.Y) > tol) return false;
            }
            return true;
        }

        private static bool AskKeyword(Editor ed, string label, string[] options, ref string current)
        {
            var opt = new PromptKeywordOptions("\n" + label + " [" + string.Join("/", options) + "] <" + current + ">: ") { AllowNone = true };
            foreach (var k in options) opt.Keywords.Add(k);
            var res = ed.GetKeywords(opt);
            if (res.Status == PromptStatus.OK) current = res.StringResult;
            else if (res.Status != PromptStatus.None) return false;
            return true;
        }

        private static bool AskScale(Editor ed)
        {
            var res = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (res.Status != PromptStatus.OK) return false;
            _scale = res.Value;
            return true;
        }
    }
}
