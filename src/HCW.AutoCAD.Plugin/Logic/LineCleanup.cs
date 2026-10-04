using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A line to clean: its end points and a key (layer, linetype, colour ...) that must match to merge.</summary>
    public class CleanLine
    {
        public Seg Seg;
        public string Key = "";
    }

    public class CleanResult
    {
        /// <summary>Indexes (into the input) of lines to erase.</summary>
        public List<int> Erase = new List<int>();
        /// <summary>Lines that stay but take new end points: input index to the merged segment.</summary>
        public Dictionary<int, Seg> Replace = new Dictionary<int, Seg>();
        public int ZeroLength, Duplicates, Merged;
    }

    /// <summary>Removes zero-length and duplicate lines and joins collinear lines that touch or overlap.</summary>
    public static class LineCleanup
    {
        private const double AngleTol = 1e-6;

        /// <summary>
        /// Lines shorter than tol are zero-length. Lines on the same key that lie on one straight line and overlap or
        /// come within tol of each other are joined into one (the first of them keeps its identity and takes the
        /// joined ends). A line lying exactly on another (duplicate or fully inside) counts as a duplicate;
        /// merging a stretch that extends past the other counts as a merge. With join false only exact duplicates
        /// (same ends, either way round) are removed.
        /// </summary>
        public static CleanResult Run(IList<CleanLine> lines, double tol, bool join)
        {
            var res = new CleanResult();
            var live = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Seg.Length <= tol) { res.Erase.Add(i); res.ZeroLength++; }
                else live.Add(i);
            }

            foreach (var byKey in live.GroupBy(i => lines[i].Key))
            {
                if (!join) { Duplicates(lines, byKey.ToList(), tol, res); continue; }

                foreach (var cluster in Collinear(lines, byKey.ToList(), tol))
                {
                    if (cluster.Count < 2) continue;
                    // Position of each line along the cluster's direction.
                    var s0 = lines[cluster[0]].Seg;
                    var u = (s0.B - s0.A) * (1.0 / s0.Length);
                    var items = cluster.Select(i =>
                    {
                        double a = P2.Dot(lines[i].Seg.A - s0.A, u), b = P2.Dot(lines[i].Seg.B - s0.A, u);
                        return new { Index = i, Lo = Math.Min(a, b), Hi = Math.Max(a, b) };
                    }).OrderBy(x => x.Lo).ThenBy(x => x.Index).ToList();

                    int k = 0;
                    while (k < items.Count)
                    {
                        int keeper = items[k].Index;
                        double lo = items[k].Lo, hi = items[k].Hi;
                        int absorbed = 0, extended = 0;
                        int j = k + 1;
                        while (j < items.Count && items[j].Lo <= hi + tol)
                        {
                            bool extends = items[j].Hi > hi + tol;
                            hi = Math.Max(hi, items[j].Hi);
                            res.Erase.Add(items[j].Index);
                            absorbed++;
                            if (extends) extended++;
                            j++;
                        }
                        if (absorbed > 0)
                        {
                            // The keeper is the lowest-numbered line of the stretch, not necessarily the first along it.
                            int lowest = items.Skip(k).Take(absorbed + 1).Min(x => x.Index);
                            if (lowest != keeper)
                            {
                                res.Erase.Remove(lowest);
                                res.Erase.Add(keeper);
                                keeper = lowest;
                            }
                            // A duplicate leaves the keeper as it is; only a longer stretch changes its ends.
                            double ka = P2.Dot(lines[keeper].Seg.A - s0.A, u), kb = P2.Dot(lines[keeper].Seg.B - s0.A, u);
                            bool same = Math.Abs(Math.Min(ka, kb) - lo) <= tol && Math.Abs(Math.Max(ka, kb) - hi) <= tol;
                            if (!same) res.Replace[keeper] = new Seg(s0.A + u * lo, s0.A + u * hi);
                            res.Duplicates += absorbed - extended;
                            res.Merged += extended;
                        }
                        k = j;
                    }
                }
            }
            res.Erase.Sort();
            return res;
        }

        private static void Duplicates(IList<CleanLine> lines, List<int> idx, double tol, CleanResult res)
        {
            var seen = new List<int>();
            foreach (int i in idx)
            {
                bool dup = false;
                foreach (int j in seen)
                {
                    var a = lines[i].Seg; var b = lines[j].Seg;
                    if ((a.A.DistanceTo(b.A) <= tol && a.B.DistanceTo(b.B) <= tol) || (a.A.DistanceTo(b.B) <= tol && a.B.DistanceTo(b.A) <= tol)) { dup = true; break; }
                }
                if (dup) { res.Erase.Add(i); res.Duplicates++; }
                else seen.Add(i);
            }
        }

        /// <summary>Groups of lines that lie on one straight line (same direction, same offset).</summary>
        private static List<List<int>> Collinear(IList<CleanLine> lines, List<int> idx, double tol)
        {
            double Angle(int i)
            {
                var d = lines[i].Seg.B - lines[i].Seg.A;
                double a = Math.Atan2(d.Y, d.X);
                if (a < 0) a += Math.PI;
                if (a >= Math.PI - AngleTol) a -= Math.PI;
                return a;
            }

            var byAngle = idx.Select(i => new { Index = i, Angle = Angle(i) }).OrderBy(x => x.Angle).ToList();
            var clusters = new List<List<int>>();
            int s = 0;
            while (s < byAngle.Count)
            {
                int e = s + 1;
                while (e < byAngle.Count && byAngle[e].Angle - byAngle[e - 1].Angle <= AngleTol) e++;
                var group = byAngle.Skip(s).Take(e - s).Select(x => x.Index).ToList();
                s = e;

                // Within one direction: same distance from the origin across the line.
                var first = lines[group[0]].Seg;
                var u = (first.B - first.A) * (1.0 / first.Length);
                var byOffset = group.Select(i => new { Index = i, Off = P2.Cross(u, lines[i].Seg.A) }).OrderBy(x => x.Off).ToList();
                int a = 0;
                while (a < byOffset.Count)
                {
                    int b = a + 1;
                    while (b < byOffset.Count && byOffset[b].Off - byOffset[b - 1].Off <= tol) b++;
                    clusters.Add(byOffset.Skip(a).Take(b - a).Select(x => x.Index).ToList());
                    a = b;
                }
            }
            return clusters;
        }
    }
}
