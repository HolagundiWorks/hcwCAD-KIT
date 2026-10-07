using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    // Plain records for the hcw-aqc-bridge v1 file (docs/bridge). No CAD types: the command layer (Q-003) fills them from the drawing.
    // Lengths in the geometry rows (walls, openings, columns, lintels, rooms, slabs) are in the DRAWING'S UNITS, named by BridgeInput.Unit; areas of
    // rooms and slabs are in those units squared. The levels, their heights, the beam depths and the project's site area are already millimetres and
    // square metres, because that is how the project data and the levels keep them. A null optional field is left out of the file.

    public class BridgeSource { public string App = "hcwCAD-KIT", Host, Drawing, DrawingId, Exported; }
    public class BridgeProject { public string Name, ClientName, CompanyName, PreparedByName, Location, Address, Pid, PlotUse; public double? SiteAreaM2; }
    public class BridgeLevel { public string Name; public double HeightMm, SlabThicknessMm, BeamDepthMm; public double? CeilingMm, LintelBottomMm; }
    public class BridgeWall { public string Ref, Level, UnitType, Layer; public double Length, Height, Thickness; }
    public class BridgeOpening { public string Ref, Kind, Mark, Level, Type, WallRef; public double Width, Height; public double? Sill; }
    public class BridgeScheduleRow { public string Mark, Kind, Type; public double Width, Height; public double? Sill; public int Nos; }
    public class BridgeColumn { public string Ref, Mark, Level; public double Width, Depth; }
    public class BridgeLintel { public string Ref, Mark, Level; public double Opening, Width, Depth; public double? Bearing; }
    public class BridgeRoom { public string Ref, Name, Level; public double Area; public double? Length, Breadth; }
    public class BridgeSlab { public string Ref, Level; public double Area, Thickness; public double? Length, Breadth; public bool? Rectangular; }

    public class BridgeInput
    {
        public BridgeSource Source = new BridgeSource();
        /// <summary>The drawing's unit for the geometry rows: mm, cm, m, in or ft.</summary>
        public string Unit = "mm";
        public BridgeProject Project;
        public List<BridgeLevel> Levels = new List<BridgeLevel>();
        public List<double> BeamDepthsMm = new List<double>();
        public List<BridgeWall> Walls = new List<BridgeWall>();
        public List<BridgeOpening> Openings = new List<BridgeOpening>();
        public List<BridgeScheduleRow> OpeningSchedule = new List<BridgeScheduleRow>();
        public List<BridgeColumn> Columns = new List<BridgeColumn>();
        public List<BridgeLintel> Lintels = new List<BridgeLintel>();
        public List<BridgeRoom> Rooms = new List<BridgeRoom>();
        public List<BridgeSlab> Slabs = new List<BridgeSlab>();
    }

    /// <summary>Writes the hcw-aqc-bridge v1 JSON (docs/bridge) with a small hand-written writer, so the plugin needs no JSON library next to it.</summary>
    public static class BridgeExport
    {
        /// <summary>Millimetres in one drawing unit; throws for a unit the file cannot be converted from.</summary>
        public static double MmPerUnit(string unit)
        {
            double k = StairFormat.MmPer(unit);
            if (k <= 0) throw new ArgumentException("Unknown drawing unit \"" + unit + "\" (use mm, cm, m, in or ft).", "unit");
            return k;
        }

        public static double ToMm(double value, string unit) => value * MmPerUnit(unit);

        /// <summary>An area in drawing units squared as square metres.</summary>
        public static double ToSquareMetres(double area, string unit) { double k = MmPerUnit(unit) / 1000.0; return area * k * k; }

        public static string ToJson(BridgeInput input)
        {
            if (input == null) throw new ArgumentNullException("input");
            double k = MmPerUnit(input.Unit);
            var src = input.Source ?? new BridgeSource();
            var o = new StringBuilder();
            o.Append("{\n");
            o.Append("  \"format\": \"hcw-aqc-bridge\",\n");
            o.Append("  \"version\": 1,\n");
            o.Append("  \"source\": ").Append(Obj(F("app", src.App), F("host", src.Host), F("drawing", src.Drawing), F("drawing_id", src.DrawingId), F("exported", src.Exported))).Append(",\n");
            o.Append("  \"units\": \"mm\"");

            var p = input.Project;
            if (p != null)
            {
                string pj = Obj(F("name", p.Name), F("client_name", p.ClientName), F("company_name", p.CompanyName), F("prepared_by_name", p.PreparedByName),
                    F("location", p.Location), F("address", p.Address), F("pid", p.Pid), F("site_area_m2", p.SiteAreaM2), F("plot_use", p.PlotUse));
                if (pj != "{  }") o.Append(",\n  \"project\": ").Append(pj);
            }
            Section(o, "levels", input.Levels.Select(l => Obj(F("name", l.Name), F("height_mm", l.HeightMm), F("slab_thickness_mm", l.SlabThicknessMm),
                F("beam_depth_mm", l.BeamDepthMm), F("ceiling_mm", l.CeilingMm), F("lintel_bottom_mm", l.LintelBottomMm))));
            if (input.BeamDepthsMm.Count > 0)
                o.Append(",\n  \"beam_depths_mm\": [ ").Append(string.Join(", ", input.BeamDepthsMm.Select(Num))).Append(" ]");
            Section(o, "walls", input.Walls.Select(w => Obj(F("ref", w.Ref), F("level", w.Level), F("length_mm", w.Length * k), F("height_mm", w.Height * k),
                F("thickness_mm", w.Thickness * k), F("unit_type", w.UnitType), F("layer", w.Layer))));
            Section(o, "openings", input.Openings.Select(x => Obj(F("ref", x.Ref), F("kind", x.Kind), F("mark", x.Mark), F("level", x.Level), F("width_mm", x.Width * k),
                F("height_mm", x.Height * k), F("sill_mm", Mm(x.Sill, k)), F("type", x.Type), F("wall_ref", x.WallRef))));
            Section(o, "opening_schedule", input.OpeningSchedule.Select(x => Obj(F("mark", x.Mark), F("kind", x.Kind), F("width_mm", x.Width * k), F("height_mm", x.Height * k),
                F("sill_mm", Mm(x.Sill, k)), F("nos", x.Nos), F("type", x.Type))));
            Section(o, "columns", input.Columns.Select(c => Obj(F("ref", c.Ref), F("mark", c.Mark), F("level", c.Level), F("width_mm", c.Width * k), F("depth_mm", c.Depth * k))));
            Section(o, "lintels", input.Lintels.Select(l => Obj(F("ref", l.Ref), F("mark", l.Mark), F("level", l.Level), F("opening_mm", l.Opening * k),
                F("bearing_mm", Mm(l.Bearing, k)), F("width_mm", l.Width * k), F("depth_mm", l.Depth * k))));
            Section(o, "rooms", input.Rooms.Select(r => Obj(F("ref", r.Ref), F("name", r.Name), F("level", r.Level), F("area_m2", ToSquareMetres(r.Area, input.Unit)),
                F("length_mm", Mm(r.Length, k)), F("breadth_mm", Mm(r.Breadth, k)))));
            Section(o, "slabs", input.Slabs.Select(s => Obj(F("ref", s.Ref), F("level", s.Level), F("area_m2", ToSquareMetres(s.Area, input.Unit)), F("thickness_mm", s.Thickness * k),
                F("length_mm", Mm(s.Length, k)), F("breadth_mm", Mm(s.Breadth, k)), F("rectangular", s.Rectangular))));
            o.Append("\n}\n");
            return o.ToString();
        }

        // ---- writer ----

        private static double? Mm(double? v, double k) => v.HasValue ? v * k : (double?)null;

        /// <summary>One field: its key and its already formatted JSON value, or null when the value is absent.</summary>
        private static KeyValuePair<string, string> F(string key, string value) => new KeyValuePair<string, string>(key, value == null ? null : Str(value));
        private static KeyValuePair<string, string> F(string key, double value) => new KeyValuePair<string, string>(key, Num(value));
        private static KeyValuePair<string, string> F(string key, double? value) => new KeyValuePair<string, string>(key, value.HasValue ? Num(value.Value) : null);
        private static KeyValuePair<string, string> F(string key, int value) => new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture));
        private static KeyValuePair<string, string> F(string key, bool? value) => new KeyValuePair<string, string>(key, value.HasValue ? (value.Value ? "true" : "false") : null);

        private static string Obj(params KeyValuePair<string, string>[] fields)
        {
            var parts = fields.Where(f => f.Value != null && !(f.Value == "\"\"" && IsOptionalText(f.Key))).Select(f => "\"" + f.Key + "\": " + f.Value);
            return "{ " + string.Join(", ", parts) + " }";
        }

        // an optional text field left empty is treated as not given; the fields the file requires are always written
        private static bool IsOptionalText(string key) => key != "name" && key != "ref" && key != "mark" && key != "kind" && key != "level" && key != "app" && key != "drawing_id" && key != "exported";

        private static void Section(StringBuilder o, string name, IEnumerable<string> rows)
        {
            var list = rows.ToList();
            if (list.Count == 0) return;
            o.Append(",\n  \"").Append(name).Append("\": [\n");
            o.Append(string.Join(",\n", list.Select(r => "    " + r)));
            o.Append("\n  ]");
        }

        /// <summary>A number with up to three decimals and no trailing zeros, using a point.</summary>
        public static string Num(double v)
        {
            double r = Math.Round(v, 3, MidpointRounding.AwayFromZero);
            if (r == 0) r = 0;                                    // no "-0"
            return r.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>A JSON string: quotes and backslashes escaped, control characters as \u00xx.</summary>
        public static string Str(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c);
                        break;
                }
            }
            return b.Append('"').ToString();
        }
    }
}
