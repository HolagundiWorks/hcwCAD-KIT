using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>One line of the column schedule, in millimetres. Depth of 0 (or Round) is a circular column of diameter Width.</summary>
    public class ColumnLine
    {
        public string Mark = "C1";
        public double WidthMm, DepthMm, HeightMm;
        public int Count = 1;
        public bool Round;
        public string Floor = "";
    }

    /// <summary>Concrete and shuttering of columns from the schedule: section x height x number.</summary>
    public static class ColumnQuantity
    {
        public static double Area(ColumnLine c) => c.Round || c.DepthMm <= 0 ? Math.PI * c.WidthMm * c.WidthMm / 4 : c.WidthMm * c.DepthMm;
        public static double Perimeter(ColumnLine c) => c.Round || c.DepthMm <= 0 ? Math.PI * c.WidthMm : 2 * (c.WidthMm + c.DepthMm);

        /// <summary>Concrete in cubic metres for the whole line (count included).</summary>
        public static double Concrete(ColumnLine c) => Area(c) * c.HeightMm * Math.Max(1, c.Count) / 1e9;

        /// <summary>Shuttering to the four sides (or the circumference) in square metres for the whole line.</summary>
        public static double Shuttering(ColumnLine c) => Perimeter(c) * c.HeightMm * Math.Max(1, c.Count) / 1e6;

        public static string[] Headers => new[] { "Mark", "Section (mm)", "Height (mm)", "Floor", "Nos", "Concrete (m3)", "Shuttering (m2)" };

        public static List<string[]> Rows(IEnumerable<ColumnLine> lines)
        {
            var ci = CultureInfo.InvariantCulture;
            var rows = new List<string[]>();
            double conc = 0, shut = 0; int nos = 0;
            foreach (var c in lines)
            {
                string section = c.Round || c.DepthMm <= 0 ? "dia " + c.WidthMm.ToString("0", ci) : c.WidthMm.ToString("0", ci) + " x " + c.DepthMm.ToString("0", ci);
                double v = Concrete(c), s = Shuttering(c);
                rows.Add(new[] { c.Mark, section, c.HeightMm.ToString("0", ci), c.Floor ?? "", Math.Max(1, c.Count).ToString(ci), v.ToString("0.000", ci), s.ToString("0.00", ci) });
                conc += v; shut += s; nos += Math.Max(1, c.Count);
            }
            rows.Add(new[] { "TOTAL", "", "", "", nos.ToString(ci), conc.ToString("0.000", ci), shut.ToString("0.00", ci) });
            return rows;
        }

        /// <summary>
        /// The height of a column on a floor: floor to floor height less the slab above (the next floor's slab, or the roof slab of the top floor).
        /// Returns 0 when the floor is not found and there are no levels.
        /// </summary>
        public static double HeightFromLevels(IList<LevelRow> levels, string floor)
        {
            if (levels == null || levels.Count == 0) return 0;
            int i = string.IsNullOrWhiteSpace(floor) ? 0 : levels.ToList().FindIndex(l => string.Equals(l.Name, floor, StringComparison.OrdinalIgnoreCase));
            if (i < 0) i = 0;
            double slabAbove = i + 1 < levels.Count ? levels[i + 1].SlabMm : levels[i].SlabMm;
            return Math.Max(0, levels[i].FflMm - slabAbove);
        }
    }
}
