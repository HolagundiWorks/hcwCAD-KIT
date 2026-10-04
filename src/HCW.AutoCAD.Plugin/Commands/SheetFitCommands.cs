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
    /// SHEETFIT: readies the title plate on a layout and fits the selected drawing into the
    /// paper-space area above it. The plate (the A3 title block) is scaled to the layout's paper
    /// size; the viewport is sized to the free area and set to a scale that shows the whole selection.
    /// </summary>
    public class SheetFitCommands
    {
        // The A3 title block is drawn 420 x 297 mm. Its free drawing area sits inside the border and above the data panel.
        private const double PlateW = 420, PlateH = 297;
        private const double AreaX1 = 8, AreaY1 = 125, AreaX2 = 412, AreaY2 = 289;
        private const string ViewportLayer = "AN-REF";

        [CommandMethod("SHEETFIT")]
        public void Run()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var lm = LayoutManager.Current;

            // 1. The layout: the one you are on, or one you name when started from the Model tab.
            var paper = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in dict)
                {
                    var lay = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (!lay.ModelType) paper.Add(lay.LayoutName);
                }
                tr.Commit();
            }
            if (paper.Count == 0)
            {
                ed.WriteMessage("\nSHEETFIT: this drawing has no layout. Add one first.");
                return;
            }

            string original = lm.CurrentLayout;
            bool inModel = string.Equals(original, "Model", StringComparison.OrdinalIgnoreCase);
            string layoutName = original;
            if (inModel)
            {
                string first = paper[0];
                var res = ed.GetString(new PromptStringOptions("\nLayout to fill <" + first + ">: ")
                    { AllowSpaces = true, DefaultValue = first, UseDefaultValue = true });
                if (res.Status != PromptStatus.OK) return;
                layoutName = paper.FirstOrDefault(n => string.Equals(n, res.StringResult.Trim(), StringComparison.OrdinalIgnoreCase));
                if (layoutName == null)
                {
                    ed.WriteMessage("\nSHEETFIT: there is no layout called \"" + res.StringResult + "\".");
                    return;
                }
            }

            // 2. The drawing to fit: select in model space, or Enter for everything.
            if (!inModel) lm.CurrentLayout = "Model";
            var psr = ed.GetSelection(new PromptSelectionOptions
                { MessageForAdding = "\nSelect the drawing to fit (Enter = whole drawing): " });
            Extents3d? box;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = new List<ObjectId>();
                if (psr.Status == PromptStatus.OK) ids.AddRange(psr.Value.GetObjectIds());
                else if (psr.Status == PromptStatus.None)
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms) ids.Add(id);
                }
                else { lm.CurrentLayout = original; return; }
                box = Union(tr, ids);
                tr.Commit();
            }
            if (!box.HasValue)
            {
                lm.CurrentLayout = original;
                ed.WriteMessage("\nSHEETFIT: nothing with a size was found to fit.");
                return;
            }

            var kw = new PromptKeywordOptions("\nScale [Standard/Exact] <Standard>: ", "Standard Exact");
            kw.Keywords.Default = "Standard";
            kw.AllowNone = true;
            var kres = ed.GetKeywords(kw);
            if (kres.Status != PromptStatus.OK && kres.Status != PromptStatus.None) { lm.CurrentLayout = original; return; }
            bool exact = kres.Status == PromptStatus.OK && kres.StringResult == "Exact";

            // 3. Title plate and viewport on the layout.
            lm.CurrentLayout = layoutName;
            string report;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                report = Fit(tr, db, lm, layoutName, box.Value, exact);
                tr.Commit();
            }
            ed.WriteMessage("\nSHEETFIT: " + report);
        }

        private static string Fit(Transaction tr, Database db, LayoutManager lm, string layoutName, Extents3d model, bool exact)
        {
            var layout = (Layout)tr.GetObject(lm.GetLayoutId(layoutName), OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);
            double unitsPerMm = Util.MmToDrawingUnits(1.0);
            double paperPerMm = layout.PlotPaperUnits == PlotPaperUnit.Inches ? 1.0 / 25.4 : 1.0;

            // The plate that is already on the sheet, if any.
            BlockReference plate = null;
            Viewport view = null;
            double bestArea = 0;
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                var vp = ent as Viewport;
                if (vp != null)
                {
                    if (vp.Number != 1 && vp.Width * vp.Height > bestArea) { view = vp; bestArea = vp.Width * vp.Height; }
                    continue;
                }
                var br = ent as BlockReference;
                if (plate == null && br != null && TitleBlockCommands.IsKind(br, "TITLE")) plate = br;
            }

            string note;
            if (plate == null)
            {
                // Scale the plate to the paper size and put it on the paper corner.
                var size = layout.PlotPaperSize;
                bool turned = layout.PlotRotation == PlotRotation.Degrees090 || layout.PlotRotation == PlotRotation.Degrees270;
                double paperW = (turned ? size.Y : size.X) / paperPerMm, paperH = (turned ? size.X : size.Y) / paperPerMm;
                double k = Math.Min(paperW / PlateW, paperH / PlateH);
                var margins = layout.PlotPaperMargins;
                var origin = layout.PlotRotation == PlotRotation.Degrees000
                    ? new Point3d(-margins.MinPoint.X, -margins.MinPoint.Y, 0) : Point3d.Origin;

                TitleBlockCommands.PrepareLayers(tr, db);
                TitleBlockCommands.EnsureRegApp(tr, db);
                ObjectId defId = TitleBlockCommands.EnsureTitleDefinition(tr, db, unitsPerMm);
                plate = new BlockReference(origin, defId) { Layer = TitleBlockCommands.LayerTitle };
                plate.ScaleFactors = new Scale3d(k * paperPerMm / unitsPerMm);
                btr.AppendEntity(plate);
                tr.AddNewlyCreatedDBObject(plate, true);
                TitleBlockCommands.AddAttributes(tr, plate);
                TitleBlockCommands.Tag(plate, "TITLE");
                note = "title plate placed (A3 plate at " + (k * 100).ToString("0") + "% for a " + paperW.ToString("0") + " x " + paperH.ToString("0") + " mm sheet), ";
            }
            else note = "existing title plate used, ";

            // Paper units per plate millimetre, from the plate's own scale.
            double ppm = plate.ScaleFactors.X * unitsPerMm;
            Point3d p0 = plate.Position;
            double ax1 = p0.X + AreaX1 * ppm, ay1 = p0.Y + AreaY1 * ppm, ax2 = p0.X + AreaX2 * ppm, ay2 = p0.Y + AreaY2 * ppm;
            double areaW = ax2 - ax1, areaH = ay2 - ay1;

            double mw = model.MaxPoint.X - model.MinPoint.X, mh = model.MaxPoint.Y - model.MinPoint.Y;
            double scale = SheetFit.ScaleToFit(areaW, areaH, mw, mh);
            double ratio = SheetFit.Ratio(scale, paperPerMm, unitsPerMm);
            if (!exact)
            {
                ratio = SheetFit.NextStandard(ratio);
                scale = SheetFit.ViewportScale(ratio, paperPerMm, unitsPerMm);
            }

            // The window shows the selection at that scale, centred in the area; the frame is the whole area.
            Util.EnsureLayer(tr, db, ViewportLayer, 250, "Continuous", LineWeight.LineWeight009, false);
            if (view == null)
            {
                view = new Viewport();
                view.SetDatabaseDefaults();
                btr.AppendEntity(view);
                tr.AddNewlyCreatedDBObject(view, true);
                view.On = true;
                note += "viewport added, ";
            }
            else
            {
                view.UpgradeOpen();
                view.Locked = false;
                note += "viewport reused, ";
            }
            view.Layer = ViewportLayer;
            view.CenterPoint = new Point3d((ax1 + ax2) / 2.0, (ay1 + ay2) / 2.0, 0);
            view.Width = areaW;
            view.Height = areaH;
            view.ViewDirection = Vector3d.ZAxis;
            view.TwistAngle = 0;
            view.ViewTarget = new Point3d((model.MinPoint.X + model.MaxPoint.X) / 2.0, (model.MinPoint.Y + model.MaxPoint.Y) / 2.0, 0);
            view.ViewCenter = Point2d.Origin;
            view.CustomScale = scale;
            view.Locked = true;
            return note + "scale 1:" + ratio.ToString("0.##") + (exact ? " (exact fit)." : ".");
        }

        private static Extents3d? Union(Transaction tr, IEnumerable<ObjectId> ids)
        {
            Extents3d? box = null;
            foreach (var id in ids)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;
                try
                {
                    var e = ent.GeometricExtents;
                    if (box.HasValue) { var b = box.Value; b.AddExtents(e); box = b; }
                    else box = e;
                }
                catch { /* entities with no extents are skipped */ }
            }
            if (box.HasValue && box.Value.MaxPoint.X - box.Value.MinPoint.X <= 0 && box.Value.MaxPoint.Y - box.Value.MinPoint.Y <= 0)
                return null;
            return box;
        }
    }
}
