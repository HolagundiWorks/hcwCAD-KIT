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
    /// Blocks: ELBLOCKS says which block is the switchboard (SB), a light point (LP), a fan point (FP), a one way or two way
    /// switch (SW1, SW2), a 5 amp or 15 amp socket (P5, P15), AC, water purifier (WP), geyser (GY), fridge (FR), oven (OV),
    /// WiFi router (WF), calling bell (CB) or TV. It is saved in the drawing; until then the default names from the settings are used.
    ///
    /// ELNUM (and SBNUM, LPNUM, FPNUM)  give every block an ID (SB-01, LP-02, GY-01 ...) kept on the block itself (extended data,
    ///                                  and the ID, NUMBER and TAG attributes when the block has them) and written beside the block.
    /// ELLAYERS                         the layers the wiring is drawn on: lighting wiring (lights, fans, switches, bell) and power
    ///                                  wiring (sockets and appliances).
    /// ELCONNECT                        works out from the wiring which points reach which board, and flags what is not wired.
    /// ELSCHEDULE                       draws the schedule: the typical table with a row per switchboard and a column per kind of
    ///                                  point (5 amp, 15 amp, one way switches, two way switches, AC, geyser ...), or a row per point.
    /// ELUPDATE                         numbers new blocks, moves labels, re-checks the wiring and redraws the schedules.
    ///
    /// Connections come from geometry only: a wire (line, polyline, arc) reaches a block when one of its vertices lies within
    /// ElectricalSnapMm of the block's extents. Wires that meet end to end, or in a T, are one run. A point, switch or appliance
    /// is a junction (a run from one light to the next carries on to the board); a board is a terminal.
    /// </summary>
    public class ElectricalCommands
    {
        private const string AppName = "HCW_ELEC";
        private const string StoreDictionary = "HCW_ELEC";
        private const string WiringRecord = "WIRING";
        private const string BlocksRecord = "BLOCKS";
        private const string TableLayer = "EL-TABLE";
        private const string FlagLayer = "EL-CHECK";

        private static double _scale = 100;
        private static string _layout = "Matrix";
        private static string _cells = "Numbers";
        private static string _renumber = "Keep";

        private static readonly ElKind Board = ElectricalKinds.Find("SB");
        private static readonly ElKind[] AllKinds = ElectricalKinds.All;
        private static readonly string[] Groups = { "LT", "PW" };

        private static string[] DefaultPatterns(ElKind kind)
            => Settings.Get(kind.Setting, kind.Code).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>One electrical block found in the drawing.</summary>
        private class ElBlock
        {
            public ObjectId Id;
            public ElKind Kind;
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

        /// <summary>The block names chosen with ELBLOCKS for each kind code; empty lists when nothing was chosen.</summary>
        private static Dictionary<string, List<string>> LoadBlockMap(Transaction tr, Database db)
        {
            var map = AllKinds.ToDictionary(k => k.Code, k => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var line in DrawingStore.Read(tr, db, StoreDictionary, BlocksRecord))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                if (map.ContainsKey(p[0])) map[p[0]] = p.Skip(1).Where(n => n.Length > 0).ToList();
            }
            return map;
        }

        /// <summary>The names that count as this kind: the ones chosen for the drawing, or the settings when none were chosen.</summary>
        private static string[] PatternsFor(Dictionary<string, List<string>> map, ElKind kind)
            => map[kind.Code].Count > 0 ? map[kind.Code].ToArray() : DefaultPatterns(kind);

        [CommandMethod("ELBLOCKS")]
        public void ElBlocks()
        {
            var ed = Util.Ed;
            var map = AskBlocks(ed, Util.Db, Board.Code);
            if (map == null) return;
            foreach (var kind in AllKinds.Where(k => map[k.Code].Count > 0))
                ed.WriteMessage("\n  " + kind.Label + ": " + string.Join(", ", map[kind.Code]));
            ed.WriteMessage("\nELBLOCKS: saved in the drawing. Run ELNUM to number them.");
        }

        /// <summary>
        /// When no block in the drawing is an electrical item by the saved mapping or the default names, asks which blocks
        /// they are. False when the user cancels that.
        /// </summary>
        private static bool EnsureBlocks(Editor ed, Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var map = LoadBlockMap(tr, db);
                bool nothingMapped = AllKinds.All(k => map[k.Code].Count == 0);
                bool nothingFound = ReadBlocks(tr, db).Count == 0;
                tr.Commit();
                if (!(nothingMapped && nothingFound)) return true;
            }
            ed.WriteMessage("\nNo block is named like the default electrical names (" + string.Join(", ", AllKinds.Take(3).Select(k => string.Join("/", DefaultPatterns(k))))
                + " ...). Choose which blocks are the switchboards, points, switches and appliances.");
            return AskBlocks(ed, db, Board.Code) != null;
        }

        /// <summary>
        /// Shows the roles and the blocks in the drawing and saves the choice in the drawing. "Pick from drawing" takes the
        /// blocks you select for the role. A block has one role. Null on Cancel.
        /// </summary>
        private static Dictionary<string, List<string>> AskBlocks(Editor ed, Database db, string startWith)
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
            // a drawing with nothing chosen for a role starts from that role's default names
            var claimed = new HashSet<string>(map.Values.SelectMany(v => v), StringComparer.OrdinalIgnoreCase);
            foreach (var kind in AllKinds)
                if (map[kind.Code].Count == 0)
                    foreach (var n in names.Where(n => !claimed.Contains(n) && ElectricalNumbering.NameMatches(n, DefaultPatterns(kind))))
                    {
                        map[kind.Code].Add(n);
                        claimed.Add(n);
                    }

            string current = startWith;
            while (true)
            {
                using (var dlg = new UI.BlockRolesForm(names, AllKinds, map, current))
                {
                    var result = dlg.ShowDialog();
                    map = dlg.Map;
                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        current = dlg.PickRole;
                        PickBlockNames(ed, db, map, ElectricalKinds.Find(current));
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

        private static void PickBlockNames(Editor ed, Database db, Dictionary<string, List<string>> map, ElKind kind)
        {
            var filter = new SelectionFilter(new[] { new TypedValue(0, "INSERT") });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the " + kind.Label.ToLowerInvariant() + " blocks: " }, filter);
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
            if (picked.Count > 0) ed.WriteMessage("\nAdded to " + kind.Label.ToLowerInvariant() + ": " + string.Join(", ", picked) + ".");
        }

        // ------------------------------------------------------------------ numbering

        [CommandMethod("SBNUM")] public void SbNum() => Number(AllKinds.Where(k => k.Code == "SB").ToArray(), false);
        [CommandMethod("LPNUM")] public void LpNum() => Number(AllKinds.Where(k => k.Code == "LP").ToArray(), false);
        [CommandMethod("FPNUM")] public void FpNum() => Number(AllKinds.Where(k => k.Code == "FP").ToArray(), false);
        [CommandMethod("ELNUM")] public void ElNum() => Number(AllKinds, true);

        private static void Number(ElKind[] kinds, bool all)
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
                int numbered = 0;
                foreach (var kind in kinds)
                {
                    var mine = blocks.Where(b => b.Kind == kind).ToList();
                    if (mine.Count == 0)
                    {
                        if (!all) ed.WriteMessage("\n" + kind.Label + ": no block is mapped as a " + kind.Label.ToLowerInvariant() + ". Run ELBLOCKS to choose which blocks they are.");
                        continue;
                    }
                    int kept = AssignIds(tr, mine, kind, _renumber == "All");
                    numbered += mine.Count;
                    ed.WriteMessage("\n" + kind.Label + ": " + mine.Count + " block(s) numbered (" + kind.Code + "); "
                        + kept + " kept their number, " + (mine.Count - kept) + " new or changed.");
                }
                int shown = SyncLabels(tr, db, blocks);
                if (shown > 0) ed.WriteMessage("\nEach ID is written beside its block (layer " + Settings.Get("ElectricalLabelLayer", "EL-LABELS") + ").");
                if (all && numbered == 0) ed.WriteMessage("\nNo electrical blocks found. Run ELBLOCKS to choose which blocks they are.");
                tr.Commit();
            }
        }

        /// <summary>Gives every block of a kind its ID and writes it to the block. Returns how many blocks kept the ID they had.</summary>
        private static int AssignIds(Transaction tr, List<ElBlock> blocks, ElKind kind, bool renumberAll)
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

        /// <summary>Wiring layers by group (LT lighting, PW power), as last saved in the drawing.</summary>
        private static Dictionary<string, List<string>> LoadWiring(Transaction tr, Database db)
        {
            var result = new Dictionary<string, List<string>> { { "LT", new List<string>() }, { "PW", new List<string>() } };
            foreach (var line in DrawingStore.Read(tr, db, StoreDictionary, WiringRecord))
            {
                if (string.IsNullOrEmpty(line) || line == "EMPTY") continue;
                var p = Fields.Split(line);
                var layers = p.Skip(1).Where(n => n.Length > 0).ToList();
                // an earlier version saved lighting wires as LP and fan wires as FP
                string key = p[0] == "LP" ? "LT" : p[0] == "FP" ? "PW" : p[0];
                if (result.ContainsKey(key)) result[key] = layers;
            }
            return result;
        }

        /// <summary>Shows the wiring dialog (lighting wires, power wires) and saves the choice in the drawing. Null on Cancel.</summary>
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
                    new KeyValuePair<string, List<string>>("Lighting wires (lights, fans, switches, bell)", wiring["LT"]),
                    new KeyValuePair<string, List<string>>("Power wires (sockets, AC, geyser, fridge ...)", wiring["PW"])
                };
                using (var dlg = new UI.LayerListsForm("hcwCAD-KIT — Wiring layers",
                    "Tick the layers the wiring lines (lines, polylines, arcs) are drawn on. Use one layer for both if they share it: each circuit group then reads only its own items.",
                    names, lists))
                {
                    var result = dlg.ShowDialog();
                    var read = dlg.Read();
                    wiring["LT"] = read[0];
                    wiring["PW"] = read[1];
                    if (result == System.Windows.Forms.DialogResult.Retry)
                    {
                        PickWiringLayers(ed, db, dlg.PickIndex == 0 ? wiring["LT"] : wiring["PW"], dlg.PickIndex == 0 ? "lighting" : "power");
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

        /// <summary>The saved wiring layers; asks for them when a group that has items in the drawing has none.</summary>
        private static Dictionary<string, List<string>> EnsureWiring(Editor ed, Database db, List<ElBlock> blocks)
        {
            Dictionary<string, List<string>> wiring;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                wiring = LoadWiring(tr, db);
                tr.Commit();
            }
            bool missing = Groups.Any(g => blocks.Any(b => !b.Kind.IsBoard && b.Kind.Group == g) && wiring[g].Count == 0);
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
            public string Group = "LT";
            public List<ElBlock> Blocks = new List<ElBlock>();
            public List<ElNode> Nodes = new List<ElNode>();
            public List<ElNet> Nets = new List<ElNet>();
            public int Wires;
        }

        private static string GroupName(string group) => group == "LT" ? "Lighting" : "Power";

        private static Analysis Analyse(Transaction tr, Database db, string group, List<ElBlock> blocks, List<string> wiringLayers, double tolerance)
        {
            var result = new Analysis { Group = group };
            result.Blocks = blocks.Where(b => b.Kind.IsBoard || b.Kind.Group == group).ToList();
            result.Nodes = result.Blocks.Select(b => new ElNode { Id = b.CurrentId, Code = b.Kind.Code, IsBoard = b.Kind.IsBoard, Box = b.Box }).ToList();

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

        private static List<Analysis> AnalyseAll(Transaction tr, Database db, List<ElBlock> blocks, Dictionary<string, List<string>> wiring, double mm)
        {
            double tol = Settings.GetDouble("ElectricalSnapMm", 100) * mm;
            var all = new List<Analysis>();
            foreach (var group in Groups)
                if (blocks.Any(b => !b.Kind.IsBoard && b.Kind.Group == group))
                    all.Add(Analyse(tr, db, group, blocks, wiring[group], tol));
            return all;
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
            if (!blocks.Any(b => !b.Kind.IsBoard) || !blocks.Any(b => b.Kind.IsBoard))
            {
                ed.WriteMessage("\nELCONNECT: needs at least one switchboard block and one other electrical block. Run ELBLOCKS to choose which blocks they are.");
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

        /// <summary>Analyses each group that has items, prints what it found, and flags what is not wired.</summary>
        private static List<Analysis> Report(Editor ed, Transaction tr, Database db, List<ElBlock> blocks, Dictionary<string, List<string>> wiring, double mm)
        {
            double tol = Settings.GetDouble("ElectricalSnapMm", 100) * mm;
            Util.EnsureLayer(tr, db, FlagLayer, 1);
            ClearFlags(tr, db);
            EnsureRegApp(tr, db);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            var all = AnalyseAll(tr, db, blocks, wiring, mm);
            foreach (var a in all)
            {
                var pairs = ElectricalSchedule.BoardsOfPoints(a.Nets);
                int points = a.Nodes.Count(n => !n.IsBoard);
                var loose = ElectricalSchedule.Unconnected(a.Nodes, a.Nets);
                ed.WriteMessage("\n" + GroupName(a.Group) + ": " + points + " item(s), " + a.Nodes.Count(n => n.IsBoard) + " board(s), "
                    + a.Wires + " wire(s) on " + string.Join(", ", wiring[a.Group]) + "; " + a.Nets.Count(n => n.Boards.Count > 0 && n.Points.Count > 0)
                    + " circuit(s), " + loose.Count(i => !a.Nodes[i].IsBoard) + " item(s) not wired to a board.");
                foreach (var entry in pairs.Where(e => e.Value.Count > 1))
                    ed.WriteMessage("\n  " + a.Nodes[entry.Key].Id + " is wired to " + entry.Value.Count + " boards: "
                        + string.Join(", ", entry.Value.Select(b => a.Nodes[b].Id)) + ".");
                foreach (int i in loose)
                {
                    ed.WriteMessage("\n  " + a.Nodes[i].Id + (a.Nodes[i].IsBoard ? " has nothing wired to it in this group." : " is not wired to a board."));
                    if (!a.Nodes[i].IsBoard) AddFlag(tr, space, a.Nodes[i].Box, tol);
                }
            }
            // a board with nothing wired to it in any group
            foreach (var board in blocks.Where(b => b.Kind.IsBoard))
                if (all.All(a => ElectricalSchedule.Unconnected(a.Nodes, a.Nets).Any(i => a.Nodes[i].Id == board.CurrentId && a.Nodes[i].IsBoard)))
                    AddFlag(tr, space, board.Box, tol);
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

            var layoutOpt = new PromptKeywordOptions("\nSchedule [Matrix/Board/Point] <" + _layout + ">: ") { AllowNone = true };
            foreach (var k in new[] { "Matrix", "Board", "Point" }) layoutOpt.Keywords.Add(k);
            var layout = ed.GetKeywords(layoutOpt);
            if (layout.Status == PromptStatus.OK) _layout = layout.StringResult;
            else if (layout.Status != PromptStatus.None) return;

            if (_layout == "Matrix")
            {
                var cellsOpt = new PromptKeywordOptions("\nCells show [Numbers/Counts] <" + _cells + ">: ") { AllowNone = true };
                cellsOpt.Keywords.Add("Numbers");
                cellsOpt.Keywords.Add("Counts");
                var cells = ed.GetKeywords(cellsOpt);
                if (cells.Status == PromptStatus.OK) _cells = cells.StringResult;
                else if (cells.Status != PromptStatus.None) return;
            }

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
            if (!blocks.Any(b => b.Kind.IsBoard) || !blocks.Any(b => !b.Kind.IsBoard))
            {
                ed.WriteMessage("\nELSCHEDULE: no switchboard and other electrical blocks found. Run ELBLOCKS to choose which blocks they are.");
                return;
            }
            var wiring = EnsureWiring(ed, db, blocks);
            if (wiring == null) return;
            double mm = UnitsPerMm(ed, blocks);
            if (mm <= 0) return;
            double h = Settings.GetDouble("ElectricalTableTextMm", 2.5) * _scale * mm;

            bool drawn;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                blocks = ReadBlocks(tr, db);
                EnsureNumbered(tr, blocks);
                SyncLabels(tr, db, blocks);
                Util.EnsureLayer(tr, db, TableLayer, 7);
                EnsureRegApp(tr, db);
                var analyses = AnalyseAll(tr, db, blocks, wiring, mm);
                drawn = DrawSchedule(tr, db, blocks, analyses, _layout, _cells, new Point3d(ppr.Value.X, ppr.Value.Y, 0), h) > 0;
                tr.Commit();
            }
            ed.WriteMessage(drawn
                ? "\nELSCHEDULE: schedule drawn on " + TableLayer + ". Use ELUPDATE to refresh it after you change the drawing."
                : "\nELSCHEDULE: no item is wired to a board, so there is nothing to schedule. Run ELCONNECT to see why.");
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
                    if (values.Length < 7) continue;
                    // TABLE, ALL, layout, cells, text height, x, y
                    string key = values[2] + "|" + values[3] + "|" + values[4] + "|" + values[5] + "|" + values[6];
                    if (!groups.ContainsKey(key)) groups[key] = new List<ObjectId>();
                    groups[key].Add(id);
                }
                Util.EnsureLayer(tr, db, TableLayer, 7);
                foreach (var group in groups)
                {
                    var p = group.Key.Split('|');
                    foreach (var id in group.Value) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                    double h = double.Parse(p[2], CultureInfo.InvariantCulture);
                    var anchor = new Point3d(double.Parse(p[3], CultureInfo.InvariantCulture), double.Parse(p[4], CultureInfo.InvariantCulture), 0);
                    if (DrawSchedule(tr, db, blocks, analyses, p[0], p[1], anchor, h) > 0) redrawn++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nELUPDATE: numbers and labels refreshed, " + redrawn + " schedule(s) redrawn.");
        }

        /// <summary>
        /// Draws the schedule with its title at <paramref name="anchor"/> and tags every piece so ELUPDATE can find and redraw it.
        /// Matrix: a row per switchboard and a column per kind of point. Board: a row per board and point. Point: a row per point.
        /// Returns the height used, or 0 when there is nothing to show.
        /// </summary>
        private static double DrawSchedule(Transaction tr, Database db, List<ElBlock> blocks, List<Analysis> analyses, string layout, string cells, Point3d anchor, double h)
        {
            string[] headers;
            var rows = new List<string[]>();
            string title = "ELECTRICAL CONNECTION SCHEDULE";
            var links = analyses.SelectMany(a => ElectricalSchedule.Links(a.Nodes, a.Nets)).ToList();
            if (links.Count == 0) return 0;

            if (layout == "Matrix")
            {
                var columns = ElectricalMatrix.ParseColumns(Settings.Get("ElectricalColumns", ElectricalMatrix.DefaultColumns));
                if (columns.Count == 0) columns = ElectricalMatrix.ParseColumns(ElectricalMatrix.DefaultColumns);
                var table = ElectricalMatrix.Build(blocks.Where(b => b.Kind.IsBoard).Select(b => b.CurrentId), links, columns, cells == "Counts");
                headers = table[0];
                rows.AddRange(table.Skip(1));
            }
            else if (layout == "Point")
            {
                headers = new[] { "Point", "Type", "Connected SB" };
                foreach (var a in analyses)
                    foreach (var e in ElectricalSchedule.ByPoint(a.Nodes, a.Nets))
                        rows.Add(new[] { e.Key, ElectricalKinds.LabelOf(a.Nodes.First(n => n.Id == e.Key).Code), e.Value });
                rows = rows.OrderBy(r => r[0], Comparer<string>.Create(ElectricalSchedule.NaturalCompare)).ToList();
                title += " - BY POINT";
            }
            else
            {
                headers = new[] { "SB no", "Point", "Type", "Connection" };
                foreach (var a in analyses)
                    foreach (var r in ElectricalSchedule.ByBoard(a.Nodes, a.Nets))
                        rows.Add(new[] { r.Board, r.Point, r.Type, r.Connection });
                rows = rows.OrderBy(r => r[0], Comparer<string>.Create(ElectricalSchedule.NaturalCompare))
                           .ThenBy(r => r[1], Comparer<string>.Create(ElectricalSchedule.NaturalCompare)).ToList();
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
                "TABLE", "ALL", layout, cells, h.ToString("R", CultureInfo.InvariantCulture),
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
        /// Puts each block's ID beside it as text (unless the block shows it in a visible ID attribute), moves the text when the
        /// block has moved, and removes text whose block is gone. The text is tied to its block by the block's handle.
        /// Returns how many blocks have an ID text.
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
