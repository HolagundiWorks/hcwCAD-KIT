using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// The building's levels: floor to floor height, ceiling height, lintel bottom and slab thickness for each floor.
    /// They are kept in the drawing with the take-off book (record HCW_MEASURE, the Floors tab of MSCHED) and read from there by the
    /// section, stair, door and window tools, so one set of figures serves them all.
    /// </summary>
    internal static class LevelStore
    {
        /// <summary>Millimetres in one schedule unit (the heights are held in metres, or feet for imperial work).</summary>
        public static double UnitMm => MeasureCommands.MeasureState.Units == MeasureCommands.UnitSys.Imperial ? 304.8 : 1000.0;

        /// <summary>The floors in the drawing, in millimetres. Empty when none are defined.</summary>
        public static List<LevelRow> Load(Transaction tr, Database db)
        {
            double k = UnitMm;
            return MeasureBook.Load(tr, db).Floors.Where(f => f.FflHeight > 0).Select(f => new LevelRow
            {
                Name = f.Name, FflMm = f.FflHeight * k, CeilingMm = (f.Height > 0 ? f.Height : f.FflHeight) * k,
                LintelMm = f.LintelBottom * k, SlabMm = f.Slab * k,
            }).ToList();
        }

        public static List<LevelRow> Load()
        {
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var r = Load(tr, Util.Db);
                tr.Commit();
                return r;
            }
        }

        /// <summary>Door and window heights that fit the first floor: the door runs to the lintel bottom, the window from its sill to the lintel bottom. Null when no floors are defined.</summary>
        public static LevelRow First()
        {
            var l = Load();
            return l.Count == 0 ? null : l[0];
        }
    }
}
