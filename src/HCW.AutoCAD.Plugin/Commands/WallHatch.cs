using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// The hatch in wall outlines. All the walls drawn by HCWWALL are joined into one shape, the doors and windows are cut out of it, and it
    /// is hatched in one go on A-WALL-HATCH. The hatch is redrawn whenever walls or openings change; it is not tied to the outlines.
    /// </summary>
    internal static class WallHatch
    {
        private const string App = "HCW_WALLH";
        internal static string Layer => Util.Out("A-WALL-HATCH");

        /// <summary>Redraws the wall hatch for the whole current space (setting WallHatch = 0 turns it off and removes it).</summary>
        internal static void Refresh(Transaction tr, Database db, BlockTableRecord space)
        {
            Erase(tr, space);
            if (Settings.GetInt("WallHatch", 1) == 0) return;
            double mm = Util.MmToDrawingUnits(1.0);

            var records = WallStore.Keys(tr, db).Select(k => WallStore.Load(tr, db, k)).Where(r => r != null).ToList();
            if (records.Count == 0) return;

            var temps = new List<Entity>();
            Region union = null;
            try
            {
                foreach (var r in records)
                    foreach (var loop in r.Outlines(mm))
                    {
                        var reg = RegionOf(loop, r.Z);
                        if (reg == null) continue;
                        if (union == null) union = reg;
                        else { union.BooleanOperation(BooleanOperationType.BoolUnite, reg); reg.Dispose(); }
                    }
                if (union == null) return;

                foreach (ObjectId id in space)
                {
                    if (id.ObjectClass.DxfName != "INSERT") continue;
                    var info = OpeningCommands.ReadOpening(tr, id);
                    if (info == null) continue;
                    var q = Quad(info.Corners, mm);
                    var cut = RegionOf(q, 0);
                    if (cut == null) continue;
                    try { union.BooleanOperation(BooleanOperationType.BoolSubtract, cut); }
                    catch (System.Exception) { }
                    cut.Dispose();
                }
                if (union.IsNull) return;

                var parts = new DBObjectCollection();
                union.Explode(parts);
                var segs = new List<Seg>();
                foreach (DBObject o in parts)
                {
                    var ln = o as Line;
                    if (ln != null) segs.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)));
                    o.Dispose();
                }
                var runs = SegmentChain.Join(segs, 0.5 * mm).Where(x => x.Closed && x.Points.Count >= 3).ToList();
                if (runs.Count == 0) return;

                Util.EnsureHcwLayer(tr, db, Layer);
                var ids = new List<ObjectId>();
                foreach (var run in runs)
                {
                    var pl = new Polyline { Closed = true, Layer = Layer };
                    for (int i = 0; i < run.Points.Count; i++) pl.AddVertexAt(i, new Point2d(run.Points[i].X, run.Points[i].Y), 0, 0, 0);
                    space.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    temps.Add(pl);
                    ids.Add(pl.ObjectId);
                }

                var hatch = new Hatch { Layer = Layer };
                space.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.Normal = Vector3d.ZAxis;
                hatch.SetHatchPattern(HatchPatternType.PreDefined, Settings.Get("WallHatchPattern", "ANSI31"));
                hatch.PatternScale = Settings.GetDouble("WallHatchSpacingMm", 60) * mm / 3.175;
                hatch.Associative = false;
                hatch.HatchStyle = HatchStyle.Normal;
                foreach (var id in ids) hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { id });
                hatch.EvaluateHatch(true);
                Tag(tr, db, hatch);
            }
            catch (System.Exception)
            {
                // a wall shape the boolean operations cannot handle: leave the walls unhatched rather than fail the command
            }
            finally
            {
                if (union != null) union.Dispose();
                foreach (var t in temps) { if (!t.IsErased) { t.UpgradeOpen(); t.Erase(); } }
            }
        }

        /// <summary>The opening's footprint, a little wider than the wall so the cut leaves no sliver on the faces.</summary>
        private static List<P2> Quad(OpeningFrame.Corners c, double mm)
        {
            var across = (c.FaceBStart - c.FaceAStart);
            var n = across.Length < 1e-9 ? new P2(0, 0) : across * (1.0 / across.Length);
            var o = n * mm;
            return new List<P2> { c.FaceAStart - o, c.FaceAEnd - o, c.FaceBEnd + o, c.FaceBStart + o };
        }

        private static Region RegionOf(IList<P2> pts, double z)
        {
            var pl = new Polyline { Closed = true, Elevation = z };
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            var curves = new DBObjectCollection { pl };
            try
            {
                var regs = Region.CreateFromCurves(curves);
                return regs.Count > 0 ? (Region)regs[0] : null;
            }
            catch (System.Exception) { return null; }
            finally { pl.Dispose(); }
        }

        private static void Tag(Transaction tr, Database db, Entity ent)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!apps.Has(App))
            {
                apps.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = App };
                apps.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            ent.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App), new TypedValue((int)DxfCode.ExtendedDataAsciiString, "WALL"));
        }

        private static void Erase(Transaction tr, BlockTableRecord space)
        {
            foreach (ObjectId id in space)
            {
                if (id.ObjectClass.DxfName != "HATCH") continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased || ent.GetXDataForApplication(App) == null) continue;
                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        /// <summary>Redraws the hatch in its own transaction (after the opening commands).</summary>
        internal static void RefreshNow()
        {
            if (Settings.GetInt("WallHatch", 1) == 0) return;
            try
            {
                using (Util.Doc.LockDocument())
                using (var tr = Util.Db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                    Refresh(tr, Util.Db, space);
                    tr.Commit();
                }
            }
            catch (System.Exception) { }
        }
    }
}
