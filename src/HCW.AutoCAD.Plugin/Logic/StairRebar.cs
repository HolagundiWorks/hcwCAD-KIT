using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Bar sizes and spacings for the stair slabs. Millimetres. They are inputs, not design: set them from the structural drawing.</summary>
    public class RebarOptions
    {
        public double MainDia = 12, MainSpacing = 150;
        public double DistDia = 8, DistSpacing = 200;
        public double Cover = 25;
        /// <summary>Length each main bar runs into its support, as a multiple of its diameter, at each end.</summary>
        public double AnchorageDiameters = 40;
        /// <summary>Hook at each end of a main bar, as a multiple of its diameter. 0 gives straight ends.</summary>
        public double HookDiameters = 0;
        /// <summary>Crank every second main bar of a flight up at each support (a 45 degree crank over the slab thickness less cover), the rest stay straight.</summary>
        public bool CrankAlternate;
        /// <summary>Top steel at both ends of each flight, over the supports: diameter (0 for none), spacing, and how far it reaches along the flight as a share of its slope length.</summary>
        public double TopDia = 0, TopSpacing = 200, TopSpanShare = 0.3;
    }

    public class BarRow
    {
        public string Mark = "", Description = "";
        /// <summary>The bar's shape for the bending schedule: Straight, Straight with hooks, Cranked, or L bar.</summary>
        public string Shape = "Straight";
        public double Dia;
        public int Nos;
        /// <summary>Length of one bar, metres.</summary>
        public double Length;
        public double TotalLength => Nos * Length;
        /// <summary>Kilograms, from the diameter squared over 162 per metre.</summary>
        public double Weight => TotalLength * Dia * Dia / 162.0;
    }

    /// <summary>
    /// An estimate of the reinforcement in an RCC staircase and its bar schedule. No CAD types are used here.
    /// Each flight has main bars along the slope (the slope length plus anchorage at each end) spaced across the width, and distribution bars
    /// across the width spaced along the slope. The landing has main bars along its length and distribution bars across it.
    /// Bars are one layer, with the cover taken off each end, and no cranks, hooks or top steel: it is a quantity check, not a design.
    /// </summary>
    public static class StairRebar
    {
        public static List<BarRow> Compute(StairSpec s, StairCalc c, RebarOptions o)
        {
            var rows = new List<BarRow>();
            const double mm = 0.001;
            double width = s.Width, cover = o.Cover;
            for (int i = 0; i < c.FlightRisers.Length; i++)
            {
                int risers = c.FlightRisers[i], treads = c.FlightTreads[i];
                if (risers <= 0) continue;
                double run = treads * s.Going, height = risers * c.Rise;
                double slope = Math.Sqrt(run * run + height * height);
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                int mains = Count(width - 2 * cover, o.MainSpacing);
                double hooks = 2 * o.HookDiameters * o.MainDia;
                double straight = (slope + 2 * o.AnchorageDiameters * o.MainDia + hooks) * mm;
                string shape = o.HookDiameters > 0 ? "Straight with hooks" : "Straight";
                // A 45 degree crank lifts the bar by D = waist less cover top and bottom and the bar itself; each crank is 0.42 D longer than the straight bar.
                double crankD = s.WaistThickness - 2 * cover - o.MainDia;
                int cranked = o.CrankAlternate && crankD > 0 ? mains / 2 : 0;
                rows.Add(new BarRow
                {
                    Mark = "M" + n, Description = "Flight " + n + " main", Dia = o.MainDia, Shape = shape,
                    Nos = mains - cranked, Length = straight,
                });
                if (cranked > 0)
                    rows.Add(new BarRow
                    {
                        Mark = "M" + n + "C", Description = "Flight " + n + " main, cranked at both supports", Dia = o.MainDia, Shape = "Cranked",
                        Nos = cranked, Length = straight + 2 * 0.42 * crankD * mm,
                    });
                rows.Add(new BarRow
                {
                    Mark = "D" + n, Description = "Flight " + n + " distribution", Dia = o.DistDia,
                    Nos = Count(slope - 2 * cover, o.DistSpacing), Length = Math.Max(0, width - 2 * cover) * mm,
                });
                if (o.TopDia > 0 && o.TopSpacing > 0)
                    rows.Add(new BarRow
                    {
                        Mark = "T" + n, Description = "Flight " + n + " top steel, both ends", Dia = o.TopDia, Shape = "L bar",
                        Nos = 2 * Count(width - 2 * cover, o.TopSpacing),
                        Length = (o.TopSpanShare * slope + Math.Max(0, s.WaistThickness - 2 * cover)) * mm,
                    });
            }
            if (s.HasWinders)
            {
                double gw = c.WinderGoing;
                int nw = s.WinderTreads;
                double slope = Math.Sqrt(nw * gw * nw * gw + (nw - 1) * c.Rise * (nw - 1) * c.Rise);
                rows.Add(new BarRow
                {
                    Mark = "MW", Description = "Winders main", Dia = o.MainDia,
                    Nos = Count(width - 2 * cover, o.MainSpacing), Length = (slope + 2 * o.AnchorageDiameters * o.MainDia) * mm,
                });
                rows.Add(new BarRow
                {
                    Mark = "DW", Description = "Winders distribution", Dia = o.DistDia,
                    Nos = Count(slope - 2 * cover, o.DistSpacing), Length = Math.Max(0, width - 2 * cover) * mm,
                });
            }
            else if (s.TwoFlights)
            {
                double lw = c.LandingWidth, ll = c.LandingLengthUsed;
                rows.Add(new BarRow
                {
                    Mark = "ML", Description = "Landing main", Dia = o.MainDia,
                    Nos = Count(lw - 2 * cover, o.MainSpacing), Length = Math.Max(0, ll - 2 * cover) * mm,
                });
                rows.Add(new BarRow
                {
                    Mark = "DL", Description = "Landing distribution", Dia = o.DistDia,
                    Nos = Count(ll - 2 * cover, o.DistSpacing), Length = Math.Max(0, lw - 2 * cover) * mm,
                });
            }
            return rows;
        }

        /// <summary>Bars at a spacing across a span: one at each edge and the rest between. A span of 0 or less, or a spacing of 0 or less, gives none.</summary>
        public static int Count(double span, double spacing)
        {
            if (span <= 0 || spacing <= 0) return 0;
            return (int)Math.Floor(span / spacing + 1e-9) + 1;
        }

        public static double TotalWeight(IEnumerable<BarRow> rows) => rows.Sum(r => r.Weight);

        public static string[] Headers => new[] { "Mark", "Description", "Shape", "Dia (mm)", "Nos", "Length each (m)", "Total length (m)", "Weight (kg)" };

        /// <summary>The bar schedule with a total weight row.</summary>
        public static List<string[]> Rows(IList<BarRow> bars, double concreteM3)
        {
            Func<double, string> f = v => v.ToString("0.00", CultureInfo.InvariantCulture);
            var rows = bars.Select(b => new[]
            {
                b.Mark, b.Description, b.Shape, b.Dia.ToString("0.#", CultureInfo.InvariantCulture), b.Nos.ToString(CultureInfo.InvariantCulture),
                f(b.Length), f(b.TotalLength), f(b.Weight),
            }).ToList();
            double kg = TotalWeight(bars);
            rows.Add(new[] { "TOTAL", "", "", "", bars.Sum(b => b.Nos).ToString(CultureInfo.InvariantCulture), "", f(bars.Sum(b => b.TotalLength)), f(kg) });
            if (concreteM3 > 0) rows.Add(new[] { "STEEL PER M3", "", "", "", "", "", "", f(kg / concreteM3) + " kg/m3" });
            return rows;
        }
    }
}
