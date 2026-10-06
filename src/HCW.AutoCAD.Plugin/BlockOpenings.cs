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
            /// <summary>The block reference the line came from.</summary>
            public ObjectId Source;
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
            var reader = new Reader(onLayers);
            var result = new List<Found>();
            foreach (ObjectId id in space)
            {
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br != null) reader.Add(tr, br, result);
            }
            return result;
        }

        /// <summary>Reads inserts one at a time (so a caller already walking the space need not walk it again). Each block definition is read once.</summary>
        public class Reader
        {
            private readonly HashSet<string> _layers;
            private readonly Dictionary<ObjectId, List<Curve>> _lines = new Dictionary<ObjectId, List<Curve>>();

            public Reader(ICollection<string> onLayers)
            {
                _layers = onLayers == null ? null : new HashSet<string>(onLayers, StringComparer.OrdinalIgnoreCase);
            }

            public void Add(Transaction tr, BlockReference br, List<Found> into)
            {
                if (_layers != null && !_layers.Contains(br.Layer)) return;

                List<Curve> deductions;
                if (!_lines.TryGetValue(br.BlockTableRecord, out deductions))
                {
                    deductions = new List<Curve>();
                    var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId entityId in def)
                    {
                        var curve = tr.GetObject(entityId, OpenMode.ForRead) as Curve;
                        if (curve != null && string.Equals(curve.Layer, MeasureCommands.LayDed, StringComparison.OrdinalIgnoreCase))
                            deductions.Add(curve);
                    }
                    _lines[br.BlockTableRecord] = deductions;
                }
                if (deductions.Count == 0 && _layers == null) return;

                string name = EffectiveName(tr, br);
                if (deductions.Count == 0)
                {
                    var fallback = ExtentsLine(br);
                    if (fallback != null) into.Add(new Found { Curve = fallback, BlockName = name, Source = br.ObjectId });
                    return;
                }
                foreach (var curve in deductions)
                {
                    var moved = curve.GetTransformedCopy(br.BlockTransform) as Curve;
                    if (moved != null) into.Add(new Found { Curve = moved, BlockName = name, Source = br.ObjectId });
                }
            }
        }
    }
}
