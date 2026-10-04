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
