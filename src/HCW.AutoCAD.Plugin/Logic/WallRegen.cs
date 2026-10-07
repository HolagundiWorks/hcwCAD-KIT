using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A wall centre line read from the drawing, with the thickness it is to be given (mm).</summary>
    public class RegenSeg
    {
        public P2 A, B;
        public double ThicknessMm;
        public bool Outer;
    }

    public class RegenReport
    {
        /// <summary>Wall ends moved onto the wall they meet or the corner they make.</summary>
        public int Snapped;
        public int TJunctions, LCorners, Crossings, FreeEnds, SquaredCorners;
    }

    /// <summary>
    /// Turns single wall lines into walls of two thicknesses: which lines are outer walls, how their ends meet at corners and T junctions,
    /// and how a corner between two thicknesses is squared off. No CAD types are used here.
    /// </summary>
    public static class WallRegen
    {
        /// <summary>
        /// A line is an outer wall when, standing on its middle, there is no other wall line to be seen on at least one of its two sides
        /// (a ray along the normal escapes). Lines inside the plan have a wall on both sides.
        /// </summary>
        public static bool[] Outer(IList<Seg> segs)
        {
            var res = new bool[segs.Count];
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                var d = s.B - s.A; double len = d.Length;
                if (len < 1e-9) continue;
                var u = d * (1.0 / len);
                var n = new P2(-u.Y, u.X);
                var mid = (s.A + s.B) * 0.5;
                foreach (double side in new[] { 1.0, -1.0 })
                {
                    bool hit = false;
                    for (int j = 0; j < segs.Count && !hit; j++)
                    {
                        if (j == i) continue;
                        double t, v;
                        if (RayHits(mid, n * side, segs[j], out t, out v) && t > 1e-6) hit = true;
                    }
                    if (!hit) { res[i] = true; break; }
                }
            }
            return res;
        }

        /// <summary>
        /// Reads lines that are the two faces of each wall and returns the centre line of every wall found. Two parallel lines that face each other
        /// between <paramref name="minGap"/> and <paramref name="maxGap"/> apart, over a length of at least that gap, are one wall: its centre line runs
        /// midway between them over the length they share, and its thickness is the gap (in mm, using <paramref name="unitsPerMm"/>). The nearest
        /// pair claims a stretch of a line first, so a stretch of face belongs to one wall. <paramref name="unpaired"/> counts the lines with no partner.
        /// </summary>
        public static List<RegenSeg> CentreLines(IList<Seg> faces, double minGap, double maxGap, double unitsPerMm, out int unpaired)
        {
            int n = faces.Count;
            var u = new P2[n]; var len = new double[n];
            for (int i = 0; i < n; i++)
            {
                var d = faces[i].B - faces[i].A; len[i] = d.Length;
                u[i] = len[i] < 1e-12 ? new P2(1, 0) : d * (1.0 / len[i]);
            }

            var cands = new List<double[]>();                 // i, j, gap, t0, t1 (the shared stretch along line i)
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (len[i] < 1e-9 || len[j] < 1e-9) continue;
                    if (Math.Abs(u[i].X * u[j].Y - u[i].Y * u[j].X) > 1e-3) continue;           // not parallel
                    var nrm = new P2(-u[i].Y, u[i].X);
                    var w = faces[j].A - faces[i].A;
                    double gap = Math.Abs(w.X * nrm.X + w.Y * nrm.Y);
                    if (gap < minGap || gap > maxGap) continue;
                    double ta = (faces[j].A - faces[i].A).X * u[i].X + (faces[j].A - faces[i].A).Y * u[i].Y;
                    double tb = (faces[j].B - faces[i].A).X * u[i].X + (faces[j].B - faces[i].A).Y * u[i].Y;
                    double t0 = Math.Max(0, Math.Min(ta, tb)), t1 = Math.Min(len[i], Math.Max(ta, tb));
                    if (t1 - t0 < gap) continue;                                                // shorter than the wall is thick: not a wall
                    cands.Add(new[] { i, j, gap, t0, t1 });
                }
            cands.Sort((a, b) => a[2].CompareTo(b[2]));

            var used = new List<double[]>[n];
            for (int i = 0; i < n; i++) used[i] = new List<double[]>();
            var paired = new bool[n];
            var result = new List<RegenSeg>();
            foreach (var c in cands)
            {
                int i = (int)c[0], j = (int)c[1]; double gap = c[2];
                double dot = u[i].X * u[j].X + u[i].Y * u[j].Y;
                double off = (faces[j].A - faces[i].A).X * u[i].X + (faces[j].A - faces[i].A).Y * u[i].Y;   // line j's own axis, in line i's coordinates: off + s * dot
                var taken = new List<double[]>(used[i]);
                foreach (var r in used[j])
                {
                    double a = off + r[0] * dot, b = off + r[1] * dot;
                    taken.Add(new[] { Math.Min(a, b), Math.Max(a, b) });
                }
                foreach (var piece in Subtract(c[3], c[4], taken))
                {
                    if (piece[1] - piece[0] < gap) continue;
                    used[i].Add(new[] { piece[0], piece[1] });
                    double s0 = (piece[0] - off) / dot, s1 = (piece[1] - off) / dot;
                    used[j].Add(new[] { Math.Min(s0, s1), Math.Max(s0, s1) });
                    paired[i] = paired[j] = true;
                    var nrm = new P2(-u[i].Y, u[i].X);
                    double sgn = Math.Sign((faces[j].A - faces[i].A).X * nrm.X + (faces[j].A - faces[i].A).Y * nrm.Y);
                    var shift = nrm * (sgn * gap / 2.0);
                    result.Add(new RegenSeg { A = faces[i].A + u[i] * piece[0] + shift, B = faces[i].A + u[i] * piece[1] + shift, ThicknessMm = gap / unitsPerMm });
                }
            }
            unpaired = paired.Count(p => !p);
            return result;
        }

        /// <summary>The parts of [a, b] not covered by any of the taken intervals.</summary>
        private static List<double[]> Subtract(double a, double b, List<double[]> taken)
        {
            var parts = new List<double[]> { new[] { a, b } };
            foreach (var t in taken)
            {
                var next = new List<double[]>();
                foreach (var p in parts)
                {
                    if (t[1] <= p[0] || t[0] >= p[1]) { next.Add(p); continue; }
                    if (t[0] > p[0]) next.Add(new[] { p[0], t[0] });
                    if (t[1] < p[1]) next.Add(new[] { t[1], p[1] });
                }
                parts = next;
            }
            return parts;
        }

        /// <summary>Where a ray from p along dir meets a segment: t along the ray, v along the segment (0..length). False when parallel or missed.</summary>
        private static bool RayHits(P2 p, P2 dir, Seg s, out double t, out double v)
        {
            t = v = 0;
            var e = s.B - s.A; double len = e.Length;
            if (len < 1e-12) return false;
            var eu = e * (1.0 / len);
            double den = dir.X * eu.Y - dir.Y * eu.X;
            if (Math.Abs(den) < 1e-9) return false;
            var w = s.A - p;
            t = (w.X * eu.Y - w.Y * eu.X) / den;
            v = (w.X * dir.Y - w.Y * dir.X) / den;
            return t >= 0 && v >= 0 && v <= len;
        }

        /// <summary>Intersection of the two infinite lines, null when they are parallel.</summary>
        private static P2? Meet(Seg a, Seg b, out double ua, out double ub)
        {
            ua = ub = 0;
            var da = a.B - a.A; var db = b.B - b.A;
            double la = da.Length, lb = db.Length;
            if (la < 1e-12 || lb < 1e-12) return null;
            var ea = da * (1.0 / la); var eb = db * (1.0 / lb);
            double den = ea.X * eb.Y - ea.Y * eb.X;
            if (Math.Abs(den) < 1e-6) return null;
            var w = b.A - a.A;
            ua = (w.X * eb.Y - w.Y * eb.X) / den;      // along a from a.A
            ub = (w.X * ea.Y - w.Y * ea.X) / den;      // along b from b.A
            return a.A + ea * ua;
        }

        /// <summary>
        /// Brings the ends together: an end within reach of another wall is moved onto it (a T junction: onto the wall's centre line; an L corner:
        /// both ends to the point where the two lines cross; a tail that runs past a wall is cut back to it). A corner between different
        /// thicknesses, and a point where three or more ends meet, are squared by running each end half the thickest other wall past the point.
        /// Returns the walls ready to be chained, with the thickness each has.
        /// </summary>
        public static List<RegenSeg> Resolve(IList<RegenSeg> input, double reach, double mmToUnits, double tol, RegenReport report)
        {
            report = report ?? new RegenReport();
            int n = input.Count;
            var segs = input.Select(s => new Seg(s.A, s.B)).ToList();
            var ends = new P2[n, 2];
            for (int i = 0; i < n; i++) { ends[i, 0] = segs[i].A; ends[i, 1] = segs[i].B; }
            var snapped = new bool[n, 2];

            for (int i = 0; i < n; i++)
                for (int e = 0; e < 2; e++)
                {
                    var s = segs[i];
                    var from = e == 0 ? s.A : s.B;
                    var other = e == 0 ? s.B : s.A;
                    var dv = from - other; double dl = dv.Length;
                    if (dl < 1e-9) continue;
                    var d = dv * (1.0 / dl);
                    double bestU = double.MaxValue; P2 bestX = from; int bestJ = -1, bestEnd = -1; bool bestL = false;
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i) continue;
                        double ui, vj;
                        var x = Meet(new Seg(other, from), segs[j], out ui, out vj);       // ui from `other` along the line toward `from`
                        if (!x.HasValue) continue;
                        double u = ui - dl;                                                  // beyond (+) or short of (-) the end
                        if (Math.Abs(u) > reach) continue;
                        double lenJ = segs[j].Length;
                        bool inBody = vj >= 0 && vj <= lenJ;
                        if (!inBody && (vj < -reach || vj > lenJ + reach)) continue;
                        // Near the other wall's end (or just past it) the two make a corner; in the middle of its body this end makes a T.
                        bool isL = !inBody || vj < reach || vj > lenJ - reach;
                        if (Math.Abs(u) < bestU)
                        {
                            bestU = Math.Abs(u); bestX = x.Value; bestJ = j; bestL = isL;
                            bestEnd = (vj < lenJ - vj) ? 0 : 1;
                        }
                    }
                    if (bestJ < 0) continue;
                    if (bestU > tol) { report.Snapped++; }
                    ends[i, e] = bestX; snapped[i, e] = true;
                    if (bestL) { ends[bestJ, bestEnd] = bestX; snapped[bestJ, bestEnd] = true; }
                }

            // Count the ends meeting at each point.
            var nodes = new List<List<KeyValuePair<int, int>>>();
            var nodeAt = new List<P2>();
            for (int i = 0; i < n; i++)
                for (int e = 0; e < 2; e++)
                {
                    int found = -1;
                    for (int k = 0; k < nodeAt.Count; k++) if (nodeAt[k].DistanceTo(ends[i, e]) <= tol) { found = k; break; }
                    if (found < 0) { nodes.Add(new List<KeyValuePair<int, int>>()); nodeAt.Add(ends[i, e]); found = nodes.Count - 1; }
                    nodes[found].Add(new KeyValuePair<int, int>(i, e));
                }

            var extra = new double[n, 2];
            for (int k = 0; k < nodes.Count; k++)
            {
                var list = nodes[k];
                if (list.Count == 1) { if (!snapped[list[0].Key, list[0].Value]) report.FreeEnds++; continue; }
                bool simple = list.Count == 2 && Math.Abs(input[list[0].Key].ThicknessMm - input[list[1].Key].ThicknessMm) < 1e-6;
                if (list.Count == 2) report.LCorners++;
                if (simple) continue;
                report.SquaredCorners++;
                foreach (var m in list)
                {
                    double others = list.Where(o => o.Key != m.Key).Max(o => input[o.Key].ThicknessMm);
                    extra[m.Key, m.Value] = others / 2.0 * mmToUnits;
                }
            }

            // T junctions: an end that ended up on the body of another wall.
            for (int i = 0; i < n; i++)
                for (int e = 0; e < 2; e++)
                {
                    if (!snapped[i, e]) continue;
                    int at = nodes.FindIndex(l => l.Any(m => m.Key == i && m.Value == e));
                    if (at >= 0 && nodes[at].Count == 1) report.TJunctions++;
                }

            // Crossings: two walls whose bodies cross away from their ends.
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double ui, uj;
                    var a = new Seg(ends[i, 0], ends[i, 1]); var b = new Seg(ends[j, 0], ends[j, 1]);
                    var x = Meet(a, b, out ui, out uj);
                    if (!x.HasValue) continue;
                    if (ui > tol && ui < a.Length - tol && uj > tol && uj < b.Length - tol) report.Crossings++;
                }

            var result = new List<RegenSeg>();
            for (int i = 0; i < n; i++)
            {
                var a = ends[i, 0]; var b = ends[i, 1];
                var d = b - a; double len = d.Length;
                if (len < 1e-9) continue;
                var u = d * (1.0 / len);
                result.Add(new RegenSeg { A = a - u * extra[i, 0], B = b + u * extra[i, 1], ThicknessMm = input[i].ThicknessMm, Outer = input[i].Outer });
            }
            return result;
        }

        /// <summary>Joins the walls of each thickness end to end into runs (a ring becomes a closed run).</summary>
        public static List<KeyValuePair<double, SegmentChain.Run>> Chains(IEnumerable<RegenSeg> walls, double tol)
        {
            var res = new List<KeyValuePair<double, SegmentChain.Run>>();
            foreach (var g in walls.GroupBy(w => Math.Round(w.ThicknessMm, 3)))
                foreach (var run in SegmentChain.Join(g.Select(w => new Seg(w.A, w.B)).ToList(), tol))
                    res.Add(new KeyValuePair<double, SegmentChain.Run>(g.Key, run));
            return res;
        }
    }
}
