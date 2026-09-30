using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>
    /// A tiny key=value settings file. Lines starting with # or ; are comments; blank lines are ignored;
    /// keys are not case sensitive. No CAD types are used here.
    /// </summary>
    public class IniFile
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static IniFile Parse(string text)
        {
            var ini = new IniFile();
            if (text == null) return ini;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                ini._values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return ini;
        }

        public bool Has(string key) => _values.ContainsKey(key);

        public string Get(string key, string fallback)
        {
            string value;
            return _values.TryGetValue(key, out value) && value.Length > 0 ? value : fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            double v;
            return double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public int GetInt(string key, int fallback)
        {
            int v;
            return int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        public void Set(string key, string value) { _values[key] = value; }

        /// <summary>Text of a settings file with a comment above each key.</summary>
        public static string Format(IEnumerable<KeyValuePair<string, string[]>> keysWithHelp)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# hcwCAD-KIT settings. Lines starting with # are comments. Delete a line to use its default.");
            foreach (var entry in keysWithHelp)
            {
                sb.AppendLine();
                // entry.Value[0] is the value, the rest are help lines
                for (int i = 1; i < entry.Value.Length; i++) sb.AppendLine("# " + entry.Value[i]);
                sb.AppendLine(entry.Key + "=" + entry.Value[0]);
            }
            return sb.ToString();
        }
    }
}
