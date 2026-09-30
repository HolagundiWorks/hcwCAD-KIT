using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Electrical layout automation.
    ///
    /// Blocks: which block is a switchboard, a light point and a fan point is chosen with ELBLOCKS (saved in the drawing);
    /// until it is, the names SB, LP and FP from the settings are used (* matches anything).
    /// ELBLOCKS                    map the blocks: which are switchboards, light points and fan points.
    /// SBNUM, LPNUM, FPNUM, ELNUM  give every block an ID (SB-01, LP-02 ...) kept on the block itself (extended data, and the
    ///                             ID, NUMBER and TAG attributes when the block has them), so it follows the block when moved,
    ///                             and write the ID as text beside the block so it can be read on the drawing.
    /// ELLAYERS                    choose the layers the wiring lines are drawn on (one set for lights, one for fans).
    /// ELCONNECT                   works out from the wiring which points reach which board, and flags what is not wired.
    /// ELSCHEDULE                  draws the connection schedule as a table (lights, fans, or both).
    /// ELUPDATE                    numbers new blocks, moves labels, re-checks the wiring and redraws the schedules already drawn.
    ///
    /// Connections come from geometry only: a wire (line, polyline, arc) reaches a block when one of its vertices lies within
    /// ElectricalSnapMm of the block's extents. Wires that meet end to end, or in a T, are one run. A point is a junction (a run
    /// from one light to the next carries on to the board); a board is a terminal (two runs that only meet at a board stay separate).
    /// </summary>
    public class ElectricalCommands
    {
        private const string AppName = "HCW_ELEC";
        private const string StoreDictionary = "HCW_ELEC";
        private const string WiringRecord = "WIRING";
        private const string TableLayer = "EL-TABLE";
        private const string FlagLayer = "EL-CHECK";

        private static double _scale = 100;
        private static string _which = "Both";
        private static string _layout = "Board";
        private static string _renumber = "Keep";

        private class Kind
        {
            public string Code, Title, Label, Setting;
        }

        private static readonly Kind Board = new Kind { Code = "SB", Title = "SWITCHBOARD", Label = "Switchboard", Setting = "ElectricalBoardBlocks" };
        private static readonly Kind Light = new Kind { Code = "LP", Title = "LIGHTING", Label = "Light", Setting = "ElectricalLightBlocks" };
        private static readonly Kind Fan = new Kind { Code = "FP", Title = "FAN", Label = "Fan", Setting = "ElectricalFanBlocks" };
        private static readonly Kind[] PointKinds = { Light, Fan };
        private static readonly Kind[] AllKinds = { Board, Light, Fan };

        private const string BlocksRecord = "BLOCKS";

        private static string[] DefaultPatterns(Kind kind)
            => Settings.Get(kind.Setting, kind.Code).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The block names chosen with ELBLOCKS for each kind code (SB, LP, FP); empty lists when nothing was chosen.</summary>
        private static Dictionary<string, List<string>> LoadBlockMap(Transaction tr, Database db)
        {
            var map = AllKinds.ToDictionary(k => k.Code, k => new List<string>());
            foreach (var line in DrawingStore.Read(tr, db, StoreDictionary, BlocksRecord))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (map.ContainsKey(p[0])) map[p[0]] = p.Skip(1).Where(n => n.Length > 0).ToList();
            }
            return map;
        }

        /// <summary>The names that count as this kind: the ones chosen for the drawing, or the settings when none were chosen.</summary>
        private static string[] PatternsFor(Dictionary<string, List<string>> map, Kind kind)
            => map[kind.Code].Count > 0 ? map[kind.Code].ToArray() : DefaultPatterns(kind);

        /// <summary>One switchboard or point block found in the drawing.</summary>
        private class ElBlock
        {
            public ObjectId Id;
            public Kind Kind;
            public string BlockName = "";
            public string Existing = "";
            public string Assigned = "";
            public Box Box;
            /// <summary>The block has an ID attribute (filled in whenever the ID is written).</summary>
            public bool HasIdAttribute;
            /// <summary>That attribute is visible, so the ID already shows beside the block without separate text.</summary>
            public bool IdAttributeVisible;
            public string CurrentId => Assigned.Length > 0 ? Assigned : Existing;
        }

        // ------------------------------------------------------------------ mapping the blocks

        [CommandMethod("ELBLOCKS")]
        public void ElBlocks()
        {
            var ed = Util.Ed;
            var map = AskBlocks(ed, Util.Db);
            if (map == null) return;
            foreach (var kind in AllKinds)
                ed.WriteMessage("\n  " + kind.Label + (kind == Board ? "es" : " points") + ": " + (map[kind.Code].Count == 0 ? "(none: settings names " + string.Join("/", DefaultPatterns(kind)) + ")" : string.Join(", ", map[kind.Code])));
            ed.WriteMessage("\nELBLOCKS: saved in the drawing. Run ELNUM to number them.");
        }

        /// <summary>
        /// When no block in the drawing is a switchboard, light or fan point by the saved mapping or the default names,
        /// asks which blocks they are. False when the user cancels that.
        /// </summary>
        private static bool EnsureBlocks(Editor ed, Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bool nothingMapped = AllKinds.All(k => LoadBlockMap(tr, db)[k.Code].Count == 0);
                bool nothingFound = ReadBlocks(tr, db).Count == 0;
                tr.Commit();
                if (!(nothingMapped && nothingFound)) return true;
            }
            ed.WriteMessage("\nNo block is named " + string.Join("/", DefaultPatterns(Board)) + ", " + string.Join("/", DefaultPatterns(Light)) + " or "
                + string.Join("/", DefaultPatterns(Fan)) + ". Choose which blocks are the switchboards, light points and fan points.");
            return AskBlocks(ed, db) != null;
        }

        /// <summary>
        /// Shows every block in the drawing in three lists (switchboards, light points, fan points) and saves the choice in the
        /// drawing. "Pick from drawing" takes the blocks you select. A block has one role. Null on Cancel.
        /// </summary>
        private static Dictionary<string, List<string>> AskBlocks(Editor ed, Database db)
        {
            var names = new List<string>();
            Dictionary<string, List<string>> map;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    var rec = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (rec.IsAnonymous || rec.IsLayout || rec.IsFromExternalReference || rec.IsDependent) continue;
                    names.Add(rec.Name);
                }
                map = LoadBlockMap(tr, db);
                tr.Commit();
            }
            foreach (var kind in AllKinds)
                map[kind.Code] = map[kind.Code].Where(n => names.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
            // a drawing with nothing chosen yet starts from the default names
            foreach (var kind in AllKinds)
                if (map[kind.Code].Count == 0)
                    map[kind.Code] = names.Where(n => ElectricalNumbering.NameMatches(n, DefaultPatterns(kind))).ToList();

            while (true)
            {
                var lists = new List<KeyValuePair<string, List<string>>>
                {
                    new KeyValuePair<string, List<string>>("Switchboards", map["SB"]),
                    new KeyValuePair<string, List<string>>("Light points", map["LP"]),
                    new KeyValuePair<string, List<string>>("Fan points", map["FP"])
                };
                using (var dlg = new UI.LayerListsForm("hcwCAD-KIT — Electrical blocks",
                    "Tick which blocks are the switchboards, the light points and the fan points. A block has one role. Use Pick from drawing to select a block in the drawing instead of finding its name.",
                    names, lists))
                {
                    var result = dlg.ShowDialog();
                    var read = dlg.Read();
                    map["SB"] = read[0];
                    map["LP"] = read[1].Where(n => !read[0].Contains(n)).ToList();
                    map["FP"] = read[2].Where(n => !read[0].Contains(n) && !read[1].Contains(n)).ToList();
                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        PickBlockNames(ed, db, map, AllKinds[dlg.PickIndex]);
                        continue;
                    }
                    if (result != System.Windows.Forms.DialogResult.OK) return null;
                    break;
                }
            }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                DrawingStore.Write(tr, db, StoreDictionary, BlocksRecord, AllKinds.Select(k => Fields.Join(new[] { k.Code }.Concat(map[k.Code]).ToArray())));
                tr.Commit();
            }
            return map;
        }

        private static void PickBlockNames(Editor ed, Database db, Dictionary<string, List<string>> map, Kind kind)
        {
            var filter = new SelectionFilter(new[] { new TypedValue(0, "INSERT") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the " + kind.Label.ToLowerInvariant() + (kind == Board ? "" : " point") + " blocks: " }, filter);
            if (psr.Status != PromptStatus.OK) return;
            var picked = new List<string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject so in psr.Value)
                {
                    var br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;
                    string name = BlockOpenings.EffectiveName(tr, br);
                    if (!picked.Contains(name, StringComparer.OrdinalIgnoreCase)) picked.Add(name);
                }
                tr.Commit();
            }
            foreach (var name in picked)
            {
                foreach (var list in map.Values) list.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                map[kind.Code].Add(name);
            }
            if (picked.Count > 0) ed.WriteMessage("\nAdded: " + string.Join(", ", picked) + ".");
        }

        // ------------------------------------------------------------------ numbering

        [CommandMethod("SBNUM")] public void SbNum() => Number(new[] { Board });
        [CommandMethod("LPNUM")] public void LpNum() => Number(new[] { Light });
        [CommandMethod("FPNUM")] public void FpNum() => Number(new[] { Fan });
        [CommandMethod("ELNUM")] public void ElNum() => Number(AllKinds);

        private static void Number(Kind[] kinds)
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!EnsureBlocks(ed, db)) return;
            var opt = new PromptKeywordOptions("\nRenumber [Keep/All] <" + _renumber + ">: ") { AllowNone = true };
            opt.Keywords.Add("Keep");
            opt.Keywords.Add("All");
            var res = ed.GetKeywords(opt);
            if (res.Status == PromptStatus.OK) _renumber = res.StringResult;
            else if (res.Status != PromptStatus.None) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blocks = ReadBlocks(tr, db);
                foreach (var kind in kinds)
                {
                    var mine = blocks.Where(b => b.Kind == kind).ToList();
                    if (mine.Count == 0)
                    {
                        ed.WriteMessage("\n" + kind.Code + "NUM: no block is mapped as " + kind.Label.ToLowerInvariant() + ". Run ELBLOCKS to choose which blocks they are.");
                        continue;
                    }
                    int kept = AssignIds(tr, mine, kind, _renumber == "All");
                    ed.WriteMessage("\n" + kind.Code + "NUM: " + mine.Count + " " + kind.Label.ToLowerInvariant() + " block(s) numbered; "
                        + kept + " kept their number, " + (mine.Count - kept) + " new or changed.");
                }
                int shown = SyncLabels(tr, db, blocks);
                if (shown > 0) ed.WriteMessage("\nEach ID is written beside its block (layer " + Settings.Get("ElectricalLabelLayer", "EL-LABELS") + ").");
                tr.Commit();
            }
        }

        /// <summary>Gives every block of a kind its ID and writes it to the block. Returns how many blocks kept the ID they had.</summary>
        private static int AssignIds(Transaction tr, List<ElBlock> blocks, Kind kind, bool renumberAll)
        {
            var items = blocks.Select((b, i) => new NumberItem
            {
                Key = i, Existing = b.Existing, X = (b.Box.MinX + b.Box.MaxX) / 2, Y = (b.Box.MinY + b.Box.MaxY) / 2
            }).ToList();
            var ids = ElectricalNumbering.Assign(items, kind.Code, renumberAll);
            int kept = 0;
            EnsureRegApp(tr, Util.Db);
            for (int i = 0; i < blocks.Count; i++)
            {
                blocks[i].Assigned = ids[i];
                if (string.Equals(blocks[i].Existing, ids[i], StringComparison.OrdinalIgnoreCase)) { kept++; if (!blocks[i].HasIdAttribute) continue; }
                WriteId(tr, blocks[i], kind.Code);
            }
            return kept;
        }

        /// <summary>Numbers any block that has no ID yet (existing IDs are kept) so the other commands always have names to work with.</summary>
        private static void EnsureNumbered(Transaction tr, List<ElBlock> blocks)
        {
            foreach (var kind in AllKinds)
            {
                var mine = blocks.Where(b => b.Kind == kind).ToList();
                if (mine.Count == 0) continue;
                bool any = mine.Any(b => ElectricalNumbering.NumberOf(b.Existing, kind.Code) <= 0)
                    || mine.GroupBy(b => b.Existing, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
                if (any) AssignIds(tr, mine, kind, false);
                else foreach (var b in mine) b.Assigned = b.Existing;
            }
        }

        private static void WriteId(Transaction tr, ElBlock block, string code)
        {
            var br = (BlockReference)tr.GetObject(block.Id, OpenMode.ForWrite);
            br.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "ID"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, block.Assigned));

            // blocks that carry ID, NUMBER and TAG attributes show them
            int number = ElectricalNumbering.NumberOf(block.Assigned, code);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                string tag = (att.Tag ?? "").ToUpperInvariant();
                string wanted = tag == "ID" ? block.Assigned
                    : tag == "NUMBER" ? number.ToString(number < 100 ? "00" : "0", CultureInfo.InvariantCulture)
                    : tag == "TAG" ? code : null;
                if (wanted == null || att.TextString == wanted) continue;
                att.UpgradeOpen();
                att.TextString = wanted;
            }
            block.Existing = block.Assigned;
        }

        // ------------------------------------------------------------------ wiring layers

        [CommandMethod("ELLAYERS")]
        public void ElLayers()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (AskWiring(ed, db) == null) return;
            ed.WriteMessage("\nELLAYERS: wiring layers saved in the drawing.");
        }

        /// <summary>Wiring layers by kind code (LP, FP), as last saved in the drawing.</summary>
        private static Dictionary<string, List<string>> LoadWiring(Transaction tr, Database db)
        {
            var result = new Dictionary<string, List<string>> { { "LP", new List<string>() }, { "FP", new List<string>() } };
            foreach (var line in DrawingStore.Read(tr, db, StoreDictionary, WiringRecord))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (result.ContainsKey(p[0])) result[p[0]] = p.Skip(1).Where(n => n.Length > 0).ToList();
            }
            return result;
        }

        /// <summary>Shows the wiring dialog (lighting wires, fan wires) and saves the choice in the drawing. Null on Cancel.</summary>
        private static Dictionary<string, List<string>> AskWiring(Editor ed, Database db)
        {
            var names = new List<string>();
            Dictionary<string, List<string>> wiring;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in table)
                {
                    var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!rec.IsDependent) names.Add(rec.Name);
                }
                wiring = LoadWiring(tr, db);
                tr.Commit();
            }
            foreach (var key in wiring.Keys.ToList()) wiring[key] = wiring[key].Where(n => names.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();

            while (true)
            {
                var lists = new List<KeyValuePair<string, List<string>>>
                {
                    new KeyValuePair<string, List<string>>("Lighting wires (to light points)", wiring["LP"]),
                    new KeyValuePair<string, List<string>>("Fan wires (to fan points)", wiring["FP"])
                };
                using (var dlg = new UI.LayerListsForm("hcwCAD-KIT — Wiring layers",
                    "Tick the layers the wiring lines (lines, polylines, arcs) are drawn on. Use one layer for both if lights and fans share it: each schedule then reads only its own points.",
                    names, lists))
                {
                    var result = dlg.ShowDialog();
                    var read = dlg.Read();
                    wiring["LP"] = read[0];
                    wiring["FP"] = read[1];
                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        PickWiringLayers(ed, db, dlg.PickIndex == 0 ? wiring["LP"] : wiring["FP"], dlg.PickIndex == 0 ? "lighting" : "fan");
                        continue;
                    }
                    if (result != System.Windows.Forms.DialogResult.OK) return null;
                    break;
                }
            }

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                DrawingStore.Write(tr, db, StoreDictionary, WiringRecord, wiring.Select(w => Fields.Join(new[] { w.Key }.Concat(w.Value).ToArray())));
                tr.Commit();
            }
            return wiring;
        }

        private static void PickWiringLayers(Editor ed, Database db, List<string> into, string label)
        {
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect wiring lines for the " + label + " circuits: " });
            if (psr.Status != PromptStatus.OK) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (SelectedObject so in psr.Value)
                {
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || !table.Has(ent.Layer)) continue;
                    if (!into.Contains(ent.Layer, StringComparer.OrdinalIgnoreCase)) into.Add(ent.Layer);
                }
                tr.Commit();
            }
        }

        /// <summary>The saved wiring layers; asks for them when a kind that has points in the drawing has none.</summary>
        private static Dictionary<string, List<string>> EnsureWiring(Editor ed, Database db, List<ElBlock> blocks)
        {
            Dictionary<string, List<string>> wiring;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                wiring = LoadWiring(tr, db);
                tr.Commit();
            }
            bool missing = PointKinds.Any(k => blocks.Any(b => b.Kind == k) && wiring[k.Code].Count == 0);
            if (missing)
            {
                ed.WriteMessage("\nChoose the wiring layers first.");
                wiring = AskWiring(ed, db);
            }
            return wiring;
        }

        // ------------------------------------------------------------------ analysis

        private class Analysis
        {
            public Kind Kind;
            public List<ElBlock> Blocks = new List<ElBlock>();
            public List<ElNode> Nodes = new List<ElNode>();
            public List<ElNet> Nets = new List<ElNet>();
            public int Wires;
        }

        private static Analysis Analyse(Transaction tr, Database db, Kind kind, List<ElBlock> blocks, List<string> wiringLayers, double tolerance)
        {
            var result = new Analysis { Kind = kind };
            result.Blocks = blocks.Where(b => b.Kind == Board || b.Kind == kind).ToList();
            result.Nodes = result.Blocks.Select(b => new ElNode { Id = b.CurrentId, IsBoard = b.Kind == Board, Box = b.Box }).ToList();

            var layers = new HashSet<string>(wiringLayers, StringComparer.OrdinalIgnoreCase);
            var wires = new List<ElWire>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var dxf = id.ObjectClass.DxfName;
                if (dxf != "LINE" && dxf != "LWPOLYLINE" && dxf != "POLYLINE" && dxf != "ARC" && dxf != "SPLINE") continue;
                var curve = tr.GetObject(id, OpenMode.ForRead) as Curve;
                if (curve == null || !layers.Contains(curve.Layer)) continue;
                wires.Add(new ElWire { Points = Vertices(tr, curve) });
            }
            result.Wires = wires.Count;
            result.Nets = ElectricalNet.Build(result.Nodes, wires, tolerance);
            return result;
        }

        private static List<PlanPoint> Vertices(Transaction tr, Curve curve)
        {
            var pts = new List<PlanPoint>();
            var poly = curve as Polyline;
            var old = curve as Polyline2d;
            if (poly != null)
                for (int i = 0; i < poly.NumberOfVertices; i++) { var p = poly.GetPoint3dAt(i); pts.Add(new PlanPoint(p.X, p.Y)); }
            else if (old != null)
            {
                foreach (ObjectId vid in old)
                {
                    var v = tr.GetObject(vid, OpenMode.ForRead) as Vertex2d;
                    if (v != null) pts.Add(new PlanPoint(v.Position.X, v.Position.Y));
                }
            }
            else
            {
                pts.Add(new PlanPoint(curve.StartPoint.X, curve.StartPoint.Y));
                pts.Add(new PlanPoint(curve.EndPoint.X, curve.EndPoint.Y));
            }
            return pts;
        }

        /// <summary>Drawing units in a real millimetre, checked against the size of the plan (see AUTODIM). 0 when cancelled.</summary>
        private static double UnitsPerMm(Editor ed, List<ElBlock> blocks)
        {
            if (blocks.Count == 0) return Util.MmToDrawingUnits(1.0);
            double span = Math.Max(blocks.Max(b => b.Box.MaxX) - blocks.Min(b => b.Box.MinX), blocks.Max(b => b.Box.MaxY) - blocks.Min(b => b.Box.MinY));
            return AutoDimCommands.ResolveUnitsPerMm(ed, span);
        }

        // ------------------------------------------------------------------ ELCONNECT

        [CommandMethod("ELCONNECT")]
        public void ElConnect()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!EnsureBlocks(ed, db)) return;
            List<ElBlock> blocks;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                tr.Commit();
            }
            if (!blocks.Any(b => b.Kind != Board) || !blocks.Any(b => b.Kind == Board))
            {
                ed.WriteMessage("\nELCONNECT: needs at least one switchboard block and one light or fan point block. Run ELBLOCKS to choose which blocks they are.");
                return;
            }
            var wiring = EnsureWiring(ed, db, blocks);
            if (wiring == null) return;
            double mm = UnitsPerMm(ed, blocks);
            if (mm <= 0) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                EnsureNumbered(tr, blocks);
                SyncLabels(tr, db, blocks);
                Report(ed, tr, db, blocks, wiring, mm);
                tr.Commit();
            }
        }

        /// <summary>Analyses each kind that has points, prints what it found, and flags what is not wired.</summary>
        private static List<Analysis> Report(Editor ed, Transaction tr, Database db, List<ElBlock> blocks, Dictionary<string, List<string>> wiring, double mm)
        {
            double tol = Settings.GetDouble("ElectricalSnapMm", 100) * mm;
            Util.EnsureLayer(tr, db, FlagLayer, 1);
            ClearFlags(tr, db);
            EnsureRegApp(tr, db);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            var all = new List<Analysis>();
            foreach (var kind in PointKinds)
            {
                if (!blocks.Any(b => b.Kind == kind)) continue;
                var a = Analyse(tr, db, kind, blocks, wiring[kind.Code], tol);
                all.Add(a);

                var pairs = ElectricalSchedule.BoardsOfPoints(a.Nets);
                int points = a.Nodes.Count(n => !n.IsBoard);
                var loose = ElectricalSchedule.Unconnected(a.Nodes, a.Nets);
                ed.WriteMessage("\n" + kind.Label + ": " + points + " point(s), " + a.Nodes.Count(n => n.IsBoard) + " board(s), "
                    + a.Wires + " wire(s) on " + string.Join(", ", wiring[kind.Code]) + "; " + a.Nets.Count(n => n.Boards.Count > 0 && n.Points.Count > 0)
                    + " circuit(s), " + loose.Count(i => !a.Nodes[i].IsBoard) + " point(s) not wired to a board.");
                foreach (var entry in pairs.Where(e => e.Value.Count > 1))
                    ed.WriteMessage("\n  " + a.Nodes[entry.Key].Id + " is wired to " + entry.Value.Count + " boards: "
                        + string.Join(", ", entry.Value.Select(b => a.Nodes[b].Id)) + ".");
                foreach (int i in loose)
                {
                    ed.WriteMessage("\n  " + a.Nodes[i].Id + (a.Nodes[i].IsBoard ? " has no " + kind.Label.ToLowerInvariant() + " points wired to it." : " is not wired to a board."));
                    AddFlag(tr, space, a.Nodes[i].Box, tol);
                }
            }
            if (all.Count > 0) ed.WriteMessage("\nFlags are on " + FlagLayer + ". Run ELUPDATE after fixing the wiring.");
            return all;
        }

        private static void AddFlag(Transaction tr, BlockTableRecord space, Box box, double tol)
        {
            double r = Math.Max(0.75 * Math.Max(box.Width, box.Height), 2 * tol);
            var circle = new Circle(new Point3d(box.CentreX, box.CentreY, 0), Vector3d.ZAxis, r) { Layer = FlagLayer };
            circle.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "FLAG"));
            space.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        private static void ClearFlags(Transaction tr, Database db)
        {
            foreach (var id in Tagged(tr, db, "FLAG"))
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
            }
        }

        // ------------------------------------------------------------------ ELSCHEDULE and ELUPDATE

        [CommandMethod("ELSCHEDULE")]
        public void ElSchedule()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!EnsureBlocks(ed, db)) return;

            var whichOpt = new PromptKeywordOptions("\nSchedule for [Light/Fan/Both] <" + _which + ">: ") { AllowNone = true };
            foreach (var k in new[] { "Light", "Fan", "Both" }) whichOpt.Keywords.Add(k);
            var which = ed.GetKeywords(whichOpt);
            if (which.Status == PromptStatus.OK) _which = which.StringResult;
            else if (which.Status != PromptStatus.None) return;

            var layoutOpt = new PromptKeywordOptions("\nLayout [Board/Point] <" + _layout + ">: ") { AllowNone = true };
            layoutOpt.Keywords.Add("Board");
            layoutOpt.Keywords.Add("Point");
            var layout = ed.GetKeywords(layoutOpt);
            if (layout.Status == PromptStatus.OK) _layout = layout.StringResult;
            else if (layout.Status != PromptStatus.None) return;

            var scale = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + _scale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = _scale, UseDefaultValue = true });
            if (scale.Status != PromptStatus.OK) return;
            _scale = scale.Value;

            var ppr = ed.GetPoint("\nPick a point for the schedule (top-left): ");
            if (ppr.Status != PromptStatus.OK) return;

            List<ElBlock> blocks;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                tr.Commit();
            }
            if (!blocks.Any(b => b.Kind == Board) || !blocks.Any(b => b.Kind != Board))
            {
                ed.WriteMessage("\nELSCHEDULE: no switchboard and point blocks found.");
                return;
            }
            var wiring = EnsureWiring(ed, db, blocks);
            if (wiring == null) return;
            double mm = UnitsPerMm(ed, blocks);
            if (mm <= 0) return;
            double h = Settings.GetDouble("ElectricalTableTextMm", 2.5) * _scale * mm;

            int drawn = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                EnsureNumbered(tr, blocks);
                SyncLabels(tr, db, blocks);
                Util.EnsureLayer(tr, db, TableLayer, 7);
                EnsureRegApp(tr, db);
                double tol = Settings.GetDouble("ElectricalSnapMm", 100) * mm;
                double y = ppr.Value.Y;
                foreach (var kind in PointKinds)
                {
                    if (_which != "Both" && _which != kind.Label) continue;
                    if (!blocks.Any(b => b.Kind == kind)) continue;
                    var a = Analyse(tr, db, kind, blocks, wiring[kind.Code], tol);
                    double used = DrawSchedule(tr, db, a, _layout, new Point3d(ppr.Value.X, y, 0), h);
                    if (used > 0) { drawn++; y -= used; }
                    else ed.WriteMessage("\n" + kind.Label + ": no point is wired to a board, so there is nothing to schedule. Run ELCONNECT to see why.");
                }
                tr.Commit();
            }
            ed.WriteMessage("\nELSCHEDULE: " + drawn + " schedule(s) drawn on " + TableLayer + ". Use ELUPDATE to refresh them after you change the drawing.");
        }

        [CommandMethod("ELUPDATE")]
        public void ElUpdate()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!EnsureBlocks(ed, db)) return;
            List<ElBlock> blocks;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                tr.Commit();
            }
            if (blocks.Count == 0)
            {
                ed.WriteMessage("\nELUPDATE: no electrical blocks in this drawing.");
                return;
            }
            var wiring = EnsureWiring(ed, db, blocks);
            if (wiring == null) return;
            double mm = UnitsPerMm(ed, blocks);
            if (mm <= 0) return;

            int redrawn = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                EnsureNumbered(tr, blocks);
                SyncLabels(tr, db, blocks);
                var analyses = Report(ed, tr, db, blocks, wiring, mm);

                // every schedule already drawn: same place, same text size, new figures
                var groups = new Dictionary<string, List<ObjectId>>();
                foreach (var id in Tagged(tr, db, "TABLE"))
                {
                    var values = ExtendedData((Entity)tr.GetObject(id, OpenMode.ForRead));
                    if (values.Length < 6) continue;
                    // TABLE, kind code, layout, text height, x, y
                    string key = values[1] + "|" + values[2] + "|" + values[3] + "|" + values[4] + "|" + values[5];
                    if (!groups.ContainsKey(key)) groups[key] = new List<ObjectId>();
                    groups[key].Add(id);
                }
                Util.EnsureLayer(tr, db, TableLayer, 7);
                foreach (var group in groups)
                {
                    var p = group.Key.Split('|');
                    var a = analyses.FirstOrDefault(x => x.Kind.Code == p[0]);
                    foreach (var id in group.Value) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                    if (a == null) continue;
                    double h = double.Parse(p[2], CultureInfo.InvariantCulture);
                    var anchor = new Point3d(double.Parse(p[3], CultureInfo.InvariantCulture), double.Parse(p[4], CultureInfo.InvariantCulture), 0);
                    if (DrawSchedule(tr, db, a, p[1], anchor, h) > 0) redrawn++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nELUPDATE: numbers and labels refreshed, " + redrawn + " schedule(s) redrawn.");
        }

        /// <summary>
        /// Draws one schedule with its title at <paramref name="anchor"/> and tags every piece so ELUPDATE can find and redraw it.
        /// Returns the height used (for stacking the next one), or 0 when there is nothing to show.
        /// </summary>
        private static double DrawSchedule(Transaction tr, Database db, Analysis a, string layout, Point3d anchor, double h)
        {
            string[] headers;
            var rows = new List<string[]>();
            string title = "ELECTRICAL " + a.Kind.Title + " CONNECTION SCHEDULE";
            if (layout == "Point")
            {
                headers = new[] { a.Kind.Code, "Connected " + Board.Code };
                foreach (var e in ElectricalSchedule.ByPoint(a.Nodes, a.Nets)) rows.Add(new[] { e.Key, e.Value });
                title += " - BY " + a.Kind.Code;
            }
            else
            {
                headers = new[] { Board.Code, a.Kind.Code, "Connection" };
                foreach (var r in ElectricalSchedule.ByBoard(a.Nodes, a.Nets)) rows.Add(new[] { r.Board, r.Point, r.Connection });
            }
            if (rows.Count == 0) return 0;

            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var made = new List<ObjectId>();
            var head = new DBText { Position = new Point3d(anchor.X, anchor.Y + 0.6 * h, 0), Height = h * 1.2, TextString = title, Layer = TableLayer };
            made.Add(space.AppendEntity(head));
            tr.AddNewlyCreatedDBObject(head, true);
            made.AddRange(MeasureCommands.DrawTable(tr, db, new Point3d(anchor.X, anchor.Y - 1.5 * h, 0), headers, rows, h, TableLayer));

            string[] tag =
            {
                "TABLE", a.Kind.Code, layout, h.ToString("R", CultureInfo.InvariantCulture),
                anchor.X.ToString("R", CultureInfo.InvariantCulture), anchor.Y.ToString("R", CultureInfo.InvariantCulture)
            };
            foreach (var id in made)
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var values = new List<TypedValue> { new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName) };
                values.AddRange(tag.Select(t => new TypedValue((int)DxfCode.ExtendedDataAsciiString, t)));
                ent.XData = new ResultBuffer(values.ToArray());
            }
            return (rows.Count + 1) * 2.0 * h + 4.5 * h;
        }

        // ------------------------------------------------------------------ reading blocks and tags

        private static List<ElBlock> ReadBlocks(Transaction tr, Database db)
        {
            var result = new List<ElBlock>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var map = LoadBlockMap(tr, db);
            var patterns = AllKinds.ToDictionary(k => k, k => PatternsFor(map, k));
            foreach (ObjectId id in space)
            {
                if (id.ObjectClass.DxfName != "INSERT") continue;
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br == null) continue;
                string name = BlockOpenings.EffectiveName(tr, br);
                var kind = AllKinds.FirstOrDefault(k => ElectricalNumbering.NameMatches(name, patterns[k]));
                if (kind == null) continue;

                var block = new ElBlock { Id = id, Kind = kind, BlockName = name };
                var values = ExtendedData(br);
                if (values.Length >= 2 && values[0] == "ID") block.Existing = values[1];
                try
                {
                    var e = br.GeometricExtents;
                    block.Box = new Box(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y);
                }
                catch { block.Box = new Box(br.Position.X, br.Position.Y, br.Position.X, br.Position.Y); }
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null && string.Equals(att.Tag, "ID", StringComparison.OrdinalIgnoreCase))
                    {
                        block.HasIdAttribute = true;
                        block.IdAttributeVisible = !att.Invisible;
                    }
                }
                result.Add(block);
            }
            return result;
        }

        /// <summary>The text values stored on an entity for this tool, without the application name.</summary>
        private static string[] ExtendedData(Entity ent)
        {
            var data = ent.GetXDataForApplication(AppName);
            if (data == null) return new string[0];
            return data.AsArray().Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString).Select(v => v.Value as string ?? "").ToArray();
        }

        /// <summary>Entities carrying this tool's tag ("LABEL", "FLAG" or "TABLE") in the current space.</summary>
        private static List<ObjectId> Tagged(Transaction tr, Database db, string kind)
        {
            var found = new List<ObjectId>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var values = ExtendedData(ent);
                if (values.Length > 0 && values[0] == kind) found.Add(id);
            }
            return found;
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(AppName)) return;
            table.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = AppName };
            table.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        // ------------------------------------------------------------------ labels

        /// <summary>
        /// Puts each block's ID beside it as text (unless the block shows it in an ID attribute), moves the text when the block
        /// has moved, and removes text whose block is gone. The text is tied to its block by the block's handle.
        /// </summary>
        private static int SyncLabels(Transaction tr, Database db, List<ElBlock> blocks)
        {
            if (Settings.GetInt("ElectricalLabels", 1) == 0) return 0;
            int shown = 0;
            string layer = Settings.Get("ElectricalLabelLayer", "EL-LABELS");
            Util.EnsureLayer(tr, db, layer, 3);
            EnsureRegApp(tr, db);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            var labels = new Dictionary<string, ObjectId>();
            foreach (var id in Tagged(tr, db, "LABEL"))
            {
                var values = ExtendedData((Entity)tr.GetObject(id, OpenMode.ForRead));
                if (values.Length >= 2) labels[values[1]] = id;
            }

            var live = new HashSet<string>();
            foreach (var block in blocks)
            {
                var br = (BlockReference)tr.GetObject(block.Id, OpenMode.ForRead);
                string handle = br.Handle.ToString();
                live.Add(handle);
                string id = block.CurrentId;
                ObjectId labelId;
                bool has = labels.TryGetValue(handle, out labelId);

                if (block.IdAttributeVisible || id.Length == 0)
                {
                    if (has) ((Entity)tr.GetObject(labelId, OpenMode.ForWrite)).Erase();
                    continue;
                }
                double size = Math.Max(block.Box.Width, block.Box.Height);
                double fixedHeight = Settings.GetDouble("ElectricalLabelHeightMm", 0);
                double height = fixedHeight > 0 ? Util.MmToDrawingUnits(fixedHeight) : Math.Max(0.4 * size, Util.MmToDrawingUnits(150));
                var at = new Point3d(block.Box.MaxX + 0.15 * height, block.Box.CentreY, 0);
                shown++;
                if (has)
                {
                    var text = (DBText)tr.GetObject(labelId, OpenMode.ForWrite);
                    text.TextString = id;
                    text.Position = at;
                    text.Height = height;
                }
                else
                {
                    var text = new DBText { Position = at, Height = height, TextString = id, Layer = layer };
                    text.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, "LABEL"),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, handle));
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                }
            }
            foreach (var entry in labels)
                if (!live.Contains(entry.Key)) ((Entity)tr.GetObject(entry.Value, OpenMode.ForWrite)).Erase();
            return shown;
        }
    }
}
