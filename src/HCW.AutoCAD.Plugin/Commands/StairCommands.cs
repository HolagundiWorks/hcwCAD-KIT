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

            var floors = ed.GetInteger(new PromptIntegerOptions("\nNumber of floors, the same stair on each <" + _floorCount + ">: ")
                { AllowNegative = false, AllowZero = false, LowerLimit = 1, UpperLimit = 30, DefaultValue = _floorCount, UseDefaultValue = true });
            if (floors.Status != PromptStatus.OK) return;
            _floorCount = floors.Value;

            // Each further floor's plan goes above the last (in the drawing's Y), its section stacks on the one below at the floor height.
            double planStep = 0;
            if (_floorCount > 1)
            {
                var calc0 = StairCalc.Calculate(spec, Limits());
                var opt0 = new StairOptions { TextHeight = Settings.GetDouble("StairTextMm", 2.5) * spec.PlotScale, DimOffset = Settings.GetDouble("StairDimOffsetMm", 10) * spec.PlotScale };
                var box0 = StairGeometry.Plan(spec, calc0, opt0).Extents();
                planStep = (Math.Abs(Math.Cos(spec.PlanAngleDegrees * Math.PI / 180)) * (box0.MaxY - box0.MinY)
                    + Math.Abs(Math.Sin(spec.PlanAngleDegrees * Math.PI / 180)) * (box0.MaxX - box0.MinX) + 8 * opt0.DimOffset) * units.PerMm;
            }

            var ids = new List<string>();
            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                for (int k = 0; k < _floorCount; k++)
                {
                    var floor = Copy(spec);
                    floor.StartLevel = spec.StartLevel + k * spec.FloorHeight;
                    floor.PlanY = spec.PlanY + k * planStep;
                    floor.SectionY = spec.SectionY + k * spec.FloorHeight * units.PerMm;
                    string id = NextId(tr, db);
                    Generate(tr, db, floor, id, units);
                    DrawingStore.Write(tr, db, StoreDictionary, id, floor.ToLines());
                    SaveQuantities(ed, tr, db, id, floor);
                    ids.Add(id);
                }
                tr.Commit();
            }
            _last = Copy(spec);
            ed.WriteMessage("\nAECSTAIR: " + string.Join(", ", ids) + (ids.Count > 1 ? " drawn, one per floor, levels rising by " + (spec.FloorHeight / units.MmPer).ToString("0.###", CultureInfo.InvariantCulture) + " " + units.Name + "." : " drawn (plan and section).")
                + " Use AECSTAIREDIT to change one.");
        }

        private static int _floorCount = 1;

        /// <summary>
        /// AECSTAIRQTY works out the quantities and bar estimate of a staircase from its inputs alone, without drawing it: for a stair
        /// made some other way. It asks the same questions as AECSTAIR, then a name, and saves the take-offs "Stair NAME" and "Stair NAME bars".
        /// </summary>
        [CommandMethod("AECSTAIRQTY")]
        public void AecStairQuantities()
        {
            var ed = Util.Ed;
            var db = Util.Db;
            if (!MeasureCommands.Prepare()) return;
            var units = GetUnits();
            var spec = Copy(_last);
            if (!AskAndCheck(ed, spec, units, false)) return;

            var nr = ed.GetString(new PromptStringOptions("\nName for these quantities <Manual>: ") { AllowSpaces = true, DefaultValue = "Manual", UseDefaultValue = true });
            if (nr.Status != PromptStatus.OK) return;
            string name = nr.StringResult.Trim().Replace("|", "").Length > 0 ? nr.StringResult.Trim().Replace("|", "") : "Manual";

            using (Util.Doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                SaveQuantities(ed, tr, db, name, spec);
                tr.Commit();
            }
            _last = Copy(spec);
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
                SaveQuantities(ed, tr, db, id, spec);
                tr.Commit();
            }
            _last = Copy(spec);
            ed.WriteMessage("\nAECSTAIREDIT: " + id + " redrawn in place.");
        }

        /// <summary>
        /// Works out the concrete, shuttering and finish quantities of the stair, lists them on the command line and keeps them as the
        /// take-off "Stair ST-01", so MEXPORT writes them with the other take-offs. A repeat run (AECSTAIREDIT) replaces them.
        /// </summary>
        private static void SaveQuantities(Editor ed, Transaction tr, Database db, string id, StairSpec spec)
        {
            var calc = StairCalc.Calculate(spec, Limits());
            if (!calc.CanDraw) return;
            var q = StairQuantities.Compute(spec, calc, Settings.GetDouble("StairLandingWallEdgeMm", 0));
            var rows = q.Rows();
            MeasureBook.SaveTakeoff(tr, db, "Stair " + id, StairQuantities.Headers, rows);
            ed.WriteMessage("\n\nQUANTITIES (" + id + ")");
            foreach (var r in rows)
                if (r[0] == r[0].ToUpperInvariant() || r[0] == "Skirting")
                    ed.WriteMessage("\n  " + Util.Pad(r[0], 22) + r[1] + " " + r[2]);
            ed.WriteMessage("\n  Saved as the take-off \"Stair " + id + "\"; MEXPORT writes it with the others.");

            // reinforcement estimate and bar schedule
            var bars = StairRebar.Compute(spec, calc, RebarFromSettings());
            MeasureBook.SaveTakeoff(tr, db, "Stair " + id + " bars", StairRebar.Headers, StairRebar.Rows(bars, q.Concrete));
            double kg = StairRebar.TotalWeight(bars);
            ed.WriteMessage("\n  Reinforcement estimate " + kg.ToString("0.0", CultureInfo.InvariantCulture) + " kg ("
                + (q.Concrete > 0 ? (kg / q.Concrete).ToString("0", CultureInfo.InvariantCulture) : "0") + " kg/m3), saved as \"Stair " + id + " bars\".");
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
            if (s.Kind == StairKind.L || s.Kind == StairKind.U)
            {
                var wo = new PromptKeywordOptions("\nLanding or winders [Landing/Winders] <" + (s.Winders ? "Winders" : "Landing") + ">: ") { AllowNone = true };
                wo.Keywords.Add("Landing");
                wo.Keywords.Add("Winders");
                var wr = ed.GetKeywords(wo);
                if (wr.Status == PromptStatus.OK) s.Winders = wr.StringResult == "Winders";
                else if (wr.Status != PromptStatus.None) return false;
            }
            else s.Winders = false;
            if (s.TwoFlights && !s.HasWinders)
            {
                if (!Number(ed, "Landing length", s.LandingLength, u, out v)) return false; s.LandingLength = v;
            }
            if ((s.Kind == StairKind.DogLeg || s.Kind == StairKind.U) && !s.HasWinders)
            {
                if (!Number(ed, "Landing width (0 = the width of both flights)", s.LandingWidthOverride, u, out v, true)) return false;
                s.LandingWidthOverride = Math.Max(0, v);
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
            if (s.Kind == StairKind.L)
            {
                var cut = new PromptKeywordOptions("\nSection [Developed/Cut] <" + (s.CutSection ? "Cut" : "Developed") + ">: ") { AllowNone = true };
                cut.Keywords.Add("Developed");
                cut.Keywords.Add("Cut");
                var cr = ed.GetKeywords(cut);
                if (cr.Status == PromptStatus.OK) s.CutSection = cr.StringResult == "Cut";
                else if (cr.Status != PromptStatus.None) return false;
            }
            else s.CutSection = false;
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
            if (s.HasWinders) ed.WriteMessage(row("Winders", s.WinderTreads + " treads, going " + u.Fmt(c.WinderGoing) + " on the walkline"));
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
                Imperial = u.Imperial,
                ShowHeadroom = Settings.GetInt("StairHeadroomLine", 1) != 0,
                Headroom = Settings.GetDouble("StairHeadroomMm", 2000),
                ShowRailing = Settings.GetInt("StairRailing", 1) != 0,
                HandrailHeight = Settings.GetDouble("StairHandrailMm", 900),
                PostSize = Settings.GetDouble("StairPostMm", 50),
                BalustersPerTread = Math.Max(1, Settings.GetInt("StairBalustersPerTread", 2)),
                ShowRebar = Settings.GetInt("StairRebarDrawing", 0) != 0,
                Rebar = RebarFromSettings(),
            };
            var plan = StairGeometry.Plan(spec, calc, opt);
            var section = StairGeometry.Section(spec, calc, opt);

            var ucs = Util.Ed.CurrentUserCoordinateSystem;
            double a = spec.PlanAngleDegrees * Math.PI / 180.0;
            var drawer = MakeDrawer(tr, db, id, ucs, spec.PlotScale, opt.TextHeight);
            drawer.Part = "PLAN";
            drawer.Draw(plan, new Point3d(spec.PlanX, spec.PlanY, 0), a);
            drawer.Part = "SECTION";
            drawer.Draw(section, new Point3d(spec.SectionX, spec.SectionY, 0), 0);
        }

        private static RebarOptions RebarFromSettings() => new RebarOptions
        {
            MainDia = Settings.GetDouble("StairMainBarDia", 12), MainSpacing = Settings.GetDouble("StairMainBarSpacing", 150),
            DistDia = Settings.GetDouble("StairDistBarDia", 8), DistSpacing = Settings.GetDouble("StairDistBarSpacing", 200),
            Cover = Settings.GetDouble("StairCover", 25), AnchorageDiameters = Settings.GetDouble("StairAnchorageDia", 40),
            HookDiameters = Settings.GetDouble("StairHookDia", 0), CrankAlternate = Settings.GetInt("StairCrank", 0) != 0,
            TopDia = Settings.GetDouble("StairTopBarDia", 0), TopSpacing = Settings.GetDouble("StairTopBarSpacing", 200), TopSpanShare = Settings.GetDouble("StairTopBarSpanPct", 30) / 100.0,
        };

        private static readonly string[] Roles = { "PLAN", "TREAD", "NOSING", "ARROW", "WELL", "TEXT", "SECTION", "LEVEL", "HATCH", "HEADROOM", "RAIL", "REBAR", "BEYOND" };

        private static short ColorFor(string role)
        {
            switch (role)
            {
                case "TREAD": case "NOSING": case "HATCH": case "BEYOND": return 8;
                case "ARROW": case "LEVEL": return 3;
                case "WELL": return 5;
                case "PLAN": return 4;
                case "HEADROOM": return 1;
                case "RAIL": return 6;
                case "REBAR": return 30;
                default: return 7;
            }
        }

        /// <summary>The shared drawer, set up for stairs: AECSTAIR-* layers by role, every object tagged with the stair's ID and part, the stair's dimension style.</summary>
        private static GDrawer MakeDrawer(Transaction tr, Database db, string id, Matrix3d ucs, double scale, double textHeight)
        {
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
            var roles = Roles.ToDictionary(r => r, r => new GDrawer.RoleLayer
            {
                Layer = "AECSTAIR-" + (r == "SECTION" ? "RCC" : r), Color = ColorFor(r),
                Weight = r == "SECTION" ? LineWeight.LineWeight035 : LineWeight.LineWeight000,
            });
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            return new GDrawer(tr, db, space, roles, textHeight)
            {
                Ucs = ucs,
                HatchScale = 2.0 * scale,
                Hatching = Settings.GetInt("StairHatch", 1) != 0,
                DimStyle = styles.Has(wanted) ? styles[wanted] : db.Dimstyle,
                Tag = (ent, part) => ent.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, id),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, part)),
            };
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
