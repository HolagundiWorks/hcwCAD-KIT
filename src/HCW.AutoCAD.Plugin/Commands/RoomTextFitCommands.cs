using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// ROOMTEXTFIT (from the ROOMTXT.lsp routine): turns the MTEXT in a room rectangle into TEXT, removes the spaces, and fits one or more
    /// lines of room name inside the rectangle. Horizontal lines are stacked top to bottom, vertical (90 or 270 degree) lines side by side,
    /// and the group is centred. The largest text height is set with ROOMTEXTFITSET (setting RoomTextMaxMm to start with).
    /// </summary>
    public class RoomTextFitCommands
    {
        private static double _maxMm = -1;

        private static double MaxMm
        {
            get { if (_maxMm <= 0) _maxMm = Settings.GetDouble("RoomTextMaxMm", 100); return _maxMm; }
        }

        [CommandMethod("ROOMTEXTFITSET")]
        [CommandMethod("HCWROOMTEXTFITSET")]
        public void SetMaxHeight()
        {
            var ed = Util.Ed;
            ed.WriteMessage("\nCurrent maximum text height: " + Util.Dim(MaxMm) + ".");
            var r = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Enter maximum text height", MaxMm))
                { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(MaxMm), UseDefaultValue = true });
            if (r.Status != PromptStatus.OK) return;
            _maxMm = Util.TypedToMm(r.Value, MaxMm);
            ed.WriteMessage("\nMaximum text height set to " + Util.Dim(_maxMm) + ".");
        }

        [CommandMethod("ROOMTEXTFIT")]
        [CommandMethod("HCWROOMTEXTFIT")]
        public void FitRoomText()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!ed.CurrentUserCoordinateSystem.IsEqualTo(Matrix3d.Identity))
            {
                ed.WriteMessage("\nROOMTEXTFIT: switch to the World UCS first (UCS, World).");
                return;
            }

            var p1 = ed.GetPoint(new PromptPointOptions("\nSpecify first corner of room rectangle: "));
            if (p1.Status != PromptStatus.OK) return;
            var p2 = ed.GetCorner(new PromptCornerOptions("\nSpecify opposite corner: ", p1.Value));
            if (p2.Status != PromptStatus.OK) return;
            double x0 = Math.Min(p1.Value.X, p2.Value.X), x1 = Math.Max(p1.Value.X, p2.Value.X);
            double y0 = Math.Min(p1.Value.Y, p2.Value.Y), y1 = Math.Max(p1.Value.Y, p2.Value.Y);
            if (x1 - x0 < 1e-9 || y1 - y0 < 1e-9) { ed.WriteMessage("\nROOMTEXTFIT: the rectangle has no size."); return; }

            // the text inside, MTEXT included; selected before the transaction so a cancelled selection changes nothing
            var sel = ed.SelectCrossingWindow(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0), new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "TEXT,MTEXT") }));
            if (sel.Status != PromptStatus.OK) { ed.WriteMessage("\nROOMTEXTFIT: no TEXT or MTEXT found."); return; }

            double cap = Util.MmToDrawingUnits(MaxMm);
            double roomW = x1 - x0, roomH = y1 - y0, cx = (x0 + x1) / 2.0, cy = (y0 + y1) / 2.0;
            int exploded = 0, fitted = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var texts = new List<DBText>();
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var dt = ent as DBText;
                    if (dt != null) { texts.Add(dt); continue; }
                    var mt = ent as MText;
                    if (mt == null) continue;
                    var parts = new DBObjectCollection();
                    try { mt.Explode(parts); }
                    catch (System.Exception) { foreach (DBObject o in parts) o.Dispose(); continue; }
                    foreach (DBObject o in parts)
                    {
                        var piece = o as DBText;
                        if (piece == null) { o.Dispose(); continue; }
                        space.AppendEntity(piece);
                        tr.AddNewlyCreatedDBObject(piece, true);
                        texts.Add(piece);
                    }
                    mt.Erase();
                    exploded++;
                }

                // keep only the text whose centre is inside the rectangle: text that merely touches the window belongs to another room
                texts = texts.Where(t => { var c = Centre(t); return c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1; }).ToList();
                int n = texts.Count;
                if (n == 0) { ed.WriteMessage("\nROOMTEXTFIT: no usable TEXT inside the room rectangle. Nothing was changed."); return; }

                foreach (var t in texts) t.TextString = RoomTextFit.Clean(t.TextString);
                texts = texts.Where(t => t.TextString.Length > 0).ToList();            // a line that was only spaces has no size to fit
                n = texts.Count;
                if (n == 0) { ed.WriteMessage("\nROOMTEXTFIT: the text inside the rectangle is empty once spaces are removed. Nothing was changed."); return; }

                double rot = texts[0].Rotation;
                bool vert = RoomTextFit.IsVertical(rot);
                double maxW = roomW * RoomTextFit.RoomShare, maxH = roomH * RoomTextFit.RoomShare;
                foreach (var t in texts)
                {
                    var bb = Box(t);
                    double w = vert ? maxW / n : maxW, h = vert ? maxH : maxH / n;
                    t.Height = RoomTextFit.FitHeight(t.Height, bb.X, bb.Y, w, h, cap);
                }

                // order the lines: horizontal top to bottom; vertical left to right (right to left for 270 degree text)
                if (vert)
                {
                    texts = texts.OrderBy(t => Centre(t).X).ToList();
                    if (Math.Sin(rot) < 0.0) texts.Reverse();
                }
                else texts = texts.OrderByDescending(t => Centre(t).Y).ToList();

                double scale;
                var sizes = texts.Select(t => { var bb = Box(t); return vert ? bb.X : bb.Y; }).ToList();
                var offsets = RoomTextFit.Stack(sizes, vert ? maxW : maxH, out scale);
                if (scale < 1.0) foreach (var t in texts) t.Height = t.Height * scale;

                for (int i = 0; i < n; i++)
                {
                    var c = Centre(texts[i]);
                    var target = vert ? new Point3d(cx + offsets[i], cy, c.Z) : new Point3d(cx, cy - offsets[i], c.Z);
                    texts[i].TransformBy(Matrix3d.Displacement(target - c));
                    fitted++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nROOMTEXTFIT: fitted " + fitted + " text line(s)" + (exploded > 0 ? ", " + exploded + " MTEXT exploded into TEXT" : "")
                + ". Maximum height " + Math.Round(MaxMm, 2) + " mm (ROOMTEXTFITSET changes it).");
        }

        private static Point3d Centre(Entity e)
        {
            var ext = e.GeometricExtents;
            return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);
        }

        /// <summary>Width and height of the text's bounding box.</summary>
        private static Point2d Box(Entity e)
        {
            var ext = e.GeometricExtents;
            return new Point2d(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y);
        }
    }
}
