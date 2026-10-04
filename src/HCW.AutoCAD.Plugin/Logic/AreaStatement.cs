using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class FloorInput
    {
        public string Name;
        /// <summary>Built-up area of the floor, square metres.</summary>
        public double Gross;
        /// <summary>Area left out of the floor area ratio (shafts, ducts, lift, stair where the bylaws allow), square metres.</summary>
        public double Deduction;
    }

    /// <summary>The building permit area statement: floor areas, floor area ratio and ground cover.</summary>
    public class AreaStatement
    {
        public class FloorRow
        {
            public string Name;
            public double Gross, Deduction, Net;
        }

        public List<FloorRow> Floors = new List<FloorRow>();
        public double TotalGross, TotalDeduction, TotalNet;
        public double Site;
        /// <summary>Total net built-up area as a percentage of the site, or null with no site area.</summary>
        public double? FarPercent;
        /// <summary>Footprint of the first (ground) floor, and its percentage of the site.</summary>
        public double GroundCover;
        public double? GroundCoverPercent;
        public List<string> Warnings = new List<string>();

        /// <summary>Net = gross less deductions (never below 0). The first floor listed is the ground floor for ground cover.</summary>
        public static AreaStatement Compute(IList<FloorInput> floors, double siteArea)
        {
            var s = new AreaStatement { Site = siteArea };
            foreach (var f in floors)
            {
                double net = f.Gross - f.Deduction;
                if (net < 0)
                {
                    s.Warnings.Add(f.Name + ": deductions (" + Fmt(f.Deduction) + ") are more than the floor area (" + Fmt(f.Gross) + "); net taken as 0.");
                    net = 0;
                }
                s.Floors.Add(new FloorRow { Name = f.Name, Gross = f.Gross, Deduction = f.Deduction, Net = net });
            }
            s.TotalGross = s.Floors.Sum(f => f.Gross);
            s.TotalDeduction = s.Floors.Sum(f => f.Deduction);
            s.TotalNet = s.Floors.Sum(f => f.Net);
            if (siteArea > 0)
            {
                s.FarPercent = s.TotalNet / siteArea * 100.0;
                if (s.Floors.Count > 0)
                {
                    s.GroundCover = s.Floors[0].Gross;
                    s.GroundCoverPercent = s.GroundCover / siteArea * 100.0;
                    if (s.GroundCoverPercent > 100) s.Warnings.Add("Ground cover is more than the site area.");
                }
            }
            else s.Warnings.Add("No site area, so the floor area ratio and ground cover are not worked out.");
            return s;
        }

        /// <summary>Title block tag values. Slots beyond the floors given read "--"; floors beyond the slots count in the totals only.</summary>
        public Dictionary<string, string> ToFields(int slots = 4)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < slots; i++)
            {
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (i < Floors.Count)
                {
                    d["FL" + n] = Floors[i].Name;
                    d["DED" + n] = Fmt(Floors[i].Deduction);
                    d["NET" + n] = Fmt(Floors[i].Net);
                    d["GROSS" + n] = Fmt(Floors[i].Gross);
                }
                else { d["FL" + n] = "--"; d["DED" + n] = "--"; d["NET" + n] = "--"; d["GROSS" + n] = "--"; }
            }
            d["TOT_DED"] = Fmt(TotalDeduction);
            d["TOT_NET"] = Fmt(TotalNet);
            d["TOT_GROSS"] = Fmt(TotalGross);
            d["SITE_AREA"] = Site > 0 ? Fmt(Site) : "--";
            d["FAR_ACH"] = FarPercent.HasValue ? Fmt(FarPercent.Value) : "--";
            d["GC_ACH"] = GroundCoverPercent.HasValue ? Fmt(GroundCover) : "--";
            d["GC_PCT"] = GroundCoverPercent.HasValue ? Fmt(GroundCoverPercent.Value) : "--";
            return d;
        }

        public static string Fmt(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
