using System;
using System.Collections.Generic;
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
                    foreach (var p in RailLayout.PostPoints(c.Points, c.Closed, spacing))
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
    }
}
