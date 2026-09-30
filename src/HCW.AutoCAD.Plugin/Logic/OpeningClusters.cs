using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A rectangle in drawing units (the extents of one piece of geometry, or of a whole opening).</summary>
    public struct Box
    {
        public double MinX, MinY, MaxX, MaxY;
        public Box(double minX, double minY, double maxX, double maxY) { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }
        public double CentreX => (MinX + MaxX) / 2.0;
        public double CentreY => (MinY + MaxY) / 2.0;
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    public struct WallSegment
    {
        public double X1, Y1, X2, Y2;
        public WallSegment(double x1, double y1, double x2, double y2) { X1 = x1; Y1 = y1; X2 = x2; Y2 = y2; }
        public bool Horizontal => Math.Abs(Y1 - Y2) <= Math.Abs(X1 - X2);
    }

    /// <summary>
    /// Turns the loose geometry of windows and doors (frame lines, sills, leaves, swing arcs) into one box per opening,
    /// so only the opening's two outer edges along its wall count as jambs. No CAD types are used here.
    /// </summary>
    public static class OpeningClusters
    {
        /// <summary>Groups boxes that overlap or come within <paramref name="tolerance"/> of each other; returns one box per group.</summary>
        public static List<Box> Cluster(IList<Box> boxes, double tolerance)
        {
            int n = boxes.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));

            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    if (a.MinX - tolerance <= b.MaxX && b.MinX - tolerance <= a.MaxX
                        && a.MinY - tolerance <= b.MaxY && b.MinY - tolerance <= a.MaxY)
                        parent[find(i)] = find(j);
                }

            var merged = new Dictionary<int, Box>();
            for (int i = 0; i < n; i++)
            {
                int root = find(i);
                Box current;
                merged[root] = merged.TryGetValue(root, out current)
                    ? new Box(Math.Min(current.MinX, boxes[i].MinX), Math.Min(current.MinY, boxes[i].MinY),
                              Math.Max(current.MaxX, boxes[i].MaxX), Math.Max(current.MaxY, boxes[i].MaxY))
                    : boxes[i];
            }
            return merged.Values.ToList();
        }

        /// <summary>
        /// True when the opening sits in a horizontal wall. The nearest wall segment decides (a door with its swing
        /// arc is nearly square, so its own shape cannot); with no walls, the longer side of the box does.
        /// </summary>
        public static bool InHorizontalWall(Box opening, IList<WallSegment> walls)
        {
            if (walls.Count == 0) return opening.Width >= opening.Height;
            double best = double.MaxValue;
            bool horizontal = opening.Width >= opening.Height;
            foreach (var w in walls)
            {
                double d = DistanceToSegment(opening.CentreX, opening.CentreY, w);
                if (d < best) { best = d; horizontal = w.Horizontal; }
            }
            return horizontal;
        }

        /// <summary>The two jamb points of an opening: its outer edges along the wall, at the middle of the wall's thickness.</summary>
        public static IEnumerable<PlanPoint> Jambs(Box opening, bool horizontalWall)
        {
            if (horizontalWall)
            {
                yield return new PlanPoint(opening.MinX, opening.CentreY);
                yield return new PlanPoint(opening.MaxX, opening.CentreY);
            }
            else
            {
                yield return new PlanPoint(opening.CentreX, opening.MinY);
                yield return new PlanPoint(opening.CentreX, opening.MaxY);
            }
        }

        private static double DistanceToSegment(double px, double py, WallSegment s)
        {
            double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - s.X1) * dx + (py - s.Y1) * dy) / len2));
            double cx = s.X1 + t * dx, cy = s.Y1 + t * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }
    }
}
