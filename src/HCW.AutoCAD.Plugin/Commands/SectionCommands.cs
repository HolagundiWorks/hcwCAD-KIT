using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWSECTIONDRAW draws a building section from the plan: the walls the section line cuts, the doors and windows in them, and the slabs,
    /// with the floor, slab and lintel heights from the levels in the drawing (the Floors tab of MSCHED). HCWLEVELS shows those levels.
    /// </summary>
    public class SectionCommands
    {
        private static string CutLayer => Util.Out("A-SECT", "BP-SECTION");
        private const string LayerText = "AN-TEXT";

        [CommandMethod("HCWLEVELS")]
        public void ShowLevels()
        {
            var ed = Util.Ed;
            var l = LevelStore.Load();
            if (l.Count == 0)
            {
                ed.WriteMessage("\nHCWLEVELS: no floors are defined. Add them on the Floors tab of MSCHED (floor to floor, ceiling, lintel bottom, slab thickness).");
                return;
            }
            ed.WriteMessage("\nHCWLEVELS: heights are kept in the drawing with the take-off book (MSCHED, Floors tab) and used by HCWSECTIONDRAW, AECSTAIR, HCWDOOR, HCWWINDOW and HCWOPENHEIGHT.");
            var elev = SectionBuilder.Elevations(l);
            for (int i = 0; i < l.Count; i++)
                ed.WriteMessage("\n  " + l[i].Name + ": FFL +" + (elev[i] / 1000).ToString("0.000", CultureInfo.InvariantCulture)
                    + ", floor to floor " + l[i].FflMm.ToString("0") + ", ceiling " + l[i].CeilingMm.ToString("0")
                    + ", lintel bottom " + l[i].LintelMm.ToString("0") + ", slab " + l[i].SlabMm.ToString("0") + " mm");
        }

        [CommandMethod("HCWSECTIONDRAW")]
        public void DrawSection()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var levels = LevelStore.Load();
            if (levels.Count == 0)
            {
                var one = new LevelRow
                {
                    FflMm = Settings.GetDouble("DefaultFflHeight", 3.15) * LevelStore.UnitMm, CeilingMm = Settings.GetDouble("DefaultCeilingHeight", 3.0) * LevelStore.UnitMm,
                    LintelMm = Settings.GetDouble("DefaultLintelBottom", 2.1) * LevelStore.UnitMm, SlabMm = Settings.GetDouble("DefaultSlabThickness", 0.15) * LevelStore.UnitMm,
                };
                levels.Add(one);
                ed.WriteMessage("\nNo floors are defined in the drawing (MSCHED, Floors tab). Drawing one floor from the default heights: floor to floor "
                    + one.FflMm.ToString("0") + ", lintel bottom " + one.LintelMm.ToString("0") + ", slab " + one.SlabMm.ToString("0") + " mm.");
            }
            else
            {
                var all = new PromptKeywordOptions("\nFloors to draw [All/One] <All>: ", "All One") { AllowNone = true };
                all.Keywords.Default = "All";
                var ar = ed.GetKeywords(all);
                if (ar.Status != PromptStatus.OK && ar.Status != PromptStatus.None) return;
                if (ar.Status == PromptStatus.OK && ar.StringResult == "One")
                {
                    var names = string.Join(", ", levels.Select((l, i) => (i + 1) + " " + l.Name));
                    var ir = ed.GetInteger(new PromptIntegerOptions("\nFloor number (" + names + ") <1>: ") { LowerLimit = 1, UpperLimit = levels.Count, DefaultValue = 1, UseDefaultValue = true });
                    if (ir.Status != PromptStatus.OK) return;
                    levels = new List<LevelRow> { levels[ir.Value - 1] };
                }
            }

            var p1 = ed.GetPoint("\nStart of the section line: ");
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetPoint(new PromptPointOptions("\nEnd of the section line: ") { UseBasePoint = true, BasePoint = p1.Value, UseDashedLine = true });
            if (p2.Status != PromptStatus.OK) return;
            var sp = ed.GetPoint("\nPick the side you look toward: ");
            if (sp.Status != PromptStatus.OK) return;
            var ucs = ed.CurrentUserCoordinateSystem;
            var a3 = p1.Value.TransformBy(ucs); var b3 = p2.Value.TransformBy(ucs); var s3 = sp.Value.TransformBy(ucs);
            var a = new P2(a3.X, a3.Y); var b = new P2(b3.X, b3.Y);
            double perMm = Util.MmToDrawingUnits(1.0);
            double length = a.DistanceTo(b) / perMm;
            if (length < 1) { ed.WriteMessage("\nHCWSECTIONDRAW: the two points are the same."); return; }
            var dir = (b - a) * (1.0 / a.DistanceTo(b));
            var look = SymbolMath.LookNormal(a, b, new P2(s3.X, s3.Y));
            var right = new P2(look.Y, -look.X);
            bool flip = P2.Dot(dir, right) < 0;           // looking toward the section, the start of the line is on the right

            var at = ed.GetPoint("\nPick the base point of the section drawing (finished floor level of the first floor): ");
            if (at.Status != PromptStatus.OK) return;
            var o = at.Value.TransformBy(ucs);

            var walls = new List<SectionWall>(); var cross = new List<double>();
            var ops = new List<SectionOpening>();
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var wallLayers = new HashSet<string>(new[] { WallCommands.WallLayer, ColumnCommands.LayerColumn, "A-WALL", "A-COL", MeasureCommands.LayCol }, StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    var ln = ent as Line; var pl = ent as Polyline;
                    if (ln != null && wallLayers.Contains(ln.Layer))
                        AddCross(cross, a, b, new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y), perMm);
                    else if (pl != null && wallLayers.Contains(pl.Layer))
                    {
                        int n = pl.NumberOfVertices, last = pl.Closed ? n : n - 1;
                        for (int i = 0; i < last; i++)
                        {
                            var p = pl.GetPoint2dAt(i); var q = pl.GetPoint2dAt((i + 1) % n);
                            AddCross(cross, a, b, new P2(p.X, p.Y), new P2(q.X, q.Y), perMm);
                        }
                    }
                    var br = ent as BlockReference;
                    if (br != null)
                    {
                        var info = OpeningCommands.ReadOpening(tr, id);
                        if (info == null) continue;
                        var c = info.Corners;
                        double t0, t1;
                        double[] px = { c.FaceAStart.X, c.FaceAEnd.X, c.FaceBEnd.X, c.FaceBStart.X }, py = { c.FaceAStart.Y, c.FaceAEnd.Y, c.FaceBEnd.Y, c.FaceBStart.Y };
                        if (SectionBuilder.ClipLine(a.X, a.Y, b.X, b.Y, px, py, out t0, out t1))
                            ops.Add(new SectionOpening { S0 = t0 * length, S1 = t1 * length, SillMm = info.Door ? 0 : info.SillMm, HeightMm = info.HeightMm, Door = info.Door });
                    }
                }
                tr.Commit();
            }
            walls = SectionBuilder.PairCrossings(cross, Settings.GetDouble("SectionMaxWallMm", 600));
            if (walls.Count == 0) { ed.WriteMessage("\nHCWSECTIONDRAW: the section line crosses no walls on " + WallCommands.WallLayer + " or " + ColumnCommands.LayerColumn + "."); return; }

            // Slabs run across the walls cut, with a little overhang.
            double from = Math.Max(0, walls.Min(w => w.S0)), to = Math.Min(length, walls.Max(w => w.S1));
            var rects = SectionBuilder.Build(levels, walls, ops, from, to);
            Func<double, double> X = s => flip ? length - s : s;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, CutLayer);
                Util.EnsureHcwLayer(tr, db, LayerText);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var r in rects)
                {
                    double x0 = X(r.X0), x1 = X(r.X1);
                    double lo = Math.Min(x0, x1), hi = Math.Max(x0, x1);
                    var pline = new Polyline { Layer = CutLayer, Closed = true };
                    pline.AddVertexAt(0, new Point2d(o.X + lo * perMm, o.Y + r.Y0 * perMm), 0, 0, 0);
                    pline.AddVertexAt(1, new Point2d(o.X + hi * perMm, o.Y + r.Y0 * perMm), 0, 0, 0);
                    pline.AddVertexAt(2, new Point2d(o.X + hi * perMm, o.Y + r.Y1 * perMm), 0, 0, 0);
                    pline.AddVertexAt(3, new Point2d(o.X + lo * perMm, o.Y + r.Y1 * perMm), 0, 0, 0);
                    pline.Elevation = o.Z;
                    space.AppendEntity(pline); tr.AddNewlyCreatedDBObject(pline, true);
                }
                var elev = SectionBuilder.Elevations(levels);
                double th = 120 * perMm;
                for (int i = 0; i < levels.Count; i++)
                {
                    double y = o.Y + elev[i] * perMm;
                    var txt = new DBText { Layer = LayerText, Height = th, Position = new Point3d(o.X + (Math.Min(X(from), X(to)) - 600) * perMm, y, o.Z), TextString = levels[i].Name + " FFL +" + (elev[i] / 1000).ToString("0.000", CultureInfo.InvariantCulture) };
                    space.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWSECTIONDRAW: " + walls.Count + " wall(s) and " + ops.Count + " opening(s) cut, " + levels.Count + " floor(s), drawn at 1:1 on " + CutLayer + ".");
        }

        private static void AddCross(List<double> cross, P2 a, P2 b, P2 p, P2 q, double perMm)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, ex = q.X - p.X, ey = q.Y - p.Y;
            double den = dx * ey - dy * ex;
            if (Math.Abs(den) < 1e-12) return;
            double t = ((p.X - a.X) * ey - (p.Y - a.Y) * ex) / den;
            double u = ((p.X - a.X) * dy - (p.Y - a.Y) * dx) / den;
            if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return;
            cross.Add(t * a.DistanceTo(b) / perMm);
        }
    }
}
