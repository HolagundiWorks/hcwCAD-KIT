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
    /// AUTODIMROOM  inside each room: clear width and depth, door and window positions along each wall, and furniture sizes.
    /// AUTODIMWALL  one aligned dimension on each selected wall segment at any angle, and radius on arcs.
    /// AUTODIMCLEAR removes what these made (every dimension is tagged), and nothing drawn by hand.
    ///
    /// Layers are chosen in a dialog (walls, windows and doors, columns, furniture). Walls are read as faces: every
    /// vertex of a wall line or polyline is a point. Openings come from the window layers: the deduction line inside a
    /// door or window block (or the block's extents), and lines or polylines on those layers.
    /// Distances are set in plotted millimetres (settings.ini), so the result is the same on paper at any scale.
    /// A dimension too short for its text is moved onto a second or third row instead of overprinting its neighbours.
    /// </summary>
    public class AutoDimCommands
    {
        private const string AppName = "HCW_AUTODIM";
        internal const string AutoDimApp = AppName;
        private const string DimLayer = "AN-DIMS";

        private static double _scale = 100;
        private static string _sides = "All";
        private static string _levels = "All";
        private static string _wallSide = "Outward";

        /// <summary>The geometry AUTODIM reads: wall points, opening jamb points, grid lines and columns.</summary>
        private class Plan
        {
            public readonly List<Point3d> Structural = new List<Point3d>();
            public readonly List<Point3d> Jambs = new List<Point3d>();
            public readonly List<double> GridX = new List<double>();
            public readonly List<double> GridY = new List<double>();
            /// <summary>Extents of each loose piece of window and door geometry, grouped into openings once the units are known.</summary>
            public readonly List<Box> WindowBoxes = new List<Box>();
            public readonly List<WallSegment> WallSegments = new List<WallSegment>();
            public int GeometryOpenings;
            public int Skipped;
        }

        // ------------------------------------------------------------------ AUTODIM

        [CommandMethod("AUTODIM")]
        public void AutoDim()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var choice = AskLayers(ed, db, "Furniture is switched off with the other layers; Room Dimensions dimensions it.");
            if (choice == null) return;
            if (!AskKeyword(ed, "Sides", new[] { "All", "Top", "Bottom", "Left", "Right" }, ref _sides)) return;
            if (!AskKeyword(ed, "Chains", new[] { "All", "Overall", "Grid", "Structure", "Openings" }, ref _levels)) return;
            if (!AskScale(ed)) return;

            var plan = new Plan();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ReadPlan(tr, db, choice, plan, null);
                tr.Commit();
            }
            if (plan.Structural.Count == 0)
            {
                ed.WriteMessage("\nAUTODIM: no horizontal or vertical wall lines were found on the wall layers (" + string.Join(", ", choice.Walls) + ")"
                    + (plan.Skipped > 0 ? " (" + plan.Skipped + " angled or curved segment(s) skipped; use AUTODIMWALL for those)" : "") + ".");
                return;
            }

            double spanX = plan.Structural.Max(p => p.X) - plan.Structural.Min(p => p.X);
            double spanY = plan.Structural.Max(p => p.Y) - plan.Structural.Min(p => p.Y);
            double mm = ResolveUnitsPerMm(ed, Math.Max(spanX, spanY));
            if (mm <= 0) return;

            FinishOpenings(plan, mm);
            double step = Settings.GetDouble("AutoDimStepMm", 10) * _scale * mm;
            double gap = Settings.GetDouble("AutoDimGapMm", 12) * _scale * mm;
            var input = new PlanInput
            {
                Band = Settings.GetDouble("AutoDimBandM", 0.6) * 1000 * mm,      // outer band, real size
                Merge = 5 * mm,                                                   // 5 mm of real size
                MinLength = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm,
                Levels = _levels
            };
            input.Structural.AddRange(plan.Structural.Select(p => new PlanPoint(p.X, p.Y)));
            input.Jambs.AddRange(plan.Jambs.Select(p => new PlanPoint(p.X, p.Y)));
            input.GridX.AddRange(plan.GridX);
            input.GridY.AddRange(plan.GridY);

            var sides = _sides == "All"
                ? new[] { PlanSide.Bottom, PlanSide.Top, PlanSide.Left, PlanSide.Right }
                : new[] { (PlanSide)Enum.Parse(typeof(PlanSide), _sides) };
            int repeated;
            var chains = DimPlanner.Chains(input, sides, out repeated);

            ed.WriteMessage("\nAUTODIM: read " + plan.Structural.Count + " wall point(s), " + plan.Jambs.Count + " opening point(s)"
                + (plan.GeometryOpenings > 0 ? " (" + plan.GeometryOpenings + " opening(s) grouped from window and door geometry)" : "") + ", "
                + (plan.GridX.Count + plan.GridY.Count) + " grid line(s); plan " + spanX.ToString("0.##", CultureInfo.InvariantCulture)
                + " x " + spanY.ToString("0.##", CultureInfo.InvariantCulture) + " drawing units.");
            if (chains.Count == 0)
            {
                ed.WriteMessage("\nAUTODIM: nothing to dimension. Every chain was shorter than " + input.MinLength.ToString("0.###", CultureInfo.InvariantCulture)
                    + " drawing units (" + Settings.GetDouble("AutoDimMinMm", 3) + " mm plotted at 1:" + _scale + "), or the same as the chain inside it."
                    + " Check the plot scale and the drawing units.");
                return;
            }

            double minX = plan.Structural.Min(p => p.X), maxX = plan.Structural.Max(p => p.X);
            double minY = plan.Structural.Min(p => p.Y), maxY = plan.Structural.Max(p => p.Y);
            int made, staggered = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sink = new Sink(tr, db);
                foreach (var chain in chains)
                {
                    bool horizontal = chain.Side == PlanSide.Bottom || chain.Side == PlanSide.Top;
                    double edge = chain.Side == PlanSide.Bottom ? minY : chain.Side == PlanSide.Top ? maxY : chain.Side == PlanSide.Left ? minX : maxX;
                    double outward = chain.Side == PlanSide.Bottom || chain.Side == PlanSide.Left ? -1 : 1;
                    staggered += DrawChain(sink, chain.Segments, horizontal, edge, outward * (gap + chain.Level * step), step);
                }
                made = sink.Count;
                tr.Commit();
            }
            int tiedA = DimAnchors.Associate(db, choice.Walls);
            Isolate(db, choice);

            ed.WriteMessage("\nAUTODIM: " + made + " dimension(s) on " + DimLayer + " at 1:" + _scale
                + (tiedA > 0 ? "; " + tiedA + " follow their walls when moved (HCWLIVE)" : "")
                + (staggered > 0 ? "; " + staggered + " short one(s) moved to a second row" : "")
                + (repeated > 0 ? "; " + repeated + " repeated dimension(s) left out" : "")
                + (plan.Skipped > 0 ? "; " + plan.Skipped + " angled or curved segment(s) skipped (use AUTODIMWALL)" : "") + ".");
        }

        // ------------------------------------------------------------------ AUTODIMROOM

        [CommandMethod("AUTODIMROOM")]
        public void AutoDimRoom()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var choice = AskLayers(ed, db, "Furniture blocks inside each room get their width and depth dimensioned.");
            if (choice == null) return;
            var filter = new SelectionFilter(new[] { new TypedValue(0, "LWPOLYLINE") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the room outlines (closed polylines, such as ROOM-RECT or MEASURE-FLOOR): " }, filter);
            if (psr.Status != PromptStatus.OK) return;
            if (!AskScale(ed)) return;

            var plan = new Plan();
            var furniture = new List<Extents3d>();
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
                ReadPlan(tr, db, choice, plan, furniture);
                tr.Commit();
            }
            if (rooms.Count == 0)
            {
                ed.WriteMessage("\nAUTODIMROOM: select closed polylines.");
                return;
            }

            double roomSpan = rooms.Max(r => Math.Max(r.Max(p => p.X) - r.Min(p => p.X), r.Max(p => p.Y) - r.Min(p => p.Y)));
            double mm = ResolveUnitsPerMm(ed, roomSpan, 1000.0);
            if (mm <= 0) return;
            FinishOpenings(plan, mm);
            double step = Settings.GetDouble("AutoDimStepMm", 10) * _scale * mm;
            double inset = Settings.GetDouble("AutoDimRoomInsetMm", 8) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;
            double merge = 5 * mm;
            double band = Settings.GetDouble("AutoDimBandM", 0.6) * 1000 * mm;

            int made, staggered = 0, skipped = 0, furnished = 0;
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
                        foreach (var e in new[] { PlanSide.Bottom, PlanSide.Top, PlanSide.Left, PlanSide.Right })
                        {
                            bool horizontal = e == PlanSide.Bottom || e == PlanSide.Top;
                            double edge = e == PlanSide.Bottom ? minY : e == PlanSide.Top ? maxY : e == PlanSide.Left ? minX : maxX;
                            double lo = horizontal ? minX : minY, hi = horizontal ? maxX : maxY;
                            double inward = (e == PlanSide.Bottom || e == PlanSide.Left) ? 1 : -1;

                            var onEdge = plan.Jambs.Where(p => Math.Abs((horizontal ? p.Y : p.X) - edge) <= band
                                && (horizontal ? p.X : p.Y) >= lo - merge && (horizontal ? p.X : p.Y) <= hi + merge)
                                .Select(p => horizontal ? p.X : p.Y).ToList();
                            if (onEdge.Count == 0) continue;
                            var pts = DimChains.Merge(new[] { lo, hi }.Concat(onEdge), merge);
                            var segs = DimChains.Segments(pts, minLen);
                            if (segs.Count == 0) continue;
                            staggered += DrawChain(sink, segs, horizontal, edge, inward * inset, step);
                            if (e == PlanSide.Bottom) bottomLevels = 1;
                            if (e == PlanSide.Left) leftLevels = 1;
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

                    // furniture inside this room: width under it, depth beside it
                    double clear = Settings.GetDouble("AutoDimFurnitureOffsetMm", 6) * _scale * mm;
                    foreach (var box in furniture)
                    {
                        var c = new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0);
                        if (c.X < minX || c.X > maxX || c.Y < minY || c.Y > maxY) continue;
                        var w = DimChains.Overall(new[] { box.MinPoint.X, box.MaxPoint.X }, minLen);
                        var d = DimChains.Overall(new[] { box.MinPoint.Y, box.MaxPoint.Y }, minLen);
                        furnished += DrawChain(sink, w, true, box.MinPoint.Y, -clear, step);
                        furnished += DrawChain(sink, d, false, box.MinPoint.X, -clear, step);
                    }
                }
                made = sink.Count;
                tr.Commit();
            }
            int tiedB = DimAnchors.Associate(db, choice.Walls);
            Isolate(db, choice);
            ed.WriteMessage("\nAUTODIMROOM: " + made + " dimension(s) in " + rooms.Count + " room(s) at 1:" + _scale
                + (tiedB > 0 ? "; " + tiedB + " follow their walls when moved (HCWLIVE)" : "")
                + (furnished > 0 ? "; " + furnished / 2 + " furniture item(s) dimensioned" : "")
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

            double reach = Math.Max(
                segments.Count == 0 ? 0 : segments.Max(g => g.Key.DistanceTo(g.Value)),
                arcs.Count == 0 ? 0 : arcs.Max(a => a.Radius * 2));
            double mm = ResolveUnitsPerMm(ed, reach, 200.0);
            if (mm <= 0) return;
            double offset = Settings.GetDouble("AutoDimGapMm", 12) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;

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
            int tiedW = DimAnchors.Associate(db, null);
            ed.WriteMessage("\nAUTODIMWALL: " + (made - radii) + " length and " + radii + " radius dimension(s) at 1:" + _scale
                + (tiedW > 0 ? "; " + tiedW + " follow their walls when moved (HCWLIVE)" : "")
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

        private const string StoreDictionary = "HCW_AUTODIM";
        private const string StoreRecord = "LAYERS";

        /// <summary>
        /// Shows the layer dialog: walls, windows and doors, columns, furniture. The last choice for this drawing is
        /// remembered in the drawing; the first time, layers are guessed from their names. Returns null on Cancel.
        /// </summary>
        private static LayerChoice AskLayers(Editor ed, Database db, string furnitureNote)
        {
            var names = new List<string>();
            LayerChoice initial;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!rec.IsDependent) names.Add(rec.Name);
                }
                initial = LayerChoice.FromLines(DrawingStore.Read(tr, db, StoreDictionary, StoreRecord));
                tr.Commit();
            }
            initial.KeepOnly(names);
            if (!initial.Any) initial = LayerChoice.Guess(names);

            LayerChoice choice;
            while (true)
            {
                using (var dlg = new UI.AutoDimLayersForm(names, initial, furnitureNote))
                {
                    var result = dlg.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        // "Pick from drawing": keep what is ticked, add the layers of the objects picked, show the dialog again
                        initial = dlg.Read();
                        PickLayers(ed, db, dlg.PickTarget, initial);
                        continue;
                    }
                    if (result != System.Windows.Forms.DialogResult.OK) return null;
                    choice = dlg.Read();
                    break;
                }
            }
            if (choice.Walls.Count == 0)
            {
                ed.WriteMessage("\nChoose at least one wall layer.");
                return null;
            }
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                DrawingStore.Write(tr, db, StoreDictionary, StoreRecord, choice.ToLines());
                tr.Commit();
            }
            return choice;
        }

        /// <summary>
        /// Asks for objects in the drawing and ticks their layers for one role (W walls, O windows and doors, C columns,
        /// F furniture). A layer belongs to one role, so it is taken off the others. Layers from external references
        /// cannot be chosen and are skipped.
        /// </summary>
        private static void PickLayers(Editor ed, Database db, string role, LayerChoice into)
        {
            string label = role == "W" ? "wall" : role == "O" ? "window and door" : role == "C" ? "column" : "furniture";
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect objects on the " + label + " layer(s): " });
            if (psr.Status != PromptStatus.OK) return;

            var picked = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || !table.Has(ent.Layer)) continue;
                    if (!picked.Contains(ent.Layer, StringComparer.OrdinalIgnoreCase)) picked.Add(ent.Layer);
                }
                tr.Commit();
            }
            if (picked.Count == 0) return;

            var roles = new Dictionary<string, List<string>>
            {
                { "W", into.Walls }, { "O", into.Windows }, { "C", into.Columns }, { "F", into.Furniture }
            };
            foreach (var layer in picked)
            {
                foreach (var list in roles.Values) list.RemoveAll(l => string.Equals(l, layer, StringComparison.OrdinalIgnoreCase));
                roles[role].Add(layer);
            }
            ed.WriteMessage("\nAdded to " + label + ": " + string.Join(", ", picked) + ".");
        }

        /// <summary>
        /// Reads the plan from the chosen layers of the current space: wall lines and polylines, window and door blocks
        /// (their deduction line, or their extents) and lines, column polylines and blocks, and furniture blocks.
        /// </summary>
        private static void ReadPlan(Transaction tr, Database db, LayerChoice choice, Plan plan, List<Extents3d> furniture)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var walls = new HashSet<string>(choice.Walls, StringComparer.OrdinalIgnoreCase);
            var windows = new HashSet<string>(choice.Windows, StringComparer.OrdinalIgnoreCase);
            var columns = new HashSet<string>(choice.Columns, StringComparer.OrdinalIgnoreCase);
            var fitments = new HashSet<string>(choice.Furniture, StringComparer.OrdinalIgnoreCase);
            var gridLayers = Layers("AutoDimGridLayers", "AN-GRID;A-GRID");
            double tol = Util.MmToDrawingUnits(1.0);

            // door and window blocks: the line inside them is the opening (on the window layers, or anywhere when none are chosen)
            var blocks = new BlockOpenings.Reader(choice.Windows.Count > 0 ? choice.Windows : null);
            var found = new List<BlockOpenings.Found>();

            // One pass over the space. Objects that cannot matter (text, hatches, dimensions ...) are skipped
            // from their type alone, without being opened.
            foreach (ObjectId id in space)
            {
                if (!Relevant(id)) continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                string layer = ent.Layer;

                var block = ent as BlockReference;
                if (block != null)
                {
                    blocks.Add(tr, block, found);
                    if (columns.Contains(layer)) AddCorners(block, plan.Structural);
                    else if (fitments.Contains(layer) && furniture != null)
                    {
                        try { furniture.Add(block.GeometricExtents); } catch { }
                    }
                    continue;
                }

                var curve = ent as Curve;
                if (curve == null) continue;

                if (walls.Contains(layer))
                {
                    Collect(curve, plan.Structural, ref plan.Skipped);
                    AddSegments(curve, plan.WallSegments);
                }
                else if (windows.Contains(layer))
                {
                    // loose window and door geometry: kept as boxes, grouped into one opening each by FinishOpenings
                    try
                    {
                        var e = curve.GeometricExtents;
                        plan.WindowBoxes.Add(new Box(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y));
                    }
                    catch { }
                }
                else if (columns.Contains(layer))
                {
                    var poly = curve as Polyline;
                    if (poly != null && poly.Closed)
                        for (int i = 0; i < poly.NumberOfVertices; i++) plan.Structural.Add(poly.GetPoint3dAt(i));
                }

                // read whatever the choice: standalone deduction lines and the structural grid
                if (string.Equals(layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase))
                    Collect(curve, plan.Jambs, ref plan.Skipped);
                else if (curve is Line && gridLayers.Contains(layer))
                {
                    var line = (Line)curve;
                    if (Math.Abs(line.StartPoint.X - line.EndPoint.X) <= tol) plan.GridX.Add(line.StartPoint.X);
                    else if (Math.Abs(line.StartPoint.Y - line.EndPoint.Y) <= tol) plan.GridY.Add(line.StartPoint.Y);
                }
            }

            foreach (var f in found)
            {
                Collect(f.Curve, plan.Jambs, ref plan.Skipped);
                f.Curve.Dispose(); // a copy that never entered the drawing
            }
        }

        /// <summary>Only these object types can be walls, openings, columns, furniture blocks or grid lines.</summary>
        private static readonly HashSet<string> RelevantTypes = new HashSet<string>
        {
            "LINE", "LWPOLYLINE", "POLYLINE", "ARC", "CIRCLE", "ELLIPSE", "SPLINE", "INSERT"
        };

        private static bool Relevant(ObjectId id) => RelevantTypes.Contains(id.ObjectClass.DxfName);

        /// <summary>
        /// Groups the loose window and door geometry (frame lines, sills, leaves, swing arcs) into one opening each and adds
        /// each opening's two outer edges along its wall as jamb points. Needs the real-size scale, so it runs after the
        /// drawing units are settled. Pieces within <c>AutoDimOpeningJoinMm</c> of each other belong to one opening.
        /// </summary>
        private static void FinishOpenings(Plan plan, double mm)
        {
            if (plan.WindowBoxes.Count == 0) return;
            double join = Settings.GetDouble("AutoDimOpeningJoinMm", 20) * mm;
            foreach (var opening in OpeningClusters.Cluster(plan.WindowBoxes, join))
            {
                bool horizontal = OpeningClusters.InHorizontalWall(opening, plan.WallSegments);
                foreach (var p in OpeningClusters.Jambs(opening, horizontal))
                    plan.Jambs.Add(new Point3d(p.X, p.Y, 0));
                plan.GeometryOpenings++;
            }
            plan.WindowBoxes.Clear();
        }

        /// <summary>Straight segments of a wall line or polyline, for finding which way the nearest wall runs.</summary>
        private static void AddSegments(Curve curve, List<WallSegment> into)
        {
            var line = curve as Line;
            if (line != null)
            {
                into.Add(new WallSegment(line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y));
                return;
            }
            var poly = curve as Polyline;
            if (poly == null) return;
            int n = poly.NumberOfVertices;
            int last = poly.Closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                if (poly.GetSegmentType(i) != SegmentType.Line) continue;
                var a = poly.GetPoint3dAt(i); var b = poly.GetPoint3dAt((i + 1) % n);
                into.Add(new WallSegment(a.X, a.Y, b.X, b.Y));
            }
        }

        private static void AddCorners(BlockReference block, List<Point3d> into)
        {
            try
            {
                var e = block.GeometricExtents;
                into.Add(new Point3d(e.MinPoint.X, e.MinPoint.Y, 0));
                into.Add(new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0));
                into.Add(new Point3d(e.MaxPoint.X, e.MaxPoint.Y, 0));
                into.Add(new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0));
            }
            catch { }
        }

        /// <summary>
        /// Leaves only the wall, window, column and dimension layers on, when the dialog said so. The layers turned off are
        /// added to the take-off list, so Restore Layers (MSHOW) switches them back on.
        /// </summary>
        private static void Isolate(Database db, LayerChoice choice)
        {
            if (!choice.LeaveIsolated) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var state = Util.IsolateLayers(tr, db, choice.Shown().Concat(new[] { DimLayer }));
                foreach (var name in state.Hidden) MeasureCommands.MeasureState.HiddenByMeasure.Add(name);
                tr.Commit();
            }
        }

        /// <summary>
        /// Drawing units in one real millimetre. Normally from the drawing's units setting; when that would make the
        /// plan an implausible size (a metre drawing marked as millimetres, or units unset), asks which unit the
        /// drawing is really in. The drawing itself is not changed. <paramref name="minMm"/> is the smallest real
        /// size that counts as plausible for what was selected (2 m for a plan, 1 m for a room, 20 cm for one wall).
        /// </summary>
        internal static double ResolveUnitsPerMm(Editor ed, double span, double minMm = 2000.0)
        {
            double mm = Util.MmToDrawingUnits(1.0);
            double realMm = span / mm;
            if (realMm >= minMm && realMm <= 1000000.0) return mm;

            string guess = UnitScale.Guess(span) ?? "Metres";
            ed.WriteMessage("\nThe selection is " + span.ToString("0.##", CultureInfo.InvariantCulture) + " drawing units across, which is "
                + (realMm / 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " m with the drawing's units setting ("
                + Util.Db.Insunits + "). That does not look like a building, so the units setting is probably wrong.");
            var opt = new PromptKeywordOptions("\nTreat the drawing units as [" + string.Join("/", UnitScale.Names) + "] <" + guess + ">: ") { AllowNone = true };
            foreach (var k in UnitScale.Names) opt.Keywords.Add(k);
            opt.Keywords.Default = guess;
            var res = ed.GetKeywords(opt);
            if (res.Status == PromptStatus.Cancel) return 0;
            string unit = res.Status == PromptStatus.OK ? res.StringResult : guess;
            return UnitScale.PerMm(unit);
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
            var old2d = curve as Polyline2d;
            if (old2d != null)
            {
                Point3d? previous = null, first = null;
                foreach (ObjectId vid in old2d)
                {
                    var vertex = old2d.Database == null ? null : old2d.Database.TransactionManager.TopTransaction.GetObject(vid, OpenMode.ForRead) as Vertex2d;
                    if (vertex == null) continue;
                    var p = vertex.Position;
                    into.Add(p);
                    if (previous.HasValue && !Orthogonal(previous.Value, p)) skipped++;
                    previous = p;
                    if (!first.HasValue) first = p;
                }
                if (old2d.Closed && previous.HasValue && first.HasValue && !Orthogonal(previous.Value, first.Value)) skipped++;
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
