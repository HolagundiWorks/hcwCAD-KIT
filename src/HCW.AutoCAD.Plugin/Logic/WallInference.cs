using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A wall read back from an outline: its centre line and thickness, in drawing units.</summary>
    public class InferredWall
    {
        public P2 A, B;
        public double Thickness;
    }

    /// <summary>
    /// Reads walls back out of a closed outline that was drawn by hand: two straight edges that face each other (run in opposite directions) a wall's thickness
    /// apart and overlap along their length are the two faces of one wall. Its centre line is the middle of the two faces along the longer of them. No CAD types are used here.
    /// </summary>
    public static class WallInference
    {
        public static List<InferredWall> Infer(IList<P2> outline, double minThickness, double maxThickness, double tol)
        {
            var walls = new List<InferredWall>();
            int n = outline.Count;
            if (n < 4) return walls;
            for (int i = 0; i < n; i++)
            {
                var a1 = outline[i]; var b1 = outline[(i + 1) % n];
                var d1 = b1 - a1;
                if (d1.Length < tol) continue;
                var u = d1 * (1.0 / d1.Length);
                for (int j = i + 1; j < n; j++)
                {
                    var a2 = outline[j]; var b2 = outline[(j + 1) % n];
                    var d2 = b2 - a2;
                    if (d2.Length < tol) continue;
                    // facing edges run opposite ways
                    if (P2.Dot(u, d2 * (1.0 / d2.Length)) > -1 + 1e-6) continue;
                    var normal = new P2(-u.Y, u.X);
                    double t1 = P2.Dot(a2 - a1, normal), t2 = P2.Dot(b2 - a1, normal);
                    if (Math.Abs(t1 - t2) > tol) continue;                       // not parallel to within tolerance
                    double t = (t1 + t2) / 2;
                    if (Math.Abs(t) < minThickness || Math.Abs(t) > maxThickness) continue;
                    // the overlap along u of the two edges
                    double s1a = 0, s1b = d1.Length, s2a = P2.Dot(a2 - a1, u), s2b = P2.Dot(b2 - a1, u);
                    double lo = Math.Max(Math.Min(s1a, s1b), Math.Min(s2a, s2b)), hi = Math.Min(Math.Max(s1a, s1b), Math.Max(s2a, s2b));
                    if (hi - lo <= Math.Abs(t) + tol) continue;                          // shorter than it is thick: a corner, not a wall
                    // The wall runs the full length of its longer face: at a corner the outer face reaches the far side and the wall covers the corner square.
                    lo = Math.Min(Math.Min(s1a, s1b), Math.Min(s2a, s2b)); hi = Math.Max(Math.Max(s1a, s1b), Math.Max(s2a, s2b));
                    walls.Add(new InferredWall { A = a1 + u * lo + normal * (t / 2), B = a1 + u * hi + normal * (t / 2), Thickness = Math.Abs(t) });
                }
            }
            // the same wall can be found from more than one pair of edges
            var kept = new List<InferredWall>();
            foreach (var w in walls.OrderByDescending(w => w.A.DistanceTo(w.B)))
                if (!kept.Any(k => Math.Abs(k.Thickness - w.Thickness) <= tol && Near(k, w, tol))) kept.Add(w);
            return kept;
        }

        private static bool Near(InferredWall a, InferredWall b, double tol) =>
            (a.A.DistanceTo(b.A) <= tol && a.B.DistanceTo(b.B) <= tol) || (a.A.DistanceTo(b.B) <= tol && a.B.DistanceTo(b.A) <= tol);
    }
}
