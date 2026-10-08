using System;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Which layers can hold wall faces, CAD-free.</summary>
    public static class WallFaceLayers
    {
        /// <summary>
        /// Take-off layers (<c>MEASURE-LINEAR</c>, <c>MEASURE-DEDUCT</c>, ...) hold measurement lines, never wall faces. <c>HCWWALL</c> draws one
        /// down the centre of each wall, so a door picked in the middle of the wall is nearer that line than either face (defect D-006).
        /// </summary>
        public static bool IsTakeOff(string layer) =>
            layer != null && layer.StartsWith("MEASURE-", StringComparison.OrdinalIgnoreCase);
    }
}
