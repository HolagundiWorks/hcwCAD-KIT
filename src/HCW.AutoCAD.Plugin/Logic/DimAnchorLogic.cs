using System;
using System.Collections.Generic;
using System.Globalization;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A wall vertex a dimension can hang on: which entity (by handle) and which vertex of it, and where it is.</summary>
    public struct Anchor
    {
        public long Handle;
        public int Index;
        public P2 Pt;
        /// <summary>A point of a block (a door or window jamb): its offset from the block's insertion point and the block's rotation when the tie was made.</summary>
        public bool Block;
        public double Dx, Dy, Rot;
    }

    /// <summary>One end of a dimension tied to something: a vertex of a line or polyline, or a point carried by a block.</summary>
    public class DimTie
    {
        public char Axis;
        public long Handle;
        public int Index;
        public bool Block;
        public double Dx, Dy, Rot;
    }

    /// <summary>
    /// How an automatic dimension follows the walls it measures. An end of a dimension along x is tied to a wall vertex with the same x
    /// (axis X), along y to one with the same y (axis Y), and an angled dimension end to the vertex it sits on (axis P). No CAD types are used here.
    /// </summary>
    public static class DimAnchorLogic
    {
        /// <summary>X for a dimension that measures along x (its two ends share a y), Y for one along y, P for any other.</summary>
        public static char AxisOf(P2 a, P2 b, double tol)
        {
            if (Math.Abs(a.Y - b.Y) <= tol && Math.Abs(a.X - b.X) > tol) return 'X';
            if (Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) > tol) return 'Y';
            return 'P';
        }

        /// <summary>
        /// The candidate a dimension end should hang on, or -1: for axis X the vertices whose x is within tol of the end's x, the one nearest
        /// in y wins (and likewise for Y); for P the vertex within tol of the end.
        /// </summary>
        public static int Pick(IList<Anchor> candidates, char axis, P2 target, double tol)
        {
            int best = -1; double bestOther = double.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i].Pt;
                double along, other;
                switch (axis)
                {
                    case 'X': along = Math.Abs(c.X - target.X); other = Math.Abs(c.Y - target.Y); break;
                    case 'Y': along = Math.Abs(c.Y - target.Y); other = Math.Abs(c.X - target.X); break;
                    default: along = c.DistanceTo(target); other = along; break;
                }
                if (along > tol || other >= bestOther) continue;
                best = i; bestOther = other;
            }
            return best;
        }

        /// <summary>Where a dimension end goes when its anchor is now at the given point: only the measured coordinate follows.</summary>
        public static P2 Follow(char axis, P2 current, P2 anchorNow)
        {
            switch (axis)
            {
                case 'X': return new P2(anchorNow.X, current.Y);
                case 'Y': return new P2(current.X, anchorNow.Y);
                default: return anchorNow;
            }
        }

        public static string Encode(char axis, long handle, int index) =>
            "A|" + axis + "|" + handle.ToString("X", CultureInfo.InvariantCulture) + "|" + index.ToString(CultureInfo.InvariantCulture);

        public static bool TryDecode(string text, out char axis, out long handle, out int index)
        {
            axis = 'P'; handle = 0; index = 0;
            var p = (text ?? "").Split('|');
            if (p.Length != 4 || p[0] != "A" || p[1].Length != 1 || "XYP".IndexOf(p[1][0]) < 0) return false;
            axis = p[1][0];
            return long.TryParse(p[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
        }
    
        public static string EncodeBlock(char axis, long handle, double dx, double dy, double rotation)
        {
            Func<double, string> n = v => v.ToString("R", CultureInfo.InvariantCulture);
            return "B|" + axis + "|" + handle.ToString("X", CultureInfo.InvariantCulture) + "|" + n(dx) + "|" + n(dy) + "|" + n(rotation);
        }

        /// <summary>Reads either kind of tie: "A|axis|handle|index" for a vertex or "B|axis|handle|dx|dy|rotation" for a point carried by a block.</summary>
        public static bool TryDecodeTie(string text, out DimTie tie)
        {
            tie = null;
            var p = (text ?? "").Split('|');
            if (p.Length < 2 || p[1].Length != 1 || "XYP".IndexOf(p[1][0]) < 0) return false;
            if (p[0] == "A")
            {
                char a; long h; int i;
                if (!TryDecode(text, out a, out h, out i)) return false;
                tie = new DimTie { Axis = a, Handle = h, Index = i };
                return true;
            }
            if (p[0] != "B" || p.Length != 6) return false;
            long handle; double dx, dy, rot;
            var st = NumberStyles.Float; var inv = CultureInfo.InvariantCulture;
            if (!long.TryParse(p[2], NumberStyles.HexNumber, inv, out handle) || !double.TryParse(p[3], st, inv, out dx)
                || !double.TryParse(p[4], st, inv, out dy) || !double.TryParse(p[5], st, inv, out rot)) return false;
            tie = new DimTie { Axis = p[1][0], Handle = handle, Block = true, Dx = dx, Dy = dy, Rot = rot };
            return true;
        }

        /// <summary>Where a block's carried point is now: the block's insertion point plus its offset turned by however much the block has turned since.</summary>
        public static P2 BlockPoint(P2 insertion, double rotationNow, DimTie tie)
        {
            double turn = rotationNow - tie.Rot, c = Math.Cos(turn), s = Math.Sin(turn);
            return new P2(insertion.X + tie.Dx * c - tie.Dy * s, insertion.Y + tie.Dx * s + tie.Dy * c);
        }
    }
}
