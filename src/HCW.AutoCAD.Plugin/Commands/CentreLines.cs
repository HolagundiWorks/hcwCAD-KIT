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
            public bool Closed;
            public double Z;
        }

        internal static void Run(Editor ed, string selectPrompt, string startPrompt, Action<List<Chain>> create)
        {
            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = selectPrompt },
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
                if (bulges > 0) ed.WriteMessage("\n" + bulges + " curved segment(s) in the centre lines were drawn straight.");
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
                    c.Z = w.Z;
                }
                create(new List<Chain> { c });
            }
        }

    }
}
