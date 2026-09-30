using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// hcwCAD-KIT manual take-off. Metric or Imperial is chosen once per
    /// session with MSETUP and stored on <see cref="MeasureState"/>.
    /// Commands: MSETUP MLIN MBRK MBML MCOL MREC MPAINT MCEIL MFLOOR MSCHED MARE MSLB MSHOW MCLEAR
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

        /// <summary>Schedule sizes are metres, or feet when Imperial is set.</summary>
        public static int RndSchedule(double displayLength)
        {
            double drawing = MeasureState.Units == UnitSys.Imperial ? displayLength * 12.0 : displayLength;
            return Rnd(drawing);
        }

        /// <summary>How far a deduction may differ from a schedule length and still be suggested: 50 mm, or 2 in (16 eighths).</summary>
        public static int SuggestTolerance => MeasureState.Units == UnitSys.Imperial ? 16 : 5;

        private static string ScheduleUnit => MeasureState.Units == UnitSys.Imperial ? "ft" : "m";

        private static int Rnd(double v) => MeasureState.Units == UnitSys.Imperial
            ? (int)Math.Floor(v * 8.0 + 0.5001)
            : (int)Math.Floor(v * 100.0 + 0.5001);

        private static double R2(double v)
        {
            double v2 = MeasureState.Units == UnitSys.Imperial ? v / 144.0 : v;
            return Math.Floor(v2 * 100.0 + 0.5) / 100.0;
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

        [CommandMethod("MSETUP")]
        public void MSetup()
        {
            var ed = Util.Ed; var db = Util.Db;
            var pko = new PromptKeywordOptions($"\nMeasurement unit system [Metric/Imperial] <{(MeasureState.Units == UnitSys.Imperial ? "Imperial" : "Metric")}>: ");
            pko.Keywords.Add("Metric"); pko.Keywords.Add("Imperial");
            pko.Keywords.Default = MeasureState.Units == UnitSys.Imperial ? "Imperial" : "Metric";
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.OK)
                MeasureState.Units = pkr.StringResult == "Imperial" ? UnitSys.Imperial : UnitSys.Metric;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                tr.Commit();
            }
            ed.WriteMessage("\nUnit system: " + (MeasureState.Units == UnitSys.Imperial
                ? "Imperial (1 drawing unit = 1 inch; lengths round to 1/8\")"
                : "Metric (1 drawing unit = 1 meter; lengths round to 1 cm)"));
            ed.WriteMessage($"\nLayers ready: {LayLin}, {LayDed}, {LayFb}, {LayHb}, {LayBm}, {LayLt}, {LayCnt}, {LayAre}, {LaySlb}, {LaySdd}");
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

                    double th = AskTextHeight(ed);
                    double tol = AskTolerance(ed);

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

                    // match each deduction to the typed line it overlaps (nearest within tolerance)
                    var pidx = new int?[deds.Count];
                    for (int di = 0; di < deds.Count; di++)
                    {
                        int? best = null; double bd = double.MaxValue;
                        for (int k = 0; k < grp.Count; k++)
                        {
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
                        foreach (var seg in segs.OrderBy(x => CurveMid(grp[x.index].ent).X).ThenBy(x => CurveMid(grp[x.index].ent).Y))
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
                        if (pidx[j] == null)
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

                    double th = AskTextHeight(ed);
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
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var book = MeasureBook.Load(tr, db);
                if (ScheduleRows(book).Count == 0)
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
            var rows = ScheduleRows(book);
            if (rows.Count == 0) return false;
            var ppr = ed.GetPoint("\nPick a point for the schedule table: ");
            if (ppr.Status != PromptStatus.OK) return false;
            DrawTable(tr, db, ppr.Value, new[] { "Item", "Detail", "Size", "Count" }, rows, MeasureState.TextHeight);
            return true;
        }

        [CommandMethod("MSCHED")]
        public void MSched()
        {
            var ed = Util.Ed; var db = Util.Db;
            MeasureBook book;
            var labels = new List<KeyValuePair<string, int>>();
            var lengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                book = MeasureBook.Load(tr, db);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
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

            using (var dlg = new UI.MeasureScheduleForm(book, labels, ScheduleUnit))
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                book = dlg.Read();
                book.CountFromMaps();
                foreach (var floor in book.Floors)
                {
                    if (floor.FflHeight > 0 && floor.Height > floor.FflHeight)
                        ed.WriteMessage("\nWARNING: " + floor.Name + " ceiling height " + floor.Height + " is more than its FFL to FFL height " + floor.FflHeight + ".");
                    if (floor.Height > 0 && floor.LintelBottom > floor.Height)
                        ed.WriteMessage("\nWARNING: " + floor.Name + " lintel bottom " + floor.LintelBottom + " is above its ceiling height " + floor.Height + ".");
                    foreach (var o in book.Openings.Where(x => x.Height > floor.LintelBottom && floor.LintelBottom > 0))
                        ed.WriteMessage("\nWARNING: " + o.Mark + " is " + o.Height + " high, above the " + floor.Name + " lintel bottom " + floor.LintelBottom + ".");
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
                    double th = AskTextHeight(ed);
                    var walls = new List<Curve>();
                    var deds = new List<Curve>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var c = (Curve)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (string.Equals(c.Layer, dedLayer, StringComparison.OrdinalIgnoreCase)) deds.Add(c);
                        else walls.Add(c);
                    }
                    if (walls.Count == 0) { ed.WriteMessage("\nNo wall lines selected."); return; }

                    var parent = new int?[deds.Count];
                    for (int i = 0; i < deds.Count; i++)
                    {
                        int? best = null; double bd = double.MaxValue;
                        for (int k = 0; k < walls.Count; k++)
                        {
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
                                deduct += OpeningArea(tr, db, book, deds[i], height, th);
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
                    double th = AskTextHeight(ed);
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
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { LayAre }, "\nSelect closed polygons/circles on MEASURE-AREA: "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE,POLYLINE,CIRCLE,ELLIPSE,SPLINE", new[] { LayAre }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = AskTextHeight(ed);
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
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { LaySlb, LaySdd }, "\nSelect slab boundary polygons AND deduction (opening) polygons: "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE,POLYLINE", new[] { LaySlb, LaySdd }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); return; }

                    double th = AskTextHeight(ed);
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

        private static double AskTextHeight(Editor ed)
        {
            var opt = new PromptDoubleOptions($"\nLabel text height <{MeasureState.TextHeight:F2}>: ")
            { AllowZero = false, AllowNegative = false, DefaultValue = MeasureState.TextHeight, UseDefaultValue = true };
            var r = ed.GetDouble(opt);
            if (r.Status == PromptStatus.OK) MeasureState.TextHeight = r.Value;
            return MeasureState.TextHeight;
        }

        private static double AskTolerance(Editor ed)
        {
            var opt = new PromptDoubleOptions($"\nDeduction overlap tolerance (m) <{MeasureState.DedupTolerance:F3}>: ")
            { AllowZero = false, AllowNegative = false, DefaultValue = MeasureState.DedupTolerance, UseDefaultValue = true };
            var r = ed.GetDouble(opt);
            if (r.Status == PromptStatus.OK) MeasureState.DedupTolerance = r.Value;
            return MeasureState.DedupTolerance;
        }

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

        private static List<string[]> ScheduleRows(MeasureBook book)
        {
            var rows = new List<string[]>();
            foreach (var floor in book.Floors)
            {
                if (floor.FflHeight > 0)
                    rows.Add(new[] { floor.Name, "FFL to FFL height", floor.FflHeight.ToString("0.###") + " " + ScheduleUnit, "" });
                if (floor.Height > 0)
                    rows.Add(new[] { floor.Name, "Ceiling height", floor.Height.ToString("0.###") + " " + ScheduleUnit, "" });
                if (floor.LintelBottom > 0)
                    rows.Add(new[] { floor.Name, "Lintel bottom height", floor.LintelBottom.ToString("0.###") + " " + ScheduleUnit, "" });
            }
            foreach (var opening in book.Openings)
                rows.Add(new[]
                {
                    opening.Mark,
                    string.IsNullOrWhiteSpace(opening.Type) ? opening.Kind : opening.Kind + ", " + opening.Type,
                    opening.Width.ToString("0.###") + " x " + opening.Height.ToString("0.###"),
                    Math.Max(1, opening.Count).ToString()
                });
            foreach (var column in book.Columns)
                rows.Add(new[]
                {
                    column.Mark,
                    string.IsNullOrWhiteSpace(column.Name) ? "Column" : column.Name,
                    column.Width.ToString("0.###") + " x " + column.Depth.ToString("0.###"),
                    Math.Max(1, column.Count).ToString()
                });
            foreach (var map in book.Maps)
            {
                if (string.IsNullOrWhiteSpace(map.Mark)) continue;
                rows.Add(new[] { map.Label, "Maps to " + map.Mark, "", "" });
            }
            return rows;
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

        private static double OpeningArea(Transaction tr, Database db, MeasureBook book, Curve deduction, double wallHeight, double textHeight)
        {
            var mid = CurveMid(deduction);
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                var text = tr.GetObject(id, OpenMode.ForRead) as DBText;
                if (text == null || !string.Equals(text.Layer, LayLbl, StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Position.DistanceTo(mid) > Math.Max(4 * textHeight, 0.25)) continue;
                string raw = RawLabel(text);
                if (!IsDeductionLabel(raw)) continue;
                var opening = book.Opening(book.MarkFor(raw));
                if (opening != null && opening.Width > 0 && opening.Height > 0)
                    return opening.Width * opening.Height;
            }
            int width = Rnd(CurveLen(deduction));
            double length = MeasureState.Units == UnitSys.Imperial ? width / (8.0 * 12.0) : width / 100.0;
            return length * wallHeight;
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
            ed.WriteMessage("\nRun MEXPORT to save this take-off as CSV.");
        }

        private static string _lastName;
        private static string[] _lastHeaders;
        private static List<string[]> _lastRows;

        [CommandMethod("MEXPORT")]
        public void ExportCsv()
        {
            var ed = Util.Ed;
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

        private static void DrawTable(Transaction tr, Database db, Point3d pt, string[] headers, List<string[]> rows, double h)
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
