using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWAREASTMT builds the building permit area statement: you select the site boundary and, for each floor, its
    /// built-up outlines and the areas left out of the floor area ratio (shafts, ducts, lift). It works out net
    /// area, total floor area ratio and ground cover, prints them, and fills the area fields of every hcwCAD-KIT
    /// title block in the drawing (site area, floor rows, totals, FAR and ground cover). An area table can also be drawn.
    /// Areas are in square metres whatever the drawing units. Run it from the Model tab.
    /// </summary>
    public class AreaStatementCommands
    {
        private static readonly string[] DefaultNames = { "GROUND", "FIRST", "SECOND", "THIRD", "FOURTH", "FIFTH", "SIXTH", "SEVENTH", "EIGHTH", "NINTH", "TENTH", "ELEVENTH" };
        private static int _floors = 2;
        private const double TableTextMm = 250;

        [CommandMethod("HCWAREASTMT")]
        public void AreaStatementCommand()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var lm = LayoutManager.Current;
            string original = lm.CurrentLayout;
            bool wasModel = string.Equals(original, "Model", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (!wasModel) lm.CurrentLayout = "Model";
                Run(ed, db);
            }
            finally
            {
                if (!wasModel) { try { lm.CurrentLayout = original; } catch (System.Exception) { /* layout gone */ } }
            }
        }

        private static void Run(Editor ed, Database db)
        {
            double upm = Util.MmToDrawingUnits(1000.0);          // drawing units per metre
            double toSqm = 1.0 / (upm * upm);

            // Site.
            double site = 0;
            var s = SumAreas(ed, "\nSelect the site boundary (closed polyline; Enter to skip): ", toSqm);
            if (s == null) return;
            site = s.Sqm;

            // Floors.
            var count = ed.GetInteger(new PromptIntegerOptions("\nNumber of floors <" + _floors + ">: ")
                { AllowNegative = false, AllowZero = false, LowerLimit = 1, UpperLimit = 12, DefaultValue = _floors, UseDefaultValue = true });
            if (count.Status != PromptStatus.OK) return;
            _floors = count.Value;

            var floors = new List<FloorInput>();
            for (int i = 0; i < _floors; i++)
            {
                string def = DefaultNames[i];
                var nr = ed.GetString(new PromptStringOptions("\nName of floor " + (i + 1) + " <" + def + ">: ")
                    { AllowSpaces = true, DefaultValue = def, UseDefaultValue = true });
                if (nr.Status != PromptStatus.OK) return;
                string name = nr.StringResult.Trim().ToUpperInvariant();
                if (name.Length == 0) name = def;

                var gross = SumAreas(ed, "\nSelect the built-up outline(s) of " + name + " (Enter if none): ", toSqm);
                if (gross == null) return;
                var ded = SumAreas(ed, "\nSelect the areas of " + name + " left out of the FAR - shafts, ducts, lift (Enter if none): ", toSqm);
                if (ded == null) return;
                floors.Add(new FloorInput { Name = name, Gross = gross.Sqm, Deduction = ded.Sqm });
            }

            var stmt = AreaStatement.Compute(floors, site, Settings.GetDouble("AreaFarPermittedPercent", 0), Settings.GetDouble("AreaGroundCoverPermittedPercent", 0));
            Report(ed, stmt);

            // Title blocks anywhere in the drawing.
            var blocks = FindTitleBlocks(db);
            if (blocks.Count > 0)
            {
                var ko = new PromptKeywordOptions("\nFill the area fields of " + blocks.Count + " title block(s) [Yes/No] <Yes>: ", "Yes No") { AllowNone = true };
                ko.Keywords.Default = "Yes";
                var kr = ed.GetKeywords(ko);
                if (kr.Status == PromptStatus.Cancel) return;
                if (kr.Status != PromptStatus.OK || kr.StringResult == "Yes")
                {
                    var fields = stmt.ToFields(4);
                    using (Util.Doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in blocks)
                            TitleBlockCommands.WriteFields(tr, (BlockReference)tr.GetObject(id, OpenMode.ForRead), fields);
                        tr.Commit();
                    }
                    ed.WriteMessage("\nHCWAREASTMT: title block fields filled." + (floors.Count > 4 ? " The title block has 4 floor rows; floors 5 and up are in the totals only." : ""));
                }
            }
            else ed.WriteMessage("\nHCWAREASTMT: no hcwCAD-KIT title block found (SHEETFIT or TITLEBLOCK places one); the numbers are above.");

            // Optional table.
            var pr = ed.GetPoint(new PromptPointOptions("\nTop-left of an area table (Enter to skip): ") { AllowNone = true });
            if (pr.Status != PromptStatus.OK) return;
            var at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            double h = Util.MmToDrawingUnits(TableTextMm);
            var rows = new List<string[]>();
            foreach (var f in stmt.Floors)
                rows.Add(new[] { f.Name, AreaStatement.Fmt(f.Gross), AreaStatement.Fmt(f.Deduction), AreaStatement.Fmt(f.Net) });
            rows.Add(new[] { "TOTAL", AreaStatement.Fmt(stmt.TotalGross), AreaStatement.Fmt(stmt.TotalDeduction), AreaStatement.Fmt(stmt.TotalNet) });
            if (stmt.Site > 0)
            {
                rows.Add(new[] { "SITE AREA", AreaStatement.Fmt(stmt.Site), "", "" });
                rows.Add(new[] { "F.A.R. %", "", "", AreaStatement.Fmt(stmt.FarPercent.Value) });
                rows.Add(new[] { "GROUND COVER %", AreaStatement.Fmt(stmt.GroundCover), "", AreaStatement.Fmt(stmt.GroundCoverPercent.Value) });
            }
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureHcwLayer(tr, db, "AN-TEXT");
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                MeasureCommands.DrawTable(tr, db, new Point3d(at.X, at.Y - 2.5 * h, 0),
                    new[] { "Floor", "Gross (sq m)", "Deduction", "Net" }, rows, h, "AN-TEXT");
                var title = new DBText { Height = h * 1.2, TextString = "AREA STATEMENT", Layer = "AN-TEXT", Position = new Point3d(at.X, at.Y - 1.2 * h, 0) };
                space.AppendEntity(title);
                tr.AddNewlyCreatedDBObject(title, true);
                tr.Commit();
            }
        }

        private class Sum { public double Sqm; public int Used, Skipped; }

        /// <summary>Total area of the closed polylines selected, in square metres. Null when cancelled; zero when Enter is pressed.</summary>
        private static Sum SumAreas(Editor ed, string prompt, double toSqm)
        {
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = prompt },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") }));
            if (psr.Status == PromptStatus.None) return new Sum();
            if (psr.Status != PromptStatus.OK) return null;

            var sum = new Sum();
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                    if (!pl.Closed) { sum.Skipped++; continue; }
                    sum.Sqm += Math.Abs(pl.Area) * toSqm;
                    sum.Used++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\n  " + sum.Used + " outline(s), " + AreaStatement.Fmt(sum.Sqm) + " sq m"
                + (sum.Skipped > 0 ? " (" + sum.Skipped + " open polyline(s) skipped)" : "") + ".");
            return sum;
        }

        private static void Report(Editor ed, AreaStatement s)
        {
            ed.WriteMessage("\n\nAREA STATEMENT (sq m)");
            ed.WriteMessage("\n  " + Util.Pad("Floor", 14) + Util.Pad("Gross", 12) + Util.Pad("Deduction", 12) + "Net");
            foreach (var f in s.Floors)
                ed.WriteMessage("\n  " + Util.Pad(f.Name, 14) + Util.Pad(AreaStatement.Fmt(f.Gross), 12) + Util.Pad(AreaStatement.Fmt(f.Deduction), 12) + AreaStatement.Fmt(f.Net));
            ed.WriteMessage("\n  " + Util.Pad("TOTAL", 14) + Util.Pad(AreaStatement.Fmt(s.TotalGross), 12) + Util.Pad(AreaStatement.Fmt(s.TotalDeduction), 12) + AreaStatement.Fmt(s.TotalNet));
            if (s.Site > 0)
            {
                ed.WriteMessage("\n  Site area " + AreaStatement.Fmt(s.Site)
                    + "   F.A.R. " + AreaStatement.Fmt(s.FarPercent.Value) + " %"
                    + "   Ground cover " + AreaStatement.Fmt(s.GroundCover) + " (" + AreaStatement.Fmt(s.GroundCoverPercent.Value) + " %)");
                if (s.FarPermittedPercent > 0 || s.GroundCoverPermittedPercent > 0)
                    ed.WriteMessage("\n  Permissible: F.A.R. " + (s.FarPermittedPercent > 0 ? AreaStatement.Fmt(s.FarPermittedPercent) + " %" : "not set")
                        + "   ground cover " + (s.GroundCoverPermitted.HasValue ? AreaStatement.Fmt(s.GroundCoverPermitted.Value) + " sq m (" + AreaStatement.Fmt(s.GroundCoverPermittedPercent) + " %)" : "not set"));
            }
            foreach (var w in s.Warnings) ed.WriteMessage("\n  WARNING: " + w);
        }

        /// <summary>Every hcwCAD-KIT title block in model space and in every layout.</summary>
        private static List<ObjectId> FindTitleBlocks(Database db)
        {
            var found = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in layouts)
                {
                    var layout = (Layout)tr.GetObject(e.Value, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                        if (br != null && TitleBlockCommands.IsKind(br, "TITLE")) found.Add(id);
                    }
                }
                tr.Commit();
            }
            return found;
        }
    }
}
