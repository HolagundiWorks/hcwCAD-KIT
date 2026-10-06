using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A switchboard or a point (light, fan): where it is, and what it is called.</summary>
    public class ElNode
    {
        public string Id = "";
        /// <summary>The kind code (SB, LP, GY ...).</summary>
        public string Code = "";
        public bool IsBoard;
        /// <summary>The block's extents (or a small box around its insertion point).</summary>
        public Box Box;
        /// <summary>The load of this one point in watts when it is rated on its own (a WATTS attribute); null uses the rating of its kind.</summary>
        public double? Watts;
        /// <summary>A one way or two way switch.</summary>
        public bool IsSwitch => Code != null && Code.StartsWith("SW", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One line, polyline or arc, as its vertices in order.</summary>
    public class ElWire
    {
        public List<PlanPoint> Points = new List<PlanPoint>();
        /// <summary>The true length of the wire (an arc is longer than its chord). 0 means use the length of the vertices.</summary>
        public double TrueLength;

        /// <summary>The length to use: the true length when known, otherwise the straight segments between the vertices.</summary>
        public double Length
        {
            get
            {
                if (TrueLength > 0) return TrueLength;
                double sum = 0;
                for (int i = 0; i + 1 < Points.Count; i++)
                {
                    double dx = Points[i + 1].X - Points[i].X, dy = Points[i + 1].Y - Points[i].Y;
                    sum += Math.Sqrt(dx * dx + dy * dy);
                }
                return sum;
            }
        }
    }

    /// <summary>Wires that are joined together, and the boards and points they reach.</summary>
    public class ElNet
    {
        public List<int> Wires = new List<int>();
        public List<int> Boards = new List<int>();
        public List<int> Points = new List<int>();
    }

    /// <summary>
    /// Works out which switchboards and points are wired together from the wire geometry alone. No CAD types are used here.
    ///
    /// Two wires are joined when an end of one touches the other (end to end, or a T). A point (light or fan) is a
    /// junction: wires that reach the same point are joined, which is how a run from one light to the next carries on
    /// to the board. A board is a terminal: two wires that only meet at a board stay separate circuits.
    /// A node touches a wire when one of the wire's vertices is within the tolerance of the node's box.
    /// </summary>
    public static class ElectricalNet
    {
        public static List<ElNet> Build(IList<ElNode> nodes, IList<ElWire> wires, double tolerance)
        {
            int w = wires.Count;
            var parent = Enumerable.Range(0, w).ToArray();
            Func<int, int> find = i =>
            {
                while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; }
                return i;
            };
            Action<int, int> union = (a, b) => { parent[find(a)] = find(b); };

            var boxes = wires.Select(x => BoxOf(x.Points)).ToArray();

            // wires that touch each other: compare only wires whose boxes are within the tolerance
            var order = Enumerable.Range(0, w).OrderBy(i => boxes[i].MinX).ToArray();
            for (int oi = 0; oi < w; oi++)
            {
                int i = order[oi];
                for (int oj = oi + 1; oj < w; oj++)
                {
                    int j = order[oj];
                    if (boxes[j].MinX - tolerance > boxes[i].MaxX) break;
                    if (boxes[i].MinY - tolerance > boxes[j].MaxY || boxes[j].MinY - tolerance > boxes[i].MaxY) continue;
                    if (Touch(wires[i], wires[j], tolerance)) union(i, j);
                }
            }

            // which wires reach each node
            var reaches = new List<int>[nodes.Count];
            for (int n = 0; n < nodes.Count; n++)
            {
                reaches[n] = new List<int>();
                var nb = nodes[n].Box;
                for (int k = 0; k < w; k++)
                {
                    if (boxes[k].MaxX < nb.MinX - tolerance || boxes[k].MinX > nb.MaxX + tolerance
                        || boxes[k].MaxY < nb.MinY - tolerance || boxes[k].MinY > nb.MaxY + tolerance) continue;
                    if (WireReaches(wires[k], nb, tolerance)) reaches[n].Add(k);
                }
                // a point is a junction: everything that reaches it is one circuit
                if (!nodes[n].IsBoard)
                    for (int r = 1; r < reaches[n].Count; r++) union(reaches[n][0], reaches[n][r]);
            }

            var nets = new Dictionary<int, ElNet>();
            for (int k = 0; k < w; k++)
            {
                int root = find(k);
                if (!nets.ContainsKey(root)) nets[root] = new ElNet();
                nets[root].Wires.Add(k);
            }
            for (int n = 0; n < nodes.Count; n++)
            {
                foreach (int root in reaches[n].Select(find).Distinct())
                {
                    var net = nets[root];
                    (nodes[n].IsBoard ? net.Boards : net.Points).Add(n);
                }
            }
            return nets.Values.Where(x => x.Boards.Count + x.Points.Count > 0).ToList();
        }

        /// <summary>
        /// A wire reaches a box when a vertex is within the tolerance of it, or when one of its straight pieces passes within the
        /// tolerance (a wire that runs straight through a symbol connects to it).
        /// </summary>
        public static bool WireReaches(ElWire wire, Box box, double tolerance)
        {
            var pts = wire.Points;
            if (pts.Count == 1) return Distance(pts[0], box) <= tolerance;
            for (int i = 0; i + 1 < pts.Count; i++)
                if (SegmentBoxDistance(pts[i], pts[i + 1], box) <= tolerance) return true;
            return false;
        }

        /// <summary>The shortest distance between a segment and a box (0 when they touch or cross).</summary>
        public static double SegmentBoxDistance(PlanPoint a, PlanPoint b, Box box)
        {
            // Liang-Barsky clip: does the segment cross the box?
            double t0 = 0, t1 = 1, dx = b.X - a.X, dy = b.Y - a.Y;
            double[] p = { -dx, dx, -dy, dy };
            double[] q = { a.X - box.MinX, box.MaxX - a.X, a.Y - box.MinY, box.MaxY - a.Y };
            bool crosses = true;
            for (int i = 0; i < 4 && crosses; i++)
            {
                if (Math.Abs(p[i]) < 1e-15) { if (q[i] < 0) crosses = false; continue; }
                double r = q[i] / p[i];
                if (p[i] < 0) { if (r > t1) crosses = false; else if (r > t0) t0 = r; }
                else { if (r < t0) crosses = false; else if (r < t1) t1 = r; }
            }
            if (crosses) return 0;
            double best = Math.Min(Distance(a, box), Distance(b, box));
            var corners = new[] { new PlanPoint(box.MinX, box.MinY), new PlanPoint(box.MaxX, box.MinY), new PlanPoint(box.MaxX, box.MaxY), new PlanPoint(box.MinX, box.MaxY) };
            foreach (var corner in corners) best = Math.Min(best, DistanceToSegment(corner, a, b));
            return best;
        }

        /// <summary>Distance from a point to a box (0 when inside).</summary>
        private static double Distance(PlanPoint p, Box b)
        {
            double dx = Math.Max(Math.Max(b.MinX - p.X, 0), p.X - b.MaxX);
            double dy = Math.Max(Math.Max(b.MinY - p.Y, 0), p.Y - b.MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Box BoxOf(List<PlanPoint> pts)
        {
            if (pts.Count == 0) return new Box(0, 0, 0, 0);
            return new Box(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }

        /// <summary>An end of one wire lies on the other (within the tolerance), or the reverse.</summary>
        private static bool Touch(ElWire a, ElWire b, double tol)
        {
            return EndOn(a, b, tol) || EndOn(b, a, tol);
        }

        private static bool EndOn(ElWire ends, ElWire on, double tol)
        {
            if (ends.Points.Count == 0) return false;
            foreach (var p in new[] { ends.Points[0], ends.Points[ends.Points.Count - 1] })
            {
                if (on.Points.Count == 1 && Dist(p, on.Points[0]) <= tol) return true;
                for (int i = 0; i + 1 < on.Points.Count; i++)
                    if (DistanceToSegment(p, on.Points[i], on.Points[i + 1]) <= tol) return true;
            }
            return false;
        }

        private static double Dist(PlanPoint a, PlanPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistanceToSegment(PlanPoint p, PlanPoint a, PlanPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new PlanPoint(a.X + t * dx, a.Y + t * dy));
        }
    }
}
