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
    /// HCWPERMITLAYERS gets a drawing ready for permit scrutiny: it reports, and on request moves, everything the tools drew on an HCW layer (A-WALL, A-DOOR, A-WIND,
    /// A-COL, ROOM-RECT, A-RAIL, AECSTAIR-* ...) onto its building permit layer (BP-BUILDING-CUT, BP-DOOR, BP-WINDOW ...) as the BpLayerMap setting says. It covers model space,
    /// the current space and the geometry inside the door, window and column blocks the tools made. Objects on locked layers are left and counted. New drawings go straight to
    /// the permit layers (setting LayerOutput = BP), so this is for drawings made before, or made with LayerOutput = HCW.
    /// </summary>
    public class PermitLayerCommands
    {
        private static readonly string[] OurBlocks = { "HCW_D_", "HCW_DD_", "HCW_DS_", "HCW_W_", "HCW_COL_" };

        [CommandMethod("HCWPERMITLAYERS")]
        public void PermitLayers()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            string job = Util.AskMode("Permit layers", "Report", "Move");
            if (job == null) return;
            var map = LayerRoles.Parse(Settings.Get("BpLayerMap", LayerRoles.DefaultMap));
            bool move = job == "Move";

            var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);     // "FROM -> TO" -> objects
            var onPermit = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // BP layer -> objects already or now on it
            int locked = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var spaces = new HashSet<ObjectId>();
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                spaces.Add(bt[BlockTableRecord.ModelSpace]);
                spaces.Add(db.CurrentSpaceId);
                foreach (ObjectId bid in bt)
                {
                    var rec = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
                    if (!rec.IsLayout && !rec.IsAnonymous && OurBlocks.Any(p => rec.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) spaces.Add(bid);
                }
                var lockedLayers = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId lid in layers)
                {
                    var l = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    lockedLayers[l.Name] = l.IsLocked;
                }

                foreach (var sid in spaces)
                {
                    var space = (BlockTableRecord)tr.GetObject(sid, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased) continue;
                        string from = ent.Layer, to;
                        if (from.StartsWith("BP-", StringComparison.OrdinalIgnoreCase)) { Add(onPermit, from); continue; }
                        if (!map.TryGetValue(from, out to)) continue;
                        bool isLocked;
                        if (lockedLayers.TryGetValue(from, out isLocked) && isLocked) { locked++; continue; }
                        Add(counts, from + " -> " + to);
                        Add(onPermit, to);
                        if (!move) continue;
                        Util.EnsureHcwLayer(tr, db, to);
                        ent.UpgradeOpen();
                        ent.Layer = to;
                    }
                }
                if (move) tr.Commit();
            }

            int total = counts.Values.Sum();
            if (total == 0) ed.WriteMessage("\nHCWPERMITLAYERS: nothing the tools drew is on an HCW layer that has a permit layer.");
            else
            {
                ed.WriteMessage("\nHCWPERMITLAYERS: " + (move ? "moved " : "to move: ") + total + " object(s)");
                foreach (var kv in counts) ed.WriteMessage("\n  " + Util.Pad(kv.Key, 40) + kv.Value);
            }
            if (onPermit.Count > 0)
            {
                ed.WriteMessage("\nOn the permit layers" + (move ? " now" : " (after the move)") + ":");
                foreach (var kv in onPermit) ed.WriteMessage("\n  " + Util.Pad(kv.Key, 24) + kv.Value);
            }
            if (locked > 0) ed.WriteMessage("\n" + locked + " object(s) are on locked layers and were left alone.");
            if (!move && total > 0) ed.WriteMessage("\nRun it again and choose Move to do it.");
        }

        private static void Add(IDictionary<string, int> d, string key)
        {
            int n;
            d.TryGetValue(key, out n);
            d[key] = n + 1;
        }
    }
}
