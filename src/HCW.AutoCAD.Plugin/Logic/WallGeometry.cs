using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A 2D point. Plain numbers, so the wall maths needs no CAD host.</summary>
    public struct P2
    {
        public double X, Y;
        public P2(double x, double y) { X = x; Y = y; }
        public static P2 operator +(P2 a, P2 b) => new P2(a.X + b.X, a.Y + b.Y);
        public static P2 operator -(P2 a, P2 b) => new P2(a.X - b.X, a.Y - b.Y);
        public static P2 operator *(P2 a, double k) => new P2(a.X * k, a.Y * k);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public static double Dot(P2 a, P2 b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(P2 a, P2 b) => a.X * b.Y - a.Y * b.X;
        public double DistanceTo(P2 o) => (this - o).Length;
    }

    public enum WallJustify { Centre, Left, Right }

    /// <summary>Wall outlines from a centreline: offsets with mitred corners.</summary>
    public static class WallGeometry
    {
        private const double Eps = 1e-9;

        /// <summary>Drops repeated points (and a closing point equal to the first).</summary>
        public static List<P2> Clean(IList<P2> pts, bool closed, double tol = 1e-9)
        {
            var r = new List<P2>();
            foreach (var p in pts)
                if (r.Count == 0 || r[r.Count - 1].DistanceTo(p) > tol) r.Add(p);
            if (closed && r.Count > 1 && r[0].DistanceTo(r[r.Count - 1]) <= tol) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>
        /// The centreline moved d to the left of its direction of travel (negative d moves it right).
        /// Interior corners are mitred; a corner sharper than miterLimit x |d| is bevelled instead.
        /// </summary>
        public static List<P2> Offset(IList<P2> pts, bool closed, double d, double miterLimit = 4.0)
        {
            var p = Clean(pts, closed);
            int n = p.Count;
            var res = new List<P2>();
            if (n < 2 || (closed && n < 3)) return res;

            int segs = closed ? n : n - 1;
            var u = new P2[segs];
            var nl = new P2[segs];
            for (int i = 0; i < segs; i++)
            {
                var v = p[(i + 1) % n] - p[i];
                double len = v.Length;
                u[i] = new P2(v.X / len, v.Y / len);
                nl[i] = new P2(-u[i].Y, u[i].X);
            }

            if (!closed) res.Add(p[0] + nl[0] * d);
            int first = closed ? 0 : 1;
            int last = closed ? n - 1 : n - 2;
            for (int i = first; i <= last; i++)
            {
                int a = (i - 1 + segs) % segs;   // segment arriving at vertex i
                int b = i % segs;                // segment leaving it
                double cr = P2.Cross(u[a], u[b]);
                double dot = P2.Dot(u[a], u[b]);
                if (Math.Abs(cr) < 1e-9)
                {
                    if (dot > 0) res.Add(p[i] + nl[a] * d);
                    else { res.Add(p[i] + nl[a] * d); res.Add(p[i] + nl[b] * d); }
                    continue;
                }
                // Mitre point: p + (n_a + n_b) * d / (1 + n_a . n_b)
                double k = 1.0 + P2.Dot(nl[a], nl[b]);
                var m = (nl[a] + nl[b]) * (d / k);
                if (m.Length > Math.Abs(d) * miterLimit)
                {
                    res.Add(p[i] + nl[a] * d);
                    res.Add(p[i] + nl[b] * d);
                }
                else res.Add(p[i] + m);
            }
            if (!closed) res.Add(p[n - 1] + nl[segs - 1] * d);
            return res;
        }

        /// <summary>
        /// The closed outlines of a wall of the given thickness along a centreline. An open run gives one
        /// outline; a closed run gives two (outer and inner face). Left or Right says which face the line sits on.
        /// </summary>
        public static List<List<P2>> Outline(IList<P2> centre, bool closed, double thickness, WallJustify justify)
        {
            double dl, dr;
            switch (justify)
            {
                case WallJustify.Left: dl = 0; dr = -thickness; break;
                case WallJustify.Right: dl = thickness; dr = 0; break;
                default: dl = thickness / 2.0; dr = -thickness / 2.0; break;
            }
            var left = Offset(centre, closed, dl);
            var right = Offset(centre, closed, dr);
            var loops = new List<List<P2>>();
            if (left.Count == 0 || right.Count == 0) return loops;
            if (closed) { loops.Add(left); loops.Add(right); return loops; }
            var loop = new List<P2>(left);
            for (int i = right.Count - 1; i >= 0; i--) loop.Add(right[i]);
            loops.Add(loop);
            return loops;
        }

        /// <summary>Removes vertices that lie on the straight line between their neighbours.</summary>
        public static List<P2> Simplify(IList<P2> pts, bool closed, double tol)
        {
            var p = Clean(pts, closed, tol);
            bool changed = true;
            while (changed && p.Count > (closed ? 3 : 2))
            {
                changed = false;
                int from = closed ? 0 : 1, to = closed ? p.Count : p.Count - 1;
                for (int i = from; i < to && p.Count > (closed ? 3 : 2); i++)
                {
                    var a = p[(i - 1 + p.Count) % p.Count];
                    var b = p[i];
                    var c = p[(i + 1) % p.Count];
                    var ac = c - a;
                    double len = ac.Length;
                    if (len < Eps) continue;
                    if (Math.Abs(P2.Cross(ac, b - a)) / len <= tol && P2.Dot(b - a, c - b) >= 0)
                    {
                        p.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            return p;
        }
    }
}

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>The two faces of a wall drawn as a single centre line, at any angle.</summary>
    public static class CentreFaces
    {
        /// <summary>
        /// The two faces of the line a to b for a wall of thickness 2 x half: each is the line moved half a thickness along the normal.
        /// Returns null for a line shorter than minLength.
        /// </summary>
        public static P2[][] Of(P2 a, P2 b, double half, double minLength)
        {
            var d = b - a;
            double len = d.Length;
            if (len < minLength || len < 1e-12) return null;
            var n = new P2(-d.Y / len, d.X / len) * half;
            return new[] { new[] { a + n, b + n }, new[] { a - n, b - n } };
        }
    }
}
