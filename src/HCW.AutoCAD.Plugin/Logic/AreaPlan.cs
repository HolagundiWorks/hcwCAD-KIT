using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Where the outlines of a site or floor come from: polylines picked by handle, or every closed polyline on some layers.</summary>
    public class AreaSource
    {
        public List<long> Handles = new List<long>();
        public List<string> Layers = new List<string>();

        public bool IsEmpty => Handles.Count == 0 && Layers.Count == 0;

        public string Encode()
        {
            if (Handles.Count > 0) return "H:" + string.Join(",", Handles.Select(h => h.ToString("X", CultureInfo.InvariantCulture)));
            if (Layers.Count > 0) return "L:" + string.Join(";", Layers);
            return "-";
        }

        public static AreaSource Decode(string text)
        {
            var s = new AreaSource();
            if (string.IsNullOrEmpty(text) || text.Length < 2) return s;
            string body = text.Substring(2);
            if (text.StartsWith("H:", StringComparison.Ordinal))
                foreach (var part in body.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    long h;
                    if (long.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out h)) s.Handles.Add(h);
                }
            else if (text.StartsWith("L:", StringComparison.Ordinal))
                s.Layers.AddRange(body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0));
            return s;
        }
    }

    public class AreaFloorSource
    {
        public string Name = "";
        public AreaSource Gross = new AreaSource();
        public AreaSource Deduction = new AreaSource();
    }

    /// <summary>What the area statement was made from, kept in the drawing so it can be worked out again when the outlines change.</summary>
    public class AreaConfig
    {
        public AreaSource Site = new AreaSource();
        public List<AreaFloorSource> Floors = new List<AreaFloorSource>();
        public bool FillTitleBlocks = true;
        /// <summary>Top-left of the area table, or null when none is drawn.</summary>
        public P2? TableAt;

        public List<string> ToLines()
        {
            Func<double, string> d = v => v.ToString("R", CultureInfo.InvariantCulture);
            var lines = new List<string> { Fields.Join("S", Site.Encode()), Fields.Join("O", FillTitleBlocks ? "1" : "0") };
            foreach (var f in Floors) lines.Add(Fields.Join("F", f.Name, f.Gross.Encode(), f.Deduction.Encode()));
            if (TableAt.HasValue) lines.Add(Fields.Join("T", d(TableAt.Value.X), d(TableAt.Value.Y)));
            return lines;
        }

        public static AreaConfig FromLines(IEnumerable<string> lines)
        {
            var c = new AreaConfig();
            bool any = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (p[0] == "S" && p.Length >= 2) { c.Site = AreaSource.Decode(p[1]); any = true; }
                else if (p[0] == "O" && p.Length >= 2) c.FillTitleBlocks = p[1] == "1";
                else if (p[0] == "F" && p.Length >= 4) { c.Floors.Add(new AreaFloorSource { Name = p[1], Gross = AreaSource.Decode(p[2]), Deduction = AreaSource.Decode(p[3]) }); any = true; }
                else if (p[0] == "T" && p.Length >= 3)
                {
                    double x, y;
                    if (double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        c.TableAt = new P2(x, y);
                }
            }
            return any ? c : null;
        }

        /// <summary>The layers the configuration reads from, so a change on one of them can trigger a refresh.</summary>
        public HashSet<string> AllLayers()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in Site.Layers) set.Add(l);
            foreach (var f in Floors) { foreach (var l in f.Gross.Layers) set.Add(l); foreach (var l in f.Deduction.Layers) set.Add(l); }
            return set;
        }

        public HashSet<long> AllHandles()
        {
            var set = new HashSet<long>(Site.Handles);
            foreach (var f in Floors) { foreach (var h in f.Gross.Handles) set.Add(h); foreach (var h in f.Deduction.Handles) set.Add(h); }
            return set;
        }
    }
}
