using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class LiftSectionOptions
    {
        public int Floors = 4;
        public double FloorHeight = 3000;
        public double PitDepth = 1400;
        public double Overhead = 4200;
        public bool MachineRoom = true;
        public double MachineRoomHeight = 2400;
        public double Wall = 230, Slab = 200;
        public double CarHeight = 2400, DoorHeight = 2100, CarFloor = 100;
        public double TextHeight = 125;
    }

    /// <summary>
    /// Section through a lift shaft, cut along its depth. x runs from the inside face of the front wall (0) to the back, y is the height above the lowest landing.
    /// Roles: WALL (shaft walls and slabs), DOOR (landing doors and the car), TEXT, LEVEL (landing levels).
    /// </summary>
    public static class LiftSection
    {
        public static GDrawing Build(LiftLayout plan, LiftSectionOptions o, out string error)
        {
            error = null;
            if (o.Floors < 2) { error = "a lift serves at least two floors"; return null; }
            if (o.FloorHeight <= o.DoorHeight) { error = "the floor-to-floor height (" + o.FloorHeight + ") is not more than the door height (" + o.DoorHeight + ")"; return null; }
            if (o.PitDepth <= 0 || o.Overhead <= o.CarHeight) { error = "the overhead (" + o.Overhead + ") must be more than the car height (" + o.CarHeight + ")"; return null; }
            double d = plan.ClearD, w = o.Wall;
            double travel = (o.Floors - 1) * o.FloorHeight;
            double top = travel + o.Overhead;                     // underside of the top slab
            var g = new GDrawing();

            Action<double, double, double, double, string, bool> rect = (x0, y0, x1, y1, role, hatch) =>
                g.Polys.Add(new GPoly
                {
                    Layer = role, Closed = true, Hatch = hatch,
                    Pts = { new PlanPoint(x0, y0), new PlanPoint(x1, y0), new PlanPoint(x1, y1), new PlanPoint(x0, y1) },
                });

            // Pit slab, the two shaft walls (the front wall is cut at each landing for the door) and the top slab.
            rect(-w, -o.PitDepth - o.Slab, d + w, -o.PitDepth, "WALL", true);
            rect(d, -o.PitDepth, d + w, top, "WALL", true);
            double y = -o.PitDepth;
            for (int i = 0; i < o.Floors; i++)
            {
                double lvl = i * o.FloorHeight;
                rect(-w, y, 0, lvl, "WALL", true);                // front wall up to the door head
                rect(-w, lvl, 0, lvl + o.DoorHeight, "DOOR", false);
                y = lvl + o.DoorHeight;
                g.Polys.Add(new GPoly { Layer = "LEVEL", Pts = { new PlanPoint(-w - 600, lvl), new PlanPoint(-w, lvl) } });
                g.Texts.Add(new GText { Text = "+" + (lvl / 1000).ToString("0.00", CultureInfo.InvariantCulture), X = -w - 700, Y = lvl + o.TextHeight * 0.5, Height = o.TextHeight });
            }
            rect(-w, y, 0, top, "WALL", true);
            double slabTop = top + o.Slab;
            rect(-w, top, d + w, slabTop, "WALL", true);

            // The car at the lowest landing, with the gap in front of it.
            double cx0 = plan.FrontGap, cx1 = plan.FrontGap + plan.CarD;
            rect(cx0, o.CarFloor, cx1, o.CarFloor + o.CarHeight, "DOOR", false);
            g.Texts.Add(new GText { Text = "CAR", X = (cx0 + cx1) / 2, Y = o.CarFloor + o.CarHeight / 2, Height = o.TextHeight, Centre = true });

            if (o.MachineRoom)
            {
                double mrTop = slabTop + o.MachineRoomHeight;
                rect(-w, slabTop, 0, mrTop, "WALL", true);
                rect(d, slabTop, d + w, mrTop, "WALL", true);
                rect(-w, mrTop, d + w, mrTop + o.Slab, "WALL", true);
                g.Texts.Add(new GText { Text = "MACHINE ROOM", X = d / 2, Y = slabTop + o.MachineRoomHeight / 2, Height = o.TextHeight, Centre = true });
            }

            // Pit and overhead sizes; the overhead is from the top landing to the underside of the slab.
            g.Dims.Add(new GDim { A = new PlanPoint(d, -o.PitDepth), B = new PlanPoint(d, 0), Line = new PlanPoint(d + w + 600, 0), Text = "PIT " + o.PitDepth.ToString("0", CultureInfo.InvariantCulture) });
            g.Dims.Add(new GDim { A = new PlanPoint(d, travel), B = new PlanPoint(d, top), Line = new PlanPoint(d + w + 600, 0), Text = "OVERHEAD " + o.Overhead.ToString("0", CultureInfo.InvariantCulture) });
            g.Dims.Add(new GDim { A = new PlanPoint(d, 0), B = new PlanPoint(d, travel), Line = new PlanPoint(d + w + 1400, 0), Text = "TRAVEL " + travel.ToString("0", CultureInfo.InvariantCulture) });
            return g;
        }
    }

    public class EscalatorOptions
    {
        public double RiseMm = 4000;
        public double AngleDeg = 30;
        /// <summary>Nominal step width: 600, 800 or 1000.</summary>
        public double StepWidth = 1000;
        /// <summary>Balustrade and skirt on each side of the steps.</summary>
        public double SideMm = 300;
        /// <summary>Flat landing length (machine pit and comb plate) at each end, beyond the inclined run.</summary>
        public double LandingMm = 2500;
        public double StepDepth = 400;
        public double TrussDepth = 1100;
        public double HandrailHeight = 900;
        public double TextHeight = 125;
    }

    /// <summary>
    /// An escalator drawn in plan and in side elevation from its rise, angle, width and landings. Plan: x along the run from the lower landing edge, y across.
    /// Elevation: x along the run, y the height above the lower floor. Roles: WALL, TREAD, ARROW, TEXT, RAIL.
    /// </summary>
    public static class Escalator
    {
        public static double Run(EscalatorOptions o) => o.RiseMm / Math.Tan(o.AngleDeg * Math.PI / 180);
        public static double Length(EscalatorOptions o) => Run(o) + 2 * o.LandingMm;
        public static double Incline(EscalatorOptions o) => Math.Sqrt(Run(o) * Run(o) + o.RiseMm * o.RiseMm);

        public static string Check(EscalatorOptions o)
        {
            if (o.RiseMm <= 0) return "the rise must be more than 0";
            if (o.AngleDeg < 20 || o.AngleDeg > 35) return "the angle (" + o.AngleDeg + ") is outside 20 to 35 degrees; 30 is usual, 35 only up to 6 m rise";
            if (o.AngleDeg > 30 && o.RiseMm > 6000) return "a " + o.AngleDeg + " degree escalator is limited to 6 m rise; use 30 degrees";
            if (o.StepWidth < 500 || o.StepWidth > 1200) return "the step width (" + o.StepWidth + ") is outside 500 to 1200";
            if (o.LandingMm < 1200) return "the landing (" + o.LandingMm + ") is shorter than 1200 mm";
            if (o.StepDepth <= 0) return "the step depth must be more than 0";
            return null;
        }

        public static GDrawing Plan(EscalatorOptions o)
        {
            double len = Length(o), wid = o.StepWidth + 2 * o.SideMm, run = Run(o);
            var g = new GDrawing();
            Func<double, double, PlanPoint> p = (x, y) => new PlanPoint(x, y);
            g.Polys.Add(new GPoly { Layer = "WALL", Closed = true, Pts = { p(0, 0), p(len, 0), p(len, wid), p(0, wid) } });
            // The steps lie between the balustrades; their risers show in plan at the tread pitch.
            g.Polys.Add(new GPoly { Layer = "TREAD", Closed = true, Pts = { p(0, o.SideMm), p(len, o.SideMm), p(len, o.SideMm + o.StepWidth), p(0, o.SideMm + o.StepWidth) } });
            double pitch = o.StepDepth * Math.Cos(o.AngleDeg * Math.PI / 180);
            int steps = Math.Max(1, (int)Math.Round(run / pitch));
            for (int i = 0; i <= steps; i++)
            {
                double x = o.LandingMm + run * i / steps;
                g.Polys.Add(new GPoly { Layer = "TREAD", Pts = { p(x, o.SideMm), p(x, o.SideMm + o.StepWidth) } });
            }
            // Comb plates at the two landings.
            foreach (double x in new[] { o.LandingMm * 0.5, len - o.LandingMm * 0.5 })
                g.Polys.Add(new GPoly { Layer = "TREAD", Pts = { p(x, o.SideMm), p(x, o.SideMm + o.StepWidth) } });
            double mid = wid / 2;
            g.Polys.Add(new GPoly { Layer = "ARROW", Pts = { p(len * 0.3, mid), p(len * 0.7, mid) } });
            g.Polys.Add(new GPoly { Layer = "ARROW", Pts = { p(len * 0.7 - 250, mid - 150), p(len * 0.7, mid), p(len * 0.7 - 250, mid + 150) } });
            g.Texts.Add(new GText { Text = "UP", X = len * 0.5, Y = mid + o.TextHeight * 1.5, Height = o.TextHeight, Centre = true });
            g.Texts.Add(new GText
            {
                Text = "ESCALATOR " + o.StepWidth.ToString("0", CultureInfo.InvariantCulture) + " WIDE, " + o.AngleDeg.ToString("0.#", CultureInfo.InvariantCulture) + " DEG",
                X = len * 0.5, Y = mid - o.TextHeight * 1.5, Height = o.TextHeight * 0.8, Centre = true,
            });
            return g;
        }

        public static GDrawing Elevation(EscalatorOptions o)
        {
            double L = o.LandingMm, run = Run(o), rise = o.RiseMm, t = o.TrussDepth;
            var g = new GDrawing();
            Func<double, double, PlanPoint> p = (x, y) => new PlanPoint(x, y);
            // The truss: its top follows the steps, its bottom runs parallel one truss depth below, and the ends are square.
            g.Polys.Add(new GPoly
            {
                Layer = "WALL", Closed = true,
                Pts = { p(0, 0), p(L, 0), p(L + run, rise), p(2 * L + run, rise), p(2 * L + run, rise - t), p(L + run, rise - t), p(L, -t), p(0, -t) },
            });
            double h = o.HandrailHeight;
            g.Polys.Add(new GPoly { Layer = "RAIL", Pts = { p(0, h), p(L, h), p(L + run, rise + h), p(2 * L + run, rise + h) } });
            g.Polys.Add(new GPoly { Layer = "LEVEL", Pts = { p(-600, 0), p(0, 0) } });
            g.Polys.Add(new GPoly { Layer = "LEVEL", Pts = { p(2 * L + run, rise), p(2 * L + run + 600, rise) } });
            g.Dims.Add(new GDim { A = p(2 * L + run, 0), B = p(2 * L + run, rise), Line = p(2 * L + run + 1200, 0), Text = "RISE " + rise.ToString("0", CultureInfo.InvariantCulture) });
            g.Dims.Add(new GDim { A = p(L, -t), B = p(L + run, -t), Line = p(0, -t - 800), Text = "RUN " + run.ToString("0", CultureInfo.InvariantCulture) });
            g.Texts.Add(new GText
            {
                Text = o.AngleDeg.ToString("0.#", CultureInfo.InvariantCulture) + " DEG", X = L + run * 0.5 + 400, Y = rise * 0.5 - t - o.TextHeight * 2,
                Height = o.TextHeight, Centre = true, Rotation = o.AngleDeg * Math.PI / 180,
            });
            return g;
        }
    }
}
