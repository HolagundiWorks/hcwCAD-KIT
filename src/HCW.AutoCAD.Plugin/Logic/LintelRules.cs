using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class LintelFloor
    {
        public string Name = "";
        public double LintelBottom;
    }

    /// <summary>Which lintel bottom height applies to an opening in the schedule. No CAD types are used here.</summary>
    public static class LintelRules
    {
        /// <summary>An opening with no floor named belongs to every floor; one with a floor named belongs to that floor only.</summary>
        public static bool AppliesToFloor(string openingFloor, string floorName) =>
            string.IsNullOrWhiteSpace(openingFloor) || string.Equals(openingFloor.Trim(), (floorName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The lintel bottom to check an opening against: its own when it has one, otherwise that of the floor it is tied to, otherwise the first
        /// floor that has one. 0 when there is none.
        /// </summary>
        public static double For(double ownLintel, string openingFloor, IList<LintelFloor> floors)
        {
            if (ownLintel > 0) return ownLintel;
            if (!string.IsNullOrWhiteSpace(openingFloor))
            {
                var tied = floors.FirstOrDefault(f => AppliesToFloor(openingFloor, f.Name) && f.LintelBottom > 0);
                if (tied != null) return tied.LintelBottom;
            }
            var first = floors.FirstOrDefault(f => f.LintelBottom > 0);
            return first == null ? 0 : first.LintelBottom;
        }
    }
}
