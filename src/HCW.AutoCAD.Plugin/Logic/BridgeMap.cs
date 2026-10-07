using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>The CAD-free parts of turning a drawing into the bridge file: marks, wall references, the schedule and the project details. No CAD types.</summary>
    public static class BridgeMap
    {
        public class ScheduleEntry { public string Mark, Kind; public double WidthMm, HeightMm, SillMm; }

        /// <summary>Length of a wall's centre line (drawing units); a closed run includes the closing segment.</summary>
        public static double WallLength(WallRecord w)
        {
            double len = 0; int n = w.Points.Count, segs = w.Closed ? n : n - 1;
            for (int i = 0; i < segs; i++) len += w.Points[i].DistanceTo(w.Points[(i + 1) % n]);
            return len;
        }

        /// <summary>The id of the wall whose centre line is nearest the point and within reach; null when none is.</summary>
        public static string NearestWall(P2 point, IEnumerable<WallRecord> walls, double reach)
        {
            string best = null; double bestD = double.MaxValue;
            foreach (var w in walls)
            {
                double d = w.DistanceTo(point);
                if (d <= reach && d < bestD) { bestD = d; best = w.Id; }
            }
            return best;
        }

        /// <summary>
        /// The mark of an opening: the schedule entry of the same kind and size (within 1 mm; the sill counts for windows), else the standard mark for
        /// its width, else a mark made from the kind and width (D-1050, W-1350) so the file still has one; run HCWOPENSYNC to give such openings a real mark.
        /// </summary>
        public static string OpeningMark(bool door, double widthMm, double heightMm, double sillMm, IEnumerable<ScheduleEntry> schedule, double ventilatorMaxHeightMm)
        {
            string kind = door ? "Door" : "Window";
            var hit = (schedule ?? new ScheduleEntry[0]).FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(e.WidthMm - widthMm) < 1 && Math.Abs(e.HeightMm - heightMm) < 1 && (door || Math.Abs(e.SillMm - sillMm) < 1));
            if (hit != null && !string.IsNullOrWhiteSpace(hit.Mark)) return hit.Mark;
            string std = door ? OpeningStandards.DoorCode(widthMm) : OpeningStandards.WindowCode(widthMm, heightMm, ventilatorMaxHeightMm);
            if (std != null) return std;
            return (door ? "D-" : "W-") + Math.Round(widthMm).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>One schedule row for each mark, kind, size and type, with the number of openings; doors first, then marks in natural order. Sizes are in the drawing units of the rows.</summary>
        public static List<BridgeScheduleRow> Schedule(IEnumerable<BridgeOpening> openings, double mmPerUnit)
        {
            Func<double, string> key = v => Math.Round(v * mmPerUnit).ToString(CultureInfo.InvariantCulture);
            return openings
                .GroupBy(o => string.Join("|", o.Mark, o.Kind, key(o.Width), key(o.Height), key(o.Sill ?? 0), o.Type ?? ""))
                .Select(g => new BridgeScheduleRow { Mark = g.First().Mark, Kind = g.First().Kind, Width = g.First().Width, Height = g.First().Height, Sill = g.First().Sill, Type = g.First().Type, Nos = g.Count() })
                .OrderBy(r => string.Equals(r.Kind, "Door", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(r => r.Mark, Comparer<string>.Create(ElectricalSchedule.NaturalCompare))
                .ToList();
        }

        /// <summary>The project details of the drawing as the file's project block.</summary>
        public static BridgeProject Project(ProjectData data)
        {
            double area;
            var text = (data.Get("SITE_AREA") ?? "").Replace(",", ".");
            var digits = new string(text.TakeWhile(c => char.IsDigit(c) || c == '.' || c == ' ').ToArray()).Trim();
            bool ok = double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out area) && area > 0;
            return new BridgeProject
            {
                Name = Blank(data.Get("PROJECT_TITLE")), ClientName = Blank(data.Get("OWNER")), CompanyName = Blank(data.Get("ARCHITECT")),
                Address = Blank(data.Get("ADDRESS")), Pid = Blank(data.Get("PID")), PlotUse = Blank(data.Get("PLOT_USE")),
                SiteAreaM2 = ok ? area : (double?)null,
            };
        }

        /// <summary>
        /// A key that is the same for two copies of one outline (the centre to a tenth of a millimetre-unit and the area to a square millimetre-unit):
        /// the room outline kept on ROOM-RECT and the copies on the take-off layers must count as one room.
        /// </summary>
        public static string OutlineKey(IList<P2> outline, double area, double unitsPerMm)
        {
            double cx = outline.Average(p => p.X), cy = outline.Average(p => p.Y);
            Func<double, string> r = v => Math.Round(v / unitsPerMm, 1).ToString(CultureInfo.InvariantCulture);
            return r(cx) + "|" + r(cy) + "|" + Math.Round(area / (unitsPerMm * unitsPerMm)).ToString(CultureInfo.InvariantCulture) + "|" + outline.Count;
        }

        private static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        /// <summary>The levels of the file: one for each floor, with the beam depth kept for it in the project data (millimetres).</summary>
        public static List<BridgeLevel> Levels(IEnumerable<LevelRow> floors, ProjectData data) =>
            floors.Select(f => new BridgeLevel
            {
                Name = f.Name, HeightMm = f.FflMm, SlabThicknessMm = f.SlabMm, BeamDepthMm = data.FloorBeamMm(f.Name),
                CeilingMm = f.CeilingMm > 0 ? f.CeilingMm : (double?)null, LintelBottomMm = f.LintelMm > 0 ? f.LintelMm : (double?)null,
            }).ToList();

        /// <summary>Floors whose clear column height (height less slab less beam) is not positive, which AQC cannot use.</summary>
        public static List<string> LevelProblems(IEnumerable<BridgeLevel> levels) =>
            levels.Where(l => l.HeightMm - l.SlabThicknessMm - l.BeamDepthMm <= 0)
                  .Select(l => l.Name + ": height " + Math.Round(l.HeightMm) + " less slab " + Math.Round(l.SlabThicknessMm) + " less beam " + Math.Round(l.BeamDepthMm) + " leaves no clear height").ToList();
    }
}
