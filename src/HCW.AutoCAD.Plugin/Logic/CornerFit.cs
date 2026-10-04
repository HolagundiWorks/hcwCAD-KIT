using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Trims or extends two lines to meet at their corner, keeping the side of each that was picked. No CAD types are used here.</summary>
    public static class CornerFit
    {
        public class Result
        {
            public Seg A, B;
            public P2 Corner;
        }

        /// <summary>
        /// The corner is where the two lines cross, extended as far as needed. Each line keeps the part on the same side of the
        /// corner as the point picked on it, and ends at the corner: a line that stopped short is extended, one that ran past is trimmed.
        /// Returns null with a reason when the lines are parallel, one has no length, or the corner is absurdly far away.
        /// </summary>
        public static Result Fit(Seg a, P2 pickA, Seg b, P2 pickB, out string error)
        {
            error = null;
            var da = a.B - a.A; var db = b.B - b.A;
            double la = da.Length, lb = db.Length;
            if (la < 1e-12 || lb < 1e-12) { error = "a line has no length"; return null; }
            double den = P2.Cross(da, db);
            if (Math.Abs(den) < 1e-9 * la * lb) { error = "the lines are parallel, so they have no corner"; return null; }

            var ab = b.A - a.A;
            double t = P2.Cross(ab, db) / den;                 // corner along a: A + da * t
            var corner = a.A + da * t;
            double u = P2.Cross(ab, da) / den;                 // corner along b
            if (Math.Abs(t) * la > 1e6 * Math.Max(la, lb) || Math.Abs(u) * lb > 1e6 * Math.Max(la, lb)) { error = "the corner is too far away"; return null; }

            return new Result { A = Keep(a, t, P2.Dot(pickA - a.A, da) / (la * la), corner), B = Keep(b, u, P2.Dot(pickB - b.A, db) / (lb * lb), corner), Corner = corner };
        }

        /// <summary>The line from the end on the picked side to the corner. cornerAt and pickAt are positions along the line, 0 at its start and 1 at its end.</summary>
        private static Seg Keep(Seg s, double cornerAt, double pickAt, P2 corner)
        {
            // Picked before the corner: keep the start. Picked after it: keep the end. Exactly at it: keep the nearer end.
            bool keepStart = Math.Abs(pickAt - cornerAt) < 1e-12 ? cornerAt >= 0.5 : pickAt < cornerAt;
            return keepStart ? new Seg(s.A, corner) : new Seg(corner, s.B);
        }
    }
}
