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
    /// HCWESCALATOR draws an escalator in plan and, if you like, in side elevation, from the rise, angle, step width and landing length.
    /// Sizes are in millimetres whatever the drawing units. The plan's first end is the lower landing; the escalator runs along the
    /// direction you pick.
    /// </summary>
    public class EscalatorCommands
    {
        private static double _rise = 4000;

        [CommandMethod("HCWESCALATOR")]
        public void Draw()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var o = new EscalatorOptions
            {
                AngleDeg = Settings.GetDouble("EscalatorAngleDeg", 30),
                StepWidth = Settings.GetDouble("EscalatorStepWidthMm", 1000),
                LandingMm = Settings.GetDouble("EscalatorLandingMm", 2500),
                SideMm = Settings.GetDouble("EscalatorSideMm", 300),
                TextHeight = 125,
            };

            var r = ed.GetDouble(new PromptDoubleOptions("\nRise (floor to floor) in mm <" + _rise + ">: ") { AllowNegative = false, AllowZero = false, DefaultValue = _rise, UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return;
            _rise = o.RiseMm = r.Value;
            var a = ed.GetDouble(new PromptDoubleOptions("\nAngle in degrees (30 or 35) <" + o.AngleDeg + ">: ") { AllowNegative = false, AllowZero = false, DefaultValue = o.AngleDeg, UseDefaultValue = true });
            if (a.Status != PromptStatus.OK) return;
            o.AngleDeg = a.Value;
            var w = ed.GetDouble(new PromptDoubleOptions("\nNominal step width in mm (600, 800 or 1000) <" + o.StepWidth + ">: ") { AllowNegative = false, AllowZero = false, DefaultValue = o.StepWidth, UseDefaultValue = true });
            if (w.Status != PromptStatus.OK) return;
            o.StepWidth = w.Value;
            var l = ed.GetDouble(new PromptDoubleOptions("\nFlat landing length at each end in mm <" + o.LandingMm + ">: ") { AllowNegative = false, AllowZero = false, DefaultValue = o.LandingMm, UseDefaultValue = true });
            if (l.Status != PromptStatus.OK) return;
            o.LandingMm = l.Value;

            string problem = Escalator.Check(o);
            if (problem != null) { ed.WriteMessage("\nHCWESCALATOR: " + problem + "."); return; }

            var ko = new PromptKeywordOptions("\nAlso draw the side elevation [Yes/No] <Yes>: ", "Yes No") { AllowNone = true };
            ko.Keywords.Default = "Yes";
            var kr = ed.GetKeywords(ko);
            bool elevation = true;
            if (kr.Status == PromptStatus.OK) elevation = kr.StringResult == "Yes";
            else if (kr.Status != PromptStatus.None) return;

            var ucs = ed.CurrentUserCoordinateSystem;
            double baseAngle = Math.Atan2(ucs.CoordinateSystem3d.Xaxis.Y, ucs.CoordinateSystem3d.Xaxis.X);
            var p1 = ed.GetPoint("\nPick the corner of the lower landing end of the escalator: ");
            if (p1.Status != PromptStatus.OK) return;
            var dir = ed.GetPoint(new PromptPointOptions("\nPick a point in the direction the escalator runs up: ") { UseBasePoint = true, BasePoint = p1.Value, UseDashedLine = true });
            if (dir.Status != PromptStatus.OK) return;
            var start = p1.Value.TransformBy(ucs);
            var toward = dir.Value.TransformBy(ucs);
            double angle = Math.Atan2(toward.Y - start.Y, toward.X - start.X);
            if ((toward - start).Length < 1e-9) angle = baseAngle;

            Point3d? elevAt = null;
            if (elevation)
            {
                var ep = ed.GetPoint("\nPick the lower landing point for the side elevation: ");
                if (ep.Status != PromptStatus.OK) return;
                elevAt = ep.Value.TransformBy(ucs);
            }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var drawer = new GDrawer(tr, db, space, Roles, o.TextHeight);
                drawer.Draw(Escalator.Plan(o), start, angle);
                if (elevAt.HasValue) drawer.Draw(Escalator.Elevation(o), elevAt.Value, baseAngle);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWESCALATOR: rise " + o.RiseMm + " mm at " + o.AngleDeg + " degrees, run " + Math.Round(Escalator.Run(o)) + " mm, length " + Math.Round(Escalator.Length(o))
                + " mm, " + Math.Round(Escalator.Incline(o)) + " mm along the incline.");
        }

        private static readonly Dictionary<string, GDrawer.RoleLayer> Roles = new Dictionary<string, GDrawer.RoleLayer>
        {
            { "WALL", new GDrawer.RoleLayer { Layer = Util.Out("A-ESCALATOR"), Color = 7, Weight = LineWeight.LineWeight035 } },
            { "TREAD", new GDrawer.RoleLayer { Layer = Util.Out("A-ESCALATOR-STEPS"), Color = 8 } },
            { "ARROW", new GDrawer.RoleLayer { Layer = Util.Out("A-ESCALATOR-STEPS"), Color = 3 } },
            { "RAIL", new GDrawer.RoleLayer { Layer = Util.Out("A-ESCALATOR-RAIL"), Color = 6 } },
            { "LEVEL", new GDrawer.RoleLayer { Layer = Util.Out("A-ESCALATOR-LVL"), Color = 3 } },
            { "TEXT", new GDrawer.RoleLayer { Layer = "AN-TEXT", Color = 7 } },
        };
    }
}
