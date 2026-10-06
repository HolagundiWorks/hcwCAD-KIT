using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Wall objects: the centre line, thickness and position of each wall drawn by HCWWALL live in the drawing's named objects
    /// (HCW_WALLS, one record per wall ID) and every outline carries the IDs of the walls it was made from as extended data.
    /// </summary>
    internal static class WallStore
    {
        internal const string App = "HCW_WALL";
        internal const string Dict = "HCW_WALLS";

        private static void EnsureApp(Transaction tr, Database db)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (apps.Has(App)) return;
            apps.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = App };
            apps.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        internal static void Tag(Transaction tr, Database db, Entity ent, IEnumerable<string> ids)
        {
            EnsureApp(tr, db);
            var rb = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, App));
            foreach (var chunk in WallIds.Pack(ids)) rb.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, chunk));
            ent.XData = rb;
        }

        internal static List<string> IdsOf(Entity ent)
        {
            var rb = ent.GetXDataForApplication(App);
            if (rb == null) return new List<string>();
            var chunks = rb.AsArray().Where(t => t.TypeCode == (int)DxfCode.ExtendedDataAsciiString).Select(t => t.Value as string);
            return WallIds.Unpack(chunks);
        }

        internal static string NextId(Transaction tr, Database db)
        {
            int max = 0;
            foreach (var k in DrawingStore.Keys(tr, db, Dict)) max = System.Math.Max(max, WallIds.NumberOf(k));
            return WallIds.Format(max + 1);
        }

        internal static void Save(Transaction tr, Database db, WallRecord w) => DrawingStore.Write(tr, db, Dict, w.Id, w.ToLines());

        internal static WallRecord Load(Transaction tr, Database db, string id) => WallRecord.FromLines(id, DrawingStore.Read(tr, db, Dict, id));
    }
}
