using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// The standard door, window and ventilator marks and their widths (millimetres): D1 800, D2 900, D3 1200; W1 600, W2 900, W3 1200, W4 1500,
    /// W5 2000; V1 600. A ventilator is the 600 mm window that is no taller than <c>ventilatorMaxHeightMm</c>. A size that is not in the table gets the
    /// next free number after the standard ones (D4, W6 ...). No CAD types are used here.
    /// </summary>
    public static class OpeningStandards
    {
        public class Std
        {
            public string Code;
            public bool Door;
            public double WidthMm;
        }

        public static readonly IReadOnlyList<Std> Doors = new[]
        {
            new Std { Code = "D1", Door = true, WidthMm = 800 },
            new Std { Code = "D2", Door = true, WidthMm = 900 },
            new Std { Code = "D3", Door = true, WidthMm = 1200 },
        };

        public static readonly IReadOnlyList<Std> Windows = new[]
        {
            new Std { Code = "W1", WidthMm = 600 },
            new Std { Code = "W2", WidthMm = 900 },
            new Std { Code = "W3", WidthMm = 1200 },
            new Std { Code = "W4", WidthMm = 1500 },
            new Std { Code = "W5", WidthMm = 2000 },
        };

        public static readonly Std Ventilator = new Std { Code = "V1", WidthMm = 600 };

        private const double Tol = 0.5;

        /// <summary>The standard width of a mark (D2 -> 900), or null when it is not a standard mark.</summary>
        public static double? WidthOf(string code)
        {
            var all = Doors.Concat(Windows).Concat(new[] { Ventilator });
            var s = all.FirstOrDefault(x => string.Equals(x.Code, code ?? "", StringComparison.OrdinalIgnoreCase));
            return s == null ? (double?)null : s.WidthMm;
        }

        /// <summary>The standard door mark for a width, or null when the width is not standard.</summary>
        public static string DoorCode(double widthMm)
        {
            var s = Doors.FirstOrDefault(x => Math.Abs(x.WidthMm - widthMm) <= Tol);
            return s == null ? null : s.Code;
        }

        /// <summary>The standard window mark for a size: V1 for the 600 mm window no taller than the ventilator limit, else W1 to W5 by width; null when not standard.</summary>
        public static string WindowCode(double widthMm, double heightMm, double ventilatorMaxHeightMm)
        {
            if (Math.Abs(Ventilator.WidthMm - widthMm) <= Tol && heightMm > 0 && heightMm <= ventilatorMaxHeightMm + Tol) return Ventilator.Code;
            var s = Windows.FirstOrDefault(x => Math.Abs(x.WidthMm - widthMm) <= Tol);
            return s == null ? null : s.Code;
        }

        /// <summary>
        /// The mark for a new schedule entry: the standard one when the size has one and no entry of this kind uses it yet, otherwise the next free
        /// number after the highest of the standard marks and the marks in use (D4, W6, V2 ...).
        /// </summary>
        public static string NewMark(string standardCode, string prefix, IEnumerable<string> marksInUse)
        {
            var used = (marksInUse ?? new string[0]).Select(m => m ?? "").ToList();
            if (standardCode != null && !used.Any(m => string.Equals(m, standardCode, StringComparison.OrdinalIgnoreCase))) return standardCode;
            int top = prefix.Equals("D", StringComparison.OrdinalIgnoreCase) ? Doors.Count : prefix.Equals("W", StringComparison.OrdinalIgnoreCase) ? Windows.Count : 1;
            foreach (var m in used)
            {
                int n;
                if (m.Length > 1 && m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(m.Substring(1), out n)) top = Math.Max(top, n);
            }
            return prefix.ToUpperInvariant() + (top + 1);
        }
    }
}
