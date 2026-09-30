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
    /// AUTODIM: dimension chains around the outside of a plan, for working drawings.
    ///
    /// Each side gets up to three chains, nearest the plan first: openings (door and window jambs and the
    /// wall pieces between them), structure (wall ends and corners), and the overall length. Points come
    /// from the selected walls (line and polyline vertices) and from deduction lines: the lines on
    /// MEASURE-DEDUCT and the deduction line inside every door or window block. Axis-aligned plans only.
    /// Everything it creates is tagged, so AUTODIMCLEAR removes it and nothing you drew by hand.
    /// </summary>
    public class AutoDimCommands
    {
        private const string AppName = "HCW_AUTODIM";
        private const string DimLayer = "AN-DIMS";

        private static double _scale = 100;
        private static string _sides = "All";
        private static string _levels = "All";

        private enum Side { Bottom, Top, Left, Right }

        [CommandMethod("AUTODIM")]
        public void AutoDim()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var filter = new SelectionFilter(new[] { new TypedValue(0, "LINE,LWPOLYLINE,POLYLINE") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the plan walls (lines and polylines; deduction lines are read too): " }, filter);
            if (psr.Status != PromptStatus.OK) return;

            var sideOpt = new PromptKeywordOptions("\nSides [All/Top/Bottom/Left/Right] <" + _sides + ">: ") { AllowNone = true };
            foreach (var k in new[] { "All", "Top", "Bottom", "Left", "Right" }) sideOpt.Keywords.Add(k);
            var sideRes = ed.GetKeywords(sideOpt);
            if (sideRes.Status == PromptStatus.OK) _sides = sideRes.StringResult;
            else if (sideRes.Status != PromptStatus.None) return;

            var levelOpt = new PromptKeywordOptions("\nChains [All/Overall/Structure/Openings] <" + _levels + ">: ") { AllowNone = true };
            foreach (var k in new[] { "All", "Overall", "Structure", "Openings" }) levelOpt.Keywords.Add(k);
            var levelRes = ed.GetKeywords(levelOpt);
            if (levelRes.Status == PromptStatus.OK) _levels = levelRes.StringResult;
            else if (levelRes.Status != PromptStatus.None) return;

            var scale = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (scale.Status != PromptStatus.OK) return;
            _scale = scale.Value;

            // Settings are plotted millimetres; convert to drawing units at the chosen scale.
            double mm = Util.MmToDrawingUnits(1.0);
            double step = Settings.GetDouble("AutoDimStepMm", 8) * _scale * mm;
            double gap = Settings.GetDouble("AutoDimGapMm", 12) * _scale * mm;
            double minLen = Settings.GetDouble("AutoDimMinMm", 3) * _scale * mm;
            double merge = 5 * mm;                                                    // 5 mm of real size
            double band = Settings.GetDouble("AutoDimBandM", 0.6) * 1000 * mm;        // outer band, real size

            var structural = new List<Point3d>();
            var jambs = new List<Point3d>();
            int skipped = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var curve = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null) continue;
                    bool deduction = string.Equals(curve.Layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase);
                    Collect(curve, deduction ? jambs : structural, ref skipped);
                }
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (var found in BlockOpenings.Collect(tr, space))
                    Collect(found.Curve, jambs, ref skipped);
                tr.Commit();
            }
            if (structural.Count == 0)
            {
                ed.WriteMessage("\nAUTODIM: no axis-aligned wall geometry was selected.");
                return;
            }

            double minX = structural.Min(p => p.X), maxX = structural.Max(p => p.X);
            double minY = structural.Min(p => p.Y), maxY = structural.Max(p => p.Y);

            var sides = _sides == "All"
                ? new[] { Side.Bottom, Side.Top, Side.Left, Side.Right }
                : new[] { (Side)Enum.Parse(typeof(Side), _sides) };

            int made = 0, dropped = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, DimLayer, 8);
                EnsureRegApp(tr, db);
                ObjectId style = FindStyle(tr, db);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var side in sides)
                {
                    bool horizontal = side == Side.Bottom || side == Side.Top;
                    // coordinate along the side, and whether a point sits in this side's outer band
                    Func<Point3d, double> along = p => horizontal ? p.X : p.Y;
                    Func<Point3d, bool> inBand = p =>
                        side == Side.Bottom ? p.Y <= minY + band :
                        side == Side.Top ? p.Y >= maxY - band :
                        side == Side.Left ? p.X <= minX + band : p.X >= maxX - band;

                    var wall = DimChains.Merge(structural.Where(inBand).Select(along), merge);
                    var all = DimChains.Merge(structural.Where(inBand).Concat(jambs.Where(inBand)).Select(along), merge);
                    var overall = DimChains.Merge(structural.Select(along), merge);

                    // nearest the plan first: openings, structure, overall
                    var chains = new List<List<KeyValuePair<double, double>>>();
                    if (_levels == "All" || _levels == "Openings")
                        if (all.Count > wall.Count) chains.Add(DimChains.Segments(all, minLen));
                    if (_levels == "All" || _levels == "Structure")
                        chains.Add(DimChains.Segments(wall, minLen));
                    if (_levels == "All" || _levels == "Overall")
                        chains.Add(DimChains.Overall(overall, minLen));

                    // a chain that repeats the one inside it adds nothing
                    for (int i = chains.Count - 1; i > 0; i--)
                        if (DimChains.Same(chains[i], chains[i - 1], merge)) { dropped += chains[i].Count; chains.RemoveAt(i); }
                    chains.RemoveAll(c => c.Count == 0);

                    for (int level = 0; level < chains.Count; level++)
                    {
                        double offset = gap + level * step;
                        foreach (var seg in chains[level])
                        {
                            Point3d a, b, line;
                            switch (side)
                            {
                                case Side.Bottom:
                                    a = new Point3d(seg.Key, minY, 0); b = new Point3d(seg.Value, minY, 0);
                                    line = new Point3d(seg.Key, minY - offset, 0); break;
                                case Side.Top:
                                    a = new Point3d(seg.Key, maxY, 0); b = new Point3d(seg.Value, maxY, 0);
                                    line = new Point3d(seg.Key, maxY + offset, 0); break;
                                case Side.Left:
                                    a = new Point3d(minX, seg.Key, 0); b = new Point3d(minX, seg.Value, 0);
                                    line = new Point3d(minX - offset, seg.Key, 0); break;
                                default:
                                    a = new Point3d(maxX, seg.Key, 0); b = new Point3d(maxX, seg.Value, 0);
                                    line = new Point3d(maxX + offset, seg.Key, 0); break;
                            }
                            var dim = new AlignedDimension(a, b, line, "", style) { Layer = DimLayer };
                            dim.XData = new ResultBuffer(
                                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "AUTODIM"));
                            btr.AppendEntity(dim);
                            tr.AddNewlyCreatedDBObject(dim, true);
                            made++;
                        }
                    }
                }
                tr.Commit();
            }

            ed.WriteMessage("\nAUTODIM: " + made + " dimension(s) on " + DimLayer + " at 1:" + _scale
                + (dropped > 0 ? "; " + dropped + " repeated dimension(s) left out" : "")
                + (skipped > 0 ? "; " + skipped + " angled or curved segment(s) skipped" : "") + ".");
        }

        /// <summary>Deletes the dimensions AUTODIM made, and only those.</summary>
        [CommandMethod("AUTODIMCLEAR")]
        public void Clear()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            int removed = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var dim = tr.GetObject(id, OpenMode.ForRead) as Dimension;
                    if (dim == null || dim.GetXDataForApplication(AppName) == null) continue;
                    dim.UpgradeOpen();
                    dim.Erase();
                    removed++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nAUTODIMCLEAR: " + removed + " dimension(s) removed.");
        }

        /// <summary>Adds the end points of a horizontal or vertical curve (every vertex for a polyline); counts angled segments.</summary>
        private static void Collect(Curve curve, List<Point3d> into, ref int skipped)
        {
            var poly = curve as Polyline;
            if (poly != null)
            {
                int n = poly.NumberOfVertices;
                for (int i = 0; i < n; i++)
                {
                    var p = poly.GetPoint3dAt(i);
                    into.Add(p);
                    int next = i + 1 < n ? i + 1 : (poly.Closed ? 0 : -1);
                    if (next >= 0 && !Orthogonal(p, poly.GetPoint3dAt(next))) skipped++;
                }
                return;
            }
            var line = curve as Line;
            if (line != null)
            {
                if (!Orthogonal(line.StartPoint, line.EndPoint)) { skipped++; return; }
                into.Add(line.StartPoint);
                into.Add(line.EndPoint);
                return;
            }
            skipped++;
        }

        private static bool Orthogonal(Point3d a, Point3d b)
        {
            double tol = Util.MmToDrawingUnits(1.0);
            return Math.Abs(a.X - b.X) <= tol || Math.Abs(a.Y - b.Y) <= tol;
        }

        private static ObjectId FindStyle(Transaction tr, Database db)
        {
            var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
            string wanted = Settings.Get("AutoDimStyle", "HCW-WORKING");
            return table.Has(wanted) ? table[wanted] : db.Dimstyle;
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(AppName)) return;
            table.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = AppName };
            table.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
