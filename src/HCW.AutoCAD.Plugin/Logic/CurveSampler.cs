using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Turns arcs into short straight pieces so the straight-line tools (walls, rooms, rails) can follow curves. No CAD types are used here.</summary>
    public static class CurveSampler
    {
        private const int MaxPieces = 720;

        /// <summary>
        /// Points along an arc from startAngle through sweep (radians, positive counter-clockwise), end points included, with
        /// no piece further than maxSagitta from the true arc. A zero sweep gives the start point only.
        /// </summary>
        public static List<P2> ArcPoints(P2 centre, double radius, double startAngle, double sweep, double maxSagitta)
        {
            var pts = new List<P2>();
            if (radius <= 0 || Math.Abs(sweep) < 1e-12) { pts.Add(centre + new P2(Math.Cos(startAngle), Math.Sin(startAngle)) * Math.Max(radius, 0)); return pts; }
            double tolerance = maxSagitta > 0 ? Math.Min(maxSagitta, radius) : radius * 0.01;
            double step = 2.0 * Math.Acos(Math.Max(-1.0, Math.Min(1.0, 1.0 - tolerance / radius)));
            int n = step < 1e-9 ? MaxPieces : (int)Math.Ceiling(Math.Abs(sweep) / step);
            n = Math.Max(1, Math.Min(MaxPieces, n));
            for (int i = 0; i <= n; i++)
            {
                double a = startAngle + sweep * i / n;
                pts.Add(centre + new P2(Math.Cos(a), Math.Sin(a)) * radius);
            }
            return pts;
        }

        /// <summary>
        /// The points of a polyline segment from a to b with the given bulge (tangent of a quarter of the included angle; positive is
        /// counter-clockwise). A bulge of 0 is straight. Both end points are in the result.
        /// </summary>
        public static List<P2> BulgePoints(P2 a, P2 b, double bulge, double maxSagitta)
        {
            if (Math.Abs(bulge) < 1e-9 || a.DistanceTo(b) < 1e-12) return new List<P2> { a, b };
            double chord = a.DistanceTo(b);
            double theta = 4.0 * Math.Atan(bulge);                       // signed included angle
            double r = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            var mid = (a + b) * 0.5;
            var dir = (b - a) * (1.0 / chord);
            var left = new P2(-dir.Y, dir.X);
            double offset = r * Math.Cos(theta / 2.0);                   // centre's distance from the chord, toward the left for a counter-clockwise arc
            var centre = mid + left * (offset * Math.Sign(theta));
            double start = Math.Atan2(a.Y - centre.Y, a.X - centre.X);
            var pts = ArcPoints(centre, r, start, theta, maxSagitta);
            pts[0] = a; pts[pts.Count - 1] = b;                          // the ends are exactly where the polyline had them
            return pts;
        }
    }
}
