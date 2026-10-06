using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// A wall as it was drawn: its centre line, thickness and which face the line sits on. Kept in the drawing so the wall can be
    /// edited later and rebuilt. Lengths are in drawing units for the points and millimetres for the thickness. No CAD types are used here.
    /// </summary>
    public class WallRecord
    {
        public string Id = "";
        public double ThicknessMm = 230;
        public WallJustify Justify = WallJustify.Centre;
        public bool Closed;
        public double Z;
        public List<P2> Points = new List<P2>();

        public List<string> ToLines()
        {
            Func<double, string> d = v => v.ToString("R", CultureInfo.InvariantCulture);
            var lines = new List<string> { Fields.Join("T", d(ThicknessMm), Justify.ToString(), Closed ? "1" : "0", d(Z)) };
            foreach (var p in Points) lines.Add(Fields.Join("P", d(p.X), d(p.Y)));
            return lines;
        }

        public static WallRecord FromLines(string id, IEnumerable<string> lines)
        {
            var w = new WallRecord { Id = id };
            bool any = false;
            Func<string, double> num = s => { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; };
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (p[0] == "T" && p.Length >= 5)
                {
                    w.ThicknessMm = num(p[1]);
                    WallJustify j;
                    if (Enum.TryParse(p[2], out j)) w.Justify = j;
                    w.Closed = p[3] == "1";
                    w.Z = num(p[4]);
                    any = true;
                }
                else if (p[0] == "P" && p.Length >= 3) w.Points.Add(new P2(num(p[1]), num(p[2])));
            }
            return any && w.Points.Count >= 2 ? w : null;
        }

        /// <summary>The distance from a point to this wall's centre line.</summary>
        public double DistanceTo(P2 pt)
        {
            double best = double.MaxValue;
            int n = Points.Count, segs = Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
                best = Math.Min(best, OpeningCut.DistanceToSegment(new Seg(Points[i], Points[(i + 1) % n]), pt));
            return best;
        }

        /// <summary>The outlines the wall is drawn as.</summary>
        public List<List<P2>> Outlines(double mmToUnits) =>
            WallGeometry.Outline(Points, Closed, ThicknessMm * mmToUnits, Justify);
    }

    /// <summary>The wall IDs carried on an outline, packed into strings short enough for extended data (255 characters each).</summary>
    public static class WallIds
    {
        private const int Chunk = 200;

        public static string[] Pack(IEnumerable<string> ids)
        {
            var list = ids.Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var chunks = new List<string>();
            var cur = new System.Text.StringBuilder();
            foreach (var id in list)
            {
                if (cur.Length > 0 && cur.Length + 1 + id.Length > Chunk) { chunks.Add(cur.ToString()); cur.Clear(); }
                if (cur.Length > 0) cur.Append(',');
                cur.Append(id);
            }
            if (cur.Length > 0) chunks.Add(cur.ToString());
            return chunks.ToArray();
        }

        public static List<string> Unpack(IEnumerable<string> chunks) =>
            chunks.SelectMany(c => (c ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>
        /// The walls that belong together: starting from the given IDs, every outline sharing an ID with the group joins it, until no more do.
        /// Each outline is given as the IDs it carries. Returns the group's IDs and the indexes of its outlines.
        /// </summary>
        public static void Group(IList<IList<string>> outlines, IEnumerable<string> start, out HashSet<string> ids, out List<int> members)
        {
            ids = new HashSet<string>(start, StringComparer.OrdinalIgnoreCase);
            members = new List<int>();
            var taken = new HashSet<int>();
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < outlines.Count; i++)
                {
                    if (taken.Contains(i) || !outlines[i].Any(ids.Contains)) continue;
                    taken.Add(i); members.Add(i);
                    foreach (var id in outlines[i]) if (ids.Add(id)) grew = true;
                    grew = true;
                }
            }
            members.Sort();
        }

        public static string Format(int number) => "W" + number.ToString("0000", CultureInfo.InvariantCulture);

        public static int NumberOf(string id)
        {
            int n;
            return id != null && id.Length > 1 && id[0] == 'W' && int.TryParse(id.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }
    }
}
