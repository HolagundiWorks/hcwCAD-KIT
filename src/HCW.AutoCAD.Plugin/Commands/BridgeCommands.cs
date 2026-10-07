using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWBRIDGE writes the drawing as an hcw-aqc-bridge file (docs/bridge): the project details, the floors with their beam depths, the walls, the doors and
    /// windows and their schedule, the columns, the lintels and the rooms. The file goes next to the drawing (or into the folder in setting BridgeFolder) as
    /// <drawing>.aqcbridge.json. The mapping work is in Logic/BridgeMap.cs and Logic/BridgeExport.cs; this class only reads the drawing.
    /// </summary>
    public class BridgeCommands
    {
        private const string Dictionary = "HCW_BRIDGE", IdRecord = "ID";

        [CommandMethod("HCWBRIDGE")]
        public void Export()
        {
            var ed = Util.Ed; var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            string unit = Util.DrawingUnitName;
            double k = BridgeExport.MmPerUnit(unit);

            // 1. where the file goes
            string dwg = db.Filename;
            if (string.IsNullOrWhiteSpace(dwg) || !File.Exists(dwg)) { ed.WriteMessage("\nHCWBRIDGE: save the drawing first, so the file can be named after it."); return; }
            string folder = Settings.Get("BridgeFolder", "").Trim();
            if (folder.Length == 0) folder = Path.GetDirectoryName(dwg);
            string path = Path.Combine(folder, Path.GetFileNameWithoutExtension(dwg) + ".aqcbridge.json");

            // 2. the floors
            List<LevelRow> levels; ProjectData project;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                levels = LevelStore.Load(tr, db);
                project = ProjectCommands.Load(tr, db);
                tr.Commit();
            }
            if (levels.Count == 0) { ed.WriteMessage("\nHCWBRIDGE: no floors are defined. Run HCWFLOORS first."); return; }
            var which = levels;
            if (levels.Count > 1)
            {
                var names = string.Join(", ", levels.Select((l, i) => (i + 1) + " " + l.Name));
                var ir = ed.GetInteger(new PromptIntegerOptions("\nFloor this plan shows (" + names + "; 0 = the same plan on every floor) <1>: ")
                    { LowerLimit = 0, UpperLimit = levels.Count, DefaultValue = 1, UseDefaultValue = true });
                if (ir.Status != PromptStatus.OK) return;
                if (ir.Value > 0) which = new List<LevelRow> { levels[ir.Value - 1] };
            }
            bool repeated = which.Count > 1;
            Func<string, LevelRow, string> refFor = (r, lv) => repeated ? r + "@" + lv.Name : r;

            // 3. read the drawing
            var input = new BridgeInput { Unit = unit };
            var skipped = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                input.Source = new BridgeSource { App = "hcwCAD-KIT", Host = "AutoCAD " + AcAp.Version.Major + "." + AcAp.Version.Minor, Drawing = Path.GetFileName(dwg), DrawingId = DrawingId(db), Exported = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) };
                input.Project = BridgeMap.Project(project);
                input.Levels = BridgeMap.Levels(levels, project);
                input.BeamDepthsMm = project.BeamDepthsMm.ToList();

                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var book = MeasureBook.Load(tr, db);
                double toMm = LevelStore.UnitMm;
                var schedule = book.Openings.Select(o => new BridgeMap.ScheduleEntry { Mark = o.Mark, Kind = o.Kind, WidthMm = o.Width * toMm, HeightMm = o.Height * toMm, SillMm = o.Sill * toMm }).ToList();
                double ventMax = Settings.GetDouble("VentilatorMaxHeightMm", 600);

                // walls: the wall objects drawn by HCWWALL, HCWWALLADOPT and HCWWALLREGEN
                var walls = WallStore.Keys(tr, db).Select(id => WallStore.Load(tr, db, id)).Where(w => w != null).ToList();
                var wallHeightFor = new Func<LevelRow, double>(lv => (lv.CeilingMm > 0 ? lv.CeilingMm : Math.Max(0, lv.FflMm - lv.SlabMm)) / k);
                foreach (var lv in which)
                    foreach (var w in walls)
                        input.Walls.Add(new BridgeWall { Ref = refFor(w.Id, lv), Level = lv.Name, Length = BridgeMap.WallLength(w), Height = wallHeightFor(lv), Thickness = w.ThicknessMm / k, Layer = WallCommands.WallLayer });
                if (walls.Count == 0) skipped.Add("no wall objects (draw walls with HCWWALL, or run HCWWALLADOPT or HCWWALLREGEN on existing walls)");

                // openings: the doors and windows made by the tools
                var cols = new List<KeyValuePair<string, ColumnSize>>();
                var lintelLines = new List<Line>();
                var roomOutlines = new List<RoomInput>(); var roomIds = new List<string>(); var roomTexts = new List<RoomText>();
                var roomLayers = new HashSet<string>(Settings.Get("RoomTableLayers", "ROOM-RECT;BP-ROOM;MEASURE-FLOOR;A-ROOM")
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
                var seenRooms = new HashSet<string>();                     // the same room outline can sit on ROOM-RECT, MEASURE-FLOOR and MEASURE-CEILING copies
                var openings = new List<BridgeOpening>();
                int noWall = 0;
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var br = ent as BlockReference;
                    if (br != null)
                    {
                        var info = OpeningCommands.ReadOpening(tr, id);
                        if (info != null)
                        {
                            double sill = info.Door ? 0 : info.SillMm;
                            string wallRef = BridgeMap.NearestWall(info.Corners.Centre, walls, Math.Max(info.ThicknessMm, 230) * 1.5 / k);
                            if (wallRef == null && walls.Count > 0) noWall++;
                            foreach (var lv in which)
                                openings.Add(new BridgeOpening
                                {
                                    Ref = refFor(id.Handle.ToString(), lv), Kind = info.Door ? "Door" : "Window", Level = lv.Name,
                                    Mark = BridgeMap.OpeningMark(info.Door, info.WidthMm, info.HeightMm, sill, schedule, ventMax),
                                    Width = info.WidthMm / k, Height = info.HeightMm / k, Sill = sill / k, Type = info.Door && info.Type.Length > 0 ? info.Type : null,
                                    WallRef = wallRef == null ? null : refFor(wallRef, lv),
                                });
                        }
                        else if (ColumnCommands.IsColumnLayer(br.Layer))
                        {
                            var size = ColumnSize.FromBlockName(BlockOpenings.EffectiveName(tr, br));
                            if (size != null) cols.Add(new KeyValuePair<string, ColumnSize>(id.Handle.ToString(), size));
                        }
                        continue;
                    }
                    var pl = ent as Polyline;
                    if (pl != null && pl.Closed)
                    {
                        double a, b;
                        if (ColumnCommands.IsColumnLayer(pl.Layer) && pl.NumberOfVertices == 4 && ColumnCommands.Rectangle(pl, out a, out b))
                            cols.Add(new KeyValuePair<string, ColumnSize>(id.Handle.ToString(), new ColumnSize { W = Math.Max(a, b) / Util.MmToDrawingUnits(1.0), D = Math.Min(a, b) / Util.MmToDrawingUnits(1.0) }));
                        else if (roomLayers.Contains(pl.Layer) && pl.NumberOfVertices >= 3)
                        {
                            var pts = new List<P2>();
                            for (int i = 0; i < pl.NumberOfVertices; i++) { var q = pl.GetPoint2dAt(i); pts.Add(new P2(q.X, q.Y)); }
                            if (seenRooms.Add(BridgeMap.OutlineKey(pts, Math.Abs(pl.Area), Util.MmToDrawingUnits(1.0))))
                            { roomOutlines.Add(new RoomInput { Outline = pts, Area = Math.Abs(pl.Area) }); roomIds.Add(id.Handle.ToString()); }
                        }
                        continue;
                    }
                    var ln = ent as Line;
                    if (ln != null && string.Equals(ln.Layer, MeasureCommands.LayLt, StringComparison.OrdinalIgnoreCase) && ln.GetXDataForApplication(OpeningCommands.LintelApp) != null) { lintelLines.Add(ln); continue; }
                    var tx = ent as DBText;
                    if (tx != null)
                    {
                        var at = tx.HorizontalMode == TextHorizontalMode.TextLeft && tx.VerticalMode == TextVerticalMode.TextBase ? tx.Position : tx.AlignmentPoint;
                        roomTexts.Add(new RoomText { At = new P2(at.X, at.Y), Text = tx.TextString });
                    }
                }
                if (noWall > 0) skipped.Add(noWall + " door or window not matched to a wall object (wall_ref left out)");
                input.Openings.AddRange(openings);
                input.OpeningSchedule = BridgeMap.Schedule(openings, k);

                // columns, marked by size as HCWCOLSCHED marks them
                var marks = ColumnMarks.Assign(cols.Select(c => c.Value));
                var markOf = marks.ToDictionary(m => m.Label, m => m.Mark);
                foreach (var lv in which)
                    foreach (var c in cols)
                    {
                        var rounded = new ColumnSize { Round = c.Value.Round, W = Math.Round(c.Value.W), D = c.Value.Round ? Math.Round(c.Value.W) : Math.Round(c.Value.D) };
                        input.Columns.Add(new BridgeColumn { Ref = refFor(c.Key, lv), Mark = markOf[rounded.Label], Level = lv.Name, Width = c.Value.W / k, Depth = (c.Value.Round ? c.Value.W : c.Value.D) / k });
                    }

                // lintels made by HCWLINTEL (centre, depth, wall thickness and opening width kept with each one)
                double mmU = Util.MmToDrawingUnits(1.0);
                double bearing = Settings.GetDouble("LintelBearingMm", 230);
                foreach (var lv in which)
                    foreach (var ln in lintelLines)
                    {
                        var v = ln.GetXDataForApplication(OpeningCommands.LintelApp).AsArray().Where(t => t.TypeCode == (int)DxfCode.ExtendedDataReal).Select(t => (double)t.Value).ToList();
                        if (v.Count < 5) continue;
                        input.Lintels.Add(new BridgeLintel { Ref = refFor(ln.ObjectId.Handle.ToString(), lv), Level = lv.Name, Opening = v[4] / k, Bearing = bearing / k, Width = v[3] / k, Depth = v[2] / k });
                    }

                // rooms: closed outlines on the room layers, named from the text inside
                for (int i = 0; i < roomOutlines.Count; i++)
                {
                    var r = roomOutlines[i];
                    var centre = new P2(r.Outline.Average(p => p.X), r.Outline.Average(p => p.Y));
                    string name = RoomTable.NameOf(roomTexts.Where(t => PlanarRooms.Contains(r.Outline, t.At)), centre);
                    double l, w;
                    RoomTable.Extent(r.Outline, out l, out w);
                    foreach (var lv in which)
                        input.Rooms.Add(new BridgeRoom { Ref = refFor(roomIds[i], lv), Name = name.Length > 0 ? name : "ROOM " + (i + 1), Level = lv.Name, Area = r.Area, Length = l, Breadth = w });
                }
                tr.Commit();
            }
            foreach (var p in BridgeMap.LevelProblems(input.Levels)) skipped.Add(p);

            // 4. write it
            string json = BridgeExport.ToJson(input);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (System.Exception ex) { ed.WriteMessage("\nHCWBRIDGE: could not write " + path + ": " + ex.Message); return; }

            ed.WriteMessage("\nHCWBRIDGE: wrote " + path + " (units mm, drawing in " + unit + ").");
            ed.WriteMessage("\n  Floors " + input.Levels.Count + " (plan on " + (repeated ? "every floor" : which[0].Name) + "), walls " + input.Walls.Count + ", openings " + input.Openings.Count
                + " (" + input.OpeningSchedule.Count + " schedule rows), columns " + input.Columns.Count + ", lintels " + input.Lintels.Count + ", rooms " + input.Rooms.Count + ".");
            foreach (var s in skipped) ed.WriteMessage("\n  Note: " + s + ".");
            ed.WriteMessage("\n  Not sent: slabs (the plugin does not draw them yet), plaster, paint, skirting, shuttering and rebar (AQC works these out).");
        }

        /// <summary>
        /// HCWBRIDGEIMPORT reads a bridge file back (the levels, project details and beam depths AQC holds), lists what would change in this drawing, and
        /// applies it only when you say Yes. The default is No, so nothing changes without an answer. It changes the floors (the levels of the take-off book),
        /// the project details and the beam depths; it never touches the walls, openings or any drawing geometry.
        /// </summary>
        [CommandMethod("HCWBRIDGEIMPORT")]
        public void Import()
        {
            var ed = Util.Ed; var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            double unitMm = LevelStore.UnitMm;

            string dwg = db.Filename;
            string def = string.IsNullOrWhiteSpace(dwg) ? "" : Path.Combine(Settings.Get("BridgeFolder", "").Trim().Length > 0 ? Settings.Get("BridgeFolder", "").Trim() : Path.GetDirectoryName(dwg), Path.GetFileNameWithoutExtension(dwg) + ".aqcbridge.json");
            var so = new PromptStringOptions("\nBridge file to read" + (def.Length > 0 ? " <" + def + ">" : "") + ": ") { AllowSpaces = true, DefaultValue = def, UseDefaultValue = def.Length > 0 };
            var sr = ed.GetString(so);
            if (sr.Status != PromptStatus.OK) return;
            string path = sr.StringResult.Trim().Trim('"');
            if (!File.Exists(path)) { ed.WriteMessage("\nHCWBRIDGEIMPORT: there is no file " + path + "."); return; }

            BridgeInput file;
            try { file = BridgeImport.Read(File.ReadAllText(path, Encoding.UTF8)); }
            catch (FormatException ex) { ed.WriteMessage("\nHCWBRIDGEIMPORT: the file is not valid JSON. " + ex.Message); return; }
            catch (ArgumentException ex) { ed.WriteMessage("\nHCWBRIDGEIMPORT: " + ex.Message); return; }

            List<LevelRow> floors; ProjectData data;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                floors = LevelStore.Load(tr, db);
                data = ProjectCommands.Load(tr, db);
                var mine = DrawingStore.Read(tr, db, Dictionary, IdRecord).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && l != "EMPTY");
                tr.Commit();
                if (mine != null && !string.IsNullOrWhiteSpace(file.Source.DrawingId) && !string.Equals(mine.Trim(), file.Source.DrawingId, StringComparison.OrdinalIgnoreCase))
                    ed.WriteMessage("\nNote: the file's drawing id is not this drawing's. It may come from another drawing.");
            }

            var plan = BridgeImport.Compare(file, floors, data);
            ed.WriteMessage("\nBridge file " + Path.GetFileName(path) + " (from " + (file.Source.App ?? "unknown") + (string.IsNullOrWhiteSpace(file.Source.Exported) ? "" : ", " + file.Source.Exported) + ").");
            foreach (var d in plan.Details) ed.WriteMessage("\n  " + d.Value);
            foreach (var c in plan.LevelChanges) ed.WriteMessage("\n  " + c);
            foreach (var a in plan.Added) ed.WriteMessage("\n  New floor " + a);
            if (plan.BeamDepthsMm != null) ed.WriteMessage("\n  Standard beam depths: " + ProjectData.FormatDepths(data.BeamDepthsMm) + " -> " + ProjectData.FormatDepths(plan.BeamDepthsMm));
            foreach (var s in plan.Skipped) ed.WriteMessage("\n  Skipped: " + s + ".");
            ed.WriteMessage("\n  The plinth level (AQC's) is never imported; walls, openings and other drawing geometry are never changed.");
            if (plan.IsEmpty) { ed.WriteMessage("\nHCWBRIDGEIMPORT: the drawing already has these values. Nothing to change."); return; }

            var ko = new PromptKeywordOptions("\nApply these changes to the drawing [Yes/No] <No>: ", "Yes No") { AllowNone = true };
            ko.Keywords.Default = "No";
            var kr = ed.GetKeywords(ko);
            if (kr.Status != PromptStatus.OK || kr.StringResult != "Yes") { ed.WriteMessage("\nHCWBRIDGEIMPORT: nothing was changed."); return; }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var d in plan.Details) data.Set(d.Key, d.Value.To);
                if (plan.BeamDepthsMm != null) data.BeamDepthsMm = plan.BeamDepthsMm;
                data.ClearFloorBeams();
                foreach (var kv in plan.FloorBeams) data.SetFloorBeam(kv.Key, kv.Value);
                ProjectCommands.Save(tr, db, data);
                var book = MeasureBook.Load(tr, db);
                book.Floors.Clear();
                foreach (var f in plan.Floors)
                    book.Floors.Add(new MeasureBook.FloorSpec { Name = f.Name, Height = f.CeilingMm / unitMm, FflHeight = f.FflMm / unitMm, LintelBottom = f.LintelMm / unitMm, Slab = f.SlabMm / unitMm });
                book.Save(tr, db);
                tr.Commit();
            }
            ed.WriteMessage("\nHCWBRIDGEIMPORT: applied " + (plan.Details.Count + plan.LevelChanges.Count + plan.Added.Count + (plan.BeamDepthsMm != null ? 1 : 0)) + " change(s). Undo (U) reverses them. HCWFLOORS and HCWPROJECT show the result.");
        }

        /// <summary>The drawing's id for the bridge file: made on the first export and kept in the drawing, so every export of this drawing carries the same one.</summary>
        private static string DrawingId(Database db)
        {
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var existing = DrawingStore.Read(tr, db, Dictionary, IdRecord).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && l != "EMPTY");
                if (existing != null) { tr.Commit(); return existing.Trim(); }
                string id = Guid.NewGuid().ToString();
                DrawingStore.Write(tr, db, Dictionary, IdRecord, new[] { id });
                tr.Commit();
                return id;
            }
        }
    }
}
