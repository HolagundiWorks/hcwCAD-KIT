using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT take-off. Units come from the drawing (INSUNITS).
    /// TOSTART creates the layers. Each element has its own command.
    /// </summary>
    public partial class MeasureCommands
    {
        public enum UnitSys { Metric, Imperial }

        public static class MeasureState
        {
            public static UnitSys Units = UnitSys.Metric;
            public static double TextHeight = 0.25;
            public static double DedupTolerance = 0.01;
            /// <summary>Layers this session's Measure commands turned off. MSHOW restores only these.</summary>
            public static readonly HashSet<string> HiddenByMeasure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public const string LayLin = "MEASURE-LINEAR";
        public const string LayDed = "MEASURE-DEDUCT";
        public const string LayFb = "MEASURE-FULLBRICK";
        public const string LayHb = "MEASURE-HALFBRICK";
        public const string LayBm = "MEASURE-BEAM";
        public const string LayLt = "MEASURE-LINTEL";
        public const string LayCnt = "MEASURE-COUNT";
        public const string LayAre = "MEASURE-AREA";
        public const string LaySlb = "MEASURE-SLAB";
        public const string LaySdd = "MEASURE-SLAB-DEDUCT";
        public const string LayLbl = "MEASURE-LABELS";
        public const string LayTbl = "MEASURE-TABLE";

        /// <summary>
        /// Copies of a room outline on the floor and ceiling take-off layers, so MFLOOR and MCEIL read the rooms the tools drew.
        /// Off with the setting RoomMeasureOutlines = 0.
        /// </summary>
        internal static void AddRoomOutlines(Transaction tr, Database db, BlockTableRecord space, Polyline outline)
        {
            if (Settings.GetInt("RoomMeasureOutlines", 1) == 0) return;
            Util.EnsureLayer(tr, db, LayFlor, 3);
            Util.EnsureLayer(tr, db, LayCeil, 141);
            foreach (var layer in new[] { LayFlor, LayCeil })
            {
                var copy = (Polyline)outline.Clone();
                copy.Layer = layer;
                space.AppendEntity(copy);
                tr.AddNewlyCreatedDBObject(copy, true);
            }
        }

        /// <summary>Makes sure the table layer exists (used by other tools that draw tables).</summary>
        internal static void EnsureTableLayer(Transaction tr, Database db) => Util.EnsureLayer(tr, db, LayTbl, 4);

        private static void MakeLayers(Transaction tr, Database db)
        {
            Util.EnsureLayer(tr, db, LayLin, 1);
            Util.EnsureLayer(tr, db, LayDed, 6);
            Util.EnsureLayer(tr, db, LayFb, 30);
            Util.EnsureLayer(tr, db, LayHb, 5);
            Util.EnsureLayer(tr, db, LayBm, 140);
            Util.EnsureLayer(tr, db, LayLt, 40);
            Util.EnsureLayer(tr, db, LayCnt, 2);
            Util.EnsureLayer(tr, db, LayAre, 3);
            Util.EnsureLayer(tr, db, LaySlb, 92);
            Util.EnsureLayer(tr, db, LaySdd, 250);
            Util.EnsureLayer(tr, db, LayLbl, 7);
            Util.EnsureLayer(tr, db, LayTbl, 4);
            Util.EnsureLayer(tr, db, LayCol, 2);
            Util.EnsureLayer(tr, db, LayCeil, 141);
            Util.EnsureLayer(tr, db, LayFlor, 3);
            EnsureRegApp(tr, db);
        }

        public const string LayCol = "MEASURE-COLUMN";
        public const string LayCeil = "MEASURE-CEILING";
        public const string LayFlor = "MEASURE-FLOOR";

        // ---- unit-aware rounding / formatting (m:rnd / m:m / m:area-from-rnd / m:r2) ----


        /// <summary>Schedule sizes are metres, or feet when the drawing is in feet or inches.</summary>
        public static int RndSchedule(double displayLength)
        {
            SyncUnits();
            double drawing;
            if (MeasureState.Units == UnitSys.Imperial)
                drawing = Util.Db.Insunits == UnitsValue.Feet ? displayLength : displayLength * 12.0;
            else
            {
                switch (Util.Db.Insunits)
                {
                    case UnitsValue.Millimeters: drawing = displayLength * 1000.0; break;
                    case UnitsValue.Centimeters: drawing = displayLength * 100.0; break;
                    default: drawing = displayLength; break;
                }
            }
            return Rnd(drawing);
        }

        /// <summary>How far a deduction may differ from a schedule length and still be suggested: 50 mm, or 2 in (16 eighths).</summary>
        public static int SuggestTolerance => MeasureState.Units == UnitSys.Imperial
            ? Settings.GetInt("SuggestToleranceEighths", 16)
            : Settings.GetInt("SuggestToleranceCm", 5);

        private static string ScheduleUnit => MeasureState.Units == UnitSys.Imperial ? "ft" : "m";

        private static void SyncUnits()
        {
            var units = Util.Db.Insunits;
            MeasureState.Units = units == UnitsValue.Feet || units == UnitsValue.Inches
                ? UnitSys.Imperial : UnitSys.Metric;
        }

        private static double MetresFromDrawing(double drawing)
        {
            switch (Util.Db.Insunits)
            {
                case UnitsValue.Millimeters: return drawing / 1000.0;
                case UnitsValue.Centimeters: return drawing / 100.0;
                case UnitsValue.Inches: return drawing * 0.0254;
                case UnitsValue.Feet: return drawing * 0.3048;
                default: return drawing;
            }
        }

        private static double InchesFromDrawing(double drawing)
        {
            switch (Util.Db.Insunits)
            {
                case UnitsValue.Inches: return drawing;
                case UnitsValue.Feet: return drawing * 12.0;
                case UnitsValue.Millimeters: return drawing / 25.4;
                case UnitsValue.Centimeters: return drawing / 2.54;
                default: return drawing / 0.0254;
            }
        }

        private static int Rnd(double drawing)
        {
            if (MeasureState.Units == UnitSys.Imperial)
                return (int)Math.Floor(InchesFromDrawing(drawing) * 8.0 + 0.5001);
            return (int)Math.Floor(MetresFromDrawing(drawing) * 100.0 + 0.5001);
        }

        private static double R2(double drawingArea)
        {
            double shown;
            switch (Util.Db.Insunits)
            {
                case UnitsValue.Millimeters: shown = drawingArea / 1e6; break;
                case UnitsValue.Centimeters: shown = drawingArea / 1e4; break;
                case UnitsValue.Inches: shown = drawingArea / 144.0; break;
                case UnitsValue.Feet: shown = drawingArea; break;
                default: shown = drawingArea; break;
            }
            return Math.Floor(shown * 100.0 + 0.5) / 100.0;
        }

        private static double AreaFromRnd(int len, int brd)
        {
            double raw = MeasureState.Units == UnitSys.Imperial
                ? (len / 8.0) * (brd / 8.0) / 144.0
                : (len * (double)brd) / 10000.0;
            return Math.Floor(raw * 100.0 + 0.5) / 100.0;
        }

        private static string Frac(int num, int den)
        {
            int g = Gcd(num, den); if (g == 0) g = 1;
            return $"{num / g}/{den / g}";
        }
        private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

        private static string FeetInch(int n)
        {
            int wholeIn = n / 8, eighth = n % 8;
            int feet = wholeIn / 12, inch = wholeIn % 12;
            return $"{feet}'-{inch}{(eighth == 0 ? "" : " " + Frac(eighth, 8))}\"";
        }

        public static string M(int n)
        {
            if (n < 0) return "-" + M(-n);
            if (MeasureState.Units == UnitSys.Imperial) return FeetInch(n);
            return $"{n / 100}.{(n % 100 < 10 ? "0" : "")}{n % 100}";
        }

        private static string LenLabel => MeasureState.Units == UnitSys.Imperial ? "ft-in" : "m";
        private static string AreaLabel => MeasureState.Units == UnitSys.Imperial ? "sq ft" : "sq m";

        [CommandMethod("TOSTART")]
        [CommandMethod("MSETUP")]
        public void MSetup()
        {
            var ed = Util.Ed; var db = Util.Db;
            if (!Prepare()) return;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (lt.Has(LayLin)) db.Clayer = lt[LayLin];
                tr.Commit();
            }
            ed.WriteMessage("\nTake-off ready. Units: " + UnitSentence() + ".");
            ed.WriteMessage("\nDraw on the TAKE-OFF layers, then click that element. Openings go on " + LayDed + ".");
            ed.WriteMessage("\nLinear " + LayLin + ", brick " + LayFb + " / " + LayHb
                + ", beams " + LayBm + ", lintels " + LayLt + ", columns " + LayCol
                + ", ceiling " + LayCeil + ", floor " + LayFlor + ".");
        }

        internal static bool Prepare()
        {
            var db = Util.Db;
            var ed = Util.Ed;
            if (db.Insunits == UnitsValue.Undefined)
            {
                var pko = new PromptKeywordOptions("\nDrawing units are unset. Treat distances as [Millimetres/Metres] <Millimetres>: ");
                pko.Keywords.Add("Millimetres");
                pko.Keywords.Add("Metres");
                pko.Keywords.Default = "Millimetres";
                pko.AllowNone = true;
                var r = ed.GetKeywords(pko);
                if (r.Status == PromptStatus.Cancel) return false;
                bool metres = r.Status == PromptStatus.OK && r.StringResult == "Metres";
                using (Util.Doc.LockDocument())
                    db.Insunits = metres ? UnitsValue.Meters : UnitsValue.Millimeters;
                ed.WriteMessage(metres ? "\nUnits set to metres." : "\nUnits set to millimetres.");
            }
            SyncUnits();
            MeasureState.TextHeight = Util.MmToDrawingUnits(Settings.GetDouble("TakeoffTextHeightMm", 125));
            MeasureState.DedupTolerance = Math.Max(Util.MmToDrawingUnits(Settings.GetDouble("DeductionToleranceMm", 10)), 1e-9);
            return true;
        }

        private static string UnitSentence()
        {
            switch (Util.Db.Insunits)
            {
                case UnitsValue.Millimeters: return "millimetres, lengths to 1 cm";
                case UnitsValue.Centimeters: return "centimetres, lengths to 1 cm";
                case UnitsValue.Inches: return "inches, lengths to 1/8 in";
                case UnitsValue.Feet: return "feet, lengths to 1/8 in";
                default: return "metres, lengths to 1 cm";
            }
        }

        [CommandMethod("MSHOW")]
        public void MShow()
        {
            var ed = Util.Ed;
            if (MeasureState.HiddenByMeasure.Count == 0)
            {
                ed.WriteMessage("\nNo layers left hidden by Measure.");
                return;
            }
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Util.Db.LayerTableId, OpenMode.ForRead);
                int n = 0;
                foreach (var name in MeasureState.HiddenByMeasure.ToList())
                {
                    if (!lt.Has(name)) continue;
                    var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                    if (ltr.IsOff) { ltr.IsOff = false; n++; }
                }
                MeasureState.HiddenByMeasure.Clear();
                tr.Commit();
                ed.WriteMessage($"\nRestored {n} layer(s) hidden by Measure. Other layers were left as they were.");
            }
        }

        [CommandMethod("MCLEAR")]
        public void MClear()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                int n = 0;
                var toErase = new List<ObjectId>();
                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && (ent.Layer == LayLbl || ent.Layer == LayTbl)) toErase.Add(id);
                }
                foreach (var id in toErase) { tr.GetObject(id, OpenMode.ForWrite).Erase(); n++; }
                tr.Commit();
                ed.WriteMessage(n > 0 ? $"\n{n} label/table objects erased." : "\nNothing to clear.");
            }
        }

        private static double CurveLen(Curve c) => c.GetDistanceAtParameter(c.EndParam);
        private static Point3d CurveMid(Curve c) => c.GetPointAtDist(CurveLen(c) / 2.0);

        private static double MaxDist(Curve d, Curve l)
        {
            Point3d[] pts = { d.StartPoint, CurveMid(d), d.EndPoint };
            double mx = 0;
            foreach (var p in pts)
            {
                var cp = l.GetClosestPointTo(p, false);
                mx = Math.Max(mx, p.DistanceTo(cp));
            }
            return mx;
        }

        private static void CenteredText(Transaction tr, BlockTableRecord btr, Point3d pt, string s, double h, string layer)
        {
            var t = new DBText
            {
                Position = pt, Height = h, TextString = s, Layer = layer,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = pt
            };
            btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
        }

        [CommandMethod("MLIN")]
        public void MLin() => RunTypedLinear(new[] { (LayLin, "L", "Linear") }, LayDed, "\nSelect linear AND deduction lines: ", "Linear", singleTypeNoPrefix: true);

        [CommandMethod("MBRK")]
        public void MBrk() => RunTypedLinear(new[] { (LayFb, "FB", "Full Brick"), (LayHb, "HB", "Half Brick") }, LayDed,
            "\nSelect full brick, half brick AND deduction lines: ", "Bricks");

        [CommandMethod("MBML")]
        public void MBml() => RunTypedLinear(new[] { (LayBm, "BM", "Concrete beam"), (LayLt, "LT", "Concrete lintel") }, LayDed,
            "\nSelect concrete beam, concrete lintel AND deduction lines: ", "BeamsLintels");

        /// <summary>
        /// Linear take-off with deductions, shared by MLIN, MBRK and MBML.
        /// </summary>
        private void RunTypedLinear((string layer, string code, string name)[] types, string dedLayer,
            string selPrompt, string csvName, bool singleTypeNoPrefix = false)
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                var book = MeasureBook.Load(tr, db);
                var isolateLayers = types.Select(t => t.layer).Append(dedLayer).ToArray();
                using (var iso = IsolateAndPrompt(tr, db, isolateLayers, selPrompt))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(),
                        BuildFilter("LINE,LWPOLYLINE,POLYLINE,ARC,SPLINE", isolateLayers));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = MeasureState.TextHeight;
                    double tol = MeasureState.DedupTolerance;

                    var grp = new List<(Curve ent, string type)>();
                    var deds = new List<Curve>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var c = (Curve)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (string.Equals(c.Layer, dedLayer, StringComparison.OrdinalIgnoreCase)) { deds.Add(c); continue; }
                        var match = types.FirstOrDefault(t => string.Equals(t.layer, c.Layer, StringComparison.OrdinalIgnoreCase));
                        if (match.layer != null) grp.Add((c, match.code));
                    }
                    if (grp.Count == 0) { ed.WriteMessage("\nNo matching typed lines selected."); return; }

                    // Door and window blocks add their own deduction line (index -> block name).
                    var blockOf = new Dictionary<int, string>();
                    foreach (var found in BlockOpenings.Collect(tr, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)))
                    {
                        blockOf[deds.Count] = found.BlockName;
                        deds.Add(found.Curve);
                    }
                    bool bookChanged = false;

                    string order = Settings.Get("WallNumbering", "LeftRight");
                    Curve numberPath = null;
                    if (string.Equals(order, "Path", StringComparison.OrdinalIgnoreCase))
                    {
                        var pathOpts = new PromptEntityOptions("\nPick the path line for wall numbering (on a visible layer, Enter = left to right): ");
                        pathOpts.SetRejectMessage("\nPick a line, polyline or arc.");
                        pathOpts.AddAllowedClass(typeof(Curve), false);
                        var pathRes = ed.GetEntity(pathOpts);
                        if (pathRes.Status == PromptStatus.OK)
                            numberPath = (Curve)tr.GetObject(pathRes.ObjectId, OpenMode.ForRead);
                    }

                    List<IList<P2>> roomOutlines = null;
                    if (string.Equals(order, "Room", StringComparison.OrdinalIgnoreCase))
                    {
                        var rs = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the room outlines to number the walls room by room (closed polylines; Enter = left to right): " },
                            new SelectionFilter(new[] { new TypedValue(0, "LWPOLYLINE") }));
                        if (rs.Status == PromptStatus.OK)
                        {
                            roomOutlines = new List<IList<P2>>();
                            foreach (var rid in rs.Value.GetObjectIds())
                            {
                                var rp = (Polyline)tr.GetObject(rid, OpenMode.ForRead);
                                if (!rp.Closed) continue;
                                var pts = new List<P2>();
                                for (int vi = 0; vi < rp.NumberOfVertices; vi++) { var v = rp.GetPoint2dAt(vi); pts.Add(new P2(v.X, v.Y)); }
                                roomOutlines.Add(pts);
                            }
                        }
                    }
                    var recolour = new List<KeyValuePair<Curve, string>>();

                    // match each deduction to the typed line it overlaps (nearest within tolerance)
                    var grpCurves = grp.Select(g => g.ent).ToList();
                    var pidx = MatchNearest(deds, grpCurves, tol);
                    var dedsByWall = Enumerable.Range(0, deds.Count)
                        .Where(n => pidx[n].HasValue)
                        .OrderBy(n => CurveMid(deds[n]).X).ThenBy(n => CurveMid(deds[n]).Y)
                        .ToLookup(n => pidx[n].Value);

                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    int gG = 0, gD = 0, gN = 0;

                    foreach (var ty in types)
                    {
                        var segs = new List<(int index, int gross)>();
                        for (int k = 0; k < grp.Count; k++)
                            if (grp[k].type == ty.code) segs.Add((k, Rnd(CurveLen(grp[k].ent))));
                        if (segs.Count == 0) continue;

                        int sG = 0, sD = 0, sN = 0;
                        int letter = 0;
                        // Number the walls left to right, then bottom to top: FB01, FB02, ...
                        var wallNo = new Dictionary<int, int>();
                        int serial = 0;
                        foreach (var seg in OrderWalls(segs, grpCurves, order, numberPath, roomOutlines))
                            wallNo[seg.index] = ++serial;
                        foreach (var bucket in segs.GroupBy(s => s.gross).OrderByDescending(s => s.Key))
                        {
                            string group = (singleTypeNoPrefix ? "L" : ty.code) + "-" + GroupLetter(letter++);
                            int each = bucket.Key;
                            int count = bucket.Count();
                            int grossSum = each * count;
                            int dsum = 0;
                            foreach (var seg in bucket)
                            {
                                var mp = CurveMid(grp[seg.index].ent);
                                PlaceLabel(tr, db, btr, new Point3d(mp.X, mp.Y + 0.8 * th, 0), group, book, th);
                                int openingNo = 0;
                                string wallId = (singleTypeNoPrefix ? "L" : ty.code) + wallNo[seg.index].ToString("00");
                                foreach (int j in dedsByWall[seg.index])
                                {
                                    openingNo++;
                                    int dv = Rnd(CurveLen(deds[j]));
                                    dsum += dv;
                                    // Wall FB01, its first opening: FB01-D1.
                                    string raw = wallId + "-D" + openingNo;
                                    string blockName;
                                    if (blockOf.TryGetValue(j, out blockName))
                                        MapBlock(ed, book, blockName, raw, dv, ref bookChanged);
                                    var dmp = CurveMid(deds[j]);
                                    PlaceLabel(tr, db, btr, new Point3d(dmp.X, dmp.Y - 0.8 * th, 0), raw, book, th, measured: dv);
                                    string shown = DisplayName(book, raw);
                                    // a standalone deduction line named in the schedule takes that name's colour (a block's lines cannot be coloured per insert)
                                    if (!blockOf.ContainsKey(j) && !string.Equals(shown, raw, StringComparison.OrdinalIgnoreCase)) recolour.Add(new KeyValuePair<Curve, string>(deds[j], shown));
                                    rows.Add(singleTypeNoPrefix
                                        ? new[] { shown, "", M(dv), "" }
                                        : new[] { shown, ty.name, "", M(dv), "" });
                                }
                            }
                            int net = grossSum - dsum;
                            if (net < 0) ed.WriteMessage("\nWARNING: deductions on " + group + " exceed its length.");
                            sG += grossSum; sD += dsum; sN += net;
                            rows.Add(singleTypeNoPrefix
                                ? new[] { group, M(each) + " x " + count, M(dsum), M(net) }
                                : new[] { group, ty.name, M(each) + " x " + count, M(dsum), M(net) });
                        }
                        rows.Add(singleTypeNoPrefix
                            ? new[] { "TOTAL", M(sG), M(sD), M(sN) }
                            : new[] { "TOTAL", ty.name, M(sG), M(sD), M(sN) });
                        gG += sG; gD += sD; gN += sN;
                        ed.WriteMessage($"\n{ty.name}:  {M(sG)} {LenLabel}  less {M(sD)} {LenLabel}  =  NET {M(sN)} {LenLabel}");
                    }

                    int unm = 0;
                    for (int j = 0; j < deds.Count; j++)
                    {
                        // A block deduction that touches none of the selected walls belongs to another wall.
                        if (pidx[j] == null && !blockOf.ContainsKey(j))
                        {
                            unm++;
                            var dmp = CurveMid(deds[j]);
                            CenteredText(tr, btr, new Point3d(dmp.X, dmp.Y - 0.8 * th, 0), "D? NO LINE", th, LayLbl);
                        }
                    }
                    if (unm > 0) ed.WriteMessage($"\nWARNING: {unm} deduction line(s) did not overlap any selected line (marked D? NO LINE, not counted).");

                    rows.Add(singleTypeNoPrefix
                        ? new[] { "GRAND TOTAL", M(gG), M(gD), M(gN) }
                        : new[] { "GRAND TOTAL", "", M(gG), M(gD), M(gN) });
                    ed.WriteMessage($"\nTotal {M(gG)} {LenLabel}  deductions {M(gD)} {LenLabel}  NET {M(gN)} {LenLabel}");

                    var headers = singleTypeNoPrefix
                        ? new[] { "Item", $"Length x count ({LenLabel})", $"Deduction ({LenLabel})", $"Net ({LenLabel})" }
                        : new[] { "Item", "Type", $"Length x count ({LenLabel})", $"Deduction ({LenLabel})", $"Net ({LenLabel})" };
                    if (bookChanged) book.Save(tr, db);
                    if (recolour.Count > 0 && Settings.GetInt("DeductionColours", 1) != 0)
                    {
                        var colours = DeductionColours.Assign(recolour.Select(r => r.Value));
                        foreach (var r in recolour)
                        {
                            var ent = (Entity)r.Key;
                            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
                            ent.ColorIndex = colours[r.Value];
                        }
                        ed.WriteMessage("\n" + recolour.Count + " deduction line(s) coloured by schedule name: " + string.Join(", ", colours.Select(c => c.Key + "=" + c.Value)) + ".");
                    }
                    Output(tr, db, csvName, headers, rows, th);
                }
                tr.Commit();
            }
        }

        [CommandMethod("MCOL")]
        public void MCol() => RunRectangles(LayCol, "C", "Columns", "\nSelect concrete column rectangles on MEASURE-COLUMN: ", true);

        [CommandMethod("MREC")]
        public void MRec() => RunRectangles(LayCnt, "R", "Rectangles", "\nSelect rectangles (closed polylines) on MEASURE-COUNT: ", false);

        private void RunRectangles(string layer, string code, string csvName, string prompt, bool useSchedule)
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                var book = MeasureBook.Load(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { layer }, prompt))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE", new[] { layer }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = MeasureState.TextHeight;
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var found = new List<(Point3d cen, int len, int brd, double area, int perim)>();
                    int skipped = 0;
                    foreach (SelectedObject so in psr.Value)
                    {
                        var pl = (Polyline)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        var rect = RectCorners(pl);
                        if (rect == null) { skipped++; continue; }
                        double a = rect[0].DistanceTo(rect[1]), b = rect[1].DistanceTo(rect[2]);
                        int len = Rnd(Math.Max(a, b)), brd = Rnd(Math.Min(a, b));
                        var cen = new Point3d((rect[0].X + rect[2].X) / 2.0, (rect[0].Y + rect[2].Y) / 2.0, 0);
                        found.Add((cen, len, brd, AreaFromRnd(len, brd), 2 * (len + brd)));
                    }
                    if (skipped > 0) ed.WriteMessage($"\n{skipped} object(s) skipped (not closed 4-sided rectangles).");
                    if (found.Count == 0) return;

                    var rows = new List<string[]>();
                    double totA = 0; int totP = 0, letter = 0;
                    foreach (var bucket in found.GroupBy(f => f.len + "x" + f.brd).OrderByDescending(g => g.First().len * g.First().brd))
                    {
                        var sample = bucket.First();
                        var scheduled = useSchedule ? book.ColumnBySize(sample.len, sample.brd) : null;
                        string name = scheduled == null
                            ? code + "-" + GroupLetter(letter++)
                            : string.IsNullOrWhiteSpace(scheduled.Name) ? scheduled.Mark : scheduled.Mark + " " + scheduled.Name;
                        int count = bucket.Count();
                        double areaSum = sample.area * count;
                        int perimSum = sample.perim * count;
                        totA += areaSum; totP += perimSum;
                        foreach (var item in bucket)
                            PlaceLabel(tr, db, btr, item.cen, name, book, th, force: name);
                        rows.Add(new[] { name, M(sample.len), M(sample.brd), count.ToString(), areaSum.ToString("F2"), M(perimSum) });
                    }
                    rows.Add(new[] { "TOTAL", "", "", found.Count.ToString(), totA.ToString("F2"), M(totP) });
                    ed.WriteMessage($"\n{found.Count} grouped as {rows.Count - 1} size(s). Total area = {totA:F2} {AreaLabel}");
                    Output(tr, db, csvName, new[] { "Mark", $"Length ({LenLabel})", $"Breadth ({LenLabel})", "Count", $"Area ({AreaLabel})", $"Perimeter ({LenLabel})" }, rows, th);
                }
                tr.Commit();
            }
        }

        [CommandMethod("MPAINT")]
        public void MPaint() => RunPaint(new[] { LayFb, LayHb, LayLin }, LayDed, "WallPaint", "wall paint");

        [CommandMethod("MCEIL")]
        public void MCeil() => RunAreas(LayCeil, "CP", "CeilingPaint", "\nSelect ceiling outlines on MEASURE-CEILING: ");

        [CommandMethod("MFLOOR")]
        public void MFloor() => RunAreas(LayFlor, "FL", "FloorArea", "\nSelect floor outlines on MEASURE-FLOOR: ");

        /// <summary>Draws the saved schedule as a table at a picked point.</summary>
        [CommandMethod("MSCHEDTABLE")]
        public void MSchedTable()
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var book = MeasureBook.Load(tr, db);
                if (ScheduleTables(book).Count == 0)
                {
                    ed.WriteMessage("\nMSCHEDTABLE: the schedule is empty. Run MSCHED first.");
                    return;
                }
                MakeLayers(tr, db);
                if (InsertScheduleTable(ed, tr, db, book)) tr.Commit();
            }
        }

        /// <summary>Draws only the door and window schedule at a picked point (used by HCWOPENSCHED).</summary>
        internal static bool InsertOpeningTable(Editor ed, Transaction tr, Database db, MeasureBook book)
        {
            var table = ScheduleTables(book).FirstOrDefault(t => t.Title == "DOOR AND WINDOW SCHEDULE");
            if (table == null) { ed.WriteMessage("\nThere are no doors or windows in the schedule yet."); return false; }
            var ppr = ed.GetPoint("\nPick a point for the door and window schedule (top-left): ");
            if (ppr.Status != PromptStatus.OK) return false;
            Util.EnsureLayer(tr, db, LayTbl, 4);
            double h = MeasureState.TextHeight;
            CenteredTextLeft(tr, db, new Point3d(ppr.Value.X, ppr.Value.Y + 0.6 * h, 0), table.Title, h * 1.2);
            DrawTable(tr, db, new Point3d(ppr.Value.X, ppr.Value.Y - 1.5 * h, 0), table.Headers, table.Rows, h);
            return true;
        }

        private static bool InsertScheduleTable(Editor ed, Transaction tr, Database db, MeasureBook book)
        {
            var tables = ScheduleTables(book);
            if (tables.Count == 0) return false;
            var ppr = ed.GetPoint("\nPick a point for the schedule tables (top-left): ");
            if (ppr.Status != PromptStatus.OK) return false;
            double h = MeasureState.TextHeight;
            double y = ppr.Value.Y;
            foreach (var table in tables)
            {
                CenteredTextLeft(tr, db, new Point3d(ppr.Value.X, y + 0.6 * h, 0), table.Title, h * 1.2);
                DrawTable(tr, db, new Point3d(ppr.Value.X, y - 1.5 * h, 0), table.Headers, table.Rows, h);
                // title line, header row, body rows, then a gap before the next table
                y -= (table.Rows.Count + 1) * 2.0 * h + 4.5 * h;
            }
            return true;
        }

        private static void CenteredTextLeft(Transaction tr, Database db, Point3d pt, string text, double h)
        {
            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var t = new DBText { Position = pt, Height = h, TextString = text, Layer = LayTbl };
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
        }

        [CommandMethod("MSCHED")]
        public void MSched()
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            MeasureBook book;
            var labels = new List<KeyValuePair<string, int>>();
            var lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var blocks = new List<UI.BlockFound>();
            var takeoffUnits = new List<KeyValuePair<string, string>>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                book = MeasureBook.Load(tr, db);
                foreach (var saved in MeasureBook.LoadAllTakeoffs(tr, db))
                {
                    // The unit is the bracketed part of the last column heading, e.g. "Net (m2)".
                    string last = saved.Headers.Length > 0 ? saved.Headers[saved.Headers.Length - 1] : "";
                    int open = last.LastIndexOf('('), close = last.LastIndexOf(')');
                    takeoffUnits.Add(new KeyValuePair<string, string>(saved.Name,
                        open >= 0 && close > open ? last.Substring(open + 1, close - open - 1) : ""));
                }
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (var group in BlockOpenings.Collect(tr, space).GroupBy(f => f.BlockName, StringComparer.OrdinalIgnoreCase))
                {
                    int first = Rnd(CurveLen(group.First().Curve));
                    blocks.Add(new UI.BlockFound
                    {
                        Name = group.Key,
                        Count = group.Count(),
                        Length = MeasureState.Units == UnitSys.Imperial ? first / 8.0 / 12.0 : first / 100.0
                    });
                }
                foreach (ObjectId id in space)
                {
                    var text = tr.GetObject(id, OpenMode.ForRead) as DBText;
                    if (text == null || !string.Equals(text.Layer, LayLbl, StringComparison.OrdinalIgnoreCase)) continue;
                    string raw = RawLabel(text);
                    if (IsDeductionLabel(raw) && !lengths.ContainsKey(raw))
                    {
                        int len = LabelLength(text);
                        lengths[raw] = len;
                        labels.Add(new KeyValuePair<string, int>(raw, len));
                    }
                }
                tr.Commit();
            }

            using (var dlg = new UI.MeasureScheduleForm(book, labels, ScheduleUnit, blocks))
            {
                dlg.SetTakeoffNames(takeoffUnits);
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                book = dlg.Read();
                book.CountFromMaps();
                foreach (var o in book.Openings.Where(x => x.LintelBottom > 0 && x.Height > x.LintelBottom))
                    ed.WriteMessage("\nWARNING: " + o.Mark + " is " + o.Height + " high, above its own lintel bottom " + o.LintelBottom + ".");
                foreach (var floor in book.Floors)
                {
                    if (floor.FflHeight > 0 && floor.Height > floor.FflHeight)
                        ed.WriteMessage("\nWARNING: " + floor.Name + " ceiling height " + floor.Height + " is more than its FFL to FFL height " + floor.FflHeight + ".");
                    if (floor.Height > 0 && floor.LintelBottom > floor.Height)
                        ed.WriteMessage("\nWARNING: " + floor.Name + " lintel bottom " + floor.LintelBottom + " is above its ceiling height " + floor.Height + ".");
                    foreach (var o in book.Openings.Where(x => x.LintelBottom <= 0 && floor.LintelBottom > 0 && x.Height > floor.LintelBottom
                        && HCW.AutoCAD.Plugin.Logic.LintelRules.AppliesToFloor(x.Floor, floor.Name)))
                        ed.WriteMessage("\nWARNING: " + o.Mark + " is " + o.Height + " high, above the " + floor.Name + " lintel bottom " + floor.LintelBottom + ".");
                }
                foreach (var o in book.Openings.Where(x => x.Sill > 0))
                {
                    double lintel = book.LintelFor(o);
                    if (lintel > 0 && Math.Abs(RndSchedule(lintel - o.Sill) - o.HeightRounded) > SuggestTolerance)
                        ed.WriteMessage("\nWARNING: " + o.Mark + " sill " + o.Sill + " and lintel bottom " + lintel + " leave "
                            + Math.Round(lintel - o.Sill, 3) + ", but its height is " + o.Height + ".");
                }
                foreach (var dup in book.Openings.GroupBy(o => o.Mark, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    ed.WriteMessage("\nWARNING: schedule name " + dup.Key + " is used more than once.");
                foreach (var map in book.Maps)
                {
                    var opening = book.Opening(map.Mark);
                    if (opening == null)
                    {
                        ed.WriteMessage("\nWARNING: " + map.Label + " maps to " + map.Mark + ", which is not in the schedule.");
                        continue;
                    }
                    int len;
                    if (lengths.TryGetValue(map.Label, out len) && len >= 0 && Math.Abs(len - opening.WidthRounded) > SuggestTolerance)
                        ed.WriteMessage("\nWARNING: " + map.Label + " measures " + M(len) + " but " + opening.Mark + " is " + M(opening.WidthRounded) + ".");
                }
                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    MakeLayers(tr, db);
                    book.Save(tr, db);
                    int renamed = ApplyNames(tr, db, book);
                    if (dlg.DrawTable) InsertScheduleTable(ed, tr, db, book);
                    tr.Commit();
                    ed.WriteMessage("\nSchedule saved. " + renamed + " measured name(s) updated.");
                }
            }
        }

        private void RunPaint(string[] wallLayers, string dedLayer, string csvName, string title)
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            MeasureBook book;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                book = MeasureBook.Load(tr, db);
                tr.Commit();
            }
            double height = AskFloorHeight(ed, book);
            if (height <= 0) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                book = MeasureBook.Load(tr, db);
                var layers = wallLayers.Append(dedLayer).ToArray();
                using (var iso = IsolateAndPrompt(tr, db, layers, "\nSelect wall lines and opening deductions for " + title + ": "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LINE,LWPOLYLINE,POLYLINE,ARC,SPLINE", layers));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
                    double th = MeasureState.TextHeight;
                    var walls = new List<Curve>();
                    var deds = new List<Curve>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var c = (Curve)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (string.Equals(c.Layer, dedLayer, StringComparison.OrdinalIgnoreCase)) deds.Add(c);
                        else walls.Add(c);
                    }
                    if (walls.Count == 0) { ed.WriteMessage("\nNo wall lines selected."); return; }

                    var blockOf = new Dictionary<int, string>();
                    foreach (var found in BlockOpenings.Collect(tr, (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)))
                    {
                        blockOf[deds.Count] = found.BlockName;
                        deds.Add(found.Curve);
                    }
                    var labelIndex = DeductionLabels(tr, db);

                    var parent = MatchNearest(deds, walls, MeasureState.DedupTolerance);
                    var dedsByParent = Enumerable.Range(0, deds.Count)
                        .Where(i => parent[i].HasValue).ToLookup(i => parent[i].Value);

                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    double tot = 0;
                    int letter = 0;
                    var sized = walls.Select((w, wi) => new { Curve = w, Index = wi, Len = Rnd(CurveLen(w)) }).GroupBy(w => w.Len).OrderByDescending(g => g.Key);
                    foreach (var bucket in sized)
                    {
                        string group = "P-" + GroupLetter(letter++);
                        double gross = (bucket.Key / (MeasureState.Units == UnitSys.Imperial ? 8.0 * 12.0 : 100.0)) * height * bucket.Count();
                        double deduct = 0;
                        foreach (var wall in bucket)
                        {
                            PlaceLabel(tr, db, btr, CurveMid(wall.Curve), group, book, th, force: group);
                            foreach (int i in dedsByParent[wall.Index])
                            {
                                string blockName;
                                deduct += OpeningArea(book, labelIndex, deds[i], height, th, blockOf.TryGetValue(i, out blockName) ? blockName : null);
                            }
                        }
                        double net = Math.Max(0, gross - deduct);
                        tot += net;
                        rows.Add(new[] { group, M(bucket.Key), bucket.Count().ToString(), height.ToString("0.###"), gross.ToString("F2"), deduct.ToString("F2"), net.ToString("F2") });
                    }
                    rows.Add(new[] { "TOTAL", "", walls.Count.ToString(), "", "", "", tot.ToString("F2") });
                    ed.WriteMessage($"\n{title}: {tot:F2} {AreaLabel} at height {height:0.###} {LenLabel}.");
                    Output(tr, db, csvName, new[] { "Group", $"Length ({LenLabel})", "Count", $"Height ({LenLabel})", $"Gross ({AreaLabel})", $"Openings ({AreaLabel})", $"Net ({AreaLabel})" }, rows, th);
                }
                tr.Commit();
            }
        }

        private void RunAreas(string layer, string code, string csvName, string prompt)
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                var book = MeasureBook.Load(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { layer }, prompt))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE", new[] { layer }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }
                    double th = MeasureState.TextHeight;
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var found = new List<(Point3d cen, double area)>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        double area = 0; Point3d cen = Point3d.Origin; bool ok = false;
                        if (ent is Polyline pl && pl.Closed) { area = Math.Abs(pl.Area); cen = Centroid(pl); ok = area > 0; }
                        else if (ent is Circle ci) { area = Math.PI * ci.Radius * ci.Radius; cen = ci.Center; ok = true; }
                        else if (ent is Curve cv) { try { area = Math.Abs(cv.Area); cen = CurveMid(cv); ok = area > 0; } catch { ok = false; } }
                        if (ok) found.Add((cen, R2(area)));
                    }
                    if (found.Count == 0) { ed.WriteMessage("\nNo closed areas selected."); return; }
                    var rows = new List<string[]>();
                    double tot = 0; int letter = 0;
                    foreach (var bucket in found.GroupBy(f => f.area.ToString("F2")).OrderByDescending(g => g.Key))
                    {
                        string name = code + "-" + GroupLetter(letter++);
                        double each = bucket.First().area;
                        double sum = each * bucket.Count();
                        tot += sum;
                        foreach (var item in bucket)
                            PlaceLabel(tr, db, btr, item.cen, name, book, th, force: name);
                        rows.Add(new[] { name, each.ToString("F2"), bucket.Count().ToString(), sum.ToString("F2") });
                    }
                    rows.Add(new[] { "TOTAL", "", found.Count.ToString(), tot.ToString("F2") });
                    ed.WriteMessage($"\n{found.Count} outline(s), {tot:F2} {AreaLabel}.");
                    Output(tr, db, csvName, new[] { "Group", $"Area each ({AreaLabel})", "Count", $"Area total ({AreaLabel})" }, rows, th);
                }
                tr.Commit();
            }
        }


        [CommandMethod("MAREA")]
        [CommandMethod("MARE")]
        public void MAre()
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { LayAre }, "\nSelect closed polygons/circles on MEASURE-AREA: "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE", new[] { LayAre }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = MeasureState.TextHeight;
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    int n = 1, skipped = 0; double totA = 0; int totP = 0;

                    foreach (SelectedObject so in psr.Value)
                    {
                        var ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        double area = 0; bool ok = false;
                        Point3d cen = Point3d.Origin; double perim = 0;
                        if (ent is Polyline pl && pl.Closed) { area = Math.Abs(pl.Area); ok = true; cen = Centroid(pl); perim = pl.Length; }
                        else if (ent is Circle ci) { area = Math.PI * ci.Radius * ci.Radius; ok = true; cen = ci.Center; perim = 2 * Math.PI * ci.Radius; }
                        else if (ent is Curve cv) { try { area = Math.Abs(cv.Area); ok = area > 0; perim = CurveLen(cv); cen = CurveMid(cv); } catch { ok = false; } }

                        if (!ok || area <= 0) { skipped++; continue; }
                        double arR = R2(area);
                        int pr = Rnd(perim);
                        totA += arR; totP += pr;
                        CenteredText(tr, btr, cen, "A" + n, th, LayLbl);
                        rows.Add(new[] { "A" + n, arR.ToString("F2"), M(pr) });
                        ed.WriteMessage($"\nA{n}:  Area = {arR:F2} {AreaLabel}   Perimeter = {M(pr)} {LenLabel}");
                        n++;
                    }
                    if (skipped > 0) ed.WriteMessage($"\n{skipped} object(s) skipped (not closed).");
                    if (rows.Count > 0)
                    {
                        rows.Add(new[] { "TOTAL", totA.ToString("F2"), M(totP) });
                        ed.WriteMessage($"\nTotal area = {totA:F2} {AreaLabel}   Total perimeter = {M(totP)} {LenLabel}");
                        Output(tr, db, "Areas", new[] { "Area No", $"Area ({AreaLabel})", $"Perimeter ({LenLabel})" }, rows, th);
                    }
                }
                tr.Commit();
            }
        }

        [CommandMethod("MSLAB")]
        [CommandMethod("MSLB")]
        public void MSlb()
        {
            if (!Prepare()) return;
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { LaySlb, LaySdd }, "\nSelect slab boundary polygons AND deduction (opening) polygons: "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE,POLYLINE", new[] { LaySlb, LaySdd }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = MeasureState.TextHeight;
                    var slabs = new List<Polyline>();
                    var deds = new List<Polyline>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var pl = (Polyline)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (string.Equals(pl.Layer, LaySdd, StringComparison.OrdinalIgnoreCase)) deds.Add(pl); else slabs.Add(pl);
                    }
                    if (slabs.Count == 0) { ed.WriteMessage("\nNo slab boundary polygons selected - deductions need a slab boundary."); return; }

                    // match each deduction to the slab boundary whose outline contains its centroid
                    var slabVerts = slabs.Select(Verts).ToList();
                    var pidx = new int?[deds.Count];
                    for (int di = 0; di < deds.Count; di++)
                    {
                        var dcen = Centroid(deds[di]);
                        int? best = null;
                        for (int k = 0; k < slabs.Count; k++)
                            if (PtInPoly(dcen, slabVerts[k])) best = k;
                        pidx[di] = best;
                    }
                    var dedsBySlab = Enumerable.Range(0, deds.Count)
                        .Where(j => pidx[j].HasValue).ToLookup(j => pidx[j].Value);

                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    double totG = 0, totD = 0, totN = 0; int n = 0;

                    for (int k = 0; k < slabs.Count; k++)
                    {
                        var s = slabs[k];
                        double v = Math.Abs(s.Area);
                        if (v <= 0) continue;
                        n++;
                        double gross = R2(v), dsum = 0; int dn = 0;
                        var dl = new List<(string lbl, double val, Polyline ent)>();
                        foreach (int j in dedsBySlab[k])
                        {
                            double dv = R2(Math.Abs(deds[j].Area));
                            dn++; dsum += dv;
                            dl.Add(($"S{n}-D{dn}", dv, deds[j]));
                        }
                        double net = gross - dsum;
                        totG += gross; totD += dsum; totN += net;
                        if (net < 0) ed.WriteMessage($"\nWARNING: deductions on S{n} exceed its area.");

                        var cen = Centroid(s);
                        CenteredText(tr, btr, cen, "S" + n, th, LayLbl);
                        rows.Add(new[] { "S" + n, gross.ToString("F2"), "", net.ToString("F2") });
                        foreach (var x in dl)
                        {
                            CenteredText(tr, btr, Centroid(x.ent), x.lbl, th, LayLbl);
                            rows.Add(new[] { x.lbl, "", x.val.ToString("F2"), "" });
                        }
                    }

                    int unm = 0;
                    for (int j = 0; j < deds.Count; j++)
                        if (pidx[j] == null) { unm++; CenteredText(tr, btr, Centroid(deds[j]), "D? NO SLAB", th, LayLbl); }
                    if (unm > 0) ed.WriteMessage($"\nWARNING: {unm} deduction polygon(s) were not inside any selected slab boundary (marked D? NO SLAB, not counted).");

                    rows.Add(new[] { "TOTAL", totG.ToString("F2"), totD.ToString("F2"), totN.ToString("F2") });
                    ed.WriteMessage($"\nSlab gross {totG:F2} {AreaLabel}   Deductions {totD:F2} {AreaLabel}   NET {totN:F2} {AreaLabel}");
                    Output(tr, db, "Slabs", new[] { "Item", $"Gross ({AreaLabel})", $"Deduction ({AreaLabel})", $"Net ({AreaLabel})" }, rows, th);
                }
                tr.Commit();
            }
        }
    }
}
