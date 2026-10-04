using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// Finds the room around a point from loose wall-face segments: the segments are cut where they cross, joined into
    /// a planar graph, and the smallest closed region that contains the point is returned.
    /// </summary>
    public static class PlanarRooms
    {
        /// <summary>
        /// The outline (counter-clockwise, collinear points removed) of the smallest closed region around pt, or null with a
        /// reason. A room that is not fully closed is not found: the point falls in the larger region around it, so check the area.
        /// Detached islands inside a room (a free-standing column) are not cut out of it.
        /// </summary>
        public static List<P2> Find(IList<Seg> segs, P2 pt, double tol, out string error)
        {
            error = null;
            if (tol <= 0) tol = 1e-9;
            var verts = new List<P2>();
            var grid = new Dictionary<long, List<int>>();
            double cell = tol * 4;

            long Key(long cx, long cy) => (cx << 32) ^ (cy & 0xffffffffL);
            int Snap(P2 p)
            {
                long cx = (long)Math.Floor(p.X / cell), cy = (long)Math.Floor(p.Y / cell);
                for (long x = cx - 1; x <= cx + 1; x++)
                    for (long y = cy - 1; y <= cy + 1; y++)
                    {
                        List<int> l;
                        if (!grid.TryGetValue(Key(x, y), out l)) continue;
                        foreach (int v in l) if (verts[v].DistanceTo(p) <= tol) return v;
                    }
                int id = verts.Count;
                verts.Add(p);
                List<int> own;
                if (!grid.TryGetValue(Key(cx, cy), out own)) grid[Key(cx, cy)] = own = new List<int>();
                own.Add(id);
                return id;
            }

            // 1. Cut every segment where others cross or touch it.
            var live = segs.Where(s => s.Length > tol).ToList();
            var cuts = new List<double>[live.Count];
            for (int i = 0; i < live.Count; i++) cuts[i] = new List<double> { 0, 1 };
            for (int i = 0; i < live.Count; i++)
            {
                var a = live[i];
                var da = a.B - a.A;
                double la = da.Length;
                for (int j = i + 1; j < live.Count; j++)
                {
                    var b = live[j];
                    if (Math.Max(a.A.X, a.B.X) + tol < Math.Min(b.A.X, b.B.X) || Math.Max(b.A.X, b.B.X) + tol < Math.Min(a.A.X, a.B.X)
                        || Math.Max(a.A.Y, a.B.Y) + tol < Math.Min(b.A.Y, b.B.Y) || Math.Max(b.A.Y, b.B.Y) + tol < Math.Min(a.A.Y, a.B.Y)) continue;
                    var db = b.B - b.A;
                    double lb = db.Length;
                    double den = P2.Cross(da, db);
                    if (Math.Abs(den) > 1e-9 * la * lb)
                    {
                        var ab = b.A - a.A;
                        double t = P2.Cross(ab, db) / den, u = P2.Cross(ab, da) / den;
                        if (t >= -tol / la && t <= 1 + tol / la && u >= -tol / lb && u <= 1 + tol / lb)
                        {
                            cuts[i].Add(Math.Max(0, Math.Min(1, t)));
                            cuts[j].Add(Math.Max(0, Math.Min(1, u)));
                        }
                    }
                    else if (Math.Abs(P2.Cross(da, b.A - a.A)) / la <= tol)
                    {
                        // Collinear: each end that lies on the other splits it.
                        foreach (var p in new[] { b.A, b.B })
                        {
                            double t = P2.Dot(p - a.A, da) / (la * la);
                            if (t > 0 && t < 1) cuts[i].Add(t);
                        }
                        foreach (var p in new[] { a.A, a.B })
                        {
                            double u = P2.Dot(p - b.A, db) / (lb * lb);
                            if (u > 0 && u < 1) cuts[j].Add(u);
                        }
                    }
                }
            }

            // 2. Graph: vertices snapped within tol, one edge per piece.
            var edges = new HashSet<long>();
            var adj = new Dictionary<int, List<int>>();
            for (int i = 0; i < live.Count; i++)
            {
                var s = live[i];
                var d = s.B - s.A;
                var ts = cuts[i].OrderBy(x => x).ToList();
                int prev = -1;
                foreach (double t in ts)
                {
                    int v = Snap(s.A + d * t);
                    if (prev >= 0 && v != prev)
                    {
                        long k = Math.Min(prev, v) * 1000003L + Math.Max(prev, v);
                        if (edges.Add(k))
                        {
                            if (!adj.ContainsKey(prev)) adj[prev] = new List<int>();
                            if (!adj.ContainsKey(v)) adj[v] = new List<int>();
                            adj[prev].Add(v); adj[v].Add(prev);
                        }
                    }
                    prev = v;
                }
            }

            // 3. Prune spurs: vertices with one edge cannot bound a room.
            var queue = new Queue<int>(adj.Where(kv => kv.Value.Count < 2).Select(kv => kv.Key));
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                List<int> nb;
                if (!adj.TryGetValue(v, out nb)) continue;
                if (nb.Count >= 2) continue;
                foreach (int w in nb)
                {
                    adj[w].Remove(v);
                    if (adj[w].Count < 2) queue.Enqueue(w);
                }
                adj.Remove(v);
            }
            if (adj.Count == 0) { error = "there are no closed outlines to find a room in"; return null; }

            // 4. Neighbours of each vertex in counter-clockwise order.
            var order = new Dictionary<int, List<int>>();
            foreach (var kv in adj)
            {
                var p = verts[kv.Key];
                order[kv.Key] = kv.Value.OrderBy(w => Math.Atan2(verts[w].Y - p.Y, verts[w].X - p.X)).ToList();
            }

            // 5. Walk every face with the face on the left; keep the counter-clockwise (bounded) ones that hold the point.
            var visited = new HashSet<long>();
            List<P2> best = null; double bestArea = double.MaxValue;
            int limit = 2 * edges.Count + 4;
            foreach (var start in order.Keys.ToList())
            {
                foreach (int first in order[start])
                {
                    long dk = (long)start * 1000003L + first;
                    if (visited.Contains(dk)) continue;

                    var cycle = new List<int>();
                    int u = start, v = first, steps = 0;
                    bool closed = false;
                    while (steps++ <= limit)
                    {
                        long k = (long)u * 1000003L + v;
                        if (!visited.Add(k)) { closed = k == dk; break; }
                        cycle.Add(u);
                        var around = order[v];
                        int idx = around.IndexOf(u);
                        int w = around[(idx - 1 + around.Count) % around.Count];
                        u = v; v = w;
                        if (u == start && v == first) { closed = true; break; }
                    }
                    if (!closed || cycle.Count < 3) continue;

                    var poly = cycle.Select(c => verts[c]).ToList();
                    double area = SignedArea(poly);
                    if (area <= tol * tol) continue;                        // the outside of a component
                    if (area >= bestArea || !Contains(poly, pt)) continue;
                    best = poly; bestArea = area;
                }
            }
            if (best == null) { error = "that point is not inside a closed room"; return null; }
            return WallGeometry.Simplify(best, true, tol);
        }

        public static double SignedArea(IList<P2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].X * q.Y - q.X * p[i].Y;
            }
            return a / 2.0;
        }

        /// <summary>Centre of area of a polygon; the average of the corners when it has no area.</summary>
        public static P2 Centroid(IList<P2> p)
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                double f = p[i].X * q.Y - q.X * p[i].Y;
                a += f; cx += (p[i].X + q.X) * f; cy += (p[i].Y + q.Y) * f;
            }
            if (Math.Abs(a) < 1e-12) return new P2(p.Average(v => v.X), p.Average(v => v.Y));
            return new P2(cx / (3 * a), cy / (3 * a));
        }

        /// <summary>Even-odd point in polygon.</summary>
        public static bool Contains(IList<P2> poly, P2 pt)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > pt.Y) != (poly[j].Y > pt.Y)
                    && pt.X < (poly[j].X - poly[i].X) * (pt.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
