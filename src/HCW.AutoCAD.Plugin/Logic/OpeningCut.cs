using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    public struct Seg
    {
        public P2 A, B;
        public Seg(P2 a, P2 b) { A = a; B = b; }
        public double Length => A.DistanceTo(B);
    }

    /// <summary>Where a door or window cuts the two faces of a wall.</summary>
    public class CutPlan
    {
        /// <summary>The face the pick landed on, and the opposite face (indexes into the segment list).</summary>
        public int First, Second;
        /// <summary>Cut interval along each face, measured from its start point, T0 &lt; T1.</summary>
        public double T0, T1, U0, U1;
        /// <summary>Cut corners on the first face (a, b) and the matching corners on the second face.</summary>
        public P2 P1a, P1b, P2a, P2b;
        /// <summary>Unit direction along the first face, and the wall thickness.</summary>
        public P2 Dir;
        public double Thickness;
    }

    /// <summary>Finds the two faces of a wall at a pick and the cut an opening makes in them.</summary>
    public static class OpeningCut
    {
        private const double Eps = 1e-9;

        public static double DistanceToSegment(Seg s, P2 p)
        {
            var v = s.B - s.A;
            double len2 = P2.Dot(v, v);
            if (len2 < Eps) return p.DistanceTo(s.A);
            double t = Math.Max(0, Math.Min(1, P2.Dot(p - s.A, v) / len2));
            return p.DistanceTo(s.A + v * t);
        }

        /// <summary>
        /// Plans an opening of the given width centred on the pick. Returns null with a reason when the pick is
        /// not on a wall, there is no opposite face between minThick and maxThick, or the opening would run
        /// past the end of either face.
        /// </summary>
        public static CutPlan Plan(IList<Seg> segs, P2 pick, double width, double minThick, double maxThick, out string error)
        {
            error = null;
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < segs.Count; i++)
            {
                if (segs[i].Length < Eps) continue;
                double d = DistanceToSegment(segs[i], pick);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0 || bd > maxThick) { error = "no wall face near that point"; return null; }

            var s1 = segs[best];
            var u1 = (s1.B - s1.A) * (1.0 / s1.Length);
            double tc = P2.Dot(pick - s1.A, u1);
            double side = P2.Cross(u1, pick - s1.A);
            double t0 = tc - width / 2.0, t1 = tc + width / 2.0;
            if (t0 < -1e-6 || t1 > s1.Length + 1e-6) { error = "the opening runs past the end of the wall face"; return null; }

            int bestJ = -1; double bestD = double.MaxValue; bool tooShort = false;
            for (int j = 0; j < segs.Count; j++)
            {
                if (j == best || segs[j].Length < Eps) continue;
                var s2 = segs[j];
                var u2 = (s2.B - s2.A) * (1.0 / s2.Length);
                if (Math.Abs(P2.Cross(u1, u2)) > 0.0175) continue;           // about 1 degree
                double dist = Math.Abs(P2.Cross(u1, s2.A - s1.A));
                if (dist < minThick || dist > maxThick) continue;
                // The second face must be on the same side of the first as the pick (when the pick is off the face).
                double side2 = P2.Cross(u1, s2.A - s1.A);
                if (Math.Abs(side) > 1e-6 && Math.Sign(side) != Math.Sign(side2)) continue;
                double ta = P2.Dot(s2.A - s1.A, u1), tb = P2.Dot(s2.B - s1.A, u1);
                double lo = Math.Min(ta, tb), hi = Math.Max(ta, tb);
                if (t0 < lo - 1e-6 || t1 > hi + 1e-6) { tooShort = true; continue; }
                if (dist < bestD) { bestD = dist; bestJ = j; }
            }
            if (bestJ < 0)
            {
                error = tooShort ? "the opening runs past the end of the opposite face" : "no opposite wall face found";
                return null;
            }

            var f2 = segs[bestJ];
            var v2 = (f2.B - f2.A) * (1.0 / f2.Length);
            var plan = new CutPlan { First = best, Second = bestJ, Dir = u1, Thickness = bestD };
            plan.P1a = s1.A + u1 * t0;
            plan.P1b = s1.A + u1 * t1;
            plan.T0 = t0; plan.T1 = t1;
            plan.P2a = f2.A + v2 * P2.Dot(plan.P1a - f2.A, v2);
            plan.P2b = f2.A + v2 * P2.Dot(plan.P1b - f2.A, v2);
            double ua = P2.Dot(plan.P2a - f2.A, v2), ub = P2.Dot(plan.P2b - f2.A, v2);
            plan.U0 = Math.Min(ua, ub); plan.U1 = Math.Max(ua, ub);
            return plan;
        }

        /// <summary>What is left of a segment once the interval t0..t1 (from its start) is removed: 0, 1 or 2 pieces.</summary>
        public static List<Seg> Remove(Seg s, double t0, double t1, double minPiece = 1e-6)
        {
            var res = new List<Seg>();
            double len = s.Length;
            if (len < Eps) return res;
            var u = (s.B - s.A) * (1.0 / len);
            if (t0 > minPiece) res.Add(new Seg(s.A, s.A + u * Math.Min(t0, len)));
            if (t1 < len - minPiece) res.Add(new Seg(s.A + u * Math.Max(t1, 0), s.B));
            return res;
        }
    }
}
