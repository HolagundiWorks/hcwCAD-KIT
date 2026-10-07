using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// The arithmetic of ROOMTEXTFIT: how big a line of room text may be, and how the lines of a room name are stacked and centred in the room.
    /// A text's bounding box grows in step with its height, so the largest height that fits is worked out directly. No CAD types are used here.
    /// </summary>
    public static class RoomTextFit
    {
        /// <summary>Share of the room rectangle the text may fill.</summary>
        public const double RoomShare = 0.90;
        /// <summary>The gap between stacked lines, as a share of the smallest line.</summary>
        public const double GapShare = 0.15;

        /// <summary>
        /// The largest height up to <paramref name="cap"/> at which a text whose box is boxW x boxH at <paramref name="height"/> fits in maxW x maxH.
        /// A text shorter than the cap grows to the cap when it fits.
        /// </summary>
        public static double FitHeight(double height, double boxW, double boxH, double maxW, double maxH, double cap)
        {
            if (height <= 0 || cap <= 0) return cap > 0 ? cap : height;
            double t = cap;
            if (boxW > 1e-12) t = Math.Min(t, height * maxW / boxW);
            if (boxH > 1e-12) t = Math.Min(t, height * maxH / boxH);
            return t;
        }

        /// <summary>
        /// Stacks lines of the given sizes along one direction, centred on zero. When the lines and the gaps between them are longer than
        /// <paramref name="avail"/> they are all scaled by the same factor (<paramref name="scale"/>, 1 when they fit) and the gap is worked out again.
        /// Returns the centre of each line, in order, measured from the middle of the group (increasing along the stacking direction).
        /// </summary>
        public static List<double> Stack(IList<double> sizes, double avail, out double scale)
        {
            scale = 1.0;
            int n = sizes.Count;
            var res = new List<double>();
            if (n == 0) return res;
            var s = sizes.ToList();
            double gap = GapShare * s.Min();
            double total = s.Sum() + gap * (n - 1);
            if (total > avail && total > 0.0)
            {
                double k = avail / total;
                scale = k;
                s = s.Select(x => x * k).ToList();
                gap = GapShare * s.Min();
                total = s.Sum() + gap * (n - 1);
            }
            double cursor = -total / 2.0;
            foreach (var x in s)
            {
                res.Add(cursor + x / 2.0);
                cursor += x + gap;
            }
            return res;
        }

        /// <summary>A text turned more than 45 degrees from horizontal is read as vertical: its lines stack side by side.</summary>
        public static bool IsVertical(double rotationRadians) => Math.Abs(Math.Sin(rotationRadians)) > 0.7071;

        /// <summary>Spaces and tabs removed from a room name.</summary>
        public static string Clean(string text) => new string((text ?? "").Where(c => c != ' ' && c != '\t').ToArray());
    }
}
