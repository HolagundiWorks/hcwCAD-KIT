using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Makes the dimensions of the auto dimension tools follow what they measure: each end is tied to a vertex of a wall, grid line or column outline, or to a
    /// point carried by a door or window block (see <see cref="DimAnchorLogic"/>), and <see cref="Refresh"/> moves the ends when those have moved. When the
    /// thing a dimension hangs on has been erased (a wall redrawn, say), the end is tied again to the vertex that now stands where it was, if there is one
    /// within AutoDimReanchorMm. The ties are kept in each dimension's extended data.
    /// </summary>
    internal static class DimAnchors
    {
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

        private static List<string> Ties(Dimension dim)
        {
            var data = dim.GetXDataForApplication(AutoDimCommands.AutoDimApp);
            var list = new List<string>();
            if (data == null) return list;
            foreach (var tv in data.AsArray())
            {
                var s = tv.Value as string;
                if (s != null && (s.StartsWith("A|", StringComparison.Ordinal) || s.StartsWith("B|", StringComparison.Ordinal))) list.Add(s);
            }
            return list;
        }

        /// <summary>Vertices of every line and polyline on the given layers (all layers when null).</summary>
        private static void AddVertices(Entity ent, List<Anchor> into)
        {
            var ln = ent as Line;
            var pl = ent as Polyline;
            if (ln != null)
            {
                into.Add(new Anchor { Handle = ln.Handle.Value, Index = 0, Pt = new P2(ln.StartPoint.X, ln.StartPoint.Y) });
                into.Add(new Anchor { Handle = ln.Handle.Value, Index = 1, Pt = new P2(ln.EndPoint.X, ln.EndPoint.Y) });
            }
            else if (pl != null)
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    into.Add(new Anchor { Handle = pl.Handle.Value, Index = i, Pt = new P2(p.X, p.Y) });
                }
        }

        /// <summary>
        /// Ties the ends of every auto dimension in the current space that has no ties yet. Vertices come from the lines and polylines on the given layers (walls,
        /// columns, grid; all layers when null); door and window points come from the blocks on the window layers. Returns how many dimensions are now associative.
        /// </summary>
        internal static int Associate(Database db, ICollection<string> wallLayers, ICollection<string> windowLayers = null)
        {
            int tied = 0;
            double tol = Util.MmToDrawingUnits(0.5);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var layerSet = wallLayers == null ? null : new HashSet<string>(wallLayers, StringComparer.OrdinalIgnoreCase);
                var winSet = windowLayers == null ? null : new HashSet<string>(windowLayers, StringComparer.OrdinalIgnoreCase);
                var cands = new List<Anchor>();
                var dims = new List<AlignedDimension>();
                var reader = winSet == null || winSet.Count == 0 ? null : new BlockOpenings.Reader(winSet);
                var found = new List<BlockOpenings.Found>();
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var dim = ent as AlignedDimension;
                    if (dim != null)
                    {
                        if (dim.GetXDataForApplication(AutoDimCommands.AutoDimApp) != null && Ties(dim).Count == 0) dims.Add(dim);
                        continue;
                    }
                    var blk = ent as BlockReference;
                    if (blk != null) { if (reader != null) reader.Add(tr, blk, found); continue; }
                    if (layerSet != null && !layerSet.Contains(ent.Layer)) continue;
                    AddVertices(ent, cands);
                }
                foreach (var f in found)
                {
                    var br = tr.GetObject(f.Source, OpenMode.ForRead) as BlockReference;
                    if (br != null)
                        foreach (var end in new[] { f.Curve.StartPoint, f.Curve.EndPoint })
                            cands.Add(new Anchor
                            {
                                Handle = br.Handle.Value, Block = true, Pt = new P2(end.X, end.Y),
                                Dx = end.X - br.Position.X, Dy = end.Y - br.Position.Y, Rot = br.Rotation,
                            });
                    f.Curve.Dispose();
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
                    foreach (int i in new[] { i1, i2 })
                    {
                        var c = cands[i];
                        rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                            c.Block ? DimAnchorLogic.EncodeBlock(axis, c.Handle, c.Dx, c.Dy, c.Rot) : DimAnchorLogic.Encode(axis, c.Handle, c.Index)));
                        handles.Add(c.Handle);
                    }
                    dim.XData = rb;
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
                        foreach (var t in Ties(dim))
                        {
                            DimTie tie;
                            if (DimAnchorLogic.TryDecodeTie(t, out tie)) handles.Add(tie.Handle);
                        }
                    }
                }
                tr.Commit();
            }
            Anchored[db] = handles;
        }

        /// <summary>Where a tie's target is now, or null when it is gone (erased, or no longer has that vertex).</summary>
        private static P2? Resolve(Transaction tr, Database db, DimTie tie)
        {
            ObjectId target;
            if (!db.TryGetObjectId(new Handle(tie.Handle), out target) || target.IsErased) return null;
            var ent = tr.GetObject(target, OpenMode.ForRead) as Entity;
            if (tie.Block)
            {
                var br = ent as BlockReference;
                return br == null ? (P2?)null : DimAnchorLogic.BlockPoint(new P2(br.Position.X, br.Position.Y), br.Rotation, tie);
            }
            var ln = ent as Line; var pl = ent as Polyline;
            if (ln != null && tie.Index <= 1) return tie.Index == 0 ? new P2(ln.StartPoint.X, ln.StartPoint.Y) : new P2(ln.EndPoint.X, ln.EndPoint.Y);
            if (pl != null && tie.Index < pl.NumberOfVertices) { var p = pl.GetPoint2dAt(tie.Index); return new P2(p.X, p.Y); }
            return null;
        }

        /// <summary>Moves the ends of every associative dimension to where what they hang on is now. Returns how many dimensions changed.</summary>
        internal static int Refresh(Database db)
        {
            int changed = 0;
            double tol = Util.MmToDrawingUnits(0.0001);
            double reach = Util.MmToDrawingUnits(Settings.GetDouble("AutoDimReanchorMm", 50));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                List<Anchor> pool = null;                         // vertices of the whole space, read only when a tie has lost its target
                HashSet<long> handles;
                if (!Anchored.TryGetValue(db, out handles)) Anchored[db] = handles = new HashSet<long>();
                foreach (var sid in Spaces(db).Distinct())
                {
                    var space = (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var dim = tr.GetObject(id, OpenMode.ForRead) as AlignedDimension;
                        if (dim == null || dim.IsErased) continue;
                        var texts = Ties(dim);
                        if (texts.Count != 2) continue;
                        var ties = new DimTie[2];
                        if (!DimAnchorLogic.TryDecodeTie(texts[0], out ties[0]) || !DimAnchorLogic.TryDecodeTie(texts[1], out ties[1])) continue;

                        var old1 = new P2(dim.XLine1Point.X, dim.XLine1Point.Y); var old2 = new P2(dim.XLine2Point.X, dim.XLine2Point.Y);
                        var olds = new[] { old1, old2 };
                        var now = new P2[2]; bool ok = true, retied = false;
                        for (int k = 0; k < 2 && ok; k++)
                        {
                            var at = Resolve(tr, db, ties[k]);
                            if (at == null && !ties[k].Block && reach > 0)
                            {
                                // the wall was erased and drawn again: hang the end on the vertex that stands where it was
                                if (pool == null)
                                {
                                    pool = new List<Anchor>();
                                    foreach (ObjectId pid in space)
                                    {
                                        var pe = tr.GetObject(pid, OpenMode.ForRead) as Entity;
                                        if (pe != null && !pe.IsErased) AddVertices(pe, pool);
                                    }
                                }
                                int pick = DimAnchorLogic.Pick(pool, ties[k].Axis, olds[k], reach);
                                if (pick >= 0)
                                {
                                    ties[k] = new DimTie { Axis = ties[k].Axis, Handle = pool[pick].Handle, Index = pool[pick].Index };
                                    at = pool[pick].Pt;
                                    retied = true;
                                }
                            }
                            if (at == null) ok = false; else now[k] = at.Value;
                        }
                        if (!ok) continue;

                        var new1 = DimAnchorLogic.Follow(ties[0].Axis, old1, now[0]); var new2 = DimAnchorLogic.Follow(ties[1].Axis, old2, now[1]);
                        bool moved = new1.DistanceTo(old1) > tol || new2.DistanceTo(old2) > tol;
                        if (!moved && !retied) continue;
                        dim.UpgradeOpen();
                        if (retied)
                        {
                            var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, AutoDimCommands.AutoDimApp),
                                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "AUTODIM"));
                            foreach (var t in ties)
                            {
                                rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                                    t.Block ? DimAnchorLogic.EncodeBlock(t.Axis, t.Handle, t.Dx, t.Dy, t.Rot) : DimAnchorLogic.Encode(t.Axis, t.Handle, t.Index)));
                                handles.Add(t.Handle);
                            }
                            dim.XData = rb;
                        }
                        if (moved)
                        {
                            double z1 = dim.XLine1Point.Z, z2 = dim.XLine2Point.Z;
                            var shift = ((new1 - old1) + (new2 - old2)) * 0.5;
                            dim.XLine1Point = new Point3d(new1.X, new1.Y, z1);
                            dim.XLine2Point = new Point3d(new2.X, new2.Y, z2);
                            if (ties[0].Axis == 'P') dim.DimLinePoint = new Point3d(dim.DimLinePoint.X + shift.X, dim.DimLinePoint.Y + shift.Y, dim.DimLinePoint.Z);
                        }
                        changed++;
                    }
                }
                if (changed > 0) tr.Commit();
            }
            return changed;
        }
    }
}
