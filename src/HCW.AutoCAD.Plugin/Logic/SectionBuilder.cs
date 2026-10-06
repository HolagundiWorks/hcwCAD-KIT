using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>One floor of the building: heights in millimetres. The slab is the one under this floor (the roof slab reuses the top floor's).</summary>
    public class LevelRow
    {
        public string Name = "Ground";
        public double FflMm = 3150;      // finished floor level to the next
        public double CeilingMm = 3000;
        public double LintelMm = 2100;   // underside of the lintel above the FFL
        public double SlabMm = 150;
    }

    /// <summary>A wall (or column) cut by the section line: its span along the line, in mm from the line's start.</summary>
    public class SectionWall { public double S0, S1; }

    /// <summary>An opening cut by the section line: its span along the line and its heights above the floor.</summary>
    public class SectionOpening { public double S0, S1, SillMm, HeightMm; public bool Door; }

    public class SectionRect
    {
        public double X0, Y0, X1, Y1;
        /// <summary>Slab, Wall or Lintel.</summary>
        public string Kind = "Wall";
        public int Floor;
    }

    /// <summary>Works out the rectangles of a building section from the level table, the walls the line cuts and the openings in them.</summary>
    public static class SectionBuilder
    {
        /// <summary>Elevation of each floor's finished floor level above the first, in mm.</summary>
        public static List<double> Elevations(IList<LevelRow> levels)
        {
            var r = new List<double>(); double e = 0;
            foreach (var l in levels) { r.Add(e); e += l.FflMm; }
            return r;
        }

        /// <summary>Overlapping or touching spans joined into one, in order.</summary>
        public static List<SectionWall> Merge(IEnumerable<SectionWall> walls, double tol = 1e-6)
        {
            var res = new List<SectionWall>();
            foreach (var w in walls.Where(w => w.S1 - w.S0 > tol).Select(w => new SectionWall { S0 = Math.Min(w.S0, w.S1), S1 = Math.Max(w.S0, w.S1) }).OrderBy(w => w.S0))
            {
                if (res.Count > 0 && w.S0 <= res[res.Count - 1].S1 + tol) res[res.Count - 1].S1 = Math.Max(res[res.Count - 1].S1, w.S1);
                else res.Add(w);
            }
            return res;
        }

        /// <summary>
        /// Rectangles for every floor, the plan repeating on each. Slabs run across spanFrom to spanTo; walls stand from each floor's finished
        /// level up to the underside of the slab above; an opening leaves a gap from its sill to its head (the lintel bottom at most) and
        /// a lintel band closes the wall from the head to the top.
        /// </summary>
        public static List<SectionRect> Build(IList<LevelRow> levels, IEnumerable<SectionWall> walls, IEnumerable<SectionOpening> openings, double spanFrom, double spanTo)
        {
            var res = new List<SectionRect>();
            if (levels == null || levels.Count == 0) return res;
            var elev = Elevations(levels);
            var wallSpans = Merge(walls);
            var ops = (openings ?? new SectionOpening[0]).Select(o => new SectionOpening { S0 = Math.Min(o.S0, o.S1), S1 = Math.Max(o.S0, o.S1), SillMm = o.SillMm, HeightMm = o.HeightMm, Door = o.Door }).ToList();
            for (int i = 0; i < levels.Count; i++)
            {
                var lv = levels[i];
                if (lv.SlabMm > 0) res.Add(new SectionRect { X0 = spanFrom, X1 = spanTo, Y0 = elev[i] - lv.SlabMm, Y1 = elev[i], Kind = "Slab", Floor = i });
                double slabAbove = i + 1 < levels.Count ? levels[i + 1].SlabMm : lv.SlabMm;
                double top = elev[i] + lv.FflMm - slabAbove, bottom = elev[i];
                if (top <= bottom) continue;
                foreach (var w in wallSpans)
                {
                    var cuts = ops.Where(o => o.S1 > w.S0 + 1e-6 && o.S0 < w.S1 - 1e-6).OrderBy(o => o.S0).ToList();
                    double x = w.S0;
                    foreach (var o in cuts)
                    {
                        double a = Math.Max(o.S0, w.S0), b = Math.Min(o.S1, w.S1);
                        if (a > x + 1e-6) res.Add(new SectionRect { X0 = x, X1 = a, Y0 = bottom, Y1 = top, Kind = "Wall", Floor = i });
                        double sill = Math.Max(0, o.SillMm);
                        double head = Math.Min(sill + o.HeightMm, lv.LintelMm > sill ? lv.LintelMm : sill + o.HeightMm);
                        head = Math.Min(head, top - bottom);
                        if (sill > 0) res.Add(new SectionRect { X0 = a, X1 = b, Y0 = bottom, Y1 = bottom + sill, Kind = "Wall", Floor = i });
                        if (bottom + head < top) res.Add(new SectionRect { X0 = a, X1 = b, Y0 = bottom + head, Y1 = top, Kind = "Lintel", Floor = i });
                        x = Math.Max(x, b);
                    }
                    if (w.S1 > x + 1e-6) res.Add(new SectionRect { X0 = x, X1 = w.S1, Y0 = bottom, Y1 = top, Kind = "Wall", Floor = i });
                }
            }
            var last = levels[levels.Count - 1];
            if (last.SlabMm > 0)
            {
                double e = elev[levels.Count - 1] + last.FflMm;
                res.Add(new SectionRect { X0 = spanFrom, X1 = spanTo, Y0 = e - last.SlabMm, Y1 = e, Kind = "Slab", Floor = levels.Count });
            }
            return res;
        }

        /// <summary>The part of the line a→b (as 0..1 parameters) that lies inside a convex quadrilateral given in order; null when it misses.</summary>
        public static bool ClipLine(double ax, double ay, double bx, double by, double[] px, double[] py, out double t0, out double t1)
        {
            t0 = 0; t1 = 1;
            double area = 0;
            for (int i = 0; i < px.Length; i++) { int j = (i + 1) % px.Length; area += px[i] * py[j] - px[j] * py[i]; }
            double sign = area >= 0 ? 1 : -1;
            double dx = bx - ax, dy = by - ay;
            for (int i = 0; i < px.Length; i++)
            {
                int j = (i + 1) % px.Length;
                double ex = px[j] - px[i], ey = py[j] - py[i];
                // inside is on the left of each edge (for a counter-clockwise polygon)
                double num = sign * (ex * (ay - py[i]) - ey * (ax - px[i]));
                double den = sign * (ex * dy - ey * dx);
                if (Math.Abs(den) < 1e-12) { if (num < 0) return false; continue; }
                double t = -num / den;
                if (den > 0) { if (t > t0) t0 = t; } else { if (t < t1) t1 = t; }
                if (t0 > t1) return false;
            }
            return t1 - t0 > 1e-9;
        }

        /// <summary>Spans along a line where it crosses the wall faces, paired into walls: two crossings no further apart than maxThickness are one wall.</summary>
        public static List<SectionWall> PairCrossings(IEnumerable<double> crossings, double maxThickness)
        {
            var s = crossings.OrderBy(v => v).ToList();
            var res = new List<SectionWall>();
            for (int i = 0; i + 1 < s.Count; i++)
            {
                if (s[i + 1] - s[i] <= maxThickness && s[i + 1] - s[i] > 1e-6) { res.Add(new SectionWall { S0 = s[i], S1 = s[i + 1] }); i++; }
            }
            return res;
        }
    }
}
