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
    /// HCWCLEAN tidies lines: zero-length lines and exact duplicates are erased, and (when you say so) collinear lines
    /// that touch or overlap are joined into one. Lines only join when their layer, linetype, colour, lineweight
    /// and elevation match. Lines on locked layers, and lines that are not flat, are left alone.
    /// </summary>
    public class CleanCommands
    {
        private static bool _join = true;

        /// <summary>
        /// HCWCORNER trims or extends two lines to meet at their corner, keeping the part of each that you click. Click the
        /// first line, then the second; a line that stopped short is extended, one that ran past is trimmed. Repeats until Enter.
        /// Like FILLET with radius 0, for lines only, on any layer that is not locked.
        /// </summary>
        [CommandMethod("HCWCORNER")]
        public void Corner()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            while (true)
            {
                var first = PickLine(ed, "\nSelect the first line, on the part to keep (Enter to finish): ");
                if (first == null) return;
                var second = PickLine(ed, "\nSelect the second line, on the part to keep: ");
                if (second == null) return;
                if (first.Value.Key == second.Value.Key) { ed.WriteMessage("\nHCWCORNER: pick two different lines."); continue; }

                using (Util.Doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var a = (Line)tr.GetObject(first.Value.Key, OpenMode.ForRead);
                    var b = (Line)tr.GetObject(second.Value.Key, OpenMode.ForRead);
                    string locked = null;
                    foreach (var ln in new[] { a, b })
                        if (((LayerTableRecord)tr.GetObject(ln.LayerId, OpenMode.ForRead)).IsLocked) locked = ln.Layer;
                    if (locked != null) { ed.WriteMessage("\nHCWCORNER: layer " + locked + " is locked."); continue; }

                    string error;
                    var fit = CornerFit.Fit(
                        new Seg(new P2(a.StartPoint.X, a.StartPoint.Y), new P2(a.EndPoint.X, a.EndPoint.Y)), new P2(first.Value.Value.X, first.Value.Value.Y),
                        new Seg(new P2(b.StartPoint.X, b.StartPoint.Y), new P2(b.EndPoint.X, b.EndPoint.Y)), new P2(second.Value.Value.X, second.Value.Value.Y), out error);
                    if (fit == null) { ed.WriteMessage("\nHCWCORNER: " + error + "."); continue; }

                    Apply(a, fit.A);
                    Apply(b, fit.B);
                    tr.Commit();
                }
                ed.WriteMessage("\nHCWCORNER: lines meet at the corner.");
            }
        }

        private static KeyValuePair<ObjectId, Point3d>? PickLine(Editor ed, string prompt)
        {
            var o = new PromptEntityOptions(prompt) { AllowNone = true };
            o.SetRejectMessage("\nSelect a line.");
            o.AddAllowedClass(typeof(Line), true);
            var r = ed.GetEntity(o);
            if (r.Status != PromptStatus.OK) return null;
            return new KeyValuePair<ObjectId, Point3d>(r.ObjectId, r.PickedPoint);
        }

        /// <summary>Gives a line new end points, keeping its elevation.</summary>
        private static void Apply(Line ln, Seg s)
        {
            ln.UpgradeOpen();
            double z = ln.StartPoint.Z;
            ln.StartPoint = new Point3d(s.A.X, s.A.Y, z);
            ln.EndPoint = new Point3d(s.B.X, s.B.Y, z);
        }

        [CommandMethod("HCWCLEAN")]
        public void CleanLines()
        {
            var ed = Util.Ed;
            var db = Util.Db;

            var jo = new PromptKeywordOptions("\nAlso join lines that touch or overlap on one straight line [Yes/No] <" + (_join ? "Yes" : "No") + ">: ", "Yes No") { AllowNone = true };
            jo.Keywords.Default = _join ? "Yes" : "No";
            var jr = ed.GetKeywords(jo);
            if (jr.Status == PromptStatus.OK) _join = jr.StringResult == "Yes";
            else if (jr.Status != PromptStatus.None) return;

            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect the lines to clean (Enter = every line in this space): " },
                new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LINE") }));
            if (psr.Status != PromptStatus.OK && psr.Status != PromptStatus.None) return;

            double tol = Util.MmToDrawingUnits(0.05);
            int locked = 0, notFlat = 0;
            CleanResult result;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = new List<ObjectId>();
                if (psr.Status == PromptStatus.OK) ids.AddRange(psr.Value.GetObjectIds());
                else
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    foreach (ObjectId id in space)
                        if (id.ObjectClass.DxfName == "LINE") ids.Add(id);
                }

                var layerLocked = new Dictionary<ObjectId, bool>();
                var lines = new List<Line>();
                var input = new List<CleanLine>();
                foreach (var id in ids)
                {
                    var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
                    if (ln == null || ln.IsErased) continue;
                    bool isLocked;
                    if (!layerLocked.TryGetValue(ln.LayerId, out isLocked))
                        layerLocked[ln.LayerId] = isLocked = ((LayerTableRecord)tr.GetObject(ln.LayerId, OpenMode.ForRead)).IsLocked;
                    if (isLocked) { locked++; continue; }
                    if (Math.Abs(ln.StartPoint.Z - ln.EndPoint.Z) > tol) { notFlat++; continue; }

                    lines.Add(ln);
                    input.Add(new CleanLine
                    {
                        Seg = new Seg(new P2(ln.StartPoint.X, ln.StartPoint.Y), new P2(ln.EndPoint.X, ln.EndPoint.Y)),
                        Key = ln.Layer + "|" + ln.Linetype + "|" + ln.Color + "|" + (int)ln.LineWeight + "|" + Math.Round(ln.StartPoint.Z / tol),
                    });
                }

                result = LineCleanup.Run(input, tol, _join);
                foreach (var kv in result.Replace)
                {
                    var ln = lines[kv.Key];
                    ln.UpgradeOpen();
                    double z = ln.StartPoint.Z;
                    ln.StartPoint = new Point3d(kv.Value.A.X, kv.Value.A.Y, z);
                    ln.EndPoint = new Point3d(kv.Value.B.X, kv.Value.B.Y, z);
                }
                foreach (int i in result.Erase)
                {
                    lines[i].UpgradeOpen();
                    lines[i].Erase();
                }
                tr.Commit();
            }

            int removed = result.Erase.Count;
            ed.WriteMessage("\nHCWCLEAN: " + removed + " line(s) erased"
                + (removed > 0 ? " (" + result.ZeroLength + " zero-length, " + result.Duplicates + " duplicate, " + result.Merged + " joined into longer lines)" : "")
                + ". " + (locked > 0 ? locked + " on locked layers left alone. " : "")
                + (notFlat > 0 ? notFlat + " not flat left alone." : ""));
        }
    }
}
