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
        public double TotalWatts => LightingWatts + PowerWatts;
        public int Circuits => LightingCircuits + PowerCircuits;
    }

    /// <summary>Connected load and circuit counts per switchboard. No CAD types are used here.</summary>
    public static class ElectricalLoad
    {
        /// <summary>Default connected load per point in watts (typical figures, to be set for the job in the settings).</summary>
        public const string DefaultWatts = "LP=15;FP=60;SW1=0;SW2=0;CB=10;P5=100;P15=1000;AC=1500;WP=50;GY=2000;FR=300;OV=1500;WF=20;TV=150";
        public const string DefaultDedicated = "AC;GY;OV";

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
        /// One row per board. A point counts in the load of every board it is wired to (as it does in the matrix schedule);
        /// the total counts each point once. Points with no watts (switches) are not counted as load points.
        /// Lighting circuits: the lighting load divided by ltCircuitW, rounded up. Power circuits: one for each point of a
        /// dedicated kind (AC, geyser, oven ...), plus the rest of the power load divided by pwCircuitW, rounded up.
        /// A circuit limit of 0 or less means one circuit per group that has any load.
        /// </summary>
        public static List<LoadRow> Build(IEnumerable<string> boardIds, IEnumerable<ElLink> links, IDictionary<string, double> watts,
            double ltCircuitW, double pwCircuitW, ISet<string> dedicated, out LoadRow total)
        {
            var linkList = links.ToList();
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            var rows = new List<LoadRow>();
            foreach (var board in boardIds.Distinct().OrderBy(b => b, cmp))
                rows.Add(Row(board, linkList.Where(l => l.Board == board), watts, ltCircuitW, pwCircuitW, dedicated));

            total = Row("TOTAL", linkList.GroupBy(l => l.Point).Select(g => g.First()), watts, ltCircuitW, pwCircuitW, dedicated);
            total.LightingCircuits = rows.Sum(r => r.LightingCircuits);
            total.PowerCircuits = rows.Sum(r => r.PowerCircuits);
            return rows;
        }

        private static LoadRow Row(string board, IEnumerable<ElLink> links, IDictionary<string, double> watts, double ltLimit, double pwLimit, ISet<string> dedicated)
        {
            var row = new LoadRow { Board = board };
            double restPower = 0;
            int dedicatedCircuits = 0;
            foreach (var l in links.GroupBy(x => x.Point).Select(g => g.First()))
            {
                var kind = ElectricalKinds.Find(l.Code);
                if (kind == null || kind.IsBoard) continue;
                double w;
                if (!watts.TryGetValue(kind.Code, out w) || w <= 0) continue;
                if (kind.Group == "LT") { row.LightingPoints++; row.LightingWatts += w; }
                else
                {
                    row.PowerPoints++; row.PowerWatts += w;
                    if (dedicated != null && dedicated.Contains(kind.Code)) dedicatedCircuits++;
                    else restPower += w;
                }
            }
            row.LightingCircuits = Circuits(row.LightingWatts, ltLimit);
            row.PowerCircuits = dedicatedCircuits + Circuits(restPower, pwLimit);
            return row;
        }

        private static int Circuits(double watts, double limit)
        {
            if (watts <= 0) return 0;
            if (limit <= 0) return 1;
            return (int)Math.Ceiling(watts / limit - 1e-9);
        }

        public static string[] Header => new[] { "SB no", "Light pts", "Lighting W", "Power pts", "Power W", "Total W", "Total kW", "Circuits (LT+PW)" };

        public static string[] ToCells(LoadRow r) => new[]
        {
            r.Board,
            r.LightingPoints == 0 ? "" : r.LightingPoints.ToString(CultureInfo.InvariantCulture),
            r.LightingWatts == 0 ? "" : r.LightingWatts.ToString("0", CultureInfo.InvariantCulture),
            r.PowerPoints == 0 ? "" : r.PowerPoints.ToString(CultureInfo.InvariantCulture),
            r.PowerWatts == 0 ? "" : r.PowerWatts.ToString("0", CultureInfo.InvariantCulture),
            r.TotalWatts == 0 ? "" : r.TotalWatts.ToString("0", CultureInfo.InvariantCulture),
            r.TotalWatts == 0 ? "" : (r.TotalWatts / 1000.0).ToString("0.00", CultureInfo.InvariantCulture),
            r.Circuits == 0 ? "" : r.LightingCircuits + "+" + r.PowerCircuits + " = " + r.Circuits,
        };
    }
}
