using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Layer name patterns and title block field names for the sheet set tool. No CAD types are used here.</summary>
    public static class SheetData
    {
        /// <summary>
        /// The layers to freeze in a viewport so only the ones matching the patterns show. Patterns are separated by ; or , and use * and ? as
        /// wildcards, ignoring case. Layer 0 and Defpoints are never frozen. No patterns means nothing is frozen.
        /// </summary>
        public static List<string> Hidden(IEnumerable<string> allLayers, string showPatterns)
        {
            var patterns = (showPatterns ?? "").Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0)
                .Select(p => new Regex("^" + Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase)).ToList();
            if (patterns.Count == 0) return new List<string>();
            return allLayers.Where(l => !string.Equals(l, "0", StringComparison.Ordinal) && !string.Equals(l, "Defpoints", StringComparison.OrdinalIgnoreCase)
                && !patterns.Any(p => p.IsMatch(l))).ToList();
        }

        /// <summary>
        /// Title block attribute values from the fields library (label and value pairs such as "Owner" and "Mr Rao"): the label upper-cased with
        /// spaces turned to underscores (PROJECT_TITLE, OWNER, PID, SITE_AREA), "Consulting architect" as ARCHITECT. Fields with no value, and the
        /// per-sheet ones (drawing number and title), are left out.
        /// </summary>
        public static Dictionary<string, string> TagsFromLibrary(IEnumerable<KeyValuePair<string, string>> library)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in library)
            {
                string tag = Regex.Replace((kv.Key ?? "").Trim().ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
                if (tag.Length == 0 || string.IsNullOrWhiteSpace(kv.Value)) continue;
                if (tag == "CONSULTING_ARCHITECT") tag = "ARCHITECT";
                if (tag == "DRAWING_NO" || tag == "DRAWING_TITLE") continue;
                d[tag] = kv.Value.Trim();
            }
            return d;
        }

        /// <summary>A title block value that has not been filled in: empty, dashes, or the template's own wording.</summary>
        public static bool IsBlank(string value, string tag)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0 || v == "--" || v == "-") return true;
            string placeholder = (tag ?? "").Replace('_', ' ');
            return string.Equals(v, placeholder, StringComparison.OrdinalIgnoreCase);
        }
    }
}
