using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Renumbers paper-space layouts in tab order, with a prefix, suffix and padded number.
    /// </summary>
    public class LayoutRenumberCommands
    {
        private static string _prefix = "";
        private static string _suffix = "";
        private static int _start = 1;
        private static int _digits = 2;

        [CommandMethod("RENUMBERLAYOUTS")]
        [CommandMethod("RL")]
        public void Renumber()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var sheets = new List<Sheet>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in dict)
                {
                    var lay = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                    if (lay.ModelType) continue;
                    sheets.Add(new Sheet { Id = entry.Value, Name = lay.LayoutName, Order = lay.TabOrder });
                }
                tr.Commit();
            }
            sheets.Sort((a, b) => a.Order.CompareTo(b.Order));
            if (sheets.Count == 0)
            {
                ed.WriteMessage("\nThis drawing has no paper layouts.");
                return;
            }

            List<string> chosen;
            using (var dlg = new UI.LayoutRenumberForm(sheets.Select(s => s.Name).ToList(), _prefix, _suffix, _start, _digits))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _prefix = dlg.Prefix;
                _suffix = dlg.Suffix;
                _start = dlg.Start;
                _digits = dlg.Digits;
                chosen = dlg.Chosen();
            }

            var selected = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
            int renamed = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var open = new List<Layout>();
                foreach (var sheet in sheets)
                    open.Add((Layout)tr.GetObject(sheet.Id, OpenMode.ForRead));

                var allNames = new List<string>();
                foreach (var lay in open) allNames.Add(lay.LayoutName.ToUpperInvariant());
                string seed = "%";
                while (allNames.Any(n => n.Contains(seed))) seed += "%";

                int tmp = 0;
                foreach (var lay in open)
                {
                    if (!selected.Contains(lay.LayoutName)) continue;
                    lay.UpgradeOpen();
                    lay.LayoutName = seed + (++tmp).ToString();
                }

                var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lay in open)
                    if (!selected.Contains(lay.LayoutName) && !lay.LayoutName.StartsWith(seed, StringComparison.Ordinal))
                        taken.Add(lay.LayoutName);

                int number = _start;
                foreach (var lay in open)
                {
                    if (!lay.LayoutName.StartsWith(seed, StringComparison.Ordinal)) continue;
                    string name = NextName(ref number, taken);
                    if (name == null)
                    {
                        ed.WriteMessage("\nStopped: ran out of valid layout names.");
                        break;
                    }
                    lay.UpgradeOpen();
                    lay.LayoutName = name;
                    taken.Add(name);
                    renamed++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nRENUMBERLAYOUTS: renamed " + renamed + " layout" + (renamed == 1 ? "" : "s") + ".");
        }

        private static string NextName(ref int number, HashSet<string> taken)
        {
            for (int guard = 0; guard < 100000; guard++)
            {
                string digits = number.ToString();
                if (_digits > digits.Length) digits = digits.PadLeft(_digits, '0');
                number++;
                string name = _prefix + digits + _suffix;
                if (taken.Contains(name)) continue;
                if (!NameOk(name)) continue;
                return name;
            }
            return null;
        }

        private static bool NameOk(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return false;
            if (name.Equals("Model", StringComparison.OrdinalIgnoreCase)) return false;
            const string banned = "\\<>/?\"::;*|,=`";
            foreach (char c in name)
                if (banned.IndexOf(c) >= 0) return false;
            return true;
        }

        private sealed class Sheet
        {
            public ObjectId Id;
            public string Name;
            public int Order;
        }
    }
}
