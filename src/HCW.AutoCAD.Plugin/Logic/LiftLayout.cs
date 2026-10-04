using System;
using System.Collections.Generic;
using System.Globalization;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Plan of a lift shaft: wall ring with the landing door opening, clear shaft, car and doors. Local coordinates, door at the bottom (-y), centred on the origin.</summary>
    public class LiftLayout
    {
        public double ClearW, ClearD, Wall, CarW, CarD, DoorW, FrontGap;

        /// <summary>The wall as one closed outline, with the door opening cut through the front wall.</summary>
        public List<P2> WallRing;
        /// <summary>Corners of the clear shaft (inside the walls), counter-clockwise from the bottom left.</summary>
        public List<P2> Clear;
        /// <summary>Corners of the car, counter-clockwise from the bottom left.</summary>
        public List<P2> Car;
        /// <summary>The closed landing door: across the opening at the inner face of the front wall.</summary>
        public P2 LandingDoorA, LandingDoorB;
        /// <summary>The car door: along the car front, centred, the width of the opening.</summary>
        public P2 CarDoorA, CarDoorB;

        /// <summary>Reads 1800x2000 (also 1800*2000 or 1800 X 2000). Returns false with a reason on anything else.</summary>
        public static bool ParseSize(string text, out double w, out double d, out string error)
        {
            w = d = 0; error = null;
            string s = (text ?? "").Replace(" ", "");
            int sep = s.IndexOfAny(new[] { 'x', 'X', '*' });
            if (sep <= 0 || sep == s.Length - 1
                || !double.TryParse(s.Substring(0, sep), NumberStyles.Float, CultureInfo.InvariantCulture, out w)
                || !double.TryParse(s.Substring(sep + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                || w <= 0 || d <= 0 || double.IsInfinity(w) || double.IsInfinity(d))
            {
                w = d = 0;
                error = "\"" + text + "\": give a width and a depth such as 1800x2000";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builds the plan, or returns null with the reason the sizes do not work: the car must fit in the clear shaft
        /// (with its front gap), and the door must fit the car front and leave 100 mm of wall either side.
        /// </summary>
        public static LiftLayout Build(double clearW, double clearD, double wall, double carW, double carD, double doorW, double frontGap, out string error)
        {
            error = null;
            if (clearW <= 0 || clearD <= 0 || carW <= 0 || carD <= 0 || doorW <= 0) { error = "all sizes must be more than 0"; return null; }
            if (wall <= 0) { error = "the wall thickness must be more than 0"; return null; }
            if (frontGap < 0) { error = "the front gap cannot be negative"; return null; }
            if (carW >= clearW) { error = "the car (" + carW + ") is not narrower than the clear shaft (" + clearW + ")"; return null; }
            if (carD + frontGap >= clearD) { error = "the car depth with its front gap (" + (carD + frontGap) + ") does not fit the shaft depth (" + clearD + ")"; return null; }
            if (doorW > carW) { error = "the door (" + doorW + ") is wider than the car (" + carW + ")"; return null; }
            if (doorW > clearW - 200) { error = "the door (" + doorW + ") leaves less than 100 mm of wall either side in a " + clearW + " shaft"; return null; }

            var L = new LiftLayout { ClearW = clearW, ClearD = clearD, Wall = wall, CarW = carW, CarD = carD, DoorW = doorW, FrontGap = frontGap };
            double hw = clearW / 2, hd = clearD / 2, d2 = doorW / 2;
            double ow = hw + wall, od = hd + wall;

            // From the left of the opening on the outer face, round the outer ring to the right of the opening,
            // in through the right jamb, and back round the inner face to the left jamb.
            L.WallRing = new List<P2>
            {
                new P2(-d2, -od),            // left of the opening, outer face
                new P2(-ow, -od),            // outer bottom-left
                new P2(-ow, od),             // outer top-left
                new P2(ow, od),              // outer top-right
                new P2(ow, -od),             // outer bottom-right
                new P2(d2, -od),             // right of the opening, outer face
                new P2(d2, -hd),             // right jamb, inner face
                new P2(hw, -hd),             // inner bottom-right
                new P2(hw, hd),              // inner top-right
                new P2(-hw, hd),             // inner top-left
                new P2(-hw, -hd),            // inner bottom-left
                new P2(-d2, -hd),            // left jamb, inner face
            };
            L.Clear = new List<P2> { new P2(-hw, -hd), new P2(hw, -hd), new P2(hw, hd), new P2(-hw, hd) };

            double cy0 = -hd + frontGap;
            L.Car = new List<P2>
            {
                new P2(-carW / 2, cy0), new P2(carW / 2, cy0), new P2(carW / 2, cy0 + carD), new P2(-carW / 2, cy0 + carD),
            };
            L.LandingDoorA = new P2(-d2, -hd); L.LandingDoorB = new P2(d2, -hd);
            L.CarDoorA = new P2(-d2, cy0); L.CarDoorB = new P2(d2, cy0);
            return L;
        }
    }
}
