using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Draws a <see cref="GDrawing"/> (real-size millimetres, local x/y) into the current space at a point and rotation.
    /// Each shape's role is turned into a layer by the table the caller gives; unknown roles go on the first layer.
    /// </summary>
    internal class GDrawer
    {
        internal class RoleLayer
        {
            public string Layer; public short Color; public LineWeight Weight = LineWeight.LineWeight000;
        }

        private readonly Transaction _tr;
        private readonly Database _db;
        private readonly BlockTableRecord _space;
        private readonly Dictionary<string, RoleLayer> _roles;
        private readonly double _k;
        private readonly double _textHeightMm;

        /// <summary>Applied to every entity before it is added (a UCS), when the drawing is laid out in the current user coordinate system.</summary>
        public Matrix3d? Ucs;
        /// <summary>Called for every entity before it is added, with the part name, so the caller can tag what it draws.</summary>
        public Action<Entity, string> Tag;
        /// <summary>Name of the part being drawn (PLAN, SECTION ...), handed to <see cref="Tag"/>.</summary>
        public string Part = "";
        /// <summary>Hatch pattern scale in sheet millimetres times the drawing scale (the hatch is drawn with PatternScale = this x mm / 3.175).</summary>
        public double HatchScale = 20;
        public bool Hatching = true;
        /// <summary>Dimension style for the dimensions; the drawing's current style when not set.</summary>
        public ObjectId? DimStyle;

        public GDrawer(Transaction tr, Database db, BlockTableRecord space, Dictionary<string, RoleLayer> roles, double textHeightMm)
        {
            _tr = tr; _db = db; _space = space; _roles = roles; _k = Util.MmToDrawingUnits(1.0); _textHeightMm = textHeightMm;
            foreach (var r in roles.Values) Util.EnsureLayer(tr, db, r.Layer, r.Color, "Continuous", r.Weight);
            Util.EnsureLayer(tr, db, "AN-DIMS", 8);
        }

        private string LayerOf(string role)
        {
            RoleLayer r;
            if (_roles.TryGetValue(role, out r)) return r.Layer;
            foreach (var v in _roles.Values) return v.Layer;
            return "0";
        }

        public void Draw(GDrawing d, Point3d origin, double rotation)
        {
            double c = Math.Cos(rotation), s = Math.Sin(rotation);
            Func<PlanPoint, Point3d> at = p => new Point3d(origin.X + (p.X * c - p.Y * s) * _k, origin.Y + (p.X * s + p.Y * c) * _k, origin.Z);
            foreach (var poly in d.Polys)
            {
                var pl = new Polyline { Layer = LayerOf(poly.Layer), Closed = poly.Closed, Elevation = origin.Z };
                for (int i = 0; i < poly.Pts.Count; i++) { var p = at(poly.Pts[i]); pl.AddVertexAt(i, new Point2d(p.X, p.Y), 0, 0, 0); }
                Add(pl);
                if (poly.Hatch && Hatching) TryHatch(pl);
            }
            foreach (var t in d.Texts)
            {
                var p = at(new PlanPoint(t.X, t.Y));
                var text = new DBText { Height = t.Height * _k, TextString = t.Text, Layer = LayerOf("TEXT"), Rotation = Readable(t.Rotation + rotation) };
                if (t.Centre)
                {
                    text.HorizontalMode = TextHorizontalMode.TextCenter;
                    text.VerticalMode = TextVerticalMode.TextVerticalMid;
                    text.AlignmentPoint = p;
                }
                else text.Position = p;
                Add(text);
            }
            foreach (var dim in d.Dims)
            {
                var ad = new AlignedDimension(at(dim.A), at(dim.B), at(dim.Line), dim.Text, DimStyle ?? _db.Dimstyle) { Layer = "AN-DIMS" };
                double h = _textHeightMm * _k;
                ad.Dimscale = 1; ad.Dimtxt = h; ad.Dimasz = h; ad.Dimexe = 0.6 * h; ad.Dimexo = 0.6 * h; ad.Dimgap = 0.4 * h;
                Add(ad);
            }
        }

        private void Add(Entity ent)
        {
            if (Tag != null) Tag(ent, Part);
            if (Ucs.HasValue) ent.TransformBy(Ucs.Value);
            _space.AppendEntity(ent);
            _tr.AddNewlyCreatedDBObject(ent, true);
        }

        private static double Readable(double rotation)
        {
            double r = rotation % (2 * Math.PI);
            if (r < 0) r += 2 * Math.PI;
            if (r > Math.PI / 2 + 1e-6 && r <= 3 * Math.PI / 2 + 1e-6) r -= Math.PI;
            return r;
        }

        /// <summary>Concrete hatch inside a closed outline; skipped quietly when the host will not make it.</summary>
        private void TryHatch(Polyline outline)
        {
            try
            {
                var hatch = new Hatch { Layer = LayerOf("HATCH") };
                if (Tag != null) Tag(hatch, Part);
                _space.AppendEntity(hatch);
                _tr.AddNewlyCreatedDBObject(hatch, true);
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                hatch.PatternScale = HatchScale * _k / 3.175;
                hatch.Associative = true;
                hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { outline.ObjectId });
                hatch.EvaluateHatch(true);
            }
            catch { }
        }
    }
}
