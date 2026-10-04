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
    /// hcwCAD-KIT building-permission layers (BBMP).
    /// </summary>
    public class BpltCommands
    {
        /// <summary>Creates the HCW standard layers plus the BP- permit layers in one pass.</summary>
        internal static void CreateAllLayers(Transaction tr, Database db)
        {
            foreach (var ld in LayerData.Hcw)
                Util.EnsureLayer(tr, db, ld.Name, (short)ld.Aci, ld.Linetype, Util.MmToLineWeight(ld.LwMm));
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

                    // Building permit submission environment. Set via SETVAR-equivalent
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
                                    "(units=Meters, " + (LayerData.Hcw.Length + LayerData.Bplt.Length) + " layers ready).");
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
                ed.WriteMessage($"\nBPLTLAYERS: {LayerData.Hcw.Length} HCW layers and {LayerData.Bplt.Length} BP- drafting layers created/verified.");
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
