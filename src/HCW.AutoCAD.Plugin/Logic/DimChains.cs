using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>The number-crunching behind AUTODIM: turning coordinates along one side into dimension segments. No CAD types are used here.</summary>
    public static class DimChains
    {
        /// <summary>Sorted coordinates, with any that lie within <paramref name="tolerance"/> of the previous kept one merged into it.</summary>
        public static List<double> Merge(IEnumerable<double> values, double tolerance)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var kept = new List<double>();
            foreach (var v in sorted)
            {
                if (kept.Count == 0 || v - kept[kept.Count - 1] > tolerance) kept.Add(v);
            }
            return kept;
        }

        /// <summary>Consecutive pairs of coordinates, dropping any pair closer than <paramref name="minLength"/>.</summary>
        public static List<KeyValuePair<double, double>> Segments(IList<double> points, double minLength)
        {
            var result = new List<KeyValuePair<double, double>>();
            for (int i = 0; i + 1 < points.Count; i++)
                if (points[i + 1] - points[i] >= minLength)
                    result.Add(new KeyValuePair<double, double>(points[i], points[i + 1]));
            return result;
        }

        /// <summary>Overall dimension: first to last coordinate, or none when they are closer than <paramref name="minLength"/>.</summary>
        public static List<KeyValuePair<double, double>> Overall(IList<double> points, double minLength)
        {
            var result = new List<KeyValuePair<double, double>>();
            if (points.Count >= 2 && points[points.Count - 1] - points[0] >= minLength)
                result.Add(new KeyValuePair<double, double>(points[0], points[points.Count - 1]));
            return result;
        }

        /// <summary>True when two chains give the same segments, so the outer one would only repeat the inner one.</summary>
        public static bool Same(IList<KeyValuePair<double, double>> a, IList<KeyValuePair<double, double>> b, double tolerance)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i].Key - b[i].Key) > tolerance || Math.Abs(a[i].Value - b[i].Value) > tolerance) return false;
            return true;
        }

        /// <summary>
        /// Which row each dimension text sits on. Row 0 is the dimension line itself: used when the segment is
        /// long enough for its text. A shorter segment's text goes to row 1 or 2 (further out), choosing the
        /// first row where it does not run into the previous text on that row. Segments must be in order along the chain.
        /// </summary>
        public static int[] Rows(IList<KeyValuePair<double, double>> segments, double textWidth, int maxRow)
        {
            var rows = new int[segments.Count];
            var lastRight = new double[maxRow + 1];
            for (int r = 0; r <= maxRow; r++) lastRight[r] = double.NegativeInfinity;
            double half = textWidth / 2.0;

            for (int i = 0; i < segments.Count; i++)
            {
                double a = segments[i].Key, b = segments[i].Value;
                double mid = (a + b) / 2.0;
                if (b - a >= textWidth)
                {
                    rows[i] = 0;
                    lastRight[0] = mid + half;
                    continue;
                }
                int chosen = -1;
                for (int r = 1; r <= maxRow; r++)
                    if (mid - half >= lastRight[r]) { chosen = r; break; }
                if (chosen < 0)
                {
                    // nowhere is clear: use the row whose last text ends earliest
                    chosen = 1;
                    for (int r = 2; r <= maxRow; r++)
                        if (lastRight[r] < lastRight[chosen]) chosen = r;
                }
                rows[i] = chosen;
                lastRight[chosen] = mid + half;
            }
            return rows;
        }
    
        /// <summary>How the rows of text of one chain are spaced.</summary>
        public struct RowSpacing
        {
            public int MaxRow;
            public double Step;
            /// <summary>False when even one row of moved text would reach the next chain's dimension line.</summary>
            public bool Fits;
        }

        /// <summary>
        /// Row spacing that keeps a chain's moved text clear of the chain outside it: the furthest row ends a text height short of the next
        /// dimension line (chains are <paramref name="chainStep"/> apart). Rows are at most 1.5 text heights apart and at most maxRow of them.
        /// </summary>
        public static RowSpacing RowPlan(double textHeight, double chainStep, int maxRow = 2)
        {
            double room = chainStep - 1.3 * textHeight;
            double need = 1.1 * textHeight;
            int rows = room > 0 ? Math.Min(maxRow, (int)Math.Floor(room / need + 1e-9)) : 0;
            bool fits = rows >= 1;
            if (rows < 1) rows = 1;
            double step = Math.Min(1.5 * textHeight, fits ? room / rows : need);
            return new RowSpacing { MaxRow = rows, Step = step, Fits = fits };
        }
    }
}
