using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// Quantities of an RCC staircase from its inputs: concrete, shuttering and finishes. Cubic metres, square metres and
    /// metres. No CAD types are used here.
    ///
    /// Each flight is a waist slab of the given thickness (measured square to the slope) along the slope, with a triangular
    /// step on every riser. The slope length is worked from the flight's run (treads x going) and its height (risers x rise).
    /// The landing is a flat slab of the landing width x length x thickness. Shuttering is the soffit, both sides of each flight
    /// (waist and steps), the risers and the free edges of the landing (its perimeter less the two places the flights join it and the length against walls you give).
    /// Winders are worked as one slab across the stair width: three treads at their going on the walkline and two risers. Finishes are the treads, the risers and
    /// the landing top. Skirting is both sides of each flight along the slope.
    /// </summary>
    public class StairQuantities
    {
        public double WaistConcrete, StepsConcrete, LandingConcrete;
        public double Soffit, FlightSides, RiserShuttering, LandingSoffit, LandingEdges;
        public double TreadFinish, RiserFinish, LandingFinish;
        public double Skirting;

        public double Concrete => WaistConcrete + StepsConcrete + LandingConcrete;
        public double Shuttering => Soffit + FlightSides + RiserShuttering + LandingSoffit + LandingEdges;
        public double Finishes => TreadFinish + RiserFinish + LandingFinish;

        public static StairQuantities Compute(StairSpec s, StairCalc c, double landingWallEdgeMm = 0)
        {
            var q = new StairQuantities();
            const double mm = 0.001;
            double width = s.Width * mm, rise = c.Rise * mm, going = s.Going * mm, waist = s.WaistThickness * mm;

            for (int i = 0; i < c.FlightRisers.Length; i++)
            {
                int risers = c.FlightRisers[i], treads = c.FlightTreads[i];
                if (risers <= 0) continue;
                double run = treads * going, height = risers * rise;
                double slope = Math.Sqrt(run * run + height * height);
                double stepArea = 0.5 * going * rise * risers;               // the triangles of all steps, seen from the side

                q.WaistConcrete += width * waist * slope;
                q.StepsConcrete += stepArea * width;
                q.Soffit += width * slope;
                q.FlightSides += 2.0 * (waist * slope + stepArea);
                q.RiserShuttering += width * rise * risers;
                q.TreadFinish += width * going * treads;
                q.RiserFinish += width * rise * risers;
                q.Skirting += 2.0 * slope;
            }

            if (s.HasWinders)
            {
                // The winders are worked from their real plan shape: the treads are the kite-shaped pieces between the dividing lines, so the slab, the
                // soffit and the finishes use the zone's true area, and each riser is as wide as its dividing line is long.
                int nw = s.WinderTreads;
                var wl = WinderLayout.Build(s.Width, s.WellWidth, s.Kind == StairKind.U);
                const double m2 = 1e-6;
                double zone = wl.ZoneArea * m2;
                double run = nw * c.WinderGoing * mm, height = (nw - 1) * rise;
                double pitchFactor = Math.Sqrt(run * run + height * height) / run;     // slope length per unit of plan run along the walkline
                double stepVolume = 0;
                for (int k = 1; k < nw; k++) stepVolume += 0.5 * rise * wl.TreadAreas[k] * m2;
                double risers = wl.RiserLengths.Sum() * mm;
                double slope = Math.Sqrt(run * run + height * height);
                q.WaistConcrete += zone * waist * pitchFactor;
                q.StepsConcrete += stepVolume;
                q.Soffit += zone * pitchFactor;
                q.FlightSides += 2.0 * (waist * slope + 0.5 * c.WinderGoing * mm * rise * (nw - 1));
                q.RiserShuttering += risers * rise;
                q.TreadFinish += zone;
                q.RiserFinish += risers * rise;
            }
            else if (s.TwoFlights)
            {
                double lw = c.LandingWidth * mm, ll = c.LandingLengthUsed * mm;
                double area = lw * ll;
                q.LandingConcrete = area * s.LandingThickness * mm;
                q.LandingSoffit = area;
                q.LandingFinish = area;
                // free edges: the perimeter less the two places the flights join it and the length against walls (given, not guessed)
                double free = Math.Max(0, 2 * (lw + ll) - 2 * width - Math.Max(0, landingWallEdgeMm) * mm);
                q.LandingEdges = free * s.LandingThickness * mm;
            }
            return q;
        }

        /// <summary>The quantities as take-off rows: item, quantity, unit and how it is worked out.</summary>
        public static string[] Headers => new[] { "Item", "Quantity", "Unit", "Basis" };

        public List<string[]> Rows()
        {
            Func<double, string> f = v => v.ToString("0.000", CultureInfo.InvariantCulture);
            var rows = new List<string[]>
            {
                new[] { "Concrete - waist slab", f(WaistConcrete), "m3", "width x waist thickness x slope length" },
                new[] { "Concrete - steps", f(StepsConcrete), "m3", "1/2 going x rise x width x risers" },
                new[] { "Concrete - landing", f(LandingConcrete), "m3", "landing width x length x thickness" },
                new[] { "CONCRETE TOTAL", f(Concrete), "m3", "" },
                new[] { "Shuttering - soffit", f(Soffit), "m2", "width x slope length" },
                new[] { "Shuttering - flight sides", f(FlightSides), "m2", "both sides, waist and steps" },
                new[] { "Shuttering - risers", f(RiserShuttering), "m2", "width x rise x risers" },
                new[] { "Shuttering - landing soffit", f(LandingSoffit), "m2", "landing area" },
                new[] { "Shuttering - landing edges", f(LandingEdges), "m2", "free edges x thickness: perimeter less the flight joins and the wall edge" },
                new[] { "SHUTTERING TOTAL", f(Shuttering), "m2", "" },
                new[] { "Finish - treads", f(TreadFinish), "m2", "width x going x treads" },
                new[] { "Finish - risers", f(RiserFinish), "m2", "width x rise x risers" },
                new[] { "Finish - landing", f(LandingFinish), "m2", "landing area" },
                new[] { "FINISHES TOTAL", f(Finishes), "m2", "" },
                new[] { "Skirting", f(Skirting), "m", "both sides of each flight along the slope" },
            };
            return rows;
        }
    }
}
