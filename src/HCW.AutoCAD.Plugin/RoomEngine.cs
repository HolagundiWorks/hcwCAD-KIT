using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using HCW.AutoCAD.Plugin.Commands;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace HCW.AutoCAD.Plugin
{
    public struct RoomLogEntry
    {
        public string System, RoomType, AreaUnit;
        public double Width, Height, AreaValue, X, Y;
    }

    /// <summary>
    /// One room-label system (Metric, Feet, or Inches). Each system has its
    /// own text height, layers, floor prefix and session log, so loading
    /// more than one in the same drawing does not mix their settings.
    /// </summary>
    public abstract class RoomEngine
    {
        public double TextHeight;
        public bool DrawRect = true;
        public string LabelLayer;
        public string RectLayer;
        public string FloorPrefix = "";
        public double? TextHeightUnits;
        public readonly List<RoomLogEntry> Log = new List<RoomLogEntry>();

        private const string StoreDictionary = "HCW_ROOMS";
        private const string StoreRecord = "LOG";

        /// <summary>Reads the room log saved in the drawing, so schedules and totals survive a restart.</summary>
        private void LoadLog(Transaction tr, Database db)
        {
            Log.Clear();
            foreach (var line in DrawingStore.Read(tr, db, StoreDictionary, StoreRecord))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (p.Length < 8) continue;
                Func<string, double> d = t =>
                {
                    double v;
                    return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
                };
                Log.Add(new RoomLogEntry
                {
                    System = p[0], RoomType = p[1], AreaUnit = p[2],
                    Width = d(p[3]), Height = d(p[4]), AreaValue = d(p[5]), X = d(p[6]), Y = d(p[7])
                });
            }
        }

        private void SaveLog(Transaction tr, Database db)
        {
            Func<double, string> n = v => v.ToString("R", CultureInfo.InvariantCulture);
            DrawingStore.Write(tr, db, StoreDictionary, StoreRecord, Log.Select(e => Fields.Join(
                e.System, e.RoomType, e.AreaUnit, n(e.Width), n(e.Height), n(e.AreaValue), n(e.X), n(e.Y))));
        }

        public abstract string SystemTag { get; }     // "M" / "F" / "I"
        public abstract string HeightPromptUnit { get; }  // "metres" / "feet" / "inches"
        public abstract double DefaultTextHeight { get; }
        public abstract string DefaultLabelLayer { get; }
        public abstract string DefaultRectLayer { get; }

        /// <summary>Human-readable unit name for messages, e.g. "Metric", "Feet", "Millimeters".</summary>
        public virtual string UnitDisplayName => SystemTag switch { "M" => "Metric", "F" => "Feet", _ => "Inches" };

        protected RoomEngine()
        {
            ResetDefaults();
        }

        public void ResetDefaults()
        {
            TextHeight = DefaultTextHeight;
            DrawRect = true;
            LabelLayer = DefaultLabelLayer;
            RectLayer = DefaultRectLayer;
            TextHeightUnits = null;
            FloorPrefix = "";
        }

        /// <summary>Format a raw drawing-unit width/height pair for display (e.g. "3.40 m x 2.50 m" or 12'-6" x 10'-0").</summary>
        public abstract string FormatDim(double w, double h);
        /// <summary>Numeric area in this system's display unit (sq m / sq ft), from raw drawing-unit w/h.</summary>
        public abstract double AreaValue(double w, double h);
        public abstract string AreaUnitLabel { get; } // "m2" / "sq ft"
        /// <summary>Scale factor from one raw drawing unit to this system's display unit for a single length value.</summary>
        public abstract double ToDisplayLength(double raw);

        public string FormatArea(double w, double h) => "Area: " + AreaValue(w, h).ToString("F2") + " " + AreaUnitLabel;

        public double AskHeight(Editor ed)
        {
            var opt = new PromptDoubleOptions($"\nEnter text height in {HeightPromptUnit} <{TextHeight:F2}>: ")
            { AllowNegative = false, AllowZero = false, DefaultValue = TextHeight, UseDefaultValue = true };
            var res = ed.GetDouble(opt);
            if (res.Status == PromptStatus.OK) TextHeight = res.Value;
            TextHeightUnits = TextHeight; // 1 drawing unit == 1 display unit for all three systems here
            return TextHeightUnits.Value;
        }

        public void EnsureLayers(Transaction tr, Database db)
        {
            Util.EnsureLayer(tr, db, LabelLayer, 3);   // green
            Util.EnsureLayer(tr, db, RectLayer, 8);    // dark grey
        }

        /// <summary>Prompt two corners, draw the rectangle on those corners, and stack the label at its centre.</summary>
        public void CreateLabel(Editor ed, Database db, string roomType)
        {
            var p1r = ed.GetPoint("\n| Select FIRST corner of room: ");
            if (p1r.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelled."); return; }
            var p2r = ed.GetCorner(new PromptCornerOptions("\n| Select OPPOSITE corner of room: ", p1r.Value));
            if (p2r.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelled."); return; }

            // GetPoint / GetCorner return the current UCS. Entities are stored in WCS.
            // Writing the UCS numbers straight into the drawing drops the rectangle and
            // the label away from the corners whenever the UCS origin is not 0,0,0
            // or the plan is rotated.
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d u1 = p1r.Value, u2 = p2r.Value;
            Point3d[] corners =
            {
                new Point3d(u1.X, u1.Y, u1.Z).TransformBy(ucs),
                new Point3d(u2.X, u1.Y, u1.Z).TransformBy(ucs),
                new Point3d(u2.X, u2.Y, u1.Z).TransformBy(ucs),
                new Point3d(u1.X, u2.Y, u1.Z).TransformBy(ucs)
            };
            Point3d center = new Point3d(
                (corners[0].X + corners[2].X) / 2.0,
                (corners[0].Y + corners[2].Y) / 2.0,
                (corners[0].Z + corners[2].Z) / 2.0);

            double w = Math.Abs(u2.X - u1.X), h = Math.Abs(u2.Y - u1.Y);
            double th = TextHeight;
            Vector3d down = Vector3d.YAxis.Negate().TransformBy(ucs);

            string fullType = string.IsNullOrEmpty(FloorPrefix) ? roomType : FloorPrefix + " " + roomType;
            string dimText = FormatDim(w, h);
            string areaText = FormatArea(w, h);

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(tr, db);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (DrawRect)
                    AddRectangle(tr, btr, corners, ucs.CoordinateSystem3d.Zaxis);

                AddCenteredText(tr, btr, fullType.ToUpperInvariant(), center, th, ucs);
                AddCenteredText(tr, btr, dimText, center + down * (th * 1.5), th * 0.8, ucs);
                AddCenteredText(tr, btr, areaText, center + down * (th * 2.8), th * 0.7, ucs);

                LoadLog(tr, db);
                Log.Add(new RoomLogEntry
                {
                    System = SystemTag,
                    RoomType = fullType,
                    Width = w,
                    Height = h,
                    AreaValue = AreaValue(w, h),
                    AreaUnit = AreaUnitLabel,
                    X = center.X,
                    Y = center.Y
                });
                SaveLog(tr, db);

                tr.Commit();
            }

            ed.WriteMessage("\n+----------------------------------------" +
                            "\n| " + fullType.ToUpperInvariant() +
                            "\n| " + dimText +
                            "\n| " + areaText +
                            "\n+----------------------------------------");
        }

        /// <summary>Rectangle through the four WCS corners, on the UCS plane the user picked.</summary>
        private void AddRectangle(Transaction tr, BlockTableRecord btr, Point3d[] wcsCorners, Vector3d normal)
        {
            var pl = new Polyline { Normal = normal, Closed = true, Layer = RectLayer };
            Matrix3d toPlane = Matrix3d.WorldToPlane(normal);
            for (int i = 0; i < wcsCorners.Length; i++)
            {
                Point3d ocs = wcsCorners[i].TransformBy(toPlane);
                if (i == 0) pl.Elevation = ocs.Z;
                pl.AddVertexAt(i, new Point2d(ocs.X, ocs.Y), 0, 0, 0);
            }
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            Commands.MeasureCommands.AddRoomOutlines(tr, Util.Db, btr, pl);
        }

        private void AddCenteredText(Transaction tr, BlockTableRecord btr, string text, Point3d center, double height, Matrix3d ucs)
        {
            var normal = ucs.CoordinateSystem3d.Zaxis;
            var t = new DBText
            {
                Height = height,
                TextString = text,
                Layer = LabelLayer,
                Normal = normal,
                Rotation = Vector3d.XAxis.GetAngleTo(ucs.CoordinateSystem3d.Xaxis, normal),
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid
            };
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            // AlignmentPoint is ignored until the text is database-resident.
            // Setting it only before AppendEntity leaves the label at the origin.
            t.AlignmentPoint = center;
            t.AdjustAlignment(btr.Database);
        }

        public void AreaLabelForSelected(Editor ed, Database db)
        {
            var per = ed.GetEntity("\n| Select polyline or rectangle: ");
            if (per.Status != PromptStatus.OK) { ed.WriteMessage("\n| No object selected."); return; }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead);
                if (!(ent is Polyline pl))
                {
                    ed.WriteMessage("\n| Selected object has no area property.");
                    return;
                }
                double area = Math.Abs(pl.Area);
                Point3d ocsMid = CentroidOf(pl);
                Point3d centroid = new Point3d(ocsMid.X, ocsMid.Y, pl.Elevation).TransformBy(Matrix3d.PlaneToWorld(pl.Normal));
                double th = TextHeight;
                string areaText = "Area: " + AreaValueFromRaw(area).ToString("F2") + " " + AreaUnitLabel;

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                AddCenteredText(tr, btr, areaText, centroid, th, Matrix3d.PlaneToWorld(pl.Normal));
                tr.Commit();
                ed.WriteMessage("\n| " + areaText + " created at centroid");
            }
        }

        /// <summary>Area value (already in raw drawing-unit^2) -> display unit. Overridden per system for the right scale^2.</summary>
        protected abstract double AreaValueFromRaw(double rawArea);

        private static Point3d CentroidOf(Polyline pl)
        {
            int n = pl.NumberOfVertices;
            double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { var p = pl.GetPoint2dAt(i); sx += p.X; sy += p.Y; }
            return new Point3d(sx / n, sy / n, 0);
        }

        public void ToggleRect(Editor ed)
        {
            DrawRect = !DrawRect;
            ed.WriteMessage("\n| Rectangle drawing: " + (DrawRect ? "ON" : "OFF"));
        }

        public void HideRect(Editor ed, Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(RectLayer)) { ed.WriteMessage($"\n| Layer {RectLayer} does not exist yet."); return; }
                var ltr = (LayerTableRecord)tr.GetObject(lt[RectLayer], OpenMode.ForWrite);
                ltr.IsFrozen = !ltr.IsFrozen;
                ed.WriteMessage($"\n| {RectLayer}: " + (ltr.IsFrozen ? "HIDDEN" : "VISIBLE"));
                tr.Commit();
            }
        }

        public void SetTextHeight(Editor ed) =>
            ed.WriteMessage($"\n| Text height set to: {AskHeight(ed):F2} {(HeightPromptUnit == "metres" ? "m" : HeightPromptUnit)}");

        public void SetLayer(Editor ed)
        {
            var r = ed.GetString("\n| Enter layer name for room labels: ");
            if (r.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(r.StringResult)) { ed.WriteMessage("\n| Cancelled."); return; }
            LabelLayer = r.StringResult.Trim();
            ed.WriteMessage("\n| Layer set to: " + LabelLayer);
        }

        public void ShowSettings(Editor ed)
        {
            ed.WriteMessage("\n============================================");
            ed.WriteMessage($"\n   CURRENT SETTINGS ({UnitDisplayName.ToUpperInvariant()})");
            ed.WriteMessage("\n============================================");
            ed.WriteMessage($"\n  Text Height    : {TextHeight:F2} {(HeightPromptUnit == "metres" ? "m" : HeightPromptUnit)}");
            ed.WriteMessage($"\n  Draw Rectangle : {(DrawRect ? "YES" : "NO")}");
            ed.WriteMessage($"\n  Text Layer     : {LabelLayer}");
            ed.WriteMessage($"\n  Rect Layer     : {RectLayer}");
            ed.WriteMessage($"\n  Floor Prefix   : {(string.IsNullOrEmpty(FloorPrefix) ? "(none)" : FloorPrefix)}");
            ed.WriteMessage("\n============================================");
        }

        public void ResetAndAnnounce(Editor ed)
        {
            ResetDefaults();
            ed.WriteMessage($"\n| All {UnitDisplayName} room settings reset to defaults.");
        }

        public void SetFloorPrefix(Editor ed)
        {
            var r = ed.GetString($"\nEnter floor prefix (e.g. GF, FF, 2F) <{(string.IsNullOrEmpty(FloorPrefix) ? "none" : FloorPrefix)}>  (Enter to clear): ");
            if (r.Status != PromptStatus.OK) return;
            FloorPrefix = r.StringResult ?? "";
            ed.WriteMessage(string.IsNullOrEmpty(FloorPrefix) ? "\nMFLOOR: Floor prefix cleared." : $"\nMFLOOR: Labels will be prefixed '{FloorPrefix}'.");
        }

        public void Relabel(Editor ed, Database db)
        {
            var per = ed.GetEntity("\nPick existing room type text to relabel: ");
            if (per.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(per.ObjectId, OpenMode.ForWrite) is DBText t)) { ed.WriteMessage("\nSelect a TEXT object."); return; }
                string old = t.TextString;
                ed.WriteMessage("\nCurrent text: " + old);
                var r = ed.GetString("\nEnter new room type: ");
                if (r.Status != PromptStatus.OK || string.IsNullOrEmpty(r.StringResult)) { ed.WriteMessage("\nCancelled."); return; }
                string newStr = (string.IsNullOrEmpty(FloorPrefix) ? r.StringResult : FloorPrefix + " " + r.StringResult).ToUpperInvariant();
                t.TextString = newStr;
                tr.Commit();
                ed.WriteMessage($"\nMRELABEL: '{old}' -> '{newStr}'");
            }
        }

        public void Audit(Editor ed, Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                int nL = 0, nR = 0;
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (string.Equals(ent.Layer, LabelLayer, StringComparison.OrdinalIgnoreCase) && ent is DBText) nL++;
                    else if (string.Equals(ent.Layer, RectLayer, StringComparison.OrdinalIgnoreCase)) nR++;
                }
                ed.WriteMessage("\n============================================");
                ed.WriteMessage($"\n   MAUDIT — {UnitDisplayName} Room Layer Audit");
                ed.WriteMessage("\n============================================");
                ed.WriteMessage($"\n  Labels found    : {nL}");
                ed.WriteMessage($"\n  Rectangles found: {nR}");
                // room labels are 3 TEXT entities per rectangle (type/dim/area)
                int labelGroups = nL / 3;
                if (labelGroups == nR) ed.WriteMessage("\n  Counts match — likely OK");
                else
                {
                    ed.WriteMessage($"\n  WARN: {Math.Abs(labelGroups - nR)} unmatched item(s)");
                    ed.WriteMessage(labelGroups > nR
                        ? $"\n  {labelGroups - nR} label(s) may have no rectangle"
                        : $"\n  {nR - labelGroups} rectangle(s) may have no label");
                }
                ed.WriteMessage("\n============================================");
            }
        }

        public void Check(Editor ed, Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                int openCount = 0, lineCount = 0, okCount = 0, any = 0;
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || !string.Equals(ent.Layer, RectLayer, StringComparison.OrdinalIgnoreCase)) continue;
                    any++;
                    if (ent is Polyline pl) { if (pl.Closed) okCount++; else openCount++; }
                    else if (ent is Line) lineCount++;
                    else openCount++;
                }
                if (any == 0) { ed.WriteMessage($"\nNo objects on the {SystemTag} rectangle layer."); return; }
                ed.WriteMessage("\n============================================");
                ed.WriteMessage("\n   MCHECK — Rectangle Layer Check");
                ed.WriteMessage("\n============================================");
                ed.WriteMessage($"\n  Closed LWPOLYLINEs : {okCount}");
                ed.WriteMessage($"\n  Open polylines     : {openCount}");
                ed.WriteMessage($"\n  LINE objects       : {lineCount}");
                ed.WriteMessage(openCount == 0 && lineCount == 0 ? "\n  Result: PASS — all rectangles OK" : "\n  Result: WARN — see counts above");
                ed.WriteMessage("\n============================================");
            }
        }

        private void RefreshLog(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                LoadLog(tr, db);
                tr.Commit();
            }
        }

        public void Schedule(Editor ed, Database db)
        {
            RefreshLog(db);
            if (Log.Count == 0) { ed.WriteMessage("\nNo room records in this drawing."); return; }
            string dwgPath = db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string csvPath = dir + $"RoomSchedule-{UnitDisplayName}.csv";
            var rows = Log.OrderBy(r => r.RoomType).Select(r => (IEnumerable<string>)new[]
            {
                r.RoomType, r.Width.ToString("F3"), r.Height.ToString("F3"), r.AreaValue.ToString("F3"), r.X.ToString("F3"), r.Y.ToString("F3")
            });
            Util.WriteCsv(csvPath, new[] { "RoomType", "Width", "Height", $"Area_{AreaUnitLabel}", "InsX", "InsY" }, rows);
            ed.WriteMessage($"\nMSCHEDULE: {Log.Count} label(s) (in this drawing) exported to {csvPath}");
        }

        /// <summary>Draws the room schedule as a table at a picked point: number, room, size and area, then the total.</summary>
        public void Table(Editor ed, Database db)
        {
            RefreshLog(db);
            if (Log.Count == 0) { ed.WriteMessage("\nNo room records in this drawing."); return; }
            var ppr = ed.GetPoint("\nPick a point for the room schedule (top-left): ");
            if (ppr.Status != PromptStatus.OK) return;
            var rows = new List<string[]>();
            int no = 0;
            double total = 0;
            foreach (var e in Log.OrderBy(r => r.RoomType, StringComparer.OrdinalIgnoreCase))
            {
                no++;
                total += e.AreaValue;
                rows.Add(new[] { no.ToString(), e.RoomType.ToUpperInvariant(), FormatDim(e.Width, e.Height), e.AreaValue.ToString("F2") });
            }
            rows.Add(new[] { "", "TOTAL", "", total.ToString("F2") });
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MeasureCommands.EnsureTableLayer(tr, db);
                MeasureCommands.DrawTable(tr, db, ppr.Value, new[] { "No", "Room", "Size", "Area (" + AreaUnitLabel + ")" }, rows, TextHeight);
                tr.Commit();
            }
            ed.WriteMessage("\nRoom schedule: " + no + " room(s), total " + total.ToString("F2") + " " + AreaUnitLabel + ".");
        }

        public void Total(Editor ed)
        {
            RefreshLog(Util.Db);
            var r = ed.GetString("\nRoom type filter (e.g. BEDROOM, or Enter for all): ");
            string filter = r.Status == PromptStatus.OK ? (r.StringResult ?? "") : "";
            if (Log.Count == 0) { ed.WriteMessage("\nNo room records in this drawing."); return; }
            double total = 0; int count = 0;
            foreach (var e in Log)
            {
                if (filter == "" || e.RoomType.ToUpperInvariant().Contains(filter.ToUpperInvariant()))
                {
                    total += e.AreaValue; count++;
                }
            }
            ed.WriteMessage($"\nMTOTAL{(filter != "" ? $" [{filter}]" : " [all]")}: {count} room(s)  total area = {total:F2} {AreaUnitLabel}");
        }
    }

    public class RoomEngineAuto : RoomEngine
    {
        private static Autodesk.AutoCAD.DatabaseServices.UnitsValue Units => Util.Db.Insunits;
        private static bool IsImperial =>
            Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet ||
            Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches;

        public override string SystemTag
        {
            get
            {
                switch (Units)
                {
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters: return "MM";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters: return "CM";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet: return "FT";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches: return "IN";
                    default: return "M";
                }
            }
        }

        public override string UnitDisplayName
        {
            get
            {
                switch (Units)
                {
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters: return "Millimeters";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters: return "Centimeters";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet: return "Feet";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches: return "Inches";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Meters: return "Meters";
                    default: return "Meters (drawing units unset, assuming metres)";
                }
            }
        }

        public override string HeightPromptUnit
        {
            get
            {
                switch (Units)
                {
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters: return "millimetres";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters: return "centimetres";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet: return "feet";
                    case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches: return "inches";
                    default: return "metres";
                }
            }
        }

        // Default label height: a real-world ~125mm (a typical 1:50 working-
        // drawing room label), converted into whatever the drawing's raw
        // unit actually is - see Util.MmToDrawingUnits.
        public override double DefaultTextHeight => Util.MmToDrawingUnits(125.0);

        public override string DefaultLabelLayer => Util.Out("ROOM-LABELS");
        public override string DefaultRectLayer => Util.Out("ROOM-RECT");

        public override string AreaUnitLabel => IsImperial ? "sq ft" : "m2";

        private static double ToMetres(double raw)
        {
            switch (Units)
            {
                case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters: return raw / 1000.0;
                case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters: return raw / 100.0;
                case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Feet: return raw * 0.3048;
                case Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches: return raw * 0.0254;
                default: return raw; // Meters, or undefined - assume metres
            }
        }

        private static double ToFeet(double raw) =>
            Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches ? raw / 12.0 : raw;

        public override double ToDisplayLength(double raw) => IsImperial ? ToFeet(raw) : ToMetres(raw);

        public override double AreaValue(double w, double h) =>
            IsImperial ? ToFeet(w) * ToFeet(h) : ToMetres(w) * ToMetres(h);

        protected override double AreaValueFromRaw(double rawArea)
        {
            if (IsImperial)
            {
                double s = Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches ? 1.0 / 12.0 : 1.0;
                return rawArea * s * s;
            }
            double m = Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters ? 1.0 / 1000.0
                     : Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters ? 1.0 / 100.0
                     : 1.0;
            return rawArea * m * m;
        }

        private static string ArchDim(double feetValue)
        {
            int totalIn = (int)(Math.Abs(feetValue) * 12.0 + 0.5);
            int ft = totalIn / 12, inch = totalIn % 12;
            return $"{ft}'-{inch}\"";
        }

        public override string FormatDim(double w, double h)
        {
            if (IsImperial) return $"{ArchDim(ToFeet(w))} x {ArchDim(ToFeet(h))}";
            string suffix = Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Millimeters ? "mm"
                          : Units == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Centimeters ? "cm" : "m";
            return $"{w:F2} {suffix} x {h:F2} {suffix}";
        }
    }
}
