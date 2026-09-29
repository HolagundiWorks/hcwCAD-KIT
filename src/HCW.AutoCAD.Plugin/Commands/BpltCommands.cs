using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// BPLT - Building Permission Layer Tool (BBMP/AutoPlan submission layers).
    /// Ported from HCW-ALL.lsp Section 6 (bplt:* helpers, c:BPLT* commands).
    /// </summary>
    public class BpltCommands
    {
        private static void CreateAllLayers(Transaction tr, Database db)
        {
            foreach (var ld in LayerData.Bplt)
            {
                Util.EnsureLayer(tr, db, ld.Name, (short)ld.Aci, ld.Linetype,
                    Util.MmToLineWeight(ld.LwHundredthsMm / 100.0));
            }
        }

        [CommandMethod("BPLTSTART")]
        public void BpltStart()
        {
            var doc = Util.Doc; var db = Util.Db; var ed = Util.Ed;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    CreateAllLayers(tr, db);

                    // AutoPlan mandatory submission environment. Set via SETVAR-equivalent
                    // calls (rather than Database properties, which don't cover every DIMVAR)
                    // so this works identically across AutoCAD 2021-2024.
                    db.Insunits = UnitsValue.Meters;
                    AcAp.SetSystemVariable("LUNITS", 2);   // Decimal
                    AcAp.SetSystemVariable("LUPREC", 3);
                    AcAp.SetSystemVariable("DIMBLK", "_CLOSEDBLANK");
                    AcAp.SetSystemVariable("DIMTXT", 0.18);
                    AcAp.SetSystemVariable("DIMASZ", 0.18);

                    tr.Commit();
                    ed.WriteMessage("\nBPLTSTART: drawing set up for Building Permission submission " +
                                    "(units=Meters, " + LayerData.Bplt.Length + " layers ready).");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\n[BPLTSTART error] " + ex.Message);
                }
            }
        }

        [CommandMethod("BPLTLAYERS")]
        public void BpltLayers()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                CreateAllLayers(tr, db);
                tr.Commit();
                int ap = LayerData.Bplt.Count(l => l.Name.StartsWith("AP-"));
                int bp = LayerData.Bplt.Length - ap;
                ed.WriteMessage($"\nBPLTLAYERS: {bp} BP- drafting layers and {ap} AP- AutoPlan " +
                                 "marking layers (non-plotting) created/verified.");
            }
        }

        [CommandMethod("BPLTCOPY")]
        public void BpltCopy()
        {
            // Duplicate every BP-* entity onto its matching AP-* marking layer (same geometry,
            // AutoPlan-required helper layer), skipping entities whose BP- layer has no AP- match.
            var db = Util.Db; var ed = Util.Ed;
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect BP- entities to duplicate onto their AP- layer: " });
            if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                int copied = 0, skipped = 0;

                foreach (SelectedObject so in psr.Value)
                {
                    var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    string layName = ent.Layer;
                    if (!layName.StartsWith("BP-", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                    string apName = "AP-" + layName.Substring(3);
                    if (!lt.Has(apName)) { skipped++; continue; }

                    var clone = (Entity)ent.Clone();
                    clone.Layer = apName;
                    btr.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                    copied++;
                }
                tr.Commit();
                ed.WriteMessage($"\nBPLTCOPY: {copied} object(s) duplicated onto AP- layers, {skipped} skipped (no matching AP- layer).");
            }
        }

        [CommandMethod("BPLTCHECK")]
        public void BpltCheck()
        {
            // Every AP- layer that AutoPlan requires must contain at least one CLOSED polyline.
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var required = LayerData.Bplt.Where(l => l.Name.StartsWith("AP-")).Select(l => l.Name);
                int ok = 0, missing = 0, notClosed = 0;

                foreach (var name in required)
                {
                    if (!lt.Has(name)) { missing++; ed.WriteMessage($"\n  MISSING LAYER: {name}"); continue; }
                    bool foundClosed = false, foundAny = false;
                    foreach (ObjectId id in btr)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || !string.Equals(ent.Layer, name, StringComparison.OrdinalIgnoreCase)) continue;
                        foundAny = true;
                        if (ent is Polyline pl && pl.Closed) { foundClosed = true; break; }
                    }
                    if (foundClosed) ok++;
                    else if (foundAny) { notClosed++; ed.WriteMessage($"\n  NOT CLOSED: {name}"); }
                    else { notClosed++; ed.WriteMessage($"\n  EMPTY: {name}"); }
                }
                ed.WriteMessage($"\nBPLTCHECK: {ok} OK, {notClosed} empty/not-closed, {missing} layer(s) missing entirely.");
                tr.Commit();
            }
        }

        [CommandMethod("BPLTAREA")]
        public void BpltArea()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var totals = new System.Collections.Generic.Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!ent.Layer.StartsWith("AP-", StringComparison.OrdinalIgnoreCase)) continue;
                    if (ent is Polyline pl && pl.Closed)
                    {
                        double a = Math.Abs(pl.Area);
                        totals[ent.Layer] = totals.TryGetValue(ent.Layer, out var t) ? t + a : a;
                    }
                }
                ed.WriteMessage("\nBPLTAREA - AutoPlan area summary (sq.m):");
                foreach (var kv in totals.OrderBy(k => k.Key))
                    ed.WriteMessage($"\n  {Util.Pad(kv.Key, 22)} {kv.Value:F2}");
                tr.Commit();
            }
        }

        [CommandMethod("BPLTREPORT")]
        public void BpltReport()
        {
            var db = Util.Db; var ed = Util.Ed;
            string dwgPath = db.Filename;
            string csvPath = (string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar)
                              + "BPLT_Report.csv";
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var rows = LayerData.Bplt.Select(l => (System.Collections.Generic.IEnumerable<string>)new[]
                {
                    l.Name, l.Aci.ToString(), l.Linetype, (l.LwHundredthsMm / 100.0).ToString("F2"),
                    lt.Has(l.Name) ? "present" : "MISSING"
                });
                Util.WriteCsv(csvPath, new[] { "Layer", "ACI Colour", "Linetype", "Lineweight (mm)", "Status" }, rows);
                tr.Commit();
                ed.WriteMessage("\nBPLTREPORT: saved " + csvPath);
            }
        }

        [CommandMethod("BPLTTITLEBLOCK")]
        public void BpltTitleBlock()
        {
            var db = Util.Db; var ed = Util.Ed;
            var pko = new PromptKeywordOptions("\nSheet size [A1/A2/A3/A4]: ");
            pko.Keywords.Add("A1"); pko.Keywords.Add("A2"); pko.Keywords.Add("A3"); pko.Keywords.Add("A4");
            pko.Keywords.Default = "A2";
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;

            (double w, double h) = pkr.StringResult switch
            {
                "A1" => (0.841, 0.594),
                "A3" => (0.420, 0.297),
                "A4" => (0.297, 0.210),
                _ => (0.594, 0.420) // A2
            };

            var ppr = ed.GetPoint("\nInsertion point for title block (bottom-left): ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d bl = ppr.Value;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, "BP-SHEET-BORDER", 8, "Continuous", LineWeight.LineWeight050);
                Util.EnsureLayer(tr, db, "BP-TITLE-BLOCK", 8, "Continuous", LineWeight.LineWeight025);
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var border = new Polyline();
                border.AddVertexAt(0, new Point2d(bl.X, bl.Y), 0, 0, 0);
                border.AddVertexAt(1, new Point2d(bl.X + w, bl.Y), 0, 0, 0);
                border.AddVertexAt(2, new Point2d(bl.X + w, bl.Y + h), 0, 0, 0);
                border.AddVertexAt(3, new Point2d(bl.X, bl.Y + h), 0, 0, 0);
                border.Closed = true;
                border.Layer = "BP-SHEET-BORDER";
                btr.AppendEntity(border); tr.AddNewlyCreatedDBObject(border, true);

                double tbH = h * 0.12;
                var tbBorder = new Polyline();
                tbBorder.AddVertexAt(0, new Point2d(bl.X, bl.Y), 0, 0, 0);
                tbBorder.AddVertexAt(1, new Point2d(bl.X + w, bl.Y), 0, 0, 0);
                tbBorder.AddVertexAt(2, new Point2d(bl.X + w, bl.Y + tbH), 0, 0, 0);
                tbBorder.AddVertexAt(3, new Point2d(bl.X, bl.Y + tbH), 0, 0, 0);
                tbBorder.Closed = true;
                tbBorder.Layer = "BP-TITLE-BLOCK";
                btr.AppendEntity(tbBorder); tr.AddNewlyCreatedDBObject(tbBorder, true);

                var txt = new DBText
                {
                    Position = new Point3d(bl.X + 0.02, bl.Y + tbH / 2.0, 0),
                    Height = tbH * 0.4,
                    TextString = "PROJECT TITLE  |  SHEET: " + pkr.StringResult + "  |  BUILDING PERMISSION SUBMISSION",
                    Layer = "BP-TITLE-BLOCK"
                };
                btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);

                tr.Commit();
                ed.WriteMessage($"\nBPLTTITLEBLOCK: {pkr.StringResult} sheet border + title block placed.");
            }
        }
    }
}
