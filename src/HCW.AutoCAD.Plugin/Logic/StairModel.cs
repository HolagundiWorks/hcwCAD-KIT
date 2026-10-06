using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public enum StairKind { Single, DogLeg, U, L }

    /// <summary>
    /// Everything that defines a staircase. All lengths are real-size millimetres; the drawing's own units only matter when
    /// the geometry is drawn. The plan and the section are both derived from this, so changing one number regenerates both.
    /// </summary>
    public class StairSpec
    {
        public StairKind Kind = StairKind.DogLeg;
        public double Width = 1200;
        public double FloorHeight = 3300;
        /// <summary>Risers in the whole staircase.</summary>
        public int TotalRisers = 20;
        /// <summary>Risers in the first flight; the second flight gets the rest. Ignored for a single flight.</summary>
        public int FirstFlightRisers = 10;
        /// <summary>Tread (going), riser face to riser face.</summary>
        public double Going = 270;
        /// <summary>Landing length along the direction of the first flight. The landing width is derived.</summary>
        public double LandingLength = 1200;
        public double WaistThickness = 150;
        public double LandingThickness = 150;
        /// <summary>Level of the lower floor (FFL), so the drawing can show real levels.</summary>
        public double StartLevel = 0;
        /// <summary>How far each tread overhangs the riser below it, shown in plan. 0 draws none.</summary>
        public double Nosing = 25;
        /// <summary>U staircase: the gap between the two flights.</summary>
        public double WellWidth = 150;
        /// <summary>U staircase: an open well shows the void; a closed well leaves the gap plain.</summary>
        public bool OpenWell = true;
        /// <summary>Whether the second flight (or the turn of an L) lies to the left of the first, looking up the stair.</summary>
        public bool TurnLeft = true;
        /// <summary>L staircase: three winders (kite treads that turn the corner) take the place of the landing. The landing square is then the stair width.</summary>
        public bool Winders;
        /// <summary>Dog-leg and U: the landing width when it is not the width of both flights (and the well). 0 takes the width from the flights. Never less than one stair width.</summary>
        public double LandingWidthOverride;
        /// <summary>L staircase: draw the section as cut along the first flight (the second flight is seen beyond) instead of developed in a straight line.</summary>
        public bool CutSection;

        // where it was drawn, so AECSTAIREDIT can redraw it in the same place
        public double PlanX, PlanY, PlanAngleDegrees;
        public double SectionX, SectionY;
        public double PlotScale = 50;

        public bool TwoFlights => Kind != StairKind.Single;
        public bool HasWinders => Kind == StairKind.L && Winders;
        /// <summary>The risers the winders add to the two flights: three winder treads are reached by three risers, the first of which is the top riser of flight 1.</summary>
        public int WinderExtraRisers => HasWinders ? 2 : 0;
        public int SecondFlightRisers => TwoFlights ? TotalRisers - FirstFlightRisers - WinderExtraRisers : 0;
        public double Rise => TotalRisers > 0 ? FloorHeight / TotalRisers : 0;

        /// <summary>The number of risers giving a rise closest to the preferred one (at least 2 per flight).</summary>
        public static int AutoRisers(double floorHeight, double preferredRise, bool twoFlights)
        {
            int n = (int)Math.Round(floorHeight / Math.Max(1.0, preferredRise));
            return Math.Max(twoFlights ? 4 : 2, n);
        }

        public static int AutoFirstFlight(int total) => (total + 1) / 2;

        public List<string> ToLines()
        {
            var lines = new List<string>();
            Action<string, string> add = (k, v) => lines.Add(Fields.Join(k, v));
            Func<double, string> d = v => v.ToString("R", CultureInfo.InvariantCulture);
            add("Kind", Kind.ToString());
            add("Width", d(Width)); add("FloorHeight", d(FloorHeight));
            add("TotalRisers", TotalRisers.ToString(CultureInfo.InvariantCulture));
            add("FirstFlightRisers", FirstFlightRisers.ToString(CultureInfo.InvariantCulture));
            add("Going", d(Going)); add("LandingLength", d(LandingLength));
            add("WaistThickness", d(WaistThickness)); add("LandingThickness", d(LandingThickness));
            add("StartLevel", d(StartLevel)); add("Nosing", d(Nosing));
            add("WellWidth", d(WellWidth)); add("OpenWell", OpenWell ? "1" : "0"); add("TurnLeft", TurnLeft ? "1" : "0");
            add("Winders", Winders ? "1" : "0"); add("LandingWidth", d(LandingWidthOverride)); add("CutSection", CutSection ? "1" : "0");
            add("PlanX", d(PlanX)); add("PlanY", d(PlanY)); add("PlanAngle", d(PlanAngleDegrees));
            add("SectionX", d(SectionX)); add("SectionY", d(SectionY)); add("PlotScale", d(PlotScale));
            return lines;
        }

        public static StairSpec FromLines(IEnumerable<string> lines)
        {
            var s = new StairSpec();
            Func<string, double, double> d = (v, fallback) =>
            {
                double x;
                return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x) ? x : fallback;
            };
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (p.Length < 2) continue;
                string v = p[1];
                switch (p[0])
                {
                    case "Kind": StairKind k; if (Enum.TryParse(v, out k)) s.Kind = k; break;
                    case "Width": s.Width = d(v, s.Width); break;
                    case "FloorHeight": s.FloorHeight = d(v, s.FloorHeight); break;
                    case "TotalRisers": s.TotalRisers = (int)d(v, s.TotalRisers); break;
                    case "FirstFlightRisers": s.FirstFlightRisers = (int)d(v, s.FirstFlightRisers); break;
                    case "Going": s.Going = d(v, s.Going); break;
                    case "LandingLength": s.LandingLength = d(v, s.LandingLength); break;
                    case "WaistThickness": s.WaistThickness = d(v, s.WaistThickness); break;
                    case "LandingThickness": s.LandingThickness = d(v, s.LandingThickness); break;
                    case "StartLevel": s.StartLevel = d(v, s.StartLevel); break;
                    case "Nosing": s.Nosing = d(v, s.Nosing); break;
                    case "WellWidth": s.WellWidth = d(v, s.WellWidth); break;
                    case "OpenWell": s.OpenWell = v != "0"; break;
                    case "TurnLeft": s.TurnLeft = v != "0"; break;
                    case "Winders": s.Winders = v == "1"; break;
                    case "LandingWidth": s.LandingWidthOverride = d(v, 0); break;
                    case "CutSection": s.CutSection = v == "1"; break;
                    case "PlanX": s.PlanX = d(v, 0); break;
                    case "PlanY": s.PlanY = d(v, 0); break;
                    case "PlanAngle": s.PlanAngleDegrees = d(v, 0); break;
                    case "SectionX": s.SectionX = d(v, 0); break;
                    case "SectionY": s.SectionY = d(v, 0); break;
                    case "PlotScale": s.PlotScale = d(v, 50); break;
                }
            }
            return s;
        }
    }

    /// <summary>The limits the checks use. They are settings, so an office can apply its own code.</summary>
    public class StairLimits
    {
        public double MaxRise = 190;
        public double MinRise = 100;
        public double MinGoing = 250;
        public double Min2RG = 550;
        public double Max2RG = 700;
    }

    public class StairCheck
    {
        public string Name = "";
        public string Value = "";
        public bool Ok;
        /// <summary>OK, CHECK (outside the office limits) or ERROR (the stair cannot be drawn).</summary>
        public string Status => Ok ? "OK" : (Error ? "ERROR" : "CHECK");
        public bool Error;
    }

    /// <summary>The numbers worked out from a <see cref="StairSpec"/>, and the checks on them. No CAD types are used here.</summary>
    public class StairCalc
    {
        public double Rise;
        public double Going;
        public double TwoRPlusG;
        public int[] FlightRisers = new int[0];
        public int[] FlightTreads = new int[0];
        public double[] FlightLengths = new double[0];
        public double LandingLevel;
        public double TopLevel;
        public double BottomLevel;
        public double LandingWidth;
        /// <summary>The landing length used: the winder square (the stair width) when winders replace the landing.</summary>
        public double LandingLengthUsed;
        /// <summary>Going of a winder along the walkline, half a stair width from the inner corner: a quarter circle shared by three winders.</summary>
        public double WinderGoing;
        public double Pitch;   // degrees
        public List<StairCheck> Checks = new List<StairCheck>();
        public bool CanDraw => Checks.All(c => !c.Error);

        public static StairCalc Calculate(StairSpec s, StairLimits limits = null)
        {
            limits = limits ?? new StairLimits();
            var c = new StairCalc { Going = s.Going, BottomLevel = s.StartLevel, TopLevel = s.StartLevel + s.FloorHeight };
            c.Rise = s.Rise;
            c.TwoRPlusG = 2 * c.Rise + s.Going;
            c.Pitch = s.Going > 0 ? Math.Atan2(c.Rise, s.Going) * 180.0 / Math.PI : 0;

            int r1 = s.TwoFlights ? s.FirstFlightRisers : s.TotalRisers;
            int r2 = s.SecondFlightRisers;
            c.FlightRisers = s.TwoFlights ? new[] { r1, r2 } : new[] { r1 };
            c.FlightTreads = c.FlightRisers.Select(r => Math.Max(0, r - 1)).ToArray();
            c.FlightLengths = c.FlightTreads.Select(t => t * s.Going).ToArray();
            c.LandingLevel = s.TwoFlights ? s.StartLevel + r1 * c.Rise : c.TopLevel;
            c.LandingWidth = s.Kind == StairKind.DogLeg ? 2 * s.Width
                : s.Kind == StairKind.U ? 2 * s.Width + s.WellWidth
                : s.Kind == StairKind.L ? s.Width : 0;
            if ((s.Kind == StairKind.DogLeg || s.Kind == StairKind.U) && s.LandingWidthOverride > 0) c.LandingWidth = Math.Max(s.Width, s.LandingWidthOverride);
            c.LandingLengthUsed = s.HasWinders ? s.Width : s.LandingLength;
            c.WinderGoing = s.HasWinders ? Math.PI * s.Width / 12.0 : 0;

            Action<string, string, bool, bool> add = (n, v, ok, err) => c.Checks.Add(new StairCheck { Name = n, Value = v, Ok = ok, Error = err });
            Func<double, string> mm = v => v.ToString("0.#", CultureInfo.InvariantCulture) + " mm";

            // things that make the stair impossible to draw
            bool valid = s.Width > 0 && s.FloorHeight > 0 && s.Going > 0 && s.TotalRisers >= 2;
            if (!valid) add("Inputs", "width, height, going > 0 and at least 2 risers", false, true);
            if (s.TwoFlights)
            {
                bool split = r1 >= 2 && r2 >= 2;
                add("Flight split", s.HasWinders ? r1 + " + 2 winder + " + r2 + " risers" : r1 + " + " + r2 + " risers", split, !split);
                if (!split) return c;
            }
            if (!valid) return c;

            add("Rise", mm(c.Rise), c.Rise <= limits.MaxRise && c.Rise >= limits.MinRise, false);
            add("Going", mm(s.Going), s.Going >= limits.MinGoing, false);
            add("2R + G", mm(c.TwoRPlusG), c.TwoRPlusG >= limits.Min2RG && c.TwoRPlusG <= limits.Max2RG, false);
            add("Riser consistency", s.TotalRisers + " equal risers", true, false);
            if (s.TwoFlights)
                add("Flight distribution", r1 + " / " + r2, Math.Abs(r1 - r2) <= 1, false);
            add("FFL closure", (s.TotalRisers * c.Rise).ToString("0.###", CultureInfo.InvariantCulture) + " = " + s.FloorHeight.ToString("0.###", CultureInfo.InvariantCulture),
                Math.Abs(s.TotalRisers * c.Rise - s.FloorHeight) < 0.01, false);
            if (s.HasWinders)
            {
                add("Winders", "3 winders, going " + mm(c.WinderGoing) + " on the walkline", c.WinderGoing >= limits.MinGoing, false);
                add("Winder level", StairFormat.Level(c.LandingLevel, false) + " to " + StairFormat.Level(c.LandingLevel + 2 * c.Rise, false), true, false);
            }
            else if (s.TwoFlights)
            {
                add("Landing length", mm(s.LandingLength) + " (width " + mm(s.Width) + ")", s.LandingLength >= s.Width, false);
                add("Landing level", StairFormat.Level(c.LandingLevel, false), true, false);
            }
            return c;
        }
    }

    /// <summary>How lengths and levels are written, in the units the drawing is in. No CAD types are used here.</summary>
    public static class StairFormat
    {
        /// <summary>Real millimetres in one unit of the given name (mm, cm, m, in, ft); 0 for an unknown name.</summary>
        public static double MmPer(string unit)
        {
            switch ((unit ?? "").ToLowerInvariant())
            {
                case "mm": return 1.0;
                case "cm": return 10.0;
                case "m": return 1000.0;
                case "in": return 25.4;
                case "ft": return 304.8;
                default: return 0.0;
            }
        }

        /// <summary>A length as drawn on the dimension: whole millimetres, or feet and inches to 1/8 in.</summary>
        public static string Length(double mm, bool imperial)
        {
            if (!imperial) return Math.Round(mm).ToString("0", CultureInfo.InvariantCulture);
            return FeetInches(mm);
        }

        /// <summary>A level: metres with three decimals (+1.650, ±0.000, -0.150), or feet and inches.</summary>
        public static string Level(double mm, bool imperial)
        {
            if (imperial) return (mm > 0.5 ? "+" : mm < -0.5 ? "-" : "±") + FeetInches(Math.Abs(mm));
            double m = mm / 1000.0;
            if (Math.Abs(m) < 0.0005) return "±0.000";
            return (m > 0 ? "+" : "-") + Math.Abs(m).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string FeetInches(double mm)
        {
            long eighths = (long)Math.Round(Math.Abs(mm) / 25.4 * 8.0);
            long inchesWhole = eighths / 8;
            int frac = (int)(eighths % 8);
            long feet = inchesWhole / 12;
            long inches = inchesWhole % 12;
            string fraction = frac == 0 ? "" : " " + (frac % 2 == 0 ? (frac % 4 == 0 ? (frac / 4) + "/2" : (frac / 2) + "/4") : frac + "/8");
            string body = inches + fraction + "\"";
            string sign = mm < 0 ? "-" : "";
            return feet > 0 ? sign + feet + "'-" + body : sign + body;
        }
    }
}
