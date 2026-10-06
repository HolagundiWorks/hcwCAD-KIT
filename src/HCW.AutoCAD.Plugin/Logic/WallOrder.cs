using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Numbering order for walls and a colour for each deduction name in the take-off. No CAD types are used here.</summary>
    public static class WallOrder
    {
        /// <summary>
        /// Order in which walls are numbered room by room: rooms left to right then bottom to top by their centre; each wall belongs to the room
        /// whose outline its midpoint is nearest (a wall shared by two rooms goes to the first of them); inside a room the walls follow its outline
        /// anticlockwise from the bottom. Walls with no rooms at all come last, left to right. Returns indexes into <paramref name="mids"/>.
        /// </summary>
        public static List<int> ByRoom(IList<P2> mids, IList<IList<P2>> rooms)
        {
            var order = new List<int>();
            if (mids.Count == 0) return order;
            var roomOrder = Enumerable.Range(0, rooms.Count).Where(i => rooms[i].Count >= 3)
                .OrderBy(i => Centre(rooms[i]).X).ThenBy(i => Centre(rooms[i]).Y).ToList();
            if (roomOrder.Count == 0) return mids.Select((m, i) => i).OrderBy(i => mids[i].X).ThenBy(i => mids[i].Y).ToList();

            var owner = new int[mids.Count];
            for (int w = 0; w < mids.Count; w++)
            {
                double best = double.MaxValue; int pick = roomOrder[0];
                foreach (int r in roomOrder)
                {
                    double d = DistanceToOutline(rooms[r], mids[w]);
                    if (d < best - 1e-9) { best = d; pick = r; }
                }
                owner[w] = pick;
            }
            foreach (int r in roomOrder)
            {
                var c = Centre(rooms[r]);
                order.AddRange(Enumerable.Range(0, mids.Count).Where(w => owner[w] == r)
                    .OrderBy(w => Angle(mids[w], c)).ThenBy(w => mids[w].X));
            }
            return order;
        }

        private static P2 Centre(IList<P2> poly) => new P2(poly.Average(p => p.X), poly.Average(p => p.Y));

        private static double Angle(P2 p, P2 centre)
        {
            double a = Math.Atan2(p.Y - centre.Y, p.X - centre.X) + Math.PI / 2;      // 0 at the bottom, increasing anticlockwise
            a %= 2 * Math.PI;
            if (a < 0) a += 2 * Math.PI;
            return Math.Round(a, 9);
        }

        private static double DistanceToOutline(IList<P2> poly, P2 pt)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
                best = Math.Min(best, OpeningCut.DistanceToSegment(new Seg(poly[i], poly[(i + 1) % poly.Count]), pt));
            return best;
        }
    }

    /// <summary>One ACI colour per name, the same every run: names are sorted and take the palette in turn.</summary>
    public static class DeductionColours
    {
        /// <summary>Strong colours that stay apart on a dark or light background (no white, grey or black).</summary>
        public static readonly short[] Palette = { 1, 2, 3, 4, 5, 6, 30, 40, 94, 150, 210, 230 };

        public static Dictionary<string, short> Assign(IEnumerable<string> names)
        {
            var map = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            var sorted = names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sorted.Count; i++) map[sorted[i]] = Palette[i % Palette.Length];
            return map;
        }
    }
}
