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
    /// MEASURE - manual linear/rectangle/area take-off. Ported from HCW-ALL.lsp
    /// Section 9 (the MEASURE tool that replaced AECQTY). Unit system (Metric/
    /// Imperial) is chosen once via MSETUP and stored on <see cref="MeasureState"/>,
    /// exactly like *M-UNITSYS* in the LISP source.
    /// Commands: MSETUP MLIN MBRK MBML MREC MARE MSLB MSHOW MCLEAR
    /// </summary>
    public class MeasureCommands
    {
        public enum UnitSys { Metric, Imperial }

        public static class MeasureState
        {
            public static UnitSys Units = UnitSys.Metric;
            public static double TextHeight = 0.25;
            public static double DedupTolerance = 0.01;
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
        }

        // ---- unit-aware rounding / formatting (m:rnd / m:m / m:area-from-rnd / m:r2) ----

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
            using (Util.Doc.LockDocument())
            using (var tr = Util.Db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(Util.Db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId id in lt) ((LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite)).IsOff = false;
                tr.Commit();
            }
            Util.Ed.WriteMessage("\nAll layers on.");
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
        public void MBml() => RunTypedLinear(new[] { (LayBm, "BM", "Beam"), (LayLt, "LT", "Lintel") }, LayDed,
            "\nSelect beam, lintel AND deduction lines: ", "BeamsLintels");

        /// <summary>
        /// N-typed linear take-off with deductions - shared by MLIN (1 type), MBRK
        /// (Full/Half Brick) and MBML (Beam/Lintel), exactly like m:run-typed-linear
        /// in the LISP source (built there specifically to avoid duplicating this
        /// ~100-line gross/deduction/net/subtotal/grand-total body per command).
        /// </summary>
        private void RunTypedLinear((string layer, string code, string name)[] types, string dedLayer,
            string selPrompt, string csvName, bool singleTypeNoPrefix = false)
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                var isolateLayers = types.Select(t => t.layer).Append(dedLayer).ToArray();
                using (var iso = IsolateAndPrompt(tr, db, isolateLayers, selPrompt))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(),
                        BuildFilter("LINE,LWPOLYLINE,POLYLINE,ARC,SPLINE", isolateLayers));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); tr.Commit(); return; }

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
                    if (grp.Count == 0) { ed.WriteMessage("\nNo matching typed lines selected."); tr.Commit(); return; }

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
                        int n = 0, cnt = 0, sG = 0, sD = 0, sN = 0;
                        for (int k = 0; k < grp.Count; k++)
                        {
                            if (grp[k].type != ty.code) continue;
                            n++; cnt++;
                            string lbl = singleTypeNoPrefix ? "L" + n : ty.code + n;
                            int gross = Rnd(CurveLen(grp[k].ent));
                            int dsum = 0, dn = 0;
                            var dl = new List<(string lbl, int val, Curve ent)>();
                            for (int j = 0; j < deds.Count; j++)
                            {
                                if (pidx[j] == k)
                                {
                                    dn++;
                                    int dv = Rnd(CurveLen(deds[j]));
                                    dsum += dv;
                                    dl.Add((lbl + "-D" + dn, dv, deds[j]));
                                }
                            }
                            int net = gross - dsum;
                            sG += gross; sD += dsum; sN += net;
                            if (net < 0) ed.WriteMessage($"\nWARNING: deductions on {lbl} exceed its length.");

                            var mp = CurveMid(grp[k].ent);
                            CenteredText(tr, btr, new Point3d(mp.X, mp.Y + 0.8 * th, 0), lbl, th, LayLbl);
                            rows.Add(singleTypeNoPrefix
                                ? new[] { lbl, M(gross), "", M(net) }
                                : new[] { lbl, ty.name, M(gross), "", M(net) });

                            foreach (var x in dl)
                            {
                                var dmp = CurveMid(x.ent);
                                CenteredText(tr, btr, new Point3d(dmp.X, dmp.Y - 0.8 * th, 0), x.lbl, th, LayLbl);
                                rows.Add(singleTypeNoPrefix
                                    ? new[] { x.lbl, "", M(x.val), "" }
                                    : new[] { x.lbl, ty.name, "", M(x.val), "" });
                            }
                        }
                        if (cnt > 0)
                        {
                            rows.Add(singleTypeNoPrefix
                                ? new[] { "TOTAL", M(sG), M(sD), M(sN) }
                                : new[] { "TOTAL", ty.name, M(sG), M(sD), M(sN) });
                            gG += sG; gD += sD; gN += sN;
                            ed.WriteMessage($"\n{ty.name}:  {M(sG)} {LenLabel}  less {M(sD)} {LenLabel}  =  NET {M(sN)} {LenLabel}");
                        }
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
                        ? new[] { "Item", $"Length ({LenLabel})", $"Deduction ({LenLabel})", $"Net ({LenLabel})" }
                        : new[] { "Item", "Type", $"Length ({LenLabel})", $"Deduction ({LenLabel})", $"Net ({LenLabel})" };
                    Output(tr, db, csvName, headers, rows, th);
                    tr.Commit();
                }
            }
        }

        [CommandMethod("MREC")]
        public void MRec()
        {
            var ed = Util.Ed; var db = Util.Db;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                MakeLayers(tr, db);
                using (var iso = IsolateAndPrompt(tr, db, new[] { LayCnt }, "\nSelect rectangles (closed polylines) on MEASURE-COUNT: "))
                {
                    var psr = ed.GetSelection(new PromptSelectionOptions(), BuildFilter("LWPOLYLINE", new[] { LayCnt }));
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); tr.Commit(); return; }

                    double th = AskTextHeight(ed);
                    var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var rows = new List<string[]>();
                    int n = 1, skipped = 0;
                    double totA = 0; int totP = 0;

                    foreach (SelectedObject so in psr.Value)
                    {
                        var pl = (Polyline)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        var rect = RectCorners(pl);
                        if (rect == null) { skipped++; continue; }
                        double a = rect[0].DistanceTo(rect[1]), b = rect[1].DistanceTo(rect[2]);
                        int len = Rnd(Math.Max(a, b)), brd = Rnd(Math.Min(a, b));
                        double ar = AreaFromRnd(len, brd);
                        int pr = 2 * (len + brd);
                        totA += ar; totP += pr;
                        var cen = new Point3d((rect[0].X + rect[2].X) / 2.0, (rect[0].Y + rect[2].Y) / 2.0, 0);
                        CenteredText(tr, btr, cen, "R" + n, th, LayLbl);
                        rows.Add(new[] { "R" + n, M(len), M(brd), ar.ToString("F2"), M(pr) });
                        n++;
                    }
                    if (skipped > 0) ed.WriteMessage($"\n{skipped} object(s) skipped (not closed 4-sided rectangles).");
                    if (rows.Count > 0)
                    {
                        rows.Add(new[] { "TOTAL", "", "", totA.ToString("F2"), M(totP) });
                        ed.WriteMessage($"\n{n - 1} rectangles named. Total area = {totA:F2} {AreaLabel}");
                        Output(tr, db, "Rectangles", new[] { "Rect No", $"Length ({LenLabel})", $"Breadth ({LenLabel})", $"Area ({AreaLabel})", $"Perimeter ({LenLabel})" }, rows, th);
                    }
                    tr.Commit();
                }
            }
        }

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
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); tr.Commit(); return; }

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
                    tr.Commit();
                }
            }
        }

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
                    if (psr.Status != PromptStatus.OK) { ed.WriteMessage("\nNothing selected."); tr.Commit(); return; }

                    double th = AskTextHeight(ed);
                    var slabs = new List<Polyline>();
                    var deds = new List<Polyline>();
                    foreach (SelectedObject so in psr.Value)
                    {
                        var pl = (Polyline)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                        if (string.Equals(pl.Layer, LaySdd, StringComparison.OrdinalIgnoreCase)) deds.Add(pl); else slabs.Add(pl);
                    }
                    if (slabs.Count == 0) { ed.WriteMessage("\nNo slab boundary polygons selected - deductions need a slab boundary."); tr.Commit(); return; }

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
                    tr.Commit();
                }
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
            return new LayerRestorer(tr, db, state);
        }

        private class LayerRestorer : IDisposable
        {
            private readonly Transaction _tr; private readonly Database _db; private readonly LayerIsolation _state;
            public LayerRestorer(Transaction tr, Database db, LayerIsolation state) { _tr = tr; _db = db; _state = state; }
            public void Dispose() => Util.RestoreLayers(_tr, _db, _state);
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

        private static void Output(Transaction tr, Database db, string defName, string[] headers, List<string[]> rows, double h)
        {
            var ed = Util.Ed;
            var ppr = ed.GetPoint("\nPick table insertion point (Enter = no table in drawing): ");
            if (ppr.Status == PromptStatus.OK)
                DrawTable(tr, db, ppr.Value, headers, rows, h);

            var dwgPath = db.Filename;
            string dir = string.IsNullOrEmpty(dwgPath) ? Path.GetTempPath() : Path.GetDirectoryName(dwgPath) + Path.DirectorySeparatorChar;
            string csvPath = dir + defName + ".csv";
            try
            {
                Util.WriteCsv(csvPath, headers, rows.Select(r => (IEnumerable<string>)r));
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
