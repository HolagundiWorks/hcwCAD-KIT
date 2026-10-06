using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A column section: a rectangle (width x depth) or a circle (diameter), in millimetres.</summary>
    public class ColumnSize
    {
        public bool Round;
        public double W, D;

        /// <summary>"230x450", "9x18" style text without units, or "Ø450".</summary>
        public string Label => Round
            ? "Ø" + W.ToString("0.##", CultureInfo.InvariantCulture)
            : W.ToString("0.##", CultureInfo.InvariantCulture) + "x" + D.ToString("0.##", CultureInfo.InvariantCulture);

        public const string BlockPrefix = "HCW_COL_";

        /// <summary>The name of the column block for this size: HCW_COL_230x450, or HCW_COL_R450 for a round column.</summary>
        public string BlockName => BlockPrefix + (Round
            ? "R" + W.ToString("0.##", CultureInfo.InvariantCulture)
            : W.ToString("0.##", CultureInfo.InvariantCulture) + "x" + D.ToString("0.##", CultureInfo.InvariantCulture));

        /// <summary>The size a column block name stands for, or null when the name is not one of ours (an anonymous copy's name will not parse).</summary>
        public static ColumnSize FromBlockName(string name)
        {
            if (name == null || !name.StartsWith(BlockPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = name.Substring(BlockPrefix.Length);
            string error;
            if (rest.Length > 1 && (rest[0] == 'R' || rest[0] == 'r')) return Parse("D" + rest.Substring(1), out error);
            return Parse(rest, out error);
        }

        public double Area => Round ? Math.PI * W * W / 4.0 : W * D;

        /// <summary>
        /// Reads 230x450 (also 230*450 or 230 X 450), a single number for a square (300), or a round column:
        /// D450, O450, dia450, Ø450 or %%c450.
        /// </summary>
        public static ColumnSize Parse(string text, out string error)
        {
            error = null;
            string s = (text ?? "").Trim().Replace(" ", "");
            if (s.Length == 0) { error = "give a size such as 230x450, 300, or D450"; return null; }

            bool round = false;
            foreach (var prefix in new[] { "%%c", "%%C", "dia", "DIA", "Dia", "Ø", "ø", "⌀", "D", "d", "O", "o" })
                if (s.StartsWith(prefix, StringComparison.Ordinal)) { round = true; s = s.Substring(prefix.Length); break; }

            double w, d;
            if (round)
            {
                if (!Num(s, out w)) { error = "\"" + text + "\": the diameter must be a positive number"; return null; }
                return new ColumnSize { Round = true, W = w, D = w };
            }

            int sep = s.IndexOfAny(new[] { 'x', 'X', '*' });
            if (sep < 0)
            {
                if (!Num(s, out w)) { error = "\"" + text + "\" is not a positive size"; return null; }
                return new ColumnSize { W = w, D = w };
            }
            if (!Num(s.Substring(0, sep), out w) || !Num(s.Substring(sep + 1), out d))
            { error = "\"" + text + "\": width and depth must be positive numbers"; return null; }
            return new ColumnSize { W = w, D = d };
        }

        private static bool Num(string s, out double v) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 && !double.IsInfinity(v);
    }

    /// <summary>A grid intersection and the angle (radians) a column there is turned to follow the grid.</summary>
    public struct GridPoint
    {
        public P2 Pt;
        public double Angle;
    }

    public static class GridIntersections
    {
        /// <summary>
        /// Points where two grid lines cross (a crossing at an end point counts). Parallel lines never cross.
        /// Points closer than tol are one point. The angle is that of the more horizontal of the two lines,
        /// folded into (-45, 45] degrees, so a rectangular column keeps its width along the grid.
        /// </summary>
        public static List<GridPoint> Find(IList<Seg> lines, double tol)
        {
            var found = new List<GridPoint>();
            for (int i = 0; i < lines.Count; i++)
            {
                var a = lines[i];
                var da = a.B - a.A;
                if (da.Length < 1e-9) continue;
                for (int j = i + 1; j < lines.Count; j++)
                {
                    var b = lines[j];
                    var db = b.B - b.A;
                    if (db.Length < 1e-9) continue;
                    double den = P2.Cross(da, db);
                    if (Math.Abs(den) < 1e-12 * da.Length * db.Length) continue;      // parallel
                    var ab = b.A - a.A;
                    double t = P2.Cross(ab, db) / den;
                    double u = P2.Cross(ab, da) / den;
                    double ta = tol / da.Length, ub = tol / db.Length;
                    if (t < -ta || t > 1 + ta || u < -ub || u > 1 + ub) continue;

                    var p = a.A + da * t;
                    if (found.Any(f => f.Pt.DistanceTo(p) <= tol)) continue;
                    double aa = Fold(Math.Atan2(da.Y, da.X)), ab2 = Fold(Math.Atan2(db.Y, db.X));
                    found.Add(new GridPoint { Pt = p, Angle = Math.Abs(aa) <= Math.Abs(ab2) ? aa : ab2 });
                }
            }
            return found;
        }

        /// <summary>An angle folded into (-45, 45] degrees (a line and its perpendicular give the same column).</summary>
        public static double Fold(double angle)
        {
            double q = Math.PI / 2;
            double a = angle - Math.Floor(angle / q) * q;      // 0 .. 90
            if (a > Math.PI / 4 + 1e-12) a -= q;
            return a;
        }
    }

    /// <summary>Column marks (C1, C2 …) by size: the biggest section is C1; equal sizes share a mark.</summary>
    public static class ColumnMarks
    {
        public class Row
        {
            public string Mark;
            public string Label;
            public double Area;
            public int Count;
        }

        /// <summary>Sizes are rounded to whole millimetres before they are compared.</summary>
        public static List<Row> Assign(IEnumerable<ColumnSize> sizes)
        {
            var groups = sizes
                .Select(s => new ColumnSize { Round = s.Round, W = Math.Round(s.W), D = s.Round ? Math.Round(s.W) : Math.Round(s.D) })
                .GroupBy(s => s.Label)
                .Select(g => new Row { Label = g.Key, Area = g.First().Area, Count = g.Count() })
                .OrderByDescending(r => r.Area).ThenBy(r => r.Label, StringComparer.Ordinal)
                .ToList();
            for (int i = 0; i < groups.Count; i++) groups[i].Mark = "C" + (i + 1);
            return groups;
        }
        }

    /// <summary>Moves columns at the edge of a grid so their outer face sits on the grid line (or a set distance outside it).</summary>
    public static class ColumnEdges
    {
        /// <summary>
        /// For each grid point, how far to move its column. The grid's own directions come from the first point's angle. A point on the lowest
        /// or highest grid line of either direction is moved inwards by half the column size less outMm, the distance the outer face may stand
        /// proud of the line (0 puts it on the line; half a wall thickness puts it flush with a wall face when the line is the wall's centre).
        /// A direction with only one grid line is left alone. Columns of any size can be mixed; widths run along the grid's first direction.
        /// </summary>
        public static List<P2> Shifts(IList<GridPoint> pts, IList<double> widths, IList<double> depths, double outMm, double tol)
        {
            var res = pts.Select(p => new P2(0, 0)).ToList();
            if (pts.Count == 0) return res;
            double a = pts[0].Angle;
            var e1 = new P2(Math.Cos(a), Math.Sin(a)); var e2 = new P2(-e1.Y, e1.X);
            var u = pts.Select(p => P2.Dot(p.Pt, e1)).ToList();
            var v = pts.Select(p => P2.Dot(p.Pt, e2)).ToList();
            double u0 = u.Min(), u1 = u.Max(), v0 = v.Min(), v1 = v.Max();
            for (int i = 0; i < pts.Count; i++)
            {
                double du = 0, dv = 0;
                if (u1 - u0 > tol)
                {
                    if (Math.Abs(u[i] - u0) <= tol) du = widths[i] / 2 - outMm;
                    else if (Math.Abs(u[i] - u1) <= tol) du = -(widths[i] / 2 - outMm);
                }
                if (v1 - v0 > tol)
                {
                    if (Math.Abs(v[i] - v0) <= tol) dv = depths[i] / 2 - outMm;
                    else if (Math.Abs(v[i] - v1) <= tol) dv = -(depths[i] / 2 - outMm);
                }
                res[i] = e1 * du + e2 * dv;
            }
            return res;
        }
    }
}
