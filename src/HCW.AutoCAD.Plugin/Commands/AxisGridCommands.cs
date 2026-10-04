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
    /// HCWAXIS draws a column grid from bay widths (4000 4000 3*3600 …) with numbered bubbles along the bottom
    /// (1, 2, 3 …) and lettered bubbles up the left (A, B, C …, skipping I and O). Bay widths are in millimetres
    /// whatever the drawing units. Lines are on AN-GRID, bubbles on AN-SYMB, labels on AN-TEXT. The bubble is
    /// 8 mm across and its text 3.5 mm high on the sheet at the plot scale you give.
    /// </summary>
    public class AxisGridCommands
    {
        private static string _xBays = "4000 4000 4000";
        private static string _yBays = "4000 4000";
        private static double _scale = 100;
        private static bool _bothEnds = true;

        private const string LayerLine = "AN-GRID", LayerBubble = "AN-SYMB", LayerText = "AN-TEXT";

        [CommandMethod("HCWAXIS")]
        public void Axis()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var xs = AskBays(ed, "\nBay widths in mm, left to right, e.g. 4000 3*3600 <" + _xBays + ">: ", _xBays);
            if (xs == null) return;
            var ys = AskBays(ed, "\nBay widths in mm, bottom to top <" + _yBays + ">: ", _yBays);
            if (ys == null) return;

            var sr = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (sr.Status != PromptStatus.OK) return;
            _scale = sr.Value;

            var eo = new PromptKeywordOptions("\nBubbles at [Both ends/Start only] <" + (_bothEnds ? "Both" : "Start") + ">: ", "Both Start") { AllowNone = true };
            eo.Keywords.Default = _bothEnds ? "Both" : "Start";
            var er = ed.GetKeywords(eo);
            if (er.Status == PromptStatus.OK) _bothEnds = er.StringResult == "Both";
            else if (er.Status != PromptStatus.None) return;

            var bp = ed.GetPoint("\nLower-left grid intersection: ");
            if (bp.Status != PromptStatus.OK) return;

            var xPos = GridModel.Positions(xs);
            var yPos = GridModel.Positions(ys);
            double mm = Util.MmToDrawingUnits(1.0);
            double dia = 8.0 * _scale * mm, r = dia / 2.0, textH = 3.5 * _scale * mm, ext = dia;
            double toUnits = mm;
            var ucs = ed.CurrentUserCoordinateSystem;
            double x0 = bp.Value.X, y0 = bp.Value.Y, z = bp.Value.Z;
            double width = xPos[xPos.Count - 1] * toUnits, height = yPos[yPos.Count - 1] * toUnits;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, LayerLine);
                Util.EnsureHcwLayer(tr, db, LayerBubble);
                Util.EnsureHcwLayer(tr, db, LayerText);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                Action<Entity> put = ent =>
                {
                    ent.TransformBy(ucs);
                    space.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                };
                Action<double, double, string> bubble = (cx, cy, label) =>
                {
                    put(new Circle(new Point3d(cx, cy, z), Vector3d.ZAxis, r) { Layer = LayerBubble });
                    var t = new DBText
                    {
                        Height = textH, TextString = label, Layer = LayerText,
                        Position = new Point3d(cx, cy, z),
                        HorizontalMode = TextHorizontalMode.TextMid,
                        VerticalMode = TextVerticalMode.TextVerticalMid,
                    };
                    t.AlignmentPoint = new Point3d(cx, cy, z);
                    put(t);
                };

                for (int i = 0; i < xPos.Count; i++)
                {
                    double x = x0 + xPos[i] * toUnits;
                    put(new Line(new Point3d(x, y0 - ext, z), new Point3d(x, y0 + height + (_bothEnds ? ext : 0), z)) { Layer = LayerLine });
                    string label = GridModel.Number(i);
                    bubble(x, y0 - ext - r, label);
                    if (_bothEnds) bubble(x, y0 + height + ext + r, label);
                }
                for (int j = 0; j < yPos.Count; j++)
                {
                    double y = y0 + yPos[j] * toUnits;
                    put(new Line(new Point3d(x0 - ext, y, z), new Point3d(x0 + width + (_bothEnds ? ext : 0), y, z)) { Layer = LayerLine });
                    string label = GridModel.Letter(j);
                    bubble(x0 - ext - r, y, label);
                    if (_bothEnds) bubble(x0 + width + ext + r, y, label);
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWAXIS: " + xPos.Count + " lines (1-" + xPos.Count + ") by " + yPos.Count + " lines (A-" + GridModel.Letter(yPos.Count - 1)
                + "), " + (xPos[xPos.Count - 1]).ToString("0") + " x " + (yPos[yPos.Count - 1]).ToString("0") + " mm.");
        }

        private static List<double> AskBays(Editor ed, string prompt, string fallback)
        {
            while (true)
            {
                var r = ed.GetString(new PromptStringOptions(prompt) { AllowSpaces = true, DefaultValue = fallback, UseDefaultValue = true });
                if (r.Status != PromptStatus.OK) return null;
                string error;
                var list = GridModel.ParseSpacings(r.StringResult, out error);
                if (list != null)
                {
                    if (prompt.Contains("left to right")) _xBays = r.StringResult.Trim(); else _yBays = r.StringResult.Trim();
                    return list;
                }
                ed.WriteMessage("\n" + error + ".");
            }
        }
    }
}
