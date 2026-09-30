using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// SHEETSET: makes a run of sheets from one template layout. Each new layout is a copy of the
    /// template (title block, notes, viewport), numbered with a prefix, its viewport set to a scale
    /// and centred on a window you pick in model space (or on the drawing extents), and the title
    /// block's drawing number and title filled in.
    /// </summary>
    public class SheetSetCommands
    {
        private static string _prefix = "A-";
        private static int _start = 1;
        private static int _count = 1;
        private static double _scale = 100;

        private class SheetPlan
        {
            public string Name;
            public string Title = "";
            public bool HasWindow;
            public Point2d Min, Max;
        }

        [CommandMethod("SHEETSET")]
        public void SheetSet()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var lm = LayoutManager.Current;

            var paper = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                var found = new List<KeyValuePair<int, string>>();
                foreach (DBDictionaryEntry entry in dict)
                {
                    var lay = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (!lay.ModelType) found.Add(new KeyValuePair<int, string>(lay.TabOrder, lay.LayoutName));
                }
                paper = found.OrderBy(f => f.Key).Select(f => f.Value).ToList();
                tr.Commit();
            }
            if (paper.Count == 0)
            {
                ed.WriteMessage("\nSHEETSET: add a template layout first (title block and a viewport).");
                return;
            }

            string original = lm.CurrentLayout;
            bool inModel = string.Equals(original, "Model", StringComparison.OrdinalIgnoreCase);
            string template = inModel ? paper[0] : original;

            var tpl = new PromptStringOptions("\nTemplate layout <" + template + ">: ") { AllowSpaces = true, DefaultValue = template, UseDefaultValue = true };
            var tplRes = ed.GetString(tpl);
            if (tplRes.Status != PromptStatus.OK) return;
            string chosen = paper.FirstOrDefault(n => string.Equals(n, tplRes.StringResult.Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosen == null)
            {
                ed.WriteMessage("\nSHEETSET: there is no layout called \"" + tplRes.StringResult + "\".");
                return;
            }
            template = chosen;

            var cnt = ed.GetInteger(new PromptIntegerOptions("\nNumber of sheets <" + _count + ">: ")
                { AllowNegative = false, AllowZero = false, LowerLimit = 1, UpperLimit = 200, DefaultValue = _count, UseDefaultValue = true });
            if (cnt.Status != PromptStatus.OK) return;
            _count = cnt.Value;

            var pre = ed.GetString(new PromptStringOptions("\nSheet name prefix <" + _prefix + ">: ")
                { AllowSpaces = true, DefaultValue = _prefix, UseDefaultValue = true });
            if (pre.Status != PromptStatus.OK) return;
            _prefix = pre.StringResult;

            var start = ed.GetInteger(new PromptIntegerOptions("\nFirst sheet number <" + _start + ">: ")
                { AllowNegative = false, DefaultValue = _start, UseDefaultValue = true });
            if (start.Status != PromptStatus.OK) return;
            _start = start.Value;

            var scale = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (scale.Status != PromptStatus.OK) return;
            _scale = scale.Value;

            var existing = new HashSet<string>(paper, StringComparer.OrdinalIgnoreCase);
            var plans = new List<SheetPlan>();
            for (int i = 0; i < _count; i++)
            {
                var plan = new SheetPlan { Name = _prefix + (_start + i).ToString("00") };
                if (existing.Contains(plan.Name))
                {
                    ed.WriteMessage("\nSHEETSET: a layout called " + plan.Name + " already exists. Nothing was created.");
                    return;
                }
                existing.Add(plan.Name);

                if (inModel)
                {
                    var c1 = ed.GetPoint("\nSheet " + plan.Name + ": first corner of the model window (Enter = whole drawing): ");
                    if (c1.Status == PromptStatus.OK)
                    {
                        var c2 = ed.GetCorner(new PromptCornerOptions("\nOpposite corner: ", c1.Value));
                        if (c2.Status != PromptStatus.OK) return;
                        Matrix3d ucs = ed.CurrentUserCoordinateSystem;
                        var a = c1.Value.TransformBy(ucs);
                        var b = c2.Value.TransformBy(ucs);
                        plan.HasWindow = true;
                        plan.Min = new Point2d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
                        plan.Max = new Point2d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
                    }
                    else if (c1.Status != PromptStatus.None) return;
                }
                var title = ed.GetString(new PromptStringOptions("\nSheet " + plan.Name + " title (Enter to leave blank): ") { AllowSpaces = true });
                if (title.Status == PromptStatus.OK) plan.Title = title.StringResult.Trim();
                else if (title.Status != PromptStatus.None) return;
                plans.Add(plan);
            }

            int made = 0;
            using (Util.Doc.LockDocument())
            {
                foreach (var plan in plans)
                {
                    lm.CloneLayout(template, plan.Name, lm.LayoutCount);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        Configure(tr, db, lm, plan);
                        tr.Commit();
                    }
                    made++;
                }
            }
            try { lm.CurrentLayout = original; } catch { }
            ed.WriteMessage("\nSHEETSET: " + made + " layout(s) created from " + template + " at 1:" + _scale + ".");
        }

        /// <summary>Sets the scale and centre of the sheet's viewport and fills the title block's number and title.</summary>
        private static void Configure(Transaction tr, Database db, LayoutManager lm, SheetPlan plan)
        {
            var layout = (Layout)tr.GetObject(lm.GetLayoutId(plan.Name), OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

            Viewport view = null;
            double bestArea = 0;
            var refs = new List<BlockReference>();
            foreach (ObjectId id in btr)
            {
                var vp = tr.GetObject(id, OpenMode.ForRead) as Viewport;
                if (vp != null)
                {
                    // Viewport 1 is the sheet itself, not a window onto the model.
                    if (vp.Number != 1 && vp.Width * vp.Height > bestArea) { view = vp; bestArea = vp.Width * vp.Height; }
                    continue;
                }
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null && br.AttributeCollection.Count > 0) refs.Add(br);
            }

            if (view != null)
            {
                Point2d min, max;
                bool have = plan.HasWindow;
                min = plan.Min; max = plan.Max;
                if (!have && db.Extmin.X <= db.Extmax.X && db.Extmin.Y <= db.Extmax.Y)
                {
                    min = new Point2d(db.Extmin.X, db.Extmin.Y);
                    max = new Point2d(db.Extmax.X, db.Extmax.Y);
                    have = true;
                }
                view.UpgradeOpen();
                view.Locked = false;
                if (have)
                {
                    view.ViewTarget = new Point3d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, 0);
                    view.ViewCenter = Point2d.Origin;
                }
                // Paper millimetres per drawing unit: 1:100 with a metre drawing is 10 paper mm per unit.
                view.CustomScale = 1.0 / (_scale * Util.MmToDrawingUnits(1.0));
                view.Locked = true;
            }

            foreach (var br in refs)
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                    string tag = (att.Tag ?? "").ToUpperInvariant();
                    if (tag == "DRAWING_NO")
                    {
                        att.UpgradeOpen();
                        att.TextString = plan.Name;
                    }
                    else if (tag == "DRAWING_TITLE" && plan.Title.Length > 0)
                    {
                        att.UpgradeOpen();
                        att.TextString = plan.Title;
                    }
                }
            }
        }
    }
}
