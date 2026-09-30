using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A switchboard or a point (light, fan): where it is, and what it is called.</summary>
    public class ElNode
    {
        public string Id = "";
        public bool IsBoard;
        /// <summary>The block's extents (or a small box around its insertion point).</summary>
        public Box Box;
    }

    /// <summary>One line, polyline or arc, as its vertices in order.</summary>
    public class ElWire
    {
        public List<PlanPoint> Points = new List<PlanPoint>();
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
                    if (wires[k].Points.Any(p => Distance(p, nb) <= tolerance)) reaches[n].Add(k);
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
