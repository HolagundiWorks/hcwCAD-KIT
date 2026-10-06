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
        internal static string WallLayer => Util.Out("A-WALL");

        private static double _thicknessMm = 230;
        private static WallJustify _justify = WallJustify.Centre;

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

            CentreLines.Run(ed, "\nSelect centre lines to turn into walls (Enter to pick points): ", "\nStart of wall (Enter to finish): ",
                chains => Create(ed, chains));
        }

        private static void Create(Editor ed, List<CentreLines.Chain> chains)
        {
            var db = Util.Db;
            double thick = Util.MmToDrawingUnits(_thicknessMm);
            int made = 0, skipped = 0, merged = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, WallLayer);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var fresh = new List<Polyline>();
                var newIds = new List<string>();
                foreach (var c in chains)
                {
                    var loops = WallGeometry.Outline(c.Points, c.Closed, thick, _justify);
                    if (loops.Count == 0) { skipped++; continue; }
                    var rec = new WallRecord { Id = WallStore.NextId(tr, db), ThicknessMm = _thicknessMm, Justify = _justify, Closed = c.Closed, Z = c.Z, Points = c.Points };
                    WallStore.Save(tr, db, rec);
                    newIds.Add(rec.Id);
                    foreach (var loop in loops)
                    {
                        fresh.Add(Outline(tr, db, space, loop, c.Z, new[] { rec.Id }));
                        made++;
                    }
                }
                if (fresh.Count > 0 && Settings.GetInt("WallJoinOnDraw", 1) != 0)
                {
                    // Walls drawn against existing wall outlines are merged with them so junctions come out clean.
                    var group = new List<Polyline>(fresh);
                    var boxes = fresh.Select(p => p.GeometricExtents).ToList();
                    double reach = Util.MmToDrawingUnits(1);

                    // A wall a door or window has cut is loose lines, which cannot be merged as outlines. When the new wall touches one, the walls it touches
                    // are drawn again together from their records, which also cuts the openings back in.
                    var seeds = new HashSet<string>(newIds);
                    bool touchesCut = false;
                    foreach (ObjectId id in space)
                    {
                        var other = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (other == null || other.IsErased || fresh.Any(f => f.ObjectId == id)) continue;
                        var oids = WallStore.IdsOf(other);
                        if (oids.Count == 0) continue;
                        var oe = other.GeometricExtents;
                        if (!boxes.Any(b => oe.MinPoint.X <= b.MaxPoint.X + reach && oe.MaxPoint.X >= b.MinPoint.X - reach
                                         && oe.MinPoint.Y <= b.MaxPoint.Y + reach && oe.MaxPoint.Y >= b.MinPoint.Y - reach)) continue;
                        foreach (var oid in oids) seeds.Add(oid);
                        var op = other as Polyline;
                        if (op == null || !op.Closed) touchesCut = true;
                    }
                    if (touchesCut)
                    {
                        int rw, ro; string err;
                        if (Rebuild(tr, db, space, seeds, null, out rw, out ro, out err)) { merged = rw; group.Clear(); }
                        else
                        {
                            // the rebuild already changed the drawing inside this transaction, so none of it is kept
                            ed.WriteMessage("\nHCWWALL: the new wall touches walls that openings are cut in, and they could not be redrawn (" + err + "). Nothing was drawn.");
                            return;
                        }
                        if (merged > 0) { tr.Commit(); ed.WriteMessage("\nHCWWALL: " + made + " wall outline(s) on " + WallLayer + ", " + _thicknessMm + " mm thick. Rebuilt with the walls it touches (" + rw + " wall(s), " + ro + " opening(s) re-cut)."); return; }
                    }
                    foreach (ObjectId id in space)
                    {
                        if (id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                        var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pl == null || pl.IsErased || !pl.Closed || !(string.Equals(pl.Layer, WallLayer, StringComparison.OrdinalIgnoreCase) || string.Equals(pl.Layer, "A-WALL", StringComparison.OrdinalIgnoreCase))) continue;
                        if (fresh.Any(f => f.ObjectId == id)) continue;
                        var e = pl.GeometricExtents;
                        if (boxes.Any(b => e.MinPoint.X <= b.MaxPoint.X + reach && e.MaxPoint.X >= b.MinPoint.X - reach
                                        && e.MinPoint.Y <= b.MaxPoint.Y + reach && e.MaxPoint.Y >= b.MinPoint.Y - reach)) group.Add(pl);
                    }
                    int m, k, bad;
                    if (group.Count > 1 && JoinOutlines(tr, db, space, group, out m, out k, out bad)) merged = m + k;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWWALL: " + made + " wall outline(s) on " + WallLayer + ", " + _thicknessMm + " mm thick."
                + (merged > 0 ? " Joined into " + merged + " outline(s)." : "")
                + (skipped > 0 ? " " + skipped + " line(s) too short to make a wall." : ""));
        }

        private static Polyline Outline(Transaction tr, Database db, BlockTableRecord space, IList<P2> loop, double z, IEnumerable<string> ids)
        {
            var pl = new Polyline();
            for (int i = 0; i < loop.Count; i++) pl.AddVertexAt(i, new Point2d(loop[i].X, loop[i].Y), 0, 0, 0);
            pl.Closed = true;
            pl.Elevation = z;
            pl.Layer = WallLayer;
            WallStore.Tag(tr, db, pl, ids);
            space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return pl;
        }

        /// <summary>
        /// Merges closed outlines into their union. The new outlines carry the wall IDs of every source; the sources are erased.
        /// Returns false (and leaves everything as it was) when there is nothing to merge.
        /// </summary>
        internal static bool JoinOutlines(Transaction tr, Database db, BlockTableRecord space, List<Polyline> sources, out int made, out int kept, out int bad)
        {
            made = kept = bad = 0;
            double tol = Util.MmToDrawingUnits(0.05);
            var regions = new List<Region>();
            var used = new List<Polyline>();
            foreach (var pl in sources)
            {
                if (!pl.Closed) { bad++; continue; }
                try
                {
                    var coll = new DBObjectCollection();
                    coll.Add(pl);
                    var made1 = Region.CreateFromCurves(coll);
                    if (made1.Count == 0) { bad++; continue; }
                    foreach (DBObject o in made1) regions.Add((Region)o);
                    used.Add(pl);
                }
                catch (System.Exception) { bad++; }
            }
            if (regions.Count < 2)
            {
                foreach (var r in regions) r.Dispose();
                return false;
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

            var ids = new List<string>();
            foreach (var pl in used) ids.AddRange(WallStore.IdsOf(pl));
            var layer = used[0].Layer;
            double z = used[0].Elevation;
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
                    if (ids.Count > 0) WallStore.Tag(tr, db, ent, ids);
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
                if (ids.Count > 0) WallStore.Tag(tr, db, pl, ids);
                space.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                made++;
            }
            if (made + kept == 0) return false;
            foreach (var pl in used)
            {
                if (pl.IsErased) continue;
                if (!pl.IsWriteEnabled) pl.UpgradeOpen();
                pl.Erase();
            }
            return true;
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

            int made, kept, bad;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var sources = psr.Value.GetObjectIds().Select(id => (Polyline)tr.GetObject(id, OpenMode.ForRead)).ToList();
                if (!JoinOutlines(tr, db, space, sources, out made, out kept, out bad))
                {
                    ed.WriteMessage("\nHCWWALLJOIN: select at least two closed outlines that merge" + (bad > 0 ? " (" + bad + " could not be used)." : "."));
                    return;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWWALLJOIN: merged into " + (made + kept) + " outline(s)." + (bad > 0 ? " " + bad + " could not be used (open or not a plane shape)." : ""));
        }

        /// <summary>
        /// Rebuilds a group of walls: every outline (or loose line left by a cut) that carries any of the given wall IDs, directly or through a wall it is joined to,
        /// is erased and the walls are drawn again from their records and joined. The doors and windows in them are taken out first and cut back in at the same
        /// places. <paramref name="change"/> may alter the records first (and returns false to stop). Nothing is committed here: on false the caller leaves the
        /// transaction uncommitted so the whole thing is undone.
        /// </summary>
        internal static bool Rebuild(Transaction tr, Database db, BlockTableRecord space, IEnumerable<string> startIds, Func<List<WallRecord>, bool> change,
            out int wallCount, out int openingCount, out string error)
        {
            wallCount = openingCount = 0; error = null;
            double mm = Util.MmToDrawingUnits(1.0);
            var tagged = new List<Entity>();
            var tagIds = new List<IList<string>>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var ids = WallStore.IdsOf(ent);
                if (ids.Count == 0) continue;
                tagged.Add(ent); tagIds.Add(ids);
            }
            HashSet<string> group; List<int> members;
            WallIds.Group(tagIds, startIds, out group, out members);

            var records = new List<WallRecord>();
            foreach (var id in group.OrderBy(i => i))
            {
                var r = WallStore.Load(tr, db, id);
                if (r != null) records.Add(r);
            }
            if (records.Count == 0) { error = "the wall's record is missing from the drawing"; return false; }
            if (change != null && !change(records)) { error = "cancelled"; return false; }

            // doors and windows sitting in any of these walls are lifted out first and cut back in once the walls are redrawn
            var inGroup = new List<OpeningCommands.OpeningInfo>();
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var m in members)
            {
                var e = tagged[m].GeometricExtents;
                minX = Math.Min(minX, e.MinPoint.X); minY = Math.Min(minY, e.MinPoint.Y);
                maxX = Math.Max(maxX, e.MaxPoint.X); maxY = Math.Max(maxY, e.MaxPoint.Y);
            }
            foreach (var o in OpeningCommands.CollectInside(tr, space, minX, minY, maxX, maxY))
                if (records.Any(r => r.DistanceTo(o.Corners.Centre) <= Math.Max(r.ThicknessMm, o.ThicknessMm) * mm)) inGroup.Add(o);
            var tags = inGroup.Select(o => OpeningCommands.Heal(tr, space, o, false)).ToList();

            foreach (var m in members)
            {
                var ent = tagged[m];
                if (ent.IsErased) continue;
                ent.UpgradeOpen();
                ent.Erase();
            }

            var fresh = new List<Polyline>();
            foreach (var r in records)
            {
                WallStore.Save(tr, db, r);
                foreach (var loop in r.Outlines(mm)) fresh.Add(Outline(tr, db, space, loop, r.Z, new[] { r.Id }));
            }
            int jm, jk, jb;
            if (fresh.Count > 1) JoinOutlines(tr, db, space, fresh, out jm, out jk, out jb);

            for (int k = 0; k < inGroup.Count; k++)
            {
                var o = inGroup[k];
                var centre = new Point3d(o.Corners.Centre.X, o.Corners.Centre.Y, o.Z);
                string message;
                if (!OpeningCommands.PlaceIn(tr, o.Door, centre, OpeningCommands.PreviousSide(o, centre), o.Corners.Flipped, o.WidthMm,
                        OpeningCommands.ParamsOf(o), tags[k], out message))
                {
                    error = (tags[k] ?? "an opening") + " does not fit: " + message;
                    return false;
                }
            }
            wallCount = records.Count; openingCount = inGroup.Count;
            return true;
        }

        /// <summary>
        /// HCWWALLEDIT changes the thickness or line position of one wall drawn by HCWWALL. The walls it is joined to are rebuilt with it,
        /// and the doors and windows in them are cut again so they follow.
        /// </summary>
        [CommandMethod("HCWWALLEDIT")]
        public void EditWall()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var per = new PromptEntityOptions("\nPick the wall to edit (a wall outline or a piece of one): ");
            per.SetRejectMessage("\nPick a wall outline.");
            per.AddAllowedClass(typeof(Polyline), true);
            per.AddAllowedClass(typeof(Line), true);
            var pr = ed.GetEntity(per);
            if (pr.Status != PromptStatus.OK) return;
            var pick = pr.PickedPoint;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var picked = tr.GetObject(pr.ObjectId, OpenMode.ForRead) as Entity;
                var start = picked == null ? new List<string>() : WallStore.IdsOf(picked);
                if (start.Count == 0)
                {
                    ed.WriteMessage("\nHCWWALLEDIT: that outline is not tied to a wall object. HCWWALLADOPT makes wall objects from walls drawn by hand.");
                    return;
                }
                WallRecord target = null;
                int walls, openings; string error;
                bool ok = Rebuild(tr, db, space, start, records =>
                {
                    var here = new P2(pick.X, pick.Y);
                    target = records.OrderBy(r => r.DistanceTo(here)).First();
                    var t = ed.GetDouble(new PromptDoubleOptions("\nThickness of wall " + target.Id + " in mm <" + target.ThicknessMm + ">: ")
                        { AllowNegative = false, AllowZero = false, DefaultValue = target.ThicknessMm, UseDefaultValue = true });
                    if (t.Status != PromptStatus.OK) return false;
                    var jo = new PromptKeywordOptions("\nLine position [Centre/Left/Right] <" + target.Justify + ">: ", "Centre Left Right") { AllowNone = true };
                    jo.Keywords.Default = target.Justify.ToString();
                    var jr = ed.GetKeywords(jo);
                    var justify = target.Justify;
                    if (jr.Status == PromptStatus.OK) Enum.TryParse(jr.StringResult, out justify);
                    else if (jr.Status != PromptStatus.None) return false;
                    target.ThicknessMm = t.Value;
                    target.Justify = justify;
                    return true;
                }, out walls, out openings, out error);
                if (!ok)
                {
                    if (error != "cancelled") ed.WriteMessage("\nHCWWALLEDIT: " + error + ". Nothing was changed.");
                    return;                                         // uncommitted: the whole edit is rolled back
                }
                tr.Commit();
                ed.WriteMessage("\nHCWWALLEDIT: wall " + target.Id + " is now " + target.ThicknessMm + " mm, line on the " + target.Justify.ToString().ToLowerInvariant()
                    + ". " + walls + " joined wall(s) rebuilt" + (openings > 0 ? ", " + openings + " opening(s) re-cut." : "."));
            }
        }

        /// <summary>
        /// HCWWALLADOPT makes wall objects from walls drawn some other way. Select closed wall outlines: wherever two straight faces a wall's thickness apart
        /// (HCWWALLMINMM to HCWWALLMAXMM, 60 to 600) face each other and overlap, that is a wall, and its centre line and thickness are saved so HCWWALLEDIT
        /// can change it. The outline is tagged with the walls found in it. A curved outline, or faces that are not parallel, give no wall.
        /// </summary>
        [CommandMethod("HCWWALLADOPT")]
        public void AdoptWalls()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the closed wall outlines to make wall objects from: " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") }));
            if (psr.Status != PromptStatus.OK) return;
            double mm = Util.MmToDrawingUnits(1.0);
            int outlines = 0, walls = 0, skipped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                    if (!pl.Closed || WallStore.IdsOf(pl).Count > 0) { skipped++; continue; }
                    var pts = new List<P2>();
                    bool curved = false;
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                    {
                        var p = pl.GetPoint2dAt(i); pts.Add(new P2(p.X, p.Y));
                        if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) curved = true;
                    }
                    if (curved) { skipped++; continue; }
                    var found = WallInference.Infer(pts, Settings.GetDouble("WallMinMm", 60) * mm, Settings.GetDouble("WallMaxMm", 600) * mm, 1 * mm);
                    if (found.Count == 0) { skipped++; continue; }
                    var ids = new List<string>();
                    foreach (var w in found)
                    {
                        var rec = new WallRecord
                        {
                            Id = WallStore.NextId(tr, db), ThicknessMm = Math.Round(w.Thickness / mm, 1), Justify = WallJustify.Centre, Closed = false, Z = pl.Elevation,
                            Points = new List<P2> { w.A, w.B },
                        };
                        WallStore.Save(tr, db, rec);
                        ids.Add(rec.Id);
                        walls++;
                    }
                    pl.UpgradeOpen();
                    WallStore.Tag(tr, db, pl, ids);
                    outlines++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWWALLADOPT: " + walls + " wall object(s) from " + outlines + " outline(s)."
                + (skipped > 0 ? " " + skipped + " outline(s) skipped (open, curved, already wall objects, or no pair of faces a wall's thickness apart)." : "")
                + " Edit them with HCWWALLEDIT.");
        }
    }
}
