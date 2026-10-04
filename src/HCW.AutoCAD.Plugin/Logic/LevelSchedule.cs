using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A schedule of the levels marked on a drawing. No CAD types are used here.</summary>
    public static class LevelSchedule
    {
        public class Row
        {
            public string Level = "";
            public double? Value;
            public int Marks;
        }

        /// <summary>Reads a level as drawn (+3.150, -0.450, ±0.000, 3.15) into metres, or null when it is not a number.</summary>
        public static double? Parse(string text)
        {
            string s = (text ?? "").Trim().Replace("±", "").Replace("+-", "").Replace("−", "-");
            if (s.Length == 0) return (text ?? "").Contains("±") ? (double?)0 : null;
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : (double?)null;
        }

        /// <summary>One row for each distinct level text, highest first, with the number of marks. Texts that are not numbers come last.</summary>
        public static List<Row> Build(IEnumerable<string> levelTexts)
        {
            var rows = levelTexts
                .Select(t => (t ?? "").Trim())
                .Where(t => t.Length > 0)
                .GroupBy(t => t)
                .Select(g => new Row { Level = g.Key, Value = Parse(g.Key), Marks = g.Count() });
            return rows.OrderBy(r => r.Value.HasValue ? 0 : 1)
                       .ThenByDescending(r => r.Value ?? 0)
                       .ThenBy(r => r.Level, StringComparer.Ordinal).ToList();
        }
    }
}
