using System;
using System.Collections.Generic;

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
    }
}
