using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Which lights, fans and other points each switch controls, read from the wiring. No CAD types are used here.</summary>
    public static class SwitchControl
    {
        public class Row
        {
            public string Switch = "";
            public string Type = "";
            public List<string> Controls = new List<string>();
        }

        /// <summary>
        /// Treats every switch as a terminal like a board, so a run of wire that ends at a switch is one leg. A switch controls the points
        /// on the legs that reach it. Switches joined by a leg of their own (the two ends of a two way pair) share what either controls.
        /// Returns pairs of (switch index, point index) into <paramref name="nodes"/>, in switch then point order.
        /// </summary>
        public static List<KeyValuePair<int, int>> Links(IList<ElNode> nodes, IList<ElWire> wires, double tolerance)
        {
            var asTerminals = nodes.Select(n => new ElNode { Id = n.Id, Code = n.Code, IsBoard = n.IsBoard || n.IsSwitch, Box = n.Box }).ToList();
            var nets = ElectricalNet.Build(asTerminals, wires, tolerance);

            // switches that share a leg are partners
            var parent = Enumerable.Range(0, nodes.Count).ToArray();
            Func<int, int> find = i => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            foreach (var net in nets)
            {
                var sw = net.Boards.Where(b => nodes[b].IsSwitch).ToList();
                for (int i = 1; i < sw.Count; i++) parent[find(sw[i])] = find(sw[0]);
            }

            var controlled = new Dictionary<int, HashSet<int>>();
            foreach (var net in nets)
            {
                var pts = net.Points.Where(p => !nodes[p].IsSwitch && !nodes[p].IsBoard).ToList();
                foreach (int s in net.Boards.Where(b => nodes[b].IsSwitch))
                {
                    int root = find(s);
                    if (!controlled.ContainsKey(root)) controlled[root] = new HashSet<int>();
                    foreach (int p in pts) controlled[root].Add(p);
                }
            }

            var links = new List<KeyValuePair<int, int>>();
            for (int s = 0; s < nodes.Count; s++)
            {
                if (!nodes[s].IsSwitch) continue;
                HashSet<int> set;
                if (!controlled.TryGetValue(find(s), out set)) continue;
                foreach (int p in set) links.Add(new KeyValuePair<int, int>(s, p));
            }
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            return links.OrderBy(l => nodes[l.Key].Id, cmp).ThenBy(l => nodes[l.Value].Id, cmp).ToList();
        }

        /// <summary>One row per switch, with the IDs of the points it controls. Switches that control nothing are listed with an empty list.</summary>
        public static List<Row> Rows(IList<ElNode> nodes, IList<KeyValuePair<int, int>> links)
        {
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            return nodes.Select((n, i) => new { n, i }).Where(x => x.n.IsSwitch)
                .Select(x => new Row
                {
                    Switch = x.n.Id,
                    Type = ElectricalKinds.LabelOf(x.n.Code),
                    Controls = links.Where(l => l.Key == x.i).Select(l => nodes[l.Value].Id).OrderBy(id => id, cmp).ToList(),
                })
                .OrderBy(r => r.Switch, cmp).ToList();
        }

        public static string[] Header => new[] { "Switch", "Type", "Controls" };

        public static string[] ToCells(Row r) => new[] { r.Switch, r.Type, r.Controls.Count == 0 ? "not wired to a point" : string.Join(", ", r.Controls) };
    }
}
