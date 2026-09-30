using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Drawing-unit sanity checks for the dimension tools. No CAD types are used here.</summary>
    public static class UnitScale
    {
        public static readonly string[] Names = { "Millimetres", "Centimetres", "Metres", "Feet", "Inches" };

        /// <summary>Drawing units in one real millimetre for a unit name, or 0 when the name is unknown.</summary>
        public static double PerMm(string unit)
        {
            switch (unit)
            {
                case "Millimetres": return 1.0;
                case "Centimetres": return 0.1;
                case "Metres": return 0.001;
                case "Feet": return 1.0 / 304.8;
                case "Inches": return 1.0 / 25.4;
                default: return 0.0;
            }
        }

        /// <summary>True when a plan this many real millimetres across is a believable building (2 m to 1 km).</summary>
        public static bool Plausible(double spanMm) => spanMm >= 2000.0 && spanMm <= 1000000.0;

        /// <summary>
        /// The unit that makes a plan <paramref name="span"/> drawing units across a believable building:
        /// metres for a few to a few hundred units, millimetres for thousands, centimetres in between.
        /// Null when none fits.
        /// </summary>
        public static string Guess(double span)
        {
            if (span >= 2 && span <= 500) return "Metres";
            if (span >= 2000 && span <= 1000000) return "Millimetres";
            if (span >= 200 && span < 2000) return "Centimetres";
            return null;
        }
    }
}
