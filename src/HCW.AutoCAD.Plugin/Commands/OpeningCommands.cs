using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// HCWDOOR and HCWWINDOW: pick a point on a wall, and the two wall faces are cut for the opening and closed
    /// with jamb lines. The door or window goes in as a block (HCW_D_900x230, HCW_W_1200x230) that carries a line on
    /// MEASURE-DEDUCT, so the take-off and the schedule read it like any other opening block. A tag (D1, W1) is placed.
    /// A wall is any run of lines or polylines on one layer: faces are found from the line you pick near.
    /// </summary>
    public class OpeningCommands
    {
        private static string LayerDoor => Util.Out("A-DOOR");
        private static string LayerWin => Util.Out("A-WIND");
        private const string LayerTag = "AN-TEXT";
        private const double MinThickMm = 60, MaxThickMm = 600, TagHeightMm = 250;

        private static double _doorMm = 900;
        private static double _windowMm = 1200;
        private static string _doorType = "single";
        private static double _doorHeightMm = 2100, _windowHeightMm = 1200, _windowSillMm = 900;

        /// <summary>What a door or window carries besides its width: the door type (single, double, sliding), the sill and the height, in millimetres.</summary>
        internal class OpeningParams
        {
            public string Type = "single";
            public double SillMm, HeightMm;
        }

        private class SegRef
        {
            public ObjectId Owner;
            public Seg Seg;
            public string Layer;
        }

        [CommandMethod("HCWDOOR")]
        public void Door() { Run(true); AfterEdit(); }

        [CommandMethod("HCWWINDOW")]
        public void Window() { Run(false); AfterEdit(); }

        private static string _seededFor;

        /// <summary>The first time in a drawing: door and window heights default to the lintel bottom of the first floor (the levels in the drawing).</summary>
        private static void SeedFromLevels()
        {
            string doc = Util.Doc.Name;
            if (_seededFor == doc) return;
            _seededFor = doc;
            var lv = LevelStore.First();
            if (lv == null || lv.LintelMm <= 0) return;
            _doorHeightMm = Math.Round(lv.LintelMm);
            _windowHeightMm = Math.Max(300, Math.Round(lv.LintelMm - _windowSillMm));
        }

        private static void Run(bool door)
        {
            var ed = Util.Ed;
            SeedFromLevels();
            string what = door ? "door" : "window";
            double current = door ? _doorMm : _windowMm;
            var std = door ? OpeningStandards.Doors : OpeningStandards.Windows.Concat(new[] { OpeningStandards.Ventilator }).ToList();
            var wo = new PromptDoubleOptions("\nWidth of the " + what + " in " + Util.DrawingUnitName + ", or a standard [" + string.Join("/", std.Select(s => s.Code + " " + Util.MmToUnitsRounded(s.WidthMm))) + "] <" + Util.MmToUnitsRounded(current) + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(current), UseDefaultValue = true, AppendKeywordsToMessage = false };
            foreach (var s in std) wo.Keywords.Add(s.Code);
            var wr = ed.GetDouble(wo);
            double width;
            if (wr.Status == PromptStatus.Keyword)
            {
                width = OpeningStandards.WidthOf(wr.StringResult) ?? current;
                if (!door && string.Equals(wr.StringResult, OpeningStandards.Ventilator.Code, StringComparison.OrdinalIgnoreCase))
                {
                    // a ventilator is a small window high in the wall: offer its usual height and sill below
                    _windowHeightMm = Settings.GetDouble("VentilatorHeightMm", 450);
                    _windowSillMm = Settings.GetDouble("VentilatorSillMm", 1800);
                }
            }
            else if (wr.Status == PromptStatus.OK) width = Util.TypedToMm(wr.Value, current);
            else return;
            if (door) _doorMm = width; else _windowMm = width;

            if (door)
            {
                var to = new PromptKeywordOptions("\nDoor type [Single/Double/Sliding] <" + _doorType + ">: ", "Single Double Sliding") { AllowNone = true };
                to.Keywords.Default = char.ToUpperInvariant(_doorType[0]) + _doorType.Substring(1);
                var tr0 = ed.GetKeywords(to);
                if (tr0.Status == PromptStatus.OK) _doorType = tr0.StringResult.ToLowerInvariant();
                else if (tr0.Status != PromptStatus.None) return;
            }
            double height = door ? _doorHeightMm : _windowHeightMm;
            var hr = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Height of the " + what, height))
                { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(height), UseDefaultValue = true });
            if (hr.Status != PromptStatus.OK) return;
            double heightMm = Util.TypedToMm(hr.Value, height);
            if (door) _doorHeightMm = heightMm; else _windowHeightMm = heightMm;
            double sill = 0;
            if (!door)
            {
                var sr0 = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Sill height above the floor", _windowSillMm))
                    { AllowNegative = false, AllowZero = true, DefaultValue = Util.MmToUnitsRounded(_windowSillMm), UseDefaultValue = true });
                if (sr0.Status != PromptStatus.OK) return;
                _windowSillMm = Util.TypedToMm(sr0.Value, _windowSillMm);
                sill = _windowSillMm;
            }
            var par = new OpeningParams { Type = door ? _doorType : "", SillMm = sill, HeightMm = door ? _doorHeightMm : _windowHeightMm };

            var ucs = ed.CurrentUserCoordinateSystem;
            while (true)
            {
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the " + what + " position on the wall (Enter to finish): ") { AllowNone = true });
                if (pr.Status != PromptStatus.OK) return;
                var pick = pr.Value.TransformBy(ucs);

                bool flip = false;
                Point3d side = pick;
                if (door)
                {
                    bool got = false;
                    while (!got)
                    {
                        var so = new PromptPointOptions("\nPick the side the door opens to [Flip hinge]: ", "Flip");
                        var sr = ed.GetPoint(so);
                        if (sr.Status == PromptStatus.Keyword) { flip = !flip; ed.WriteMessage("\nHinge on the other end."); continue; }
                        if (sr.Status != PromptStatus.OK) return;
                        side = sr.Value.TransformBy(ucs);
                        got = true;
                    }
                }

                string message;
                try { message = Place(door, pick, side, flip, door ? _doorMm : _windowMm, par); }
                catch (System.Exception ex) { message = "failed: " + ex.Message; }
                ed.WriteMessage("\n" + (door ? "HCWDOOR: " : "HCWWINDOW: ") + message);
            }
        }

        private static string Place(bool door, Point3d pickW, Point3d sideW, bool flip, double widthMm, OpeningParams par)
        {
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                string message;
                if (!PlaceIn(tr, door, pickW, sideW, flip, widthMm, par, null, out message)) return message;
                tr.Commit();
                return message;
            }
        }

        /// <summary>
        /// Cuts the wall and inserts the block inside the caller's transaction. When it returns false nothing the caller
        /// should keep has been done (the caller leaves the transaction uncommitted). tagOverride reuses a tag such as D3.
        /// </summary>
        internal static bool PlaceIn(Transaction tr, bool door, Point3d pickW, Point3d sideW, bool flip, double widthMm, OpeningParams par, string tagOverride, out string message)
        {
            var db = Util.Db;
            double w = Util.MmToDrawingUnits(widthMm);
            double minT = Util.MmToDrawingUnits(MinThickMm), maxT = Util.MmToDrawingUnits(MaxThickMm);
            var pick = new P2(pickW.X, pickW.Y);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            // Take-off lines (MEASURE-*) are never wall faces: HCWWALL draws one down the centre of each wall, nearer than either face (D-006).
            var near = Segments(tr, space, pick, maxT + w).Where(r => !WallFaceLayers.IsTakeOff(r.Layer)).ToList();
            if (near.Count == 0) { message = "no wall face near that point."; return false; }

            // The wall layer is the layer whose segments give a valid opening. Try the layers nearest the pick first: lines left under a
            // wall made from selected lines can be nearer than either face (D-007), and they never have an opposite face to pair with.
            var byLayer = near.GroupBy(r => r.Layer, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Faces = g.ToList(), Dist = g.Min(r => OpeningCut.DistanceToSegment(r.Seg, pick)) })
                .Where(g => g.Dist <= maxT)
                .OrderBy(g => g.Dist).ToList();
            if (byLayer.Count == 0) { message = "no wall face near that point."; return false; }

            string error = null;
            CutPlan plan = null;
            List<SegRef> faces = null;
            foreach (var g in byLayer)
            {
                string e;
                var candidate = OpeningCut.Plan(g.Faces.Select(r => r.Seg).ToList(), pick, w, minT, maxT, out e);
                if (error == null) error = e;                 // report the nearest layer's reason if none works
                if (candidate != null) { plan = candidate; faces = g.Faces; break; }
            }
            if (plan == null) { message = error + "."; return false; }

            // Turn the two faces into single lines (a polyline outline is exploded where it is cut).
            var exploded = new Dictionary<ObjectId, List<Line>>();
            Line first = Resolve(tr, space, faces[plan.First], exploded);
            Line second = Resolve(tr, space, faces[plan.Second], exploded);
            if (first == null || second == null) { message = "could not read the wall faces."; return false; }
            string wallLayer = first.Layer;
            double z = pickW.Z;

            var p1a = plan.P1a; var p1b = plan.P1b; var p2a = plan.P2a; var p2b = plan.P2b;
            Cut(tr, space, first, p1a, p1b);
            Cut(tr, space, second, p2a, p2b);
            AddLine(tr, space, p1a, p2a, wallLayer, z);
            AddLine(tr, space, p1b, p2b, wallLayer, z);

            var n = new P2(-plan.Dir.Y, plan.Dir.X);
            var place = OpeningFrame.Compute(door, plan, new P2(sideW.X, sideW.Y), flip);
            double thickMm = Math.Round(plan.Thickness / Util.MmToDrawingUnits(1.0));
            string size = Math.Round(widthMm) + "x" + thickMm;
            double textH = Util.MmToDrawingUnits(TagHeightMm);

            P2 tagAt; string tag; string blockName;
            if (door)
            {
                // Tag on the side the door does not swing to, just outside the wall.
                var oppA = place.HingeOnFirst ? p2a : p1a; var oppB = place.HingeOnFirst ? p2b : p1b;
                tagAt = (oppA + oppB) * 0.5 + n * (-place.Sw * textH);
                blockName = OpeningFrame.NamePrefix(true, par.Type) + size;
                EnsureDoorBlock(tr, db, blockName, par.Type, w, plan.Thickness);
            }
            else
            {
                tagAt = (p1a + p1b) * 0.5 + n * (-place.S2 * textH);
                blockName = OpeningFrame.NamePrefix(false, "") + size;
                EnsureWindowBlock(tr, db, blockName, w, plan.Thickness);
            }
            tag = tagOverride ?? OpeningTag(tr, db, space, door, widthMm, par.HeightMm, door ? 0 : par.SillMm);
            InsertBlock(tr, space, blockName, new Point3d(place.Origin.X, place.Origin.Y, z), place.Angle, place.Sx, place.Sy, door ? LayerDoor : LayerWin, par);
            // A lintel is made on request (HCWLINTEL), or automatically when LintelAuto = 1. An opening that had one keeps it when it is moved or re-cut.
            if (Settings.GetInt("LintelAuto", 0) != 0 || (tagOverride != null && LintelTags.Remove(tag)))
                AddLintel(tr, db, space, (p1a + p1b + p2a + p2b) * 0.25, plan.Dir, w, plan.Thickness, z);

            Util.EnsureHcwLayer(tr, db, LayerTag);
            var text = new DBText
            {
                Position = new Point3d(tagAt.X, tagAt.Y, z),
                Height = textH,
                TextString = tag,
                Layer = LayerTag,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
            };
            text.AlignmentPoint = new Point3d(tagAt.X, tagAt.Y, z);
            space.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);

            message = tag + " " + Math.Round(widthMm) + " mm in a " + thickMm + " mm wall.";
            return true;
        }

        // ---- replace, move and slide ----

        internal class OpeningInfo
        {
            public ObjectId Id;
            public bool Door;
            public string Type = "";
            public double WidthMm, ThicknessMm, SillMm, HeightMm;
            public OpeningFrame.Corners Corners;
            public double Z;
            public string Layer = "";
            /// <summary>Not made by HCWDOOR or HCWWINDOW: a gap in the wall, or some other block standing in one. Its size comes from the gap.</summary>
            public bool Foreign;
        }

        /// <summary>Reads a door or window block made by HCWDOOR or HCWWINDOW; null for any other block.</summary>
        internal static OpeningInfo ReadOpening(Transaction tr, ObjectId id)
        {
            var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
            if (br == null) return null;
            bool door; string type; double wMm, tMm;
            if (!OpeningFrame.TryParseName(BlockOpenings.EffectiveName(tr, br), out door, out type, out wMm, out tMm)) return null;
            double mm = Util.MmToDrawingUnits(1.0);
            var info = new OpeningInfo
            {
                Id = id, Door = door, Type = type, WidthMm = wMm, ThicknessMm = tMm, Z = br.Position.Z, Layer = br.Layer,
                SillMm = 0, HeightMm = door ? Settings.GetDouble("DoorHeightMm", 2100) : Settings.GetDouble("WindowHeightMm", 1200),
                Corners = OpeningFrame.FromBlock(door, new P2(br.Position.X, br.Position.Y), br.Rotation, br.ScaleFactors.X, br.ScaleFactors.Y, wMm * mm, tMm * mm),
            };
            if (!door) info.SillMm = Settings.GetDouble("WindowSillMm", 900);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(attId, OpenMode.ForRead);
                double v;
                if (!double.TryParse(att.TextString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                string tag = (att.Tag ?? "").ToUpperInvariant();
                if (tag == "SILL") info.SillMm = v; else if (tag == "HEIGHT") info.HeightMm = v;
            }
            return info;
        }

        internal static OpeningParams ParamsOf(OpeningInfo i) => new OpeningParams { Type = i.Door ? (i.Type.Length > 0 ? i.Type : "single") : "", SillMm = i.SillMm, HeightMm = i.HeightMm };

        private static OpeningInfo SelectOpening(Editor ed, string prompt)
        {
            while (true)
            {
                var o = new PromptEntityOptions(prompt + "[Gap] ", "Gap");
                o.SetRejectMessage("\nSelect a door or window block, or choose Gap to pick an opening drawn as a break in the wall.");
                o.AddAllowedClass(typeof(BlockReference), true);
                var r = ed.GetEntity(o);
                if (r.Status == PromptStatus.Keyword) { var gap = PickGap(ed); if (gap != null) return gap; continue; }
                if (r.Status != PromptStatus.OK) return null;
                using (var tr = Util.Db.TransactionManager.StartTransaction())
                {
                    var info = ReadOpening(tr, r.ObjectId) ?? ForeignBlock(ed, tr, r.ObjectId, true);
                    tr.Commit();
                    if (info != null) return info;
                    ed.WriteMessage("\nThat block does not stand in a gap between wall lines, so it cannot be moved or replaced as an opening.");
                }
            }
        }

        /// <summary>Asks for a point inside a break in the wall lines and reads the opening from the break (null when there is none there).</summary>
        private static OpeningInfo PickGap(Editor ed)
        {
            var pr = ed.GetPoint("\nPick a point inside the gap in the wall: ");
            if (pr.Status != PromptStatus.OK) return null;
            var at = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var kind = new PromptKeywordOptions("\nThe gap is a [Door/Window] <Door>: ", "Door Window") { AllowNone = true };
            kind.Keywords.Default = "Door";
            var kr = ed.GetKeywords(kind);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return null;
            bool door = kr.Status == PromptStatus.None || kr.StringResult == "Door";
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var info = GapAt(tr, (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForRead), new P2(at.X, at.Y), door, ObjectId.Null);
                tr.Commit();
                if (info == null) ed.WriteMessage("\nNo gap with a wall face on both sides was found there. The gap must be 300 mm to 6 m wide and break both face lines in the same place.");
                return info;
            }
        }

        /// <summary>A block that is not one of ours, standing in a gap of the wall: its opening is the gap. Door or window is guessed from the block name, or asked.</summary>
        private static OpeningInfo ForeignBlock(Editor ed, Transaction tr, ObjectId id, bool ask)
        {
            var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
            if (br == null) return null;
            Extents3d e;
            try { e = br.GeometricExtents; } catch (System.Exception) { return null; }
            var centre = new P2((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2);
            string name = (BlockOpenings.EffectiveName(tr, br) ?? "").ToUpperInvariant();
            bool door = name.Contains("DOOR") || name.StartsWith("D") || name.Contains("DR");
            bool window = name.Contains("WIN") || name.StartsWith("W") || name.Contains("GLAZ");
            if (door == window)
            {
                door = true;
                if (ask)
                {
                    var kind = new PromptKeywordOptions("\n" + name + " is a [Door/Window] <Door>: ", "Door Window") { AllowNone = true };
                    kind.Keywords.Default = "Door";
                    var kr = ed.GetKeywords(kind);
                    if (kr.Status == PromptStatus.OK) door = kr.StringResult == "Door";
                }
            }
            var info = GapAt(tr, (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForRead), centre, door, id);
            if (info != null) info.Layer = br.Layer;
            return info;
        }

        /// <summary>
        /// The opening a break in both faces of a straight wall makes, as an opening the move, slide and replace commands can work on.
        /// The faces and ends come from the gap; the swing defaults to the left of the wall's direction.
        /// </summary>
        internal static OpeningInfo GapAt(Transaction tr, BlockTableRecord space, P2 at, bool door, ObjectId block)
        {
            double mm = Util.MmToDrawingUnits(1.0), tol = 2 * mm;
            var segs = new List<WallSegment>();
            foreach (ObjectId sid in space)
            {
                if (sid.ObjectClass.DxfName != "LINE" && sid.ObjectClass.DxfName != "LWPOLYLINE") continue;
                var ent = tr.GetObject(sid, OpenMode.ForRead) as Entity;
                var ln = ent as Line; var pl = ent as Polyline;
                if (ln != null) segs.Add(new WallSegment(ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y));
                else if (pl != null)
                {
                    int n = pl.NumberOfVertices, count = pl.Closed ? n : n - 1;
                    for (int i = 0; i < count; i++)
                    {
                        if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                        var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % n);
                        segs.Add(new WallSegment(a.X, a.Y, b.X, b.Y));
                    }
                }
            }
            var gaps = WallGaps.Find(segs, 1 * mm, 300 * mm, 6000 * mm, MaxThickMm * mm, 5 * mm);
            var g = gaps.FirstOrDefault(x =>
            {
                double along = x.Horizontal ? at.X : at.Y, across = x.Horizontal ? at.Y : at.X;
                return along >= x.Lo - tol && along <= x.Hi + tol
                    && across >= Math.Min(x.Face1, x.Face2) - tol && across <= Math.Max(x.Face1, x.Face2) + tol;
            });
            if (g == null) return null;

            var u = g.Horizontal ? new P2(1, 0) : new P2(0, 1);
            var normal = new P2(-u.Y, u.X);
            Func<double, double, P2> pt = (a, c) => g.Horizontal ? new P2(a, c) : new P2(c, a);
            var corners = new OpeningFrame.Corners
            {
                FaceAStart = pt(g.Lo, g.Face1), FaceAEnd = pt(g.Hi, g.Face1),
                FaceBStart = pt(g.Lo, g.Face2), FaceBEnd = pt(g.Hi, g.Face2),
                Swing = door ? normal : new P2(0, 0), Flipped = false,
            };
            corners.Centre = (corners.FaceAStart + corners.FaceAEnd) * 0.5;
            return new OpeningInfo
            {
                Id = block, Door = door, Type = door ? "single" : "", WidthMm = g.Width / mm, ThicknessMm = Math.Abs(g.Face2 - g.Face1) / mm,
                SillMm = door ? 0 : Settings.GetDouble("WindowSillMm", 900),
                HeightMm = door ? Settings.GetDouble("DoorHeightMm", 2100) : Settings.GetDouble("WindowHeightMm", 1200),
                Corners = corners, Z = 0, Foreign = true,
            };
        }

        /// <summary>The doors and windows made by the tools among the selected blocks, and how many other objects were skipped.</summary>
        private static List<OpeningInfo> SelectOpenings(Editor ed, string prompt, out int skipped)
        {
            skipped = 0;
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = prompt },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "INSERT") }));
            if (Util.NoSelection(psr.Status))
            {
                // Enter: an opening drawn as a break in the wall, picked by a point inside it.
                var gap = PickGap(ed);
                return gap == null ? null : new List<OpeningInfo> { gap };
            }
            if (psr.Status != PromptStatus.OK) return null;
            var list = new List<OpeningInfo>();
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                foreach (var id in psr.Value.GetObjectIds())
                {
                    var info = ReadOpening(tr, id) ?? ForeignBlock(ed, tr, id, false);
                    if (info == null) skipped++; else list.Add(info);
                }
                tr.Commit();
            }
            return list;
        }

        /// <summary>The openings whose centre lies inside the box (drawing units).</summary>
        internal static List<OpeningInfo> CollectInside(Transaction tr, BlockTableRecord space, double minX, double minY, double maxX, double maxY)
        {
            var found = new List<OpeningInfo>();
            foreach (ObjectId id in space)
            {
                if (id.ObjectClass.DxfName != "INSERT") continue;
                var info = ReadOpening(tr, id);
                if (info == null) continue;
                var c = info.Corners.Centre;
                if (c.X >= minX && c.X <= maxX && c.Y >= minY && c.Y <= maxY) found.Add(info);
            }
            return found;
        }

        /// <summary>
        /// Takes an opening out of the wall: erases its block, tag and jamb lines and, when bridge is true, bridges the gap in both faces.
        /// Returns the tag text it had (null if no tag was found).
        /// </summary>
        internal static string Heal(Transaction tr, BlockTableRecord space, OpeningInfo info, bool bridge = true)
        {
            double mm = Util.MmToDrawingUnits(1.0);
            double tol = 2 * mm;                         // the block name rounds the wall thickness to a whole mm
            var c = info.Corners;
            double h = Util.MmToDrawingUnits(TagHeightMm);

            bool hadLintel = EraseLintel(tr, space, (c.FaceAStart + c.FaceAEnd + c.FaceBStart + c.FaceBEnd) * 0.25, 10 * mm);

            string wallLayer = null;
            var pairs = new[] { new[] { c.FaceAStart, c.FaceBStart }, new[] { c.FaceAEnd, c.FaceBEnd } };
            var rx = new Regex("^[DW]\\d+(/\\d+)?$", RegexOptions.IgnoreCase);
            DBText tagText = null; double tagDist = double.MaxValue;
            var lines = new List<Line>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var ln = ent as Line;
                if (ln != null) { lines.Add(ln); continue; }
                var t = ent as DBText;
                if (t != null && string.Equals(t.Layer, LayerTag, StringComparison.OrdinalIgnoreCase) && rx.IsMatch(t.TextString ?? ""))
                {
                    double d = new P2(t.Position.X, t.Position.Y).DistanceTo(c.Centre);
                    double reach = Math.Max(info.WidthMm * mm, 4 * h);
                    if (d <= reach && d < tagDist) { tagText = t; tagDist = d; }
                }
            }

            // Jamb lines run across the wall between the two face corners at each end of the opening.
            foreach (var pair in pairs)
            {
                foreach (var ln in lines)
                {
                    var a = new P2(ln.StartPoint.X, ln.StartPoint.Y); var b = new P2(ln.EndPoint.X, ln.EndPoint.Y);
                    bool match = (a.DistanceTo(pair[0]) <= tol && b.DistanceTo(pair[1]) <= tol)
                              || (a.DistanceTo(pair[1]) <= tol && b.DistanceTo(pair[0]) <= tol);
                    if (!match || ln.IsErased) continue;
                    wallLayer = ln.Layer;
                    ln.UpgradeOpen();
                    ln.Erase();
                    break;
                }
            }

            // Bridge each face across the gap, with the look of the face line next to it.
            if (bridge)
            {
                var faces = new[] { new[] { c.FaceAStart, c.FaceAEnd }, new[] { c.FaceBStart, c.FaceBEnd } };
                foreach (var face in faces)
                {
                    Line like = null;
                    foreach (var ln in lines)
                    {
                        if (ln.IsErased) continue;
                        if (wallLayer != null && !string.Equals(ln.Layer, wallLayer, StringComparison.OrdinalIgnoreCase)) continue;
                        var a = new P2(ln.StartPoint.X, ln.StartPoint.Y); var b = new P2(ln.EndPoint.X, ln.EndPoint.Y);
                        if (a.DistanceTo(face[0]) <= tol || a.DistanceTo(face[1]) <= tol || b.DistanceTo(face[0]) <= tol || b.DistanceTo(face[1]) <= tol) { like = ln; break; }
                    }
                    Line bridgeLine;
                    if (like != null)
                    {
                        bridgeLine = (Line)like.Clone();
                        bridgeLine.StartPoint = new Point3d(face[0].X, face[0].Y, info.Z);
                        bridgeLine.EndPoint = new Point3d(face[1].X, face[1].Y, info.Z);
                    }
                    else
                    {
                        bridgeLine = new Line(new Point3d(face[0].X, face[0].Y, info.Z), new Point3d(face[1].X, face[1].Y, info.Z)) { Layer = wallLayer ?? WallCommands.WallLayer };
                    }
                    space.AppendEntity(bridgeLine);
                    tr.AddNewlyCreatedDBObject(bridgeLine, true);
                }
            }

            string tag = tagText?.TextString;
            if (hadLintel && tag != null) LintelTags.Add(tag);
            if (tagText != null) { tagText.UpgradeOpen(); tagText.Erase(); }
            if (!info.Id.IsNull)
            {
                var block = tr.GetObject(info.Id, OpenMode.ForWrite);
                block.Erase();
            }
            return tag;
        }

        /// <summary>Asks the side a door opens to. With previous swing, Enter keeps it.</summary>
        private static bool AskSwing(Editor ed, bool hasPrevious, ref bool flip, out Point3d? side)
        {
            side = null;
            while (true)
            {
                var o = new PromptPointOptions("\nPick the side the door opens to [Flip hinge]" + (hasPrevious ? " <same as before>" : "") + ": ", "Flip")
                    { AllowNone = hasPrevious };
                var r = ed.GetPoint(o);
                if (r.Status == PromptStatus.Keyword) { flip = !flip; ed.WriteMessage("\nHinge on the other end."); continue; }
                if (r.Status == PromptStatus.None && hasPrevious) return true;
                if (r.Status != PromptStatus.OK) return false;
                side = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                return true;
            }
        }

        /// <summary>
        /// Moves one opening to a point you pick on a wall, or several together by the distance between two points. Each is closed up
        /// where it was and cut where it goes, keeping its width, type, heights and tag number. If any cannot be placed, nothing changes.
        /// </summary>
        [CommandMethod("HCWOPENMOVE")]
        public void MoveOpening() { MoveOpeningCore(); AfterEdit(); }

        private void MoveOpeningCore()
        {
            var ed = Util.Ed;
            string job = Util.AskMode("Edit openings", "Move", "Slide", "Replace", "Convert", "Heights", "Lintel", "Sync");
            if (job == null) return;
            if (job == "Slide") { SlideOpening(); return; }
            if (job == "Convert") { ConvertBlocks(); return; }
            if (job == "Heights") { SetHeights(); return; }
            if (job == "Lintel") { GenerateLintel(); return; }
            if (job == "Replace") { ReplaceOpening(); return; }
            if (job == "Sync") { SyncSchedule(); return; }
            int skipped;
            var infos = SelectOpenings(ed, "\nSelect the doors and windows to move (Enter to pick a gap in the wall): ", out skipped);
            if (infos == null) return;
            if (infos.Count == 0) { ed.WriteMessage("\nHCWOPENMOVE: none of that is a door or window made by HCWDOOR or HCWWINDOW."); return; }
            if (skipped > 0) ed.WriteMessage("\n" + skipped + " other object(s) skipped.");

            var ucs = ed.CurrentUserCoordinateSystem;
            var targets = new List<Point3d>();
            var sides = new List<Point3d?>();
            var flips = new List<bool>();
            if (infos.Count == 1)
            {
                var info = infos[0];
                var pr = ed.GetPoint(new PromptPointOptions("\nPick the new position on the wall: "));
                if (pr.Status != PromptStatus.OK) return;
                var pick = pr.Value.TransformBy(ucs);
                bool flip = info.Corners.Flipped;
                Point3d? side = null;
                if (info.Door && !AskSwing(ed, !info.Foreign, ref flip, out side)) return;
                targets.Add(new Point3d(pick.X, pick.Y, info.Z)); sides.Add(side); flips.Add(flip);
            }
            else
            {
                var bp = ed.GetPoint("\nBase point: ");
                if (bp.Status != PromptStatus.OK) return;
                var tp = ed.GetPoint(new PromptPointOptions("\nSecond point (the openings move by this distance): ") { UseBasePoint = true, BasePoint = bp.Value, UseDashedLine = true });
                if (tp.Status != PromptStatus.OK) return;
                var d = tp.Value.TransformBy(ucs) - bp.Value.TransformBy(ucs);
                foreach (var i in infos)
                {
                    targets.Add(new Point3d(i.Corners.Centre.X + d.X, i.Corners.Centre.Y + d.Y, i.Z));
                    sides.Add(null); flips.Add(i.Corners.Flipped);
                }
            }

            string message;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                var tags = infos.Select(i => Heal(tr, space, i)).ToList();
                var report = new List<string>();
                for (int k = 0; k < infos.Count; k++)
                {
                    var info = infos[k];
                    Point3d sideW = sides[k] ?? PreviousSide(info, targets[k]);
                    if (!PlaceIn(tr, info.Door, targets[k], sideW, flips[k], info.WidthMm, ParamsOf(info), tags[k], out message))
                    {
                        ed.WriteMessage("\nHCWOPENMOVE: " + (tags[k] ?? "an opening") + ": " + message + " Nothing was moved.");
                        return;                                         // uncommitted: every heal is rolled back
                    }
                    report.Add(message);
                }
                tr.Commit();
                ed.WriteMessage("\nHCWOPENMOVE: " + (infos.Count == 1 ? report[0] : infos.Count + " openings moved."));
            }
        }

        /// <summary>HCWOPENSLIDE drags a door or window along its wall: a ghost of the opening follows the cursor and the move is made where you click.</summary>
        [CommandMethod("HCWOPENSLIDE")]
        public void SlideOpening() { SlideOpeningCore(); AfterEdit(); }

        private void SlideOpeningCore()
        {
            var ed = Util.Ed;
            var info = SelectOpening(ed, "\nSelect the door or window to slide: ");
            if (info == null) return;
            var along = info.Corners.FaceAEnd - info.Corners.FaceAStart;
            double len = along.Length;
            if (len < 1e-9) return;
            var jig = new SlideJig(new Point3d(info.Corners.Centre.X, info.Corners.Centre.Y, info.Z), new Vector3d(along.X / len, along.Y / len, 0),
                len, info.ThicknessMm * Util.MmToDrawingUnits(1.0));
            var res = ed.Drag(jig);
            if (res.Status != PromptStatus.OK) return;
            double slide = jig.Distance;
            if (Math.Abs(slide) < 1e-9) return;

            string message;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                string tag = Heal(tr, space, info);
                var target = new Point3d(info.Corners.Centre.X + jig.Direction.X * slide, info.Corners.Centre.Y + jig.Direction.Y * slide, info.Z);
                if (!PlaceIn(tr, info.Door, target, PreviousSide(info, target), info.Corners.Flipped, info.WidthMm, ParamsOf(info), tag, out message))
                {
                    ed.WriteMessage("\nHCWOPENSLIDE: " + message + " The opening was left where it was.");
                    return;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWOPENSLIDE: " + message);
        }

        private class SlideJig : DrawJig
        {
            private readonly Point3d _centre;
            private readonly double _length, _thickness;
            public Vector3d Direction { get; }
            public double Distance { get; private set; }

            public SlideJig(Point3d centre, Vector3d direction, double length, double thickness)
            { _centre = centre; Direction = direction; _length = length; _thickness = thickness; }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var o = new JigPromptPointOptions("\nSlide to (click where the opening should go): ")
                    { UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted };
                var r = prompts.AcquirePoint(o);
                if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                double d = (r.Value - _centre).DotProduct(Direction);
                if (Math.Abs(d - Distance) < 1e-9) return SamplerStatus.NoChange;
                Distance = d;
                return SamplerStatus.OK;
            }

            protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
            {
                var c = _centre + Direction * Distance;
                var n = new Vector3d(-Direction.Y, Direction.X, 0);
                var a = c - Direction * (_length / 2) - n * (_thickness / 2);
                var b = c + Direction * (_length / 2) - n * (_thickness / 2);
                var d = c + Direction * (_length / 2) + n * (_thickness / 2);
                var e = c - Direction * (_length / 2) + n * (_thickness / 2);
                draw.Geometry.WorldLine(a, b); draw.Geometry.WorldLine(b, d); draw.Geometry.WorldLine(d, e); draw.Geometry.WorldLine(e, a);
                return true;
            }
        }

        [CommandMethod("HCWOPENREPLACE")]
        public void ReplaceOpening() { ReplaceOpeningCore(); AfterEdit(); }

        private void ReplaceOpeningCore()
        {
            var ed = Util.Ed;
            var info = SelectOpening(ed, "\nSelect the door or window to replace: ");
            if (info == null) return;

            var ko = new PromptKeywordOptions("\nReplace with [Door/Window] <" + (info.Door ? "Door" : "Window") + ">: ", "Door Window") { AllowNone = true };
            ko.Keywords.Default = info.Door ? "Door" : "Window";
            var kr = ed.GetKeywords(ko);
            bool door = info.Door;
            if (kr.Status == PromptStatus.OK) door = kr.StringResult == "Door";
            else if (kr.Status != PromptStatus.None) return;

            double defaultMm = door == info.Door ? info.WidthMm : (door ? _doorMm : _windowMm);
            var wr = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Width", defaultMm))
                { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(defaultMm), UseDefaultValue = true });
            if (wr.Status != PromptStatus.OK) return;
            double widthMm = Util.TypedToMm(wr.Value, defaultMm);

            string type = "";
            if (door)
            {
                string cur = info.Door && info.Type.Length > 0 ? info.Type : _doorType;
                var to = new PromptKeywordOptions("\nDoor type [Single/Double/Sliding] <" + cur + ">: ", "Single Double Sliding") { AllowNone = true };
                to.Keywords.Default = char.ToUpperInvariant(cur[0]) + cur.Substring(1);
                var tr0 = ed.GetKeywords(to);
                type = tr0.Status == PromptStatus.OK ? tr0.StringResult.ToLowerInvariant() : cur;
                if (tr0.Status != PromptStatus.OK && tr0.Status != PromptStatus.None) return;
            }
            double defHeight = door == info.Door ? info.HeightMm : (door ? _doorHeightMm : _windowHeightMm);
            var hr = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Height", defHeight))
                { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(defHeight), UseDefaultValue = true });
            if (hr.Status != PromptStatus.OK) return;
            double heightMm = Util.TypedToMm(hr.Value, defHeight);
            double sill = 0;
            if (!door)
            {
                double defSill = !info.Door ? info.SillMm : _windowSillMm;
                var sr = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Sill height", defSill))
                    { AllowNegative = false, AllowZero = true, DefaultValue = Util.MmToUnitsRounded(defSill), UseDefaultValue = true });
                if (sr.Status != PromptStatus.OK) return;
                sill = Util.TypedToMm(sr.Value, defSill);
            }

            bool flip = door && info.Door && info.Corners.Flipped;
            Point3d? side = null;
            if (door && !AskSwing(ed, info.Door && !info.Foreign, ref flip, out side)) return;

            var centre = new Point3d(info.Corners.Centre.X, info.Corners.Centre.Y, info.Z);
            string message;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                string tag = Heal(tr, space, info);
                Point3d sideW = side ?? PreviousSide(info, centre);
                string keep = door == info.Door ? tag : null;           // the same kind keeps its number
            if (!door && info.Door == door && (Math.Abs(widthMm - info.WidthMm) > 0.5 || Math.Abs(heightMm - info.HeightMm) > 0.5 || Math.Abs(sill - info.SillMm) > 0.5))
                keep = null;                                         // a different window size is a different code
                var par = new OpeningParams { Type = type, SillMm = sill, HeightMm = heightMm };
                if (!PlaceIn(tr, door, centre, sideW, flip, widthMm, par, keep, out message))
                {
                    ed.WriteMessage("\nHCWOPENREPLACE: " + message + " The opening was left as it was.");
                    return;
                }
                tr.Commit();
            }
            if (door) { _doorMm = widthMm; _doorType = type; _doorHeightMm = heightMm; } else { _windowMm = widthMm; _windowHeightMm = heightMm; _windowSillMm = sill; }
            ed.WriteMessage("\nHCWOPENREPLACE: " + message);
        }

        /// <summary>
        /// Swaps doors and windows that are other blocks (or gaps in the wall) for the blocks HCWDOOR and HCWWINDOW make, each keeping its
        /// width, position and tag, so the take-off and the schedule read one component. If any cannot be placed, nothing changes.
        /// </summary>
        [CommandMethod("HCWOPENCONVERT")]
        public void ConvertBlocks() { ConvertBlocksCore(); AfterEdit(); }

        private void ConvertBlocksCore()
        {
            var ed = Util.Ed;
            int skipped;
            var infos = SelectOpenings(ed, "\nSelect the existing door and window blocks to replace with the wall and opening blocks: ", out skipped);
            if (infos == null) return;
            infos = infos.Where(i => i.Foreign).ToList();
            if (infos.Count == 0) { ed.WriteMessage("\nHCWOPENCONVERT: none of that is a foreign block standing in a wall gap (blocks from the tools are already in use)."); return; }
            if (skipped > 0) ed.WriteMessage("\n" + skipped + " other object(s) skipped.");

            string message;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                var olds = infos.Where(i => !i.Id.IsNull).Select(i => i.Id).ToList();
                var tags = infos.Select(i => Heal(tr, space, i)).ToList();
                foreach (var oid in olds)
                {
                    var ent = tr.GetObject(oid, OpenMode.ForWrite) as Entity;
                    if (ent != null && !ent.IsErased) ent.Erase();
                }
                for (int k = 0; k < infos.Count; k++)
                {
                    var i = infos[k];
                    var centre = new Point3d(i.Corners.Centre.X, i.Corners.Centre.Y, i.Z);
                    if (!PlaceIn(tr, i.Door, centre, PreviousSide(i, centre), i.Corners.Flipped, i.WidthMm, ParamsOf(i), null, out message))
                    {
                        ed.WriteMessage("\nHCWOPENCONVERT: " + message + " Nothing was changed.");
                        return;
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWOPENCONVERT: " + infos.Count + " block(s) replaced. Run HCWOPENSYNC to fill the door and window schedule.");
        }

        /// <summary>
        /// Generate lintel: select doors and windows (made by the tools) and a lintel is drawn through the wall over each: a line on MEASURE-LINTEL
        /// as long as the opening plus the bearing at each end, and a dashed outline as wide as the whole wall. The wall is not cut or changed.
        /// Choose Remove to take the lintels off the selected openings. An opening that has one is redrawn, never doubled.
        /// </summary>
        [CommandMethod("HCWLINTEL")]
        public void GenerateLintel()
        {
            var ed = Util.Ed;
            var ko = new PromptKeywordOptions("\nLintel [Generate/Remove] <Generate>: ", "Generate Remove") { AllowNone = true };
            ko.Keywords.Default = "Generate";
            var kr = ed.GetKeywords(ko);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            bool remove = kr.Status == PromptStatus.OK && kr.StringResult == "Remove";
            int skipped;
            var infos = SelectOpenings(ed, "\nSelect the doors and windows to " + (remove ? "remove the lintel from" : "generate a lintel for") + " (Enter to pick a gap in the wall): ", out skipped);
            if (infos == null) return;
            if (infos.Count == 0) { ed.WriteMessage("\nHCWLINTEL: none of that is a door or window made by the tools."); return; }
            if (skipped > 0) ed.WriteMessage("\n" + skipped + " other object(s) skipped.");
            double mm = Util.MmToDrawingUnits(1.0);
            int done = 0;
            var depths = new List<string>();
            LintelBeyondTable = false;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(Util.Db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var i in infos)
                {
                    var c = i.Corners;
                    var centre = (c.FaceAStart + c.FaceAEnd + c.FaceBStart + c.FaceBEnd) * 0.25;
                    EraseLintel(tr, space, centre, 10 * mm);
                    if (!remove)
                    {
                        var dir = c.FaceAEnd - c.FaceAStart;
                        double depth = AddLintel(tr, Util.Db, space, centre, dir, dir.Length, i.ThicknessMm * mm, i.Z);
                        depths.Add(Math.Round(i.WidthMm) + " mm opening: " + Math.Round(depth, 1) + " mm deep");
                    }
                    done++;
                }
                tr.Commit();
            }
            if (!remove && depths.Count > 0) ed.WriteMessage("\nLintel depths (setting LintelDepthTable): " + string.Join("; ", depths.Distinct()) + ".");
            if (LintelBeyondTable) ed.WriteMessage("\nWARNING: an opening is wider than the lintel depth table covers (" + LintelDepthTable() + "); the deepest step was used.");
            ed.WriteMessage("\nHCWLINTEL: lintel " + (remove ? "removed from " : "generated for ") + done + " opening(s)" + (remove ? "." : ", through the full wall thickness with " + Settings.GetDouble("LintelBearingMm", 230) + " mm bearing each end."));
        }

        /// <summary>
        /// Sets the height, sill and lintel of doors and windows already in the drawing. Choose the levels of the first floor (door to the lintel
        /// bottom, window sill kept, head at the lintel bottom) or type the figures. The invisible SILL, HEIGHT and LINTEL attributes are rewritten,
        /// so HCWOPENSYNC, the take-off and HCWSECTIONDRAW follow.
        /// </summary>
        [CommandMethod("HCWOPENHEIGHT")]
        public void SetHeights() { SetHeightsCore(); AfterEdit(); }

        private void SetHeightsCore()
        {
            var ed = Util.Ed;
            int skipped;
            var infos = SelectOpenings(ed, "\nSelect the doors and windows to set heights on (Enter to pick a gap in the wall): ", out skipped);
            if (infos == null) return;
            var mine = infos.Where(i => !i.Foreign).ToList();
            if (mine.Count == 0)
            {
                ed.WriteMessage("\nHCWOPENHEIGHT: none of that is a door or window made by the tools. Use Convert on HCWOPENMOVE first to swap other blocks for them.");
                return;
            }
            if (infos.Count > mine.Count || skipped > 0) ed.WriteMessage("\n" + (infos.Count - mine.Count + skipped) + " other object(s) skipped.");

            var lv = LevelStore.First();
            bool fromLevels = false;
            if (lv != null && lv.LintelMm > 0)
            {
                var ko = new PromptKeywordOptions("\nHeights from [Levels/Typed] <Levels>: ", "Levels Typed") { AllowNone = true };
                ko.Keywords.Default = "Levels";
                var kr = ed.GetKeywords(ko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                fromLevels = kr.Status == PromptStatus.None || kr.StringResult == "Levels";
            }
            double doorH = _doorHeightMm, winH = _windowHeightMm, sill = _windowSillMm;
            if (fromLevels) { doorH = Math.Round(lv.LintelMm); }
            else
            {
                if (mine.Any(i => i.Door))
                {
                    var r = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Door height", doorH)) { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(doorH), UseDefaultValue = true });
                    if (r.Status != PromptStatus.OK) return; doorH = Util.TypedToMm(r.Value, doorH);
                }
                if (mine.Any(i => !i.Door))
                {
                    var r = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Window sill height", sill)) { AllowNegative = false, AllowZero = true, DefaultValue = Util.MmToUnitsRounded(sill), UseDefaultValue = true });
                    if (r.Status != PromptStatus.OK) return; sill = Util.TypedToMm(r.Value, sill);
                    var h = ed.GetDouble(new PromptDoubleOptions(Util.LengthPrompt("Window height", winH)) { AllowNegative = false, AllowZero = false, DefaultValue = Util.MmToUnitsRounded(winH), UseDefaultValue = true });
                    if (h.Status != PromptStatus.OK) return; winH = Util.TypedToMm(h.Value, winH);
                }
            }

            int changed = 0;
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                foreach (var i in mine)
                {
                    var br = (BlockReference)tr.GetObject(i.Id, OpenMode.ForRead);
                    double s = i.Door ? 0 : (fromLevels ? i.SillMm : sill);
                    double h = i.Door ? doorH : (fromLevels ? Math.Max(300, Math.Round(lv.LintelMm - s)) : winH);
                    foreach (ObjectId aid in br.AttributeCollection)
                    {
                        var att = (AttributeReference)tr.GetObject(aid, OpenMode.ForWrite);
                        string tag = (att.Tag ?? "").ToUpperInvariant();
                        if (tag == "SILL") att.TextString = Math.Round(s).ToString();
                        else if (tag == "HEIGHT") att.TextString = Math.Round(h).ToString();
                        else if (tag == "LINTEL") att.TextString = Math.Round(s + h).ToString();
                    }
                    changed++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\nHCWOPENHEIGHT: " + changed + " opening(s) updated. Run HCWOPENSYNC to refresh the schedule.");
        }

        /// <summary>A point on the side the old door swung to, beyond the thickest wall, for use at a new position.</summary>
        internal static Point3d PreviousSide(OpeningInfo info, Point3d at)
        {
            double far = 2 * Util.MmToDrawingUnits(MaxThickMm);
            var s = info.Corners.Swing;
            return new Point3d(at.X + s.X * far, at.Y + s.Y * far, at.Z);
        }

        /// <summary>Straight segments of lines and polylines whose box comes within reach of the pick.</summary>
        private static List<SegRef> Segments(Transaction tr, BlockTableRecord space, P2 pick, double reach)
        {
            var res = new List<SegRef>();
            foreach (ObjectId id in space)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (!(ent is Line) && !(ent is Polyline)) continue;
                try
                {
                    var e = ent.GeometricExtents;
                    if (pick.X < e.MinPoint.X - reach || pick.X > e.MaxPoint.X + reach
                        || pick.Y < e.MinPoint.Y - reach || pick.Y > e.MaxPoint.Y + reach) continue;
                }
                catch (System.Exception) { continue; }

                var line = ent as Line;
                if (line != null)
                {
                    res.Add(new SegRef { Owner = id, Layer = line.Layer, Seg = new Seg(new P2(line.StartPoint.X, line.StartPoint.Y), new P2(line.EndPoint.X, line.EndPoint.Y)) });
                    continue;
                }
                var pl = (Polyline)ent;
                int count = pl.NumberOfVertices;
                int segs = pl.Closed ? count : count - 1;
                for (int i = 0; i < segs; i++)
                {
                    if (pl.GetSegmentType(i) != SegmentType.Line) continue;
                    var a = pl.GetPoint2dAt(i); var b = pl.GetPoint2dAt((i + 1) % count);
                    res.Add(new SegRef { Owner = id, Layer = pl.Layer, Seg = new Seg(new P2(a.X, a.Y), new P2(b.X, b.Y)) });
                }
            }
            return res;
        }

        /// <summary>The line for a wall face: the line itself, or the matching piece of an exploded polyline.</summary>
        private static Line Resolve(Transaction tr, BlockTableRecord space, SegRef r, Dictionary<ObjectId, List<Line>> exploded)
        {
            List<Line> pieces;
            if (!exploded.TryGetValue(r.Owner, out pieces))
            {
                var ent = tr.GetObject(r.Owner, OpenMode.ForWrite);
                var line = ent as Line;
                if (line != null) return line;
                var pl = ent as Polyline;
                if (pl == null) return null;

                var parts = new DBObjectCollection();
                pl.Explode(parts);
                pieces = new List<Line>();
                foreach (DBObject o in parts)
                {
                    var piece = (Entity)o;
                    piece.SetPropertiesFrom(pl);
                    var xd = pl.XData;
                    if (xd != null) piece.XData = xd;      // pieces stay part of the same wall object
                    space.AppendEntity(piece);
                    tr.AddNewlyCreatedDBObject(piece, true);
                    if (piece is Line l) pieces.Add(l);
                }
                pl.Erase();
                exploded[r.Owner] = pieces;
            }
            double tol = Util.MmToDrawingUnits(0.05);
            foreach (var l in pieces)
            {
                if (l.IsErased) continue;
                var a = new P2(l.StartPoint.X, l.StartPoint.Y); var b = new P2(l.EndPoint.X, l.EndPoint.Y);
                bool same = a.DistanceTo(r.Seg.A) <= tol && b.DistanceTo(r.Seg.B) <= tol;
                bool flipped = a.DistanceTo(r.Seg.B) <= tol && b.DistanceTo(r.Seg.A) <= tol;
                if (same || flipped) return l;
            }
            return null;
        }

        /// <summary>Removes the stretch between two points on a line, keeping what is left at either end.</summary>
        private static void Cut(Transaction tr, BlockTableRecord space, Line ln, P2 a, P2 b)
        {
            var s = ln.StartPoint; var e = ln.EndPoint;
            var seg = new Seg(new P2(s.X, s.Y), new P2(e.X, e.Y));
            if (seg.Length < 1e-9) return;
            var u = (seg.B - seg.A) * (1.0 / seg.Length);
            double ta = P2.Dot(a - seg.A, u), tb = P2.Dot(b - seg.A, u);
            var pieces = OpeningCut.Remove(seg, Math.Min(ta, tb), Math.Max(ta, tb));
            if (pieces.Count == 0) { ln.Erase(); return; }
            double z = s.Z;
            if (pieces.Count > 1)
            {
                var more = (Line)ln.Clone();
                more.StartPoint = new Point3d(pieces[1].A.X, pieces[1].A.Y, z);
                more.EndPoint = new Point3d(pieces[1].B.X, pieces[1].B.Y, z);
                space.AppendEntity(more);
                tr.AddNewlyCreatedDBObject(more, true);
            }
            ln.StartPoint = new Point3d(pieces[0].A.X, pieces[0].A.Y, z);
            ln.EndPoint = new Point3d(pieces[0].B.X, pieces[0].B.Y, z);
        }

        private static void AddLine(Transaction tr, BlockTableRecord space, P2 a, P2 b, string layer, double z)
        {
            var ln = new Line(new Point3d(a.X, a.Y, z), new Point3d(b.X, b.Y, z)) { Layer = layer };
            space.AppendEntity(ln);
            tr.AddNewlyCreatedDBObject(ln, true);
        }

        /// <summary>
        /// Door block: wall body below the x axis (y from -thickness to 0), a frame at each jamb (DoorFrameMm), and the leaf or leaves
        /// opened 90 degrees up from the hinge at the origin with their swing arcs (single), or two leaves hinged at each end (double),
        /// or two panels sliding past each other in the middle of the wall (sliding). The opening length is a line on MEASURE-DEDUCT
        /// through the middle of the wall. The SILL, HEIGHT and LINTEL attributes are invisible and carry the schedule figures.
        /// </summary>
        private static void EnsureDoorBlock(Transaction tr, Database db, string name, string type, double w, double thickness)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name)) return;
            Util.EnsureHcwLayer(tr, db, LayerDoor);
            Util.EnsureLayer(tr, db, MeasureCommands.LayDed, 6);
            var def = NewBlock(tr, bt, name);
            double mm = Util.MmToDrawingUnits(1.0);
            double leaf = Settings.GetDouble("DoorLeafMm", 40) * mm, frame = Settings.GetDouble("DoorFrameMm", 50) * mm;

            if (frame > 0)
            {
                Add(tr, def, Rect(0, -thickness, frame, 0, LayerDoor));
                Add(tr, def, Rect(w - frame, -thickness, w, 0, LayerDoor));
            }
            if (string.Equals(type, "sliding", StringComparison.OrdinalIgnoreCase))
            {
                double lap = 0.05 * w;
                double l = Math.Max(leaf, thickness / 10);
                Add(tr, def, Rect(0, -thickness / 2 - l, w / 2 + lap, -thickness / 2, LayerDoor));
                Add(tr, def, Rect(w / 2 - lap, -thickness / 2, w, -thickness / 2 + l, LayerDoor));
            }
            else if (string.Equals(type, "double", StringComparison.OrdinalIgnoreCase))
            {
                Add(tr, def, LeafOrLine(0, 0, leaf, w / 2, LayerDoor));
                Add(tr, def, Rect(w - leaf, 0, w, w / 2, LayerDoor));
                Add(tr, def, new Arc(Point3d.Origin, w / 2, 0, Math.PI / 2) { Layer = LayerDoor });
                Add(tr, def, new Arc(new Point3d(w, 0, 0), w / 2, Math.PI / 2, Math.PI) { Layer = LayerDoor });
            }
            else
            {
                Add(tr, def, LeafOrLine(0, 0, leaf, w, LayerDoor));
                Add(tr, def, new Arc(Point3d.Origin, w, 0, Math.PI / 2) { Layer = LayerDoor });
            }
            Add(tr, def, new Line(new Point3d(0, -thickness / 2, 0), new Point3d(w, -thickness / 2, 0)) { Layer = MeasureCommands.LayDed });
            AddScheduleAttributes(tr, def, thickness);
        }

        /// <summary>Window block: four lines across the opening (the two faces and two frame lines), wall body above the x axis.</summary>
        private static void EnsureWindowBlock(Transaction tr, Database db, string name, double w, double thickness)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name)) return;
            Util.EnsureHcwLayer(tr, db, LayerWin);
            Util.EnsureLayer(tr, db, MeasureCommands.LayDed, 6);
            var def = NewBlock(tr, bt, name);
            for (int k = 0; k <= 3; k++)
            {
                double y = thickness * k / 3.0;
                Add(tr, def, new Line(new Point3d(0, y, 0), new Point3d(w, y, 0)) { Layer = LayerWin });
            }
            Add(tr, def, new Line(new Point3d(0, thickness / 2, 0), new Point3d(w, thickness / 2, 0)) { Layer = MeasureCommands.LayDed });
            AddScheduleAttributes(tr, def, thickness);
        }

        private static Entity Rect(double x1, double y1, double x2, double y2, string layer)
        {
            var pl = new Polyline();
            pl.AddVertexAt(0, new Point2d(x1, y1), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x2, y1), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x2, y2), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x1, y2), 0, 0, 0);
            pl.Closed = true;
            pl.Layer = layer;
            return pl;
        }

        /// <summary>A door leaf as a thin rectangle, or a plain line when the leaf has no thickness.</summary>
        private static Entity LeafOrLine(double x, double y, double leaf, double length, string layer) =>
            leaf > 0 ? Rect(x, y, x + leaf, y + length, layer) : new Line(new Point3d(x, y, 0), new Point3d(x, y + length, 0)) { Layer = layer };

        private static void AddScheduleAttributes(Transaction tr, BlockTableRecord def, double thickness)
        {
            double h = Math.Max(thickness / 4, 1e-6);
            foreach (var tag in new[] { "SILL", "HEIGHT", "LINTEL" })
                Add(tr, def, new AttributeDefinition
                {
                    Position = Point3d.Origin, Height = h, Tag = tag, Prompt = tag.Substring(0, 1) + tag.Substring(1).ToLowerInvariant() + " (mm)",
                    TextString = "0", Invisible = true, Layer = "0",
                });
        }

        private static BlockTableRecord NewBlock(Transaction tr, BlockTable bt, string name)
        {
            bt.UpgradeOpen();
            var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(def);
            tr.AddNewlyCreatedDBObject(def, true);
            return def;
        }

        private static void Add(Transaction tr, BlockTableRecord def, Entity ent)
        {
            def.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
        }

        private static void InsertBlock(Transaction tr, BlockTableRecord space, string name, Point3d at, double angle, double sx, double sy, string layer, OpeningParams par)
        {
            var bt = (BlockTable)tr.GetObject(Util.Db.BlockTableId, OpenMode.ForRead);
            var br = new BlockReference(at, bt[name]) { Layer = layer, Rotation = angle, ScaleFactors = new Scale3d(sx, sy, 1) };
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            TitleBlockCommands.AddAttributes(tr, br);
            foreach (ObjectId id in br.AttributeCollection)
            {
                var att = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                string tag = (att.Tag ?? "").ToUpperInvariant();
                if (tag == "SILL") att.TextString = Math.Round(par.SillMm).ToString();
                else if (tag == "HEIGHT") att.TextString = Math.Round(par.HeightMm).ToString();
                else if (tag == "LINTEL") att.TextString = Math.Round(par.SillMm + par.HeightMm).ToString();
            }
        }

        // ---- lintels: every opening gets one, as a measurement line for the take-off and a dashed outline in plan ----

        internal const string LintelApp = "HCW_LINTEL";
        private static string LayerLintel => Util.Out("A-LINTEL");

        /// <summary>
        /// A lintel over a new opening: a line on MEASURE-LINTEL (opening length plus the bearing each side, read by MBML as a concrete lintel)
        /// and a dashed outline as wide as the wall. Both carry the opening's centre so they are removed with it. Setting LintelAuto = 0 leaves them out.
        /// </summary>
        private static double AddLintel(Transaction tr, Database db, BlockTableRecord space, P2 centre, P2 dir, double openingLen, double thickness, double z)
        {
            double mmU = Util.MmToDrawingUnits(1.0);
            bool beyond;
            double depthMm = LintelDepth.For(openingLen / mmU, LintelDepth.Parse(Settings.Get("LintelDepthTable", LintelDepth.Default)), out beyond);
            if (beyond) LintelBeyondTable = true;
            double bearing = Util.MmToDrawingUnits(Settings.GetDouble("LintelBearingMm", 230));
            var box = LintelGeometry.Outline(centre, dir, openingLen, thickness, bearing);

            Util.EnsureLayer(tr, db, MeasureCommands.LayLt, 40);
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!apps.Has(LintelApp))
            {
                apps.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = LintelApp };
                apps.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            // centre, then the lintel's depth, the wall thickness it runs through and the opening it spans (all mm): what the lintel take-off reads
            Func<ResultBuffer> tag = () => new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, LintelApp),
                new TypedValue((int)DxfCode.ExtendedDataReal, centre.X), new TypedValue((int)DxfCode.ExtendedDataReal, centre.Y),
                new TypedValue((int)DxfCode.ExtendedDataReal, depthMm), new TypedValue((int)DxfCode.ExtendedDataReal, thickness / mmU),
                new TypedValue((int)DxfCode.ExtendedDataReal, openingLen / mmU));

            var u = dir * (1.0 / dir.Length);
            var half = u * (LintelGeometry.Length(openingLen, bearing) / 2);
            var line = new Line(new Point3d(centre.X - half.X, centre.Y - half.Y, z), new Point3d(centre.X + half.X, centre.Y + half.Y, z)) { Layer = MeasureCommands.LayLt };
            space.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
            line.XData = tag();

            Util.EnsureHcwLayer(tr, db, LayerLintel);
            var pl = new Polyline { Layer = LayerLintel, Closed = true, Elevation = z };
            for (int i = 0; i < box.Length; i++) pl.AddVertexAt(i, new Point2d(box[i].X, box[i].Y), 0, 0, 0);
            var lt = Util.LoadLinetype(tr, db, "HIDDEN");
            if (lt != ObjectId.Null) pl.LinetypeId = lt;
            space.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
            pl.XData = tag();
            return depthMm;
        }

        private static bool LintelBeyondTable;
        private static string LintelDepthTable() => Settings.Get("LintelDepthTable", LintelDepth.Default);

        /// <summary>Erases the lintel made with an opening (the entities tagged with its centre).</summary>
        private static bool EraseLintel(Transaction tr, BlockTableRecord space, P2 centre, double reach)
        {
            bool erased = false;
            foreach (ObjectId id in space)
            {
                if (id.ObjectClass.DxfName != "LINE" && id.ObjectClass.DxfName != "LWPOLYLINE") continue;
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var rb = ent.GetXDataForApplication(LintelApp);
                if (rb == null) continue;
                var v = rb.AsArray().Where(t => t.TypeCode == (int)DxfCode.ExtendedDataReal).Select(t => (double)t.Value).ToList();
                if (v.Count < 2 || new P2(v[0], v[1]).DistanceTo(centre) > reach) continue;
                ent.UpgradeOpen();
                ent.Erase();
                erased = true;
            }
            return erased;
        }

        /// <summary>Tags of openings whose lintel was taken off by Heal, so the same tag gets its lintel back when the opening is placed again.</summary>
        private static readonly HashSet<string> LintelTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A window's tag is its window code from the schedule and its number among the windows of that code, "W1/3" (setting WindowTagFormat,
        /// {code}/{no} by default). Windows of the same width, height and sill share a code; a size not yet in the schedule gets the next free
        /// code and is added to the schedule at once.
        /// </summary>
        private static string OpeningTag(Transaction tr, Database db, BlockTableRecord space, bool door, double widthMm, double heightMm, double sillMm)
        {
            bool imperial = MeasureCommands.MeasureState.Units == MeasureCommands.UnitSys.Imperial;
            Func<double, double> toBook = mm => imperial ? mm / 25.4 : mm / 1000.0;
            double w = toBook(widthMm), h = toBook(heightMm), sill = toBook(sillMm);
            string kind = door ? "Door" : "Window";
            var book = MeasureBook.Load(tr, db);
            var entry = book.Openings.FirstOrDefault(o => string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(o.Width - w) < 1e-3 && Math.Abs(o.Height - h) < 1e-3 && Math.Abs(o.Sill - sill) < 1e-3);
            if (entry == null)
            {
                entry = new MeasureBook.OpeningSpec { Mark = NewMark(book, door, widthMm, heightMm), Kind = kind, Width = w, Height = h, Sill = sill, LintelBottom = toBook(sillMm + heightMm), Count = 0 };
                book.Openings.Add(entry);
                book.Save(tr, db);
            }
            string code = entry.Mark;
            var rx = new Regex("^" + Regex.Escape(code) + "/(\\d+)$", RegexOptions.IgnoreCase);
            int max = 0;
            foreach (ObjectId id in space)
            {
                var t = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (t == null || !string.Equals(t.Layer, LayerTag, StringComparison.OrdinalIgnoreCase)) continue;
                var m = rx.Match(t.TextString ?? "");
                int n;
                if (m.Success && int.TryParse(m.Groups[1].Value, out n)) max = Math.Max(max, n);
            }
            return Settings.Get(door ? "DoorTagFormat" : "WindowTagFormat", "{code}/{no}").Replace("{code}", code).Replace("{no}", (max + 1).ToString());
        }

        /// <summary>The mark for a new schedule entry: the standard one for its width (D1 800, D2 900, D3 1200, W1 600 ... W5 2000, V1 600), or the next free number after the standard ones.</summary>
        private static string NewMark(MeasureBook book, bool door, double widthMm, double heightMm)
        {
            string std = door ? OpeningStandards.DoorCode(widthMm) : OpeningStandards.WindowCode(widthMm, heightMm, Settings.GetDouble("VentilatorMaxHeightMm", 600));
            string prefix = std != null && std.StartsWith("V", StringComparison.OrdinalIgnoreCase) ? "V" : door ? "D" : "W";
            return OpeningStandards.NewMark(std, prefix, book.Openings.Select(o => o.Mark));
        }

        // ---- schedule ----

        /// <summary>
        /// HCWOPENSYNC reads the doors and windows made by the tools (their block names, SILL and HEIGHT attributes) and puts them in the opening
        /// schedule of the take-off: one entry for each kind, width, height and sill, with its count, block name and lintel bottom (sill plus height).
        /// Entries already in the schedule for the same blocks and sizes keep their mark and type; new ones get the next free D or W mark.
        /// </summary>
        /// <summary>
        /// The door and window schedule, from the blocks: brings the schedule up to date (as HCWOPENSYNC) and draws it as a table.
        /// The same entries are on the Doors and Windows tabs of MSCHED and in the take-off, which deducts them.
        /// </summary>
        [CommandMethod("HCWOPENSCHED")]
        public void OpeningSchedule()
        {
            var ed = Util.Ed;
            if (!MeasureCommands.Prepare()) return;
            int count, added, updated;
            if (!SyncNow(out count, out added, out updated))
            {
                ed.WriteMessage("\nHCWOPENSCHED: no doors or windows made by HCWDOOR or HCWWINDOW in this space.");
                return;
            }
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var book = MeasureBook.Load(tr, Util.Db);
                if (MeasureCommands.InsertOpeningTable(ed, tr, Util.Db, book)) tr.Commit();
            }
        }

        [CommandMethod("HCWOPENSYNC")]
        public void SyncSchedule()
        {
            var ed = Util.Ed;
            if (!MeasureCommands.Prepare()) return;
            int count, added, updated;
            if (!SyncNow(out count, out added, out updated)) { ed.WriteMessage("\nHCWOPENSYNC: no doors or windows made by HCWDOOR or HCWWINDOW in this space."); return; }
            ed.WriteMessage("\nHCWOPENSYNC: " + count + " opening(s) in the schedule: " + added + " new entr" + (added == 1 ? "y" : "ies") + ", " + updated + " updated. HCWOPENSCHED draws the table; MSCHED shows or changes the entries.");
        }

        /// <summary>
        /// Keeps the opening schedule in step with the blocks: runs after the door, window and edit commands (setting OpeningAutoSync, on by default).
        /// Quiet when nothing needs doing.
        /// </summary>
        /// <summary>After an opening command: the wall hatch is cut around the openings again, and the schedule brought up to date.</summary>
        private static void AfterEdit()
        {
            WallHatch.RefreshNow();
            AutoSync();
        }

        private static void AutoSync()
        {
            if (Settings.GetInt("OpeningAutoSync", 1) == 0) return;
            try
            {
                if (!MeasureCommands.Prepare()) return;
                int count, added, updated;
                if (SyncNow(out count, out added, out updated))
                    Util.Ed.WriteMessage("\nOpening schedule updated (" + count + " opening(s), " + added + " new).");
            }
            catch (System.Exception) { /* the schedule can always be brought up to date with HCWOPENSYNC */ }
        }

        /// <summary>Reads the doors and windows made by the tools in the current space into the schedule (book of the drawing). False when there are none.</summary>
        private static bool SyncNow(out int count, out int added, out int updated)
        {
            var db = Util.Db;
            count = added = updated = 0;
            bool imperial = MeasureCommands.MeasureState.Units == MeasureCommands.UnitSys.Imperial;
            Func<double, double> toBook = mm => imperial ? mm / 25.4 : mm / 1000.0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                var all = new List<OpeningInfo>();
                foreach (ObjectId id in space)
                {
                    if (id.ObjectClass.DxfName != "INSERT") continue;
                    var info = ReadOpening(tr, id);
                    if (info != null) all.Add(info);
                }
                if (all.Count == 0) return false;
                count = all.Count;
                var book = MeasureBook.Load(tr, db);
                foreach (var g in all.GroupBy(i => string.Join("|", i.Door ? "Door" : "Window", i.Type, Math.Round(i.WidthMm), Math.Round(i.HeightMm), Math.Round(i.SillMm))))
                {
                    var first = g.First();
                    string blockName = OpeningFrame.NamePrefix(first.Door, first.Type) + Math.Round(first.WidthMm) + "x" + Math.Round(first.ThicknessMm);
                    var blocks = g.Select(i => OpeningFrame.NamePrefix(i.Door, i.Type) + Math.Round(i.WidthMm) + "x" + Math.Round(i.ThicknessMm)).Distinct().ToList();
                    string kind = first.Door ? "Door" : "Window";
                    double width = toBook(first.WidthMm), height = toBook(first.HeightMm), sill = toBook(first.SillMm);

                    var entry = book.Openings.FirstOrDefault(o => o.Kind == kind && blocks.Any(b => (o.BlockName ?? "").Split(';').Any(x => string.Equals(x.Trim(), b, StringComparison.OrdinalIgnoreCase)))
                            && Math.Abs(o.Height - height) < 1e-3 && Math.Abs(o.Sill - sill) < 1e-3)
                        ?? book.Openings.FirstOrDefault(o => o.Kind == kind && Math.Abs(o.Width - width) < 1e-3 && Math.Abs(o.Height - height) < 1e-3 && Math.Abs(o.Sill - sill) < 1e-3);
                    if (entry == null)
                    {
                        entry = new MeasureBook.OpeningSpec
                        {
                            Mark = NewMark(book, first.Door, first.WidthMm, first.HeightMm), Kind = kind, Width = width, Height = height, Sill = sill,
                            Type = first.Type == "double" ? "Double leaf" : first.Type == "sliding" ? "Sliding" : "",
                        };
                        book.Openings.Add(entry);
                        added++;
                    }
                    else updated++;
                    entry.Count = g.Count();
                    entry.LintelBottom = toBook(first.SillMm + first.HeightMm);
                    var names = new HashSet<string>((entry.BlockName ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
                    foreach (var b in blocks) names.Add(b);
                    entry.BlockName = string.Join(";", names);
                }
                book.Save(tr, db);
                tr.Commit();
                return true;
            }
        }
    }
}
