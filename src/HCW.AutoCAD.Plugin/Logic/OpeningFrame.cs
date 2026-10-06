using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// How a door or window block sits in a wall, and how to read that back from the block.
    /// Door block: origin on the hinge face, wall body at local y -thickness..0, leaf and swing toward +y.
    /// Window block: origin on the first face, wall body at local y 0..thickness. Local x runs along the opening.
    /// </summary>
    public static class OpeningFrame
    {
        /// <summary>The block's insertion: origin, rotation (radians) and the scale factors that mirror it.</summary>
        public class Placement
        {
            public P2 Origin;
            public double Angle, Sx, Sy;
            /// <summary>Side of the second face relative to the first (+1 left of the face direction, -1 right).</summary>
            public double S2;
            /// <summary>Door: side it swings to (+1 or -1 along the face's left normal). Window: same as S2.</summary>
            public double Sw;
            public bool HingeOnFirst;
        }

        /// <summary>Where the block goes for a cut. side is a point on the side the door opens to; flip puts the hinge at the far end.</summary>
        public static Placement Compute(bool door, CutPlan plan, P2 side, bool flip)
        {
            var u = plan.Dir;
            var n = new P2(-u.Y, u.X);
            double s2 = Math.Sign(P2.Cross(u, plan.P2a - plan.P1a));
            var p = new Placement { Angle = Math.Atan2(u.Y, u.X), S2 = s2 };
            if (!door)
            {
                p.Origin = plan.P1a; p.Sx = 1; p.Sy = s2; p.Sw = s2; p.HingeOnFirst = true;
                return p;
            }
            double sw = Math.Sign(P2.Dot(side - plan.P1a, n));
            if (sw == 0) sw = -s2;
            p.HingeOnFirst = sw == -s2;
            var a = p.HingeOnFirst ? plan.P1a : plan.P2a;
            var b = p.HingeOnFirst ? plan.P1b : plan.P2b;
            p.Origin = flip ? b : a;
            p.Sx = flip ? -1 : 1;
            p.Sy = sw; p.Sw = sw;
            return p;
        }

        /// <summary>The four wall corners an opening block stands on, read from its insertion.</summary>
        public class Corners
        {
            /// <summary>The face the block origin is on (start, end along the opening), and the opposite face below it.</summary>
            public P2 FaceAStart, FaceAEnd, FaceBStart, FaceBEnd;
            public P2 Centre;
            /// <summary>Unit vector toward the side a door swings to; zero for a window.</summary>
            public P2 Swing;
            public bool Flipped;
        }

        public static Corners FromBlock(bool door, P2 origin, double angle, double sx, double sy, double width, double thickness)
        {
            var u = new P2(Math.Cos(angle), Math.Sin(angle));
            var n = new P2(-u.Y, u.X);
            var c = new Corners { FaceAStart = origin, FaceAEnd = origin + u * (sx * width), Flipped = sx < 0 };
            var off = door ? n * (-thickness * sy) : n * (thickness * sy);
            c.FaceBStart = c.FaceAStart + off;
            c.FaceBEnd = c.FaceAEnd + off;
            c.Centre = (c.FaceAStart + c.FaceAEnd) * 0.5;
            c.Swing = door ? n * sy : new P2(0, 0);
            return c;
        }

        private static readonly Regex NameRx = new Regex(@"^HCW_(DD|DS|D|W)_(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase);

        /// <summary>Block name prefix for a kind: HCW_D_ single door, HCW_DD_ double leaf, HCW_DS_ sliding, HCW_W_ window.</summary>
        public static string NamePrefix(bool door, string type)
        {
            if (!door) return "HCW_W_";
            return string.Equals(type, "double", StringComparison.OrdinalIgnoreCase) ? "HCW_DD_"
                : string.Equals(type, "sliding", StringComparison.OrdinalIgnoreCase) ? "HCW_DS_" : "HCW_D_";
        }

        /// <summary>Reads HCW_D_900x230 / HCW_DD_ / HCW_DS_ / HCW_W_1200x230: kind, door type (single, double, sliding; "" for a window), width and wall thickness in millimetres.</summary>
        public static bool TryParseName(string name, out bool door, out string type, out double widthMm, out double thicknessMm)
        {
            door = false; type = ""; widthMm = thicknessMm = 0;
            var m = NameRx.Match(name ?? "");
            if (!m.Success) return false;
            string kind = m.Groups[1].Value.ToUpperInvariant();
            door = kind != "W";
            type = kind == "DD" ? "double" : kind == "DS" ? "sliding" : kind == "D" ? "single" : "";
            return double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out widthMm)
                && double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out thicknessMm);
        }

        /// <summary>Reads HCW_D_900x230 / HCW_W_1200x230: door or window, width and wall thickness in millimetres.</summary>
        public static bool TryParseName(string name, out bool door, out double widthMm, out double thicknessMm)
        {
            string type;
            return TryParseName(name, out door, out type, out widthMm, out thicknessMm);
        }
    }
}
