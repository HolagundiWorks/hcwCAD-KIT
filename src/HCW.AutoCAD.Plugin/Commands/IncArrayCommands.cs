using System;
using HCW.AutoCAD.Plugin.Logic;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// Arrays a selection and increments every number found in the copied annotation.
    /// Leading zeros are kept only when the original number already had them.
    /// </summary>
    public class IncArrayCommands
    {
        private static decimal _increment = 1m;
        private static string _incrementText = "1";

        [CommandMethod("INCARRAY")]
        [CommandMethod("TextIncrement")]
        public void IncArray()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            string how = Util.AskMode("Place the copies by", "Array", "Picks");
            if (how == null) return;
            if (how == "Picks") { IncCopy(); return; }
            if (!AskIncrement(ed)) return;

            var filter = new SelectionFilter(new[]
            {
                new TypedValue(-4, "<NOT"),
                new TypedValue(0, "VIEWPORT"),
                new TypedValue(-4, "NOT>")
            });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect objects to array: " }, filter);
            if (psr.Status != PromptStatus.OK) return;

            var bpr = ed.GetPoint("\nSpecify base point: ");
            if (bpr.Status != PromptStatus.OK) return;

            var vectorOpts = new PromptPointOptions("\nSpecify array vector: ")
            {
                UseBasePoint = true,
                BasePoint = bpr.Value
            };
            PromptPointResult vpr;
            while (true)
            {
                vpr = ed.GetPoint(vectorOpts);
                if (vpr.Status != PromptStatus.OK) return;
                if (vpr.Value.DistanceTo(bpr.Value) > 1e-8) break;
                ed.WriteMessage("\nInvalid array vector.");
            }

            var endOpts = new PromptPointOptions("\nSpecify array end point: ")
            {
                UseBasePoint = true,
                BasePoint = bpr.Value
            };
            var epr = ed.GetPoint(endOpts);
            if (epr.Status != PromptStatus.OK) return;

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Vector3d step = vpr.Value.TransformBy(ucs) - bpr.Value.TransformBy(ucs);
            Vector3d span = epr.Value.TransformBy(ucs) - bpr.Value.TransformBy(ucs);
            double lengthSq = step.DotProduct(step);
            if (lengthSq < 1e-12)
            {
                ed.WriteMessage("\nInvalid array vector.");
                return;
            }
            int qty = (int)(span.DotProduct(step) / lengthSq);
            if (qty == 0)
            {
                ed.WriteMessage("\nINCARRAY: the end point does not reach one spacing.");
                return;
            }
            Vector3d direction = qty < 0 ? -step : step;
            int copies = Math.Abs(qty);

            var placements = new List<KeyValuePair<Vector3d, decimal>>();
            for (int k = 1; k <= copies; k++)
                placements.Add(new KeyValuePair<Vector3d, decimal>(direction * k, _increment * k));
            int made = PlaceCopies(ed, db, psr, placements);
            ed.WriteMessage("\nINCARRAY: placed " + made + " object" + (made == 1 ? "" : "s") + ".");
        }

        /// <summary>
        /// Copy and paste with an incrementing number: each pick copies the selection to a new
        /// point and adds the increment once more to every number in the copied annotation.
        /// </summary>
        [CommandMethod("INCCOPY")]
        public void IncCopy()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!AskIncrement(ed)) return;

            var filter = new SelectionFilter(new[]
            {
                new TypedValue(-4, "<NOT"),
                new TypedValue(0, "VIEWPORT"),
                new TypedValue(-4, "NOT>")
            });
            var psr = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nSelect objects to copy: " }, filter);
            if (psr.Status != PromptStatus.OK) return;

            var bpr = ed.GetPoint("\nSpecify base point: ");
            if (bpr.Status != PromptStatus.OK) return;

            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d basePt = bpr.Value.TransformBy(ucs);
            var placements = new List<KeyValuePair<Vector3d, decimal>>();
            while (true)
            {
                var ppr = ed.GetPoint(new PromptPointOptions("\nSpecify paste point <done>: ")
                {
                    UseBasePoint = true,
                    BasePoint = bpr.Value,
                    AllowNone = true
                });
                if (ppr.Status != PromptStatus.OK) break;
                var disp = ppr.Value.TransformBy(ucs) - basePt;
                placements.Add(new KeyValuePair<Vector3d, decimal>(disp, _increment * (placements.Count + 1)));
                // Place each copy as it is picked so the next prompt shows the result.
                int one = PlaceCopies(ed, db, psr, new List<KeyValuePair<Vector3d, decimal>> { placements[placements.Count - 1] });
                if (one == 0) return;
            }
            ed.WriteMessage("\nINCCOPY: placed " + placements.Count + " cop" + (placements.Count == 1 ? "y" : "ies") + ".");
        }

        private static int PlaceCopies(Editor ed, Database db, PromptSelectionResult psr, List<KeyValuePair<Vector3d, decimal>> placements)
        {
            int made = 0;
            var total = System.Diagnostics.Stopwatch.StartNew();
            var phase = System.Diagnostics.Stopwatch.StartNew();
            long captureMs = 0, copyMs = 0, commitMs = 0; int objects = 0;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var sources = new List<Source>();
                var byOwner = new Dictionary<ObjectId, ObjectIdCollection>();
                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null || ent is Viewport) continue;
                    var layer = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                    if (layer.IsLocked)
                    {
                        ed.WriteMessage("\nSkipped an object on locked layer " + layer.Name + ".");
                        continue;
                    }
                    sources.Add(Capture(tr, ent));
                    if (!byOwner.TryGetValue(ent.OwnerId, out var col))
                    {
                        col = new ObjectIdCollection();
                        byOwner[ent.OwnerId] = col;
                    }
                    col.Add(ent.ObjectId);
                }
                if (sources.Count == 0)
                {
                    ed.WriteMessage("\nNothing to copy.");
                    return 0;
                }

                objects = sources.Count;
                captureMs = phase.ElapsedMilliseconds; phase.Restart();
                var lookup = new Dictionary<ObjectId, Source>();
                foreach (var src in sources) lookup[src.Id] = src;

                foreach (var placement in placements)
                {
                    foreach (var owner in byOwner)
                    {
                        using (var map = new IdMapping())
                        {
                            db.DeepCloneObjects(owner.Value, owner.Key, map, false);
                            foreach (IdPair pair in map)
                            {
                                if (!pair.IsPrimary || !lookup.TryGetValue(pair.Key, out var src)) continue;
                                var clone = (Entity)tr.GetObject(pair.Value, OpenMode.ForWrite);
                                clone.TransformBy(Matrix3d.Displacement(placement.Key));
                                Apply(tr, clone, src, placement.Value);
                                made++;
                            }
                        }
                    }
                }
                copyMs = phase.ElapsedMilliseconds; phase.Restart();
                tr.Commit();
                commitMs = phase.ElapsedMilliseconds;
            }
            // One line so a real drawing shows where the time goes before the cloning is changed (setting CopyTiming = 0 hides it).
            if (made > 0 && Settings.GetInt("CopyTiming", 1) != 0)
                ed.WriteMessage("\nTiming: " + objects + " object(s) x " + placements.Count + " cop" + (placements.Count == 1 ? "y" : "ies") + " = " + made + " new, " + total.ElapsedMilliseconds
                    + " ms (read " + captureMs + ", copy " + copyMs + ", commit " + commitMs + "; "
                    + (made > 0 ? (copyMs / (double)made).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "0") + " ms per new object).");
            return made;
        }

        private static bool AskIncrement(Editor ed)
        {
            var opt = new PromptStringOptions("\nSpecify increment <" + _incrementText + ">: ")
            {
                AllowSpaces = false,
                DefaultValue = _incrementText,
                UseDefaultValue = true
            };
            var res = ed.GetString(opt);
            if (res.Status != PromptStatus.OK) return false;
            string typed = (res.StringResult ?? "").Trim();
            if (typed.Length == 0) return true;
            if (!decimal.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
            {
                ed.WriteMessage("\nEnter a number.");
                return false;
            }
            _increment = value;
            _incrementText = typed;
            return true;
        }

        private static Source Capture(Transaction tr, Entity ent)
        {
            var src = new Source { Id = ent.ObjectId };
            if (ent is BlockReference br && br.AttributeCollection.Count > 0)
            {
                foreach (ObjectId id in br.AttributeCollection)
                {
                    var att = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                    src.Fields.Add(NumberIncrement.Tokenize(att.TextString));
                }
                return src;
            }
            if (ent is DBText text)
                src.Fields.Add(NumberIncrement.Tokenize(text.TextString));
            else if (ent is MText mtext)
                src.Fields.Add(NumberIncrement.Tokenize(mtext.Contents));
            else if (ent is MLeader leader && leader.MText != null)
                src.Fields.Add(NumberIncrement.Tokenize(leader.MText.Contents));
            else if (ent is Dimension dim)
                src.Fields.Add(NumberIncrement.Tokenize(dim.DimensionText ?? ""));
            else if (ent is AttributeDefinition ad)
            {
                src.Fields.Add(NumberIncrement.Tokenize(ad.Tag));
                src.Fields.Add(NumberIncrement.Tokenize(ad.Prompt));
                src.Fields.Add(NumberIncrement.Tokenize(ad.TextString));
            }
            return src;
        }

        private static void Apply(Transaction tr, Entity ent, Source src, decimal delta)
        {
            if (src.Fields.Count == 0) return;
            if (ent is BlockReference br && br.AttributeCollection.Count > 0)
            {
                int i = 0;
                foreach (ObjectId id in br.AttributeCollection)
                {
                    if (i >= src.Fields.Count) break;
                    var att = (AttributeReference)tr.GetObject(id, OpenMode.ForWrite);
                    att.TextString = NumberIncrement.Render(_incrementText, src.Fields[i], delta);
                    i++;
                }
                return;
            }
            if (ent is DBText text && src.Fields.Count > 0)
                text.TextString = NumberIncrement.Render(_incrementText, src.Fields[0], delta);
            else if (ent is MText mtext && src.Fields.Count > 0)
                mtext.Contents = NumberIncrement.Render(_incrementText, src.Fields[0], delta);
            else if (ent is MLeader leader && src.Fields.Count > 0 && leader.MText != null)
            {
                var mt = leader.MText;
                mt.Contents = NumberIncrement.Render(_incrementText, src.Fields[0], delta);
                leader.MText = mt;
            }
            else if (ent is Dimension dim && src.Fields.Count > 0)
                dim.DimensionText = NumberIncrement.Render(_incrementText, src.Fields[0], delta);
            else if (ent is AttributeDefinition ad && src.Fields.Count >= 3)
            {
                ad.Tag = NumberIncrement.Render(_incrementText, src.Fields[0], delta);
                ad.Prompt = NumberIncrement.Render(_incrementText, src.Fields[1], delta);
                ad.TextString = NumberIncrement.Render(_incrementText, src.Fields[2], delta);
            }
        }

        private sealed class Source
        {
            public ObjectId Id;
            public readonly List<List<NumberIncrement.Token>> Fields = new List<List<NumberIncrement.Token>>();
        }

    }
}
