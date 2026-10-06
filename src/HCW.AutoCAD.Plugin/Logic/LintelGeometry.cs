using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>The plan of a lintel over an opening: the opening's length plus a bearing each side, as wide as the wall.</summary>
    public static class LintelGeometry
    {
        /// <summary>Total length of the lintel.</summary>
        public static double Length(double openingLength, double bearing) => openingLength + 2 * Math.Max(0, bearing);

        /// <summary>The four corners of the lintel outline, in order, about the centre of the opening.</summary>
        public static P2[] Outline(P2 centre, P2 dir, double openingLength, double wallThickness, double bearing)
        {
            double len = Length(openingLength, bearing);
            var u = dir * (1.0 / Math.Max(1e-12, dir.Length));
            var n = new P2(-u.Y, u.X);
            var a = u * (len / 2); var b = n * (wallThickness / 2);
            return new[] { centre - a - b, centre + a - b, centre + a + b, centre - a + b };
        }
    }
}
