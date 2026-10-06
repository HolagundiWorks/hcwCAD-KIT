using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A saved take-off by name, as the quantity summary reads it.</summary>
    public class SavedTakeoff
    {
        public string Name = "";
        public string[] Headers = new string[0];
        public List<string[]> Rows = new List<string[]>();
    }

    /// <summary>Concrete and shuttering of the structural elements (stairs, columns) added into one table, from the take-offs saved in the drawing.</summary>
    public static class QuantitySummary
    {
        public static string[] Headers => new[] { "Element", "Take-offs", "Concrete (m3)", "Shuttering (m2)" };

        private static double Num(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        public static List<string[]> Build(IEnumerable<SavedTakeoff> takeoffs)
        {
            var ci = CultureInfo.InvariantCulture;
            double stairC = 0, stairS = 0, colC = 0, colS = 0, linC = 0, linS = 0; int stairs = 0, cols = 0, lins = 0;
            foreach (var t in takeoffs)
            {
                if (t.Name.StartsWith("Stair ", StringComparison.OrdinalIgnoreCase) && !t.Name.EndsWith(" bars", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var r in t.Rows)
                    {
                        if (r.Length < 2) continue;
                        if (string.Equals(r[0], "CONCRETE TOTAL", StringComparison.OrdinalIgnoreCase)) stairC += Num(r[1]);
                        else if (string.Equals(r[0], "SHUTTERING TOTAL", StringComparison.OrdinalIgnoreCase)) stairS += Num(r[1]);
                    }
                    stairs++;
                }
                else if (string.Equals(t.Name, "Lintels", StringComparison.OrdinalIgnoreCase))
                {
                    int cI = Array.FindIndex(t.Headers, h => h.StartsWith("Concrete", StringComparison.OrdinalIgnoreCase));
                    int sI = Array.FindIndex(t.Headers, h => h.StartsWith("Shuttering", StringComparison.OrdinalIgnoreCase));
                    var total = t.Rows.LastOrDefault(r => r.Length > 0 && string.Equals(r[0], "TOTAL", StringComparison.OrdinalIgnoreCase));
                    if (total != null && cI >= 0 && cI < total.Length) linC += Num(total[cI]);
                    if (total != null && sI >= 0 && sI < total.Length) linS += Num(total[sI]);
                    lins++;
                }
                else if (string.Equals(t.Name, "Columns", StringComparison.OrdinalIgnoreCase))
                {
                    int ci2 = Array.FindIndex(t.Headers, h => h.StartsWith("Concrete", StringComparison.OrdinalIgnoreCase));
                    int si = Array.FindIndex(t.Headers, h => h.StartsWith("Shuttering", StringComparison.OrdinalIgnoreCase));
                    var total = t.Rows.LastOrDefault(r => r.Length > 0 && string.Equals(r[0], "TOTAL", StringComparison.OrdinalIgnoreCase));
                    if (total != null && ci2 >= 0 && ci2 < total.Length) colC += Num(total[ci2]);
                    if (total != null && si >= 0 && si < total.Length) colS += Num(total[si]);
                    cols++;
                }
            }
            var rows = new List<string[]>();
            if (stairs > 0) rows.Add(new[] { "Stairs", stairs.ToString(ci), stairC.ToString("0.000", ci), stairS.ToString("0.00", ci) });
            if (cols > 0) rows.Add(new[] { "Columns", cols.ToString(ci), colC.ToString("0.000", ci), colS.ToString("0.00", ci) });
            if (lins > 0) rows.Add(new[] { "Lintels", lins.ToString(ci), linC.ToString("0.000", ci), linS.ToString("0.00", ci) });
            if (rows.Count > 0) rows.Add(new[] { "TOTAL", "", (stairC + colC + linC).ToString("0.000", ci), (stairS + colS + linS).ToString("0.00", ci) });
            return rows;
        }
    }
}
