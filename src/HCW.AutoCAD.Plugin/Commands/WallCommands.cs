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
    /// HCWWALL draws wall faces from a centre line (picked points, or lines and polylines you select),
    /// with mitred corners. HCWWALLJOIN merges overlapping wall outlines so T and L junctions are clean.
    /// Walls are closed polylines on A-WALL; the plan must be drawn in a UCS whose Z axis is the world Z.
    /// </summary>
    public class WallCommands
    {
        internal const string WallLayer = "A-WALL";

        private static double _thicknessMm = 230;
        private static WallJustify _justify = WallJustify.Centre;

        private class Chain
        {
            public List<P2> Points = new List<P2>();
            public bool Closed;
            public double Z;
        }

        [CommandMethod("HCWWALL")]
        public void DrawWall()
        {
            var ed = Util.Ed;

            var t = ed.GetDouble(new PromptDoubleOptions("\nWall thickness in mm <" + _thicknessMm + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _thicknessMm, UseDefaultValue = true });
            if (t.Status != PromptStatus.OK) return;
            _thicknessMm = t.Value;

            var jo = new PromptKeywordOptions("\nLine position [Centre/Left/Right] <" + _justify + ">: ", "Centre Left Right") { AllowNone = true };
            jo.Keywords.Default = _justify.ToString();
            var jr = ed.GetKeywords(jo);
            if (jr.Status == PromptStatus.OK) Enum.TryParse(jr.StringResult, out _justify);
            else if (jr.Status != PromptStatus.None) return;

            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\nSelect centre lines to turn into walls (Enter to pick points): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE") }));
            if (psr.Status == PromptStatus.OK)
            {
                var chains = new List<Chain>();
                int bulges = 0;
                using (var tr = Util.Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds())
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        var line = ent as Line;
                        var pl = ent as Polyline;
                        var c = new Chain();
                        if (line != null)
                        {
                            c.Points.Add(new P2(line.StartPoint.X, line.StartPoint.Y));
                            c.Points.Add(new P2(line.EndPoint.X, line.EndPoint.Y));
                            c.Z = line.StartPoint.Z;
                        }
                        else if (pl != null)
                        {
                            for (int i = 0; i < pl.NumberOfVertices; i++)
                            {
                                var p = pl.GetPoint2dAt(i);
                                c.Points.Add(new P2(p.X, p.Y));
                                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) bulges++;
                            }
                            c.Closed = pl.Closed;
                            c.Z = pl.Elevation;
                        }
                        else continue;
                        chains.Add(c);
                    }
                    tr.Commit();
                }
                if (bulges > 0) ed.WriteMessage("\nHCWWALL: " + bulges + " curved segment(s) were drawn straight.");
                Create(ed, chains);
                return;
            }
            if (psr.Status != PromptStatus.None) return;

            // Pick points: one run after another until Enter on the start prompt.
            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var first = ed.GetPoint(new PromptPointOptions("\nStart of wall (Enter to finish): ") { AllowNone = true });
                if (first.Status != PromptStatus.OK) return;

                var pts = new List<Point3d> { first.Value };
                bool closed = false;
                while (true)
                {
                    var po = new PromptPointOptions("\nNext point [Close/Undo] <end of run>: ", "Close Undo")
                        { AllowNone = true, UseBasePoint = true, BasePoint = pts[pts.Count - 1], UseDashedLine = true };
                    var pr = ed.GetPoint(po);
                    if (pr.Status == PromptStatus.None || pr.Status == PromptStatus.Cancel) break;
                    if (pr.Status == PromptStatus.Keyword)
                    {
                        if (pr.StringResult == "Undo") { if (pts.Count > 1) pts.RemoveAt(pts.Count - 1); }
                        else if (pr.StringResult == "Close" && pts.Count >= 3) { closed = true; break; }
                        continue;
                    }
                    if (pr.Status != PromptStatus.OK) break;
                    pts.Add(pr.Value);
                }
                if (pts.Count < 2) continue;

                var c = new Chain { Closed = closed };
                foreach (var p in pts)
                {
                    var w = p.TransformBy(ucs);
                    c.Points.Add(new P2(w.X, w.Y));
                    c.Z = w.Z;
                }
                Create(ed, new List<Chain> { c });
            }
        }

        private static void Create(Editor ed, List<Chain> chains)
        {
            var db = Util.Db;
            double thick = Util.MmToDrawingUnits(_thicknessMm);
            int made = 0, skipped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, WallLayer);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var c in chains)
                {
                    var loops = WallGeometry.Outline(c.Points, c.Closed, thick, _justify);
                    if (loops.Count == 0) { skipped++; continue; }
                    foreach (var loop in loops)
                    {
                        var pl = new Polyline();
                        for (int i = 0; i < loop.Count; i++) pl.AddVertexAt(i, new Point2d(loop[i].X, loop[i].Y), 0, 0, 0);
                        pl.Closed = true;
                        pl.Elevation = c.Z;
                        pl.Layer = WallLayer;
                        space.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        made++;
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWWALL: " + made + " wall outline(s) on " + WallLayer + ", " + _thicknessMm + " mm thick."
                + (skipped > 0 ? " " + skipped + " line(s) too short to make a wall." : ""));
        }

        [CommandMethod("HCWWALLJOIN")]
        public void JoinWalls()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\nSelect the closed wall outlines to merge: " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") }));
            if (psr.Status != PromptStatus.OK) return;

            double tol = Util.MmToDrawingUnits(0.05);
            int notClosed = 0, bad = 0, made = 0, kept = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sources = new List<Polyline>();
                var regions = new List<Region>();
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                    if (!pl.Closed) { notClosed++; continue; }
                    try
                    {
                        var coll = new DBObjectCollection();
                        coll.Add(pl);
                        var made1 = Region.CreateFromCurves(coll);
                        if (made1.Count == 0) { bad++; continue; }
                        foreach (DBObject o in made1) regions.Add((Region)o);
                        sources.Add(pl);
                    }
                    catch (System.Exception) { bad++; }
                }
                if (regions.Count < 2)
                {
                    foreach (var r in regions) r.Dispose();
                    ed.WriteMessage("\nHCWWALLJOIN: select at least two closed outlines" + (notClosed + bad > 0 ? " (" + (notClosed + bad) + " could not be used)." : "."));
                    return;
                }

                var acc = regions[0];
                for (int i = 1; i < regions.Count; i++)
                {
                    try { acc.BooleanOperation(BooleanOperationType.BoolUnite, regions[i]); }
                    catch (System.Exception) { bad++; }
                    regions[i].Dispose();
                }

                var parts = new DBObjectCollection();
                acc.Explode(parts);
                acc.Dispose();

                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var layer = sources[0].Layer;
                double z = sources[0].Elevation;
                var segs = new List<Seg>();
                foreach (DBObject o in parts)
                {
                    var ln = o as Line;
                    if (ln != null) { segs.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y))); ln.Dispose(); continue; }
                    // Anything curved stays as it came out of the merge.
                    var ent = o as Entity;
                    if (ent != null)
                    {
                        ent.Layer = layer;
                        space.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                        kept++;
                    }
                    else o.Dispose();
                }
                foreach (var run in SegmentChain.Join(segs, tol))
                {
                    var pts = WallGeometry.Simplify(run.Points, run.Closed, tol);
                    if (pts.Count < 2) continue;
                    var pl = new Polyline();
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                    pl.Closed = run.Closed;
                    pl.Elevation = z;
                    pl.Layer = layer;
                    space.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    made++;
                }
                if (made + kept == 0)
                {
                    ed.WriteMessage("\nHCWWALLJOIN: the merge gave nothing; the outlines were left as they are.");
                    return;
                }
                foreach (var pl in sources)
                {
                    pl.UpgradeOpen();
                    pl.Erase();
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWWALLJOIN: merged into " + (made + kept) + " outline(s)."
                + (notClosed + bad > 0 ? " " + (notClosed + bad) + " could not be used (open or not a plane shape)." : ""));
        }
    }
}
