using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>
    /// Named note sets (ELECTRIC NOTES, CONSTRUCTION NOTES, …) saved beside
    /// the user profile so they can be dropped onto any sheet.
    /// </summary>
    public static class TitleNoteLibrary
    {
        public static string Current = "CONSTRUCTION NOTES";

        private static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "hcwCAD-KIT");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "title-notes.txt");
            }
        }

        public static void Ensure()
        {
            if (File.Exists(FilePath)) return;
            var seed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CONSTRUCTION NOTES"] =
                    "1. ALL DIMENSIONS ARE IN METRES UNLESS NOTED OTHERWISE.\n" +
                    "2. DO NOT SCALE THE DRAWING. FOLLOW WRITTEN DIMENSIONS.\n" +
                    "3. ALL LEVELS ARE IN METRES.\n" +
                    "4. WORK SHALL CONFORM TO IS 456 AND THE APPROVED STRUCTURAL DRAWINGS.\n" +
                    "5. DISCREPANCIES SHALL BE REPORTED BEFORE EXECUTION.",
                ["ELECTRIC NOTES"] =
                    "1. ALL ELECTRICAL WORK SHALL CONFORM TO IS 732 AND THE LOCAL SUPPLY AUTHORITY.\n" +
                    "2. CONDUITS SHALL BE CONCEALED UNLESS NOTED OTHERWISE.\n" +
                    "3. EARTHING SHALL BE PROVIDED AS PER IS 3043.\n" +
                    "4. DISTRIBUTION BOARDS AND CIRCUIT LOADS ARE INDICATIVE.",
                ["STABILITY CERTIFICATE"] =
                    "THE PROPOSED STRUCTURE IS DESIGNED AS PER I.S. CODE AND CAN BARE THE LOAD OF THE SUPER STRUCTURE.",
                ["GENERAL NOTES"] =
                    "1. THIS DRAWING IS FOR BUILDING PERMIT SUBMISSION.\n" +
                    "2. READ THIS SHEET WITH THE AREA STATEMENT AND THE STABILITY CERTIFICATE."
            };
            Save(seed);
        }

        public static List<string> Names()
        {
            Ensure();
            return Load().Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Get(string name)
        {
            var all = Load();
            foreach (var kv in all)
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            return "";
        }

        public static void SaveOne(string name, string body)
        {
            var all = Load();
            string key = all.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name.Trim();
            all[key] = body ?? "";
            Save(all);
            Current = key;
        }

        public static Dictionary<string, string> Load()
        {
            Ensure();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string name = null;
            var body = new StringBuilder();
            foreach (var raw in File.ReadAllLines(FilePath))
            {
                if (raw.StartsWith("=== ") && raw.EndsWith(" ==="))
                {
                    if (name != null) map[name] = body.ToString().TrimEnd();
                    name = raw.Substring(4, raw.Length - 8);
                    body.Clear();
                }
                else if (name != null)
                {
                    if (body.Length > 0) body.Append('\n');
                    body.Append(raw);
                }
            }
            if (name != null) map[name] = body.ToString().TrimEnd();
            return map;
        }

        private static void Save(Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append("=== ").Append(kv.Key).Append(" ===\r\n");
                sb.Append(kv.Value ?? "").Append("\r\n");
            }
            File.WriteAllText(FilePath, sb.ToString());
        }
    }
}
