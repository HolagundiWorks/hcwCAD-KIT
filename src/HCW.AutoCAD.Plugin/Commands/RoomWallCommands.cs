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
    /// HCWROOMWALLS finds a room from the wall faces: pick a point inside it and the closed outline of the clear room is drawn
    /// on ROOM-RECT (the same layer the room tools use, which AUTODIMROOM reads), with an optional name and area label on
    /// ROOM-LABELS. The walls are the lines and polylines on the layer(s) of the wall objects you select (A-WALL by default).
    /// A room has to be closed, door and window openings included (the opening tools close them with jamb lines).
    /// Areas are in square metres whatever the drawing units.
    /// </summary>
    public class RoomWallCommands
    {
        private const string RectLayer = "ROOM-RECT", LabelLayer = "ROOM-LABELS";
        private const double TextMm = 125, SnapMm = 1;

        [CommandMethod("HCWROOMWALLS")]
        public void RoomsFromWalls()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect wall lines or polylines to use their layer (Enter = layer " + WallCommands.WallLayer + "): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE") }));
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

            var segs = new List<Seg>();
            int arcs = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !layers.Contains(ent.Layer)) continue;
                    var ln = ent as Line;
                    var pl = ent as Polyline;
                    if (ln != null) segs.Add(new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)));
                    else if (pl != null)
                    {
                        int n = pl.NumberOfVertices, count = pl.Closed ? n : n - 1;
                        for (int i = 0; i < count; i++)
                        {
                            if (pl.GetSegmentType(i) != SegmentType.Line) { arcs++; continue; }
                            var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % n);
                            segs.Add(new Seg(new P2(a.X, a.Y), new P2(b.X, b.Y)));
                        }
                    }
                }
                tr.Commit();
            }
            if (segs.Count < 3)
            {
                ed.WriteMessage("\nHCWROOMWALLS: found only " + segs.Count + " wall line(s) on " + string.Join(", ", layers) + ".");
                return;
            }
            ed.WriteMessage("\nHCWROOMWALLS: " + segs.Count + " wall line(s) on " + string.Join(", ", layers)
                + (arcs > 0 ? "; " + arcs + " curved segment(s) are not used, so a room with a curved wall will not close." : "."));

            double mm = Util.MmToDrawingUnits(1.0);
            double upm = Util.MmToDrawingUnits(1000.0);
            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick a point inside a room (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var pick3 = pr.Value.TransformBy(ucs);
                var pick = new P2(pick3.X, pick3.Y);

                string error;
                var room = PlanarRooms.Find(segs, pick, SnapMm * mm, out error);
                if (room == null) { ed.WriteMessage("\nHCWROOMWALLS: " + error + ". Check that the walls close, door and window openings included."); continue; }

                double sqm = Math.Abs(PlanarRooms.SignedArea(room)) / (upm * upm);
                var nr = ed.GetString(new PromptStringOptions("\nRoom name (Enter for none): ") { AllowSpaces = true });
                string name = nr.Status == PromptStatus.OK ? nr.StringResult.Trim() : "";
                if (nr.Status == PromptStatus.Cancel) return;

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Util.EnsureLayer(tr, db, RectLayer, 8);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var pl = new Polyline();
                    for (int i = 0; i < room.Count; i++) pl.AddVertexAt(i, new Point2d(room[i].X, room[i].Y), 0, 0, 0);
                    pl.Closed = true;
                    pl.Elevation = pick3.Z;
                    pl.Layer = RectLayer;
                    space.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    if (name.Length > 0)
                    {
                        Util.EnsureLayer(tr, db, LabelLayer, 3);
                        var c = PlanarRooms.Centroid(room);
                        if (!PlanarRooms.Contains(room, c)) c = pick;       // a bent room's centre can fall outside it
                        double h = TextMm * mm;
                        foreach (var line in new[] { new KeyValuePair<string, double>(name.ToUpperInvariant(), h * 0.75), new KeyValuePair<string, double>("Area: " + sqm.ToString("F2") + " m2", -h * 0.75) })
                        {
                            var t = new DBText
                            {
                                Height = h, TextString = line.Key, Layer = LabelLayer, Position = new Point3d(c.X, c.Y + line.Value, pick3.Z),
                                HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                            };
                            t.AlignmentPoint = new Point3d(c.X, c.Y + line.Value, pick3.Z);
                            space.AppendEntity(t);
                            tr.AddNewlyCreatedDBObject(t, true);
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\nHCWROOMWALLS: " + (name.Length > 0 ? name.ToUpperInvariant() + ", " : "") + sqm.ToString("F2") + " m2, " + room.Count + " corners. Check the area: a room that is not closed gives the larger space around it.");
            }
        }
    }
}
