using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using HCW.AutoCAD.Plugin.Commands;

namespace HCW.AutoCAD.Plugin
{
    /// <summary>
    /// Door and window blocks that carry a line on the deduction layer (MEASURE-DEDUCT).
    /// The line is the opening length. Every insert of such a block in the current space gives one
    /// deduction, and the block's name maps it to a schedule entry.
    /// Dynamic blocks are read as they are inserted, so a stretched door or window gives its own length.
    /// </summary>
    public static class BlockOpenings
    {
        public class Found
        {
            /// <summary>The deduction line, moved into the drawing's coordinates. Not stored in the database.</summary>
            public Curve Curve;
            /// <summary>The block's effective name (the dynamic block's own name, not its anonymous copy).</summary>
            public string BlockName;
        }

        public static string EffectiveName(Transaction tr, BlockReference br)
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }

        public static List<Found> Collect(Transaction tr, BlockTableRecord space)
        {
            var result = new List<Found>();
            // Definitions are read once, however many times the block is inserted.
            var lines = new Dictionary<ObjectId, List<Curve>>();
            foreach (ObjectId id in space)
            {
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br == null) continue;

                List<Curve> deductions;
                if (!lines.TryGetValue(br.BlockTableRecord, out deductions))
                {
                    deductions = new List<Curve>();
                    var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId entityId in def)
                    {
                        var curve = tr.GetObject(entityId, OpenMode.ForRead) as Curve;
                        if (curve != null && string.Equals(curve.Layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase))
                            deductions.Add(curve);
                    }
                    lines[br.BlockTableRecord] = deductions;
                }
                if (deductions.Count == 0) continue;

                string name = EffectiveName(tr, br);
                foreach (var curve in deductions)
                {
                    var moved = curve.GetTransformedCopy(br.BlockTransform) as Curve;
                    if (moved != null) result.Add(new Found { Curve = moved, BlockName = name });
                }
            }
            return result;
        }
    }
}
