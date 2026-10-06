using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Post positions along a handrail run.</summary>
    public static class RailLayout
    {
        /// <summary>
        /// Posts at every corner and end, with enough extra posts in each straight stretch that none are further
        /// apart than maxSpacing (a stretch is divided into equal parts). A closed run has no end post.
        /// </summary>
        public static List<P2> PostPoints(IList<P2> centre, bool closed, double maxSpacing)
        {
            var p = WallGeometry.Clean(centre, closed);
            var posts = new List<P2>();
            if (p.Count == 0) return posts;
            if (p.Count == 1 || maxSpacing <= 0) { posts.AddRange(p); return posts; }

            int segs = closed ? p.Count : p.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                double len = a.DistanceTo(b);
                int parts = Math.Max(1, (int)Math.Ceiling(len / maxSpacing - 1e-9));
                var step = (b - a) * (1.0 / parts);
                for (int k = 0; k < parts; k++) posts.Add(a + step * k);
            }
            if (!closed) posts.Add(p[p.Count - 1]);
            return posts;
        }
    
        /// <summary>
        /// Like <see cref="PostPoints(IList{P2}, bool, double)"/> for a run that may contain curves cut into short straight pieces. Vertices flagged
        /// smooth lie inside a curve and are not corners: posts go at the real corners and ends, and each stretch between them (curved or straight)
        /// is divided into equal parts no longer than maxSpacing measured along the stretch.
        /// </summary>
        public static List<P2> PostPoints(IList<P2> centre, bool closed, double maxSpacing, IList<bool> smooth)
        {
            if (smooth == null || !smooth.Any(f => f)) return PostPoints(centre, closed, maxSpacing);
            var pts = new List<P2>(); var flags = new List<bool>();
            for (int i = 0; i < centre.Count; i++)
            {
                if (pts.Count > 0 && pts[pts.Count - 1].DistanceTo(centre[i]) < 1e-9) { flags[flags.Count - 1] = flags[flags.Count - 1] && smooth[i]; continue; }
                pts.Add(centre[i]); flags.Add(smooth[i]);
            }
            if (closed && pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) < 1e-9) { pts.RemoveAt(pts.Count - 1); flags.RemoveAt(flags.Count - 1); }
            var posts = new List<P2>();
            if (pts.Count == 0) return posts;
            if (pts.Count == 1) { posts.AddRange(pts); return posts; }
            foreach (double d in PostDistances(pts, closed, maxSpacing, flags)) posts.Add(PointAt(pts, closed, d));
            return posts;
        }

        /// <summary>Distances along the run (from its first point) where posts go. Points must have no repeats.</summary>
        public static List<double> PostDistances(IList<P2> pts, bool closed, double maxSpacing, IList<bool> smooth)
        {
            int n = pts.Count;
            var at = new double[n + 1];
            for (int i = 1; i < n; i++) at[i] = at[i - 1] + pts[i - 1].DistanceTo(pts[i]);
            at[n] = closed ? at[n - 1] + pts[n - 1].DistanceTo(pts[0]) : at[n - 1];
            double total = at[n];

            var corners = new List<double>();
            for (int i = 0; i < n; i++)
                if (smooth == null || !smooth[i] || (!closed && (i == 0 || i == n - 1))) corners.Add(at[i]);
            if (corners.Count == 0) corners.Add(0);            // a closed curve with no corner at all: start anywhere
            var result = new List<double>();
            int stretches = closed ? corners.Count : corners.Count - 1;
            for (int c = 0; c < stretches; c++)
            {
                double a = corners[c], b = closed && c == corners.Count - 1 ? corners[0] + total : corners[c + 1];
                double len = b - a;
                int parts = maxSpacing <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(len / maxSpacing - 1e-9));
                for (int k = 0; k < parts; k++) result.Add((a + len * k / parts) % (total > 0 ? total : 1));
            }
            if (!closed) result.Add(corners[corners.Count - 1]);
            return result;
        }

        /// <summary>The point at a distance along a run (a closed run wraps round).</summary>
        public static P2 PointAt(IList<P2> pts, bool closed, double distance)
        {
            int n = pts.Count, segs = closed ? n : n - 1;
            double d = distance;
            for (int i = 0; i < segs; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                double len = a.DistanceTo(b);
                if (d <= len + 1e-9 || i == segs - 1) return len < 1e-12 ? a : a + (b - a) * (Math.Min(1.0, d / len));
                d -= len;
            }
            return pts[0];
        }
    }
}
