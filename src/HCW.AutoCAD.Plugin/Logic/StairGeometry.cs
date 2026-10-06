using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A polyline in real-size millimetres. <see cref="Layer"/> is a role (PLAN, TREAD ...), not a layer name.</summary>
    public class GPoly
    {
        public List<PlanPoint> Pts = new List<PlanPoint>();
        public bool Closed;
        public string Layer = "PLAN";
        /// <summary>Fill a closed outline with the concrete hatch.</summary>
        public bool Hatch;
    }

    public class GText
    {
        public string Text = "";
        public double X, Y;
        public double Height;
        /// <summary>Radians, anticlockwise.</summary>
        public double Rotation;
        public bool Centre;
        public string Layer = "TEXT";
    }

    /// <summary>An aligned dimension: the two points measured and a point on the dimension line. The text is written out, in the drawing's units.</summary>
    public class GDim
    {
        public PlanPoint A, B, Line;
        public string Text = "";
    }

    public class GDrawing
    {
        public List<GPoly> Polys = new List<GPoly>();
        public List<GText> Texts = new List<GText>();
        public List<GDim> Dims = new List<GDim>();

        public Box Extents()
        {
            var pts = Polys.SelectMany(p => p.Pts).ToList();
            return new Box(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        }
    }

    public class StairOptions
    {
        /// <summary>Real-size text height (plotted height times the plot scale).</summary>
        public double TextHeight = 125;
        /// <summary>Real-size distance from the drawing to its dimension lines.</summary>
        public double DimOffset = 500;
        public bool Imperial;
        /// <summary>Length of the top floor slab shown beyond the last riser, and of the lower floor slab before the first.</summary>
        public double FloorSlabLength = 600;
        /// <summary>Draw a headroom line over each flight, this far above the line through the nosings (measured vertically), with its dimension.</summary>
        public bool ShowHeadroom;
        public double Headroom = 2000;
        /// <summary>Draw the handrail, its posts at each end of a flight and the balusters, in the section.</summary>
        public bool ShowRailing;
        public double HandrailHeight = 900;
        public double PostSize = 50;
        public int BalustersPerTread = 2;
        /// <summary>Draw the main bars along each flight and the distribution bars as dots, in the section.</summary>
        public bool ShowRebar;
        public RebarOptions Rebar = new RebarOptions();
    }

    /// <summary>
    /// Builds the plan and the section of a staircase from one <see cref="StairSpec"/>, in real-size millimetres. Every
    /// dimension, level and count is derived from the spec, so the two drawings cannot disagree. No CAD types are used here.
    ///
    /// Plan coordinates: u runs up the first flight from its first riser, v runs across it (0 to Width).
    /// Section coordinates: x runs along the stair, y is the level above the lower floor. For two flights the section is
    /// the section through the stair: a dog-leg or U returns over the first flight; an L is developed in a straight line.
    /// </summary>
    public static class StairGeometry
    {
        // ------------------------------------------------------------------ plan

        public static GDrawing Plan(StairSpec s, StairCalc c, StairOptions o)
        {
            var d = new GDrawing();
            double W = s.Width, G = s.Going, LL = c.LandingLengthUsed;
            int r1 = c.FlightRisers[0];
            double L1 = c.FlightLengths[0];
            double th = o.TextHeight;
            bool two = s.TwoFlights;
            int r2 = two ? c.FlightRisers[1] : 0;
            double L2 = two ? c.FlightLengths[1] : 0;
            double well = s.Kind == StairKind.U ? s.WellWidth : 0;
            double LW = two ? c.LandingWidth : W;

            // flight 1
            Rect(d, 0, 0, L1, W, "PLAN");
            for (int k = 1; k <= r1 - 2; k++) Seg(d, k * G, 0, k * G, W, "TREAD");
            if (s.Nosing > 0)
                for (int k = 1; k <= r1 - 1; k++) Seg(d, k * G - s.Nosing, 0, k * G - s.Nosing, W, "NOSING");
            Arrow(d, 0.1 * L1, W / 2, 0.9 * L1, W / 2, th);
            d.Texts.Add(new GText { Text = "UP", X = 0.1 * L1, Y = W / 2 + 0.4 * th, Height = th, Layer = "TEXT" });
            d.Texts.Add(new GText { Text = "FLIGHT 1 - " + r1 + " RISERS", X = L1 / 2, Y = W * 0.22, Height = th, Centre = true });

            if (two)
            {
                if (s.Kind == StairKind.L)
                {
                    Rect(d, L1, 0, L1 + LL, W, "PLAN");                                    // landing, or the winder square
                    if (s.HasWinders)
                    {
                        // kite winders: lines at 30 and 60 degrees from the inner corner divide the quarter turn into three treads
                        double tan30 = Math.Tan(Math.PI / 6);
                        Seg(d, L1, W, L1 + W * tan30, 0, "TREAD");
                        Seg(d, L1, W, L1 + W, W - W * tan30, "TREAD");
                    }
                    double u0 = L1 + LL - W;
                    Rect(d, u0, W, u0 + W, W + L2, "PLAN");                                 // flight 2, turned
                    for (int k = 1; k <= r2 - 2; k++) Seg(d, u0, W + k * G, u0 + W, W + k * G, "TREAD");
                    if (s.Nosing > 0)
                        for (int k = 1; k <= r2 - 1; k++) Seg(d, u0, W + k * G - s.Nosing, u0 + W, W + k * G - s.Nosing, "NOSING");
                    Arrow(d, u0 + W / 2, W + 0.1 * L2, u0 + W / 2, W + 0.9 * L2, th);
                    d.Texts.Add(new GText { Text = "FLIGHT 2 - " + r2 + " RISERS", X = u0 + 0.2 * W, Y = W + L2 * 0.5, Height = th, Centre = true, Rotation = Math.PI / 2 });
                    d.Texts.Add(new GText { Text = s.HasWinders ? "3 WINDERS " + StairFormat.Level(c.LandingLevel, o.Imperial) : "LANDING " + StairFormat.Level(c.LandingLevel, o.Imperial), X = L1 + LL / 2, Y = W * 0.35, Height = th, Centre = true });
                    Dim(d, u0 + W, W, u0 + W, W + L2, u0 + W + o.DimOffset, W + L2 / 2, Counted(r2 - 1, G, o));
                    Dim(d, L1, 0, L1 + LL, 0, L1 + LL / 2, -o.DimOffset, StairFormat.Length(LL, o.Imperial));
                }
                else
                {
                    Rect(d, L1, 0, L1 + LL, LW, "PLAN");                                   // landing across both flights, or the winder zone
                    if (s.HasWinders) WinderLinesU(d, L1, W, well, c.WinderGoing);
                    Rect(d, L1 - L2, W + well, L1, LW, "PLAN");                            // flight 2, returning
                    for (int k = 1; k <= r2 - 2; k++) Seg(d, L1 - k * G, W + well, L1 - k * G, LW, "TREAD");
                    if (s.Nosing > 0)
                        for (int k = 1; k <= r2 - 1; k++) Seg(d, L1 - k * G + s.Nosing, W + well, L1 - k * G + s.Nosing, LW, "NOSING");
                    Arrow(d, L1 - 0.1 * L2, W + well + W / 2, L1 - 0.9 * L2, W + well + W / 2, th);
                    d.Texts.Add(new GText { Text = "UP", X = L1 - 0.1 * L2, Y = W + well + W / 2 + 0.4 * th, Height = th });
                    d.Texts.Add(new GText { Text = "FLIGHT 2 - " + r2 + " RISERS", X = L1 - L2 / 2, Y = W + well + W * 0.22, Height = th, Centre = true });
                    d.Texts.Add(new GText { Text = (s.HasWinders ? "6 WINDERS " : "LANDING ") + StairFormat.Level(c.LandingLevel, o.Imperial), X = L1 + LL / 2, Y = LW / 2, Height = th, Centre = true, Rotation = s.HasWinders ? Math.PI / 2 : 0 });
                    if (well > 0)
                    {
                        double left = Math.Min(0, L1 - L2);
                        Rect(d, left, W, L1, W + well, "WELL");
                        if (s.OpenWell)
                        {
                            Seg(d, left, W, L1, W + well, "WELL");
                            Seg(d, left, W + well, L1, W, "WELL");
                        }
                    }
                    Dim(d, L1 - L2, LW, L1, LW, L1 - L2 / 2, LW + o.DimOffset, Counted(r2 - 1, G, o));
                    Dim(d, L1, 0, L1 + LL, 0, L1 + LL / 2, -o.DimOffset, StairFormat.Length(LL, o.Imperial));
                    Dim(d, L1 + LL, 0, L1 + LL, LW, L1 + LL + o.DimOffset, LW / 2, StairFormat.Length(LW, o.Imperial));
                }
            }

            Dim(d, 0, 0, 0, W, -o.DimOffset, W / 2, StairFormat.Length(W, o.Imperial));
            Dim(d, 0, 0, L1, 0, L1 / 2, -o.DimOffset, Counted(r1 - 1, G, o));

            if (two && !s.TurnLeft) Mirror(d, W);
            var box = d.Extents();
            d.Texts.Add(new GText { Text = "STAIRCASE PLAN", X = (box.MinX + box.MaxX) / 2, Y = box.MinY - 3 * o.DimOffset, Height = 1.4 * th, Centre = true });
            return d;
        }

        /// <summary>
        /// The lines between the six winders of a U stair, in the zone one stair width long beyond the first flight. The walkline runs a quarter circle
        /// of radius W/2 round the inner corner of the first flight, straight across the well, then a quarter circle round the inner corner of the second
        /// flight; it is divided into six equal goings and each division line is drawn square to it, from the inner corner (in the bends) to the outer edge.
        /// </summary>
        private static void WinderLinesU(GDrawing d, double L1, double W, double well, double going)
        {
            double a = Math.PI * W / 4;                       // one quarter circle on the walkline
            for (int i = 1; i <= 5; i++)
            {
                double sd = i * going;
                if (sd <= a + 1e-9)
                {
                    double phi = sd / (W / 2);
                    double sx = Math.Sin(phi), cy = Math.Cos(phi);
                    double t = double.MaxValue;
                    if (sx > 1e-9) t = Math.Min(t, W / sx);
                    if (cy > 1e-9) t = Math.Min(t, W / cy);
                    Seg(d, L1, W, L1 + sx * t, W - cy * t, "TREAD");
                }
                else if (sd >= a + well - 1e-9)
                {
                    double psi = (sd - a - well) / (W / 2);
                    double cx = Math.Cos(psi), sy = Math.Sin(psi);
                    double t = double.MaxValue;
                    if (cx > 1e-9) t = Math.Min(t, W / cx);
                    if (sy > 1e-9) t = Math.Min(t, W / sy);
                    Seg(d, L1, W + well, L1 + cx * t, W + well + sy * t, "TREAD");
                }
                else
                {
                    double v = W + (sd - a);
                    Seg(d, L1, v, L1 + W, v, "TREAD");
                }
            }
        }

        /// <summary>"9 x 270 = 2430": treads, going, and the length they make.</summary>
        private static string Counted(int treads, double going, StairOptions o)
            => treads + " x " + StairFormat.Length(going, o.Imperial) + " = " + StairFormat.Length(treads * going, o.Imperial);

        /// <summary>Flips the plan across the first flight's centre line, so the second flight lies on the right.</summary>
        private static void Mirror(GDrawing d, double width)
        {
            Func<PlanPoint, PlanPoint> flip = p => new PlanPoint(p.X, width - p.Y);
            foreach (var poly in d.Polys) poly.Pts = poly.Pts.Select(flip).ToList();
            foreach (var t in d.Texts) { t.Y = width - t.Y; t.Rotation = -t.Rotation; }
            foreach (var dim in d.Dims) { dim.A = flip(dim.A); dim.B = flip(dim.B); dim.Line = flip(dim.Line); }
        }

        // ------------------------------------------------------------------ section

        public static GDrawing Section(StairSpec s, StairCalc c, StairOptions o)
        {
            if (s.HasWinders) return SectionWinders(s, c, o);
            var d = new GDrawing();
            double r = c.Rise, G = s.Going, tw = s.WaistThickness, tl = s.LandingThickness, LL = c.LandingLengthUsed;
            double theta = Math.Atan2(r, G);
            double tv = tw / Math.Cos(theta);          // the waist measured vertically
            double th = o.TextHeight, ext = o.FloorSlabLength;
            int r1 = c.FlightRisers[0];
            double xTop = (r1 - 1) * G;
            double E1 = r1 * r;
            double H = s.FloorHeight;
            bool two = s.TwoFlights;

            var flights = new List<FlightInfo>();

            // lower floor slab, ending at the first riser
            d.Polys.Add(new GPoly
            {
                Layer = "SECTION", Closed = true, Hatch = true,
                Pts = { new PlanPoint(-ext, 0), new PlanPoint(0, 0), new PlanPoint(0, -tl), new PlanPoint(-ext, -tl) }
            });

            // flight 1 with its landing (or, for a single flight, the upper floor slab)
            double landEnd = xTop + (two ? LL : ext);
            var f1 = Steps(0, 0, r1, r, G, +1);
            double yb = E1 - tl;
            var soffit1 = new Soffit(0, 0, r, G, tv, +1);
            double xb = Math.Min(soffit1.XAt(yb), landEnd);
            var poly1 = new GPoly { Layer = "SECTION", Closed = true, Hatch = true };
            poly1.Pts.AddRange(f1);
            poly1.Pts.Add(new PlanPoint(landEnd, E1));
            poly1.Pts.Add(new PlanPoint(landEnd, yb));
            poly1.Pts.Add(new PlanPoint(xb, yb));
            double xa = soffit1.XAt(0);
            poly1.Pts.Add(xa >= 0 ? new PlanPoint(xa, 0) : new PlanPoint(0, soffit1.YAt(0)));
            d.Polys.Add(poly1);
            flights.Add(new FlightInfo { X0 = 0, Y0 = 0, Risers = r1, Dir = +1, Soffit = soffit1, Theta = theta });

            double xMin = -ext, xMax = landEnd;
            double yTop = E1;
            if (two && s.Kind == StairKind.L && s.CutSection)
            {
                // cut along the first flight: the second flight runs away from the viewer, so it is seen end on above the landing
                int r2c = c.FlightRisers[1];
                double xl = landEnd - s.Width;
                d.Polys.Add(new GPoly { Layer = "BEYOND", Closed = true, Pts = { new PlanPoint(xl, E1), new PlanPoint(landEnd, E1), new PlanPoint(landEnd, H), new PlanPoint(xl, H) } });
                for (int k = 1; k < r2c; k++) Seg(d, xl, E1 + k * r, landEnd, E1 + k * r, "BEYOND");
                d.Texts.Add(new GText { Text = "FLIGHT 2 BEYOND - " + r2c + " RISERS", X = (xl + landEnd) / 2, Y = (E1 + H) / 2, Height = th, Centre = true, Rotation = Math.PI / 2 });
                yTop = H;
            }
            else if (two)
            {
                int r2 = c.FlightRisers[1];
                double E2 = H;
                double x0 = s.Kind == StairKind.L ? landEnd : xTop;
                int dir = s.Kind == StairKind.L ? +1 : -1;
                var f2 = Steps(x0, E1, r2, r, G, dir);
                var soffit2 = new Soffit(x0, E1, r, G, tv, dir);
                double xt = x0 + dir * (r2 - 1) * G;
                var poly2 = new GPoly { Layer = "SECTION", Closed = true, Hatch = true };
                poly2.Pts.AddRange(f2);
                poly2.Pts.Add(new PlanPoint(xt + dir * ext, E2));
                poly2.Pts.Add(new PlanPoint(xt + dir * ext, E2 - tl));
                poly2.Pts.Add(new PlanPoint(soffit2.XAt(E2 - tl), E2 - tl));
                poly2.Pts.Add(new PlanPoint(x0, soffit2.YAt(x0)));
                d.Polys.Add(poly2);
                flights.Add(new FlightInfo { X0 = x0, Y0 = E1, Risers = r2, Dir = dir, Soffit = soffit2, Theta = theta });
                xMin = Math.Min(xMin, xt + dir * ext);
                xMax = Math.Max(xMax, xt + dir * ext);
                yTop = E2;

                // second flight: waist note along its soffit
                double mx = (x0 + xt) / 2;
                Along(d, "WAIST SLAB " + StairFormat.Length(tw, o.Imperial), mx, soffit2.YAt(mx), dir > 0 ? theta : -theta, th, dir > 0 ? 1 : -1, tv);
            }

            FlightDetails(d, s, c, o, flights);

            // notes on the first flight and the landing
            double mid = xTop / 2;
            Along(d, "WAIST SLAB " + StairFormat.Length(tw, o.Imperial), mid, soffit1.YAt(mid), theta, th, +1, tv);
            d.Texts.Add(new GText
            {
                Text = (two ? "LANDING SLAB " : "FLOOR SLAB ") + StairFormat.Length(tl, o.Imperial),
                X = (xTop + landEnd) / 2, Y = yb - 1.6 * th, Height = th, Centre = true
            });
            d.Texts.Add(new GText
            {
                Text = "PITCH " + (theta * 180.0 / Math.PI).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " DEG   R " + StairFormat.Length(r, o.Imperial)
                    + "   G " + StairFormat.Length(G, o.Imperial),
                X = mid, Y = -3.2 * o.DimOffset + 0.0, Height = th, Centre = true
            });

            // levels, on the left
            double xm = xMin - 1500;
            Level(d, xm, 0, "FFL " + StairFormat.Level(c.BottomLevel, o.Imperial), th, xMax);
            if (two) Level(d, xm, E1, "LANDING " + StairFormat.Level(c.LandingLevel, o.Imperial), th, xMax);
            Level(d, xm, H, "FFL " + StairFormat.Level(c.TopLevel, o.Imperial), th, xMax);

            // dimensions
            double off = o.DimOffset;
            Dim(d, 0, 0, 0, r, -off, r / 2, StairFormat.Length(r, o.Imperial));                       // one riser
            Dim(d, 0, r, G, r, G / 2, r + off, StairFormat.Length(G, o.Imperial));                    // one tread
            Dim(d, 0, 0, xTop, 0, xTop / 2, -1.5 * off, Counted(r1 - 1, G, o));                        // flight 1
            if (two) Dim(d, xTop, E1, landEnd, E1, (xTop + landEnd) / 2, E1 + off, StairFormat.Length(LL, o.Imperial));
            Dim(d, xMax, 0, xMax, H, xMax + off, H / 2, StairFormat.Length(H, o.Imperial));           // floor to floor

            var box = d.Extents();
            d.Texts.Add(new GText { Text = "STAIRCASE SECTION", X = (box.MinX + box.MaxX) / 2, Y = box.MinY - 6 * o.DimOffset, Height = 1.4 * th, Centre = true });
            return d;
        }

        /// <summary>
        /// Section of an L staircase with winders, developed in a straight line: flight 1, then the three winders at the going they have
        /// on the walkline, then flight 2, all one slab. The winder treads are reached by three risers, the first of which tops flight 1.
        /// </summary>
        private static GDrawing SectionWinders(StairSpec s, StairCalc c, StairOptions o)
        {
            var d = new GDrawing();
            double r = c.Rise, G = s.Going, Gw = c.WinderGoing, tw = s.WaistThickness, tl = s.LandingThickness;
            double th = o.TextHeight, ext = o.FloorSlabLength, H = s.FloorHeight;
            int r1 = c.FlightRisers[0], r2 = c.FlightRisers[1];
            int nw = s.WinderTreads;
            int n = r1 + (nw - 1) + r2;

            // the going of the tread after each riser: flight 1, the winders, flight 2
            var goings = new List<double>();
            for (int k = 0; k < n - 1; k++) goings.Add(k < r1 - 1 ? G : k < r1 + nw - 1 ? Gw : G);
            var xs = new List<double> { 0 };
            for (int k = 0; k < n - 1; k++) xs.Add(xs[k] + goings[k]);                 // x of each riser

            d.Polys.Add(new GPoly
            {
                Layer = "SECTION", Closed = true, Hatch = true,
                Pts = { new PlanPoint(-ext, 0), new PlanPoint(0, 0), new PlanPoint(0, -tl), new PlanPoint(-ext, -tl) }
            });

            var slab = new GPoly { Layer = "SECTION", Closed = true, Hatch = true };
            slab.Pts.Add(new PlanPoint(0, 0));
            for (int k = 0; k < n; k++)
            {
                slab.Pts.Add(new PlanPoint(xs[k], (k + 1) * r));
                if (k < n - 1) slab.Pts.Add(new PlanPoint(xs[k + 1], (k + 1) * r));
            }
            double xEnd = xs[n - 1] + ext;
            slab.Pts.Add(new PlanPoint(xEnd, H));
            slab.Pts.Add(new PlanPoint(xEnd, H - tl));
            // the underside runs under every nosing, tw measured square to the pitch of the step it is under
            for (int k = n - 1; k >= 0; k--)
            {
                double g = k < n - 1 ? goings[k] : goings[n - 2];
                double tv = tw / Math.Cos(Math.Atan2(r, g));
                slab.Pts.Add(new PlanPoint(xs[k], (k + 1) * r - tv));
            }
            double tv0 = tw / Math.Cos(Math.Atan2(r, goings[0]));
            double y0 = r - tv0;
            slab.Pts.Add(y0 >= 0 ? new PlanPoint(-y0 * goings[0] / r, 0) : new PlanPoint(0, y0));
            d.Polys.Add(slab);

            var flights = new List<FlightInfo>
            {
                new FlightInfo { X0 = 0, Y0 = 0, Risers = r1, Dir = +1, Soffit = new Soffit(0, 0, r, G, tw / Math.Cos(Math.Atan2(r, G)), +1), Theta = Math.Atan2(r, G) },
                new FlightInfo { X0 = xs[r1 + nw - 1], Y0 = (r1 + nw - 1) * r, Risers = r2, Dir = +1,
                    Soffit = new Soffit(xs[r1 + nw - 1], (r1 + nw - 1) * r, r, G, tw / Math.Cos(Math.Atan2(r, G)), +1), Theta = Math.Atan2(r, G) },
            };
            FlightDetails(d, s, c, o, flights);

            double xw0 = xs[r1 - 1], xw1 = xs[r1 + nw - 1];
            double xMax = xEnd;
            d.Texts.Add(new GText
            {
                Text = nw + " WINDERS - GOING " + StairFormat.Length(Gw, o.Imperial) + " ON THE WALKLINE",
                X = (xw0 + xw1) / 2, Y = c.LandingLevel + r - 1.6 * th - tw, Height = th, Centre = true
            });
            d.Texts.Add(new GText
            {
                Text = "WAIST SLAB " + StairFormat.Length(tw, o.Imperial) + "   R " + StairFormat.Length(r, o.Imperial) + "   G " + StairFormat.Length(G, o.Imperial),
                X = xs[r1 - 1] / 2, Y = -3.2 * o.DimOffset, Height = th, Centre = true
            });

            double xm = -ext - 1500;
            Level(d, xm, 0, "FFL " + StairFormat.Level(c.BottomLevel, o.Imperial), th, xMax);
            Level(d, xm, c.FlightRisers[0] * r, "WINDERS " + StairFormat.Level(c.LandingLevel, o.Imperial), th, xMax);
            Level(d, xm, H, "FFL " + StairFormat.Level(c.TopLevel, o.Imperial), th, xMax);

            double off = o.DimOffset;
            Dim(d, 0, 0, 0, r, -off, r / 2, StairFormat.Length(r, o.Imperial));
            Dim(d, 0, r, G, r, G / 2, r + off, StairFormat.Length(G, o.Imperial));
            Dim(d, 0, 0, xs[r1 - 1], 0, xs[r1 - 1] / 2, -1.5 * off, Counted(r1 - 1, G, o));
            Dim(d, xw0, -tl * 0, xw1, 0, (xw0 + xw1) / 2, -1.5 * off, nw + " x " + StairFormat.Length(Gw, o.Imperial) + " = " + StairFormat.Length(nw * Gw, o.Imperial));
            Dim(d, xMax, 0, xMax, H, xMax + off, H / 2, StairFormat.Length(H, o.Imperial));

            var box = d.Extents();
            d.Texts.Add(new GText { Text = "STAIRCASE SECTION", X = (box.MinX + box.MaxX) / 2, Y = box.MinY - 6 * o.DimOffset, Height = 1.4 * th, Centre = true });
            return d;
        }

        private struct FlightInfo
        {
            public double X0, Y0, Theta;
            public int Risers, Dir;
            public Soffit Soffit;
        }

        /// <summary>
        /// Headroom line, handrail with posts and balusters, and reinforcement for each flight of the section, when the options ask for them.
        /// The nosing line runs through the top corner of every riser; headroom and handrail are measured vertically above it.
        /// The bars are drawn along the soffit, the cover above it, with the distribution bars as dots on top of them.
        /// </summary>
        private static void FlightDetails(GDrawing d, StairSpec s, StairCalc c, StairOptions o, List<FlightInfo> flights)
        {
            if (!o.ShowHeadroom && !o.ShowRailing && !o.ShowRebar) return;
            double r = c.Rise, G = s.Going, th = o.TextHeight;
            foreach (var f in flights)
            {
                if (f.Risers < 2) continue;
                var first = new PlanPoint(f.X0, f.Y0 + r);
                var last = new PlanPoint(f.X0 + f.Dir * (f.Risers - 1) * G, f.Y0 + f.Risers * r);

                if (o.ShowHeadroom)
                {
                    Seg(d, first.X, first.Y + o.Headroom, last.X, last.Y + o.Headroom, "HEADROOM");
                    Dim(d, first.X, first.Y, first.X, first.Y + o.Headroom, first.X - f.Dir * 0.6 * o.DimOffset, first.Y + o.Headroom / 2,
                        "HEADROOM " + StairFormat.Length(o.Headroom, o.Imperial));
                }

                if (o.ShowRailing)
                {
                    double hh = o.HandrailHeight, half = o.PostSize / 2;
                    Seg(d, first.X, first.Y + hh, last.X, last.Y + hh, "RAIL");
                    foreach (var p in new[] { first, last })
                        d.Polys.Add(new GPoly
                        {
                            Layer = "RAIL", Closed = true,
                            Pts = { new PlanPoint(p.X - half, p.Y), new PlanPoint(p.X + half, p.Y), new PlanPoint(p.X + half, p.Y + hh), new PlanPoint(p.X - half, p.Y + hh) }
                        });
                    int n = Math.Max(1, o.BalustersPerTread);
                    for (int k = 0; k < f.Risers - 1; k++)
                    {
                        double xk = f.X0 + f.Dir * k * G, yTread = f.Y0 + (k + 1) * r;
                        for (int i = 0; i < n; i++)
                        {
                            double x = xk + f.Dir * G * (i + 0.5) / n;
                            double yPitch = first.Y + f.Dir * (x - first.X) * r / G;
                            Seg(d, x, yTread, x, yPitch + hh, "RAIL");
                        }
                    }
                    d.Texts.Add(new GText { Text = "HANDRAIL " + StairFormat.Length(hh, o.Imperial), X = (first.X + last.X) / 2, Y = (first.Y + last.Y) / 2 + hh + 1.2 * th, Height = th, Centre = true, Layer = "TEXT",
                        Rotation = f.Dir > 0 ? f.Theta : -f.Theta });
                }

                if (o.ShowRebar)
                {
                    var ro = o.Rebar;
                    double cos = Math.Cos(f.Theta), sin = Math.Sin(f.Theta);
                    double off = ro.Cover / cos;
                    Func<double, double> yBar = x => f.Soffit.YAt(x) + off;
                    double xs = f.X0, xe = f.X0 + f.Dir * (f.Risers - 1) * G;
                    d.Polys.Add(new GPoly { Layer = "REBAR", Pts = { new PlanPoint(xs, yBar(xs)), new PlanPoint(xe, yBar(xe)) } });
                    double slope = Math.Abs(xe - xs) / cos;
                    double lift = (ro.MainDia / 2 + ro.DistDia / 2) / cos;
                    double rad = ro.DistDia / 2;
                    for (double t = ro.Cover; ro.DistSpacing > 0 && t <= slope - ro.Cover + 1e-9; t += ro.DistSpacing)
                    {
                        double x = xs + f.Dir * t * cos;
                        double cy = yBar(x) + lift;
                        var dot = new GPoly { Layer = "REBAR", Closed = true };
                        for (int a = 0; a < 8; a++)
                            dot.Pts.Add(new PlanPoint(x + rad * Math.Cos(a * Math.PI / 4), cy + rad * Math.Sin(a * Math.PI / 4)));
                        d.Polys.Add(dot);
                    }
                }
            }
            if (o.ShowRebar)
            {
                var ro = o.Rebar;
                double y0 = -3.2 * o.DimOffset - 1.8 * th;
                double xm = flights.Count > 0 ? flights[0].X0 : 0;
                d.Texts.Add(new GText
                {
                    Text = "MAIN " + ro.MainDia.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " @ " + ro.MainSpacing.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                        + " C/C   DIST " + ro.DistDia.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " @ " + ro.DistSpacing.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                        + " C/C   COVER " + ro.Cover.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "   ANCHORAGE " + ro.AnchorageDiameters.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " D",
                    X = xm + 3 * G, Y = y0, Height = th, Centre = true, Layer = "TEXT",
                });
            }
        }

        /// <summary>The upper outline of a flight: from the foot of the first riser, up every riser and along every tread to the top nosing.</summary>
        private static List<PlanPoint> Steps(double x0, double y0, int risers, double rise, double going, int dir)
        {
            var pts = new List<PlanPoint> { new PlanPoint(x0, y0) };
            for (int k = 0; k < risers; k++)
            {
                double x = x0 + dir * k * going;
                pts.Add(new PlanPoint(x, y0 + (k + 1) * rise));
                if (k < risers - 1) pts.Add(new PlanPoint(x + dir * going, y0 + (k + 1) * rise));
            }
            return pts;
        }

        /// <summary>
        /// The underside of a flight: parallel to the line through the nosings, <c>tv</c> below it measured vertically
        /// (the waist thickness divided by the cosine of the pitch).
        /// </summary>
        private struct Soffit
        {
            private readonly double _x0, _y0, _rise, _going, _tv;
            private readonly int _dir;
            public Soffit(double x0, double y0, double rise, double going, double tv, int dir)
            { _x0 = x0; _y0 = y0; _rise = rise; _going = going; _tv = tv; _dir = dir; }
            public double YAt(double x) => _y0 + _rise - _tv + _dir * (x - _x0) * _rise / _going;
            public double XAt(double y) => _x0 + _dir * (y - (_y0 + _rise - _tv)) * _going / _rise;
        }

        /// <summary>Text along the soffit, on its underside, centred at the given point of the soffit.</summary>
        private static void Along(GDrawing d, string text, double x, double y, double rotation, double th, int dir, double tv)
        {
            double gap = 1.6 * th;
            // the normal pointing away from the concrete (down and to the side)
            double nx = dir > 0 ? Math.Sin(-rotation) * -1 : -Math.Sin(Math.Abs(rotation));
            double ny = -Math.Cos(rotation);
            d.Texts.Add(new GText
            {
                Text = text, X = x + nx * gap, Y = y + ny * gap, Height = th, Rotation = rotation, Centre = true
            });
        }

        private static void Level(GDrawing d, double x, double y, string text, double th, double xEnd)
        {
            // a line at the level with an inverted triangle and the text above it
            Seg(d, x, y, xEnd, y, "LEVEL");
            d.Polys.Add(new GPoly
            {
                Layer = "LEVEL", Closed = true,
                Pts = { new PlanPoint(x, y), new PlanPoint(x - 0.6 * th, y + th), new PlanPoint(x + 0.6 * th, y + th) }
            });
            d.Texts.Add(new GText { Text = text, X = x + 1.2 * th, Y = y + 0.3 * th, Height = th, Layer = "TEXT" });
        }

        // ------------------------------------------------------------------ primitives

        private static void Rect(GDrawing d, double x1, double y1, double x2, double y2, string layer)
        {
            d.Polys.Add(new GPoly
            {
                Layer = layer, Closed = true,
                Pts = { new PlanPoint(x1, y1), new PlanPoint(x2, y1), new PlanPoint(x2, y2), new PlanPoint(x1, y2) }
            });
        }

        private static void Seg(GDrawing d, double x1, double y1, double x2, double y2, string layer)
        {
            d.Polys.Add(new GPoly { Layer = layer, Pts = { new PlanPoint(x1, y1), new PlanPoint(x2, y2) } });
        }

        private static void Dim(GDrawing d, double ax, double ay, double bx, double by, double lx, double ly, string text)
        {
            d.Dims.Add(new GDim { A = new PlanPoint(ax, ay), B = new PlanPoint(bx, by), Line = new PlanPoint(lx, ly), Text = text });
        }

        /// <summary>A line with an open arrowhead at its end.</summary>
        private static void Arrow(GDrawing d, double x1, double y1, double x2, double y2, double th)
        {
            Seg(d, x1, y1, x2, y2, "ARROW");
            double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return;
            double ux = dx / len, uy = dy / len;
            double head = Math.Min(2.0 * th, len / 3), half = head * 0.35;
            Seg(d, x2, y2, x2 - ux * head - uy * half, y2 - uy * head + ux * half, "ARROW");
            Seg(d, x2, y2, x2 - ux * head + uy * half, y2 - uy * head - ux * half, "ARROW");
        }
    }
}
