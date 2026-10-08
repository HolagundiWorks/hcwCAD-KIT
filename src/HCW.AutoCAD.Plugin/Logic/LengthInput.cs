using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Turns a length typed at a prompt (in the drawing's units) back into millimetres, CAD-free.</summary>
    public static class LengthInput
    {
        /// <summary>
        /// <paramref name="typed"/> is in drawing units; <paramref name="unitsPerMm"/> is how many drawing units make one millimetre.
        /// The prompt shows the default rounded to 4 decimals, so when the typed number equals that rounded default the exact default is
        /// returned (1050 mm shown as 3.4449 ft must come back as 1050, not 1049.99). Anything else is converted and rounded to 0.01 mm.
        /// </summary>
        public static double ToMm(double typed, double defaultMm, double unitsPerMm)
        {
            if (unitsPerMm <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerMm));
            if (Math.Abs(typed - Math.Round(defaultMm * unitsPerMm, 4)) < 1e-9) return defaultMm;
            return Math.Round(typed / unitsPerMm, 2);
        }
    }
}
