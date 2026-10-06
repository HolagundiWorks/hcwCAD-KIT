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
    /// HCWRAIL draws a handrail in plan along a line or polyline (selected, or picked point by point): the rail as a
    /// closed outline of the width you give, and square posts at every corner and end with extra posts so none are
    /// further apart than the spacing. Everything is on A-RAIL. Sizes are in millimetres whatever the drawing units.
    /// </summary>
    public class RailCommands
    {
        internal const string RailLayer = "A-RAIL";

        private static double _railMm = 50;
        private static double _spacingMm = 1200;
        private static double _postMm = 50;

        [CommandMethod("HCWRAIL")]
        public void DrawRail()
        {
            var ed = Util.Ed;
            if (!Ask(ed, "\nHandrail width in mm <" + _railMm + ">: ", ref _railMm, false)) return;
            if (!Ask(ed, "\nLargest distance between posts in mm <" + _spacingMm + ">: ", ref _spacingMm, false)) return;
            if (!Ask(ed, "\nPost size in mm, 0 for no posts <" + _postMm + ">: ", ref _postMm, true)) return;

            CentreLines.Run(ed, "\nSelect the lines or polylines the handrail follows (Enter to pick points): ",
                "\nStart of handrail (Enter to finish): ", chains => Create(ed, chains));
        }

        private static bool Ask(Editor ed, string prompt, ref double value, bool allowZero)
        {
            var r = ed.GetDouble(new PromptDoubleOptions(prompt)
                { AllowNegative = false, AllowZero = allowZero, DefaultValue = value, UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return false;
            value = r.Value;
            return true;
        }

        private static void Create(Editor ed, List<CentreLines.Chain> chains)
        {
            var db = Util.Db;
            double mm = Util.MmToDrawingUnits(1.0);
            double rail = _railMm * mm, spacing = _spacingMm * mm, post = _postMm * mm;
            int rails = 0, posts = 0, skipped = 0;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, RailLayer);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var c in chains)
                {
                    var loops = WallGeometry.Outline(c.Points, c.Closed, rail, WallJustify.Centre);
                    if (loops.Count == 0) { skipped++; continue; }
                    foreach (var loop in loops)
                    {
                        var pl = new Polyline();
                        for (int i = 0; i < loop.Count; i++) pl.AddVertexAt(i, new Point2d(loop[i].X, loop[i].Y), 0, 0, 0);
                        pl.Closed = true;
                        pl.Elevation = c.Z;
                        pl.Layer = RailLayer;
                        space.AppendEntity(pl);
                        tr.AddNewlyCreatedDBObject(pl, true);
                        rails++;
                    }
                    if (post <= 0) continue;
                    double half = post / 2.0;
                    foreach (var p in RailLayout.PostPoints(c.Points, c.Closed, spacing, c.Smooth))
                    {
                        var sq = new Polyline();
                        sq.AddVertexAt(0, new Point2d(p.X - half, p.Y - half), 0, 0, 0);
                        sq.AddVertexAt(1, new Point2d(p.X + half, p.Y - half), 0, 0, 0);
                        sq.AddVertexAt(2, new Point2d(p.X + half, p.Y + half), 0, 0, 0);
                        sq.AddVertexAt(3, new Point2d(p.X - half, p.Y + half), 0, 0, 0);
                        sq.Closed = true;
                        sq.Elevation = c.Z;
                        sq.Layer = RailLayer;
                        space.AppendEntity(sq);
                        tr.AddNewlyCreatedDBObject(sq, true);
                        posts++;
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWRAIL: " + rails + " rail outline(s) " + _railMm + " mm wide and " + posts + " post(s) on " + RailLayer
                + (skipped > 0 ? ". " + skipped + " line(s) too short." : "."));
        }
    
        private static double _heightMm = 900, _balusterMm = 12, _gapMm = 100, _slopeDeg;

        /// <summary>
        /// HCWBALUSTRADE draws a balustrade in elevation along the same kind of line or polyline as HCWRAIL: the handrail, posts where the plan
        /// has them (every corner and end, and at the largest spacing between), and balusters between the posts so no gap is wider than the
        /// limit. A slope in degrees makes the rail follow a stair flight. Each line or polyline is drawn as one run, one below the other.
        /// </summary>
        [CommandMethod("HCWBALUSTRADE")]
        public void DrawBalustrade()
        {
            var ed = Util.Ed;
            if (!Ask(ed, "\nHandrail height in mm <" + _heightMm + ">: ", ref _heightMm, false)) return;
            if (!Ask(ed, "\nLargest distance between posts in mm <" + _spacingMm + ">: ", ref _spacingMm, false)) return;
            if (!Ask(ed, "\nPost size in mm <" + _postMm + ">: ", ref _postMm, false)) return;
            if (!Ask(ed, "\nBaluster size in mm <" + _balusterMm + ">: ", ref _balusterMm, false)) return;
            if (!Ask(ed, "\nLargest clear gap between balusters in mm <" + _gapMm + ">: ", ref _gapMm, false)) return;
            if (!Ask(ed, "\nSlope of the flight in degrees, 0 for a level run <" + _slopeDeg + ">: ", ref _slopeDeg, true)) return;

            CentreLines.Run(ed, "\nSelect the lines or polylines the balustrade follows (Enter to pick points): ",
                "\nStart of balustrade (Enter to finish): ", chains => Elevation(ed, chains));
        }

        private static void Elevation(Editor ed, List<CentreLines.Chain> chains)
        {
            var db = Util.Db;
            double mm = Util.MmToDrawingUnits(1.0);
            var opt = new BalustradeOptions
            {
                HandrailHeight = _heightMm, PostSize = _postMm, BalusterSize = _balusterMm, MaxClearGap = _gapMm, SlopeDeg = _slopeDeg,
            };
            var runs = new List<BalustradeResult>();
            foreach (var c in chains)
            {
                // Plan lengths in drawing units become millimetres; corners stay posts and curves are measured along the curve.
                var pts = new List<P2>(); var flags = new List<bool>();
                for (int i = 0; i < c.Points.Count; i++)
                {
                    if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(c.Points[i]) < 1e-9) continue;
                    pts.Add(c.Points[i]); flags.Add(i < c.Smooth.Count && c.Smooth[i]);
                }
                if (c.Closed && pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-9) { pts.RemoveAt(pts.Count - 1); flags.RemoveAt(flags.Count - 1); }
                if (pts.Count < 2) continue;
                double length = 0;
                for (int i = 1; i < pts.Count; i++) length += pts[i - 1].DistanceTo(pts[i]);
                if (c.Closed) length += pts[pts.Count - 1].DistanceTo(pts[0]);
                var dist = RailLayout.PostDistances(pts, c.Closed, _spacingMm * mm, flags);
                if (c.Closed) dist.Add(length);            // a closed run is drawn opened out, with a post at both ends
                string error;
                var run = Balustrade.Build(length / mm, dist.Select(d => d / mm).ToList(), opt, out error);
                if (run == null) { ed.WriteMessage("\nHCWBALUSTRADE: " + error + "."); continue; }
                runs.Add(run);
            }
            if (runs.Count == 0) return;

            var pr = ed.GetPoint("\nPick the point for the start of the balustrade at floor level: ");
            if (pr.Status != PromptStatus.OK) return;
            var ucs = ed.CurrentUserCoordinateSystem;
            var at = pr.Value.TransformBy(ucs);
            double angle = Math.Atan2(ucs.CoordinateSystem3d.Xaxis.Y, ucs.CoordinateSystem3d.Xaxis.X);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var drawer = new GDrawer(tr, db, space, BalustradeRoles, opt.TextHeight);
                double down = 0;
                foreach (var run in runs)
                {
                    var box = run.Drawing.Extents();
                    double step = (_heightMm + 600 + 2 * opt.TextHeight) * mm;
                    var origin = new Point3d(at.X - Math.Sin(angle) * -down, at.Y + Math.Cos(angle) * -down, at.Z);
                    drawer.Draw(run.Drawing, origin, angle);
                    down += step + Math.Max(0, -box.MinY) * mm;
                }
                tr.Commit();
            }
            int balusters = runs.Sum(r => r.Balusters), posts = runs.Sum(r => r.Posts);
            ed.WriteMessage("\nHCWBALUSTRADE: " + runs.Count + " run(s), " + posts + " post(s), " + balusters + " baluster(s), largest gap "
                + Math.Round(runs.Max(r => r.ClearGap)) + " mm.");
        }

        private static readonly Dictionary<string, GDrawer.RoleLayer> BalustradeRoles = new Dictionary<string, GDrawer.RoleLayer>
        {
            { "RAIL", new GDrawer.RoleLayer { Layer = "A-RAIL-ELEV", Color = 6, Weight = LineWeight.LineWeight025 } },
            { "POST", new GDrawer.RoleLayer { Layer = "A-RAIL-ELEV", Color = 6 } },
            { "BALUSTER", new GDrawer.RoleLayer { Layer = "A-RAIL-BAL", Color = 8 } },
            { "LEVEL", new GDrawer.RoleLayer { Layer = "A-RAIL-LVL", Color = 3 } },
            { "TEXT", new GDrawer.RoleLayer { Layer = "AN-TEXT", Color = 7 } },
        };
    }
}
