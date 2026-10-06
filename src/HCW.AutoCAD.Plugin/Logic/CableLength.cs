using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Cable length per switchboard from the wiring: the length drawn, a drop at each point, and an allowance. No CAD types are used here.</summary>
    public static class CableLength
    {
        /// <summary>
        /// Metres of wire for each board. Each run of connected wires (a net) gets its drawn length plus dropMetres for every
        /// point on it (the drop down to a switch or up to a fitting), then the allowance. A net that reaches several boards
        /// is shared equally between them; a net that reaches none is left out and counted in unattachedMetres.
        /// Boards with no wire read 0.
        /// </summary>
        public static Dictionary<string, double> PerBoard(IList<ElNode> nodes, IList<ElNet> nets, IList<ElWire> wires,
            double unitsPerMetre, double allowancePct, double dropMetres, out double unattachedMetres)
        {
            if (unitsPerMetre <= 0) throw new ArgumentOutOfRangeException("unitsPerMetre");
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in nodes.Where(n => n.IsBoard)) if (!result.ContainsKey(n.Id)) result[n.Id] = 0;
            unattachedMetres = 0;
            double factor = 1.0 + Math.Max(0, allowancePct) / 100.0;

            foreach (var net in nets)
            {
                double metres = net.Wires.Sum(w => wires[w].Length) / unitsPerMetre + Math.Max(0, dropMetres) * net.Points.Count;
                metres *= factor;
                if (net.Boards.Count == 0) { unattachedMetres += metres; continue; }
                foreach (int b in net.Boards)
                {
                    string id = nodes[b].Id;
                    result[id] = (result.ContainsKey(id) ? result[id] : 0) + metres / net.Boards.Count;
                }
            }
            return result;
        }

        /// <summary>
        /// Metres of cable on each circuit (keyed by "SB-01/L1"). A run of wires (net) is shared between the boards it reaches, then between
        /// the circuits of each board in proportion to the number of their points on that run. Each circuit point adds the drop; the allowance
        /// goes on the lot. A run with no circuit point on it is left out.
        /// </summary>
        public static Dictionary<string, double> PerCircuit(IList<ElNode> nodes, IList<ElNet> nets, IList<ElWire> wires, IEnumerable<CircuitRow> circuits,
            double unitsPerMetre, double allowancePct, double dropMetres)
        {
            if (unitsPerMetre <= 0) throw new ArgumentOutOfRangeException("unitsPerMetre");
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var circuitList = circuits.ToList();
            foreach (var c in circuitList) result[c.FullName] = 0;
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < nodes.Count; i++) index[nodes[i].Id] = i;
            double factor = 1.0 + Math.Max(0, allowancePct) / 100.0;
            double drop = Math.Max(0, dropMetres);

            foreach (var net in nets)
            {
                if (net.Boards.Count == 0) continue;
                var pointSet = new HashSet<int>(net.Points);
                double metres = net.Wires.Sum(w => wires[w].Length) / unitsPerMetre;
                foreach (int b in net.Boards)
                {
                    var mine = circuitList.Where(c => string.Equals(c.Board, nodes[b].Id, StringComparison.OrdinalIgnoreCase))
                        .Select(c => new { Circuit = c, Count = c.Points.Count(p => index.ContainsKey(p) && pointSet.Contains(index[p])) })
                        .Where(x => x.Count > 0).ToList();
                    int total = mine.Sum(x => x.Count);
                    if (total == 0) continue;
                    foreach (var x in mine)
                        result[x.Circuit.FullName] += (metres / net.Boards.Count * x.Count / total + drop * x.Count) * factor;
                }
            }
            return result;
        }

        public static string[] BoqHeader => new[] { "Cable (mm2)", "Cores", "Circuits", "Length (m)" };

        /// <summary>The cable bill of quantities: for each cable size, the number of circuits and the total length (circuits need CableMm2 and LengthM set). A TOTAL row follows.</summary>
        public static List<string[]> Boq(IEnumerable<CircuitRow> circuits, int cores)
        {
            var rows = new List<string[]>();
            double total = 0; int count = 0;
            foreach (var g in circuits.Where(c => c.CableMm2 > 0).GroupBy(c => c.CableMm2).OrderBy(g => g.Key))
            {
                double len = g.Sum(c => c.LengthM ?? 0);
                total += len; count += g.Count();
                rows.Add(new[] { g.Key.ToString("0.##", CultureInfo.InvariantCulture), cores.ToString(CultureInfo.InvariantCulture), g.Count().ToString(CultureInfo.InvariantCulture), len.ToString("0.0", CultureInfo.InvariantCulture) });
            }
            rows.Add(new[] { "TOTAL", "", count.ToString(CultureInfo.InvariantCulture), total.ToString("0.0", CultureInfo.InvariantCulture) });
            return rows;
        }

        public static string[] Header => new[] { "SB no", "Lighting wiring (m)", "Power wiring (m)", "Total (m)" };

        /// <summary>A row per board, then TOTAL. Lengths to one decimal; a zero shows as blank.</summary>
        public static List<string[]> Table(IEnumerable<string> boardIds, IDictionary<string, double> lighting, IDictionary<string, double> power)
        {
            var rows = new List<string[]>();
            double tl = 0, tp = 0;
            foreach (var id in boardIds.Distinct().OrderBy(b => b, Comparer<string>.Create(ElectricalSchedule.NaturalCompare)))
            {
                double l = lighting != null && lighting.ContainsKey(id) ? lighting[id] : 0;
                double p = power != null && power.ContainsKey(id) ? power[id] : 0;
                tl += l; tp += p;
                rows.Add(Cells(id, l, p));
            }
            rows.Add(Cells("TOTAL", tl, tp));
            return rows;
        }

        private static string[] Cells(string board, double l, double p) => new[] { board, Fmt(l), Fmt(p), Fmt(l + p) };

        private static string Fmt(double v) => v <= 0 ? "" : v.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
