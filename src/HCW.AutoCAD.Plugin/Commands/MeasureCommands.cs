using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HCW.AutoCAD.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT take-off. Units come from the drawing (INSUNITS).
    /// TOSTART creates the layers. Each element has its own command.
    /// </summary>
    public class MeasureCommands
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

        public static int RndPublic(double v) => Rnd(v);

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

        private static bool Prepare()
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

                    // match each deduction to the typed line it overlaps (nearest within tolerance)
                    var pidx = new int?[deds.Count];
                    for (int di = 0; di < deds.Count; di++)
                    {
                        int? best = null; double bd = double.MaxValue;
                        for (int k = 0; k < grp.Count; k++)
                        {
                            if (!Near(deds[di], grp[k].ent, tol)) continue;
                            double md = MaxDist(deds[di], grp[k].ent);
                            if (md <= tol && md < bd) { bd = md; best = k; }
                        }
                        pidx[di] = best;
                    }

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
                        foreach (var seg in OrderWalls(segs, grp.Select(g => g.ent).ToList(), order, numberPath))
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
                                foreach (int j in Enumerable.Range(0, deds.Count)
                                    .Where(n => pidx[n] == seg.index)
                                    .OrderBy(n => CurveMid(deds[n]).X).ThenBy(n => CurveMid(deds[n]).Y))
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
                    foreach (var o in book.Openings.Where(x => x.LintelBottom <= 0 && floor.LintelBottom > 0 && x.Height > floor.LintelBottom))
                        ed.WriteMessage("\nWARNING: " + o.Mark + " is " + o.Height + " high, above the " + floor.Name + " lintel bottom " + floor.LintelBottom + ".");
                }
                var lintelFloor = book.Floors.FirstOrDefault(f => f.LintelBottom > 0);
                foreach (var o in book.Openings.Where(x => x.Sill > 0))
                {
                    double lintel = o.LintelBottom > 0 ? o.LintelBottom : (lintelFloor == null ? 0 : lintelFloor.LintelBottom);
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

                    var parent = new int?[deds.Count];
                    for (int i = 0; i < deds.Count; i++)
                    {
                        int? best = null; double bd = double.MaxValue;
                        for (int k = 0; k < walls.Count; k++)
                        {
                            if (!Near(deds[i], walls[k], MeasureState.DedupTolerance)) continue;
                            double md = MaxDist(deds[i], walls[k]);
                            if (md <= MeasureState.DedupTolerance && md < bd) { bd = md; best = k; }
                        }
                        parent[i] = best;
                    }

                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    double tot = 0;
                    int letter = 0;
                    var sized = walls.Select(w => new { Curve = w, Len = Rnd(CurveLen(w)) }).GroupBy(w => w.Len).OrderByDescending(g => g.Key);
                    foreach (var bucket in sized)
                    {
                        string group = "P-" + GroupLetter(letter++);
                        double gross = (bucket.Key / (MeasureState.Units == UnitSys.Imperial ? 8.0 * 12.0 : 100.0)) * height * bucket.Count();
                        double deduct = 0;
                        foreach (var wall in bucket)
                        {
                            int index = walls.IndexOf(wall.Curve);
                            PlaceLabel(tr, db, btr, CurveMid(wall.Curve), group, book, th, force: group);
                            for (int i = 0; i < deds.Count; i++)
                            {
                                if (parent[i] != index) continue;
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
                    var pidx = new int?[deds.Count];
                    for (int di = 0; di < deds.Count; di++)
                    {
                        var dcen = Centroid(deds[di]);
                        int? best = null;
                        for (int k = 0; k < slabs.Count; k++)
                            if (PtInPoly(dcen, Verts(slabs[k]))) best = k;
                        pidx[di] = best;
                    }

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
                        for (int j = 0; j < deds.Count; j++)
                        {
                            if (pidx[j] == k)
                            {
                                double dv = R2(Math.Abs(deds[j].Area));
                                dn++; dsum += dv;
                                dl.Add(($"S{n}-D{dn}", dv, deds[j]));
                            }
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

        // ---- shared helpers ----

        private static SelectionFilter BuildFilter(string dxfTypes, string[] layers)
        {
            var list = new List<Autodesk.AutoCAD.DatabaseServices.TypedValue>
            {
                new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.Start, dxfTypes),
                new Autodesk.AutoCAD.DatabaseServices.TypedValue((int)DxfCode.LayerName, string.Join(",", layers))
            };
            return new SelectionFilter(list.ToArray());
        }

        private static IDisposable IsolateAndPrompt(Transaction tr, Database db, string[] layers, string prompt)
        {
            Util.Ed.WriteMessage(prompt);
            var state = Util.IsolateLayers(tr, db, layers);
            foreach (var name in state.Hidden)
                MeasureState.HiddenByMeasure.Add(name);
            return new LayerRestorer(tr, db, state);
        }

        private class LayerRestorer : IDisposable
        {
            private readonly Transaction _tr;
            private readonly Database _db;
            private readonly LayerIsolation _state;
            public LayerRestorer(Transaction tr, Database db, LayerIsolation state)
            {
                _tr = tr; _db = db; _state = state;
            }
            public void Dispose()
            {
                // Must run while the transaction is still open. Committing first
                // and restoring on dispose throws, which aborted every Measure command.
                Util.RestoreLayers(_tr, _db, _state);
                if (_state?.Hidden == null) return;
                foreach (var name in _state.Hidden)
                    MeasureState.HiddenByMeasure.Remove(name);
            }
        }

        private static List<Point3d> Verts(Polyline pl)
        {
            var l = new List<Point3d>();
            for (int i = 0; i < pl.NumberOfVertices; i++) l.Add(pl.GetPoint3dAt(i));
            return l;
        }

        private static Point3d Centroid(Polyline pl)
        {
            int n = pl.NumberOfVertices; double sx = 0, sy = 0;
            for (int i = 0; i < n; i++) { var p = pl.GetPoint2dAt(i); sx += p.X; sy += p.Y; }
            return new Point3d(sx / n, sy / n, 0);
        }

        /// <summary>Ray-casting point-in-polygon test (m:pt-in-poly).</summary>
        private static bool PtInPoly(Point3d p, List<Point3d> poly)
        {
            int n = poly.Count; bool c = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = poly[i].X, yi = poly[i].Y, xj = poly[j].X, yj = poly[j].Y;
                if (((yi > p.Y) != (yj > p.Y)) && (p.X < xi + (xj - xi) * (p.Y - yi) / (yj - yi)))
                    c = !c;
            }
            return c;
        }

        /// <summary>4-vertex axis- (or any-) angle rectangle check, returns corners in order or null (m:rect).</summary>
        private static Point3d[] RectCorners(Polyline pl)
        {
            var v = Verts(pl);
            bool closed = pl.Closed;
            if (v.Count == 5 && v[0].DistanceTo(v[4]) < 1e-6) { v.RemoveAt(4); closed = true; }
            if (!closed || v.Count != 4) return null;
            if (!Perp(v[0], v[1], v[2]) || !Perp(v[1], v[2], v[3]) || !Perp(v[2], v[3], v[0])) return null;
            return v.ToArray();
        }
        private static bool Perp(Point3d a, Point3d b, Point3d c)
        {
            var v1 = b - a; var v2 = c - b;
            double m1 = v1.Length, m2 = v2.Length;
            if (m1 < 1e-9 || m2 < 1e-9) return false;
            return Math.Abs(v1.DotProduct(v2)) <= 1e-4 * m1 * m2;
        }

        private const string AppName = "HCWKIT";

        private static string GroupLetter(int index)
        {
            index++;
            var letters = "";
            while (index > 0)
            {
                index--;
                letters = (char)('A' + index % 26) + letters;
                index /= 26;
            }
            return letters;
        }

        private static void PlaceLabel(Transaction tr, Database db, BlockTableRecord btr, Point3d pt, string raw, MeasureBook book, double h, string force = null, int measured = -1)
        {
            var t = new DBText
            {
                Position = pt, Height = h, TextString = force ?? DisplayName(book, raw), Layer = LayLbl,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = pt
            };
            btr.AppendEntity(t);
            tr.AddNewlyCreatedDBObject(t, true);
            TagLabel(tr, db, t, raw, measured);
        }

        private static string DisplayName(MeasureBook book, string raw)
        {
            string mark = book.MarkFor(raw);
            if (string.IsNullOrWhiteSpace(mark)) return raw;
            var opening = book.Opening(mark);
            if (opening != null && !string.IsNullOrWhiteSpace(opening.Type))
                return mark + " " + opening.Type;
            var column = book.Columns.FirstOrDefault(c => string.Equals(c.Mark, mark, StringComparison.OrdinalIgnoreCase));
            if (column != null && !string.IsNullOrWhiteSpace(column.Name))
                return mark + " " + column.Name;
            return mark;
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(AppName)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = AppName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void TagLabel(Transaction tr, Database db, DBText text, string raw, int measured = -1)
        {
            EnsureRegApp(tr, db);
            text.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "MEASURE"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, raw ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "LEN"),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, measured));
        }

        /// <summary>The rounded length stored with a deduction label, or -1 when none was stored.</summary>
        private static int LabelLength(DBText text)
        {
            var data = text.GetXDataForApplication(AppName);
            if (data == null) return -1;
            var vals = data.AsArray();
            for (int i = 0; i < vals.Length - 1; i++)
                if (vals[i].TypeCode == (int)DxfCode.ExtendedDataAsciiString
                    && string.Equals(vals[i].Value as string, "LEN", StringComparison.Ordinal)
                    && vals[i + 1].Value is int len)
                    return len;
            return -1;
        }

        /// <summary>True for deduction labels: FB01-D1 (wall, opening) and the older FB D-01.</summary>
        private static bool IsDeductionLabel(string raw)
        {
            return raw != null && System.Text.RegularExpressions.Regex.IsMatch(raw, @"(\s|-)D-?\d+$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string RawLabel(DBText text)
        {
            var data = text.GetXDataForApplication(AppName);
            if (data != null)
            {
                var vals = data.AsArray();
                for (int i = 0; i < vals.Length - 1; i++)
                {
                    if (vals[i].TypeCode == (int)DxfCode.ExtendedDataAsciiString
                        && string.Equals(vals[i].Value as string, "MEASURE", StringComparison.Ordinal))
                        return vals[i + 1].Value as string ?? text.TextString ?? "";
                }
            }
            return text.TextString ?? "";
        }

        private static int ApplyNames(Transaction tr, Database db, MeasureBook book)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            int renamed = 0;
            foreach (ObjectId id in space)
            {
                var text = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (text == null || !string.Equals(text.Layer, LayLbl, StringComparison.OrdinalIgnoreCase)) continue;
                string raw = RawLabel(text);
                string shown = DisplayName(book, raw);
                if (string.Equals(text.TextString, shown, StringComparison.Ordinal)) continue;
                text.UpgradeOpen();
                text.TextString = shown;
                renamed++;
            }
            return renamed;
        }

        private class ScheduleTable
        {
            public string Title;
            public string[] Headers;
            public List<string[]> Rows = new List<string[]>();
        }

        /// <summary>One table per group: floors, doors and windows, columns, and the deduction map.</summary>
        private static List<ScheduleTable> ScheduleTables(MeasureBook book)
        {
            var tables = new List<ScheduleTable>();
            string u = " (" + ScheduleUnit + ")";
            Func<double, string> f = v => v > 0 ? v.ToString("0.###") : "";

            if (book.Floors.Count > 0)
            {
                var t = new ScheduleTable { Title = "FLOOR HEIGHTS", Headers = new[] { "Floor", "FFL to FFL" + u, "Ceiling" + u, "Lintel bottom" + u } };
                foreach (var floor in book.Floors)
                    t.Rows.Add(new[] { floor.Name, f(floor.FflHeight), f(floor.Height), f(floor.LintelBottom) });
                tables.Add(t);
            }
            if (book.Openings.Count > 0)
            {
                var t = new ScheduleTable
                {
                    Title = "DOOR AND WINDOW SCHEDULE",
                    Headers = new[] { "Name", "Kind", "Type", "Length" + u, "Height" + u, "Sill" + u, "Lintel bottom" + u, "Count" }
                };
                foreach (var kind in new[] { "Door", "Window" })
                {
                    var group = book.Openings.Where(q => string.Equals(q.Kind, kind, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var o in group)
                        t.Rows.Add(new[] { o.Mark, o.Kind, o.Type, f(o.Width), f(o.Height), f(o.Sill), f(o.LintelBottom), Math.Max(1, o.Count).ToString() });
                    if (group.Count > 0)
                        t.Rows.Add(new[] { "TOTAL", kind + "s", "", "", "", "", "", group.Sum(q => Math.Max(1, q.Count)).ToString() });
                }
                // Anything that is neither kind still appears, so no entry goes missing.
                foreach (var o in book.Openings.Where(q => !string.Equals(q.Kind, "Door", StringComparison.OrdinalIgnoreCase)
                                                          && !string.Equals(q.Kind, "Window", StringComparison.OrdinalIgnoreCase)))
                    t.Rows.Add(new[] { o.Mark, o.Kind, o.Type, f(o.Width), f(o.Height), f(o.Sill), f(o.LintelBottom), Math.Max(1, o.Count).ToString() });
                tables.Add(t);
            }
            if (book.Columns.Count > 0)
            {
                var t = new ScheduleTable { Title = "COLUMN SCHEDULE", Headers = new[] { "Mark", "Width" + u, "Depth" + u, "Name", "Count" } };
                foreach (var c in book.Columns)
                    t.Rows.Add(new[] { c.Mark, f(c.Width), f(c.Depth), c.Name, Math.Max(1, c.Count).ToString() });
                t.Rows.Add(new[] { "TOTAL", "", "", "", book.Columns.Sum(c => Math.Max(1, c.Count)).ToString() });
                tables.Add(t);
            }
            var mapped = book.Maps.Where(m => !string.IsNullOrWhiteSpace(m.Mark)).ToList();
            if (mapped.Count > 0)
            {
                var t = new ScheduleTable { Title = "DEDUCTION MAP", Headers = new[] { "Deduction", "Schedule name" } };
                foreach (var m in mapped) t.Rows.Add(new[] { m.Label, m.Mark });
                tables.Add(t);
            }
            return tables;
        }

        private static double AskFloorHeight(Editor ed, MeasureBook book)
        {
            var floors = book.Floors.Where(f => f.Height > 0).Take(12).ToList();
            if (floors.Count == 0)
            {
                var opt = new PromptDoubleOptions("\nWall height (" + ScheduleUnit + ") <3>: ")
                {
                    DefaultValue = 3, AllowNegative = false, AllowZero = false, UseDefaultValue = true
                };
                var got = ed.GetDouble(opt);
                return got.Status == PromptStatus.OK ? got.Value : 0;
            }
            var pko = new PromptKeywordOptions("\nFloor height");
            for (int i = 0; i < floors.Count; i++)
                pko.Keywords.Add("F" + (i + 1));
            pko.Keywords.Add("All");
            pko.Keywords.Default = "F1";
            ed.WriteMessage("\n" + string.Join(", ", floors.Select((f, i) => "F" + (i + 1) + " " + f.Name + " " + f.Height.ToString("0.###"))));
            var key = ed.GetKeywords(pko);
            if (key.Status != PromptStatus.OK) return 0;
            if (string.Equals(key.StringResult, "All", StringComparison.OrdinalIgnoreCase))
                return floors.Sum(f => f.Height);
            int index = int.Parse(key.StringResult.Substring(1)) - 1;
            return floors[index].Height;
        }

        /// <summary>Positions and names of every deduction label in the current space, read once per run.</summary>
        private static List<KeyValuePair<Point3d, string>> DeductionLabels(Transaction tr, Database db)
        {
            var list = new List<KeyValuePair<Point3d, string>>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var text = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (text == null || !string.Equals(text.Layer, LayLbl, StringComparison.OrdinalIgnoreCase)) continue;
                string raw = RawLabel(text);
                if (IsDeductionLabel(raw)) list.Add(new KeyValuePair<Point3d, string>(text.Position, raw));
            }
            return list;
        }

        /// <summary>
        /// Area of one opening: the schedule size when the opening is known (from its block, or from the
        /// label next to the deduction line), otherwise the measured length times the wall height.
        /// </summary>
        private static double OpeningArea(MeasureBook book, List<KeyValuePair<Point3d, string>> labels, Curve deduction,
            double wallHeight, double textHeight, string blockName)
        {
            var byBlock = book.OpeningForBlock(blockName);
            if (byBlock != null && byBlock.Width > 0 && byBlock.Height > 0)
                return byBlock.Width * byBlock.Height;

            var mid = CurveMid(deduction);
            double reach = Math.Max(4 * textHeight, 0.25);
            foreach (var label in labels)
            {
                if (label.Key.DistanceTo(mid) > reach) continue;
                var opening = book.Opening(book.MarkFor(label.Value));
                if (opening != null && opening.Width > 0 && opening.Height > 0)
                    return opening.Width * opening.Height;
            }
            int width = Rnd(CurveLen(deduction));
            double length = MeasureState.Units == UnitSys.Imperial ? width / (8.0 * 12.0) : width / 100.0;
            return length * wallHeight;
        }

        /// <summary>Maps a deduction to the schedule entry its block is assigned to, and checks the length.</summary>
        private static void MapBlock(Editor ed, MeasureBook book, string blockName, string raw, int measured, ref bool changed)
        {
            var opening = book.OpeningForBlock(blockName);
            if (opening == null) return;
            if (!string.Equals(book.MarkFor(raw), opening.Mark, StringComparison.OrdinalIgnoreCase))
            {
                book.SetMap(raw, opening.Mark);
                changed = true;
            }
            if (Math.Abs(measured - opening.WidthRounded) > SuggestTolerance)
                ed.WriteMessage("\nWARNING: " + raw + " (block " + blockName + ") measures " + M(measured)
                    + " but " + opening.Mark + " is " + M(opening.WidthRounded) + ".");
        }

        /// <summary>
        /// Order in which walls are numbered: LeftRight (then bottom to top), TopBottom (then left to right),
        /// or Path (by distance along a picked line, measured to the closest point to each wall's midpoint).
        /// </summary>
        private static IEnumerable<(int index, int gross)> OrderWalls(List<(int index, int gross)> segs, List<Curve> curves, string order, Curve path)
        {
            if (path != null)
                return segs.OrderBy(x => path.GetDistAtPoint(path.GetClosestPointTo(CurveMid(curves[x.index]), false)));
            if (string.Equals(order, "TopBottom", StringComparison.OrdinalIgnoreCase))
                return segs.OrderByDescending(x => CurveMid(curves[x.index]).Y).ThenBy(x => CurveMid(curves[x.index]).X);
            return segs.OrderBy(x => CurveMid(curves[x.index]).X).ThenBy(x => CurveMid(curves[x.index]).Y);
        }

        /// <summary>Quick reject before the exact distance test: do the two curves' boxes come within the tolerance?</summary>
        private static bool Near(Curve a, Curve b, double tol)
        {
            try
            {
                var ea = a.GeometricExtents;
                var eb = b.GeometricExtents;
                return ea.MinPoint.X - tol <= eb.MaxPoint.X && eb.MinPoint.X - tol <= ea.MaxPoint.X
                    && ea.MinPoint.Y - tol <= eb.MaxPoint.Y && eb.MinPoint.Y - tol <= ea.MaxPoint.Y;
            }
            catch { return true; }
        }

        private static void Output(Transaction tr, Database db, string defName, string[] headers, List<string[]> rows, double h)
        {
            var ed = Util.Ed;
            var ppr = ed.GetPoint("\nPick table insertion point (Enter = no table in drawing): ");
            if (ppr.Status == PromptStatus.OK)
                DrawTable(tr, db, ppr.Value, headers, rows, h);

            // The CSV is written on demand by MEXPORT, not with every take-off.
            _lastName = defName;
            _lastHeaders = headers;
            _lastRows = rows;
            MeasureBook.SaveTakeoff(tr, db, defName, headers, rows);
            ed.WriteMessage("\nRun MEXPORT to save this take-off as CSV or Excel.");
        }

        private static string _lastName;
        private static string[] _lastHeaders;
        private static List<string[]> _lastRows;

        /// <summary>Creates the settings file if needed and opens it for editing.</summary>
        [CommandMethod("HCWSETTINGS")]
        public void OpenSettings()
        {
            var ed = Util.Ed;
            string path = Settings.EnsureFile();
            ed.WriteMessage("\nSettings file: " + path + "\nRestart the host after editing it.");
            try { System.Diagnostics.Process.Start(path); }
            catch { ed.WriteMessage("\nCould not open it; open the file in any text editor."); }
        }

        /// <summary>Saves take-offs: the latest as CSV, or every saved take-off as one Excel workbook.</summary>
        [CommandMethod("MEXPORT")]
        public void Export()
        {
            var ed = Util.Ed;
            var opt = new PromptKeywordOptions("\nExport as [Csv/Xlsx] <Csv>: ") { AllowNone = true };
            opt.Keywords.Add("Csv");
            opt.Keywords.Add("Xlsx");
            var pick = ed.GetKeywords(opt);
            if (pick.Status != PromptStatus.OK && pick.Status != PromptStatus.None) return;
            if (pick.Status == PromptStatus.OK && pick.StringResult == "Xlsx") ExportXlsx();
            else ExportCsv();
        }

        [CommandMethod("MEXPORTX")]
        public void ExportXlsx()
        {
            var ed = Util.Ed; var db = Util.Db;
            List<MeasureBook.Takeoff> saved;
            MeasureBook book;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                saved = MeasureBook.LoadAllTakeoffs(tr, db);
                book = MeasureBook.Load(tr, db);
                tr.Commit();
            }
            if (saved.Count == 0)
            {
                ed.WriteMessage("\nMEXPORTX: run a take-off first.");
                return;
            }
            var data = saved.Select(t => new Logic.Billing.TakeoffData { Name = t.Name, Headers = t.Headers, Rows = t.Rows }).ToList();
            var rates = book.Rates.Select(r => new Logic.Billing.RateData { Takeoff = r.Takeoff, Unit = r.Unit, Rate = r.Rate }).ToList();

            var dwgPath = db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string stem = string.IsNullOrEmpty(dwgPath) ? "Drawing" : Path.GetFileNameWithoutExtension(dwgPath);
            string path = dir + stem + "-Takeoffs.xlsx";
            try
            {
                Logic.XlsxWriter.Write(path, Logic.Billing.Sheets(data, rates));
                ed.WriteMessage("\nSaved: " + path + " (" + data.Count + " take-off sheet" + (data.Count == 1 ? "" : "s")
                    + (rates.Any(r => r.Rate > 0) ? ", with a Bill sheet" : "") + ").");
            }
            catch { ed.WriteMessage("\nCould not write the Excel file (is it open elsewhere?)."); }
        }

        public void ExportCsv()
        {
            var ed = Util.Ed;
            if (_lastRows == null)
            {
                // After a restart the last take-off is read back from the drawing.
                using (var tr = Util.Db.TransactionManager.StartTransaction())
                {
                    var saved = MeasureBook.LoadTakeoff(tr, Util.Db);
                    tr.Commit();
                    if (saved != null)
                    {
                        _lastName = saved.Name;
                        _lastHeaders = saved.Headers;
                        _lastRows = saved.Rows;
                    }
                }
            }
            if (_lastRows == null)
            {
                ed.WriteMessage("\nMEXPORT: run a take-off first.");
                return;
            }
            var dwgPath = Util.Db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string csvPath = dir + _lastName + ".csv";
            try
            {
                Util.WriteCsv(csvPath, _lastHeaders, _lastRows.Select(r => (IEnumerable<string>)r));
                ed.WriteMessage("\nSaved: " + csvPath);
            }
            catch { ed.WriteMessage("\nCould not write CSV file (is it open elsewhere?)."); }
        }

        internal static void DrawTable(Transaction tr, Database db, Point3d pt, string[] headers, List<string[]> rows, double h)
        {
            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var all = new List<string[]> { headers };
            all.AddRange(rows);
            int nc = headers.Length, nr = all.Count;
            double rh = 2.0 * h, y0 = pt.Y;

            var ws = new double[nc];
            for (int j = 0; j < nc; j++)
            {
                double w = 0;
                foreach (var r in all) if (j < r.Length) w = Math.Max(w, (r[j] ?? "").Length);
                ws[j] = w * h * 0.85 + h;
            }
            var xs = new double[nc + 1];
            xs[0] = pt.X;
            for (int j = 0; j < nc; j++) xs[j + 1] = xs[j] + ws[j];

            for (int k = 0; k <= nr; k++)
            {
                var ln = new Line(new Point3d(xs[0], y0 - k * rh, 0), new Point3d(xs[nc], y0 - k * rh, 0)) { Layer = LayTbl };
                btr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
            }
            foreach (var x in xs)
            {
                var ln = new Line(new Point3d(x, y0, 0), new Point3d(x, y0 - nr * rh, 0)) { Layer = LayTbl };
                btr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
            }
            for (int k = 0; k < nr; k++)
            {
                for (int j = 0; j < all[k].Length; j++)
                {
                    var t = new DBText
                    {
                        Position = new Point3d(xs[j] + 0.5 * h, y0 - (k + 1) * rh + 0.5 * h, 0),
                        Height = h, TextString = all[k][j] ?? "", Layer = LayTbl
                    };
                    btr.AppendEntity(t); tr.AddNewlyCreatedDBObject(t, true);
                }
            }
        }
    }
}
