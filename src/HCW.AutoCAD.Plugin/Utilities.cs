using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Colors;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>
    /// Shared helpers used across every command module - the C# equivalent of
    /// Section 1 (hcw:*) in the merged HCW-ALL.lsp file: layer/linetype
    /// creation, string padding, CSV writing, and the "current document /
    /// active transaction" boilerplate every command needs.
    /// </summary>
    public static class Util
    {
        public static Document Doc => AcAp.DocumentManager.MdiActiveDocument;
        public static Database Db => Doc.Database;
        public static Editor Ed => Doc.Editor;

        /// <summary>Left/right pad a string to a fixed width (hcw:pad).</summary>
        public static string Pad(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s : s + new string(' ', width - s.Length);
        }

        /// <summary>
        /// Ensure a layer exists with the given colour/linetype/lineweight,
        /// creating or updating it as needed. Equivalent of hcw:make-layer.
        /// </summary>
        public static ObjectId EnsureLayer(Transaction tr, Database db, string name,
            short colorIndex, string linetype = "Continuous", LineWeight lw = LineWeight.LineWeight000,
            bool plottable = true)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            ObjectId ltypeId = LoadLinetype(tr, db, linetype);

            if (lt.Has(name))
            {
                var existing = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                existing.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                if (ltypeId != ObjectId.Null) existing.LinetypeObjectId = ltypeId;
                existing.LineWeight = lw;
                existing.IsPlottable = plottable;
                return existing.ObjectId;
            }

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                LineWeight = lw,
                IsPlottable = plottable
            };
            if (ltypeId != ObjectId.Null) ltr.LinetypeObjectId = ltypeId;

            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        /// <summary>Load a linetype from acad.lin if it isn't already loaded (hcw:load-lt).</summary>
        public static ObjectId LoadLinetype(Transaction tr, Database db, string name)
        {
            if (string.Equals(name, "Continuous", StringComparison.OrdinalIgnoreCase))
            {
                var ltTbl0 = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                return ltTbl0.Has("Continuous") ? ltTbl0["Continuous"] : ObjectId.Null;
            }

            var ltTbl = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltTbl.Has(name)) return ltTbl[name];

            try
            {
                db.LoadLineTypeFile(name, "acad.lin");
            }
            catch
            {
                try { db.LoadLineTypeFile(name, "acadiso.lin"); } catch { /* leave unloaded */ }
            }

            ltTbl = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            return ltTbl.Has(name) ? ltTbl[name] : ObjectId.Null;
        }

        /// <summary>
        /// The layer name an item is drawn on. With the setting LayerOutput at HCW (the default) items keep the HCW name (A-WALL ...); with BP items that have a
        /// building permit layer go on it, as the map BpLayerMap says. <paramref name="permitName"/> is the permit layer for an item the map does not list.
        /// </summary>
        public static string Out(string hcwName, string permitName = null) =>
            Logic.LayerRoles.Resolve(hcwName, string.Equals(Settings.Get("LayerOutput", "HCW"), "BP", StringComparison.OrdinalIgnoreCase),
                Logic.LayerRoles.Parse(Settings.Get("BpLayerMap", Logic.LayerRoles.DefaultMap)), permitName);

        /// <summary>Creates a layer from the HCW Layer Standard or the building permit table (colour, linetype, lineweight), or colour 7 when the name is in neither.</summary>
        public static ObjectId EnsureHcwLayer(Transaction tr, Database db, string name)
        {
            foreach (var l in LayerData.Hcw)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                    return EnsureLayer(tr, db, l.Name, (short)l.Aci, l.Linetype, MmToLineWeight(l.LwMm));
            foreach (var l in LayerData.Bplt)
                if (string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                    return EnsureLayer(tr, db, l.Name, (short)l.Aci, l.Linetype, MmToLineWeight(l.LwHundredthsMm / 100.0));
            return EnsureLayer(tr, db, name, 7);
        }

        /// <summary>Millimetre lineweight value -> nearest AutoCAD LineWeight enum member.</summary>
        public static LineWeight MmToLineWeight(double mm)
        {
            int[] steps = { 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211 };
            int hundredths = (int)Math.Round(mm * 100.0);
            int best = steps[0];
            int bestDiff = Math.Abs(hundredths - steps[0]);
            foreach (var s in steps)
            {
                int diff = Math.Abs(hundredths - s);
                if (diff < bestDiff) { best = s; bestDiff = diff; }
            }
            return (LineWeight)best;
        }

        /// <summary>Write a simple CSV file (header + rows), each cell escaped like hcw's m:q / m:csvline.</summary>
        public static void WriteCsv(string path, IEnumerable<string> headers, IEnumerable<IEnumerable<string>> rows)
        {
            using (var f = new StreamWriter(path, false))
            {
                f.WriteLine(CsvLine(headers));
                foreach (var r in rows) f.WriteLine(CsvLine(r));
            }
        }

        private static string CsvLine(IEnumerable<string> cells)
        {
            var parts = new List<string>();
            foreach (var c in cells)
            {
                string s = c ?? "";
                bool isNumeric = IsPlainNumber(s);
                parts.Add(isNumeric ? s : Quote(s));
            }
            return string.Join(",", parts);
        }

        private static bool IsPlainNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (!(char.IsDigit(c) || c == '.' || c == '-')) return false;
            return true;
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        public static void Info(string msg) => Ed.WriteMessage("\n" + msg);

        /// <summary>
        /// Asks which of several related jobs one command should do, as a keyword prompt that Enter answers with the first. Returns the
        /// keyword, or null when the user cancels. Lets one button stand for a family of commands that used to be separate.
        /// </summary>
        public static string AskMode(string what, params string[] modes)
        {
            var o = new PromptKeywordOptions("\n" + what + " [" + string.Join("/", modes) + "] <" + modes[0] + ">: ", string.Join(" ", modes)) { AllowNone = true };
            o.Keywords.Default = modes[0];
            var r = Ed.GetKeywords(o);
            if (r.Status == PromptStatus.None) return modes[0];
            return r.Status == PromptStatus.OK ? r.StringResult : null;
        }

        /// <summary>
        /// A real-world length in millimetres -> the current drawing's raw
        /// unit, based on its actual INSUNITS setting. Used anywhere a
        /// command needs a plot-correct physical size (text height, arrow
        /// size, ...) without assuming what unit the drawing happens to be
        /// in - the single source of truth for that conversion.
        /// </summary>
        public static double MmToDrawingUnits(double mm)
        {
            switch (Db.Insunits)
            {
                case UnitsValue.Millimeters: return mm;
                case UnitsValue.Centimeters: return mm / 10.0;
                case UnitsValue.Feet: return mm / 304.8;
                case UnitsValue.Inches: return mm / 25.4;
                case UnitsValue.Meters:
                default: return mm / 1000.0;
            }
        }

        /// <summary>Isolate a set of layers (turn everything else off), returning the state to restore.</summary>
        public static LayerIsolation IsolateLayers(Transaction tr, Database db, IEnumerable<string> keep)
        {
            var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
            var state = new LayerIsolation
            {
                PreviouslyOff = new List<string>(),
                Hidden = new List<string>()
            };
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            var records = new List<LayerTableRecord>();
            ObjectId fallback = ObjectId.Null;
            string currentName = null;
            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                records.Add(ltr);
                if (id == db.Clayer) currentName = ltr.Name;
                if (fallback.IsNull && keepSet.Contains(ltr.Name)) fallback = id;
            }
            // The current layer cannot be turned off. Move to a kept layer first.
            if (currentName != null && !keepSet.Contains(currentName) && !fallback.IsNull)
                db.Clayer = fallback;

            foreach (var ltr in records)
            {
                bool shown = keepSet.Contains(ltr.Name);
                if (ltr.IsOff) state.PreviouslyOff.Add(ltr.Name);
                else if (!shown) state.Hidden.Add(ltr.Name);
                if (ltr.ObjectId != db.Clayer)
                    ltr.IsOff = !shown;
            }
            return state;
        }

        public static void RestoreLayers(Transaction tr, Database db, LayerIsolation state)
        {
            if (state == null) return;
            var offSet = new HashSet<string>(state.PreviouslyOff, StringComparer.OrdinalIgnoreCase);
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                ltr.IsOff = offSet.Contains(ltr.Name);
            }
        }
    }

    public class LayerIsolation
    {
        public List<string> PreviouslyOff;
        /// <summary>Layers that were on and that this isolation turned off.</summary>
        public List<string> Hidden;
    }
}
