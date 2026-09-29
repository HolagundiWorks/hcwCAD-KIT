using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>Named sheet fields (project title, drawing number, and any you add), saved with the kit.</summary>
    public static class SheetFieldLibrary
    {
        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hcwCAD-KIT");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "sheet-fields.txt");
            }
        }

        public static void Ensure()
        {
            if (File.Exists(FilePath)) return;
            Save(new List<KeyValuePair<string, string>>
            {
                Pair("Project title", "PROJECT TITLE"),
                Pair("Drawing title", "DRAWING TITLE"),
                Pair("Drawing no", ""),
                Pair("Size", "A3"),
                Pair("Plot use", ""),
                Pair("Owner", ""),
                Pair("Consulting architect", ""),
                Pair("PID", ""),
                Pair("Site area", "")
            });
        }

        public static List<KeyValuePair<string, string>> Load()
        {
            Ensure();
            var list = new List<KeyValuePair<string, string>>();
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                if (raw.Trim().Length == 0) continue;
                int cut = raw.IndexOf('|');
                if (cut < 0) list.Add(Pair(Unesc(raw), ""));
                else list.Add(Pair(Unesc(raw.Substring(0, cut)), Unesc(raw.Substring(cut + 1))));
            }
            return list;
        }

        public static void Save(IList<KeyValuePair<string, string>> fields)
        {
            var sb = new StringBuilder();
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.Key)) continue;
                sb.Append(Esc(field.Key.Trim())).Append('|').Append(Esc(field.Value ?? "")).Append("\r\n");
            }
            File.WriteAllText(FilePath, sb.ToString());
        }

        private static KeyValuePair<string, string> Pair(string name, string value) =>
            new KeyValuePair<string, string>(name, value);

        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("|", "\\p").Replace("\r", "").Replace("\n", "\\n");
        private static string Unesc(string s)
        {
            var cur = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    cur.Append(n == 'p' ? '|' : n == 'n' ? '\n' : n);
                }
                else cur.Append(s[i]);
            }
            return cur.ToString();
        }
    }
}
