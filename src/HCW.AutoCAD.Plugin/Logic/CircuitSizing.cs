using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Breaker and cable sizing for circuits, and phase balancing. The ratings and tables are inputs from your own rules; nothing here is a design check. No CAD types are used here.</summary>
    public class SizingOptions
    {
        public double Voltage = 230;
        public double PowerFactor = 1;
        /// <summary>The breaker is chosen for the current times this margin.</summary>
        public double Margin = 1.25;
        /// <summary>1 or 3. With 3, the circuits of each board are spread over the phases R, Y and B.</summary>
        public int Phases = 1;
        public double[] Breakers = { 6, 10, 16, 20, 25, 32, 40, 50, 63 };
        /// <summary>Breaker rating (A) to cable size (mm2): a circuit takes the size of the first rating at or above its breaker.</summary>
        public SortedDictionary<double, double> CableTable = new SortedDictionary<double, double>
        {
            { 6, 1.5 }, { 10, 1.5 }, { 16, 2.5 }, { 20, 4 }, { 25, 4 }, { 32, 6 }, { 40, 10 }, { 50, 10 }, { 63, 16 },
        };
    }

    public static class CircuitSizing
    {
        public static double[] ParseRatings(string text, double[] fallback)
        {
            var list = (text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => { double v; return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 ? v : -1; })
                .Where(v => v > 0).Distinct().OrderBy(v => v).ToArray();
            return list.Length == 0 ? fallback : list;
        }

        /// <summary>Reads "6=1.5;16=2.5". Items that are not number=number are skipped; with none usable the fallback is returned.</summary>
        public static SortedDictionary<double, double> ParseCableTable(string text, SortedDictionary<double, double> fallback)
        {
            var d = new SortedDictionary<double, double>();
            foreach (var item in (text ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                double a, b;
                if (double.TryParse(item.Substring(0, eq).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a) && a > 0
                    && double.TryParse(item.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b) && b > 0) d[a] = b;
            }
            return d.Count == 0 ? fallback : d;
        }

        /// <summary>
        /// Fills in current, breaker, cable size and phase for each circuit. Current is watts over volts and power factor. The breaker is the
        /// smallest rating at or above current x margin (the largest, flagged as over, if none is big enough). The cable is looked up from the breaker.
        /// With three phases, each board's circuits are taken biggest first and each put on the phase carrying least so far (R before Y before B on a tie).
        /// </summary>
        public static void Apply(IList<CircuitRow> circuits, SizingOptions o)
        {
            var ratings = o.Breakers.OrderBy(r => r).ToArray();
            foreach (var c in circuits)
            {
                c.Amps = o.Voltage > 0 && o.PowerFactor > 0 ? c.Watts / (o.Voltage * o.PowerFactor) : 0;
                double need = c.Amps * o.Margin;
                double mcb = ratings.FirstOrDefault(r => r >= need - 1e-9);
                c.BreakerOver = mcb == 0 && ratings.Length > 0;
                c.Breaker = ratings.Length == 0 ? 0 : (c.BreakerOver ? ratings.Last() : mcb);
                var key = o.CableTable.Keys.Where(k => k >= c.Breaker - 1e-9).Select(k => (double?)k).FirstOrDefault();
                c.CableMm2 = key.HasValue ? o.CableTable[key.Value] : (o.CableTable.Count > 0 ? o.CableTable.Values.Last() : 0);
            }

            if (o.Phases == 3)
            {
                string[] names = { "R", "Y", "B" };
                foreach (var board in circuits.GroupBy(c => c.Board))
                {
                    var load = new double[3];
                    foreach (var c in board.OrderByDescending(x => x.Watts).ThenBy(x => x.Name, StringComparer.Ordinal))
                    {
                        int p = Array.IndexOf(load, load.Min());
                        c.Phase = names[p];
                        load[p] += c.Watts;
                    }
                }
            }
            else foreach (var c in circuits) c.Phase = "";
        }

        /// <summary>The load on each phase of one board's circuits, in watts: R, Y, B.</summary>
        public static double[] PhaseLoads(IEnumerable<CircuitRow> boardCircuits)
        {
            var load = new double[3];
            foreach (var c in boardCircuits)
            {
                int i = c.Phase == "R" ? 0 : c.Phase == "Y" ? 1 : c.Phase == "B" ? 2 : -1;
                if (i >= 0) load[i] += c.Watts;
            }
            return load;
        }
    }
}
