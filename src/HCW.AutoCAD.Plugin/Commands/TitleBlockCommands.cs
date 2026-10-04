using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// A3 building-permit title block from the office template: project fields,
    /// area statement, F.A.R., ground cover, stability certificate, and movable notes.
    /// </summary>
    public class TitleBlockCommands
    {
        private const string TitleBlockName = "HCW_TITLE_A3";
        private const string AppName = "HCWKIT";
        private const string LayerBorder = "BP-SHEET-BORDER";
        internal const string LayerTitle = "BP-TITLE-BLOCK";
        private const string LayerNotes = "BP-NOTES";
        private const string LayerFields = "BP-FIELDS";

        private static readonly string[] FieldOrder =
        {
            "PROJECT_TITLE", "DRAWING_TITLE", "DRAWING_NO", "SIZE", "PLOT_USE",
            "OWNER", "ARCHITECT", "PID", "SITE_AREA", "STABILITY",
            "FAR_ACH", "FAR_PERM", "GC_ACH", "GC_PCT", "GC_PERM",
            "FL1", "DED1", "NET1", "GROSS1",
            "FL2", "DED2", "NET2", "GROSS2",
            "FL3", "DED3", "NET3", "GROSS3",
            "FL4", "DED4", "NET4", "GROSS4",
            "TOT_DED", "TOT_NET", "TOT_GROSS"
        };

        [CommandMethod("TITLEBLOCK")]
        [CommandMethod("BPLTTITLEBLOCK")]
        public void InsertTitle()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var ppr = ed.GetPoint("\nInsertion point for the A3 title block (bottom-left of the sheet): ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d ins = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                double mm = Util.MmToDrawingUnits(1.0);
                PrepareLayers(tr, db);
                EnsureRegApp(tr, db);
                ObjectId defId = EnsureTitleDefinition(tr, db, mm);
                var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var br = new BlockReference(ins, defId) { Layer = LayerTitle };
                ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                AddAttributes(tr, br);
                Tag(br, "TITLE");
                tr.Commit();
            }
            ed.WriteMessage("\nTITLEBLOCK: A3 building-permit sheet placed. Edit Fields fills the boxes. Place Notes drops a note set you can move.");
        }

        [CommandMethod("TITLEFIELDS")]
        public void EditFields()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var opt = new PromptEntityOptions("\nSelect the title block to edit: ");
            opt.SetRejectMessage("\nSelect the hcwCAD-KIT title block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(opt);
            if (per.Status != PromptStatus.OK) return;

            List<KeyValuePair<string, string>> fields;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null || !IsKind(br, "TITLE"))
                {
                    ed.WriteMessage("\nThat is not an hcwCAD-KIT title block.");
                    return;
                }
                fields = ReadFields(tr, br);
                tr.Commit();
            }

            using (var dlg = new UI.TitleFieldsForm(fields))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var values = dlg.Values();
                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    WriteFields(tr, br, values);
                    tr.Commit();
                }
                ed.WriteMessage("\nTITLEFIELDS: title block updated.");
            }
        }

        [CommandMethod("TITLENOTES")]
        public void NotesLibrary()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            using (var dlg = new UI.TitleNotesForm())
            {
                var result = dlg.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK && result != System.Windows.Forms.DialogResult.Yes) return;
                if (string.IsNullOrWhiteSpace(dlg.SelectedName))
                {
                    ed.WriteMessage("\nGive the note set a name.");
                    return;
                }
                TitleNoteLibrary.Current = dlg.SelectedName;
                if (result == System.Windows.Forms.DialogResult.OK)
                    PlaceNotes(ed, db, dlg.SelectedName, dlg.SelectedBody);
                else
                    UpdatePickedNotes(ed, db, dlg.SelectedName, dlg.SelectedBody);
            }
        }

        [CommandMethod("TITLENOTE")]
        public void PlaceCurrentNotes()
        {
            TitleNoteLibrary.Ensure();
            string name = TitleNoteLibrary.Current;
            if (string.IsNullOrWhiteSpace(name)) name = "CONSTRUCTION NOTES";
            PlaceNotes(Util.Ed, Util.Db, name, TitleNoteLibrary.Get(name));
        }

        [CommandMethod("TITLENOTESAVE")]
        public void SaveNotes()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var opt = new PromptEntityOptions("\nSelect the notes block to save: ");
            opt.SetRejectMessage("\nSelect an hcwCAD-KIT notes block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(opt);
            if (per.Status != PromptStatus.OK) return;

            string existing, body;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null || !IsKind(br, "NOTES"))
                {
                    ed.WriteMessage("\nThat is not an hcwCAD-KIT notes block.");
                    return;
                }
                existing = KindValue(br, 2);
                body = ReadNoteBody(tr, br);
                tr.Commit();
            }

            var pso = new PromptStringOptions("\nSave these notes as: ") { AllowSpaces = true, DefaultValue = existing ?? "GENERAL NOTES", UseDefaultValue = true };
            var name = ed.GetString(pso);
            if (name.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(name.StringResult)) return;
            TitleNoteLibrary.SaveOne(name.StringResult.Trim(), body);
            ed.WriteMessage("\nTITLENOTESAVE: saved \"" + name.StringResult.Trim() + "\". It will appear in Notes the next time the ribbon is built, and is available now from Notes.");
        }

        [CommandMethod("FIELDS")]
        public void EditFieldList()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            using (var dlg = new UI.SheetFieldsForm(SheetFieldLibrary.Load()))
            {
                var result = dlg.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK && result != System.Windows.Forms.DialogResult.Yes) return;
                var fields = dlg.Read();
                if (fields.Count == 0)
                {
                    ed.WriteMessage("\nAdd at least one field.");
                    return;
                }
                SheetFieldLibrary.Save(fields);
                if (result == System.Windows.Forms.DialogResult.OK)
                    PlaceFields(ed, db, fields);
                else
                    UpdatePickedFields(ed, db, fields);
            }
        }

        [CommandMethod("FIELDSEDIT")]
        public void EditPlacedFields()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            var opt = new PromptEntityOptions("\nSelect the fields block to edit: ");
            opt.SetRejectMessage("\nSelect an hcwCAD-KIT fields block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(opt);
            if (per.Status != PromptStatus.OK) return;

            List<KeyValuePair<string, string>> current;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null || !IsKind(br, "FIELDS"))
                {
                    ed.WriteMessage("\nThat is not an hcwCAD-KIT fields block.");
                    return;
                }
                current = ParseFieldLines(ReadNoteBody(tr, br));
                tr.Commit();
            }
            if (current.Count == 0) current = SheetFieldLibrary.Load();

            using (var dlg = new UI.SheetFieldsForm(current))
            {
                var result = dlg.ShowDialog();
                if (result != System.Windows.Forms.DialogResult.OK && result != System.Windows.Forms.DialogResult.Yes) return;
                var fields = dlg.Read();
                SheetFieldLibrary.Save(fields);
                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br == null || !IsKind(br, "FIELDS"))
                    {
                        ed.WriteMessage("\nThat is not an hcwCAD-KIT fields block.");
                        return;
                    }
                    Point3d at = br.Position;
                    br.UpgradeOpen();
                    br.Erase();
                    double mm = Util.MmToDrawingUnits(1.0);
                    PrepareLayers(tr, db);
                    EnsureRegApp(tr, db);
                    InsertFields(tr, db, at, mm, fields);
                    tr.Commit();
                }
            }
            ed.WriteMessage("\nFields updated.");
        }

        private static void PlaceFields(Editor ed, Database db, List<KeyValuePair<string, string>> fields)
        {
            var ppr = ed.GetPoint("\nPick where these fields should sit (they can be moved afterwards): ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d ins = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                double mm = Util.MmToDrawingUnits(1.0);
                PrepareLayers(tr, db);
                EnsureRegApp(tr, db);
                InsertFields(tr, db, ins, mm, fields);
                tr.Commit();
            }
            ed.WriteMessage("\nFields placed. Use MOVE to shift them, or Edit Field to change them.");
        }

        private static void UpdatePickedFields(Editor ed, Database db, List<KeyValuePair<string, string>> fields)
        {
            var opt = new PromptEntityOptions("\nSelect the fields block to rewrite: ");
            opt.SetRejectMessage("\nSelect an hcwCAD-KIT fields block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(opt);
            if (per.Status != PromptStatus.OK) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null || !IsKind(br, "FIELDS"))
                {
                    ed.WriteMessage("\nThat is not an hcwCAD-KIT fields block.");
                    return;
                }
                Point3d at = br.Position;
                br.UpgradeOpen();
                br.Erase();
                double mm = Util.MmToDrawingUnits(1.0);
                PrepareLayers(tr, db);
                EnsureRegApp(tr, db);
                InsertFields(tr, db, at, mm, fields);
                tr.Commit();
            }
            ed.WriteMessage("\nFields updated.");
        }

        private static void InsertFields(Transaction tr, Database db, Point3d ins, double mm, IList<KeyValuePair<string, string>> fields)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var def = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
            ObjectId defId = bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);

            double w = 120;
            double h = 8 + fields.Count * 6;
            AddRect(tr, def, 0, 0, w * mm, h * mm, LayerFields);
            var lines = new List<string>();
            foreach (var field in fields)
                lines.Add(field.Key.Trim() + ": " + (field.Value ?? "").Replace("\r", " ").Replace("\n", " "));
            var mt = new MText
            {
                Location = new Point3d(2 * mm, (h - 3) * mm, 0),
                TextHeight = 2.2 * mm,
                Width = (w - 4) * mm,
                Contents = string.Join("\\P", lines),
                Layer = LayerFields
            };
            def.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);

            var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(ins, defId) { Layer = LayerFields };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            Tag(br, "FIELDS");
        }

        private static List<KeyValuePair<string, string>> ParseFieldLines(string body)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(body)) return list;
            foreach (var raw in body.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int cut = line.IndexOf(':');
                if (cut < 0) list.Add(new KeyValuePair<string, string>(line, ""));
                else list.Add(new KeyValuePair<string, string>(line.Substring(0, cut).Trim(), line.Substring(cut + 1).Trim()));
            }
            return list;
        }

        private static void PlaceNotes(Editor ed, Database db, string name, string body)
        {
            var ppr = ed.GetPoint("\nPick where this note set should sit (it can be moved afterwards): ");
            if (ppr.Status != PromptStatus.OK) return;
            Point3d ins = ppr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                double mm = Util.MmToDrawingUnits(1.0);
                PrepareLayers(tr, db);
                EnsureRegApp(tr, db);
                InsertNotes(tr, db, ins, mm, name, body);
                tr.Commit();
            }
            ed.WriteMessage("\nNotes placed: " + name + ". Use MOVE to shift them on the sheet.");
        }

        private static void UpdatePickedNotes(Editor ed, Database db, string name, string body)
        {
            var opt = new PromptEntityOptions("\nSelect the notes block to rewrite: ");
            opt.SetRejectMessage("\nSelect an hcwCAD-KIT notes block.");
            opt.AddAllowedClass(typeof(BlockReference), true);
            var per = ed.GetEntity(opt);
            if (per.Status != PromptStatus.OK) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(per.ObjectId, OpenMode.ForRead) as BlockReference;
                if (br == null || !IsKind(br, "NOTES"))
                {
                    ed.WriteMessage("\nThat is not an hcwCAD-KIT notes block.");
                    return;
                }
                WriteNoteBody(tr, br, name, body);
                Tag(br, "NOTES", name);
                tr.Commit();
            }
            ed.WriteMessage("\nNotes updated: " + name);
        }

        private static void InsertNotes(Transaction tr, Database db, Point3d ins, double mm, string name, string body)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var def = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
            ObjectId defId = bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);

            double w = 95, h = 55;
            AddRect(tr, def, 0, 0, w * mm, h * mm, LayerNotes);
            var title = new DBText
            {
                Position = new Point3d(2 * mm, (h - 5) * mm, 0),
                Height = 2.8 * mm,
                TextString = name,
                Layer = LayerNotes
            };
            def.AppendEntity(title);
            tr.AddNewlyCreatedDBObject(title, true);

            var mt = new MText
            {
                Location = new Point3d(2 * mm, (h - 8) * mm, 0),
                TextHeight = 2.2 * mm,
                Width = (w - 4) * mm,
                Contents = (body ?? "").Replace("\r", "").Replace("\n", "\\P"),
                Layer = LayerNotes
            };
            def.AppendEntity(mt);
            tr.AddNewlyCreatedDBObject(mt, true);

            var ms = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(ins, defId) { Layer = LayerNotes };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            Tag(br, "NOTES", name);
        }

        private static string ReadNoteBody(Transaction tr, BlockReference br)
        {
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in def)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is MText mt)
                    return (mt.Contents ?? "").Replace("\\P", "\n").Replace("\\p", "\n");
            }
            return "";
        }

        private static void WriteNoteBody(Transaction tr, BlockReference br, string name, string body)
        {
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in def)
            {
                var obj = tr.GetObject(id, OpenMode.ForWrite);
                if (obj is DBText t && t.Height > 0) t.TextString = name;
                else if (obj is MText mt)
                    mt.Contents = (body ?? "").Replace("\r", "").Replace("\n", "\\P");
            }
        }

        internal static ObjectId EnsureTitleDefinition(Transaction tr, Database db, double mm)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(TitleBlockName)) return bt[TitleBlockName];
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = TitleBlockName, Origin = Point3d.Origin };
            ObjectId id = bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            DrawTitle(tr, def, mm);
            return id;
        }

        private static void DrawTitle(Transaction tr, BlockTableRecord def, double mm)
        {
            double S(double v) => v * mm;
            void Box(double x1, double y1, double x2, double y2, string layer) =>
                AddRect(tr, def, S(x1), S(y1), S(x2), S(y2), layer);
            void Cap(string text, double x, double y, double h) =>
                AddText(tr, def, text, S(x), S(y), S(h), LayerTitle);
            void Field(string tag, string prompt, string value, double x, double y, double h) =>
                AddAttr(tr, def, tag, prompt, value, S(x), S(y), S(h));

            Box(0, 0, 420, 297, LayerBorder);
            Box(5, 5, 415, 292, LayerBorder);
            Box(5, 5, 415, 122, LayerTitle);
            Box(5, 5, 415, 20, LayerTitle);
            Box(5, 20, 415, 36, LayerTitle);
            Box(5, 36, 415, 52, LayerTitle);
            Box(5, 52, 205, 122, LayerTitle);

            Cap("STABILITY CERTIFICATE", 10, 14, 1.8);
            Field("STABILITY", "Stability certificate",
                "THE PROPOSED STRUCTURE IS DESIGNED AS PER I.S. CODE AND CAN BARE THE LOAD OF THE SUPER STRUCTURE",
                78, 14, 1.8);

            Cap("F.A.R. ACH %", 10, 26, 1.8);
            Field("FAR_ACH", "F.A.R. achieved %", "--", 52, 26, 2.0);
            Cap("PERM %", 88, 26, 1.8);
            Field("FAR_PERM", "F.A.R. permissible %", "--", 118, 26, 2.0);
            Cap("GROUND COVER", 154, 26, 1.8);
            Field("GC_ACH", "Ground cover achieved sq m", "--", 214, 26, 2.0);
            Field("GC_PCT", "Ground cover %", "--", 250, 26, 2.0);
            Field("GC_PERM", "Ground cover permissible sq m", "--", 286, 26, 2.0);

            Cap("PID", 10, 42, 1.8);
            Field("PID", "PID", "--", 28, 42, 2.0);
            Cap("SITE AREA", 90, 42, 1.8);
            Field("SITE_AREA", "Site area", "--", 132, 42, 2.0);
            Cap("OWNER'S SIGNATURE", 190, 42, 1.8);
            Field("OWNER", "Owner's signature", "--", 280, 42, 2.2);

            Cap("AREA STATEMENT", 10, 114, 2.2);
            Cap("FLOOR", 10, 106, 1.6);
            Cap("DEDUCTION", 58, 106, 1.6);
            Cap("NET", 112, 106, 1.6);
            Cap("GROSS", 156, 106, 1.6);

            string[] floors = { "GROUND", "FIRST", "SECOND", "TERRACE" };
            for (int i = 0; i < 4; i++)
            {
                double y = 96 - i * 9;
                string n = (i + 1).ToString();
                Field("FL" + n, "Floor " + n, floors[i], 10, y, 2.0);
                Field("DED" + n, "Deduction " + n, "--", 58, y, 2.0);
                Field("NET" + n, "Built-up net " + n, "--", 112, y, 2.0);
                Field("GROSS" + n, "Built-up gross " + n, "--", 156, y, 2.0);
            }
            Cap("TOTAL", 10, 58, 1.8);
            Field("TOT_DED", "Total deduction", "--", 58, 58, 2.0);
            Field("TOT_NET", "Total built-up net", "--", 112, 58, 2.0);
            Field("TOT_GROSS", "Total built-up gross", "--", 156, 58, 2.0);

            Cap("BUILDING PERMIT DRAWING", 212, 114, 2.4);
            Cap("PROJECT TITLE", 212, 104, 1.6);
            Field("PROJECT_TITLE", "Project title", "PROJECT TITLE", 212, 96, 2.4);
            Cap("DRAWING TITLE", 212, 86, 1.6);
            Field("DRAWING_TITLE", "Drawing title", "DRAWING TITLE", 212, 78, 2.2);
            Cap("DRAWING #", 212, 68, 1.6);
            Field("DRAWING_NO", "Drawing number", "--", 258, 68, 2.0);
            Cap("SIZE", 320, 68, 1.6);
            Field("SIZE", "Sheet size", "A3", 344, 68, 2.0);
            Cap("PLOT USE", 212, 60, 1.6);
            Field("PLOT_USE", "Plot use", "--", 258, 60, 2.0);
            Cap("CONSULTING ARCHITECT", 212, 54, 1.6);
            Field("ARCHITECT", "Consulting architect", "--", 300, 54, 2.0);
        }

        private static void AddRect(Transaction tr, BlockTableRecord def, double x1, double y1, double x2, double y2, string layer)
        {
            var pl = new Polyline { Layer = layer, Closed = true };
            pl.AddVertexAt(0, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x2, y1), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x2, y2), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1, y2), 0, 0, 0);
            def.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void AddText(Transaction tr, BlockTableRecord def, string text, double x, double y, double h, string layer)
        {
            var t = new DBText
            {
                Position = new Point3d(x, y, 0),
                Height = h,
                TextString = text,
                Layer = layer
            };
            def.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        private static void AddAttr(Transaction tr, BlockTableRecord def, string tag, string prompt, string value, double x, double y, double h)
        {
            var ad = new AttributeDefinition
            {
                Position = new Point3d(x, y, 0),
                Height = h,
                Tag = tag,
                Prompt = prompt,
                TextString = value,
                Layer = LayerTitle,
                LockPositionInBlock = true
            };
            def.AppendEntity(ad);
            tr.AddNewlyCreatedDBObject(ad, true);
        }

        internal static void AddAttributes(Transaction tr, BlockReference br)
        {
            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in def)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, br.BlockTransform);
                br.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);
            }
        }

        private static List<KeyValuePair<string, string>> ReadFields(Transaction tr, BlockReference br)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                found[ar.Tag] = ar.TextString;
            }
            var list = new List<KeyValuePair<string, string>>();
            foreach (var tag in FieldOrder)
                if (found.ContainsKey(tag)) list.Add(new KeyValuePair<string, string>(tag, found[tag]));
            foreach (var kv in found)
                if (!FieldOrder.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                    list.Add(kv);
            return list;
        }

        private static void WriteFields(Transaction tr, BlockReference br, Dictionary<string, string> values)
        {
            foreach (ObjectId id in br.AttributeCollection)
            {
                var ar = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                if (values.TryGetValue(ar.Tag, out string value))
                    ar.TextString = value ?? "";
            }
        }

        internal static void PrepareLayers(Transaction tr, Database db)
        {
            Util.EnsureLayer(tr, db, LayerBorder, 8, "Continuous", LineWeight.LineWeight050);
            Util.EnsureLayer(tr, db, LayerTitle, 8, "Continuous", LineWeight.LineWeight025);
            Util.EnsureLayer(tr, db, LayerNotes, 2, "Continuous", LineWeight.LineWeight018);
            Util.EnsureLayer(tr, db, LayerFields, 8, "Continuous", LineWeight.LineWeight018);
        }

        internal static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(AppName)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = AppName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        internal static void Tag(Entity ent, string kind, string extra = null)
        {
            var buffer = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, kind));
            if (!string.IsNullOrEmpty(extra))
                buffer.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, extra));
            ent.XData = buffer;
        }

        internal static bool IsKind(Entity ent, string kind)
        {
            var data = ent.GetXDataForApplication(AppName);
            if (data == null) return false;
            var values = data.AsArray();
            return values.Length > 1 && string.Equals(values[1].Value as string, kind, StringComparison.OrdinalIgnoreCase);
        }

        private static string KindValue(Entity ent, int index)
        {
            var data = ent.GetXDataForApplication(AppName);
            if (data == null) return null;
            var values = data.AsArray();
            return values.Length > index ? values[index].Value as string : null;
        }
    }
}
