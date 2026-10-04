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

        /// <summary>
        /// For each deduction, the index of the nearest line it overlaps within tol (null if none).
        /// Bounding boxes are read once per curve instead of once per pair.
        /// </summary>
        private static int?[] MatchNearest(IList<Curve> deds, IList<Curve> lines, double tol)
        {
            Box2? Box(Curve x)
            {
                try { var e = x.GeometricExtents; return new Box2(e.MinPoint.X, e.MinPoint.Y, e.MaxPoint.X, e.MaxPoint.Y); }
                catch { return null; }
            }
            var cand = BoxIndex.Candidates(deds.Select(Box).ToArray(), lines.Select(Box).ToArray(), tol);
            var res = new int?[deds.Count];
            for (int i = 0; i < deds.Count; i++)
            {
                int? best = null; double bd = double.MaxValue;
                foreach (int k in cand[i])
                {
                    double md = MaxDist(deds[i], lines[k]);
                    if (md <= tol && md < bd) { bd = md; best = k; }
                }
                res[i] = best;
            }
            return res;
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

        /// <summary>Creates the settings file if needed and shows where it is.</summary>
        [CommandMethod("HCWSETTINGS")]
        public void OpenSettings()
        {
            var ed = Util.Ed;
            string path = Settings.EnsureFile();
            ed.WriteMessage("\nSettings file: " + path + "\nOpen it in any text editor, then restart the host.");
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

        internal static List<ObjectId> DrawTable(Transaction tr, Database db, Point3d pt, string[] headers, List<string[]> rows, double h, string layer = null)
        {
            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var made = new List<ObjectId>();
            layer = layer ?? LayTbl;
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
                var ln = new Line(new Point3d(xs[0], y0 - k * rh, 0), new Point3d(xs[nc], y0 - k * rh, 0)) { Layer = layer };
                made.Add(btr.AppendEntity(ln)); tr.AddNewlyCreatedDBObject(ln, true);
            }
            foreach (var x in xs)
            {
                var ln = new Line(new Point3d(x, y0, 0), new Point3d(x, y0 - nr * rh, 0)) { Layer = layer };
                made.Add(btr.AppendEntity(ln)); tr.AddNewlyCreatedDBObject(ln, true);
            }
            for (int k = 0; k < nr; k++)
            {
                for (int j = 0; j < all[k].Length; j++)
                {
                    var t = new DBText
                    {
                        Position = new Point3d(xs[j] + 0.5 * h, y0 - (k + 1) * rh + 0.5 * h, 0),
                        Height = h, TextString = all[k][j] ?? "", Layer = layer
                    };
                    made.Add(btr.AppendEntity(t)); tr.AddNewlyCreatedDBObject(t, true);
                }
            }
            return made;
        }
    }
}
