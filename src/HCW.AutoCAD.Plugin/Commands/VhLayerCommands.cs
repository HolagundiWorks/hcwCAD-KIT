using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// VHLAYERS — underscore layer names (A_WALL_CUT, S_COLUMN, and so on).
    /// Separate from HCWLAYERS. Use one layer standard per drawing.
    /// </summary>
    public class VhLayerCommands
    {
        [CommandMethod("VHLAYERS")]
        public void VhLayers()
        {
            var db = Util.Db; var ed = Util.Ed;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var ld in LayerData.Vh)
                    Util.EnsureLayer(tr, db, ld.Name, (short)ld.Aci, ld.Linetype, Util.MmToLineWeight(ld.LwMm), ld.Plot);

                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has("A_WALL_CUT")) db.Clayer = lt["A_WALL_CUT"];

                tr.Commit();
                ed.WriteMessage($"\nVH Indian Architecture Layer Set created successfully. Command: VHLAYERS ({LayerData.Vh.Length} layers).");
            }
        }
    }
}
