using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT text tools: FIXTXT, FIXTXTH, TextIncrement, WinLabel,
    /// TXTALIGN, TXTDUP, TXTAUDIT, TXTEXPORT and TXTSTYLE.
    /// </summary>
    public class TextToolCommands
    {
        private static double WinLabelHeight = 0.15;

        // ---- FIXTXT / FIXTXTH : uniform height + push-apart along one axis ----

        [CommandMethod("FIXTXT")]
        public void FixTxt()
        {
            string axis = Util.AskMode("Separate overlapping text", "Vertical", "Horizontal");
            if (axis != null) FixOverlap(vertical: axis == "Vertical");
        }

        [CommandMethod("FIXTXTH")]
        public void FixTxtH() => FixOverlap(vertical: false);

        private void FixOverlap(bool vertical)
        {
            var ed = Util.Ed; var db = Util.Db;
            ed.WriteMessage(vertical ? "\nSelect TEXT objects: " : "\nSelect TEXT objects for horizontal fix: ");
            var psr = ed.GetSelection(new PromptSelectionOptions(), new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT,MTEXT") }));
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
            ed.WriteMessage($"\n{psr.Value.Count} object(s) selected.");

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = psr.Value.GetObjectIds();
                double maxH = 0;
                foreach (var id in ids)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is DBText t) maxH = Math.Max(maxH, t.Height);
                }
                if (maxH == 0) maxH = 2.5;
                double c2cMin = maxH * 1.5;
                ed.WriteMessage(vertical
                    ? $"\nUniform height: {maxH:F4}  |  c2c min: {c2cMin:F4}  |  edge gap: {maxH * 0.5:F4}"
                    : $"\nUniform height: {maxH:F4}  |  min c2c X: {c2cMin:F4}");

                var items = new List<(double key, Point3d ip, ObjectId id)>();
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite);
                    Point3d ip;
                    if (ent is DBText t)
                    {
                        t.Height = maxH;
                        t.HorizontalMode = TextHorizontalMode.TextCenter;
                        t.VerticalMode = TextVerticalMode.TextVerticalMid;
                        ip = t.Position;
                        t.AlignmentPoint = ip;
                        t.Position = ip;
                    }
                    else if (ent is MText m)
                    {
                        m.TextHeight = maxH;
                        ip = m.Location;
                    }
                    else continue;
                    items.Add((vertical ? ip.Y : ip.X, ip, id));
                }

                var sorted = items.OrderBy(x => x.key).ToList();
                double? prev = null;
                foreach (var item in sorted)
                {
                    double cur = item.key;
                    if (prev.HasValue)
                    {
                        double need = prev.Value + c2cMin;
                        if (cur < need)
                        {
                            double delta = need - cur;
                            var ent = tr.GetObject(item.id, OpenMode.ForWrite);
                            if (ent is DBText t)
                            {
                                var np = vertical ? new Point3d(t.Position.X, t.Position.Y + delta, t.Position.Z)
                                                   : new Point3d(t.Position.X + delta, t.Position.Y, t.Position.Z);
                                t.Position = np; t.AlignmentPoint = np;
                            }
                            else if (ent is MText m)
                            {
                                m.Location = vertical ? new Point3d(m.Location.X, m.Location.Y + delta, m.Location.Z)
                                                       : new Point3d(m.Location.X + delta, m.Location.Y, m.Location.Z);
                            }
                            cur = need;
                        }
                    }
                    prev = cur;
                }
                tr.Commit();
                ed.WriteMessage(vertical
                    ? $"\nDone.  height={maxH:F4}  edge-gap={maxH * 0.5:F4}  (= 0.5 x height between text edges)"
                    : $"\nFIXTXTH done.  height={maxH:F4}  h-gap={maxH * 0.5:F4}");
            }
        }

        // ---- WinLabel / WinLabelHeight ----

        [CommandMethod("WinLabel")]
        public void WinLabel()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, "WIN_LABELS", 7);

                ed.WriteMessage("\nSelect Window block(s): ");
                var psr = ed.GetSelection();
                if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo objects selected."); return; }
                var ppr = ed.GetPoint("\nPick text insertion point: ");
                if (ppr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo point selected."); return; }

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                int i = 0;
                foreach (SelectedObject so in psr.Value)
                {
                    if (!(tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br)) { i++; continue; }
                    string paramVal = null;
                    if (br.IsDynamicBlock)
                    {
                        foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                        {
                            if (prop.PropertyName == "WNAME") { paramVal = prop.Value?.ToString(); break; }
                        }
                    }
                    if (paramVal != null)
                    {
                        var txtPt = new Point3d(ppr.Value.X, ppr.Value.Y - i * (WinLabelHeight * 2.5), 0);
                        var t = new DBText
                        {
                            Position = txtPt, Height = WinLabelHeight,
                            TextString = paramVal.Length >= 2 ? paramVal.Substring(0, 2) : paramVal,
                            Layer = "WIN_LABELS"
                        };
                        btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                        ed.WriteMessage("\nLabelled: " + paramVal);
                    }
                    i++;
                }
                tr.Commit();
                ed.WriteMessage("\nDone.");
            }
        }

        [CommandMethod("WinLabelHeight")]
        public void WinLabelHeightCmd()
        {
            var ed = Util.Ed;
            var r = ed.GetDouble(new PromptDoubleOptions($"\nEnter text height <{WinLabelHeight:F3}>: ") { AllowNegative = false, AllowZero = false });
            if (r.Status == PromptStatus.OK) { WinLabelHeight = r.Value; ed.WriteMessage($"\nText height set to: {WinLabelHeight:F3}"); }
            else ed.WriteMessage("\nHeight unchanged.");
        }

        // ---- TXTALIGN ----

        [CommandMethod("TXTALIGN")]
        public void TxtAlign()
        {
            var ed = Util.Ed; var db = Util.Db;
            ed.WriteMessage("\nSelect TEXT objects to align: ");
            var psr = ed.GetSelection(new PromptSelectionOptions(), new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT") }));
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

            var pko = new PromptKeywordOptions("\nAlign to [Left/Right/CentreX/Top/Middle/Bottom]: ");
            foreach (var k in new[] { "Left", "Right", "CentreX", "Top", "Middle", "Bottom" }) pko.Keywords.Add(k);
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;
            string axis = pkr.StringResult;

            var ppr = ed.GetPoint("\nPick reference point: ");
            if (ppr.Status != PromptStatus.OK) { ed.WriteMessage("\nCancelled."); return; }
            Point3d refPt = ppr.Value;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                int n = 0;
                foreach (SelectedObject so in psr.Value)
                {
                    if (!(tr.GetObject(so.ObjectId, OpenMode.ForWrite) is DBText t)) continue;
                    Point3d ip = t.Position;
                    Point3d newIp = axis switch
                    {
                        "Left" or "Right" or "CentreX" => new Point3d(refPt.X, ip.Y, ip.Z),
                        _ => new Point3d(ip.X, refPt.Y, ip.Z)
                    };
                    t.Position = newIp;
                    t.AlignmentPoint = newIp;
                    n++;
                }
                tr.Commit();
                ed.WriteMessage($"\nTXTALIGN: {n} object(s) aligned to {axis}.");
            }
        }

        // ---- TXTDUP ----

        [CommandMethod("TXTDUP")]
        public void TxtDup()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var psr = ed.SelectAll(new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT") }));
                if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo TEXT objects found."); return; }

                var seen = new Dictionary<string, ObjectId>();
                var dupList = new List<ObjectId>();
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var t = (DBText)tr.GetObject(id, OpenMode.ForRead);
                    string key = t.TextString + "|" + t.Position.X.ToString("F3") + "," + t.Position.Y.ToString("F3");
                    if (seen.ContainsKey(key)) dupList.Add(id); else seen[key] = id;
                }

                if (dupList.Count == 0) { ed.WriteMessage("\nTXTDUP: No duplicate text found. PASS."); tr.Commit(); return; }
                ed.WriteMessage($"\nTXTDUP: {dupList.Count} duplicate(s) found.");
                var pko = new PromptKeywordOptions("\nDelete duplicates? [Yes/No]: ");
                pko.Keywords.Add("Yes"); pko.Keywords.Add("No"); pko.Keywords.Default = "No";
                var pkr = ed.GetKeywords(pko);
                if (pkr.Status == PromptStatus.OK && pkr.StringResult == "Yes")
                {
                    int n = 0;
                    foreach (var id in dupList) { tr.GetObject(id, OpenMode.ForWrite).Erase(); n++; }
                    ed.WriteMessage($"\n  {n} duplicate(s) deleted.");
                }
                else ed.WriteMessage("\n  Duplicates kept - no changes made.");
                tr.Commit();
            }
        }

        // ---- TXTAUDIT ----

        [CommandMethod("TXTAUDIT")]
        public void TxtAudit()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var psr = ed.SelectAll(new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT,MTEXT") }));
                if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo TEXT/MTEXT found."); return; }

                var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int count = 0;
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    if (ent.Color.ColorMethod != Autodesk.AutoCAD.Colors.ColorMethod.ByLayer)
                    {
                        overrides[ent.Layer] = overrides.TryGetValue(ent.Layer, out var c) ? c + 1 : 1;
                        count++;
                    }
                }
                if (overrides.Count == 0) ed.WriteMessage("\nTXTAUDIT: No explicit text overrides found. PASS.");
                else
                {
                    ed.WriteMessage($"\nTXTAUDIT: {count} text object(s) with explicit colour:");
                    foreach (var kv in overrides.OrderBy(k => k.Key, StringComparer.Ordinal))
                        ed.WriteMessage($"\n  {Util.Pad(kv.Key, 28)}{kv.Value} object(s)");
                }
                tr.Commit();
            }
        }

        // ---- TXTEXPORT ----

        [CommandMethod("TXTEXPORT")]
        public void TxtExport()
        {
            var ed = Util.Ed; var db = Util.Db;
            string dwgPath = db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string csvPath = dir + "TextExport.csv";

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var psr = ed.SelectAll(new SelectionFilter(new[] { new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, "TEXT,MTEXT") }));
                if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNo TEXT/MTEXT found."); return; }

                var rows = new List<IEnumerable<string>>();
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    string type = ent.GetType().Name.ToUpperInvariant();
                    string txt = ""; Point3d ip = Point3d.Origin; double ht = 0; string sty = "";
                    if (ent is DBText t) { txt = t.TextString; ip = t.Position; ht = t.Height; sty = SafeStyleName(tr, t.TextStyleId); }
                    else if (ent is MText m) { txt = m.Contents; ip = m.Location; ht = m.TextHeight; sty = SafeStyleName(tr, m.TextStyleId); }
                    txt = (txt ?? "").Replace(",", " ").Replace("\n", " ");
                    rows.Add(new[] { ent.Layer, type, ip.X.ToString("F3"), ip.Y.ToString("F3"), ht.ToString("F4"), sty, txt });
                }
                Util.WriteCsv(csvPath, new[] { "Layer", "Type", "X", "Y", "Height", "Style", "Content" }, rows);
                ed.WriteMessage($"\nTXTEXPORT: {rows.Count} object(s) exported to {csvPath}");
                tr.Commit();
            }
        }

        private static string SafeStyleName(Transaction tr, ObjectId styleId)
        {
            if (styleId.IsNull) return "";
            var st = tr.GetObject(styleId, OpenMode.ForRead) as TextStyleTableRecord;
            return st?.Name ?? "";
        }

        // ---- TXTSTYLE ----

        [CommandMethod("TXTSTYLE")]
        public void TxtStyle()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                var names = new List<string>();
                foreach (ObjectId id in st) names.Add(((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
                names.Sort(StringComparer.Ordinal);

                ed.WriteMessage("\nText styles in drawing:");
                for (int i = 0; i < names.Count; i++) ed.WriteMessage($"\n  {Util.Pad(i.ToString(), 3)}  {names[i]}");

                var r = ed.GetString("\nEnter style name or number to set current: ");
                if (r.Status != PromptStatus.OK) return;
                string choice = r.StringResult;

                if (choice.Length > 0 && char.IsDigit(choice[0]))
                {
                    if (int.TryParse(choice, out int idx) && idx >= 0 && idx < names.Count) choice = names[idx];
                    else { ed.WriteMessage("\n[Error] Index out of range."); return; }
                }

                if (st.Has(choice)) { db.Textstyle = st[choice]; ed.WriteMessage($"\nTXTSTYLE: Current style set to '{choice}'."); }
                else ed.WriteMessage($"\n[Error] Style '{choice}' not found.");
                tr.Commit();
            }
        }
    }
}
