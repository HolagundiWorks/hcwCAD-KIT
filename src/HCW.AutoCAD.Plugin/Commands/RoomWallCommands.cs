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
    /// HCWROOMWALLS finds rooms from the wall faces: pick a point inside a room and the closed outline of the clear room is drawn
    /// on ROOM-RECT (the same layer the room tools use, which AUTODIMROOM reads), with an optional name and area label on
    /// ROOM-LABELS. Choose All at the pick prompt to find every room in the drawing in one go. The walls are the lines, arcs and
    /// polylines (curved segments included) on the layer(s) of the wall objects you select (A-WALL by default). A room has to be closed;
    /// gaps up to the size you give (an unframed door opening) are bridged. Free-standing columns inside a room come off its area.
    /// Areas are in square metres whatever the drawing units.
    /// </summary>
    public class RoomWallCommands
    {
        private const string RectLayer = "ROOM-RECT", LabelLayer = "ROOM-LABELS";
        private const double TextMm = 125, SnapMm = 1, SagittaMm = 2, WallBodyMm = 600;

        private static double _gapMm = 0;

        /// <summary>The wall segments on the given layers: lines, polylines and arcs, with curves cut into short straight pieces.</summary>
        internal static List<Seg> WallSegments(Transaction tr, BlockTableRecord space, ICollection<string> layers)
        {
            double sagitta = SagittaMm * Util.MmToDrawingUnits(1.0);
            var segs = new List<Seg>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !layers.Contains(ent.Layer)) continue;
                var ln = ent as Line;
                var pl = ent as Polyline;
                var arc = ent as Arc;
                if (ln != null) segs.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)));
                else if (arc != null)
                {
                    double sweep = arc.EndAngle - arc.StartAngle;
                    if (sweep <= 0) sweep += 2 * Math.PI;
                    AddChain(segs, CurveSampler.ArcPoints(new P2(arc.Center.X, arc.Center.Y), arc.Radius, arc.StartAngle, sweep, sagitta));
                }
                else if (pl != null)
                {
                    int n = pl.NumberOfVertices, count = pl.Closed ? n : n - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % n);
                        AddChain(segs, CurveSampler.BulgePoints(new P2(a.X, a.Y), new P2(b.X, b.Y), pl.GetBulgeAt(i), sagitta));
                    }
                }
            }
            return segs;
        }

        private static void AddChain(List<Seg> into, IList<P2> pts)
        {
            for (int i = 0; i + 1 < pts.Count; i++) into.Add(new Seg(pts[i], pts[i + 1]));
        }

        [CommandMethod("HCWROOMWALLS")]
        public void RoomsFromWalls()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect wall lines, arcs or polylines to use their layer (Enter = layer " + WallCommands.WallLayer + "): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,ARC") }));
            if (psr.Status == PromptStatus.OK)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in psr.Value.GetObjectIds()) layers.Add(((Entity)tr.GetObject(id, OpenMode.ForRead)).Layer);
                    tr.Commit();
                }
            }
            else if (psr.Status == PromptStatus.None) layers.Add(WallCommands.WallLayer);
            else return;

            var gap = ed.GetDouble(new PromptDoubleOptions("\nBridge gaps in the walls up to mm, for doors drawn without jambs (0 = none) <" + _gapMm + ">: ")
                { AllowNegative = false, AllowZero = true, DefaultValue = _gapMm, UseDefaultValue = true });
            if (gap.Status != PromptStatus.OK) return;
            _gapMm = gap.Value;

            List<Seg> segs;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                segs = WallSegments(tr, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead), layers);
                tr.Commit();
            }
            if (segs.Count < 3)
            {
                ed.WriteMessage("\nHCWROOMWALLS: found only " + segs.Count + " wall line(s) on " + string.Join(", ", layers) + ".");
                return;
            }
            ed.WriteMessage("\nHCWROOMWALLS: " + segs.Count + " wall piece(s) on " + string.Join(", ", layers) + ".");

            double mm = Util.MmToDrawingUnits(1.0);
            double upm = Util.MmToDrawingUnits(1000.0);
            double gapUnits = _gapMm * mm;
            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var po = new PromptPointOptions("\nPick a point inside a room [All] (Enter to finish): ", "All") { AllowNone = true };
                var pr = ed.GetPoint(po);
                if (pr.Status == PromptStatus.Keyword)
                {
                    AllRooms(ed, db, segs, SnapMm * mm, WallBodyMm * mm, gapUnits, upm);
                    continue;
                }
                if (pr.Status != PromptStatus.OK) return;
                var pick3 = pr.Value.TransformBy(ucs);
                var pick = new P2(pick3.X, pick3.Y);

                string error;
                var room = PlanarRooms.Find(segs, pick, SnapMm * mm, out error, gapUnits);
                if (room == null) { ed.WriteMessage("\nHCWROOMWALLS: " + error + ". Check that the walls close, or give a gap size."); continue; }

                double sqm = Math.Abs(PlanarRooms.SignedArea(room)) / (upm * upm);
                var nr = ed.GetString(new PromptStringOptions("\nRoom name (Enter for none): ") { AllowSpaces = true });
                string name = nr.Status == PromptStatus.OK ? nr.StringResult.Trim() : "";
                if (nr.Status == PromptStatus.Cancel) return;

                Draw(db, room, name, sqm, pick, pick3.Z, TextMm * mm);
                ed.WriteMessage("\nHCWROOMWALLS: " + (name.Length > 0 ? name.ToUpperInvariant() + ", " : "") + sqm.ToString("F2") + " m2, " + room.Count + " corners. Check the area: a room that is not closed gives the larger space around it.");
            }
        }

        private static void AllRooms(Editor ed, Database db, List<Seg> segs, double tol, double minThickness, double gap, double upm)
        {
            var rooms = PlanarRooms.AllRooms(segs, tol, minThickness, gap);
            if (rooms.Count == 0) { ed.WriteMessage("\nHCWROOMWALLS: no closed rooms found. Check that the walls close, or give a gap size."); return; }
            double mm = Util.MmToDrawingUnits(1.0);
            int n = 0;
            double total = 0;
            foreach (var r in rooms)
            {
                n++;
                double net = r.NetArea / (upm * upm);
                total += net;
                var c = PlanarRooms.Centroid(r.Outline);
                if (!PlanarRooms.Contains(r.Outline, c)) c = r.Outline[0];
                Draw(db, r.Outline, "R" + n, net, c, 0, TextMm * mm);
                ed.WriteMessage("\n  R" + n + ": " + net.ToString("F2") + " m2" + (r.Holes.Count > 0 ? " (" + r.Holes.Count + " column(s) taken out)" : ""));
            }
            ed.WriteMessage("\nHCWROOMWALLS: " + rooms.Count + " room(s), " + total.ToString("F2") + " m2 in all, labelled R1, R2 … Rename them with RTAG.");
        }

        private static void Draw(Database db, List<P2> room, string name, double sqm, P2 labelAt, double z, double h)
        {
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, RectLayer, 8);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var pl = new Polyline();
                for (int i = 0; i < room.Count; i++) pl.AddVertexAt(i, new Point2d(room[i].X, room[i].Y), 0, 0, 0);
                pl.Closed = true;
                pl.Elevation = z;
                pl.Layer = RectLayer;
                space.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                if (name.Length > 0)
                {
                    Util.EnsureLayer(tr, db, LabelLayer, 3);
                    var c = PlanarRooms.Centroid(room);
                    if (!PlanarRooms.Contains(room, c)) c = labelAt;       // a bent room's centre can fall outside it
                    foreach (var line in new[] { new KeyValuePair<string, double>(name.ToUpperInvariant(), h * 0.75), new KeyValuePair<string, double>("Area: " + sqm.ToString("F2") + " m2", -h * 0.75) })
                    {
                        var t = new DBText
                        {
                            Height = h, TextString = line.Key, Layer = LabelLayer, Position = new Point3d(c.X, c.Y + line.Value, z),
                            HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                        };
                        t.AlignmentPoint = new Point3d(c.X, c.Y + line.Value, z);
                        space.AppendEntity(t);
                        tr.AddNewlyCreatedDBObject(t, true);
                    }
                }
                tr.Commit();
            }
        }
    }
}
