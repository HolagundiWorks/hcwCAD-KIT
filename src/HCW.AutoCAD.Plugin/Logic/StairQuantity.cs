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
    /// (waist and steps) and the risers; the free edges of the landing are not included. Finishes are the treads, the risers and
    /// the landing top. Skirting is both sides of each flight along the slope.
    /// </summary>
    public class StairQuantities
    {
        public double WaistConcrete, StepsConcrete, LandingConcrete;
        public double Soffit, FlightSides, RiserShuttering, LandingSoffit;
        public double TreadFinish, RiserFinish, LandingFinish;
        public double Skirting;

        public double Concrete => WaistConcrete + StepsConcrete + LandingConcrete;
        public double Shuttering => Soffit + FlightSides + RiserShuttering + LandingSoffit;
        public double Finishes => TreadFinish + RiserFinish + LandingFinish;

        public static StairQuantities Compute(StairSpec s, StairCalc c)
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

            if (s.TwoFlights)
            {
                double area = c.LandingWidth * mm * s.LandingLength * mm;
                q.LandingConcrete = area * s.LandingThickness * mm;
                q.LandingSoffit = area;
                q.LandingFinish = area;
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
                new[] { "Shuttering - landing soffit", f(LandingSoffit), "m2", "landing area; edges not included" },
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
