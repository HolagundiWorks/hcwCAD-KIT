using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A break in both faces of a straight wall, at the same place: an opening drawn as a gap with no door or window block in it.</summary>
    public class WallGap
    {
        public bool Horizontal;
        /// <summary>The two face lines (y for a horizontal wall, x for a vertical one).</summary>
        public double Face1, Face2;
        /// <summary>The gap along the wall (x for a horizontal wall, y for a vertical one).</summary>
        public double Lo, Hi;
        public double Width => Hi - Lo;
    }

    public class TagPoint
    {
        public double Along, Across;
        public string Text = "";
    }

    /// <summary>Finding openings that are only gaps in the wall lines, and matching opening tags to dimension segments. No CAD types are used here.</summary>
    public static class WallGaps
    {
        /// <summary>
        /// Gaps between the ends of collinear wall segments, between minGap and maxGap wide, that occur at the same place in a second parallel face
        /// line no more than maxWall away. Only horizontal and vertical walls are read. Ends of the two faces must agree within matchTol.
        /// </summary>
        public static List<WallGap> Find(IList<WallSegment> segs, double tol, double minGap, double maxGap, double maxWall, double matchTol)
        {
            var found = new List<WallGap>();
            foreach (bool horizontal in new[] { true, false })
            {
                var lines = Lines(segs.Where(s => s.Horizontal == horizontal && Math.Max(Math.Abs(s.X2 - s.X1), Math.Abs(s.Y2 - s.Y1)) > tol).ToList(), horizontal, tol);
                var gaps = lines.Select(l => new { l.Coord, Gaps = GapsOf(l.Intervals, tol, minGap, maxGap) }).ToList();
                for (int i = 0; i < gaps.Count; i++)
                    for (int j = i + 1; j < gaps.Count; j++)
                    {
                        double sep = Math.Abs(gaps[j].Coord - gaps[i].Coord);
                        if (sep <= tol || sep > maxWall) continue;
                        foreach (var a in gaps[i].Gaps)
                            foreach (var b in gaps[j].Gaps)
                                if (Math.Abs(a.Key - b.Key) <= matchTol && Math.Abs(a.Value - b.Value) <= matchTol)
                                    found.Add(new WallGap
                                    {
                                        Horizontal = horizontal, Face1 = gaps[i].Coord, Face2 = gaps[j].Coord,
                                        Lo = (a.Key + b.Key) / 2, Hi = (a.Value + b.Value) / 2,
                                    });
                    }
            }
            return found;
        }

        private class Line { public double Coord; public List<KeyValuePair<double, double>> Intervals = new List<KeyValuePair<double, double>>(); }

        private static List<Line> Lines(IList<WallSegment> segs, bool horizontal, double tol)
        {
            var lines = new List<Line>();
            foreach (var s in segs.OrderBy(s => horizontal ? s.Y1 : s.X1))
            {
                double c = horizontal ? (s.Y1 + s.Y2) / 2 : (s.X1 + s.X2) / 2;
                double lo = horizontal ? Math.Min(s.X1, s.X2) : Math.Min(s.Y1, s.Y2), hi = horizontal ? Math.Max(s.X1, s.X2) : Math.Max(s.Y1, s.Y2);
                var line = lines.FirstOrDefault(l => Math.Abs(l.Coord - c) <= tol);
                if (line == null) { line = new Line { Coord = c }; lines.Add(line); }
                line.Intervals.Add(new KeyValuePair<double, double>(lo, hi));
            }
            return lines;
        }

        private static List<KeyValuePair<double, double>> GapsOf(List<KeyValuePair<double, double>> intervals, double tol, double minGap, double maxGap)
        {
            var gaps = new List<KeyValuePair<double, double>>();
            var sorted = intervals.OrderBy(i => i.Key).ToList();
            double end = sorted[0].Value;
            for (int k = 1; k < sorted.Count; k++)
            {
                double gap = sorted[k].Key - end;
                if (gap > tol && gap >= minGap && gap <= maxGap) gaps.Add(new KeyValuePair<double, double>(end, sorted[k].Key));
                end = Math.Max(end, sorted[k].Value);
            }
            return gaps;
        }

        /// <summary>
        /// The tag (D1, W2 ...) that belongs to a dimension segment from u1 to u2 along a wall at the given edge: the one lying within the segment
        /// (give or take mergeTol) and within band of the edge, nearest the middle. Null when none.
        /// </summary>
        public static string TagFor(IList<TagPoint> tags, double u1, double u2, double edge, double band, double mergeTol)
        {
            double mid = (u1 + u2) / 2;
            return tags.Where(t => Math.Abs(t.Across - edge) <= band && t.Along >= u1 - mergeTol && t.Along <= u2 + mergeTol)
                .OrderBy(t => Math.Abs(t.Along - mid)).Select(t => t.Text).FirstOrDefault();
        }
    }
}
