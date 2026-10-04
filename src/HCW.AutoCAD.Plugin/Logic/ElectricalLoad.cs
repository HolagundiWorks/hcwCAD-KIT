using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class LoadRow
    {
        public string Board = "";
        public int LightingPoints, PowerPoints;
        public double LightingWatts, PowerWatts;
        public int LightingCircuits, PowerCircuits;
        /// <summary>Connected load less the diversity factors (lighting watts x LT factor + power watts x PW factor).</summary>
        public double DemandWatts;
        public double TotalWatts => LightingWatts + PowerWatts;
        public int Circuits => LightingCircuits + PowerCircuits;
    }

    /// <summary>One circuit of a board: its name (L1, P1 ...), the points on it and their load.</summary>
    public class CircuitRow
    {
        public string Board = "";
        public string Name = "";
        /// <summary>Lighting, Power, or the kind label of a dedicated circuit (Air conditioner ...).</summary>
        public string Kind = "";
        public string Group = "LT";
        public List<string> Points = new List<string>();
        public double Watts;
        public string FullName => Board + "/" + Name;
    }

    /// <summary>Connected load, circuits and demand per switchboard. No CAD types are used here.</summary>
    public static class ElectricalLoad
    {
        /// <summary>Default connected load per point in watts (typical figures, to be set for the job in the settings).</summary>
        public const string DefaultWatts = "LP=15;FP=60;EF=40;SW1=0;SW2=0;CB=10;P5=100;P15=1000;AC=1500;WP=50;GY=2000;FR=300;OV=1500;WF=20;TV=150;INV=0";
        public const string DefaultDedicated = "AC;GY;OV";
        /// <summary>Diversity factors (demand as a share of connected load) for the lighting and power groups; 1 means none.</summary>
        public const string DefaultDiversity = "LT=1;PW=1";

        /// <summary>Reads "LP=15;FP=60 ..." (also comma separated). Unknown codes are kept out; bad numbers and negatives are skipped.</summary>
        public static Dictionary<string, double> ParseWatts(string text)
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in (text ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                string code = item.Substring(0, eq).Trim();
                double w;
                if (ElectricalKinds.Find(code) == null) continue;
                if (!double.TryParse(item.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w) || w < 0 || double.IsInfinity(w)) continue;
                d[ElectricalKinds.Find(code).Code] = w;
            }
            return d;
        }

        public static HashSet<string> ParseCodes(string text)
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in (text ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var k = ElectricalKinds.Find(c.Trim());
                if (k != null && !k.IsBoard) s.Add(k.Code);
            }
            return s;
        }

        /// <summary>
        /// Reads "LT=0.8;PW=0.6" into the factors for the lighting and power groups. A group not given, or given outside
        /// 0 to 1, has a factor of 1 (no diversity).
        /// </summary>
        public static void ParseDiversity(string text, out double lighting, out double power)
        {
            lighting = power = 1;
            foreach (var item in (text ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                double f;
                if (!double.TryParse(item.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) || f < 0 || f > 1) continue;
                string g = item.Substring(0, eq).Trim().ToUpperInvariant();
                if (g == "LT") lighting = f; else if (g == "PW") power = f;
            }
        }

        /// <summary>
        /// The circuits of one board, in the order they are named. Lighting points are packed in point order into circuits
        /// L1, L2 ... each holding at most ltLimit watts (a point on its own can exceed it). Power: every point of a
        /// dedicated kind gets a circuit of its own, then the rest are packed the same way into the shared circuits.
        /// Dedicated circuits are named P1, P2 ... first, shared ones carry on the numbering. A limit of 0 or less puts
        /// the whole group on one circuit. Points with no watts (switches) are on no circuit.
        /// </summary>
        public static List<CircuitRow> CircuitsOf(string board, IEnumerable<ElLink> links, IDictionary<string, double> watts,
            double ltLimit, double pwLimit, ISet<string> dedicated)
        {
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            var pts = links.GroupBy(x => x.Point).Select(g => g.First())
                .Select(l => new { l.Point, Kind = ElectricalKinds.Find(l.Code) })
                .Where(x => x.Kind != null && !x.Kind.IsBoard)
                .Select(x => new { x.Point, x.Kind, W = watts.TryGetValue(x.Kind.Code, out double w) ? w : 0 })
                .Where(x => x.W > 0)
                .OrderBy(x => x.Point, cmp).ToList();

            var result = new List<CircuitRow>();
            int l = 0, p = 0;
            Func<string, string, string, CircuitRow> open = (name, kind, group) =>
            {
                var c = new CircuitRow { Board = board, Name = name, Kind = kind, Group = group };
                result.Add(c);
                return c;
            };

            // lighting
            CircuitRow cur = null;
            foreach (var x in pts.Where(x => x.Kind.Group == "LT"))
            {
                if (cur == null || (ltLimit > 0 && cur.Watts > 0 && cur.Watts + x.W > ltLimit + 1e-9))
                    cur = open("L" + (++l), "Lighting", "LT");
                cur.Points.Add(x.Point); cur.Watts += x.W;
            }

            // power: dedicated first, then the shared ones
            foreach (var x in pts.Where(x => x.Kind.Group == "PW" && dedicated != null && dedicated.Contains(x.Kind.Code)))
            {
                var c = open("P" + (++p), x.Kind.Label, "PW");
                c.Points.Add(x.Point); c.Watts = x.W;
            }
            cur = null;
            foreach (var x in pts.Where(x => x.Kind.Group == "PW" && !(dedicated != null && dedicated.Contains(x.Kind.Code))))
            {
                if (cur == null || (pwLimit > 0 && cur.Watts > 0 && cur.Watts + x.W > pwLimit + 1e-9))
                    cur = open("P" + (++p), "Power", "PW");
                cur.Points.Add(x.Point); cur.Watts += x.W;
            }
            return result;
        }

        /// <summary>Every board's circuits, boards in natural order.</summary>
        public static List<CircuitRow> Circuits(IEnumerable<string> boardIds, IEnumerable<ElLink> links, IDictionary<string, double> watts,
            double ltLimit, double pwLimit, ISet<string> dedicated)
        {
            var linkList = links.ToList();
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            var all = new List<CircuitRow>();
            foreach (var board in boardIds.Distinct().OrderBy(b => b, cmp))
                all.AddRange(CircuitsOf(board, linkList.Where(l => l.Board == board), watts, ltLimit, pwLimit, dedicated));
            return all;
        }

        public static string[] CircuitHeader => new[] { "Circuit", "Type", "Points", "Load W" };

        public static string[] ToCells(CircuitRow c) => new[]
        {
            c.FullName, c.Kind, c.Points.Count + ": " + string.Join(", ", c.Points), c.Watts.ToString("0", CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// One row per board. A point counts in the load of every board it is wired to (as it does in the matrix schedule);
        /// the total counts each point once. Points with no watts (switches) are not counted as load points.
        /// Circuit counts come from <see cref="CircuitsOf"/>. Demand applies the diversity factors to each group's load.
        /// </summary>
        public static List<LoadRow> Build(IEnumerable<string> boardIds, IEnumerable<ElLink> links, IDictionary<string, double> watts,
            double ltCircuitW, double pwCircuitW, ISet<string> dedicated, out LoadRow total, double ltDiversity = 1, double pwDiversity = 1)
        {
            var linkList = links.ToList();
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            var rows = new List<LoadRow>();
            foreach (var board in boardIds.Distinct().OrderBy(b => b, cmp))
                rows.Add(Row(board, linkList.Where(l => l.Board == board), watts, ltCircuitW, pwCircuitW, dedicated, ltDiversity, pwDiversity));

            total = Row("TOTAL", linkList.GroupBy(l => l.Point).Select(g => g.First()), watts, ltCircuitW, pwCircuitW, dedicated, ltDiversity, pwDiversity);
            total.LightingCircuits = rows.Sum(r => r.LightingCircuits);
            total.PowerCircuits = rows.Sum(r => r.PowerCircuits);
            return rows;
        }

        private static LoadRow Row(string board, IEnumerable<ElLink> links, IDictionary<string, double> watts, double ltLimit, double pwLimit,
            ISet<string> dedicated, double ltDiv, double pwDiv)
        {
            var linkList = links.ToList();
            var row = new LoadRow { Board = board };
            foreach (var l in linkList.GroupBy(x => x.Point).Select(g => g.First()))
            {
                var kind = ElectricalKinds.Find(l.Code);
                if (kind == null || kind.IsBoard) continue;
                double w;
                if (!watts.TryGetValue(kind.Code, out w) || w <= 0) continue;
                if (kind.Group == "LT") { row.LightingPoints++; row.LightingWatts += w; }
                else { row.PowerPoints++; row.PowerWatts += w; }
            }
            var circuits = CircuitsOf(board, linkList, watts, ltLimit, pwLimit, dedicated);
            row.LightingCircuits = circuits.Count(c => c.Group == "LT");
            row.PowerCircuits = circuits.Count(c => c.Group == "PW");
            row.DemandWatts = row.LightingWatts * ltDiv + row.PowerWatts * pwDiv;
            return row;
        }

        public static string[] Header => new[] { "SB no", "Light pts", "Lighting W", "Power pts", "Power W", "Total W", "Total kW", "Demand kW", "Circuits (LT+PW)" };

        public static string[] ToCells(LoadRow r) => new[]
        {
            r.Board,
            r.LightingPoints == 0 ? "" : r.LightingPoints.ToString(CultureInfo.InvariantCulture),
            r.LightingWatts == 0 ? "" : r.LightingWatts.ToString("0", CultureInfo.InvariantCulture),
            r.PowerPoints == 0 ? "" : r.PowerPoints.ToString(CultureInfo.InvariantCulture),
            r.PowerWatts == 0 ? "" : r.PowerWatts.ToString("0", CultureInfo.InvariantCulture),
            r.TotalWatts == 0 ? "" : r.TotalWatts.ToString("0", CultureInfo.InvariantCulture),
            r.TotalWatts == 0 ? "" : (r.TotalWatts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture),
            r.TotalWatts == 0 ? "" : (r.DemandWatts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture),
            r.Circuits == 0 ? "" : r.LightingCircuits + "+" + r.PowerCircuits + " = " + r.Circuits,
        };
    }
}
