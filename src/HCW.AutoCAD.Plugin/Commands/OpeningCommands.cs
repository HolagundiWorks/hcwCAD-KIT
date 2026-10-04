using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWDOOR and HCWWINDOW: pick a point on a wall, and the two wall faces are cut for the opening and closed
    /// with jamb lines. The door or window goes in as a block (HCW_D_900x230, HCW_W_1200x230) that carries a line on
    /// MEASURE-DEDUCT, so the take-off and the schedule read it like any other opening block. A tag (D1, W1) is placed.
    /// A wall is any run of lines or polylines on one layer: faces are found from the line you pick near.
    /// </summary>
    public class OpeningCommands
    {
        private const string LayerDoor = "A-DOOR";
        private const string LayerWin = "A-WIND";
        private const string LayerTag = "AN-TEXT";
        private const double MinThickMm = 60, MaxThickMm = 600, TagHeightMm = 250;

        private static double _doorMm = 900;
        private static double _windowMm = 1200;

        private class SegRef
        {
            public ObjectId Owner;
            public Seg Seg;
            public string Layer;
        }

        [CommandMethod("HCWDOOR")]
        public void Door() => Run(true);

        [CommandMethod("HCWWINDOW")]
        public void Window() => Run(false);

        private static void Run(bool door)
        {
            var ed = Util.Ed;
            string what = door ? "door" : "window";
            double current = door ? _doorMm : _windowMm;
            var wr = ed.GetDouble(new PromptDoubleOptions("\nWidth of the " + what + " in mm <" + current + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = current, UseDefaultValue = true });
            if (wr.Status != PromptStatus.OK) return;
            if (door) _doorMm = wr.Value; else _windowMm = wr.Value;

            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the " + what + " position on the wall (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var pick = pr.Value.TransformBy(ucs);

                bool flip = false;
                Point3d side = pick;
                if (door)
                {
                    bool got = false;
                    while (!got)
                    {
                        var so = new PromptPointOptions("\nPick the side the door opens to [Flip hinge]: ", "Flip");
                        var sr = ed.GetPoint(so);
                        if (sr.Status == PromptStatus.Keyword) { flip = !flip; ed.WriteMessage("\nHinge on the other end."); continue; }
                        if (sr.Status != PromptStatus.OK) return;
                        side = sr.Value.TransformBy(ucs);
                        got = true;
                    }
                }

                string message;
                try { message = Place(door, pick, side, flip, door ? _doorMm : _windowMm); }
                catch (System.Exception ex) { message = "failed: " + ex.Message; }
                ed.WriteMessage("\n" + (door ? "HCWDOOR: " : "HCWWINDOW: ") + message);
            }
        }

        private static string Place(bool door, Point3d pickW, Point3d sideW, bool flip, double widthMm)
        {
            var db = Util.Db;
            double w = Util.MmToDrawingUnits(widthMm);
            double minT = Util.MmToDrawingUnits(MinThickMm), maxT = Util.MmToDrawingUnits(MaxThickMm);
            var pick = new P2(pickW.X, pickW.Y);

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // The wall layer is the layer of the line nearest the pick; its faces are every segment on that layer.
                var near = Segments(tr, space, pick, maxT + w);
                SegRef nearest = null; double nd = double.MaxValue;
                foreach (var r in near)
                {
                    double d = OpeningCut.DistanceToSegment(r.Seg, pick);
                    if (d < nd) { nd = d; nearest = r; }
                }
                if (nearest == null || nd > maxT) return "no wall face near that point.";
                var faces = near.Where(r => string.Equals(r.Layer, nearest.Layer, StringComparison.OrdinalIgnoreCase)).ToList();

                string error;
                var plan = OpeningCut.Plan(faces.Select(r => r.Seg).ToList(), pick, w, minT, maxT, out error);
                if (plan == null) return error + ".";

                // Turn the two faces into single lines (a polyline outline is exploded where it is cut).
                var exploded = new Dictionary<ObjectId, List<Line>>();
                Line first = Resolve(tr, space, faces[plan.First], exploded);
                Line second = Resolve(tr, space, faces[plan.Second], exploded);
                if (first == null || second == null) return "could not read the wall faces.";
                string wallLayer = first.Layer;
                double z = pickW.Z;

                var p1a = plan.P1a; var p1b = plan.P1b; var p2a = plan.P2a; var p2b = plan.P2b;
                Cut(tr, space, first, p1a, p1b);
                Cut(tr, space, second, p2a, p2b);
                AddLine(tr, space, p1a, p2a, wallLayer, z);
                AddLine(tr, space, p1b, p2b, wallLayer, z);

                // Orientation. n is the left normal of the first face; s2 is the side the second face lies on.
                var u = plan.Dir;
                var n = new P2(-u.Y, u.X);
                double s2 = Math.Sign(P2.Cross(u, p2a - p1a));
                double thickMm = Math.Round(plan.Thickness / Util.MmToDrawingUnits(1.0));
                string size = Math.Round(widthMm) + "x" + thickMm;
                double textH = Util.MmToDrawingUnits(TagHeightMm);
                double ang = Math.Atan2(u.Y, u.X);

                Point3d origin; double sx, sy; P2 tagAt; string tag;
                if (door)
                {
                    var side = new P2(sideW.X, sideW.Y);
                    double sw = Math.Sign(P2.Dot(side - p1a, n));
                    if (sw == 0) sw = -s2;
                    bool hingeOnFirst = sw == -s2;
                    var a = hingeOnFirst ? p1a : p2a;
                    var b = hingeOnFirst ? p1b : p2b;
                    var o = flip ? b : a;
                    origin = new Point3d(o.X, o.Y, z);
                    sx = flip ? -1 : 1; sy = sw;
                    // Tag on the side the door does not swing to, just outside the wall.
                    var oppA = hingeOnFirst ? p2a : p1a; var oppB = hingeOnFirst ? p2b : p1b;
                    tagAt = (oppA + oppB) * 0.5 + n * (-sw * textH * 1.0);
                    EnsureDoorBlock(tr, db, "HCW_D_" + size, w, plan.Thickness);
                    tag = NextTag(tr, space, "D");
                    InsertBlock(tr, space, "HCW_D_" + size, origin, ang, sx, sy, LayerDoor);
                }
                else
                {
                    origin = new Point3d(p1a.X, p1a.Y, z);
                    sx = 1; sy = s2;
                    tagAt = (p1a + p1b) * 0.5 + n * (-s2 * textH * 1.0);
                    EnsureWindowBlock(tr, db, "HCW_W_" + size, w, plan.Thickness);
                    tag = NextTag(tr, space, "W");
                    InsertBlock(tr, space, "HCW_W_" + size, origin, ang, sx, sy, LayerWin);
                }

                Util.EnsureHcwLayer(tr, db, LayerTag);
                var text = new DBText
                {
                    Position = new Point3d(tagAt.X, tagAt.Y, z),
                    Height = textH,
                    TextString = tag,
                    Layer = LayerTag,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                };
                text.AlignmentPoint = new Point3d(tagAt.X, tagAt.Y, z);
                space.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                tr.Commit();
                return tag + " " + Math.Round(widthMm) + " mm in a " + thickMm + " mm wall.";
            }
        }

        /// <summary>Straight segments of lines and polylines whose box comes within reach of the pick.</summary>
        private static List<SegRef> Segments(Transaction tr, BlockTableRecord space, P2 pick, double reach)
        {
            var res = new List<SegRef>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (!(ent is Line) && !(ent is Polyline)) continue;
                try
                {
                    var e = ent.GeometricExtents;
                    if (pick.X < e.MinPoint.X - reach || pick.X > e.MaxPoint.X + reach
                        || pick.Y < e.MinPoint.Y - reach || pick.Y > e.MaxPoint.Y + reach) continue;
                }
                catch (System.Exception) { continue; }

                var line = ent as Line;
                if (line != null)
                {
                    res.Add(new SegRef { Owner = id, Layer = line.Layer, Seg = new Seg(new P2(line.StartPoint.X, line.StartPoint.Y), new P2(line.EndPoint.X, line.EndPoint.Y)) });
                    continue;
                }
                var pl = (Polyline)ent;
                int count = pl.NumberOfVertices;
                int segs = pl.Closed ? count : count - 1;
                for (int i = 0; i < segs; i++)
                {
                    if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                    var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % count);
                    res.Add(new SegRef { Owner = id, Layer = pl.Layer, Seg = new Seg(new P2(a.X, a.Y), new P2(b.X, b.Y)) });
                }
            }
            return res;
        }

        /// <summary>The line for a wall face: the line itself, or the matching piece of an exploded polyline.</summary>
        private static Line Resolve(Transaction tr, BlockTableRecord space, SegRef r, Dictionary<ObjectId, List<Line>> exploded)
        {
            List<Line> pieces;
            if (!exploded.TryGetValue(r.Owner, out pieces))
            {
                var ent = tr.GetObject(r.Owner, OpenMode.ForWrite);
                var line = ent as Line;
                if (line != null) return line;
                var pl = ent as Polyline;
                if (pl == null) return null;

                var parts = new DBObjectCollection();
                pl.Explode(parts);
                pieces = new List<Line>();
                foreach (DBObject o in parts)
                {
                    var piece = (Entity)o;
                    piece.SetPropertiesFrom(pl);
                    space.AppendEntity(piece);
                    tr.AddNewlyCreatedDBObject(piece, true);
                    if (piece is Line l) pieces.Add(l);
                }
                pl.Erase();
                exploded[r.Owner] = pieces;
            }
            double tol = Util.MmToDrawingUnits(0.05);
            foreach (var l in pieces)
            {
                if (l.IsErased) continue;
                var a = new P2(l.StartPoint.X, l.StartPoint.Y); var b = new P2(l.EndPoint.X, l.EndPoint.Y);
                bool same = a.DistanceTo(r.Seg.A) <= tol && b.DistanceTo(r.Seg.B) <= tol;
                bool flipped = a.DistanceTo(r.Seg.B) <= tol && b.DistanceTo(r.Seg.A) <= tol;
                if (same || flipped) return l;
            }
            return null;
        }

        /// <summary>Removes the stretch between two points on a line, keeping what is left at either end.</summary>
        private static void Cut(Transaction tr, BlockTableRecord space, Line ln, P2 a, P2 b)
        {
            var s = ln.StartPoint; var e = ln.EndPoint;
            var seg = new Seg(new P2(s.X, s.Y), new P2(e.X, e.Y));
            if (seg.Length < 1e-9) return;
            var u = (seg.B - seg.A) * (1.0 / seg.Length);
            double ta = P2.Dot(a - seg.A, u), tb = P2.Dot(b - seg.A, u);
            var pieces = OpeningCut.Remove(seg, Math.Min(ta, tb), Math.Max(ta, tb));
            if (pieces.Count == 0) { ln.Erase(); return; }
            double z = s.Z;
            if (pieces.Count > 1)
            {
                var more = (Line)ln.Clone();
                more.StartPoint = new Point3d(pieces[1].A.X, pieces[1].A.Y, z);
                more.EndPoint = new Point3d(pieces[1].B.X, pieces[1].B.Y, z);
                space.AppendEntity(more);
                tr.AddNewlyCreatedDBObject(more, true);
            }
            ln.StartPoint = new Point3d(pieces[0].A.X, pieces[0].A.Y, z);
            ln.EndPoint = new Point3d(pieces[0].B.X, pieces[0].B.Y, z);
        }

        private static void AddLine(Transaction tr, BlockTableRecord space, P2 a, P2 b, string layer, double z)
        {
            var ln = new Line(new Point3d(a.X, a.Y, z), new Point3d(b.X, b.Y, z)) { Layer = layer };
            space.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        /// <summary>
        /// Door block: wall body below the x axis (y from -thickness to 0), the leaf opened 90 degrees up from the
        /// hinge at the origin and its swing arc, and the opening length on MEASURE-DEDUCT through the middle of the wall.
        /// </summary>
        private static void EnsureDoorBlock(Transaction tr, Database db, string name, double w, double thickness)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name)) return;
            Util.EnsureHcwLayer(tr, db, LayerDoor);
            Util.EnsureLayer(tr, db, MeasureCommands.LayDed, 6);
            var def = NewBlock(tr, bt, name);
            Add(tr, def, new Line(Point3d.Origin, new Point3d(0, w, 0)) { Layer = LayerDoor });
            Add(tr, def, new Arc(Point3d.Origin, w, 0, Math.PI / 2) { Layer = LayerDoor });
            Add(tr, def, new Line(new Point3d(0, -thickness / 2, 0), new Point3d(w, -thickness / 2, 0)) { Layer = MeasureCommands.LayDed });
        }

        /// <summary>Window block: four lines across the opening (the two faces and two frame lines), wall body above the x axis.</summary>
        private static void EnsureWindowBlock(Transaction tr, Database db, string name, double w, double thickness)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name)) return;
            Util.EnsureHcwLayer(tr, db, LayerWin);
            Util.EnsureLayer(tr, db, MeasureCommands.LayDed, 6);
            var def = NewBlock(tr, bt, name);
            for (int k = 0; k <= 3; k++)
            {
                double y = thickness * k / 3.0;
                Add(tr, def, new Line(new Point3d(0, y, 0), new Point3d(w, y, 0)) { Layer = LayerWin });
            }
            Add(tr, def, new Line(new Point3d(0, thickness / 2, 0), new Point3d(w, thickness / 2, 0)) { Layer = MeasureCommands.LayDed });
        }

        private static BlockTableRecord NewBlock(Transaction tr, BlockTable bt, string name)
        {
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            return def;
        }

        private static void Add(Transaction tr, BlockTableRecord def, Entity ent)
        {
            def.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        private static void InsertBlock(Transaction tr, BlockTableRecord space, string name, Point3d at, double angle, double sx, double sy, string layer)
        {
            var bt = (BlockTable)tr.GetObject(Util.Db.BlockTableId, OpenMode.ForRead);
            var br = new BlockReference(at, bt[name]) { Layer = layer, Rotation = angle, ScaleFactors = new Scale3d(sx, sy, 1) };
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }

        /// <summary>The next free tag: one more than the highest D or W number already on the tag layer.</summary>
        private static string NextTag(Transaction tr, BlockTableRecord space, string prefix)
        {
            var rx = new Regex("^" + prefix + "(\\d+)$", RegexOptions.IgnoreCase);
            int max = 0;
            foreach (ObjectId id in space)
            {
                var t = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (t == null || !string.Equals(t.Layer, LayerTag, StringComparison.OrdinalIgnoreCase)) continue;
                var m = rx.Match(t.TextString ?? "");
                int n;
                if (m.Success && int.TryParse(m.Groups[1].Value, out n)) max = Math.Max(max, n);
            }
            return prefix + (max + 1);
        }
    }
}
