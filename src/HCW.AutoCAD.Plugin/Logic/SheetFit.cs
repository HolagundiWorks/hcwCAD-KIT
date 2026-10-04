using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Maths for fitting a model-space window into a paper-space area. No CAD host needed.</summary>
    public static class SheetFit
    {
        /// <summary>Plot scales as 1:N, smallest drawing first.</summary>
        public static readonly double[] StandardScales =
            { 1, 2, 5, 10, 20, 25, 50, 75, 100, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000, 2500, 5000, 10000 };

        /// <summary>The smallest standard 1:N that is at least n, so the drawing still fits. Beyond the list n is returned.</summary>
        public static double NextStandard(double n)
        {
            foreach (var s in StandardScales)
                if (s >= n - 1e-9) return s;
            return n;
        }

        /// <summary>
        /// Paper units per model unit that make a model window of modelW x modelH fill
        /// fill (0-1) of an area of areaW x areaH. A window with no size returns 1.
        /// </summary>
        public static double ScaleToFit(double areaW, double areaH, double modelW, double modelH, double fill = 0.95)
        {
            if (areaW <= 0 || areaH <= 0 || (modelW <= 0 && modelH <= 0)) return 1;
            double sx = modelW > 0 ? areaW * fill / modelW : double.MaxValue;
            double sy = modelH > 0 ? areaH * fill / modelH : double.MaxValue;
            return Math.Min(sx, sy);
        }

        /// <summary>
        /// The 1:N ratio of a viewport scale. paperPerMm is paper units per millimetre (1 for mm sheets),
        /// unitsPerMm is drawing units per millimetre (0.001 for a metre drawing).
        /// </summary>
        public static double Ratio(double paperPerModelUnit, double paperPerMm, double unitsPerMm) =>
            paperPerMm / (unitsPerMm * paperPerModelUnit);

        /// <summary>Inverse of <see cref="Ratio"/>: the viewport scale for a 1:ratio plot.</summary>
        public static double ViewportScale(double ratio, double paperPerMm, double unitsPerMm) =>
            paperPerMm / (unitsPerMm * ratio);
    }
}
