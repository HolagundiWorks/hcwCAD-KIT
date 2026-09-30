using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using HCW.AutoCAD.Plugin.Commands;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>
    /// Floors, opening schedule, column schedule, and deduction-name map,
    /// stored in the drawing so the next measure pass can reuse them.
    /// </summary>
    public class MeasureBook
    {
        public const string DictName = "HCW_MEASURE";
        private const string RecordName = "BOOK";

        public List<FloorSpec> Floors = new List<FloorSpec>();
        public List<OpeningSpec> Openings = new List<OpeningSpec>();
        public List<ColumnSpec> Columns = new List<ColumnSpec>();
        public List<DeductionMap> Maps = new List<DeductionMap>();
        public List<RateSpec> Rates = new List<RateSpec>();

        /// <summary>A price for one take-off (matched on its name, for example WallPaint), used in the Excel bill.</summary>
        public class RateSpec
        {
            public string Takeoff = "";
            public string Unit = "";
            public double Rate;
        }

        public string MarkFor(string rawLabel)
        {
            string key = NormKey(rawLabel);
            var map = Maps.FirstOrDefault(m => NormKey(m.Label) == key);
            return map == null ? null : map.Mark;
        }

        public static string NormKey(string label) => HCW.AutoCAD.Plugin.Logic.NameMatch.NormKey(label);

        /// <summary>
        /// The schedule entry closest in length to a measured deduction, within the tolerance
        /// (50 mm, or 2 in when Imperial). Null when nothing is in range or two entries are equally close.
        /// Used to pre-fill the deduction map.
        /// </summary>
        public OpeningSpec SuggestOpening(int measuredLength)
        {
            int at = HCW.AutoCAD.Plugin.Logic.LengthMatch.Closest(
                Openings.Select(o => o.WidthRounded).ToList(), measuredLength, MeasureCommands.SuggestTolerance);
            return at < 0 ? null : Openings[at];
        }

        /// <summary>The schedule entry a drawing block is mapped to (matched on the block's effective name).</summary>
        public OpeningSpec OpeningForBlock(string blockName)
        {
            if (string.IsNullOrWhiteSpace(blockName)) return null;
            return Openings.FirstOrDefault(o => (o.BlockName ?? "")
                .Split(';').Any(b => b.Trim().Length > 0 && string.Equals(b.Trim(), blockName.Trim(), StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Adds or replaces the map entry for a deduction name.</summary>
        public void SetMap(string label, string mark)
        {
            string key = NormKey(label);
            var map = Maps.FirstOrDefault(m => NormKey(m.Label) == key);
            if (map == null) Maps.Add(new DeductionMap { Label = label, Mark = mark });
            else map.Mark = mark;
        }

        /// <summary>Sets each mapped opening's count to the number of deductions mapped to it.</summary>
        public void CountFromMaps()
        {
            foreach (var o in Openings)
            {
                int n = Maps.Count(m => string.Equals(m.Mark, o.Mark, StringComparison.OrdinalIgnoreCase));
                if (n > 0) o.Count = n;
            }
        }

        public OpeningSpec Opening(string mark)
        {
            return Openings.FirstOrDefault(o => string.Equals(o.Mark, mark, StringComparison.OrdinalIgnoreCase));
        }

        public ColumnSpec ColumnBySize(int sideA, int sideB)
        {
            return Columns.FirstOrDefault(c =>
                (c.WidthRounded == sideA && c.DepthRounded == sideB) ||
                (c.WidthRounded == sideB && c.DepthRounded == sideA));
        }

        public static MeasureBook Load(Transaction tr, Database db)
        {
            var book = new MeasureBook();
            foreach (var line in ReadLines(tr, db, RecordName))
                Parse(book, line);
            return book;
        }

        public void Save(Transaction tr, Database db)
        {
            WriteLines(tr, db, RecordName, Serialize());
        }

        private static IEnumerable<string> ReadLines(Transaction tr, Database db, string record)
            => DrawingStore.Read(tr, db, DictName, record);

        private static void WriteLines(Transaction tr, Database db, string record, IEnumerable<string> lines)
            => DrawingStore.Write(tr, db, DictName, record, lines);

        /// <summary>The most recent take-off result, kept in the drawing so MEXPORT works after a restart.</summary>
        public class Takeoff
        {
            public string Name = "";
            public string[] Headers = new string[0];
            public List<string[]> Rows = new List<string[]>();
        }

        private const string TakeoffPrefix = "TO_";
        private const string LastTakeoff = "TO_LAST";

        /// <summary>Stores a take-off under its name (a repeat run replaces it) and remembers it as the latest.</summary>
        public static void SaveTakeoff(Transaction tr, Database db, string name, string[] headers, List<string[]> rows)
        {
            var lines = new List<string> { "N|" + Esc(name), "H|" + string.Join("|", headers.Select(Esc)) };
            foreach (var r in rows)
                lines.Add("R|" + string.Join("|", r.Select(Esc)));
            WriteLines(tr, db, TakeoffPrefix + name, lines);
            WriteLines(tr, db, LastTakeoff, new[] { name });
        }

        /// <summary>The most recent take-off, or null when none was saved.</summary>
        public static Takeoff LoadTakeoff(Transaction tr, Database db)
        {
            foreach (var name in ReadLines(tr, db, LastTakeoff))
                if (!string.IsNullOrEmpty(name) && name != "EMPTY")
                    return LoadTakeoff(tr, db, name);
            return null;
        }

        public static Takeoff LoadTakeoff(Transaction tr, Database db, string name)
        {
            var t = new Takeoff();
            foreach (var line in ReadLines(tr, db, TakeoffPrefix + name))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Split(line);
                if (p[0] == "N" && p.Length > 1) t.Name = p[1];
                else if (p[0] == "H") t.Headers = p.Skip(1).ToArray();
                else if (p[0] == "R") t.Rows.Add(p.Skip(1).ToArray());
            }
            return t.Rows.Count == 0 && t.Headers.Length == 0 ? null : t;
        }

        /// <summary>Every saved take-off, in name order.</summary>
        public static List<Takeoff> LoadAllTakeoffs(Transaction tr, Database db)
        {
            var all = new List<Takeoff>();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return all;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            var names = new List<string>();
            foreach (DBDictionaryEntry entry in dict)
                if (entry.Key.StartsWith(TakeoffPrefix, StringComparison.Ordinal) && entry.Key != LastTakeoff)
                    names.Add(entry.Key.Substring(TakeoffPrefix.Length));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var t = LoadTakeoff(tr, db, name);
                if (t != null) all.Add(t);
            }
            return all;
        }

        public void GroupSameSizes()
        {
            Openings = Collapse(Openings, o => o.Kind + "|" + (o.Type ?? "").Trim().ToLowerInvariant() + "|" + o.WidthRounded + "|" + o.HeightRounded);
            Columns = CollapseColumns(Columns);
        }

        private static List<OpeningSpec> Collapse(List<OpeningSpec> rows, Func<OpeningSpec, string> key)
        {
            var kept = new List<OpeningSpec>();
            foreach (var group in rows.GroupBy(key))
            {
                var first = group.First();
                first.Count = group.Sum(o => Math.Max(1, o.Count));
                kept.Add(first);
            }
            return kept;
        }

        private static List<ColumnSpec> CollapseColumns(List<ColumnSpec> rows)
        {
            var kept = new List<ColumnSpec>();
            foreach (var group in rows.GroupBy(c => Math.Min(c.WidthRounded, c.DepthRounded) + "|" + Math.Max(c.WidthRounded, c.DepthRounded)))
            {
                var first = group.First();
                first.Count = group.Sum(c => Math.Max(1, c.Count));
                kept.Add(first);
            }
            return kept;
        }

        private IEnumerable<string> Serialize()
        {
            foreach (var f in Floors)
                yield return "F|" + Esc(f.Name) + "|" + Num(f.Height) + "|" + Num(f.FflHeight) + "|" + Num(f.LintelBottom);
            foreach (var o in Openings)
                yield return "O|" + Esc(o.Mark) + "|" + Esc(o.Kind) + "|" + Num(o.Width) + "|" + Num(o.Height) + "|" + Esc(o.Type) + "|" + o.Count.ToString(CultureInfo.InvariantCulture) + "|" + Num(o.LintelBottom) + "|" + Num(o.Sill) + "|" + Esc(o.BlockName);
            foreach (var c in Columns)
                yield return "C|" + Esc(c.Mark) + "|" + Num(c.Width) + "|" + Num(c.Depth) + "|" + Esc(c.Name) + "|" + c.Count.ToString(CultureInfo.InvariantCulture);
            foreach (var m in Maps)
                yield return "M|" + Esc(m.Label) + "|" + Esc(m.Mark);
            foreach (var r in Rates)
                yield return "P|" + Esc(r.Takeoff) + "|" + Esc(r.Unit) + "|" + Num(r.Rate);
        }

        private static void Parse(MeasureBook book, string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line == "EMPTY") return;
            var p = Split(line);
            if (p.Length < 2) return;
            if (p[0] == "F" && p.Length >= 3)
                book.Floors.Add(new FloorSpec { Name = p[1], Height = D(p[2]), FflHeight = p.Length > 3 ? D(p[3]) : 0, LintelBottom = p.Length > 4 ? D(p[4]) : 0 });
            else if (p[0] == "O" && p.Length >= 7)
                book.Openings.Add(new OpeningSpec { Mark = p[1], Kind = p[2], Width = D(p[3]), Height = D(p[4]), Type = p[5], Count = I(p[6]), LintelBottom = p.Length > 7 ? D(p[7]) : 0, Sill = p.Length > 8 ? D(p[8]) : 0, BlockName = p.Length > 9 ? p[9] : "" });
            else if (p[0] == "C" && p.Length >= 6)
                book.Columns.Add(new ColumnSpec { Mark = p[1], Width = D(p[2]), Depth = D(p[3]), Name = p[4], Count = I(p[5]) });
            else if (p[0] == "P" && p.Length >= 4)
                book.Rates.Add(new RateSpec { Takeoff = p[1], Unit = p[2], Rate = D(p[3]) });
            else if (p[0] == "M" && p.Length >= 3)
                book.Maps.Add(new DeductionMap { Label = p[1], Mark = p[2] });
        }

        private static string Esc(string s) => HCW.AutoCAD.Plugin.Logic.Fields.Escape(s);
        private static string[] Split(string line) => HCW.AutoCAD.Plugin.Logic.Fields.Split(line);

        private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static double D(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
        private static int I(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 1;
        }

        public class FloorSpec
        {
            public string Name = "Ground";
            /// <summary>Ceiling height above the finished floor level (FFL). Wall paint uses it.</summary>
            public double Height = Settings.GetDouble("DefaultCeilingHeight", 3.0);
            /// <summary>Finished floor level to the next finished floor level (FFL to FFL).</summary>
            public double FflHeight = Settings.GetDouble("DefaultFflHeight", 3.15);
            /// <summary>Height of the underside of the lintel above the FFL.</summary>
            public double LintelBottom = Settings.GetDouble("DefaultLintelBottom", 2.1);
        }

        /// <summary>Choices offered in the schedule for each kind of opening.</summary>
        public static readonly string[] DoorTypes = { "Wood", "Flush door", "UPVC", "Aluminium", "WPC", "Fabricated" };
        public static readonly string[] WindowTypes = { "Wood", "UPVC", "Aluminium", "System aluminium" };
        public static string[] TypesFor(string kind) =>
            string.Equals(kind, "Window", StringComparison.OrdinalIgnoreCase) ? WindowTypes : DoorTypes;

        public class OpeningSpec
        {
            /// <summary>Schedule name, for example D1 or W1.</summary>
            public string Mark = "D1";
            public string Kind = "Door";
            /// <summary>Length of the opening along the wall (the deduction length).</summary>
            public double Width = 0.9;
            public double Height = 2.1;
            /// <summary>Material or type, for example UPVC, Timber, Aluminium.</summary>
            public string Type = "";
            public int Count = 1;
            /// <summary>Lintel bottom height for this opening only. 0 means use the floor's lintel bottom height.</summary>
            public double LintelBottom = 0;
            /// <summary>Sill height above the FFL (windows). 0 when not recorded.</summary>
            public double Sill = 0;
            /// <summary>Drawing block names that stand for this entry, separated by semicolons.</summary>
            public string BlockName = "";
            public int WidthRounded => MeasureCommands.RndSchedule(Width);
            public int HeightRounded => MeasureCommands.RndSchedule(Height);
        }

        public class ColumnSpec
        {
            public string Mark = "C1";
            public double Width = 0.23;
            public double Depth = 0.45;
            public string Name = "Column";
            public int Count = 1;
            public int WidthRounded => MeasureCommands.RndSchedule(Width);
            public int DepthRounded => MeasureCommands.RndSchedule(Depth);
        }

        public class DeductionMap
        {
            public string Label = "";
            public string Mark = "";
        }
    }
}
