using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Makes the dimensions of the auto dimension tools follow the walls: each end is tied to a wall vertex (see <see cref="DimAnchorLogic"/>),
    /// and <see cref="Refresh"/> moves the ends when the wall has moved. The ties are kept in each dimension's extended data.
    /// </summary>
    internal static class DimAnchors
    {
        private const string Kind = "AUTODIM";

        /// <summary>Handles of the entities some dimension hangs on, per drawing, so the live service knows which changes matter.</summary>
        internal static readonly Dictionary<Database, HashSet<long>> Anchored = new Dictionary<Database, HashSet<long>>();

        private static IEnumerable<ObjectId> Spaces(Database db)
        {
            var model = ModelSpaceId(db);
            yield return model;
            if (db.CurrentSpaceId != model) yield return db.CurrentSpaceId;
        }

        private static ObjectId ModelSpaceId(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var id = bt[BlockTableRecord.ModelSpace];
                tr.Commit();
                return id;
            }
        }

        private static List<string> Anchors(Dimension dim)
        {
            var data = dim.GetXDataForApplication(AutoDimCommands.AutoDimApp);
            var list = new List<string>();
            if (data == null) return list;
            foreach (var tv in data.AsArray())
            {
                var s = tv.Value as string;
                if (s != null && s.StartsWith("A|", StringComparison.Ordinal)) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Ties the ends of every auto dimension in the current space that has no ties yet to the wall vertices they sit on. Walls are the
        /// lines and polylines on the given layers (all layers when null). Returns how many dimensions are now associative.
        /// </summary>
        internal static int Associate(Database db, ICollection<string> wallLayers)
        {
            int tied = 0;
            double tol = Util.MmToDrawingUnits(0.5);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var layerSet = wallLayers == null ? null : new HashSet<string>(wallLayers, StringComparer.OrdinalIgnoreCase);
                var cands = new List<Anchor>();
                var dims = new List<AlignedDimension>();
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var dim = ent as AlignedDimension;
                    if (dim != null)
                    {
                        if (dim.GetXDataForApplication(AutoDimCommands.AutoDimApp) != null && Anchors(dim).Count == 0) dims.Add(dim);
                        continue;
                    }
                    if (layerSet != null && !layerSet.Contains(ent.Layer)) continue;
                    var ln = ent as Line;
                    var pl = ent as Polyline;
                    if (ln != null)
                    {
                        cands.Add(new Anchor { Handle = ln.Handle.Value, Index = 0, Pt = new P2(ln.StartPoint.X, ln.StartPoint.Y) });
                        cands.Add(new Anchor { Handle = ln.Handle.Value, Index = 1, Pt = new P2(ln.EndPoint.X, ln.EndPoint.Y) });
                    }
                    else if (pl != null)
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            var p = pl.GetPoint2dAt(i);
                            cands.Add(new Anchor { Handle = pl.Handle.Value, Index = i, Pt = new P2(p.X, p.Y) });
                        }
                }
                HashSet<long> handles;
                if (!Anchored.TryGetValue(db, out handles)) Anchored[db] = handles = new HashSet<long>();
                foreach (var dim in dims)
                {
                    var e1 = new P2(dim.XLine1Point.X, dim.XLine1Point.Y); var e2 = new P2(dim.XLine2Point.X, dim.XLine2Point.Y);
                    char axis = DimAnchorLogic.AxisOf(e1, e2, tol);
                    int i1 = DimAnchorLogic.Pick(cands, axis, e1, tol), i2 = DimAnchorLogic.Pick(cands, axis, e2, tol);
                    if (i1 < 0 || i2 < 0) continue;
                    dim.UpgradeOpen();
                    var rb = dim.GetXDataForApplication(AutoDimCommands.AutoDimApp);
                    rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, DimAnchorLogic.Encode(axis, cands[i1].Handle, cands[i1].Index)));
                    rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, DimAnchorLogic.Encode(axis, cands[i2].Handle, cands[i2].Index)));
                    dim.XData = rb;
                    handles.Add(cands[i1].Handle); handles.Add(cands[i2].Handle);
                    tied++;
                }
                tr.Commit();
            }
            return tied;
        }

        /// <summary>Collects which entities the dimensions in a drawing hang on (run when a drawing is opened or the service starts).</summary>
        internal static void Scan(Database db)
        {
            var handles = new HashSet<long>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var sid in Spaces(db).Distinct())
                {
                    var space = (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var dim = tr.GetObject(id, OpenMode.ForRead) as AlignedDimension;
                        if (dim == null || dim.IsErased) continue;
                        foreach (var a in Anchors(dim))
                        {
                            char axis; long h; int idx;
                            if (DimAnchorLogic.TryDecode(a, out axis, out h, out idx)) handles.Add(h);
                        }
                    }
                }
                tr.Commit();
            }
            Anchored[db] = handles;
        }

        /// <summary>Moves the ends of every associative dimension to where their wall vertices are now. Returns how many dimensions changed.</summary>
        internal static int Refresh(Database db)
        {
            int changed = 0;
            double tol = Util.MmToDrawingUnits(0.0001);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var sid in Spaces(db).Distinct())
                {
                    var space = (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var dim = tr.GetObject(id, OpenMode.ForRead) as AlignedDimension;
                        if (dim == null || dim.IsErased) continue;
                        var ties = Anchors(dim);
                        if (ties.Count != 2) continue;
                        var now = new P2[2]; var axes = new char[2]; bool ok = true;
                        for (int k = 0; k < 2 && ok; k++)
                        {
                            long h; int idx;
                            if (!DimAnchorLogic.TryDecode(ties[k], out axes[k], out h, out idx)) { ok = false; break; }
                            ObjectId target;
                            if (!db.TryGetObjectId(new Handle(h), out target) || target.IsErased) { ok = false; break; }
                            var ent = tr.GetObject(target, OpenMode.ForRead) as Entity;
                            var ln = ent as Line; var pl = ent as Polyline;
                            if (ln != null && idx <= 1) now[k] = new P2(idx == 0 ? ln.StartPoint.X : ln.EndPoint.X, idx == 0 ? ln.StartPoint.Y : ln.EndPoint.Y);
                            else if (pl != null && idx < pl.NumberOfVertices) { var p = pl.GetPoint2dAt(idx); now[k] = new P2(p.X, p.Y); }
                            else ok = false;
                        }
                        if (!ok) continue;
                        var old1 = new P2(dim.XLine1Point.X, dim.XLine1Point.Y); var old2 = new P2(dim.XLine2Point.X, dim.XLine2Point.Y);
                        var new1 = DimAnchorLogic.Follow(axes[0], old1, now[0]); var new2 = DimAnchorLogic.Follow(axes[1], old2, now[1]);
                        if (new1.DistanceTo(old1) <= tol && new2.DistanceTo(old2) <= tol) continue;
                        dim.UpgradeOpen();
                        double z1 = dim.XLine1Point.Z, z2 = dim.XLine2Point.Z;
                        var shift = ((new1 - old1) + (new2 - old2)) * 0.5;
                        dim.XLine1Point = new Point3d(new1.X, new1.Y, z1);
                        dim.XLine2Point = new Point3d(new2.X, new2.Y, z2);
                        if (axes[0] == 'P') dim.DimLinePoint = new Point3d(dim.DimLinePoint.X + shift.X, dim.DimLinePoint.Y + shift.Y, dim.DimLinePoint.Z);
                        changed++;
                    }
                }
                if (changed > 0) tr.Commit();
            }
            return changed;
        }
    }
}
