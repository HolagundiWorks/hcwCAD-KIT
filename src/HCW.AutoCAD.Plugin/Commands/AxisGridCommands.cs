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
            string mode = Util.AskMode("Grid", "New", "Add", "Remove");
            if (mode == null) return;
            if (mode == "Add") { AddLine(); return; }
            if (mode == "Remove") { RemoveLine(); return; }

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
    
        // ------------------------------------------------------------------ add and remove a grid line

        private class Bubble { public Circle Ring; public DBText Label; }

        /// <summary>The bubbles at the ends of a grid line: circles on the bubble layer centred on the line's extension, each with the text inside it.</summary>
        private static List<Bubble> BubblesOf(Transaction tr, BlockTableRecord space, Line line)
        {
            var found = new List<Bubble>();
            var a = new P2(line.StartPoint.X, line.StartPoint.Y); var b = new P2(line.EndPoint.X, line.EndPoint.Y);
            double len = a.DistanceTo(b);
            if (len < 1e-9) return found;
            var u = (b - a) * (1.0 / len);
            var circles = new List<Circle>(); var texts = new List<DBText>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var c = ent as Circle;
                if (c != null && string.Equals(c.Layer, LayerBubble, StringComparison.OrdinalIgnoreCase)) circles.Add(c);
                var t = ent as DBText;
                if (t != null) texts.Add(t);
            }
            foreach (var c in circles)
            {
                var d = new P2(c.Center.X, c.Center.Y) - a;
                double along = P2.Dot(d, u), across = Math.Abs(P2.Cross(u, d));
                if (across > c.Radius * 0.5 || (along >= -1e-9 && along <= len + 1e-9) || Math.Min(Math.Abs(along), Math.Abs(along - len)) > 8 * c.Radius) continue;
                var bub = new Bubble { Ring = c };
                foreach (var t in texts)
                {
                    var tp = t.HorizontalMode == TextHorizontalMode.TextLeft && t.VerticalMode == TextVerticalMode.TextBase ? t.Position : t.AlignmentPoint;
                    if (new P2(tp.X, tp.Y).DistanceTo(new P2(c.Center.X, c.Center.Y)) <= c.Radius) { bub.Label = t; break; }
                }
                found.Add(bub);
            }
            return found;
        }

        /// <summary>
        /// HCWAXISADD adds a grid line parallel to one already drawn, as long as it and with bubbles at the same ends, labelled with the
        /// next number or letter (or any label you give). Pick a point for the line, or press Enter and type a distance in millimetres
        /// (positive to the left of the line looking from its start to its end).
        /// </summary>
        [CommandMethod("HCWAXISADD")]
        public void AddLine()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var o = new PromptEntityOptions("\nSelect the grid line to copy: ");
            o.SetRejectMessage("\nSelect a grid line.");
            o.AddAllowedClass(typeof(Line), true);
            var er = ed.GetEntity(o);
            if (er.Status != PromptStatus.OK) return;

            var pp = ed.GetPoint(new PromptPointOptions("\nPick a point on the new grid line (Enter to type a distance): ") { AllowNone = true });
            Point3d? through = null; double? distance = null;
            if (pp.Status == PromptStatus.OK) through = pp.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            else if (pp.Status == PromptStatus.None)
            {
                var dr = ed.GetDouble(new PromptDoubleOptions("\nDistance in mm from that line (positive to its left, negative to its right): ") { AllowZero = false });
                if (dr.Status != PromptStatus.OK) return;
                distance = dr.Value;
            }
            else return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var line = (Line)tr.GetObject(er.ObjectId, OpenMode.ForRead);
                var a = new P2(line.StartPoint.X, line.StartPoint.Y); var b = new P2(line.EndPoint.X, line.EndPoint.Y);
                double len = a.DistanceTo(b);
                if (len < 1e-9) { ed.WriteMessage("\nHCWAXISADD: that line has no length."); return; }
                var u = (b - a) * (1.0 / len);
                var left = new P2(-u.Y, u.X);
                double off = through.HasValue ? P2.Dot(new P2(through.Value.X, through.Value.Y) - a, left) : distance.Value * Util.MmToDrawingUnits(1.0);
                if (Math.Abs(off) < Util.MmToDrawingUnits(1.0)) { ed.WriteMessage("\nHCWAXISADD: that is on the line itself."); return; }
                var move = Matrix3d.Displacement(new Vector3d(left.X * off, left.Y * off, 0));

                var bubbles = BubblesOf(tr, space, line);
                string current = bubbles.Select(x => x.Label?.TextString).FirstOrDefault(t => !string.IsNullOrEmpty(t));
                string next = current == null ? null : GridModel.NextLabel(current);
                var lr = ed.GetString(new PromptStringOptions("\nLabel for the new line" + (next != null ? " <" + next + ">" : "") + ": ")
                    { AllowSpaces = false, DefaultValue = next ?? "", UseDefaultValue = next != null });
                if (lr.Status != PromptStatus.OK) return;
                string label = lr.StringResult.Trim();

                var copy = (Line)line.Clone();
                copy.TransformBy(move);
                space.AppendEntity(copy);
                tr.AddNewlyCreatedDBObject(copy, true);
                foreach (var bub in bubbles)
                {
                    var ring = (Circle)bub.Ring.Clone();
                    ring.TransformBy(move);
                    space.AppendEntity(ring);
                    tr.AddNewlyCreatedDBObject(ring, true);
                    if (bub.Label == null) continue;
                    var text = (DBText)bub.Label.Clone();
                    text.TransformBy(move);
                    text.TextString = label;
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                }
                tr.Commit();
                ed.WriteMessage("\nHCWAXISADD: grid line " + (label.Length > 0 ? label + " " : "") + "added " + Math.Round(Math.Abs(off) / Util.MmToDrawingUnits(1.0)) + " mm "
                    + (off > 0 ? "to the left" : "to the right") + " with " + bubbles.Count + " bubble(s).");
            }
        }

        /// <summary>HCWAXISDEL erases a grid line together with its bubbles and their labels. The other lines keep their labels.</summary>
        [CommandMethod("HCWAXISDEL")]
        public void RemoveLine()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var o = new PromptEntityOptions("\nSelect the grid line to remove: ");
            o.SetRejectMessage("\nSelect a grid line.");
            o.AddAllowedClass(typeof(Line), true);
            var er = ed.GetEntity(o);
            if (er.Status != PromptStatus.OK) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var line = (Line)tr.GetObject(er.ObjectId, OpenMode.ForWrite);
                var bubbles = BubblesOf(tr, space, line);
                string label = bubbles.Select(x => x.Label?.TextString).FirstOrDefault(t => !string.IsNullOrEmpty(t));
                foreach (var bub in bubbles)
                {
                    bub.Ring.UpgradeOpen(); bub.Ring.Erase();
                    if (bub.Label != null) { bub.Label.UpgradeOpen(); bub.Label.Erase(); }
                }
                line.Erase();
                tr.Commit();
                ed.WriteMessage("\nHCWAXISDEL: grid line " + (label ?? "") + " and " + bubbles.Count + " bubble(s) removed. The other lines keep their labels.");
            }
        }
    }
}
