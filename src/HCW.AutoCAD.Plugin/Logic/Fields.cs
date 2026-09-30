using System.Collections.Generic;
using System.Text;

namespace HCW.AutoCAD.Plugin.Logic
{
    /// <summary>Joins and splits '|' separated records, escaping '|' and '\' inside a field. No CAD types are used here.</summary>
    public static class Fields
    {
        public static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace("|", "\\p");

        public static string[] Split(string line)
        {
            var parts = new List<string>();
            var cur = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\\' && i + 1 < line.Length)
                {
                    char n = line[++i];
                    cur.Append(n == 'p' ? '|' : n);
                }
                else if (line[i] == '|')
                {
                    parts.Add(cur.ToString());
                    cur.Clear();
                }
                else cur.Append(line[i]);
            }
            parts.Add(cur.ToString());
            return parts.ToArray();
        }

        public static string Join(params string[] fields)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(Escape(fields[i]));
            }
            return sb.ToString();
        }
    }
}
