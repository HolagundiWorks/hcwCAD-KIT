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
    /// POLYAREA — numbers selected polylines and draws a running-total area table.
    /// </summary>
    public class PolyAreaCommands
    {
        [CommandMethod("POLYAREA")]
        public void PolyArea()
        {
            var ed = Util.Ed; var db = Util.Db;

            // The other ways of putting an area on a shape are the same job with a different result.
            string how = Util.AskMode("Area", "Table", "Live", "Field", "Room", "Measure");
            if (how == null) return;
            if (how == "Live") { new DraftExtraCommands().AreaLabel(); return; }
            if (how == "Field") { new DraftExtraCommands().AreaField(); return; }
            if (how == "Room") { new HcwLayerCommands().HcwRoomArea(); return; }
            if (how == "Measure") { new MeasureCommands().MAre(); return; }

            // Step 1 - drawing units
            string drawUnitName = db.Insunits switch
            {
                UnitsValue.Inches => "Inches",
                UnitsValue.Feet => "Feet",
                UnitsValue.Millimeters => "Millimetres",
                UnitsValue.Centimeters => "Centimetres",
                UnitsValue.Meters => "Metres",
                UnitsValue.Kilometers => "Kilometres",
                _ => "Unitless/Unknown"
            };
            ed.WriteMessage($"\nDrawing units detected: {drawUnitName} (INSUNITS={(int)db.Insunits})");

            // Step 2 - output unit
            var pio = new PromptIntegerOptions("\nOutput area units: 1 = Square Metres (sq m), 2 = Square Feet (sq ft) <1>: ")
            { AllowNegative = false, AllowZero = false, DefaultValue = 1, UseDefaultValue = true, LowerLimit = 1, UpperLimit = 2 };
            var pir = ed.GetInteger(pio);
            int answer = pir.Status == PromptStatus.OK ? pir.Value : 1;

            string unitLabel; double convFactor;
            if (answer == 1)
            {
                unitLabel = "sq m";
                convFactor = db.Insunits switch
                {
                    UnitsValue.Inches => 0.0254 * 0.0254,
                    UnitsValue.Feet => 0.3048 * 0.3048,
                    UnitsValue.Millimeters => 1.0e-6,
                    UnitsValue.Centimeters => 1.0e-4,
                    UnitsValue.Meters => 1.0,
                    UnitsValue.Kilometers => 1.0e6,
                    _ => 1.0
                };
            }
            else
            {
                unitLabel = "sq ft";
                convFactor = db.Insunits switch
                {
                    UnitsValue.Inches => 1.0 / 144.0,
                    UnitsValue.Feet => 1.0,
                    UnitsValue.Millimeters => 1.07639e-5,
                    UnitsValue.Centimeters => 1.07639e-3,
                    UnitsValue.Meters => 10.7639,
                    UnitsValue.Kilometers => 1.07639e7,
                    _ => 1.0
                };
            }
            ed.WriteMessage("\nAreas will be reported in: " + unitLabel);

            // Step 3 - prefix
            var pfr = ed.GetString(new PromptStringOptions("\nEnter a prefix for polyline numbers (e.g. ROOM- or UNIT A-). Prefix <none>: ") { AllowSpaces = true });
            string prefix = pfr.Status == PromptStatus.OK ? (pfr.StringResult ?? "") : "";
            ed.WriteMessage(prefix == "" ? "\nNo prefix - labels will be: 1, 2, 3 ..." : $"\nPrefix set - labels will be: {prefix}1, {prefix}2, {prefix}3 ...");

            // Step 4 - text height
            double txtHDefault = db.Textsize > 1e-6 ? db.Textsize : 2.5;
            var thr = ed.GetDistance(new PromptDistanceOptions($"\nEnter text height <{txtHDefault:F4}>: ") { AllowZero = false, AllowNegative = false, DefaultValue = txtHDefault, UseDefaultValue = true });
            double txtH = thr.Status == PromptStatus.OK ? thr.Value : txtHDefault;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                string activeLayer = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;
                ed.WriteMessage("\nPoly number labels will be placed on layer: " + activeLayer);
                Util.EnsureLayer(tr, db, "AREA_TABLE", 4);

                ed.WriteMessage("\nSelect polylines to number and measure: ");
                var psr = ed.GetSelection(new PromptSelectionOptions(), new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE") }));
                if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo polylines selected. Command cancelled."); tr.Commit(); return; }

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var areaList = new List<(int idx, double area, Point3d pt)>();
                double totalArea = 0; int closedCount = 0, n = 0;

                foreach (SelectedObject so in psr.Value)
                {
                    n++;
                    var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForWrite);
                    double area = 0; Point3d pt = Point3d.Origin;
                    if (ent is Polyline pl)
                    {
                        if (!pl.Closed) { pl.Closed = true; closedCount++; }
                        area = Math.Abs(pl.Area);
                        pt = Centroid(pl);
                    }
                    else if (ent is Polyline2d || ent is Polyline3d)
                    {
                        try { area = Math.Abs(((Curve)ent).Area); } catch { area = 0; }
                        var bb = ent.Bounds;
                        if (bb.HasValue) pt = new Point3d((bb.Value.MinPoint.X + bb.Value.MaxPoint.X) / 2.0, (bb.Value.MinPoint.Y + bb.Value.MaxPoint.Y) / 2.0, 0);
                    }
                    areaList.Add((n, area, pt));
                    totalArea += area;
                }

                foreach (var item in areaList)
                {
                    string lbl = prefix + item.idx;
                    PlaceText(tr, btr, lbl, item.pt.X, item.pt.Y, txtH, activeLayer, 256);
                }

                var ppr = ed.GetPoint("\nPick table insertion point: ");
                if (ppr.Status != PromptStatus.OK) { ed.WriteMessage("\nTable insertion cancelled."); tr.Commit(); return; }
                Point3d tblPt = ppr.Value;

                int maxLblLen = areaList.Max(item => (prefix + item.idx).Length);
                double colW = Math.Max(txtH * 14.0, maxLblLen * txtH * 0.8);
                double rowH = txtH * 2.8, hdrH = txtH * 3.5;
                double x0 = tblPt.X, y0 = tblPt.Y;
                double x1 = x0 + colW, x2 = x0 + colW * 2.0, x3 = x0 + colW * 3.0;

                double ytop = y0, ybot = y0 - hdrH;
                DrawRect(tr, btr, x0, ytop, x3, ybot);
                PlaceText(tr, btr, "POLYLINE AREA TABLE", (x0 + x3) / 2.0, (ytop + ybot) / 2.0, txtH * 1.4, "AREA_TABLE", 4);

                ytop = ybot; ybot = ytop - rowH * 0.9;
                DrawRect(tr, btr, x0, ytop, x3, ybot);
                PlaceText(tr, btr, $"Drawing units: {drawUnitName}   |   Areas in: {unitLabel}" + (prefix == "" ? "" : $"   |   Prefix: {prefix}"),
                    (x0 + x3) / 2.0, (ytop + ybot) / 2.0, txtH * 0.75, "AREA_TABLE", 3);

                ytop = ybot; ybot = ytop - rowH;
                DrawRect(tr, btr, x0, ytop, x1, ybot); DrawRect(tr, btr, x1, ytop, x2, ybot); DrawRect(tr, btr, x2, ytop, x3, ybot);
                PlaceText(tr, btr, "LABEL", (x0 + x1) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 4);
                PlaceText(tr, btr, $"AREA ({unitLabel})", (x1 + x2) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 4);
                PlaceText(tr, btr, $"CUM. SUM ({unitLabel})", (x2 + x3) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 4);

                double runningSum = 0;
                foreach (var item in areaList)
                {
                    double convertedArea = item.area * convFactor;
                    string lbl = prefix + item.idx;
                    runningSum += convertedArea;
                    ytop = ybot; ybot = ytop - rowH;
                    DrawRect(tr, btr, x0, ytop, x1, ybot); DrawRect(tr, btr, x1, ytop, x2, ybot); DrawRect(tr, btr, x2, ytop, x3, ybot);
                    PlaceText(tr, btr, lbl, (x0 + x1) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 7);
                    PlaceText(tr, btr, convertedArea.ToString("F4"), (x1 + x2) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 7);
                    PlaceText(tr, btr, runningSum.ToString("F4"), (x2 + x3) / 2.0, (ytop + ybot) / 2.0, txtH, "AREA_TABLE", 7);
                }

                double convertedTotal = runningSum;
                ytop = ybot; ybot = ytop - rowH * 1.4;
                DrawRect(tr, btr, x0, ytop, x3, ybot); DrawRect(tr, btr, x0, ytop, x2, ybot);
                PlaceText(tr, btr, "TOTAL AREA", (x0 + x2) / 2.0, (ytop + ybot) / 2.0, txtH * 1.1, "AREA_TABLE", 1);
                PlaceText(tr, btr, $"{convertedTotal:F4} {unitLabel}", (x2 + x3) / 2.0, (ytop + ybot) / 2.0, txtH * 1.1, "AREA_TABLE", 1);

                tr.Commit();

                ed.WriteMessage("\n\n=== POLYAREA COMPLETE ===" +
                    $"\n  Polylines processed : {n}" +
                    $"\n  Polylines closed    : {closedCount}" +
                    $"\n  Drawing units       : {drawUnitName}" +
                    $"\n  Output units        : {unitLabel}" +
                    $"\n  Total area          : {convertedTotal:F4} {unitLabel}" +
                    $"\n  Prefix used         : {(prefix == "" ? "(none)" : prefix)}" +
                    $"\n  Text height         : {txtH:F4}" +
                    $"\n  Label layer         : {activeLayer} (your active layer)" +
                    "\n  Table layer         : AREA_TABLE (cyan)" +
                    "\n=========================\n");
            }
        }

        private static Point3d Centroid(Polyline pl)
        {
            int n = pl.NumberOfVertices; double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { var p = pl.GetPoint2dAt(i); sx += p.X; sy += p.Y; }
            return new Point3d(sx / n, sy / n, 0);
        }

        private static void DrawRect(Transaction tr, BlockTableRecord btr, double x1, double y1, double x2, double y2)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x2, y1), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x2, y2), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1, y2), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = "AREA_TABLE";
            pl.ColorIndex = 256; // ByLayer
            btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void PlaceText(Transaction tr, BlockTableRecord btr, string txt, double cx, double cy, double ht, string layer, int col)
        {
            var pt = new Point3d(cx, cy, 0);
            var t = new DBText
            {
                Position = pt, Height = ht, TextString = txt, Layer = layer,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = pt,
                ColorIndex = col
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
        }
    }
}
