using System;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// DELETEAREATEXT - bulk-deletes TEXT/MTEXT objects whose content looks
    /// like "Area: <number>[ sq unit / m2 / m² / ^2]" (as produced by MEASURE,
    /// the Room Dimension Tool, or POLYAREA). Ported from HCW-ALL.lsp Section 11.
    /// </summary>
    public class DeleteAreaTextCommands
    {
        private static readonly Regex AreaPattern = new Regex(@"^AREA:\s*\S", RegexOptions.IgnoreCase);

        [CommandMethod("DELETEAREATEXT")]
        public void DeleteAreaText()
        {
            var ed = Util.Ed; var db = Util.Db;
            var pko = new PromptKeywordOptions("\nSearch [All/Selection] <All>: ");
            pko.Keywords.Add("All"); pko.Keywords.Add("Selection"); pko.Keywords.Default = "All";
            var pkr = ed.GetKeywords(pko);
            string choice = pkr.Status == PromptStatus.OK ? pkr.StringResult : "All";

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId[] candidates;
                var filter = new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT,MTEXT") });

                if (choice == "Selection")
                {
                    ed.WriteMessage("\nSelect region to search: ");
                    var psr = ed.GetSelection(new PromptSelectionOptions(), filter);
                    candidates = psr.Status == PromptStatus.OK ? psr.Value.GetObjectIds() : Array.Empty<ObjectId>();
                }
                else
                {
                    var psr = ed.SelectAll(filter);
                    candidates = psr.Status == PromptStatus.OK ? psr.Value.GetObjectIds() : Array.Empty<ObjectId>();
                }

                if (candidates.Length == 0) { ed.WriteMessage("\nNo text found."); tr.Commit(); return; }
                ed.WriteMessage($"\nScanning {candidates.Length} text object(s)...");

                int count = 0;
                foreach (var id in candidates)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    string txt = GetTextString(ent);
                    if (IsAreaText(txt))
                    {
                        ed.WriteMessage($"\n  Deleting: \"{txt}\"");
                        ent.UpgradeOpen();
                        ent.Erase();
                        count++;
                    }
                }
                ed.WriteMessage($"\nDone. {count} Area text(s) deleted.");
                tr.Commit();
            }
        }

        private static string GetTextString(DBObject ent)
        {
            if (ent is DBText t) return t.TextString;
            if (ent is MText m)
            {
                string raw = m.Contents ?? m.Text;
                raw = raw.Replace("\\P", "").Replace("\\p", "");
                raw = new string(raw.Where(c => c != '{' && c != '}').ToArray());
                return raw;
            }
            return "";
        }

        /// <summary>Does string match "Area: &lt;digits/decimals&gt;[unit]" (m2, m², sq m, ^2, or bare)?</summary>
        private static bool IsAreaText(string s) => !string.IsNullOrEmpty(s) && AreaPattern.IsMatch(s.Trim());
    }
}
