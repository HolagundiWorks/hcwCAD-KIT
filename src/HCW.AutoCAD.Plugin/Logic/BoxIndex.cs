using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>An axis-aligned box. Plain numbers, so the logic needs no CAD host.</summary>
    public struct Box2
    {
        public double MinX, MinY, MaxX, MaxY;
        public Box2(double minX, double minY, double maxX, double maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }

        /// <summary>Do the boxes come within tol of each other?</summary>
        public bool Near(Box2 o, double tol) =>
            MinX - tol <= o.MaxX && o.MinX - tol <= MaxX && MinY - tol <= o.MaxY && o.MinY - tol <= MaxY;
    }

    /// <summary>
    /// For each query box, the items whose box is within tol of it, in ascending item order.
    /// A grid keeps this near-linear on large drawings; a box that is unknown (null) is
    /// treated as near everything, as the exact test then decides.
    /// </summary>
    public static class BoxIndex
    {
        private const int BruteForceLimit = 32;
        private const int MaxCellsPerSide = 32;

        public static List<int>[] Candidates(IList<Box2?> queries, IList<Box2?> items, double tol)
        {
            var result = new List<int>[queries.Count];
            if (items.Count <= BruteForceLimit)
            {
                for (int q = 0; q < queries.Count; q++)
                {
                    var list = new List<int>();
                    for (int i = 0; i < items.Count; i++)
                        if (!queries[q].HasValue || !items[i].HasValue || queries[q].Value.Near(items[i].Value, tol)) list.Add(i);
                    result[q] = list;
                }
                return result;
            }

            // Cell size: the typical item size, never below tol.
            double sum = 0; int known = 0;
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var b in items)
            {
                if (!b.HasValue) continue;
                sum += Math.Max(b.Value.MaxX - b.Value.MinX, b.Value.MaxY - b.Value.MinY);
                minX = Math.Min(minX, b.Value.MinX); minY = Math.Min(minY, b.Value.MinY);
                known++;
            }
            double cell = known == 0 ? 1 : Math.Max(Math.Max(tol, 1e-9), sum / known);
            if (known == 0) { minX = 0; minY = 0; }

            var always = new List<int>();               // unknown box, or too large to bucket
            var grid = new Dictionary<long, List<int>>();
            Func<double, double, int> cx = (v, o) => (int)Math.Floor((v - o) / cell);

            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].HasValue) { always.Add(i); continue; }
                var b = items[i].Value;
                int x0 = cx(b.MinX - tol, minX), x1 = cx(b.MaxX + tol, minX);
                int y0 = cx(b.MinY - tol, minY), y1 = cx(b.MaxY + tol, minY);
                if (x1 - x0 >= MaxCellsPerSide || y1 - y0 >= MaxCellsPerSide) { always.Add(i); continue; }
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        long key = ((long)x << 32) ^ (uint)y;
                        List<int> l;
                        if (!grid.TryGetValue(key, out l)) grid[key] = l = new List<int>();
                        l.Add(i);
                    }
            }

            var seen = new HashSet<int>();
            for (int q = 0; q < queries.Count; q++)
            {
                var list = new List<int>();
                if (!queries[q].HasValue)
                {
                    for (int i = 0; i < items.Count; i++) list.Add(i);
                    result[q] = list;
                    continue;
                }
                var qb = queries[q].Value;
                seen.Clear();
                foreach (int i in always) seen.Add(i);
                int x0 = cx(qb.MinX, minX), x1 = cx(qb.MaxX, minX);
                int y0 = cx(qb.MinY, minY), y1 = cx(qb.MaxY, minY);
                if (x1 - x0 >= MaxCellsPerSide * 4 || y1 - y0 >= MaxCellsPerSide * 4)
                {
                    for (int i = 0; i < items.Count; i++) seen.Add(i);
                }
                else
                {
                    for (int x = x0; x <= x1; x++)
                        for (int y = y0; y <= y1; y++)
                        {
                            List<int> l;
                            if (grid.TryGetValue(((long)x << 32) ^ (uint)y, out l))
                                foreach (int i in l) seen.Add(i);
                        }
                }
                foreach (int i in seen)
                    if (!items[i].HasValue || qb.Near(items[i].Value, tol)) list.Add(i);
                list.Sort();
                result[q] = list;
            }
            return result;
        }
    }
}
