using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A room found from wall faces: its outline, any free-standing islands (columns) inside it, and its areas.</summary>
    public class RoomShape
    {
        public List<P2> Outline = new List<P2>();
        public List<List<P2>> Holes = new List<List<P2>>();
        public double GrossArea, NetArea;
    }

    /// <summary>
    /// Finds rooms from loose wall-face segments: the segments are cut where they cross, joined into a planar graph, and its closed
    /// regions are read off. A room is a region that is not a sliver of wall.
    /// </summary>
    public static class PlanarRooms
    {
        private class Graph
        {
            public List<P2> Verts = new List<P2>();
            public Dictionary<int, List<int>> Adj = new Dictionary<int, List<int>>();
            public int Edges;
        }

        private class Face
        {
            public List<P2> Poly;
            public double Area;
            public double Perimeter;
            /// <summary>Twice the area over the perimeter: the width of a strip. A wall body is thin, a room is not.</summary>
            public double Thickness => Perimeter < 1e-12 ? 0 : 2.0 * Area / Perimeter;
        }

        /// <summary>
        /// The outline (counter-clockwise, collinear points removed) of the smallest closed region around pt, or null with a
        /// reason. closeGap bridges gaps up to that size between facing wall ends (an unframed door opening); 0 leaves them open.
        /// A room that is not closed is not found: the point falls in the larger region around it, so check the area.
        /// </summary>
        public static List<P2> Find(IList<Seg> segs, P2 pt, double tol, out string error, double closeGap = 0)
        {
            error = null;
            if (tol <= 0) tol = 1e-9;
            var g = Build(segs, tol, closeGap);
            if (g.Adj.Count == 0) { error = "there are no closed outlines to find a room in"; return null; }

            Face best = null;
            foreach (var f in Faces(g, tol))
                if (f.Area > tol * tol && (best == null || f.Area < best.Area) && Contains(f.Poly, pt)) best = f;
            if (best == null) { error = "that point is not inside a closed room"; return null; }
            return WallGeometry.Simplify(best.Poly, true, tol);
        }

        /// <summary>
        /// Every room in the drawing. Regions thinner than minThickness (wall bodies, columns) are not rooms; a region that holds
        /// another room (the outside of a wall ring) is not one either. Thin regions lying inside a room are its holes (free-standing
        /// columns) and come off its net area. Rooms are listed bottom to top, then left to right.
        /// </summary>
        public static List<RoomShape> AllRooms(IList<Seg> segs, double tol, double minThickness, double closeGap = 0)
        {
            if (tol <= 0) tol = 1e-9;
            var g = Build(segs, tol, closeGap);
            var faces = Faces(g, tol).Where(f => f.Area > tol * tol).ToList();
            var cand = faces.Where(f => f.Thickness >= minThickness).ToList();

            var rooms = new List<RoomShape>();
            foreach (var c in cand)
            {
                bool holdsRoom = cand.Any(d => !ReferenceEquals(d, c) && d.Area < c.Area && Contains(c.Poly, Centroid(d.Poly)));
                if (holdsRoom) continue;
                var shape = new RoomShape { Outline = WallGeometry.Simplify(c.Poly, true, tol), GrossArea = c.Area };
                foreach (var h in faces.Where(f => f.Thickness < minThickness && f.Area < c.Area))
                    if (h.Poly.All(p => Contains(c.Poly, p) || OnBoundary(c.Poly, p, tol)) && Contains(c.Poly, Centroid(h.Poly))
                        && !h.Poly.All(p => OnBoundary(c.Poly, p, tol)))
                        shape.Holes.Add(WallGeometry.Simplify(h.Poly, true, tol));
                shape.NetArea = c.Area - shape.Holes.Sum(h => Math.Abs(SignedArea(h)));
                rooms.Add(shape);
            }
            return rooms.OrderBy(r => Math.Round(Centroid(r.Outline).Y, 6)).ThenBy(r => Centroid(r.Outline).X).ToList();
        }

        // ---- the graph ----

        private static Graph Build(IList<Seg> segs, double tol, double closeGap)
        {
            var g = new Graph();
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
                        foreach (int v in l) if (g.Verts[v].DistanceTo(p) <= tol) return v;
                    }
                int id = g.Verts.Count;
                g.Verts.Add(p);
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

            // 2. Vertices snapped within tol, one edge per piece.
            var edges = new HashSet<long>();
            Action<int, int> addEdge = (p, q) =>
            {
                if (p == q) return;
                long k = Math.Min(p, q) * 1000003L + Math.Max(p, q);
                if (!edges.Add(k)) return;
                if (!g.Adj.ContainsKey(p)) g.Adj[p] = new List<int>();
                if (!g.Adj.ContainsKey(q)) g.Adj[q] = new List<int>();
                g.Adj[p].Add(q); g.Adj[q].Add(p);
            };
            for (int i = 0; i < live.Count; i++)
            {
                var s = live[i];
                var d = s.B - s.A;
                int prev = -1;
                foreach (double t in cuts[i].OrderBy(x => x))
                {
                    int v = Snap(s.A + d * t);
                    if (prev >= 0) addEdge(prev, v);
                    prev = v;
                }
            }

            // 3. Bridge small gaps between facing wall ends (a door opening drawn without jambs).
            if (closeGap > tol)
            {
                var ends = g.Adj.Where(kv => kv.Value.Count == 1).Select(kv => kv.Key).ToList();
                var away = new Dictionary<int, P2>();
                foreach (int v in ends)
                {
                    var d = g.Verts[g.Adj[v][0]] - g.Verts[v];
                    away[v] = d * (1.0 / d.Length);                       // along its own edge, away from the end
                }
                var best = new Dictionary<int, int>();
                foreach (int i in ends)
                {
                    int pick = -1; double bd = double.MaxValue;
                    foreach (int j in ends)
                    {
                        if (j == i || g.Adj[i][0] == j) continue;
                        var c = g.Verts[j] - g.Verts[i];
                        double dist = c.Length;
                        if (dist > closeGap || dist < tol || dist >= bd) continue;
                        var cu = c * (1.0 / dist);
                        // each end must point away from the other: the two wall faces face each other across the gap
                        if (P2.Dot(away[i], cu) > -0.94 || P2.Dot(away[j], cu * -1) > -0.94) continue;
                        pick = j; bd = dist;
                    }
                    if (pick >= 0) best[i] = pick;
                }
                foreach (var kv in best)
                    if (best.ContainsKey(kv.Value) && best[kv.Value] == kv.Key && kv.Key < kv.Value) addEdge(kv.Key, kv.Value);
            }

            // 4. Prune spurs: vertices with one edge cannot bound a room.
            var queue = new Queue<int>(g.Adj.Where(kv => kv.Value.Count < 2).Select(kv => kv.Key));
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                List<int> nb;
                if (!g.Adj.TryGetValue(v, out nb) || nb.Count >= 2) continue;
                foreach (int w in nb)
                {
                    g.Adj[w].Remove(v);
                    if (g.Adj[w].Count < 2) queue.Enqueue(w);
                }
                g.Adj.Remove(v);
            }
            g.Edges = edges.Count;
            return g;
        }

        /// <summary>Walks every face with the face on the left. Counter-clockwise cycles are bounded regions; clockwise ones are the outside of a component.</summary>
        private static List<Face> Faces(Graph g, double tol)
        {
            var order = new Dictionary<int, List<int>>();
            foreach (var kv in g.Adj)
            {
                var p = g.Verts[kv.Key];
                order[kv.Key] = kv.Value.OrderBy(w => Math.Atan2(g.Verts[w].Y - p.Y, g.Verts[w].X - p.X)).ToList();
            }

            var faces = new List<Face>();
            var visited = new HashSet<long>();
            int limit = 2 * g.Edges + 4;
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

                    var poly = cycle.Select(c => g.Verts[c]).ToList();
                    double area = SignedArea(poly);
                    if (area <= 0) continue;
                    double per = 0;
                    for (int i = 0; i < poly.Count; i++) per += poly[i].DistanceTo(poly[(i + 1) % poly.Count]);
                    faces.Add(new Face { Poly = poly, Area = area, Perimeter = per });
                }
            }
            return faces;
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

        private static bool OnBoundary(IList<P2> poly, P2 p, double tol)
        {
            for (int i = 0; i < poly.Count; i++)
                if (OpeningCut.DistanceToSegment(new Seg(poly[i], poly[(i + 1) % poly.Count]), p) <= tol) return true;
            return false;
        }
    }
}
