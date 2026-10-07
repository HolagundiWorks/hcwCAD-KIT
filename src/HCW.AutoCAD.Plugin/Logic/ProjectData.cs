using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>One project detail: the key it is kept under, the label shown, and the title block field it fills (null when it has none).</summary>
    public class ProjectField
    {
        public string Key, Label, TitleField;
        public ProjectField(string key, string label, string titleField) { Key = key; Label = label; TitleField = titleField; }
    }

    /// <summary>
    /// The project data kept once in the drawing: the details that go in the title block (name plate), and the standard beam depths. Floors and their
    /// heights are the levels of the take-off book, not kept here. Held as lines of KEY|value. No CAD types are used here.
    /// </summary>
    public class ProjectData
    {
        public static readonly IReadOnlyList<ProjectField> Fields = new[]
        {
            new ProjectField("PROJECT_TITLE", "Project title", "PROJECT_TITLE"),
            new ProjectField("OWNER", "Owner", "OWNER"),
            new ProjectField("ARCHITECT", "Consulting architect", "ARCHITECT"),
            new ProjectField("STABILITY", "Structural / stability engineer", "STABILITY"),
            new ProjectField("PID", "Property ID (PID)", "PID"),
            new ProjectField("SITE_AREA", "Site area", "SITE_AREA"),
            new ProjectField("PLOT_USE", "Plot use", "PLOT_USE"),
            new ProjectField("ADDRESS", "Site address", null),
            new ProjectField("AUTHORITY", "Local authority", null),
            new ProjectField("DATE", "Date", null),
        };

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Standard beam depths in millimetres, smallest first, without repeats.</summary>
        public List<double> BeamDepthsMm = new List<double>();

        public string Get(string key) { string v; return _values.TryGetValue(key, out v) ? v : ""; }

        public void Set(string key, string value)
        {
            value = (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (value.Length == 0) _values.Remove(key); else _values[key] = value;
        }

        public IEnumerable<string> ToLines()
        {
            foreach (var f in Fields)
                if (_values.ContainsKey(f.Key)) yield return f.Key + "|" + _values[f.Key];
            if (BeamDepthsMm.Count > 0) yield return "BEAMS|" + FormatDepths(BeamDepthsMm);
        }

        public static ProjectData FromLines(IEnumerable<string> lines)
        {
            var p = new ProjectData();
            foreach (var line in lines ?? Enumerable.Empty<string>())
            {
                int bar = (line ?? "").IndexOf('|');
                if (bar <= 0) continue;
                string key = line.Substring(0, bar), value = line.Substring(bar + 1);
                if (string.Equals(key, "BEAMS", StringComparison.OrdinalIgnoreCase)) p.BeamDepthsMm = ParseDepths(value);
                else p.Set(key, value);
            }
            return p;
        }

        /// <summary>The title block fields this data fills: title field -> value, only for details that have a value.</summary>
        public Dictionary<string, string> TitleValues()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Fields)
                if (f.TitleField != null && _values.ContainsKey(f.Key)) map[f.TitleField] = _values[f.Key];
            return map;
        }

        /// <summary>Beam depths from text such as "300, 375; 450 600" (any of comma, semicolon, space): positive numbers, sorted, no repeats.</summary>
        public static List<double> ParseDepths(string text)
        {
            var res = new List<double>();
            foreach (var part in (text ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double v;
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 && !res.Any(x => Math.Abs(x - v) < 0.01)) res.Add(v);
            }
            res.Sort();
            return res;
        }

        public static string FormatDepths(IEnumerable<double> depths) =>
            string.Join(", ", depths.Select(d => Math.Round(d, 1).ToString("0.#", CultureInfo.InvariantCulture)));

        /// <summary>The name of the floor at a position: Ground, First, Second, Third, then Floor 5, Floor 6 ...</summary>
        public static string FloorName(int index)
        {
            switch (index)
            {
                case 0: return "Ground";
                case 1: return "First";
                case 2: return "Second";
                case 3: return "Third";
                case 4: return "Fourth";
                default: return "Floor " + (index + 1);
            }
        }

        /// <summary>
        /// The beam depth to use for a span: the smallest standard depth that is at least span / <paramref name="ratio"/> (a span to depth ratio such as 12),
        /// or the largest standard depth when none is deep enough; 0 when no depths are set.
        /// </summary>
        public static double BeamDepthFor(IList<double> depthsMm, double spanMm, double ratio)
        {
            if (depthsMm == null || depthsMm.Count == 0 || ratio <= 0) return 0;
            double need = spanMm / ratio;
            var sorted = depthsMm.OrderBy(d => d).ToList();
            foreach (var d in sorted) if (d + 1e-9 >= need) return d;
            return sorted.Last();
        }
    }
}
