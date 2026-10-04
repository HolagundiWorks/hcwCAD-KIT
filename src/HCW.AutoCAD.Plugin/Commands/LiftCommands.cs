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
    /// HCWLIFT draws a lift shaft in plan from its sizes: the wall as one outline with the landing door opening cut
    /// through it (A-WALL), the clear shaft as a closed polyline on BP-LIFT (select it as a deduction in
    /// HCWAREASTMT), the car and the landing and car doors (A-DOOR), and a label. Sizes are in millimetres whatever
    /// the drawing units. The sizes it starts with are typical for a small passenger lift; use your lift maker's.
    /// </summary>
    public class LiftCommands
    {
        private const string LayerWall = "A-WALL", LayerDoor = "A-DOOR", LayerLift = "BP-LIFT", LayerText = "AN-TEXT";
        private const double TextHeightMm = 250, FrontGapMm = 30;

        private static string _clear = "1800x2000";
        private static double _wallMm = 230;
        private static string _car = "1100x1400";
        private static double _doorMm = 800;
        private static string _side = "Bottom";

        [CommandMethod("HCWLIFT")]
        public void DrawLift()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            double cw, cd, carW, carD;
            if (!AskSize(ed, "\nClear size of the shaft in mm, width x depth <" + _clear + ">: ", ref _clear, out cw, out cd)) return;
            if (!AskNumber(ed, "\nShaft wall thickness in mm <" + _wallMm + ">: ", ref _wallMm)) return;
            if (!AskSize(ed, "\nCar size in mm, width x depth <" + _car + ">: ", ref _car, out carW, out carD)) return;
            if (!AskNumber(ed, "\nDoor width in mm <" + _doorMm + ">: ", ref _doorMm)) return;

            string error;
            var layout = LiftLayout.Build(cw, cd, _wallMm, carW, carD, _doorMm, FrontGapMm, out error);
            if (layout == null) { ed.WriteMessage("\nHCWLIFT: " + error + "."); return; }

            var so = new PromptKeywordOptions("\nDoor on which side [Bottom/Right/Top/Left] <" + _side + ">: ", "Bottom Right Top Left") { AllowNone = true };
            so.Keywords.Default = _side;
            var sr = ed.GetKeywords(so);
            if (sr.Status == PromptStatus.OK) _side = sr.StringResult;
            else if (sr.Status != PromptStatus.None) return;
            double turn = _side == "Right" ? Math.PI / 2 : _side == "Top" ? Math.PI : _side == "Left" ? 3 * Math.PI / 2 : 0;

            var ucs = ed.CurrentUserCoordinateSystem;
            var xAxis = ucs.CoordinateSystem3d.Xaxis;
            double angle = turn + Math.Atan2(xAxis.Y, xAxis.X);

            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the centre of the lift shaft (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var centre = pr.Value.TransformBy(ucs);
                Place(db, layout, centre, angle, _clear.Replace(" ", ""));
                ed.WriteMessage("\nHCWLIFT: lift shaft " + cw + " x " + cd + " mm, door on the " + _side.ToLowerInvariant() + ".");
            }
        }

        private static void Place(Database db, LiftLayout L, Point3d centre, double angle, string sizeLabel)
        {
            double mm = Util.MmToDrawingUnits(1.0);
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            Func<P2, Point2d> at = p => new Point2d(centre.X + (p.X * cos - p.Y * sin) * mm, centre.Y + (p.X * sin + p.Y * cos) * mm);
            Func<P2, Point3d> at3 = p => { var q = at(p); return new Point3d(q.X, q.Y, centre.Z); };

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LayerWall);
                Util.EnsureHcwLayer(tr, db, LayerDoor);
                Util.EnsureHcwLayer(tr, db, LayerText);
                EnsureLiftLayer(tr, db);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Action<IList<P2>, string> loop = (pts, layer) =>
                {
                    var pl = new Polyline();
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, at(pts[i]), 0, 0, 0);
                    pl.Closed = true;
                    pl.Elevation = centre.Z;
                    pl.Layer = layer;
                    space.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                };
                Action<P2, P2, string> line = (a, b, layer) =>
                {
                    var ln = new Line(at3(a), at3(b)) { Layer = layer };
                    space.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                };
                Action<string, P2, double> label = (text, p, h) =>
                {
                    var t = new DBText
                    {
                        Height = h, TextString = text, Layer = LayerText, Position = at3(p),
                        HorizontalMode = TextHorizontalMode.TextMid, VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    t.AlignmentPoint = at3(p);
                    space.AppendEntity(t);
                    tr.AddNewlyCreatedDBObject(t, true);
                };

                loop(L.WallRing, LayerWall);
                loop(L.Clear, LayerLift);
                loop(L.Car, LayerDoor);
                line(L.LandingDoorA, L.LandingDoorB, LayerDoor);
                line(L.CarDoorA, L.CarDoorB, LayerDoor);

                double h = TextHeightMm * mm;
                double carMidY = (L.Car[0].Y + L.Car[2].Y) / 2;
                label("LIFT", new P2(0, carMidY + 0.8 * TextHeightMm), h);
                label(sizeLabel, new P2(0, carMidY - 0.8 * TextHeightMm), h * 0.8);
                tr.Commit();
            }
        }

        /// <summary>BP-LIFT is a building permit layer; make it from the table if the drawing does not have it yet.</summary>
        private static void EnsureLiftLayer(Transaction tr, Database db)
        {
            foreach (var l in LayerData.Bplt)
                if (string.Equals(l.Name, LayerLift, StringComparison.OrdinalIgnoreCase))
                {
                    Util.EnsureLayer(tr, db, l.Name, (short)l.Aci, l.Linetype, Util.MmToLineWeight(l.LwHundredthsMm / 100.0));
                    return;
                }
            Util.EnsureLayer(tr, db, LayerLift, 141);
        }

        private static bool AskNumber(Editor ed, string prompt, ref double value)
        {
            var r = ed.GetDouble(new PromptDoubleOptions(prompt)
                { AllowNegative = false, AllowZero = false, DefaultValue = value, UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return false;
            value = r.Value;
            return true;
        }

        private static bool AskSize(Editor ed, string prompt, ref string text, out double w, out double d)
        {
            w = d = 0;
            while (true)
            {
                var r = ed.GetString(new PromptStringOptions(prompt) { AllowSpaces = true, DefaultValue = text, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return false;
                string error;
                if (LiftLayout.ParseSize(r.StringResult, out w, out d, out error)) { text = r.StringResult.Trim(); return true; }
                ed.WriteMessage("\n" + error + ".");
            }
        }
    }
}
