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
        private static string LayerWall => Util.Out("A-WALL");
        private static string LayerDoor => Util.Out("A-DOOR", "BP-LIFT");          // the car and its doors belong to the lift
        private const string LayerLift = "BP-LIFT", LayerText = "AN-TEXT";
        private static string LayerMachine => Util.Out("A-LIFT-MR");
        private const double TextHeightMm = 250, FrontGapMm = 30;

        private static string _clear = "1800x2000";
        private static double _wallMm = 230;
        private static string _car = "1100x1400";
        private static double _doorMm = 800;
        private static string _side = "Bottom";
        private static bool _machine;

        [CommandMethod("HCWLIFT")]
        public void DrawLift()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            string drawing = Util.AskMode("Lift drawing", "Plan", "Section");
            if (drawing == null) return;
            if (drawing == "Section") { DrawSection(); return; }

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

            var mro = new PromptKeywordOptions("\nDraw the machine room outline above the shaft [Yes/No] <" + (_machine ? "Yes" : "No") + ">: ", "Yes No") { AllowNone = true };
            mro.Keywords.Default = _machine ? "Yes" : "No";
            var mr = ed.GetKeywords(mro);
            if (mr.Status == PromptStatus.OK) _machine = mr.StringResult == "Yes";
            else if (mr.Status != PromptStatus.None) return;

            var ucs = ed.CurrentUserCoordinateSystem;
            var xAxis = ucs.CoordinateSystem3d.Xaxis;
            double angle = turn + Math.Atan2(xAxis.Y, xAxis.X);

            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the centre of the lift shaft (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var centre = pr.Value.TransformBy(ucs);
                Place(db, layout, centre, angle, _clear.Replace(" ", ""), _machine);
                ed.WriteMessage("\nHCWLIFT: lift shaft " + cw + " x " + cd + " mm, door on the " + _side.ToLowerInvariant() + ".");
            }
        }

        private static void Place(Database db, LiftLayout L, Point3d centre, double angle, string sizeLabel, bool machineRoom)
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
                if (machineRoom)
                {
                    // The machine room sits on the roof over the shaft: wider than the shaft by a working margin all round, shown dashed.
                    double m = Settings.GetDouble("LiftMachineMarginMm", 1000);
                    double hw = L.ClearW / 2 + L.Wall + m, hd = L.ClearD / 2 + L.Wall + m;
                    Util.EnsureLayer(tr, db, LayerMachine, 8, "DASHED");
                    loop(new List<P2> { new P2(-hw, -hd), new P2(hw, -hd), new P2(hw, hd), new P2(-hw, hd) }, LayerMachine);
                }
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

        private static string _levelsFor;
        private static int _floors = 4;
        private static double _floorHeightMm = 3000, _pitMm = 1400, _overheadMm = 4200;
        private static bool _machineSection = true;

        /// <summary>
        /// HCWLIFTSECTION draws a section through a lift shaft from the same sizes as the plan: the pit, the walls and door openings at each landing,
        /// the car, the overhead and an optional machine room, with the pit, overhead and travel dimensioned.
        /// </summary>
        [CommandMethod("HCWLIFTSECTION")]
        public void DrawSection()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            double cw, cd, carW, carD;
            if (!AskSize(ed, "\nClear size of the shaft in mm, width x depth <" + _clear + ">: ", ref _clear, out cw, out cd)) return;
            if (!AskSize(ed, "\nCar size in mm, width x depth <" + _car + ">: ", ref _car, out carW, out carD)) return;
            string error;
            var layout = LiftLayout.Build(cw, cd, _wallMm, carW, carD, _doorMm, FrontGapMm, out error);
            if (layout == null) { ed.WriteMessage("\nHCWLIFTSECTION: " + error + "."); return; }

            if (_levelsFor != Util.Doc.Name)
            {
                _levelsFor = Util.Doc.Name;
                var lv = LevelStore.Load();
                if (lv.Count > 0) { _floorHeightMm = Math.Round(lv[0].FflMm); _floors = Math.Max(2, lv.Count); ed.WriteMessage("\nFloors and floor-to-floor height taken from the levels in the drawing."); }
            }
            var f = ed.GetInteger(new PromptIntegerOptions("\nNumber of floors served <" + _floors + ">: ") { AllowNegative = false, AllowZero = false, DefaultValue = _floors, UseDefaultValue = true });
            if (f.Status != PromptStatus.OK) return;
            _floors = f.Value;
            if (!AskNumber(ed, "\nFloor-to-floor height in mm <" + _floorHeightMm + ">: ", ref _floorHeightMm)) return;
            if (!AskNumber(ed, "\nPit depth in mm <" + _pitMm + ">: ", ref _pitMm)) return;
            if (!AskNumber(ed, "\nOverhead (top landing to underside of the slab) in mm <" + _overheadMm + ">: ", ref _overheadMm)) return;
            var mro = new PromptKeywordOptions("\nDraw the machine room [Yes/No] <" + (_machineSection ? "Yes" : "No") + ">: ", "Yes No") { AllowNone = true };
            mro.Keywords.Default = _machineSection ? "Yes" : "No";
            var mr = ed.GetKeywords(mro);
            if (mr.Status == PromptStatus.OK) _machineSection = mr.StringResult == "Yes";
            else if (mr.Status != PromptStatus.None) return;

            var o = new LiftSectionOptions
            {
                Floors = _floors, FloorHeight = _floorHeightMm, PitDepth = _pitMm, Overhead = _overheadMm, MachineRoom = _machineSection, Wall = _wallMm,
                TextHeight = TextHeightMm / 2,
            };
            var drawing = LiftSection.Build(layout, o, out error);
            if (drawing == null) { ed.WriteMessage("\nHCWLIFTSECTION: " + error + "."); return; }

            var ucs = ed.CurrentUserCoordinateSystem;
            double angle = Math.Atan2(ucs.CoordinateSystem3d.Xaxis.Y, ucs.CoordinateSystem3d.Xaxis.X);
            var pr = ed.GetPoint("\nPick the inside face of the front wall at the lowest landing: ");
            if (pr.Status != PromptStatus.OK) return;
            var at = pr.Value.TransformBy(ucs);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                new GDrawer(tr, db, space, SectionRoles, o.TextHeight).Draw(drawing, at, angle);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWLIFTSECTION: " + _floors + " floors, travel " + (_floors - 1) * _floorHeightMm + " mm, pit " + _pitMm + ", overhead " + _overheadMm + ".");
        }

        internal static readonly Dictionary<string, GDrawer.RoleLayer> SectionRoles = new Dictionary<string, GDrawer.RoleLayer>
        {
            { "WALL", new GDrawer.RoleLayer { Layer = Util.Out("A-LIFT-SEC"), Color = 7, Weight = LineWeight.LineWeight035 } },
            { "DOOR", new GDrawer.RoleLayer { Layer = LayerDoor, Color = 4 } },
            { "LEVEL", new GDrawer.RoleLayer { Layer = Util.Out("A-LIFT-LVL"), Color = 3 } },
            { "TEXT", new GDrawer.RoleLayer { Layer = LayerText, Color = 7 } },
            { "HATCH", new GDrawer.RoleLayer { Layer = Util.Out("A-LIFT-HATCH"), Color = 8 } },
        };

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
