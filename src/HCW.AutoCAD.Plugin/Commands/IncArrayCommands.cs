using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
                tr.Commit();
            }
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
                    src.Fields.Add(Tokenize(att.TextString));
                }
                return src;
            }
            if (ent is DBText text)
                src.Fields.Add(Tokenize(text.TextString));
            else if (ent is MText mtext)
                src.Fields.Add(Tokenize(mtext.Contents));
            else if (ent is MLeader leader && leader.MText != null)
                src.Fields.Add(Tokenize(leader.MText.Contents));
            else if (ent is Dimension dim)
                src.Fields.Add(Tokenize(dim.DimensionText ?? ""));
            else if (ent is AttributeDefinition ad)
            {
                src.Fields.Add(Tokenize(ad.Tag));
                src.Fields.Add(Tokenize(ad.Prompt));
                src.Fields.Add(Tokenize(ad.TextString));
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
                    att.TextString = Render(src.Fields[i], delta);
                    i++;
                }
                return;
            }
            if (ent is DBText text && src.Fields.Count > 0)
                text.TextString = Render(src.Fields[0], delta);
            else if (ent is MText mtext && src.Fields.Count > 0)
                mtext.Contents = Render(src.Fields[0], delta);
            else if (ent is MLeader leader && src.Fields.Count > 0 && leader.MText != null)
            {
                var mt = leader.MText;
                mt.Contents = Render(src.Fields[0], delta);
                leader.MText = mt;
            }
            else if (ent is Dimension dim && src.Fields.Count > 0)
                dim.DimensionText = Render(src.Fields[0], delta);
            else if (ent is AttributeDefinition ad && src.Fields.Count >= 3)
            {
                ad.Tag = Render(src.Fields[0], delta);
                ad.Prompt = Render(src.Fields[1], delta);
                ad.TextString = Render(src.Fields[2], delta);
            }
        }

        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            if (string.IsNullOrEmpty(text))
            {
                tokens.Add(new Token("", false));
                return tokens;
            }
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsDigit(text[i]))
                {
                    int j = i;
                    while (j < text.Length && char.IsDigit(text[j])) j++;
                    if (j < text.Length && text[j] == '.' && j + 1 < text.Length && char.IsDigit(text[j + 1]))
                    {
                        j++;
                        while (j < text.Length && char.IsDigit(text[j])) j++;
                    }
                    tokens.Add(new Token(text.Substring(i, j - i), true));
                    i = j;
                }
                else
                {
                    int j = i + 1;
                    while (j < text.Length && !char.IsDigit(text[j])) j++;
                    tokens.Add(new Token(text.Substring(i, j - i), false));
                    i = j;
                }
            }
            return tokens;
        }

        private static string Render(List<Token> tokens, decimal delta)
        {
            var sb = new StringBuilder();
            foreach (var token in tokens)
                sb.Append(token.IsNumber ? Increment(token.Text, delta) : token.Text);
            return sb.ToString();
        }

        private static string Increment(string original, decimal delta)
        {
            if (!decimal.TryParse(original, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
                return original;
            decimal num = value + delta;
            string body = original.TrimStart('-');
            string incBody = _incrementText.TrimStart('-', '+');
            int dcs = DecimalPlaces(body);
            int dci = DecimalPlaces(incBody);
            int places = Math.Max(dcs, dci);
            string rendered = Math.Abs(num).ToString("F" + places, CultureInfo.InvariantCulture);
            if (body.Length > 0 && body[0] == '0')
            {
                int len = body.Length;
                if (dcs > 0) len = len - dcs + places;
                else if (dci > 0) len = len + dci + 1;
                while (rendered.Length < len) rendered = "0" + rendered;
            }
            return num < 0 ? "-" + rendered : rendered;
        }

        private static int DecimalPlaces(string text)
        {
            int dot = text.IndexOf('.');
            return dot < 0 ? 0 : text.Length - dot - 1;
        }

        private sealed class Source
        {
            public ObjectId Id;
            public readonly List<List<Token>> Fields = new List<List<Token>>();
        }

        private sealed class Token
        {
            public readonly string Text;
            public readonly bool IsNumber;
            public Token(string text, bool isNumber) { Text = text; IsNumber = isNumber; }
        }
    }
}
