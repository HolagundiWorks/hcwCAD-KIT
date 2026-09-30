using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
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

        /// <summary>A line through the middle of a block's extents along its longer side, or null when it has no extents.</summary>
        private static Line ExtentsLine(BlockReference br)
        {
            try
            {
                var e = br.GeometricExtents;
                double w = e.MaxPoint.X - e.MinPoint.X, h = e.MaxPoint.Y - e.MinPoint.Y;
                double midX = (e.MinPoint.X + e.MaxPoint.X) / 2.0, midY = (e.MinPoint.Y + e.MaxPoint.Y) / 2.0;
                return w >= h
                    ? new Line(new Point3d(e.MinPoint.X, midY, 0), new Point3d(e.MaxPoint.X, midY, 0))
                    : new Line(new Point3d(midX, e.MinPoint.Y, 0), new Point3d(midX, e.MaxPoint.Y, 0));
            }
            catch { return null; }
        }

        public static string EffectiveName(Transaction tr, BlockReference br)
        {
            var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
            return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
        }

        /// <summary>
        /// Every insert in the space with a deduction line. When <paramref name="onLayers"/> is given, only inserts on those
        /// layers count; an insert on such a layer with no deduction line inside falls back to its own extents
        /// (its longer side is the opening).
        /// </summary>
        public static List<Found> Collect(Transaction tr, BlockTableRecord space, ICollection<string> onLayers = null)
        {
            HashSet<string> layers = onLayers == null ? null : new HashSet<string>(onLayers, StringComparer.OrdinalIgnoreCase);
            var result = new List<Found>();
            // Definitions are read once, however many times the block is inserted.
            var lines = new Dictionary<ObjectId, List<Curve>>();
            foreach (ObjectId id in space)
            {
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br == null) continue;
                if (layers != null && !layers.Contains(br.Layer)) continue;

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
                string name = EffectiveName(tr, br);
                if (deductions.Count == 0)
                {
                    if (layers == null) continue;
                    var fallback = ExtentsLine(br);
                    if (fallback != null) result.Add(new Found { Curve = fallback, BlockName = name });
                    continue;
                }
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
