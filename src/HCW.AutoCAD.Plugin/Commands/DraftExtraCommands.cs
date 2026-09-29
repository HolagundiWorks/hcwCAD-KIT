using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Block counts, grids, attribute numbering, and live area fields.
    /// </summary>
    public class DraftExtraCommands
    {
        private static int _rows = 2;
        private static int _cols = 2;
        private static string _blockPattern = "";
        private static string _tagPattern = "MARK";
        private static string _labelPrefix = "";
        private static string _labelSuffix = "";
        private static int _labelStart = 1;
        private static int _labelDigits = 2;
        private static bool _labelBlocks = true;
        private static bool _labelLeaders = true;
        private static int _areaStart = 1;

        [CommandMethod("DBCOUNT")]
        public void CountBlocks()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var opt = new PromptSelectionOptions { MessageForAdding = "\nSelect blocks to count <all>: " };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") });
            var picked = ed.GetSelection(opt, filter);
            if (picked.Status == PromptStatus.Cancel) return;

            var totals = new SortedDictionary<string, BlockCount>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (picked.Status == PromptStatus.OK)
                {
                    foreach (SelectedObject so in picked.Value)
                    {
                        if (so == null) continue;
                        if (tr.GetObject(so.ObjectId, OpenMode.ForRead) is BlockReference br && br.OwnerId == db.CurrentSpaceId)
                            AddBlock(tr, br, totals);
                    }
                }
                else
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br)
                            AddBlock(tr, br, totals);
                    }
                }
                tr.Commit();
            }

            if (totals.Count == 0)
            {
                ed.WriteMessage("\nNo blocks found in the current layout.");
                return;
            }

            ed.WriteMessage("\n" + new string('=', 46));
            ed.WriteMessage("\n" + Pad(" Block", "Count", 46));
            ed.WriteMessage("\n" + new string('=', 46));
            int grand = 0;
            foreach (var kv in totals)
            {
                grand += kv.Value.Total;
                ed.WriteMessage("\n" + Pad(" " + kv.Key, kv.Value.Total.ToString(CultureInfo.InvariantCulture), 46));
                foreach (var state in kv.Value.States.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                    ed.WriteMessage("\n" + Pad("    " + state.Key, state.Value.ToString(CultureInfo.InvariantCulture), 46));
                ed.WriteMessage("\n" + new string('-', 46));
            }
            ed.WriteMessage("\n" + Pad(" Total", grand.ToString(CultureInfo.InvariantCulture), 46));
            ed.WriteMessage("\n" + new string('=', 46));

            var file = new PromptKeywordOptions("\nWrite a file [TXT/CSV/None] <None>: ") { AllowNone = true };
            file.Keywords.Add("TXT");
            file.Keywords.Add("CSV");
            file.Keywords.Add("None");
            file.Keywords.Default = "None";
            var choice = ed.GetKeywords(file);
            if (choice.Status != PromptStatus.OK || choice.StringResult == "None") return;

            bool csv = choice.StringResult == "CSV";
            string path = UniquePath(db, csv ? ".csv" : ".txt");
            string del = csv ? "," : "\t";
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("Block" + del + "Visibility" + del + "Count");
                foreach (var kv in totals)
                {
                    writer.WriteLine(Cell(kv.Key, csv) + del + del + kv.Value.Total.ToString(CultureInfo.InvariantCulture));
                    foreach (var state in kv.Value.States.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase))
                        writer.WriteLine(del + Cell(state.Key, csv) + del + state.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            ed.WriteMessage("\nDBCOUNT wrote " + path);
        }

        [CommandMethod("DGRID")]
        public void DrawGrid()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskInt(ed, "\nNumber of rows <" + _rows + ">: ", ref _rows)) return;
            if (!AskInt(ed, "\nNumber of columns <" + _cols + ">: ", ref _cols)) return;

            var bpr = ed.GetPoint("\nSpecify base point: ");
            if (bpr.Status != PromptStatus.OK) return;
            var cpr = ed.GetCorner("\nSpecify opposite corner: ", bpr.Value);
            if (cpr.Status != PromptStatus.OK) return;

            double hd = cpr.Value.X - bpr.Value.X;
            double vd = cpr.Value.Y - bpr.Value.Y;
            if (Math.Abs(hd) < 1e-9 || Math.Abs(vd) < 1e-9)
            {
                ed.WriteMessage("\nThe two corners need a width and a height.");
                return;
            }

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            double z = bpr.Value.Z;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                string layer = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;
                double hs = hd / _cols;
                double vs = vd / _rows;
                for (int c = 0; c <= _cols; c++)
                    AddLine(tr, space, layer, ucs, bpr.Value.X + c * hs, bpr.Value.Y, bpr.Value.X + c * hs, bpr.Value.Y + vd, z);
                for (int r = 0; r <= _rows; r++)
                    AddLine(tr, space, layer, ucs, bpr.Value.X, bpr.Value.Y + r * vs, bpr.Value.X + hd, bpr.Value.Y + r * vs, z);
                tr.Commit();
            }
            ed.WriteMessage("\nDGRID: " + _rows + " rows, " + _cols + " columns.");
        }

        [CommandMethod("AUTOLABEL")]
        public void AutoLabel()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            using (var dlg = new UI.AutoLabelForm(_blockPattern, _tagPattern, _labelPrefix, _labelSuffix, _labelStart, _labelDigits, _labelBlocks, _labelLeaders))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                _blockPattern = dlg.BlockPattern;
                _tagPattern = dlg.TagPattern;
                _labelPrefix = dlg.Prefix;
                _labelSuffix = dlg.Suffix;
                _labelStart = dlg.Start;
                _labelDigits = dlg.Digits;
                _labelBlocks = dlg.IncludeBlocks;
                _labelLeaders = dlg.IncludeLeaders;
            }

            int number = _labelStart;
            int labelled = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (ObjectId id in space)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    bool wrote = false;
                    if (_labelBlocks && ent is BlockReference br)
                        wrote = LabelBlock(tr, br, ref number);
                    else if (_labelLeaders && ent is MLeader ml)
                        wrote = LabelLeader(tr, ml, ref number);
                    if (wrote) labelled++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nAUTOLABEL: numbered " + labelled + " in this layout. Run it again after you copy or erase.");
        }

        [CommandMethod("AREAFIELD")]
        [CommandMethod("A2F")]
        public void AreaField()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "ARC,CIRCLE,ELLIPSE,HATCH,LWPOLYLINE,POLYLINE,REGION,SPLINE")
            });
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect objects to sum: " }, filter);
            if (sel.Status != PromptStatus.OK) return;
            var ppr = ed.GetPoint("\nPick a point or a table cell for the field: ");
            if (ppr.Status != PromptStatus.OK) return;

            var ids = new List<ObjectId>();
            foreach (SelectedObject so in sel.Value)
                if (so != null) ids.Add(so.ObjectId);
            if (ids.Count == 0) return;

            string unit = AreaUnitLabel(db);
            double divisor = AreaDivisor(db);
            string field = AreaExpression(ids, divisor);
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d picked = ppr.Value.TransformBy(ucs);

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!TryFillTableCell(tr, db, ed, picked, field))
                {
                    Util.EnsureLayer(tr, db, "AREA_TABLE", 4);
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    double height = db.Textsize > 1e-6 ? db.Textsize : Util.MmToDrawingUnits(2.5);
                    var mt = new MText
                    {
                        Location = picked,
                        TextHeight = height,
                        Contents = field,
                        Layer = "AREA_TABLE",
                        Width = 0
                    };
                    space.AppendEntity(mt);
                    tr.AddNewlyCreatedDBObject(mt, true);
                }
                tr.Commit();
            }
            ed.WriteMessage("\nAREAFIELD: live area in " + unit + ". Regen if the number does not show yet.");
        }

        [CommandMethod("AREALABEL")]
        [CommandMethod("AT")]
        public void AreaLabel()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var mode = new PromptKeywordOptions("\nArea labels to a [Table/File] <Table>: ") { AllowNone = true };
            mode.Keywords.Add("Table");
            mode.Keywords.Add("File");
            mode.Keywords.Default = "Table";
            var chosen = ed.GetKeywords(mode);
            if (chosen.Status != PromptStatus.OK && chosen.Status != PromptStatus.None) return;
            bool toFile = chosen.Status == PromptStatus.OK && chosen.StringResult == "File";

            var startOpt = new PromptIntegerOptions("\nStarting number <" + _areaStart + ">: ")
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = _areaStart,
                UseDefaultValue = true
            };
            var startRes = ed.GetInteger(startOpt);
            if (startRes.Status != PromptStatus.OK && startRes.Status != PromptStatus.None) return;
            if (startRes.Status == PromptStatus.OK) _areaStart = startRes.Value;

            double defaultH = db.Textsize > 1e-6 ? db.Textsize : Util.MmToDrawingUnits(2.5);
            var heightOpt = new PromptDistanceOptions("\nText height <" + defaultH.ToString("F4", CultureInfo.InvariantCulture) + ">: ")
            {
                AllowZero = false,
                AllowNegative = false,
                AllowNone = true,
                DefaultValue = defaultH,
                UseDefaultValue = true
            };
            var heightRes = ed.GetDistance(heightOpt);
            if (heightRes.Status != PromptStatus.OK && heightRes.Status != PromptStatus.None) return;
            double textHeight = heightRes.Status == PromptStatus.OK ? heightRes.Value : defaultH;

            string unit = AreaUnitLabel(db);
            double divisor = AreaDivisor(db);
            ed.WriteMessage("\nAreas are shown in " + unit + ".");

            if (toFile)
                LabelToFile(ed, db, textHeight, divisor, unit);
            else
                LabelToTable(ed, db, textHeight, divisor, unit);
        }

        private void LabelToTable(Editor ed, Database db, double textHeight, double divisor, string unit)
        {
            var ppr = ed.GetPoint("\nPick point for the area table: ");
            if (ppr.Status != PromptStatus.OK) return;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d origin = ppr.Value.TransformBy(ucs);
            double rot = UcsAngle(ucs);
            ObjectId tableId = ObjectId.Null;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layer = (LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead);
                if (layer.IsLocked)
                {
                    ed.WriteMessage("\nThe current layer is locked.");
                    return;
                }
                Util.EnsureLayer(tr, db, "AREA_TABLE", 4);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var table = new Table();
                table.SetDatabaseDefaults(db);
                table.TableStyle = db.Tablestyle;
                table.SetSize(1, 2);
                table.SetRowHeight(textHeight * 2.2);
                table.Columns[0].Width = textHeight * 10;
                table.Columns[1].Width = textHeight * 14;
                table.Position = origin;
                table.Layer = "AREA_TABLE";
                table.Cells[0, 0].TextString = "Number";
                table.Cells[0, 1].TextString = "Area (" + unit + ")";
                space.AppendEntity(table);
                tr.AddNewlyCreatedDBObject(table, true);
                if (Math.Abs(rot) > 1e-8)
                    table.TransformBy(Matrix3d.Rotation(rot, Vector3d.ZAxis.TransformBy(ucs), origin));
                table.GenerateLayout();
                tableId = table.ObjectId;
                tr.Commit();
            }

            int number = _areaStart;
            var made = new List<PlacedArea>();
            bool objects = true;
            while (true)
            {
                PromptStatus status;
                string keyword;
                Point3d pick;
                ObjectId entityId;
                if (!NextArea(ed, objects, made.Count > 0, out status, out keyword, out pick, out entityId))
                    break;
                if (status == PromptStatus.Keyword && keyword == "Undo")
                {
                    if (made.Count == 0)
                    {
                        ed.WriteMessage("\nNothing to undo.");
                        continue;
                    }
                    UndoLast(db, tableId, made);
                    number--;
                    continue;
                }
                if (status == PromptStatus.Keyword && (keyword == "Object" || keyword == "Pick"))
                {
                    objects = keyword == "Object";
                    continue;
                }

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    Entity source = null;
                    ObjectId boundaryId = ObjectId.Null;
                    if (objects)
                    {
                        source = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                        if (!Measurable(source))
                        {
                            ed.WriteMessage("\nSelect a closed object with an area.");
                            continue;
                        }
                    }
                    else
                    {
                        source = BoundaryFromPick(ed, db, tr, space, pick, out boundaryId);
                        if (source == null)
                        {
                            ed.WriteMessage("\nNo closed area at that point.");
                            continue;
                        }
                    }

                    string label = number.ToString(CultureInfo.InvariantCulture);
                    Point3d center = CenterOf(source);
                    var text = new DBText
                    {
                        TextString = label,
                        Height = textHeight,
                        Rotation = rot,
                        Layer = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        VerticalMode = TextVerticalMode.TextVerticalMid
                    };
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                    text.AlignmentPoint = center;
                    text.AdjustAlignment(db);

                    var table = (Table)tr.GetObject(tableId, OpenMode.ForWrite);
                    int row = table.Rows.Count;
                    table.InsertRows(row, textHeight * 2.2, 1);
                    table.Cells[row, 0].TextString = FieldOf(text.ObjectId, "TextString", 1);
                    table.Cells[row, 1].TextString = AreaExpression(new List<ObjectId> { source.ObjectId }, divisor);
                    table.GenerateLayout();
                    tr.Commit();
                    made.Add(new PlacedArea { LabelId = text.ObjectId, BoundaryId = boundaryId, Row = row });
                    number++;
                }
            }

            if (made.Count == 0 && tableId.IsValid)
            {
                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    tr.GetObject(tableId, OpenMode.ForWrite).Erase();
                    tr.Commit();
                }
            }
            else
            {
                _areaStart = number;
                ed.WriteMessage("\nAREALABEL: " + made.Count + " area" + (made.Count == 1 ? "" : "s") + ".");
            }
        }

        private void LabelToFile(Editor ed, Database db, double textHeight, double divisor, string unit)
        {
            var save = new PromptSaveFileOptions("Save area list")
            {
                Filter = "CSV (*.csv)|*.csv|Text (*.txt)|*.txt",
                DialogCaption = "Save area list"
            };
            var pathRes = ed.GetFileNameForSave(save);
            if (pathRes.Status != PromptStatus.OK) return;

            var rows = new List<string>();
            int number = _areaStart;
            bool objects = true;
            var labels = new List<ObjectId>();
            var boundaries = new List<ObjectId>();
            double rot = UcsAngle(ed.CurrentUserCoordinateSystem);
            string del = pathRes.StringResult.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "," : "\t";

            while (true)
            {
                PromptStatus status;
                string keyword;
                Point3d pick;
                ObjectId entityId;
                if (!NextArea(ed, objects, labels.Count > 0, out status, out keyword, out pick, out entityId))
                    break;
                if (status == PromptStatus.Keyword && keyword == "Undo")
                {
                    if (labels.Count == 0) { ed.WriteMessage("\nNothing to undo."); continue; }
                    EraseOne(db, labels[labels.Count - 1]);
                    if (boundaries.Count > 0 && boundaries[boundaries.Count - 1].IsValid)
                        EraseOne(db, boundaries[boundaries.Count - 1]);
                    labels.RemoveAt(labels.Count - 1);
                    boundaries.RemoveAt(boundaries.Count - 1);
                    rows.RemoveAt(rows.Count - 1);
                    number--;
                    continue;
                }
                if (status == PromptStatus.Keyword && (keyword == "Object" || keyword == "Pick"))
                {
                    objects = keyword == "Object";
                    continue;
                }

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    Entity source;
                    ObjectId boundaryId = ObjectId.Null;
                    if (objects)
                    {
                        source = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                        if (!Measurable(source))
                        {
                            ed.WriteMessage("\nSelect a closed object with an area.");
                            continue;
                        }
                    }
                    else
                    {
                        source = BoundaryFromPick(ed, db, tr, space, pick, out boundaryId);
                        if (source == null)
                        {
                            ed.WriteMessage("\nNo closed area at that point.");
                            continue;
                        }
                    }
                    double area = MeasuredArea(source) / divisor;
                    string label = number.ToString(CultureInfo.InvariantCulture);
                    var text = new DBText
                    {
                        TextString = label,
                        Height = textHeight,
                        Rotation = rot,
                        Layer = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name,
                        HorizontalMode = TextHorizontalMode.TextCenter,
                        VerticalMode = TextVerticalMode.TextVerticalMid
                    };
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                    text.AlignmentPoint = CenterOf(source);
                    text.AdjustAlignment(db);
                    tr.Commit();
                    labels.Add(text.ObjectId);
                    boundaries.Add(boundaryId);
                    rows.Add(label + del + area.ToString("F4", CultureInfo.InvariantCulture));
                    number++;
                }
            }

            if (rows.Count == 0) return;
            using (var writer = new StreamWriter(pathRes.StringResult))
            {
                writer.WriteLine("Number" + del + "Area (" + unit + ")");
                foreach (var row in rows) writer.WriteLine(row);
            }
            _areaStart = number;
            ed.WriteMessage("\nAREALABEL wrote " + rows.Count + " rows to " + pathRes.StringResult);
        }

        private static bool NextArea(Editor ed, bool objects, bool canUndo, out PromptStatus status, out string keyword, out Point3d pick, out ObjectId entityId)
        {
            keyword = null;
            pick = Point3d.Origin;
            entityId = ObjectId.Null;
            if (objects)
            {
                var opt = new PromptEntityOptions("\nSelect object or [" + (canUndo ? "Undo/" : "") + "Pick] <exit>: ");
                opt.SetRejectMessage("\nSelect a closed object with an area.");
                opt.AddAllowedClass(typeof(Curve), false);
                opt.AddAllowedClass(typeof(Region), false);
                opt.Keywords.Add("Pick");
                if (canUndo) opt.Keywords.Add("Undo");
                opt.AllowNone = true;
                var res = ed.GetEntity(opt);
                status = res.Status;
                if (res.Status == PromptStatus.OK) entityId = res.ObjectId;
                if (res.Status == PromptStatus.Keyword) keyword = res.StringResult;
                return res.Status == PromptStatus.OK || res.Status == PromptStatus.Keyword;
            }

            var popt = new PromptPointOptions("\nPick inside an area or [" + (canUndo ? "Undo/" : "") + "Object] <exit>: ") { AllowNone = true };
            popt.Keywords.Add("Object");
            if (canUndo) popt.Keywords.Add("Undo");
            var pr = ed.GetPoint(popt);
            status = pr.Status;
            if (pr.Status == PromptStatus.OK) pick = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            if (pr.Status == PromptStatus.Keyword) keyword = pr.StringResult;
            return pr.Status == PromptStatus.OK || pr.Status == PromptStatus.Keyword;
        }

        private static Entity BoundaryFromPick(Editor ed, Database db, Transaction tr, BlockTableRecord space, Point3d wcs, out ObjectId boundaryId)
        {
            boundaryId = ObjectId.Null;
            DBObjectCollection loops;
            try { loops = ed.TraceBoundary(wcs, false); }
            catch { return null; }
            if (loops == null || loops.Count == 0) return null;
            var pl = loops[0] as Polyline;
            if (pl == null) return null;
            string layer = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;
            pl.Layer = layer;
            space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            for (int i = 1; i < loops.Count; i++) loops[i].Dispose();
            boundaryId = pl.ObjectId;
            return pl;
        }

        private static void UndoLast(Database db, ObjectId tableId, List<PlacedArea> made)
        {
            var last = made[made.Count - 1];
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tr.GetObject(last.LabelId, OpenMode.ForWrite).Erase();
                if (!last.BoundaryId.IsNull && last.BoundaryId.IsValid)
                    tr.GetObject(last.BoundaryId, OpenMode.ForWrite).Erase();
                var table = (Table)tr.GetObject(tableId, OpenMode.ForWrite);
                if (last.Row < table.Rows.Count)
                    table.DeleteRows(last.Row, 1);
                table.GenerateLayout();
                tr.Commit();
            }
            made.RemoveAt(made.Count - 1);
        }

        private static void EraseOne(Database db, ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var obj = tr.GetObject(id, OpenMode.ForWrite, false);
                if (obj != null && !obj.IsErased) obj.Erase();
                tr.Commit();
            }
        }

        private static bool Measurable(Entity ent)
        {
            if (ent == null || ent is Hatch) return false;
            if (ent is Region) return true;
            if (ent is Curve curve)
            {
                try { return curve.Closed && curve.Area > 1e-12; }
                catch { return false; }
            }
            return false;
        }

        private static double MeasuredArea(Entity ent)
        {
            if (ent is Region region) return Math.Abs(region.Area);
            if (ent is Curve curve) return Math.Abs(curve.Area);
            return 0;
        }

        private static Point3d CenterOf(Entity ent)
        {
            if (ent is Circle circle) return circle.Center;
            if (ent is Ellipse ellipse) return ellipse.Center;
            if (ent is Polyline pl) return PolyCentroid(pl);
            if (ent.Bounds.HasValue)
            {
                var box = ent.Bounds.Value;
                return new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2.0, (box.MinPoint.Y + box.MaxPoint.Y) / 2.0, (box.MinPoint.Z + box.MaxPoint.Z) / 2.0);
            }
            return Point3d.Origin;
        }

        private static Point3d PolyCentroid(Polyline pl)
        {
            int n = pl.NumberOfVertices;
            double twice = 0, cx = 0, cy = 0;
            for (int i = 0; i < n; i++)
            {
                var p = pl.GetPoint2dAt(i);
                var q = pl.GetPoint2dAt((i + 1) % n);
                double cross = p.X * q.Y - q.X * p.Y;
                twice += cross;
                cx += (p.X + q.X) * cross;
                cy += (p.Y + q.Y) * cross;
            }
            if (Math.Abs(twice) < 1e-12)
            {
                if (pl.Bounds.HasValue)
                {
                    var box = pl.Bounds.Value;
                    return new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2.0, (box.MinPoint.Y + box.MaxPoint.Y) / 2.0, pl.Elevation);
                }
                return Point3d.Origin;
            }
            var flat = new Point3d(cx / (3.0 * twice), cy / (3.0 * twice), pl.Elevation);
            return flat.TransformBy(Matrix3d.PlaneToWorld(new Plane(Point3d.Origin, pl.Normal)));
        }

        private static bool TryFillTableCell(Transaction tr, Database db, Editor ed, Point3d wcs, string field)
        {
            Vector3d view;
            using (var vw = ed.GetCurrentView())
                view = vw.ViewDirection;
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Table table)) continue;
                table.UpgradeOpen();
                var hit = table.HitTest(wcs, view);
                if (hit.Row < 0 || hit.Column < 0) continue;
                table.Cells[hit.Row, hit.Column].TextString = field;
                table.GenerateLayout();
                return true;
            }
            return false;
        }

        private static string AreaExpression(IList<ObjectId> ids, double divisor)
        {
            var parts = new List<string>();
            foreach (var id in ids)
                parts.Add("%<\\AcObjProp Object(%<\\_ObjId " + id.OldIdPtr.ToInt64().ToString(CultureInfo.InvariantCulture) + ">%).Area>%");
            string sum = parts.Count == 1 ? parts[0] : string.Join(" + ", parts);
            string body = Math.Abs(divisor - 1.0) < 1e-12
                ? sum
                : "(" + sum + ") / " + divisor.ToString("0.################", CultureInfo.InvariantCulture);
            if (parts.Count == 1 && Math.Abs(divisor - 1.0) < 1e-12)
                return "%<\\AcObjProp Object(%<\\_ObjId " + ids[0].OldIdPtr.ToInt64().ToString(CultureInfo.InvariantCulture) + ">%).Area \\f \"%lu2%pr2\">%";
            return "%<\\AcExpr " + body + " \\f \"%lu2%pr2\">%";
        }

        private static string FieldOf(ObjectId id, string property, double divisor)
        {
            string obj = "%<\\AcObjProp Object(%<\\_ObjId " + id.OldIdPtr.ToInt64().ToString(CultureInfo.InvariantCulture) + ">%)." + property;
            if (Math.Abs(divisor - 1.0) < 1e-12) return obj + ">%";
            return obj + ">%";
        }

        private static string AreaUnitLabel(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Inches:
                case UnitsValue.Feet:
                    return "sq ft";
                case UnitsValue.Millimeters:
                case UnitsValue.Centimeters:
                case UnitsValue.Meters:
                    return "sq m";
                default:
                    return "drawing units";
            }
        }

        private static double AreaDivisor(Database db)
        {
            switch (db.Insunits)
            {
                case UnitsValue.Inches: return 144.0;
                case UnitsValue.Feet: return 1.0;
                case UnitsValue.Millimeters: return 1000000.0;
                case UnitsValue.Centimeters: return 10000.0;
                case UnitsValue.Meters: return 1.0;
                default: return 1.0;
            }
        }

        private static double UcsAngle(Matrix3d ucs)
        {
            Vector3d x = Vector3d.XAxis.TransformBy(ucs);
            return Math.Atan2(x.Y, x.X);
        }

        private static bool LabelBlock(Transaction tr, BlockReference br, ref int number)
        {
            string name = EffectiveName(tr, br);
            if (!Wild(_blockPattern, name) || br.AttributeCollection.Count == 0) return false;
            foreach (ObjectId id in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                if (!Wild(_tagPattern, att.Tag)) continue;
                att.UpgradeOpen();
                att.TextString = FormatLabel(number);
                number++;
                return true;
            }
            return false;
        }

        private static bool LabelLeader(Transaction tr, MLeader leader, ref int number)
        {
            if (leader.ContentType != ContentType.BlockContent || leader.BlockContentId.IsNull) return false;
            var def = (BlockTableRecord)tr.GetObject(leader.BlockContentId, OpenMode.ForRead);
            if (!Wild(_blockPattern, def.Name)) return false;
            foreach (ObjectId id in def)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                if (!Wild(_tagPattern, ad.Tag)) continue;
                leader.UpgradeOpen();
                var written = new AttributeReference();
                written.SetAttributeFromBlock(ad, Matrix3d.Identity);
                written.TextString = FormatLabel(number);
                leader.SetBlockAttribute(id, written);
                number++;
                return true;
            }
            return false;
        }

        private static string FormatLabel(int number)
        {
            string digits = number.ToString(CultureInfo.InvariantCulture);
            if (_labelDigits > digits.Length) digits = digits.PadLeft(_labelDigits, '0');
            return _labelPrefix + digits + _labelSuffix;
        }

        private static string EffectiveName(Transaction tr, BlockReference br)
        {
            if (!br.IsDynamicBlock) return br.Name;
            var def = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead);
            return def.Name;
        }

        private static bool Wild(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value ?? "", regex, RegexOptions.IgnoreCase);
        }

        private static void AddBlock(Transaction tr, BlockReference br, IDictionary<string, BlockCount> totals)
        {
            string name = EffectiveName(tr, br);
            if (!totals.TryGetValue(name, out var count))
            {
                count = new BlockCount();
                totals[name] = count;
            }
            count.Total++;
            string state = VisibilityState(br);
            if (string.IsNullOrEmpty(state)) return;
            if (!count.States.ContainsKey(state)) count.States[state] = 0;
            count.States[state]++;
        }

        private static string VisibilityState(BlockReference br)
        {
            if (!br.IsDynamicBlock) return null;
            var named = new List<DynamicBlockReferenceProperty>();
            var only = new List<DynamicBlockReferenceProperty>();
            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
            {
                if (!(prop.Value is string)) continue;
                object[] allowed;
                try { allowed = prop.GetAllowedValues(); }
                catch { continue; }
                if (allowed == null || allowed.Length == 0 || !(allowed[0] is string)) continue;
                only.Add(prop);
                if (prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                    named.Add(prop);
            }
            if (named.Count > 0) return named[0].Value as string;
            if (only.Count == 1) return only[0].Value as string;
            return null;
        }

        private static bool AskInt(Editor ed, string message, ref int value)
        {
            var opt = new PromptIntegerOptions(message)
            {
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = value,
                UseDefaultValue = true,
                LowerLimit = 1
            };
            var res = ed.GetInteger(opt);
            if (res.Status == PromptStatus.Cancel) return false;
            if (res.Status == PromptStatus.OK) value = res.Value;
            return true;
        }

        private static void AddLine(Transaction tr, BlockTableRecord space, string layer, Matrix3d ucs, double x1, double y1, double x2, double y2, double z)
        {
            var line = new Line(new Point3d(x1, y1, z).TransformBy(ucs), new Point3d(x2, y2, z).TransformBy(ucs))
            {
                Layer = layer
            };
            space.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static string UniquePath(Database db, string extension)
        {
            string dir = Path.GetDirectoryName(db.Filename);
            if (string.IsNullOrEmpty(dir))
                dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string name = Path.GetFileNameWithoutExtension(db.Filename);
            if (string.IsNullOrEmpty(name)) name = "blocks";
            string path = Path.Combine(dir, name + extension);
            int n = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, name + "(" + n.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                n++;
            }
            return path;
        }

        private static string Pad(string left, string right, int width)
        {
            int dots = width - left.Length - right.Length;
            if (dots < 2) dots = 2;
            return left + new string('.', dots) + right;
        }

        private static string Cell(string value, bool csv)
        {
            if (!csv) return value ?? "";
            string text = value ?? "";
            if (text.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private sealed class BlockCount
        {
            public int Total;
            public readonly Dictionary<string, int> States = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PlacedArea
        {
            public ObjectId LabelId;
            public ObjectId BoundaryId;
            public int Row;
        }
    }
}
