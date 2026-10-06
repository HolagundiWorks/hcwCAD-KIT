using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Reads centre lines for the wall and rail tools: lines and polylines you select, or points you pick one run
    /// after another. Each batch is handed to a callback so the result is drawn as soon as it is read.
    /// </summary>
    internal static class CentreLines
    {
        internal class Chain
        {
            public List<P2> Points = new List<P2>();
            /// <summary>Per point: true for a point inside a curve (not a corner). Empty when there are no curves.</summary>
            public List<bool> Smooth = new List<bool>();
            public bool Closed;
            public double Z;
        }

        /// <summary>Curved centre lines are cut into straight pieces no further than this from the true curve.</summary>
        private static double Sagitta => Util.MmToDrawingUnits(2);

        internal static void Run(Editor ed, string selectPrompt, string startPrompt, Action<List<Chain>> create)
        {
            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = selectPrompt },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,ARC,LWPOLYLINE") }));
            if (psr.Status == PromptStatus.OK)
            {
                var chains = new List<Chain>();
                using (var tr = Util.Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds())
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead);
                        var line = ent as Line;
                        var pl = ent as Polyline;
                        var arc = ent as Arc;
                        var c = new Chain();
                        if (line != null)
                        {
                            c.Points.Add(new P2(line.StartPoint.X, line.StartPoint.Y));
                            c.Points.Add(new P2(line.EndPoint.X, line.EndPoint.Y));
                            c.Smooth.Add(false); c.Smooth.Add(false);
                            c.Z = line.StartPoint.Z;
                        }
                        else if (arc != null)
                        {
                            double sweep = arc.EndAngle - arc.StartAngle;
                            if (sweep <= 0) sweep += 2 * Math.PI;
                            c.Points = CurveSampler.ArcPoints(new P2(arc.Center.X, arc.Center.Y), arc.Radius, arc.StartAngle, sweep, Sagitta);
                            for (int i = 0; i < c.Points.Count; i++) c.Smooth.Add(i > 0 && i < c.Points.Count - 1);
                            c.Z = arc.Center.Z;
                        }
                        else if (pl != null)
                        {
                            int n = pl.NumberOfVertices, last = pl.Closed ? n : n - 1;
                            for (int i = 0; i < last; i++)
                            {
                                var a = pl.GetPoint2dAt(i);
                                var b = pl.GetPoint2dAt((i + 1) % n);
                                var seg = CurveSampler.BulgePoints(new P2(a.X, a.Y), new P2(b.X, b.Y), pl.GetBulgeAt(i), Sagitta);
                                // Each piece starts where the last ended, so drop the repeated point.
                                for (int k = 0; k < seg.Count - 1; k++) { c.Points.Add(seg[k]); c.Smooth.Add(k > 0); }
                                if (!pl.Closed && i == last - 1) { c.Points.Add(seg[seg.Count - 1]); c.Smooth.Add(false); }
                            }
                            if (n == 1) { var p = pl.GetPoint2dAt(0); c.Points.Add(new P2(p.X, p.Y)); c.Smooth.Add(false); }
                            c.Closed = pl.Closed;
                            c.Z = pl.Elevation;
                        }
                        else continue;
                        chains.Add(c);
                    }
                    tr.Commit();
                }
                create(chains);
                return;
            }
            if (psr.Status != PromptStatus.None) return;

            // Pick points: one run after another until Enter on the start prompt.
            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var first = ed.GetPoint(new PromptPointOptions(startPrompt) { AllowNone = true });
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
                    c.Smooth.Add(false);
                    c.Z = w.Z;
                }
                create(new List<Chain> { c });
            }
        }

    }
}
