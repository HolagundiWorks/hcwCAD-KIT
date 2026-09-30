using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT building-permission layers (BBMP/AutoPlan).
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

    }
}
