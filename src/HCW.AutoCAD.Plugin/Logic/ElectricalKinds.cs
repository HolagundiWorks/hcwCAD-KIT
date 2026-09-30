using System;
using System.Collections.Generic;
using System.Linq;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>One kind of electrical block: the switchboard, or a point, switch, socket or appliance wired to it.</summary>
    public class ElKind
    {
        /// <summary>The prefix of its IDs (SB-01, LP-01, GY-01).</summary>
        public string Code = "";
        public string Label = "";
        public bool IsBoard;
        /// <summary>LT: on the lighting wiring, PW: on the power wiring.</summary>
        public string Group = "LT";
        /// <summary>The settings key holding the default block names, used until blocks are mapped with ELBLOCKS.</summary>
        public string Setting = "";
    }

    /// <summary>The kinds of electrical block the tools know, in the order they are listed. No CAD types are used here.</summary>
    public static class ElectricalKinds
    {
        public static readonly ElKind[] All =
        {
            new ElKind { Code = "SB",  Label = "Switchboard",          IsBoard = true, Group = "LT", Setting = "ElectricalBoardBlocks" },
            new ElKind { Code = "LP",  Label = "Light point",          Group = "LT", Setting = "ElectricalLightBlocks" },
            new ElKind { Code = "FP",  Label = "Fan point",            Group = "LT", Setting = "ElectricalFanBlocks" },
            new ElKind { Code = "SW1", Label = "One way switch",       Group = "LT", Setting = "ElectricalBlocks_SW1" },
            new ElKind { Code = "SW2", Label = "Two way switch",       Group = "LT", Setting = "ElectricalBlocks_SW2" },
            new ElKind { Code = "CB",  Label = "Calling bell",         Group = "LT", Setting = "ElectricalBlocks_CB" },
            new ElKind { Code = "P5",  Label = "5 amp socket",         Group = "PW", Setting = "ElectricalBlocks_P5" },
            new ElKind { Code = "P15", Label = "15 amp socket",        Group = "PW", Setting = "ElectricalBlocks_P15" },
            new ElKind { Code = "AC",  Label = "Air conditioner",      Group = "PW", Setting = "ElectricalBlocks_AC" },
            new ElKind { Code = "WP",  Label = "Water purifier",       Group = "PW", Setting = "ElectricalBlocks_WP" },
            new ElKind { Code = "GY",  Label = "Geyser",               Group = "PW", Setting = "ElectricalBlocks_GY" },
            new ElKind { Code = "FR",  Label = "Fridge",               Group = "PW", Setting = "ElectricalBlocks_FR" },
            new ElKind { Code = "OV",  Label = "Oven",                 Group = "PW", Setting = "ElectricalBlocks_OV" },
            new ElKind { Code = "WF",  Label = "WiFi router",          Group = "PW", Setting = "ElectricalBlocks_WF" },
            new ElKind { Code = "TV",  Label = "TV",                   Group = "PW", Setting = "ElectricalBlocks_TV" }
        };

        public static ElKind Find(string code)
            => All.FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase));

        public static string LabelOf(string code)
        {
            var k = Find(code);
            return k == null ? code : k.Label;
        }
    }

    /// <summary>A column of the matrix schedule: its heading, and which kinds of point it lists.</summary>
    public class ColumnSpec
    {
        public string Heading = "";
        public List<string> Codes = new List<string>();
    }

    /// <summary>One point wired to one board.</summary>
    public class ElLink
    {
        public string Board = "";
        public string Point = "";
        public string Code = "";
    }

    /// <summary>
    /// The typical electrical schedule: one row per switchboard, one column per kind of point (5 amp, 15 amp, one way
    /// switches, two way switches, AC, geyser ...), each cell the numbers of the points wired to that board.
    /// </summary>
    public static class ElectricalMatrix
    {
        /// <summary>
        /// The columns after the switchboard column. "5 Amp=LP,FP,P5;15 Amp=P15;One way switches=SW1;AC=AC": each item is a
        /// heading, then "=", then the kinds (codes) it lists. Unknown codes and empty items are ignored.
        /// </summary>
        public const string DefaultColumns =
            "5 Amp=LP,FP,P5;15 Amp=P15;One way switches=SW1;2 way switches=SW2;AC=AC;Water purifier=WP;Geyser=GY;Fridge=FR;Oven=OV;WiFi router=WF;Calling bell=CB;TV=TV";

        public static List<ColumnSpec> ParseColumns(string text)
        {
            var result = new List<ColumnSpec>();
            foreach (var item in (text ?? "").Split(';'))
            {
                int eq = item.IndexOf('=');
                if (eq <= 0) continue;
                var spec = new ColumnSpec { Heading = item.Substring(0, eq).Trim() };
                foreach (var code in item.Substring(eq + 1).Split(','))
                {
                    var kind = ElectricalKinds.Find(code.Trim());
                    if (kind != null && !kind.IsBoard && !spec.Codes.Contains(kind.Code)) spec.Codes.Add(kind.Code);
                }
                if (spec.Heading.Length > 0 && spec.Codes.Count > 0) result.Add(spec);
            }
            return result;
        }

        /// <summary>
        /// The header row, a row per board, and a total row. <paramref name="counts"/> writes the number of points in each cell
        /// instead of their IDs. The total row counts each point once, even when it is wired to two boards.
        /// </summary>
        public static List<string[]> Build(IEnumerable<string> boardIds, IEnumerable<ElLink> links, IList<ColumnSpec> columns, bool counts, string boardHeading = "SB no")
        {
            var rows = new List<string[]>();
            var header = new List<string> { boardHeading };
            header.AddRange(columns.Select(c => c.Heading));
            rows.Add(header.ToArray());

            var linkList = links.ToList();
            var cmp = Comparer<string>.Create(ElectricalSchedule.NaturalCompare);
            foreach (var board in boardIds.Distinct().OrderBy(b => b, cmp))
            {
                var row = new List<string> { board };
                foreach (var col in columns)
                {
                    var ids = linkList.Where(l => l.Board == board && col.Codes.Contains(l.Code)).Select(l => l.Point).Distinct().OrderBy(p => p, cmp).ToList();
                    row.Add(ids.Count == 0 ? "" : counts ? ids.Count.ToString() : string.Join(", ", ids));
                }
                rows.Add(row.ToArray());
            }

            var total = new List<string> { "TOTAL" };
            foreach (var col in columns)
            {
                int n = linkList.Where(l => col.Codes.Contains(l.Code)).Select(l => l.Point).Distinct().Count();
                total.Add(n == 0 ? "" : n.ToString());
            }
            rows.Add(total.ToArray());
            return rows;
        }
    }
}
