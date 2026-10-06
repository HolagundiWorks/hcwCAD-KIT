using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>A room outline picked from the drawing. Area is in square drawing units when the host worked it out (curves included), or 0 to work it out from the points.</summary>
    public class RoomInput
    {
        public List<P2> Outline = new List<P2>();
        public double Area;
    }

    /// <summary>A line of text in the drawing and where it stands.</summary>
    public class RoomText
    {
        public P2 At;
        public string Text = "";
    }

    /// <summary>A dimension in the drawing: its two measured points and its value, in drawing units.</summary>
    public class RoomDim
    {
        public P2 A, B;
        public double Value;
    }

    public class RoomRow
    {
        public int Number;
        public string Name = "";
        /// <summary>The longer and shorter sides of the room's bounding rectangle, turned to its longest wall, in metres.</summary>
        public double Length, Width;
        public double Area, Perimeter;
        /// <summary>An area written in a label inside the room, when there is one.</summary>
        public double? LabelArea;
        /// <summary>The dimensions found inside the room, largest first, in metres.</summary>
        public List<double> Dimensions = new List<double>();
        public P2 Centre;
        public string Note = "";
    }

    public enum RoomOrder { Position, Name }

    /// <summary>
    /// Reads rooms from a selection: each closed outline with the name written inside it, its length, width, area and the dimensions that lie in it,
    /// and puts them in a table. No CAD types are used here.
    /// </summary>
    public static class RoomTable
    {
        private static readonly Regex AreaLabel = new Regex(@"^\s*(\d+(?:[.,]\d+)?)\s*(?:m2|m²|sq\.?\s?m|sqm)\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex NumberOnly = new Regex(@"^[\s\d.,+\-x*]+$", RegexOptions.IgnoreCase);
        private static readonly Regex Tag = new Regex(@"^(?:[DW]\d+|R\d+|C\d+|LP|FP|SB)[\w-]*$", RegexOptions.IgnoreCase);

        public static List<RoomRow> Build(IList<RoomInput> rooms, IList<RoomText> texts, IList<RoomDim> dims, double unitsPerMetre, RoomOrder order)
        {
            if (unitsPerMetre <= 0) throw new ArgumentOutOfRangeException("unitsPerMetre");
            var rows = new List<RoomRow>();
            foreach (var room in rooms)
            {
                var poly = room.Outline;
                if (poly.Count < 3) continue;
                var row = new RoomRow();
                double area = room.Area > 0 ? room.Area : Math.Abs(PlanarRooms.SignedArea(poly));
                row.Area = area / (unitsPerMetre * unitsPerMetre);
                row.Perimeter = Perimeter(poly) / unitsPerMetre;
                row.Centre = new P2(poly.Average(p => p.X), poly.Average(p => p.Y));
                double l, w;
                Extent(poly, out l, out w);
                row.Length = l / unitsPerMetre; row.Width = w / unitsPerMetre;

                double tol = 0.01 * Math.Max(l, w);
                var inside = texts.Where(t => PlanarRooms.Contains(poly, t.At)).ToList();
                row.Name = NameOf(inside, row.Centre);
                foreach (var t in inside)
                    foreach (var line in Lines(t.Text))
                    {
                        var m = AreaLabel.Match(line);
                        double v;
                        if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) { row.LabelArea = v; break; }
                    }
                if (row.LabelArea.HasValue && Math.Abs(row.LabelArea.Value - row.Area) > 0.01 * Math.Max(1, row.Area))
                    row.Note = "label says " + row.LabelArea.Value.ToString("0.00", CultureInfo.InvariantCulture);

                var found = new List<double>();
                foreach (var d in dims)
                {
                    var mid = (d.A + d.B) * 0.5;
                    if (d.Value <= 0 || !(OnOrIn(poly, d.A, tol) && OnOrIn(poly, d.B, tol) && OnOrIn(poly, mid, tol))) continue;
                    double m2 = Math.Round(d.Value / unitsPerMetre, 3);
                    if (!found.Contains(m2)) found.Add(m2);
                }
                row.Dimensions = found.OrderByDescending(v => v).ToList();
                rows.Add(row);
            }

            var ordered = order == RoomOrder.Name
                ? rows.OrderBy(r => r.Name.Length == 0 ? 1 : 0).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Centre.X).ToList()
                // top to bottom in rows of similar height, then left to right
                : rows.OrderByDescending(r => Math.Round(r.Centre.Y / (unitsPerMetre * 1.5))).ThenBy(r => r.Centre.X).ToList();
            for (int i = 0; i < ordered.Count; i++) ordered[i].Number = i + 1;
            return ordered;
        }

        /// <summary>The name written inside a room: the first line that is not a number, an area, a tag (D1, R2) or a dimension; the one nearest the middle wins.</summary>
        public static string NameOf(IEnumerable<RoomText> inside, P2 centre)
        {
            foreach (var t in inside.OrderBy(t => t.At.DistanceTo(centre)))
                foreach (var line in Lines(t.Text))
                {
                    string s = line.Trim();
                    if (s.Length == 0 || NumberOnly.IsMatch(s) || AreaLabel.IsMatch(s) || Tag.IsMatch(s)) continue;
                    return s.ToUpperInvariant();
                }
            return "";
        }

        private static IEnumerable<string> Lines(string text) =>
            (text ?? "").Replace("\\P", "\n").Replace("\\X", "\n").Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Length and width of the room's bounding rectangle, turned to its longest edge (so a tilted rectangle gives its real sides).</summary>
        public static void Extent(IList<P2> poly, out double length, out double width)
        {
            double best = -1; P2 dir = new P2(1, 0);
            for (int i = 0; i < poly.Count; i++)
            {
                var e = poly[(i + 1) % poly.Count] - poly[i];
                if (e.Length > best) { best = e.Length; dir = e * (1.0 / Math.Max(e.Length, 1e-12)); }
            }
            var n = new P2(-dir.Y, dir.X);
            double u0 = double.MaxValue, u1 = double.MinValue, v0 = double.MaxValue, v1 = double.MinValue;
            foreach (var p in poly)
            {
                double u = P2.Dot(p, dir), v = P2.Dot(p, n);
                u0 = Math.Min(u0, u); u1 = Math.Max(u1, u); v0 = Math.Min(v0, v); v1 = Math.Max(v1, v);
            }
            double a = u1 - u0, b = v1 - v0;
            length = Math.Max(a, b); width = Math.Min(a, b);
        }

        private static double Perimeter(IList<P2> poly)
        {
            double s = 0;
            for (int i = 0; i < poly.Count; i++) s += poly[i].DistanceTo(poly[(i + 1) % poly.Count]);
            return s;
        }

        private static bool OnOrIn(IList<P2> poly, P2 p, double tol)
        {
            if (PlanarRooms.Contains(poly, p)) return true;
            for (int i = 0; i < poly.Count; i++)
                if (OpeningCut.DistanceToSegment(new Seg(poly[i], poly[(i + 1) % poly.Count]), p) <= tol) return true;
            return false;
        }

        // ------------------------------------------------------------------ the table

        public static string[] Headers(string lengthUnit) => new[]
        {
            "No", "Room", "Length (" + lengthUnit + ")", "Width (" + lengthUnit + ")", "Area (sq m)", "Perimeter (" + lengthUnit + ")", "Dimensions (" + lengthUnit + ")", "Note",
        };

        /// <summary>Rows of text for the table, with a TOTAL row. Lengths in metres (2 places) or millimetres (whole numbers); areas in square metres, 2 places.</summary>
        public static List<string[]> ToRows(IList<RoomRow> rows, bool millimetres)
        {
            Func<double, string> len = v => millimetres
                ? Math.Round(v * 1000).ToString("0", CultureInfo.InvariantCulture)
                : v.ToString("0.00", CultureInfo.InvariantCulture);
            var res = new List<string[]>();
            foreach (var r in rows)
                res.Add(new[]
                {
                    r.Number.ToString(CultureInfo.InvariantCulture), r.Name, len(r.Length), len(r.Width), r.Area.ToString("0.00", CultureInfo.InvariantCulture),
                    len(r.Perimeter), string.Join(" ; ", r.Dimensions.Select(len)), r.Note,
                });
            res.Add(new[] { "TOTAL", rows.Count + " room" + (rows.Count == 1 ? "" : "s"), "", "", rows.Sum(r => r.Area).ToString("0.00", CultureInfo.InvariantCulture), "", "", "" });
            return res;
        }
    }
}
