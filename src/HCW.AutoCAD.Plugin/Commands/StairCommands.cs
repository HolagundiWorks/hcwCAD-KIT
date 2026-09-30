using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using HCW.AutoCAD.Plugin.Logic;

namespace HCW.AutoCAD.Plugin.Commands
{
    /// <summary>
    /// AECSTAIR: a parametric RCC staircase. One set of inputs makes the plan and the section together, with every dimension,
    /// count and level derived from them, and the inputs are kept in the drawing so AECSTAIREDIT can change one number and
    /// redraw both. Types: single flight, dog-legged, U (with an open or closed well) and L.
    ///
    /// Inputs are in the drawing's own units: millimetres in a metric drawing, inches in a feet or inches drawing
    /// (StairInputUnit in the settings can say mm, cm, m, in or ft). The calculation is always in real millimetres, and the
    /// geometry is scaled to whatever the drawing's units are. The checks (rise, going, 2R+G ...) use limits from the settings;
    /// the tool reports them and never decides for you that a stair is acceptable.
    /// </summary>
    public class StairCommands
    {
        private const string AppName = "HCW_STAIR";
        private const string StoreDictionary = "HCW_STAIR";

        private static StairSpec _last = new StairSpec();

        // ------------------------------------------------------------------ units

        private class Units
        {
            public bool Imperial;            // feet and inches drawing
            public string Name = "mm";
            public double MmPer = 1.0;       // real millimetres in one input unit
            public double PerMm = 0.001;     // drawing units in one real millimetre

            public string Fmt(double mm) => (mm / MmPer).ToString("0.###", CultureInfo.InvariantCulture) + " " + Name;
        }

        private static Units GetUnits()
        {
            var db = Util.Db;
            var insunits = db.Insunits;
            bool imperial = insunits == UnitsValue.Feet || insunits == UnitsValue.Inches;
            string wanted = Settings.Get("StairInputUnit", "Auto");
            string name = string.Equals(wanted, "Auto", StringComparison.OrdinalIgnoreCase) ? (imperial ? "in" : "mm") : wanted.ToLowerInvariant();
            double per = StairFormat.MmPer(name);
            if (per <= 0) { name = imperial ? "in" : "mm"; per = StairFormat.MmPer(name); }
            return new Units { Imperial = imperial, Name = name, MmPer = per, PerMm = Util.MmToDrawingUnits(1.0) };
        }

        // ------------------------------------------------------------------ commands

        [CommandMethod("AECSTAIR")]
        public void AecStair()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            var units = GetUnits();

            var spec = Copy(_last);
            if (!AskAndCheck(ed, spec, units, false)) return;

            // where to draw them
            var planPt = ed.GetPoint("\nPlan: pick the first riser, on the right-hand side looking up the stair: ");
            if (planPt.Status != PromptStatus.OK) return;
            var angle = ed.GetAngle(new PromptAngleOptions("\nDirection of the first flight (up): ")
                { UseBasePoint = true, BasePoint = planPt.Value, UseDashedLine = true, DefaultValue = 0, UseDefaultValue = true });
            if (angle.Status != PromptStatus.OK) return;
            var secPt = ed.GetPoint("\nSection: pick the foot of the first riser: ");
            if (secPt.Status != PromptStatus.OK) return;

            spec.PlanX = planPt.Value.X; spec.PlanY = planPt.Value.Y;
            spec.PlanAngleDegrees = angle.Value * 180.0 / Math.PI;
            spec.SectionX = secPt.Value.X; spec.SectionY = secPt.Value.Y;

            string id;
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                id = NextId(tr, db);
                Generate(tr, db, spec, id, units);
                DrawingStore.Write(tr, db, StoreDictionary, id, spec.ToLines());
                tr.Commit();
            }
            _last = Copy(spec);
            ed.WriteMessage("\nAECSTAIR: " + id + " drawn (plan and section). Use AECSTAIREDIT to change it.");
        }

        [CommandMethod("AECSTAIREDIT")]
        public void AecStairEdit()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            var units = GetUnits();

            var pick = ed.GetEntity("\nSelect any part of the staircase to edit: ");
            if (pick.Status != PromptStatus.OK) return;
            string id = null;
            StairSpec spec;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(pick.ObjectId, OpenMode.ForRead) as Entity;
                var tag = ent == null ? null : StairTag(ent);
                if (tag != null) id = tag[0];
                spec = id == null ? null : StairSpec.FromLines(DrawingStore.Read(tr, db, StoreDictionary, id));
                tr.Commit();
            }
            if (id == null || spec == null)
            {
                ed.WriteMessage("\nThat is not part of a staircase made by AECSTAIR.");
                return;
            }

            ed.WriteMessage("\nEditing " + id + ".");
            if (!AskAndCheck(ed, spec, units, true)) return;

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EraseStair(tr, db, id);
                Generate(tr, db, spec, id, units);
                DrawingStore.Write(tr, db, StoreDictionary, id, spec.ToLines());
                tr.Commit();
            }
            _last = Copy(spec);
            ed.WriteMessage("\nAECSTAIREDIT: " + id + " redrawn in place.");
        }

        // ------------------------------------------------------------------ inputs and the report

        /// <summary>Asks for every input, shows the calculation, and lets the user change it or generate. False on cancel.</summary>
        private static bool AskAndCheck(Editor ed, StairSpec spec, Units u, bool editing)
        {
            while (true)
            {
                if (!Ask(ed, spec, u, editing)) return false;
                var calc = StairCalc.Calculate(spec, Limits());
                Report(ed, spec, calc, u);
                if (!calc.CanDraw)
                {
                    ed.WriteMessage("\nThis staircase cannot be drawn. Change the inputs.");
                    editing = true;
                    continue;
                }
                var opt = new PromptKeywordOptions("\n[Generate/Change] <Generate>: ") { AllowNone = true };
                opt.Keywords.Add("Generate");
                opt.Keywords.Add("Change");
                var res = ed.GetKeywords(opt);
                if (res.Status == PromptStatus.Cancel) return false;
                if (res.Status == PromptStatus.OK && res.StringResult == "Change") { editing = true; continue; }
                return true;
            }
        }

        private static StairLimits Limits() => new StairLimits
        {
            MaxRise = Settings.GetDouble("StairMaxRiseMm", 190),
            MinRise = Settings.GetDouble("StairMinRiseMm", 100),
            MinGoing = Settings.GetDouble("StairMinGoingMm", 250),
            Min2RG = Settings.GetDouble("Stair2RGMinMm", 550),
            Max2RG = Settings.GetDouble("Stair2RGMaxMm", 700)
        };

        private static bool Ask(Editor ed, StairSpec s, Units u, bool editing)
        {
            // type
            string current = s.Kind == StairKind.DogLeg ? "Dog" : s.Kind.ToString();
            var kindOpt = new PromptKeywordOptions("\nStaircase type [Single/Dog/U/L] <" + current + ">: ") { AllowNone = true };
            foreach (var k in new[] { "Single", "Dog", "U", "L" }) kindOpt.Keywords.Add(k);
            var kind = ed.GetKeywords(kindOpt);
            if (kind.Status == PromptStatus.OK) s.Kind = kind.StringResult == "Dog" ? StairKind.DogLeg : (StairKind)Enum.Parse(typeof(StairKind), kind.StringResult);
            else if (kind.Status != PromptStatus.None) return false;

            double v;
            if (!Number(ed, "Stair width", s.Width, u, out v)) return false; s.Width = v;
            if (!Number(ed, "FFL to FFL height", s.FloorHeight, u, out v)) return false; s.FloorHeight = v;

            // risers: automatic unless the user has typed one before
            double preferred = Settings.GetDouble("StairPreferredRiseMm", 165);
            int auto = StairSpec.AutoRisers(s.FloorHeight, preferred, s.TwoFlights);
            int total = editing ? s.TotalRisers : auto;
            var risers = ed.GetInteger(new PromptIntegerOptions("\nTotal risers (auto " + auto + ") <" + total + ">: ")
                { AllowNegative = false, AllowZero = false, LowerLimit = 2, UpperLimit = 200, DefaultValue = total, UseDefaultValue = true });
            if (risers.Status != PromptStatus.OK) return false;
            s.TotalRisers = risers.Value;

            if (s.TwoFlights)
            {
                int first = editing ? Math.Min(s.FirstFlightRisers, s.TotalRisers - 2) : StairSpec.AutoFirstFlight(s.TotalRisers);
                first = Math.Max(2, first);
                var f = ed.GetInteger(new PromptIntegerOptions("\nRisers in the first flight <" + first + ">: ")
                    { AllowNegative = false, AllowZero = false, LowerLimit = 1, UpperLimit = s.TotalRisers - 1, DefaultValue = first, UseDefaultValue = true });
                if (f.Status != PromptStatus.OK) return false;
                s.FirstFlightRisers = f.Value;
            }

            if (!Number(ed, "Tread / going", s.Going, u, out v)) return false; s.Going = v;
            if (s.TwoFlights)
            {
                if (!Number(ed, "Landing length", s.LandingLength, u, out v)) return false; s.LandingLength = v;
            }
            if (!Number(ed, "Waist slab thickness", s.WaistThickness, u, out v)) return false; s.WaistThickness = v;
            if (!Number(ed, "Landing / floor slab thickness", s.LandingThickness, u, out v)) return false; s.LandingThickness = v;
            if (!Number(ed, "Starting level (FFL)", s.StartLevel, u, out v, true)) return false; s.StartLevel = v;
            if (!Number(ed, "Nosing (0 for none)", s.Nosing, u, out v, true)) return false; s.Nosing = Math.Max(0, v);

            if (s.Kind == StairKind.U)
            {
                if (!Number(ed, "Well width between the flights", s.WellWidth, u, out v)) return false; s.WellWidth = v;
                var well = new PromptKeywordOptions("\nWell [Open/Closed] <" + (s.OpenWell ? "Open" : "Closed") + ">: ") { AllowNone = true };
                well.Keywords.Add("Open");
                well.Keywords.Add("Closed");
                var w = ed.GetKeywords(well);
                if (w.Status == PromptStatus.OK) s.OpenWell = w.StringResult == "Open";
                else if (w.Status != PromptStatus.None) return false;
            }
            if (s.TwoFlights)
            {
                var turn = new PromptKeywordOptions("\nSecond flight turns [Left/Right] <" + (s.TurnLeft ? "Left" : "Right") + ">: ") { AllowNone = true };
                turn.Keywords.Add("Left");
                turn.Keywords.Add("Right");
                var t = ed.GetKeywords(turn);
                if (t.Status == PromptStatus.OK) s.TurnLeft = t.StringResult == "Left";
                else if (t.Status != PromptStatus.None) return false;
            }

            var scale = ed.GetDouble(new PromptDoubleOptions("\nPlot scale 1: <" + s.PlotScale + ">: ")
                { AllowNegative = false, AllowZero = false, DefaultValue = s.PlotScale, UseDefaultValue = true });
            if (scale.Status != PromptStatus.OK) return false;
            s.PlotScale = scale.Value;
            return true;
        }

        /// <summary>A length in the input unit, kept as real millimetres.</summary>
        private static bool Number(Editor ed, string label, double currentMm, Units u, out double mm, bool allowZeroOrNegative = false)
        {
            mm = currentMm;
            var opt = new PromptDoubleOptions("\n" + label + " (" + u.Name + ") <" + (currentMm / u.MmPer).ToString("0.###", CultureInfo.InvariantCulture) + ">: ")
            {
                AllowNegative = allowZeroOrNegative, AllowZero = allowZeroOrNegative,
                DefaultValue = currentMm / u.MmPer, UseDefaultValue = true
            };
            var res = ed.GetDouble(opt);
            if (res.Status != PromptStatus.OK) return false;
            mm = res.Value * u.MmPer;
            return true;
        }

        private static void Report(Editor ed, StairSpec s, StairCalc c, Units u)
        {
            Func<string, string, string> row = (name, value) => "\n  " + name.PadRight(22) + value;
            ed.WriteMessage("\n\nSTAIRCASE CALCULATION (" + s.Kind + ")");
            ed.WriteMessage(row("FFL to FFL", u.Fmt(s.FloorHeight)));
            ed.WriteMessage(row("Total risers", s.TotalRisers.ToString()));
            ed.WriteMessage(row("Rise", u.Fmt(c.Rise)));
            ed.WriteMessage(row("Tread (going)", u.Fmt(s.Going)));
            ed.WriteMessage(row("2R + G", u.Fmt(c.TwoRPlusG)));
            ed.WriteMessage(row("Pitch", c.Pitch.ToString("0.0", CultureInfo.InvariantCulture) + " deg"));
            for (int i = 0; i < c.FlightRisers.Length; i++)
                ed.WriteMessage(row("Flight " + (i + 1), c.FlightRisers[i] + " risers, " + c.FlightTreads[i] + " treads, length " + u.Fmt(c.FlightLengths[i])));
            if (s.TwoFlights)
                ed.WriteMessage(row("Intermediate level", StairFormat.Level(c.LandingLevel, u.Imperial)));
            ed.WriteMessage(row("Levels", StairFormat.Level(c.BottomLevel, u.Imperial) + " to " + StairFormat.Level(c.TopLevel, u.Imperial)));
            ed.WriteMessage("\n\nCHECKS");
            foreach (var check in c.Checks)
                ed.WriteMessage(row(check.Name, check.Value.PadRight(28) + check.Status));
            ed.WriteMessage("\n  The limits come from the settings file; apply your own code criteria there.");
        }

        // ------------------------------------------------------------------ drawing

        private static void Generate(Transaction tr, Database db, StairSpec spec, string id, Units u)
        {
            var calc = StairCalc.Calculate(spec, Limits());
            var opt = new StairOptions
            {
                TextHeight = Settings.GetDouble("StairTextMm", 2.5) * spec.PlotScale,
                DimOffset = Settings.GetDouble("StairDimOffsetMm", 10) * spec.PlotScale,
                Imperial = u.Imperial
            };
            var plan = StairGeometry.Plan(spec, calc, opt);
            var section = StairGeometry.Section(spec, calc, opt);

            var ucs = Util.Ed.CurrentUserCoordinateSystem;
            double k = u.PerMm;
            double a = spec.PlanAngleDegrees * Math.PI / 180.0;
            double ca = Math.Cos(a), sa = Math.Sin(a);
            Func<PlanPoint, Point3d> planPt = p => new Point3d(spec.PlanX + (p.X * ca - p.Y * sa) * k, spec.PlanY + (p.X * sa + p.Y * ca) * k, 0);
            Func<PlanPoint, Point3d> sectionPt = p => new Point3d(spec.SectionX + p.X * k, spec.SectionY + p.Y * k, 0);

            var maker = new Maker(tr, db, id, ucs, k, spec.PlotScale, opt.TextHeight);
            maker.Draw(plan, planPt, a, "PLAN");
            maker.Draw(section, sectionPt, 0, "SECTION");
        }

        /// <summary>Turns the geometry (real millimetres) into entities in the current space, tagged with the stair's ID.</summary>
        private class Maker
        {
            private readonly Transaction _tr;
            private readonly BlockTableRecord _space;
            private readonly string _id;
            private readonly Matrix3d _ucs;
            private readonly double _k, _scale, _textHeight;
            private readonly ObjectId _dimStyle;

            public Maker(Transaction tr, Database db, string id, Matrix3d ucs, double k, double scale, double textHeight)
            {
                _tr = tr; _id = id; _ucs = ucs; _k = k; _scale = scale; _textHeight = textHeight;
                var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!apps.Has(AppName))
                {
                    apps.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = AppName };
                    apps.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                var styles = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                string wanted = Settings.Get("StairDimStyle", "HCW-WORKING");
                _dimStyle = styles.Has(wanted) ? styles[wanted] : db.Dimstyle;
                _space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var role in new[] { "PLAN", "TREAD", "NOSING", "ARROW", "WELL", "TEXT", "SECTION", "LEVEL", "HATCH" })
                    Util.EnsureLayer(tr, db, LayerFor(role), ColorFor(role), "Continuous", role == "SECTION" ? LineWeight.LineWeight035 : LineWeight.LineWeight000);
                Util.EnsureLayer(tr, db, "AN-DIMS", 8);
            }

            private static string LayerFor(string role) => "AECSTAIR-" + (role == "SECTION" ? "RCC" : role);
            private static short ColorFor(string role)
            {
                switch (role)
                {
                    case "TREAD": case "NOSING": case "HATCH": return 8;
                    case "ARROW": case "LEVEL": return 3;
                    case "WELL": return 5;
                    case "PLAN": return 4;
                    default: return 7;
                }
            }

            private void Tag(Entity ent, string part)
            {
                ent.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, _id),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, part));
            }

            private ObjectId Add(Entity ent, string part)
            {
                Tag(ent, part);
                ent.TransformBy(_ucs);
                var id = _space.AppendEntity(ent);
                _tr.AddNewlyCreatedDBObject(ent, true);
                return id;
            }

            public void Draw(GDrawing d, Func<PlanPoint, Point3d> at, double rotation, string part)
            {
                foreach (var poly in d.Polys)
                {
                    var pl = new Polyline { Layer = LayerFor(poly.Layer), Closed = poly.Closed };
                    for (int i = 0; i < poly.Pts.Count; i++)
                    {
                        var p = at(poly.Pts[i]);
                        pl.AddVertexAt(i, new Point2d(p.X, p.Y), 0, 0, 0);
                    }
                    var id = Add(pl, part);
                    if (poly.Hatch) TryHatch(id, part);
                }
                foreach (var t in d.Texts)
                {
                    var p = at(new PlanPoint(t.X, t.Y));
                    var text = new DBText
                    {
                        Height = t.Height * _k, TextString = t.Text, Layer = LayerFor("TEXT"),
                        Rotation = Readable(t.Rotation + rotation)
                    };
                    if (t.Centre)
                    {
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        text.AlignmentPoint = p;
                    }
                    else text.Position = p;
                    Add(text, part);
                }
                foreach (var dim in d.Dims)
                {
                    var a = at(dim.A); var b = at(dim.B); var line = at(dim.Line);
                    var ad = new AlignedDimension(a, b, line, dim.Text, _dimStyle) { Layer = "AN-DIMS" };
                    double h = _textHeight * _k;
                    ad.Dimscale = 1;
                    ad.Dimtxt = h; ad.Dimasz = h; ad.Dimexe = 0.6 * h; ad.Dimexo = 0.6 * h; ad.Dimgap = 0.4 * h;
                    Add(ad, part);
                }
            }

            /// <summary>Text reads left to right or upwards, never upside down.</summary>
            private static double Readable(double rotation)
            {
                double r = rotation % (2 * Math.PI);
                if (r < 0) r += 2 * Math.PI;
                if (r > Math.PI / 2 + 1e-6 && r <= 3 * Math.PI / 2 + 1e-6) r -= Math.PI;
                return r;
            }

            /// <summary>Concrete hatch inside a closed outline. Skipped quietly when the host will not make it.</summary>
            private void TryHatch(ObjectId outline, string part)
            {
                if (Settings.GetInt("StairHatch", 1) == 0) return;
                try
                {
                    var hatch = new Hatch { Layer = LayerFor("HATCH") };
                    Tag(hatch, part);
                    _space.AppendEntity(hatch);
                    _tr.AddNewlyCreatedDBObject(hatch, true);
                    hatch.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                    hatch.PatternScale = 2.0 * _scale * _k / 3.175;
                    hatch.Associative = true;
                    hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { outline });
                    hatch.EvaluateHatch(true);
                }
                catch { }
            }
        }

        // ------------------------------------------------------------------ IDs, tags, erasing

        private static string NextId(Transaction tr, Database db)
        {
            int max = DrawingStore.Keys(tr, db, StoreDictionary).Select(k => ElectricalNumbering.NumberOf(k, "ST")).DefaultIfEmpty(0).Max();
            return ElectricalNumbering.Format("ST", Math.Max(0, max) + 1);
        }

        /// <summary>The stair ID and part (PLAN or SECTION) tagged on an entity, or null.</summary>
        private static string[] StairTag(Entity ent)
        {
            var data = ent.GetXDataForApplication(AppName);
            if (data == null) return null;
            var values = data.AsArray().Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString).Select(v => v.Value as string ?? "").ToArray();
            return values.Length >= 1 ? values : null;
        }

        private static void EraseStair(Transaction tr, Database db, string id)
        {
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId oid in space)
            {
                var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                if (ent == null || ent.IsErased) continue;
                var tag = StairTag(ent);
                if (tag == null || tag[0] != id) continue;
                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        private static StairSpec Copy(StairSpec s) => StairSpec.FromLines(s.ToLines());
    }
}
