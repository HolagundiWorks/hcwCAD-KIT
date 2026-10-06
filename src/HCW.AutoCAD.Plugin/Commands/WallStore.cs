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

        private static void EnsureApp(Transaction tr, Database db, string app = App)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (apps.Has(app)) return;
            apps.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = app };
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

        // ---- measurement lines: each wall also gets a line for the take-off, carrying the wall's ID in its own extended data ----

        internal const string MeasureApp = "HCW_WALLM";

        /// <summary>The layer the wall's take-off line is drawn on (MEASURE-LINEAR unless WallMeasureLayer says otherwise); null when the lines are turned off.</summary>
        internal static string MeasureLayer => Settings.GetInt("WallMeasureLines", 1) == 0 ? null : Settings.Get("WallMeasureLayer", MeasureCommands.LayLin);

        private static short MeasureColour(string layer)
        {
            if (string.Equals(layer, MeasureCommands.LayFb, System.StringComparison.OrdinalIgnoreCase)) return 30;
            if (string.Equals(layer, MeasureCommands.LayHb, System.StringComparison.OrdinalIgnoreCase)) return 5;
            if (string.Equals(layer, MeasureCommands.LayBm, System.StringComparison.OrdinalIgnoreCase)) return 140;
            if (string.Equals(layer, MeasureCommands.LayLt, System.StringComparison.OrdinalIgnoreCase)) return 40;
            return 1;
        }

        /// <summary>Draws the wall's measurement line along its centre line (a closed run stays closed) and tags it with the wall's ID.</summary>
        internal static void DrawMeasure(Transaction tr, Database db, BlockTableRecord space, WallRecord r)
        {
            string layer = MeasureLayer;
            if (layer == null || r.Points == null || r.Points.Count < 2) return;
            Util.EnsureLayer(tr, db, layer, MeasureColour(layer));
            var pl = new Polyline();
            for (int i = 0; i < r.Points.Count; i++) pl.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(r.Points[i].X, r.Points[i].Y), 0, 0, 0);
            pl.Closed = r.Closed;
            pl.Elevation = r.Z;
            pl.Layer = layer;
            space.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            EnsureApp(tr, db, MeasureApp);
            pl.XData = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, MeasureApp), new TypedValue((int)DxfCode.ExtendedDataAsciiString, r.Id));
        }

        /// <summary>The ID of the wall a measurement line belongs to, or null for any other entity.</summary>
        internal static string MeasureIdOf(Entity ent)
        {
            var rb = ent.GetXDataForApplication(MeasureApp);
            if (rb == null) return null;
            var t = rb.AsArray().Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString).Select(v => v.Value as string).FirstOrDefault();
            return t;
        }

        /// <summary>Erases the measurement lines of the given walls (they are drawn again from the records).</summary>
        internal static void EraseMeasure(Transaction tr, BlockTableRecord space, ICollection<string> ids)
        {
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                string w = MeasureIdOf(ent);
                if (w == null || !ids.Contains(w)) continue;
                ent.UpgradeOpen();
                ent.Erase();
            }
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
