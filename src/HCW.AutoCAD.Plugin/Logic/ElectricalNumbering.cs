using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HCW.AutoCAD.Plugin.Logic
{
    public class NumberItem
    {
        /// <summary>Any key the caller can use to find the block again.</summary>
        public int Key;
        /// <summary>The ID it already has (SB-01), or empty.</summary>
        public string Existing = "";
        public double X, Y;
    }

    /// <summary>Gives blocks their IDs (SB-01, LP-02 ...). No CAD types are used here.</summary>
    public static class ElectricalNumbering
    {
        public static string Format(string prefix, int number) => prefix + "-" + number.ToString(number < 100 ? "00" : "0");

        /// <summary>The number in an ID with this prefix, or -1 when it is not one.</summary>
        public static int NumberOf(string id, string prefix)
        {
            var m = Regex.Match(id ?? "", "^" + Regex.Escape(prefix) + @"-(\d+)$", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : -1;
        }

        /// <summary>
        /// The ID for every item. With <paramref name="renumberAll"/> false, an item keeps the ID it has when that ID is valid and
        /// nobody else has it (a copied block carries its old ID along, so a repeat is renumbered: the one nearest the
        /// start of the reading order keeps it); new items get the numbers after the highest one in use.
        /// With it true, every item is numbered again from 1 in reading order: left to right, then bottom to top.
        /// </summary>
        public static Dictionary<int, string> Assign(IList<NumberItem> items, string prefix, bool renumberAll)
        {
            var result = new Dictionary<int, string>();
            var ordered = items.OrderBy(i => i.X).ThenBy(i => i.Y).ToList();

            if (renumberAll)
            {
                int n = 1;
                foreach (var item in ordered) result[item.Key] = Format(prefix, n++);
                return result;
            }

            var taken = new HashSet<int>();
            var pending = new List<NumberItem>();
            foreach (var item in ordered)
            {
                int number = NumberOf(item.Existing, prefix);
                if (number > 0 && taken.Add(number)) result[item.Key] = Format(prefix, number);
                else pending.Add(item);
            }
            int next = taken.Count == 0 ? 1 : taken.Max() + 1;
            foreach (var item in pending) result[item.Key] = Format(prefix, next++);
            return result;
        }

        /// <summary>True when a block name matches one of the patterns (* matches anything), ignoring case.</summary>
        public static bool NameMatches(string blockName, IEnumerable<string> patterns)
        {
            foreach (var pattern in patterns)
            {
                string p = pattern.Trim();
                if (p.Length == 0) continue;
                string regex = "^" + Regex.Escape(p).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(blockName ?? "", regex, RegexOptions.IgnoreCase)) return true;
            }
            return false;
        }
    }
}
