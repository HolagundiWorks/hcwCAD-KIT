using System;
using System.Globalization;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Numbers and angles for level marks, section markers and slope arrows. No CAD host needed.</summary>
    public static class SymbolMath
    {
        /// <summary>A level in metres as drawn: +3.150, -0.450, ±0.000 (anything under half a millimetre).</summary>
        public static string LevelText(double metres)
        {
            double r = Math.Round(metres, 3, MidpointRounding.AwayFromZero);
            if (Math.Abs(r) < 0.0005) return "±0.000";
            return (r > 0 ? "+" : "-") + Math.Abs(r).ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>The level of a point from a datum: the datum level plus the height above the datum point, in metres.</summary>
        public static double LevelFromDatum(double datumLevel, double datumY, double y, double unitsPerMetre)
        {
            if (unitsPerMetre <= 0) throw new ArgumentOutOfRangeException("unitsPerMetre");
            return datumLevel + (y - datumY) / unitsPerMetre;
        }

        /// <summary>An angle turned into (-90, 90] degrees so text along a line never reads upside down. Radians in and out.</summary>
        public static double ReadableAngle(double angle)
        {
            double a = Math.Atan2(Math.Sin(angle), Math.Cos(angle));       // -180 .. 180
            if (a > Math.PI / 2 + 1e-12) a -= Math.PI;
            else if (a <= -Math.PI / 2 + 1e-12) a += Math.PI;
            return a;
        }

        /// <summary>
        /// The unit normal of the line a-b that points toward the side point; the left normal when the point is on the line.
        /// A zero-length line gives (0, 1).
        /// </summary>
        public static P2 LookNormal(P2 a, P2 b, P2 side)
        {
            var d = b - a;
            double len = d.Length;
            if (len < 1e-12) return new P2(0, 1);
            var n = new P2(-d.Y / len, d.X / len);
            return P2.Dot(side - a, n) < 0 ? n * -1 : n;
        }
    }
}
