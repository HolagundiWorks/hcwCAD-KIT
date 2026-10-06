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

        private const string StoreDictionary = "HCW_AREA", StoreRecord = "CONFIG", KindTable = "AREATABLE";

        private static string _readFrom = "Selection";

        private static void Run(Editor ed, Database db)
        {
            double upm = Util.MmToDrawingUnits(1000.0);          // drawing units per metre
            double toSqm = 1.0 / (upm * upm);

            var ro = new PromptKeywordOptions("\nRead the outlines from [Selection/Layers] <" + _readFrom + ">: ", "Selection Layers") { AllowNone = true };
            ro.Keywords.Default = _readFrom;
            var rr = ed.GetKeywords(ro);
            if (rr.Status == PromptStatus.OK) _readFrom = rr.StringResult;
            else if (rr.Status != PromptStatus.None) return;
            bool byLayer = _readFrom == "Layers";

            var cfg = new AreaConfig();
            // Site.
            if (!AskSource(ed, byLayer, "the site boundary", Settings.Get("AreaSiteLayer", "BP-SITE-BOUNDARY"), toSqm, out cfg.Site)) return;

            // Floors: named and counted from the levels kept in the drawing (MSCHED, Floors tab) when there are any.
            var levels = LevelStore.Load();
            bool fromLevels = false;
            if (levels.Count > 0)
            {
                var lo = new PromptKeywordOptions("\nFloors from [Levels/Typed] <Levels> (" + string.Join(", ", levels.Select(l => l.Name)) + "): ", "Levels Typed") { AllowNone = true };
                lo.Keywords.Default = "Levels";
                var lr = ed.GetKeywords(lo);
                if (lr.Status != PromptStatus.OK && lr.Status != PromptStatus.None) return;
                fromLevels = lr.Status == PromptStatus.None || lr.StringResult == "Levels";
            }
            if (fromLevels) _floors = Math.Min(12, levels.Count);
            else
            {
                var count = ed.GetInteger(new PromptIntegerOptions("\nNumber of floors <" + _floors + ">: ")
                    { AllowNegative = false, AllowZero = false, LowerLimit = 1, UpperLimit = 12, DefaultValue = _floors, UseDefaultValue = true });
                if (count.Status != PromptStatus.OK) return;
                _floors = count.Value;
            }

            for (int i = 0; i < _floors; i++)
            {
                string name;
                if (fromLevels) name = levels[i].Name.Trim().ToUpperInvariant();
                else
                {
                    string def = DefaultNames[i];
                    var nr = ed.GetString(new PromptStringOptions("\nName of floor " + (i + 1) + " <" + def + ">: ")
                        { AllowSpaces = true, DefaultValue = def, UseDefaultValue = true });
                    if (nr.Status != PromptStatus.OK) return;
                    name = nr.StringResult.Trim().ToUpperInvariant();
                    if (name.Length == 0) name = def;
                }
                if (name.Length == 0) name = DefaultNames[i];

                var floor = new AreaFloorSource { Name = name };
                if (i > 0)
                {
                    // a repeated floor reuses the outlines of the one before
                    var so = new PromptKeywordOptions("\n" + name + " has the same outlines as " + cfg.Floors[i - 1].Name + " [Yes/No] <No>: ", "Yes No") { AllowNone = true };
                    so.Keywords.Default = "No";
                    var sr = ed.GetKeywords(so);
                    if (sr.Status == PromptStatus.Cancel) return;
                    if (sr.Status == PromptStatus.OK && sr.StringResult == "Yes")
                    {
                        floor.Gross = AreaSource.Decode(cfg.Floors[i - 1].Gross.Encode());
                        floor.Deduction = AreaSource.Decode(cfg.Floors[i - 1].Deduction.Encode());
                        cfg.Floors.Add(floor);
                        continue;
                    }
                }
                if (!AskSource(ed, byLayer, "the built-up outline(s) of " + name, Settings.Get("AreaGrossLayer", "BP-BUILDING-CUT"), toSqm, out floor.Gross)) return;
                if (!AskSource(ed, byLayer, "the areas of " + name + " left out of the FAR - shafts, ducts, lift", "", toSqm, out floor.Deduction)) return;
                cfg.Floors.Add(floor);
            }

            AreaStatement stmt;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                stmt = Compute(tr, db, cfg, toSqm);
                tr.Commit();
            }
            Report(ed, stmt);

            // Title blocks anywhere in the drawing.
            var blocks = FindTitleBlocks(db);
            cfg.FillTitleBlocks = false;
            if (blocks.Count > 0)
            {
                var ko = new PromptKeywordOptions("\nFill the area fields of " + blocks.Count + " title block(s) [Yes/No] <Yes>: ", "Yes No") { AllowNone = true };
                ko.Keywords.Default = "Yes";
                var kr = ed.GetKeywords(ko);
                if (kr.Status == PromptStatus.Cancel) return;
                cfg.FillTitleBlocks = kr.Status != PromptStatus.OK || kr.StringResult == "Yes";
            }
            else ed.WriteMessage("\nHCWAREASTMT: no hcwCAD-KIT title block found (SHEETFIT or TITLEBLOCK places one); the numbers are above.");

            // Optional table.
            var pr = ed.GetPoint(new PromptPointOptions("\nTop-left of an area table (Enter to skip): ") { AllowNone = true });
            if (pr.Status == PromptStatus.OK)
            {
                var at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                cfg.TableAt = new P2(at.X, at.Y);
            }
            else if (pr.Status != PromptStatus.None) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                DrawingStore.Write(tr, db, StoreDictionary, StoreRecord, cfg.ToLines());
                Apply(tr, db, cfg, stmt, inModel: true);
                tr.Commit();
            }
            LoadWatch(db);
            if (cfg.FillTitleBlocks) ed.WriteMessage("\nHCWAREASTMT: title block fields filled." + (stmt.Floors.Count > 4 ? " The title block has 4 floor rows; floors 4 and up are summed in the last row." : ""));
            ed.WriteMessage("\nHCWAREASTMT: the statement is saved with the drawing and updates itself when its outlines change while live updates are on (HCWLIVE).");
        }

        /// <summary>Asks where a set of outlines comes from: a selection (kept by handle) or layer names. False when cancelled.</summary>
        private static bool AskSource(Editor ed, bool byLayer, string what, string defaultLayer, double toSqm, out AreaSource source)
        {
            source = new AreaSource();
            if (byLayer)
            {
                string prompt = "\nLayer(s) holding " + what + ", separated by ;" + (defaultLayer.Length > 0 ? " <" + defaultLayer + ">" : " (Enter if none)") + ": ";
                var r = ed.GetString(new PromptStringOptions(prompt) { AllowSpaces = false, DefaultValue = defaultLayer, UseDefaultValue = defaultLayer.Length > 0 });
                if (r.Status == PromptStatus.None) return true;
                if (r.Status != PromptStatus.OK) return false;
                source.Layers.AddRange(r.StringResult.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0));
                return true;
            }
            var s = SumAreas(ed, "\nSelect " + what + " (closed polylines; Enter if none): ", toSqm);
            if (s == null) return false;
            source.Handles.AddRange(s.Handles);
            return true;
        }

        /// <summary>The areas, in square metres, of the closed polylines a source names (by handle, or on layers in model space), and the area on each layer.</summary>
        private static double Measure(Transaction tr, Database db, AreaSource src, double toSqm, Dictionary<string, double> byLayer)
        {
            double total = 0;
            Action<Polyline> add = pl =>
            {
                if (!pl.Closed) return;
                double a = Math.Abs(pl.Area) * toSqm;
                total += a;
                double have;
                byLayer.TryGetValue(pl.Layer, out have);
                byLayer[pl.Layer] = have + a;
            };
            foreach (var h in src.Handles)
            {
                ObjectId id;
                if (!db.TryGetObjectId(new Handle(h), out id) || id.IsErased) continue;
                var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                if (pl != null) add(pl);
            }
            if (src.Layers.Count > 0)
            {
                var layers = new HashSet<string>(src.Layers, StringComparer.OrdinalIgnoreCase);
                var model = (BlockTableRecord)tr.GetObject(((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in model)
                {
                    if (id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                    var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                    if (pl != null && !pl.IsErased && layers.Contains(pl.Layer)) add(pl);
                }
            }
            return total;
        }

        /// <summary>Works the statement out from what the configuration points at now, </summary>
        internal static AreaStatement Compute(Transaction tr, Database db, AreaConfig cfg, double toSqm)
        {
            double site = Measure(tr, db, cfg.Site, toSqm, new Dictionary<string, double>());
            var floors = new List<FloorInput>();
            foreach (var f in cfg.Floors)
            {
                double gross = Measure(tr, db, f.Gross, toSqm, new Dictionary<string, double>());
                var byLayer = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                double all = Measure(tr, db, f.Deduction, toSqm, byLayer);
                double deduction = all;                  // every outline picked, or on the layers named, counts in full
                floors.Add(new FloorInput { Name = f.Name, Gross = gross, Deduction = deduction });
            }
            var stmt = AreaStatement.Compute(floors, site);
            return stmt;
        }

        /// <summary>Writes the statement to the title blocks and redraws the area table (replacing the one drawn before).</summary>
        private static void Apply(Transaction tr, Database db, AreaConfig cfg, AreaStatement stmt, bool inModel)
        {
            if (cfg.FillTitleBlocks)
            {
                var fields = stmt.ToFields(4);
                foreach (var id in FindTitleBlocks(db))
                    TitleBlockCommands.WriteFields(tr, (BlockReference)tr.GetObject(id, OpenMode.ForRead), fields);
            }
            if (!cfg.TableAt.HasValue) return;

            // The table lives in model space, so it can be redrawn from a layout too.
            TitleBlockCommands.EnsureRegApp(tr, db);
            var modelId = ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];
            var space = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForWrite);
            foreach (ObjectId id in space)
            {
                var old = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (old == null || old.IsErased || !TitleBlockCommands.IsKind(old, KindTable)) continue;
                old.UpgradeOpen(); old.Erase();
            }
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
            Util.EnsureHcwLayer(tr, db, "AN-TEXT");
            var at = cfg.TableAt.Value;
            var ids = MeasureCommands.DrawTable(tr, db, new Point3d(at.X, at.Y - 2.5 * h, 0), new[] { "Floor", "Gross (sq m)", "Deduction", "Net" }, rows, h, "AN-TEXT", modelId);
            var title = new DBText { Height = h * 1.2, TextString = "AREA STATEMENT", Layer = "AN-TEXT", Position = new Point3d(at.X, at.Y - 1.2 * h, 0) };
            ids.Add(space.AppendEntity(title));
            tr.AddNewlyCreatedDBObject(title, true);
            foreach (var id in ids) TitleBlockCommands.Tag((Entity)tr.GetObject(id, OpenMode.ForWrite), KindTable);
        }

        // ------------------------------------------------------------------ live updates

        internal static readonly Dictionary<Database, KeyValuePair<HashSet<string>, HashSet<long>>> Watched =
            new Dictionary<Database, KeyValuePair<HashSet<string>, HashSet<long>>>();

        private static AreaConfig ReadConfig(Transaction tr, Database db) => AreaConfig.FromLines(DrawingStore.Read(tr, db, StoreDictionary, StoreRecord));

        /// <summary>Notes which layers and outlines the saved statement reads, so the live service can tell when one of them changes.</summary>
        internal static void LoadWatch(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var cfg = ReadConfig(tr, db);
                if (cfg == null) Watched.Remove(db);
                else Watched[db] = new KeyValuePair<HashSet<string>, HashSet<long>>(cfg.AllLayers(), cfg.AllHandles());
                tr.Commit();
            }
        }

        /// <summary>Works the saved statement out again and rewrites the title blocks and table. Does nothing when no statement was saved.</summary>
        internal static void LiveRefresh(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            var db = doc.Database;
            double upm = Util.MmToDrawingUnits(1000.0);
            bool inModel = string.Equals(LayoutManager.Current.CurrentLayout, "Model", StringComparison.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var cfg = ReadConfig(tr, db);
                if (cfg == null) return;
                var stmt = Compute(tr, db, cfg, 1.0 / (upm * upm));
                Apply(tr, db, cfg, stmt, inModel);
                tr.Commit();
            }
        }

        private class Sum { public double Sqm; public int Used, Skipped; public List<long> Handles = new List<long>(); }

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
                    sum.Handles.Add(pl.Handle.Value);
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
