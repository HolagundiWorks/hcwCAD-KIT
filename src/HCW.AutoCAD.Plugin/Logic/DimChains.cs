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
    }
}
