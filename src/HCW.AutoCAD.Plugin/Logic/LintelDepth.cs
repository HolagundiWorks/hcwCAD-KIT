using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// The depth of a lintel by the width of the opening it spans, from a table such as "1219.2=152.4; 1828.8=228.6; 3048=304.8"
    /// (opening up to this width in mm = lintel depth in mm). The default is 4 ft = 6 in, 6 ft = 9 in, 10 ft = 1 ft.
    /// </summary>
    public static class LintelDepth
    {
        public const string Default = "1219.2=152.4; 1828.8=228.6; 3048=304.8";

        public class Step { public double UpTo, Depth; }

        public static List<Step> Parse(string text)
        {
            var list = new List<Step>();
            foreach (var row in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = row.Split('=');
                double up, depth;
                if (p.Length != 2 || !double.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out up)
                    || !double.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out depth) || up <= 0 || depth <= 0) continue;
                list.Add(new Step { UpTo = up, Depth = depth });
            }
            return list.OrderBy(s => s.UpTo).ToList();
        }

        /// <summary>
        /// The lintel depth for an opening of this width: the first step whose limit holds it (a width exactly on a limit takes that step).
        /// A wider opening than the table covers gets the deepest step and <paramref name="beyond"/> is true. Zero when the table is empty.
        /// </summary>
        public static double For(double openingMm, IList<Step> table, out bool beyond)
        {
            beyond = false;
            if (table == null || table.Count == 0) return 0;
            foreach (var s in table)
                if (openingMm <= s.UpTo + 1e-6) return s.Depth;
            beyond = true;
            return table[table.Count - 1].Depth;
        }
    }

    /// <summary>One lintel as drawn: its length, the wall thickness it runs through and its depth, all in millimetres.</summary>
    public class LintelLine
    {
        public double OpeningMm, LengthMm, ThicknessMm, DepthMm;
        public int Count = 1;
    }

    /// <summary>Concrete and shuttering of lintels: length x wall thickness x depth; shuttering is the soffit and both sides.</summary>
    public static class LintelQuantity
    {
        public static double Concrete(LintelLine l) => l.LengthMm * l.ThicknessMm * l.DepthMm * Math.Max(1, l.Count) / 1e9;
        public static double Shuttering(LintelLine l) => l.LengthMm * (l.ThicknessMm + 2 * l.DepthMm) * Math.Max(1, l.Count) / 1e6;

        public static string[] Headers => new[] { "Mark", "Opening (mm)", "Length (mm)", "Wall (mm)", "Depth (mm)", "Nos", "Concrete (m3)", "Shuttering (m2)" };

        /// <summary>Lintels of the same opening, length, wall and depth share a mark (LT1, LT2 ...), widest opening first.</summary>
        public static List<string[]> Rows(IEnumerable<LintelLine> lintels)
        {
            var ci = CultureInfo.InvariantCulture;
            var groups = lintels
                .GroupBy(l => Math.Round(l.OpeningMm) + "|" + Math.Round(l.LengthMm) + "|" + Math.Round(l.ThicknessMm) + "|" + Math.Round(l.DepthMm, 1))
                .Select(g => new LintelLine { OpeningMm = g.First().OpeningMm, LengthMm = g.First().LengthMm, ThicknessMm = g.First().ThicknessMm, DepthMm = g.First().DepthMm, Count = g.Sum(x => Math.Max(1, x.Count)) })
                .OrderByDescending(l => l.OpeningMm).ThenByDescending(l => l.ThicknessMm).ToList();
            var rows = new List<string[]>();
            double conc = 0, shut = 0; int nos = 0, n = 0;
            foreach (var l in groups)
            {
                double c = Concrete(l), s = Shuttering(l);
                rows.Add(new[] { "LT" + (++n), l.OpeningMm.ToString("0", ci), l.LengthMm.ToString("0", ci), l.ThicknessMm.ToString("0", ci), l.DepthMm.ToString("0.#", ci), l.Count.ToString(ci), c.ToString("0.000", ci), s.ToString("0.00", ci) });
                conc += c; shut += s; nos += l.Count;
            }
            if (rows.Count > 0) rows.Add(new[] { "TOTAL", "", "", "", "", nos.ToString(ci), conc.ToString("0.000", ci), shut.ToString("0.00", ci) });
            return rows;
        }
    }
}
