using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// The winders of an L or U stair in plan, in zone coordinates: x runs 0 to W along the first flight's direction from the zone's start edge, y runs across
    /// (0 to W for an L, 0 to 2W + well for a U). The walkline runs half a stair width from the inner corner(s): a quarter circle round the first flight's
    /// inner corner (0, W), for a U straight across the well, then a quarter circle round (0, W + well). It is divided into equal goings and a line is drawn
    /// square to it at each division, from the inner corner in a bend or straight across the well. No CAD types are used here.
    /// </summary>
    public class WinderLayout
    {
        public double W, Well;
        public int Treads;
        /// <summary>The dividing lines, first to last; each starts at its inner corner in a bend.</summary>
        public List<Seg> Lines = new List<Seg>();
        /// <summary>Plan area of each winder tread, in the order walked.</summary>
        public double[] TreadAreas;
        /// <summary>Length of each dividing line, which is the width of the riser there.</summary>
        public double[] RiserLengths;
        public double ZoneArea;
        public double WalklineGoing;

        public static WinderLayout Build(double width, double well, bool uStair)
        {
            var L = new WinderLayout { W = width, Well = uStair ? well : 0, Treads = uStair ? 6 : 3 };
            double a = Math.PI * width / 4;
            double total = uStair ? 2 * a + L.Well : a;
            L.WalklineGoing = total / L.Treads;
            double zoneW = uStair ? 2 * width + L.Well : width;

            for (int i = 1; i < L.Treads; i++)
            {
                double s = i * L.WalklineGoing;
                if (s <= a + 1e-9)
                {
                    double phi = s / (width / 2);
                    double sx = Math.Sin(phi), cy = Math.Cos(phi), t = double.MaxValue;
                    if (sx > 1e-9) t = Math.Min(t, width / sx);
                    if (cy > 1e-9) t = Math.Min(t, width / cy);
                    L.Lines.Add(new Seg(new P2(0, width), new P2(sx * t, width - cy * t)));
                }
                else if (s >= a + L.Well - 1e-9)
                {
                    double psi = (s - a - L.Well) / (width / 2);
                    double cx = Math.Cos(psi), sy = Math.Sin(psi), t = double.MaxValue;
                    if (cx > 1e-9) t = Math.Min(t, width / cx);
                    if (sy > 1e-9) t = Math.Min(t, width / sy);
                    L.Lines.Add(new Seg(new P2(0, width + L.Well), new P2(cx * t, width + L.Well + sy * t)));
                }
                else
                {
                    double y = width + (s - a);
                    L.Lines.Add(new Seg(new P2(0, y), new P2(width, y)));
                }
            }
            L.RiserLengths = L.Lines.Select(l => l.Length).ToArray();
            L.ZoneArea = width * zoneW;

            // each tread is the zone cut by the line before it and the line after it, on the side the walkline is on
            var zone = new List<P2> { new P2(0, 0), new P2(width, 0), new P2(width, zoneW), new P2(0, zoneW) };
            L.TreadAreas = new double[L.Treads];
            for (int k = 0; k < L.Treads; k++)
            {
                var mid = Path(width, L.Well, (k + 0.5) * L.WalklineGoing);
                var poly = zone;
                if (k >= 1) poly = Clip(poly, L.Lines[k - 1], mid);
                if (k <= L.Treads - 2) poly = Clip(poly, L.Lines[k], mid);
                L.TreadAreas[k] = Math.Abs(PlanarRooms.SignedArea(poly));
            }
            return L;
        }

        /// <summary>The point on the walkline at a distance along it.</summary>
        private static P2 Path(double w, double well, double s)
        {
            double a = Math.PI * w / 4, r = w / 2;
            if (s <= a) { double phi = s / r; return new P2(r * Math.Sin(phi), w - r * Math.Cos(phi)); }
            if (s <= a + well) return new P2(r, w + (s - a));
            double psi = (s - a - well) / r;
            return new P2(r * Math.Cos(psi), w + well + r * Math.Sin(psi));
        }

        /// <summary>Keeps the part of a polygon on the same side of the line as the given point (Sutherland-Hodgman against one half-plane).</summary>
        private static List<P2> Clip(List<P2> poly, Seg line, P2 keep)
        {
            Func<P2, double> side = p => P2.Cross(line.B - line.A, p - line.A);
            double sign = Math.Sign(side(keep));
            var res = new List<P2>();
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i]; var prev = poly[(i + poly.Count - 1) % poly.Count];
                double dc = side(cur) * sign, dp = side(prev) * sign;
                if (dc >= 0)
                {
                    if (dp < 0) res.Add(Cut(prev, cur, dp, dc));
                    res.Add(cur);
                }
                else if (dp >= 0) res.Add(Cut(prev, cur, dp, dc));
            }
            return res;
        }

        private static P2 Cut(P2 a, P2 b, double da, double db) => a + (b - a) * (da / (da - db));
    }
}
