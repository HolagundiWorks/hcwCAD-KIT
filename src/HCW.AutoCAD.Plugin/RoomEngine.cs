using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

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

        /// <summary>Core of MBR/MKI/.../MDO/dialog pick: prompt two corners, draw rect + 3-line label stack, log it.</summary>
        public void CreateLabel(Editor ed, Database db, string roomType)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayers(tr, db);

                var p1r = ed.GetPoint("\n| Select FIRST corner of room: ");
                if (p1r.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelled."); tr.Commit(); return; }
                var p2opt = new PromptCornerOptions("\n| Select OPPOSITE corner of room: ", p1r.Value);
                var p2r = ed.GetCorner(p2opt);
                if (p2r.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelled."); tr.Commit(); return; }

                Point3d p1 = p1r.Value, p2 = p2r.Value;
                double cx = (p1.X + p2.X) / 2.0, cy = (p1.Y + p2.Y) / 2.0;
                double w = Math.Abs(p2.X - p1.X), h = Math.Abs(p2.Y - p1.Y);

                double th = TextHeightUnits ?? AskHeight(ed);

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                if (DrawRect)
                {
                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
                    pl.Closed = true;
                    pl.Layer = RectLayer;
                    btr.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                }

                string fullType = string.IsNullOrEmpty(FloorPrefix) ? roomType : FloorPrefix + " " + roomType;
                string dimText = FormatDim(w, h);
                string areaText = FormatArea(w, h);

                AddCenteredText(tr, btr, fullType.ToUpperInvariant(), cx, cy, th);
                AddCenteredText(tr, btr, dimText, cx, cy - th * 1.5, th * 0.8);
                AddCenteredText(tr, btr, areaText, cx, cy - th * 2.8, th * 0.7);

                Log.Add(new RoomLogEntry
                {
                    System = SystemTag,
                    RoomType = fullType,
                    Width = w,
                    Height = h,
                    AreaValue = AreaValue(w, h),
                    AreaUnit = AreaUnitLabel,
                    X = cx,
                    Y = cy
                });

                tr.Commit();
                ed.WriteMessage("\n+----------------------------------------" +
                                "\n| " + fullType.ToUpperInvariant() +
                                "\n| " + dimText +
                                "\n| " + areaText +
                                "\n+----------------------------------------");
            }
        }

        private void AddCenteredText(Transaction tr, BlockTableRecord btr, string text, double x, double y, double h)
        {
            var t = new DBText
            {
                Position = new Point3d(x, y, 0),
                Height = h,
                TextString = text,
                Layer = LabelLayer,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = new Point3d(x, y, 0)
            };
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
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
                Point3d centroid = CentroidOf(pl);
                double th = TextHeightUnits ?? AskHeight(ed);
                string areaText = "Area: " + AreaValueFromRaw(area).ToString("F2") + " " + AreaUnitLabel;

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var t = new DBText
                {
                    Position = centroid,
                    Height = th,
                    TextString = areaText,
                    Layer = LabelLayer,
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = centroid
                };
                btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
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

        public void Schedule(Editor ed, Database db)
        {
            if (Log.Count == 0) { ed.WriteMessage("\nNo room records logged this session."); return; }
            string dwgPath = db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string csvPath = dir + $"RoomSchedule-{UnitDisplayName}.csv";
            var rows = Log.OrderBy(r => r.RoomType).Select(r => (IEnumerable<string>)new[]
            {
                r.RoomType, r.Width.ToString("F3"), r.Height.ToString("F3"), r.AreaValue.ToString("F3"), r.X.ToString("F3"), r.Y.ToString("F3")
            });
            Util.WriteCsv(csvPath, new[] { "RoomType", "Width", "Height", $"Area_{AreaUnitLabel}", "InsX", "InsY" }, rows);
            ed.WriteMessage($"\nMSCHEDULE: {Log.Count} label(s) (this session) exported to {csvPath}");
        }

        public void Total(Editor ed)
        {
            var r = ed.GetString("\nRoom type filter (e.g. BEDROOM, or Enter for all): ");
            string filter = r.Status == PromptStatus.OK ? (r.StringResult ?? "") : "";
            if (Log.Count == 0) { ed.WriteMessage("\nNo room records logged this session."); return; }
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

    public class RoomEngineMetric : RoomEngine
    {
        public override string SystemTag => "M";
        public override string HeightPromptUnit => "metres";
        public override double DefaultTextHeight => 0.15;
        public override string DefaultLabelLayer => "ROOM-LABELS-M";
        public override string DefaultRectLayer => "ROOM-RECT-M";
        public override string AreaUnitLabel => "m2";
        public override double ToDisplayLength(double raw) => raw; // 1 drawing unit = 1 metre
        public override double AreaValue(double w, double h) => w * h;
        protected override double AreaValueFromRaw(double rawArea) => rawArea;
        public override string FormatDim(double w, double h) => $"{w:F2} m x {h:F2} m";
    }

    public class RoomEngineFeet : RoomEngine
    {
        public override string SystemTag => "F";
        public override string HeightPromptUnit => "feet";
        public override double DefaultTextHeight => 0.5;
        public override string DefaultLabelLayer => "ROOM-LABELS-F";
        public override string DefaultRectLayer => "ROOM-RECT-F";
        public override string AreaUnitLabel => "sq ft";

        // Drawings set up this way are very often actually modeled in inches
        // (Insunits = Inches) even though the user works "in feet" - assuming
        // 1 drawing unit = 1 foot unconditionally silently inflated every
        // dimension/area by 12x on those drawings. Check the real unit first.
        private static bool DrawingIsInInches => Util.Db.Insunits == Autodesk.AutoCAD.DatabaseServices.UnitsValue.Inches;
        private static double RawToFeet(double raw) => DrawingIsInInches ? raw / 12.0 : raw;

        public override double ToDisplayLength(double raw) => RawToFeet(raw);
        public override double AreaValue(double w, double h) => RawToFeet(w) * RawToFeet(h);
        protected override double AreaValueFromRaw(double rawArea) => DrawingIsInInches ? rawArea / 144.0 : rawArea;

        // decimal feet -> architectural  Ft'-In"
        private static string ArchDim(double decimalFeet)
        {
            int totalIn = (int)(Math.Abs(decimalFeet) * 12.0 + 0.5);
            int ft = totalIn / 12, inch = totalIn % 12;
            return $"{ft}'-{inch}\"";
        }
        public override string FormatDim(double w, double h) => $"{ArchDim(RawToFeet(w))} x {ArchDim(RawToFeet(h))}";
    }

    public class RoomEngineInches : RoomEngine
    {
        public override string SystemTag => "I";
        public override string HeightPromptUnit => "inches";
        public override double DefaultTextHeight => 6.0; // 6" tall labels - was 0.15 (a leftover metric-scale value, illegible on an inches drawing)
        public override string DefaultLabelLayer => "ROOM-LABELS-I";
        public override string DefaultRectLayer => "ROOM-RECT-I";
        public override string AreaUnitLabel => "sq ft";
        public override double ToDisplayLength(double raw) => raw / 12.0; // inches -> feet
        public override double AreaValue(double w, double h) => (w / 12.0) * (h / 12.0); // raw inches -> sq ft
        protected override double AreaValueFromRaw(double rawArea) => rawArea / 144.0; // sq inches -> sq ft

        // raw inches value -> architectural Ft'-In"
        private static string ArchDim(double inchesValue)
        {
            int totalIn = (int)(Math.Abs(inchesValue) + 0.5);
            int ft = totalIn / 12, inch = totalIn % 12;
            return $"{ft}'-{inch}\"";
        }
        public override string FormatDim(double w, double h) => $"{ArchDim(w)} x {ArchDim(h)}";
    }

    /// <summary>
    /// Room-label engine for the hcwCAD-KIT ribbon.
    /// It reads the current drawing's INSUNITS every
    /// time and scales accordingly (mm/cm/m/ft/in), so there's nothing to
    /// get out of sync with the drawing itself. The typed M-/F-/I- commands
    /// keep using their own fixed engines unchanged.
    /// </summary>
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

        public override string DefaultLabelLayer => "ROOM-LABELS";
        public override string DefaultRectLayer => "ROOM-RECT";

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
