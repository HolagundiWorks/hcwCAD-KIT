using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// Reads an hcw-aqc-bridge v1 file back (the other direction: AQC writes the levels, project details and beam depths, the plugin reads them) and works out
    /// what applying it would change, so the owner sees the changes before anything is written. No CAD types are used here.
    /// </summary>
    public static class BridgeImport
    {
        /// <summary>The file as plain records, all in millimetres (Unit is "mm"). Throws FormatException (bad JSON) or ArgumentException (not a bridge file we can read).</summary>
        public static BridgeInput Read(string json)
        {
            var root = JsonLite.Parse(json);
            if (!(root is Dictionary<string, object>)) throw new ArgumentException("This is not a bridge file: the top level is not an object.");
            if (JsonLite.Text(root, "format") != "hcw-aqc-bridge") throw new ArgumentException("This is not an hcw-aqc-bridge file (format is \"" + JsonLite.Text(root, "format") + "\").");
            var version = JsonLite.Number(root, "version");
            if (version != 1) throw new ArgumentException("This bridge file is version " + (version.HasValue ? version.Value.ToString(CultureInfo.InvariantCulture) : "(missing)") + "; this plugin reads version 1.");
            if (JsonLite.Text(root, "units") != "mm") throw new ArgumentException("This bridge file is not in millimetres (units \"" + JsonLite.Text(root, "units") + "\").");

            var src = JsonLite.Get(root, "source");
            var input = new BridgeInput
            {
                Unit = "mm",
                Source = new BridgeSource { App = JsonLite.Text(src, "app"), Host = JsonLite.Text(src, "host"), Drawing = JsonLite.Text(src, "drawing"), DrawingId = JsonLite.Text(src, "drawing_id"), Exported = JsonLite.Text(src, "exported") },
            };
            var p = JsonLite.Get(root, "project");
            if (p != null)
                input.Project = new BridgeProject
                {
                    Name = JsonLite.Text(p, "name"), ClientName = JsonLite.Text(p, "client_name"), CompanyName = JsonLite.Text(p, "company_name"), PreparedByName = JsonLite.Text(p, "prepared_by_name"),
                    Location = JsonLite.Text(p, "location"), Address = JsonLite.Text(p, "address"), Pid = JsonLite.Text(p, "pid"), SiteAreaM2 = JsonLite.Number(p, "site_area_m2"), PlotUse = JsonLite.Text(p, "plot_use"),
                };
            foreach (var l in JsonLite.List(root, "levels"))
                input.Levels.Add(new BridgeLevel
                {
                    Name = JsonLite.Text(l, "name") ?? "", HeightMm = JsonLite.Number(l, "height_mm") ?? 0, SlabThicknessMm = JsonLite.Number(l, "slab_thickness_mm") ?? 0,
                    BeamDepthMm = JsonLite.Number(l, "beam_depth_mm") ?? 0, CeilingMm = JsonLite.Number(l, "ceiling_mm"), LintelBottomMm = JsonLite.Number(l, "lintel_bottom_mm"),
                });
            foreach (var d in JsonLite.List(root, "beam_depths_mm"))
                if (d is double && (double)d > 0) input.BeamDepthsMm.Add((double)d);
            return input;
        }

        public class Change { public string What, From, To; public override string ToString() => What + ": " + (string.IsNullOrEmpty(From) ? "(none)" : From) + " -> " + To; }

        public class Plan
        {
            /// <summary>Changes to project details: key (as ProjectData keeps it), the label, the old and the new text.</summary>
            public List<KeyValuePair<string, Change>> Details = new List<KeyValuePair<string, Change>>();
            /// <summary>Floors that already exist, with the values that would change.</summary>
            public List<Change> LevelChanges = new List<Change>();
            /// <summary>The floors after applying: existing floors updated, floors only in the file added at the end, floors only in the drawing left as they are.</summary>
            public List<LevelRow> Floors = new List<LevelRow>();
            /// <summary>Beam depth for each floor name after applying.</summary>
            public Dictionary<string, double> FloorBeams = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            public List<double> BeamDepthsMm;
            public List<string> Added = new List<string>(), Skipped = new List<string>();
            public bool IsEmpty => Details.Count == 0 && LevelChanges.Count == 0 && Added.Count == 0 && BeamDepthsMm == null;
        }

        private static bool IsPlinth(string name) => (name ?? "").IndexOf("plinth", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string Mm(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>
        /// What applying the file would do to this drawing's floors and project details. Floors are matched by name, then by position among the floors above the
        /// plinth (AQC's plinth, Lvl0, is AQC's and is never imported). A value that is absent in the file leaves the drawing's value alone.
        /// </summary>
        public static Plan Compare(BridgeInput file, IList<LevelRow> floors, ProjectData data)
        {
            var plan = new Plan();
            var p = file.Project;
            if (p != null)
            {
                Action<string, string, string> detail = (key, label, value) =>
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    string old = data.Get(key);
                    if (!string.Equals(old, value.Trim(), StringComparison.Ordinal)) plan.Details.Add(new KeyValuePair<string, Change>(key, new Change { What = label, From = old, To = value.Trim() }));
                };
                detail("PROJECT_TITLE", "Project title", p.Name);
                detail("OWNER", "Owner", p.ClientName);
                detail("ARCHITECT", "Consulting architect", p.CompanyName);
                detail("ADDRESS", "Site address", p.Address);
                detail("PID", "Property ID", p.Pid);
                detail("PLOT_USE", "Plot use", p.PlotUse);
                if (p.SiteAreaM2.HasValue && p.SiteAreaM2.Value > 0)
                {
                    string area = p.SiteAreaM2.Value.ToString("0.##", CultureInfo.InvariantCulture);
                    var old = BridgeMap.Project(data).SiteAreaM2;
                    if (!old.HasValue || Math.Abs(old.Value - p.SiteAreaM2.Value) > 0.005) plan.Details.Add(new KeyValuePair<string, Change>("SITE_AREA", new Change { What = "Site area (sq m)", From = data.Get("SITE_AREA"), To = area }));
                }
            }

            plan.Floors = floors.Select(f => new LevelRow { Name = f.Name, FflMm = f.FflMm, CeilingMm = f.CeilingMm, LintelMm = f.LintelMm, SlabMm = f.SlabMm }).ToList();
            foreach (var f in plan.Floors) plan.FloorBeams[f.Name] = data.FloorBeamMm(f.Name);

            var above = file.Levels.Where(l => !IsPlinth(l.Name)).ToList();
            for (int i = 0; i < above.Count; i++)
            {
                var lv = above[i];
                if (lv.HeightMm <= 0) { plan.Skipped.Add("level \"" + lv.Name + "\" has no height"); continue; }
                var target = plan.Floors.FirstOrDefault(f => string.Equals(f.Name, lv.Name, StringComparison.OrdinalIgnoreCase));
                if (target == null && i < plan.Floors.Count && !above.Any(a => string.Equals(a.Name, plan.Floors[i].Name, StringComparison.OrdinalIgnoreCase))) target = plan.Floors[i];   // by position when the names differ
                if (target == null)
                {
                    var add = new LevelRow { Name = lv.Name, FflMm = lv.HeightMm, SlabMm = lv.SlabThicknessMm, CeilingMm = lv.CeilingMm ?? Math.Max(0, lv.HeightMm - 150), LintelMm = lv.LintelBottomMm ?? 2100 };
                    plan.Floors.Add(add); plan.FloorBeams[add.Name] = lv.BeamDepthMm > 0 ? lv.BeamDepthMm : data.DefaultBeamMm;
                    plan.Added.Add(lv.Name + ": floor to floor " + Mm(lv.HeightMm) + ", slab " + Mm(lv.SlabThicknessMm) + ", beam " + Mm(lv.BeamDepthMm));
                    continue;
                }
                string name = target.Name;
                Action<string, double, double, Action<double>> set = (what, old, nw, apply) =>
                {
                    if (Math.Abs(old - nw) > 0.05) { plan.LevelChanges.Add(new Change { What = name + " " + what, From = Mm(old), To = Mm(nw) }); apply(nw); }
                };
                set("floor to floor", target.FflMm, lv.HeightMm, v => target.FflMm = v);
                set("slab thickness", target.SlabMm, lv.SlabThicknessMm, v => target.SlabMm = v);
                if (lv.CeilingMm.HasValue) set("ceiling height", target.CeilingMm, lv.CeilingMm.Value, v => target.CeilingMm = v);
                if (lv.LintelBottomMm.HasValue) set("lintel bottom", target.LintelMm, lv.LintelBottomMm.Value, v => target.LintelMm = v);
                if (lv.BeamDepthMm > 0) { double was = data.FloorBeamMm(name); set("beam depth", was, lv.BeamDepthMm, v => plan.FloorBeams[name] = v); }
            }
            if (file.BeamDepthsMm.Count > 0 && !file.BeamDepthsMm.OrderBy(x => x).SequenceEqual(data.BeamDepthsMm.OrderBy(x => x)))
                plan.BeamDepthsMm = file.BeamDepthsMm.OrderBy(x => x).Distinct().ToList();
            return plan;
        }
    }
}
