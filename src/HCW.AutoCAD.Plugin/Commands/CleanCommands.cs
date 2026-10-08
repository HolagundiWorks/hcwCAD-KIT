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
    /// HCWCLEAN tidies lines: zero-length lines and exact duplicates are erased, and (when you say so) collinear lines
    /// that touch or overlap are joined into one. Lines only join when their layer, linetype, colour, lineweight
    /// and elevation match. Lines on locked layers, and lines that are not flat, are left alone.
    /// </summary>
    public class CleanCommands
    {
        private static bool _join = true;

        /// <summary>
        /// HCWCORNER trims or extends two lines to meet at their corner, keeping the part of each that you click. Click the
        /// first line, then the second; a line that stopped short is extended, one that ran past is trimmed. Repeats until Enter.
        /// Like FILLET with radius 0, for lines only, on any layer that is not locked.
        /// </summary>
        [CommandMethod("HCWCORNER")]
        public void Corner()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            while (true)
            {
                var first = PickLine(ed, "\nSelect the first line, on the part to keep (Enter to finish): ");
                if (first == null) return;
                var second = PickLine(ed, "\nSelect the second line, on the part to keep: ");
                if (second == null) return;
                if (first.Value.Key == second.Value.Key) { ed.WriteMessage("\nHCWCORNER: pick two different lines."); continue; }

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var a = Target(tr, first.Value.Key, first.Value.Value);
                    var b = Target(tr, second.Value.Key, second.Value.Value);
                    if (a == null || b == null) { ed.WriteMessage("\nHCWCORNER: pick a line, or a straight piece of a polyline."); continue; }
                    string locked = null;
                    foreach (var ent in new[] { a.Entity, b.Entity })
                        if (((LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead)).IsLocked) locked = ent.Layer;
                    if (locked != null) { ed.WriteMessage("\nHCWCORNER: layer " + locked + " is locked."); continue; }

                    string error;
                    var fit = CornerFit.Fit(a.Seg, new P2(first.Value.Value.X, first.Value.Value.Y), b.Seg, new P2(second.Value.Value.X, second.Value.Value.Y), out error);
                    if (fit == null) { ed.WriteMessage("\nHCWCORNER: " + error + "."); continue; }

                    if (!a.Move(fit.A, out error) || !b.Move(fit.B, out error)) { ed.WriteMessage("\nHCWCORNER: " + error); continue; }
                    tr.Commit();
                }
                ed.WriteMessage("\nHCWCORNER: lines meet at the corner.");
            }
        }

        private static KeyValuePair<ObjectId, Point3d>? PickLine(Editor ed, string prompt)
        {
            var o = new PromptEntityOptions(prompt) { AllowNone = true };
            o.SetRejectMessage("\nSelect a line or polyline.");
            o.AddAllowedClass(typeof(Line), true);
            o.AddAllowedClass(typeof(Polyline), true);
            var r = ed.GetEntity(o);
            if (r.Status != PromptStatus.OK) return null;
            return new KeyValuePair<ObjectId, Point3d>(r.ObjectId, r.PickedPoint);
        }

        /// <summary>One straight piece to fit at a corner: a line, or the straight segment of a polyline nearest the pick.</summary>
        private class CornerTarget
        {
            public Entity Entity;
            public Seg Seg;
            public Func<Seg, string> Apply;

            public bool Move(Seg to, out string error)
            {
                error = Apply(to);
                return error == null;
            }
        }

        private static CornerTarget Target(Transaction tr, ObjectId id, Point3d pick)
        {
            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
            var ln = ent as Line;
            if (ln != null)
                return new CornerTarget
                {
                    Entity = ln,
                    Seg = new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)),
                    Apply = s =>
                    {
                        ln.UpgradeOpen();
                        double z = ln.StartPoint.Z;
                        ln.StartPoint = new Point3d(s.A.X, s.A.Y, z);
                        ln.EndPoint = new Point3d(s.B.X, s.B.Y, z);
                        return null;
                    },
                };
            var pl = ent as Polyline;
            if (pl == null) return null;
            int n = pl.NumberOfVertices, segs = pl.Closed ? n : n - 1, best = -1;
            double bestD = double.MaxValue;
            var here = new P2(pick.X, pick.Y);
            for (int i = 0; i < segs; i++)
            {
                if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                var p = pl.GetPoint2dAt(i); var q = pl.GetPoint2dAt((i + 1) % n);
                double d = OpeningCut.DistanceToSegment(new Seg(new P2(p.X, p.Y), new P2(q.X, q.Y)), here);
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return null;
            var p0 = pl.GetPoint2dAt(best); var p1 = pl.GetPoint2dAt((best + 1) % n);
            int seg = best;
            return new CornerTarget
            {
                Entity = pl,
                Seg = new Seg(new P2(p0.X, p0.Y), new P2(p1.X, p1.Y)),
                Apply = s =>
                {
                    // Only a free end of an open polyline can move; a vertex shared with the next segment would bend that one too.
                    double tol = Util.MmToDrawingUnits(0.001);
                    int i0 = seg, i1 = (seg + 1) % n;
                    bool moveA = new P2(p0.X, p0.Y).DistanceTo(s.A) > tol, moveB = new P2(p1.X, p1.Y).DistanceTo(s.B) > tol;
                    if (moveA && (pl.Closed || i0 != 0)) return "that corner is in the middle of a polyline; only the end of an open polyline can be trimmed or extended.";
                    if (moveB && (pl.Closed || i1 != n - 1)) return "that corner is in the middle of a polyline; only the end of an open polyline can be trimmed or extended.";
                    if ((moveA && pl.GetBulgeAt(i0) != 0) || (moveB && n >= 2 && pl.GetBulgeAt(i0) != 0)) return "that segment is curved.";
                    pl.UpgradeOpen();
                    if (moveA) pl.SetPointAt(i0, new Point2d(s.A.X, s.A.Y));
                    if (moveB) pl.SetPointAt(i1, new Point2d(s.B.X, s.B.Y));
                    return null;
                },
            };
        }

        /// <summary>Duplicate removal and end-to-end joining for polylines and arcs. A joined chain becomes one polyline on the first piece's layer.</summary>
        private static void CleanCurves(Transaction tr, Database db, List<ObjectId> ids, double tol, bool join, Dictionary<ObjectId, bool> layerLocked,
            ref int locked, ref int duplicates, ref int joined)
        {
            var ents = new List<Entity>();
            var items = new List<PolyItem>();
            foreach (var id in ids)
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                bool isLocked;
                if (!layerLocked.TryGetValue(ent.LayerId, out isLocked))
                    layerLocked[ent.LayerId] = isLocked = ((LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead)).IsLocked;
                if (isLocked) { locked++; continue; }
                var key = ent.Layer + "|" + ent.Linetype + "|" + ent.Color + "|" + (int)ent.LineWeight;
                PolyPath path;
                var pl = ent as Polyline;
                var arc = ent as Arc;
                if (pl != null)
                {
                    if (pl.NumberOfVertices < 2 || pl.HasWidth) continue;       // wide polylines keep their own widths
                    path = new PolyPath { Closed = pl.Closed };
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint2dAt(i);
                        path.Points.Add(new P2(p.X, p.Y));
                        path.Bulges.Add(pl.GetBulgeAt(i));
                    }
                    key += "|" + Math.Round(pl.Elevation / tol);
                }
                else if (arc != null)
                {
                    double sweep = arc.EndAngle - arc.StartAngle;
                    if (sweep <= 0) sweep += 2 * Math.PI;
                    path = PolyPath.FromArc(new P2(arc.Center.X, arc.Center.Y), arc.Radius, arc.StartAngle, sweep);
                    key += "|" + Math.Round(arc.Center.Z / tol);
                }
                else continue;
                ents.Add(ent);
                items.Add(new PolyItem { Path = path, Key = key });
            }
            var res = PolylineJoin.Run(items, tol, join);
            foreach (int i in res.Duplicates)
            {
                ents[i].UpgradeOpen();
                ents[i].Erase();
                duplicates++;
            }
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            foreach (var j in res.Joined)
            {
                var keeper = ents[j.Keeper];
                var np = new Polyline();
                for (int i = 0; i < j.Path.Points.Count; i++)
                    np.AddVertexAt(i, new Point2d(j.Path.Points[i].X, j.Path.Points[i].Y), j.Path.Bulges[i], 0, 0);
                np.Closed = j.Path.Closed;
                np.SetPropertiesFrom(keeper);
                var kp = keeper as Polyline;
                np.Elevation = kp != null ? kp.Elevation : ((Arc)keeper).Center.Z;
                space.AppendEntity(np);
                tr.AddNewlyCreatedDBObject(np, true);
                var xd = keeper.XData;
                if (xd != null) np.XData = xd;
                foreach (var e in new[] { keeper }.Concat(j.Absorbed.Select(a => ents[a])))
                {
                    if (e.IsErased) continue;
                    e.UpgradeOpen();
                    e.Erase();
                }
                joined += j.Absorbed.Count + 1;
            }
        }

        [CommandMethod("HCWCLEAN")]
        public void CleanLines()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var jo = new PromptKeywordOptions("\nAlso join lines that touch or overlap on one straight line [Yes/No] <" + (_join ? "Yes" : "No") + ">: ", "Yes No") { AllowNone = true };
            jo.Keywords.Default = _join ? "Yes" : "No";
            var jr = ed.GetKeywords(jo);
            if (jr.Status == PromptStatus.OK) _join = jr.StringResult == "Yes";
            else if (jr.Status != PromptStatus.None) return;

            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the lines to clean (Enter = every line in this space): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,ARC") }));
            if (psr.Status != PromptStatus.OK && !Util.NoSelection(psr.Status)) return;

            double tol = Util.MmToDrawingUnits(0.05);
            int locked = 0, notFlat = 0, polyDup = 0, polyJoined = 0;
            CleanResult result;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = new List<ObjectId>();
                if (psr.Status == PromptStatus.OK) ids.AddRange(psr.Value.GetObjectIds());
                else
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                        if (id.ObjectClass.DxfName == "LINE" || id.ObjectClass.DxfName == "LWPOLYLINE" || id.ObjectClass.DxfName == "ARC") ids.Add(id);
                }

                var layerLocked = new Dictionary<ObjectId, bool>();
                var curveIds = new List<ObjectId>();
                var lines = new List<Line>();
                var input = new List<CleanLine>();
                foreach (var id in ids)
                {
                    var any = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (any == null || any.IsErased) continue;
                    var ln = any as Line;
                    if (ln == null) { curveIds.Add(id); continue; }
                    bool isLocked;
                    if (!layerLocked.TryGetValue(ln.LayerId, out isLocked))
                        layerLocked[ln.LayerId] = isLocked = ((LayerTableRecord)tr.GetObject(ln.LayerId, OpenMode.ForRead)).IsLocked;
                    if (isLocked) { locked++; continue; }
                    if (Math.Abs(ln.StartPoint.Z - ln.EndPoint.Z) > tol) { notFlat++; continue; }

                    lines.Add(ln);
                    input.Add(new CleanLine
                    {
                        Seg = new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)),
                        Key = ln.Layer + "|" + ln.Linetype + "|" + ln.Color + "|" + (int)ln.LineWeight + "|" + Math.Round(ln.StartPoint.Z / tol),
                    });
                }

                result = LineCleanup.Run(input, tol, _join);
                foreach (var kv in result.Replace)
                {
                    var ln = lines[kv.Key];
                    ln.UpgradeOpen();
                    double z = ln.StartPoint.Z;
                    ln.StartPoint = new Point3d(kv.Value.A.X, kv.Value.A.Y, z);
                    ln.EndPoint = new Point3d(kv.Value.B.X, kv.Value.B.Y, z);
                }
                foreach (int i in result.Erase)
                {
                    lines[i].UpgradeOpen();
                    lines[i].Erase();
                }
                polyDup = 0; polyJoined = 0;
                CleanCurves(tr, db, curveIds, tol, _join, layerLocked, ref locked, ref polyDup, ref polyJoined);
                tr.Commit();
            }

            int removed = result.Erase.Count;
            ed.WriteMessage("\nHCWCLEAN: " + removed + " line(s) erased"
                + (removed > 0 ? " (" + result.ZeroLength + " zero-length, " + result.Duplicates + " duplicate, " + result.Merged + " joined into longer lines)" : "")
                + (polyDup + polyJoined > 0 ? ". Polylines and arcs: " + polyDup + " duplicate(s) erased, " + polyJoined + " joined end to end" : "")
                + ". " + (locked > 0 ? locked + " on locked layers left alone. " : "")
                + (notFlat > 0 ? notFlat + " not flat left alone." : ""));
        }
    }
}
