using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// The Project tab: HCWPROJECT (details for the title block, floors and heights, beam depths in one dialog; HCWFLOORS and HCWBEAMS open it on
    /// that page), HCWPROJECTTITLE (fill the title block from the saved details) and HCWOPENSTD (the standard doors, windows and ventilator in the schedule).
    /// The floors are the levels of the take-off book (the Floors tab of MSCHED), so every tool that reads levels sees what is entered here.
    /// </summary>
    public class ProjectCommands
    {
        private const string Dictionary = "HCW_PROJECT";
        private const string Record = "DATA";

        internal static ProjectData Load(Transaction tr, Database db) => ProjectData.FromLines(DrawingStore.Read(tr, db, Dictionary, Record));

        /// <summary>Saves the project data in the drawing (used by the bridge import as well as the Project dialog).</summary>
        internal static void Save(Transaction tr, Database db, ProjectData data) => DrawingStore.Write(tr, db, Dictionary, Record, data.ToLines());

        [CommandMethod("HCWPROJECT")]
        public void ProjectDetails() { Edit(UI.ProjectForm.PageDetails); }

        [CommandMethod("HCWFLOORS")]
        public void Floors() { Edit(UI.ProjectForm.PageFloors); }

        [CommandMethod("HCWBEAMS")]
        public void BeamDepths() { Edit(UI.ProjectForm.PageBeams); }

        private static void Edit(int page)
        {
            var ed = Util.Ed; var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            double unit = LevelStore.UnitMm;
            ProjectData data; List<LevelRow> floors;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                data = Load(tr, db);
                floors = LevelStore.Load(tr, db);
                tr.Commit();
            }
            var fresh = new LevelRow
            {
                FflMm = Settings.GetDouble("DefaultFflHeight", 3.15) * unit, CeilingMm = Settings.GetDouble("DefaultCeilingHeight", 3.0) * unit,
                LintelMm = Settings.GetDouble("DefaultLintelBottom", 2.1) * unit, SlabMm = Settings.GetDouble("DefaultSlabThickness", 0.15) * unit,
            };

            using (var dlg = new UI.ProjectForm(data, floors, fresh, page))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                int filled = 0;
                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawingStore.Write(tr, db, Dictionary, Record, dlg.Data.ToLines());
                    var book = MeasureBook.Load(tr, db);
                    book.Floors.Clear();
                    foreach (var f in dlg.Floors)
                        book.Floors.Add(new MeasureBook.FloorSpec { Name = f.Name, Height = f.CeilingMm / unit, FflHeight = f.FflMm / unit, LintelBottom = f.LintelMm / unit, Slab = f.SlabMm / unit });
                    book.Save(tr, db);
                    if (dlg.FillTitleBlock) filled = FillTitleBlocks(tr, db, dlg.Data);
                    tr.Commit();
                }
                ed.WriteMessage("\nProject data saved: " + dlg.Floors.Count + " floor(s)"
                    + (dlg.Data.BeamDepthsMm.Count > 0 ? ", beam depths " + ProjectData.FormatDepths(dlg.Data.BeamDepthsMm) + " mm" : "")
                    + (dlg.FillTitleBlock ? ", " + filled + " title block(s) filled" : "") + ". HCWLEVELS lists the floors.");
            }
        }

        [CommandMethod("HCWPROJECTTITLE")]
        public void FillTitle()
        {
            var ed = Util.Ed; var db = Util.Db;
            int filled;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var data = Load(tr, db);
                if (data.TitleValues().Count == 0) { ed.WriteMessage("\nHCWPROJECTTITLE: no project details are saved yet. Enter them with HCWPROJECT."); return; }
                filled = FillTitleBlocks(tr, db, data);
                tr.Commit();
            }
            ed.WriteMessage(filled == 0
                ? "\nHCWPROJECTTITLE: no title block found in this drawing (TITLEBLOCK places one)."
                : "\nHCWPROJECTTITLE: " + filled + " title block(s) filled from the project details.");
        }

        /// <summary>Fills every title block, in model space and on every layout, with the details that have a title block field. Returns how many were filled.</summary>
        private static int FillTitleBlocks(Transaction tr, Database db, ProjectData data)
        {
            var values = data.TitleValues();
            if (values.Count == 0) return 0;
            int n = 0;
            var layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                var layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.ObjectClass.DxfName != "INSERT") continue;
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null || !TitleBlockCommands.IsKind(br, "TITLE")) continue;
                    TitleBlockCommands.WriteFields(tr, br, values);
                    n++;
                }
            }
            return n;
        }

        // ---- standard openings in the schedule ----

        [CommandMethod("HCWOPENSTD")]
        public void StandardOpenings()
        {
            var ed = Util.Ed; var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            bool imperial = MeasureCommands.MeasureState.Units == MeasureCommands.UnitSys.Imperial;
            Func<double, double> toBook = mm => imperial ? mm / 25.4 : mm / 1000.0;
            var first = LevelStore.First();
            double doorH = first != null && first.LintelMm > 0 ? first.LintelMm : Settings.GetDouble("DoorHeightMm", 2100);
            double winH = Settings.GetDouble("WindowHeightMm", 1200), winSill = Settings.GetDouble("WindowSillMm", 900);
            double ventH = Settings.GetDouble("VentilatorHeightMm", 450), ventSill = Settings.GetDouble("VentilatorSillMm", 1800);

            var wanted = new List<MeasureBook.OpeningSpec>();
            foreach (var s in OpeningStandards.Doors)
                wanted.Add(new MeasureBook.OpeningSpec { Mark = s.Code, Kind = "Door", Width = toBook(s.WidthMm), Height = toBook(doorH), Sill = 0, Count = 0 });
            foreach (var s in OpeningStandards.Windows)
                wanted.Add(new MeasureBook.OpeningSpec { Mark = s.Code, Kind = "Window", Width = toBook(s.WidthMm), Height = toBook(winH), Sill = toBook(winSill), Count = 0 });
            var v = OpeningStandards.Ventilator;
            wanted.Add(new MeasureBook.OpeningSpec { Mark = v.Code, Kind = "Window", Width = toBook(v.WidthMm), Height = toBook(ventH), Sill = toBook(ventSill), Count = 0 });
            foreach (var w in wanted) w.LintelBottom = w.Sill + w.Height;

            int added = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var book = MeasureBook.Load(tr, db);
                foreach (var w in wanted)
                {
                    bool have = book.Openings.Any(o => string.Equals(o.Mark, w.Mark, StringComparison.OrdinalIgnoreCase)
                        || (string.Equals(o.Kind, w.Kind, StringComparison.OrdinalIgnoreCase) && Math.Abs(o.Width - w.Width) < 1e-3 && Math.Abs(o.Height - w.Height) < 1e-3 && Math.Abs(o.Sill - w.Sill) < 1e-3));
                    if (have) continue;
                    book.Openings.Add(w);
                    added++;
                }
                book.Save(tr, db);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWOPENSTD: " + added + " standard opening(s) added to the schedule (" + (wanted.Count - added) + " already there). "
                + "Doors " + string.Join(", ", OpeningStandards.Doors.Select(s => s.Code + " " + s.WidthMm))
                + "; windows " + string.Join(", ", OpeningStandards.Windows.Select(s => s.Code + " " + s.WidthMm)) + "; ventilator " + v.Code + " " + v.WidthMm + " mm. "
                + "HCWOPENSCHED draws the table; MSCHED edits the entries.");
        }
    }
}
