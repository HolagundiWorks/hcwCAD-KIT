using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A plane polyline (or arc) as points and bulges. Bulge i belongs to the segment from point i to point i+1.</summary>
    public class PolyPath
    {
        public List<P2> Points = new List<P2>();
        public List<double> Bulges = new List<double>();
        public bool Closed;

        public P2 First => Points[0];
        public P2 Last => Points[Points.Count - 1];

        public PolyPath Reversed()
        {
            var r = new PolyPath { Closed = Closed };
            r.Points.AddRange(Enumerable.Reverse(Points));
            int n = Points.Count;
            // Segment k of the reverse is segment n-2-k of the original, run the other way.
            for (int k = 0; k < n; k++)
            {
                int src = Closed ? ((n - 2 - k) % n + n) % n : n - 2 - k;
                r.Bulges.Add(src >= 0 && src < Bulges.Count ? -Bulges[src] : 0);
            }
            return r;
        }

        public static PolyPath FromArc(P2 centre, double radius, double startAngle, double sweep)
        {
            var p = new PolyPath();
            p.Points.Add(centre + new P2(Math.Cos(startAngle), Math.Sin(startAngle)) * radius);
            p.Points.Add(centre + new P2(Math.Cos(startAngle + sweep), Math.Sin(startAngle + sweep)) * radius);
            p.Bulges.Add(Math.Tan(sweep / 4));
            p.Bulges.Add(0);
            return p;
        }
    }

    public class PolyItem
    {
        public PolyPath Path;
        public string Key = "";
    }

    public class PolyJoined
    {
        /// <summary>The item that stays; it takes the joined path.</summary>
        public int Keeper;
        public List<int> Absorbed = new List<int>();
        public PolyPath Path;
    }

    public class PolyCleanResult
    {
        public List<int> Duplicates = new List<int>();
        public List<PolyJoined> Joined = new List<PolyJoined>();
    }

    /// <summary>Removes duplicate polylines and joins open ones end to end where their ends meet.</summary>
    public static class PolylineJoin
    {
        private const double BulgeTol = 1e-6;

        public static PolyCleanResult Run(IList<PolyItem> items, double tol, bool join)
        {
            var res = new PolyCleanResult();
            var removed = new HashSet<int>();
            foreach (var group in Enumerable.Range(0, items.Count).GroupBy(i => items[i].Key))
            {
                var idx = group.ToList();
                for (int a = 0; a < idx.Count; a++)
                {
                    if (removed.Contains(idx[a])) continue;
                    for (int b = a + 1; b < idx.Count; b++)
                        if (!removed.Contains(idx[b]) && Same(items[idx[a]].Path, items[idx[b]].Path, tol)) { removed.Add(idx[b]); res.Duplicates.Add(idx[b]); }
                }
                if (!join) continue;

                var live = idx.Where(i => !removed.Contains(i) && !items[i].Path.Closed && items[i].Path.Points.Count >= 2).ToList();
                var work = live.ToDictionary(i => i, i => new PolyJoined { Keeper = i, Path = items[i].Path });
                bool merged = true;
                while (merged)
                {
                    merged = false;
                    var keys = work.Keys.OrderBy(k => k).ToList();
                    foreach (var ka in keys)
                    {
                        var a = work[ka];
                        if (a.Path.Closed) continue;
                        foreach (var kb in keys)
                        {
                            if (kb <= ka || !work.ContainsKey(kb) || work[kb].Path.Closed) continue;
                            var b = work[kb];
                            var joined = Meet(a.Path, b.Path, tol);
                            if (joined == null) continue;
                            a.Path = joined;
                            a.Absorbed.Add(kb); a.Absorbed.AddRange(b.Absorbed);
                            work.Remove(kb);
                            merged = true;
                            break;
                        }
                        if (merged) break;
                    }
                }
                foreach (var w in work.Values)
                {
                    var self = Close(w.Path, tol);
                    if (self != null) w.Path = self;
                    if (w.Absorbed.Count > 0 || self != null) res.Joined.Add(w);
                }
            }
            res.Duplicates.Sort();
            res.Joined.Sort((x, y) => x.Keeper.CompareTo(y.Keeper));
            return res;
        }

        /// <summary>True when two paths have the same points and bulges, in either direction.</summary>
        public static bool Same(PolyPath a, PolyPath b, double tol)
        {
            if (a.Closed != b.Closed || a.Points.Count != b.Points.Count) return false;
            return Equal(a, b, tol) || (!a.Closed && Equal(a, b.Reversed(), tol));
        }

        private static bool Equal(PolyPath a, PolyPath b, double tol)
        {
            int segs = a.Closed ? a.Points.Count : a.Points.Count - 1;
            for (int i = 0; i < a.Points.Count; i++) if (a.Points[i].DistanceTo(b.Points[i]) > tol) return false;
            for (int i = 0; i < segs; i++) if (Math.Abs(a.Bulges[i] - b.Bulges[i]) > BulgeTol) return false;
            return true;
        }

        /// <summary>The path made by running a into b where one end of each meets, or null if no end of a meets an end of b.</summary>
        private static PolyPath Meet(PolyPath a, PolyPath b, double tol)
        {
            foreach (var x in new[] { a, a.Reversed() })
                foreach (var y in new[] { b, b.Reversed() })
                    if (x.Last.DistanceTo(y.First) <= tol)
                    {
                        var r = new PolyPath();
                        for (int i = 0; i < x.Points.Count; i++) { r.Points.Add(x.Points[i]); r.Bulges.Add(i < x.Points.Count - 1 ? x.Bulges[i] : y.Bulges[0]); }
                        for (int i = 1; i < y.Points.Count; i++) { r.Points.Add(y.Points[i]); r.Bulges.Add(i < y.Points.Count - 1 ? y.Bulges[i] : 0); }
                        return r;
                    }
            return null;
        }

        /// <summary>If the ends of an open path meet, the same path closed; otherwise null.</summary>
        private static PolyPath Close(PolyPath p, double tol)
        {
            if (p.Points.Count < 3 || p.First.DistanceTo(p.Last) > tol) return null;
            var r = new PolyPath { Closed = true };
            r.Points.AddRange(p.Points.Take(p.Points.Count - 1));
            r.Bulges.AddRange(p.Bulges.Take(p.Points.Count - 1));
            return r;
        }
    }
}
