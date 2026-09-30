using System;
using System.Collections.Generic;

namespace HCW.AutoCAD.Plugin.Logic
{
    public static class LengthMatch
    {
        /// <summary>
        /// Index of the length closest to <paramref name="measured"/> within <paramref name="tolerance"/>,
        /// or -1 when none is in range or two are equally close. Lengths are in the plugin's rounded units
        /// (centimetres, or eighths of an inch when Imperial).
        /// </summary>
        public static int Closest(IList<int> lengths, int measured, int tolerance)
        {
            int best = -1, bestGap = int.MaxValue;
            bool tie = false;
            for (int i = 0; i < lengths.Count; i++)
            {
                int gap = Math.Abs(lengths[i] - measured);
                if (gap > tolerance) continue;
                if (gap < bestGap) { best = i; bestGap = gap; tie = false; }
                else if (gap == bestGap) tie = true;
            }
            return tie ? -1 : best;
        }
    }
}
