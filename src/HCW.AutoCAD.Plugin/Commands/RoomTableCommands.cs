using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWROOMREPORT reads the rooms in a selection and makes a table of them: each closed outline with the name written inside it, its length,
    /// width, area, perimeter and the dimensions that lie in it (the ones AUTODIMROOM draws, or any other). The table can be drawn in the drawing,
    /// saved as the take-off "Rooms" (so MEXPORT and MEXPORTX include it) and written to a CSV or Excel file.
    /// Select outlines, names, area labels and dimensions together, or press Enter to take everything in this space (then only the outlines on the
    /// RoomTableLayers layers count as rooms). Areas are in square metres whatever the drawing units.
    /// </summary>
    public class RoomTableCommands
    {
        private const string KindTable = "ROOMTABLE", TakeoffName = "Rooms";
        private static string _order = "Position";
        private static string _export = "None";

        [CommandMethod("HCWROOMREPORT")]
        public void RoomReportCommand()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            // The older label-based room tools are the other sources of the same report.
            string source = Util.AskMode("Read the rooms from", "Outlines", "Labels");
            if (source == null) return;
            if (source == "Labels")
            {
                string result = Util.AskMode("From the room labels", "Table", "Csv", "Total");
                if (result == "Table") UI.RoomUnitSelector.Current.Table(ed, db);
                else if (result == "Csv") UI.RoomUnitSelector.Current.Schedule(ed, db);
                else if (result == "Total") UI.RoomUnitSelector.Current.Total(ed);
                return;
            }

            var psr = ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = "\nSelect the rooms: outlines, names, area labels and dimensions (Enter = everything in this space): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE,TEXT,MTEXT,DIMENSION") }));
            if (psr.Status != PromptStatus.OK && !Util.NoSelection(psr.Status)) return;
            bool all = Util.NoSelection(psr.Status);

            var oo = new PromptKeywordOptions("\nOrder the rooms by [Position/Name] <" + _order + ">: ", "Position Name") { AllowNone = true };
            oo.Keywords.Default = _order;
            var or = ed.GetKeywords(oo);
            if (or.Status == PromptStatus.OK) _order = or.StringResult;
            else if (or.Status != PromptStatus.None) return;

            var rooms = new List<RoomInput>();
            var texts = new List<RoomText>();
            var dims = new List<RoomDim>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = new List<ObjectId>();
                if (all)
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        string t = id.ObjectClass.DxfName;
                        if (t == "LWPOLYLINE" || t == "TEXT" || t == "MTEXT" || t.StartsWith("DIMENSION", StringComparison.Ordinal)) ids.Add(id);
                    }
                }
                else ids.AddRange(psr.Value.GetObjectIds());

                var roomLayers = new HashSet<string>(Settings.Get("RoomTableLayers", "ROOM-RECT;BP-ROOM;MEASURE-FLOOR;A-ROOM")
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var pl = ent as Polyline;
                    if (pl != null)
                    {
                        if (!pl.Closed || pl.NumberOfVertices < 3 || (all && !roomLayers.Contains(pl.Layer))) continue;
                        var pts = new List<P2>();
                        for (int i = 0; i < pl.NumberOfVertices; i++) { var p = pl.GetPoint2dAt(i); pts.Add(new P2(p.X, p.Y)); }
                        rooms.Add(new RoomInput { Outline = pts, Area = Math.Abs(pl.Area) });
                        continue;
                    }
                    var text = ent as DBText;
                    if (text != null)
                    {
                        var at = text.HorizontalMode == TextHorizontalMode.TextLeft && text.VerticalMode == TextVerticalMode.TextBase ? text.Position : text.AlignmentPoint;
                        texts.Add(new RoomText { At = new P2(at.X, at.Y), Text = text.TextString });
                        continue;
                    }
                    var mtext = ent as MText;
                    if (mtext != null) { texts.Add(new RoomText { At = new P2(mtext.Location.X, mtext.Location.Y), Text = mtext.Text }); continue; }
                    var aligned = ent as AlignedDimension;
                    var rotated = ent as RotatedDimension;
                    if (aligned != null) dims.Add(new RoomDim { A = Flat(aligned.XLine1Point), B = Flat(aligned.XLine2Point), Value = aligned.Measurement });
                    else if (rotated != null) dims.Add(new RoomDim { A = Flat(rotated.XLine1Point), B = Flat(rotated.XLine2Point), Value = rotated.Measurement });
                }
                tr.Commit();
            }
            if (rooms.Count == 0)
            {
                ed.WriteMessage("\nHCWROOMREPORT: no closed room outlines" + (all ? " on the " + Settings.Get("RoomTableLayers", "ROOM-RECT;BP-ROOM;MEASURE-FLOOR;A-ROOM") + " layers" : " in the selection")
                    + ". Make them with HCWROOMWALLS, or select closed polylines.");
                return;
            }

            double upm = Util.MmToDrawingUnits(1000.0);
            bool millimetres = string.Equals(Settings.Get("RoomTableUnit", "m"), "mm", StringComparison.OrdinalIgnoreCase);
            var rows = RoomTable.Build(rooms, texts, dims, upm, string.Equals(_order, "Name", StringComparison.OrdinalIgnoreCase) ? RoomOrder.Name : RoomOrder.Position);
            var headers = RoomTable.Headers(millimetres ? "mm" : "m");
            var table = RoomTable.ToRows(rows, millimetres);

            ed.WriteMessage("\n\nROOMS");
            foreach (var r in table)
                ed.WriteMessage("\n  " + Util.Pad(r[0], 6) + Util.Pad(r[1], 22) + Util.Pad(r[2], 10) + Util.Pad(r[3], 10) + Util.Pad(r[4], 10) + r[7]);
            int unnamed = rows.Count(r => r.Name.Length == 0);
            if (unnamed > 0) ed.WriteMessage("\n  " + unnamed + " room(s) have no name text inside them (name them with HCWROOMWALLS or type the name inside the outline).");

            var pr = ed.GetPoint(new PromptPointOptions("\nTop-left of the table in the drawing (Enter to skip): ") { AllowNone = true });
            if (pr.Status != PromptStatus.OK && pr.Status != PromptStatus.None) return;
            Point3d? at0 = pr.Status == PromptStatus.OK ? pr.Value.TransformBy(ed.CurrentUserCoordinateSystem) : (Point3d?)null;

            var eo = new PromptKeywordOptions("\nExport to a file [None/Csv/Xlsx/Both] <" + _export + ">: ", "None Csv Xlsx Both") { AllowNone = true };
            eo.Keywords.Default = _export;
            var er = ed.GetKeywords(eo);
            if (er.Status == PromptStatus.OK) _export = er.StringResult;
            else if (er.Status != PromptStatus.None) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MeasureBook.SaveTakeoff(tr, db, TakeoffName, headers, table);
                if (at0.HasValue) Draw(tr, db, at0.Value, headers, table);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWROOMREPORT: " + rows.Count + " room(s), " + rows.Sum(r => r.Area).ToString("0.00") + " sq m. Saved as the take-off \"" + TakeoffName + "\" (MEXPORT and MEXPORTX include it)"
                + (at0.HasValue ? "; table drawn." : "."));

            if (_export == "None") return;
            string dwg = db.Filename;
            string dir = string.IsNullOrEmpty(dwg) ? Path.GetTempPath() : Path.GetDirectoryName(dwg) + Path.DirectorySeparatorChar;
            string stem = (string.IsNullOrEmpty(dwg) ? "Drawing" : Path.GetFileNameWithoutExtension(dwg)) + "-Rooms";
            if (_export == "Csv" || _export == "Both")
            {
                try { Util.WriteCsv(dir + stem + ".csv", headers, table.Select(r => (IEnumerable<string>)r)); ed.WriteMessage("\nSaved: " + dir + stem + ".csv"); }
                catch (System.Exception) { ed.WriteMessage("\nCould not write the CSV file (is it open elsewhere?)."); }
            }
            if (_export == "Xlsx" || _export == "Both")
            {
                var sheet = new XlsxSheet { Name = "Rooms" };
                sheet.Rows.Add(headers.Select(h => XlsxCell.Str(h, true)).ToList());
                foreach (var r in table)
                {
                    bool total = r[0] == "TOTAL";
                    sheet.Rows.Add(r.Select(c => XlsxCell.Auto(c, total)).ToList());
                }
                try { XlsxWriter.Write(dir + stem + ".xlsx", new[] { sheet }); ed.WriteMessage("\nSaved: " + dir + stem + ".xlsx"); }
                catch (System.Exception) { ed.WriteMessage("\nCould not write the Excel file (is it open elsewhere?)."); }
            }
        }

        private static P2 Flat(Point3d p) => new P2(p.X, p.Y);

        /// <summary>Draws the table at the point, replacing an earlier room table in this space.</summary>
        private static void Draw(Transaction tr, Database db, Point3d at, string[] headers, List<string[]> table)
        {
            TitleBlockCommands.EnsureRegApp(tr, db);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            foreach (ObjectId id in space)
            {
                var old = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (old == null || old.IsErased || !TitleBlockCommands.IsKind(old, KindTable)) continue;
                old.UpgradeOpen(); old.Erase();
            }
            double h = Util.MmToDrawingUnits(Settings.GetDouble("RoomTableTextMm", 250));
            Util.EnsureHcwLayer(tr, db, "AN-TEXT");
            var ids = MeasureCommands.DrawTable(tr, db, new Point3d(at.X, at.Y - 2.5 * h, 0), headers, table, h, "AN-TEXT");
            var title = new DBText { Height = h * 1.2, TextString = "ROOM SCHEDULE", Layer = "AN-TEXT", Position = new Point3d(at.X, at.Y - 1.2 * h, 0) };
            ids.Add(space.AppendEntity(title));
            tr.AddNewlyCreatedDBObject(title, true);
            foreach (var id in ids) TitleBlockCommands.Tag((Entity)tr.GetObject(id, OpenMode.ForWrite), KindTable);
        }
    }
}
