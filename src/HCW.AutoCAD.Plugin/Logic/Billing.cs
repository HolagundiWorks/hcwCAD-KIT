using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Builds the worksheets for a take-off export, with an optional bill of quantities. No CAD types are used here.</summary>
    public static class Billing
    {
        public class TakeoffData
        {
            public string Name = "";
            public string[] Headers = new string[0];
            public List<string[]> Rows = new List<string[]>();
        }

        public class RateData
        {
            public string Takeoff = "";
            public string Unit = "";
            public double Rate;
        }

        /// <summary>
        /// The quantity of a take-off: the last cell of its GRAND TOTAL row, or of its TOTAL row when there is
        /// no grand total. Null when there is no such row or the cell is not a plain number.
        /// </summary>
        public static double? Quantity(TakeoffData takeoff)
        {
            string[] row = takeoff.Rows.LastOrDefault(r => r.Length > 0 && string.Equals(r[0], "GRAND TOTAL", StringComparison.OrdinalIgnoreCase))
                        ?? takeoff.Rows.LastOrDefault(r => r.Length > 0 && string.Equals(r[0], "TOTAL", StringComparison.OrdinalIgnoreCase));
            if (row == null || row.Length < 2) return null;
            double v;
            return double.TryParse(row[row.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (double?)v : null;
        }

        /// <summary>One sheet per take-off, plus a Bill sheet when at least one take-off has a rate.</summary>
        public static List<XlsxSheet> Sheets(IList<TakeoffData> takeoffs, IList<RateData> rates)
        {
            var sheets = new List<XlsxSheet>();
            foreach (var t in takeoffs)
            {
                var sheet = new XlsxSheet { Name = t.Name };
                sheet.Rows.Add(t.Headers.Select(h => XlsxCell.Str(h, true)).ToList());
                foreach (var r in t.Rows)
                {
                    bool total = r.Length > 0 && (r[0].StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
                                               || r[0].StartsWith("GRAND", StringComparison.OrdinalIgnoreCase));
                    sheet.Rows.Add(r.Select(c => XlsxCell.Auto(c, total)).ToList());
                }
                sheets.Add(sheet);
            }

            var bill = new XlsxSheet { Name = "Bill" };
            bill.Rows.Add(new[] { "Item", "Unit", "Quantity", "Rate", "Amount" }.Select(h => XlsxCell.Str(h, true)).ToList());
            foreach (var t in takeoffs)
            {
                var rate = rates == null ? null : rates.FirstOrDefault(r => string.Equals(r.Takeoff, t.Name, StringComparison.OrdinalIgnoreCase) && r.Rate > 0);
                double? quantity = Quantity(t);
                if (rate == null || quantity == null) continue;
                int n = bill.Rows.Count + 1; // the Excel row this entry will occupy
                bill.Rows.Add(new List<XlsxCell>
                {
                    XlsxCell.Str(t.Name), XlsxCell.Str(rate.Unit), XlsxCell.Num(quantity.Value),
                    XlsxCell.Num(rate.Rate), XlsxCell.Calc("C" + n + "*D" + n)
                });
            }
            if (bill.Rows.Count > 1)
            {
                bill.Rows.Add(new List<XlsxCell>
                {
                    XlsxCell.Str("TOTAL", true), null, null, null, XlsxCell.Calc("SUM(E2:E" + bill.Rows.Count + ")", true)
                });
                sheets.Add(bill);
            }
            return sheets;
        }
    }
}
