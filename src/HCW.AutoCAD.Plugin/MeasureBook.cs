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

        public string MarkFor(string rawLabel)
        {
            var map = Maps.FirstOrDefault(m => string.Equals(m.Label, rawLabel, StringComparison.OrdinalIgnoreCase));
            return map == null ? null : map.Mark;
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
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictName)) return book;
            var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
            if (!dict.Contains(RecordName)) return book;
            var xr = tr.GetObject(dict.GetAt(RecordName), OpenMode.ForRead) as Xrecord;
            if (xr?.Data == null) return book;
            foreach (TypedValue tv in xr.Data)
            {
                if (tv.TypeCode != (int)DxfCode.Text) continue;
                Parse(book, tv.Value as string);
            }
            return book;
        }

        public void Save(Transaction tr, Database db)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary dict;
            if (nod.Contains(DictName))
                dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
            else
            {
                nod.UpgradeOpen();
                dict = new DBDictionary();
                nod.SetAt(DictName, dict);
                tr.AddNewlyCreatedDBObject(dict, true);
            }

            var data = new ResultBuffer();
            foreach (var line in Serialize())
                data.Add(new TypedValue((int)DxfCode.Text, line));
            if (data.AsArray().Length == 0)
                data.Add(new TypedValue((int)DxfCode.Text, "EMPTY"));

            if (dict.Contains(RecordName))
            {
                var xr = (Xrecord)tr.GetObject(dict.GetAt(RecordName), OpenMode.ForWrite);
                xr.Data = data;
            }
            else
            {
                var xr = new Xrecord { Data = data };
                dict.SetAt(RecordName, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }
        }

        public void GroupSameSizes()
        {
            Openings = Collapse(Openings, o => o.Kind + "|" + o.WidthRounded + "|" + o.HeightRounded);
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
                yield return "F|" + Esc(f.Name) + "|" + Num(f.Height);
            foreach (var o in Openings)
                yield return "O|" + Esc(o.Mark) + "|" + Esc(o.Kind) + "|" + Num(o.Width) + "|" + Num(o.Height) + "|" + Esc(o.Name) + "|" + o.Count.ToString(CultureInfo.InvariantCulture);
            foreach (var c in Columns)
                yield return "C|" + Esc(c.Mark) + "|" + Num(c.Width) + "|" + Num(c.Depth) + "|" + Esc(c.Name) + "|" + c.Count.ToString(CultureInfo.InvariantCulture);
            foreach (var m in Maps)
                yield return "M|" + Esc(m.Label) + "|" + Esc(m.Mark);
        }

        private static void Parse(MeasureBook book, string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line == "EMPTY") return;
            var p = Split(line);
            if (p.Length < 2) return;
            if (p[0] == "F" && p.Length >= 3)
                book.Floors.Add(new FloorSpec { Name = p[1], Height = D(p[2]) });
            else if (p[0] == "O" && p.Length >= 7)
                book.Openings.Add(new OpeningSpec { Mark = p[1], Kind = p[2], Width = D(p[3]), Height = D(p[4]), Name = p[5], Count = I(p[6]) });
            else if (p[0] == "C" && p.Length >= 6)
                book.Columns.Add(new ColumnSpec { Mark = p[1], Width = D(p[2]), Depth = D(p[3]), Name = p[4], Count = I(p[5]) });
            else if (p[0] == "M" && p.Length >= 3)
                book.Maps.Add(new DeductionMap { Label = p[1], Mark = p[2] });
        }

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("|", "\\p");
        private static string[] Split(string line)
        {
            var parts = new List<string>();
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    char n = line[++i];
                    cur.Append(n == 'p' ? '|' : n);
                }
                else if (line[i] == '|')
                {
                    parts.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(line[i]);
            }
            parts.Add(cur.ToString());
            return parts.ToArray();
        }

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
            public double Height = 3.0;
        }

        public class OpeningSpec
        {
            public string Mark = "D1";
            public string Kind = "Door";
            public double Width = 0.9;
            public double Height = 2.1;
            public string Name = "Door 01";
            public int Count = 1;
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
