using System;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class NameMatchTests
    {
        [Theory]
        [InlineData("FB01-D1", "FB 01 D-1")]
        [InlineData("fb01-d1", "FB01-D1")]
        [InlineData("FB D-01", "FB-D1")]
        [InlineData("BM10-D2", "bm10 d-2")]
        public void SameDeduction(string a, string b)
        {
            Assert.Equal(NameMatch.NormKey(a), NameMatch.NormKey(b));
        }

        [Theory]
        [InlineData("FB01-D1", "FB01-D2")]
        [InlineData("FB01-D1", "FB10-D1")]
        [InlineData("FB01-D1", "HB01-D1")]
        public void DifferentDeduction(string a, string b)
        {
            Assert.NotEqual(NameMatch.NormKey(a), NameMatch.NormKey(b));
        }

        [Fact]
        public void TensAreNotTruncated()
        {
            Assert.Equal("fb10d1", NameMatch.NormKey("FB10-D1"));
            Assert.Equal("fb1d1", NameMatch.NormKey("FB01-D1"));
        }

        [Fact]
        public void NullAndEmpty()
        {
            Assert.Equal("", NameMatch.NormKey(null));
            Assert.Equal("", NameMatch.NormKey(""));
        }
    }

    public class LengthMatchTests
    {
        [Fact]
        public void PicksClosestWithinTolerance()
        {
            // schedule lengths in cm: 90, 100, 120; tolerance 50 mm = 5 cm
            var lengths = new[] { 90, 100, 120 };
            Assert.Equal(1, LengthMatch.Closest(lengths, 100, 5));
            Assert.Equal(1, LengthMatch.Closest(lengths, 104, 5));
            Assert.Equal(1, LengthMatch.Closest(lengths, 96, 5));
        }

        [Fact]
        public void NothingInRange()
        {
            Assert.Equal(-1, LengthMatch.Closest(new[] { 90, 120 }, 106, 5));
        }

        [Fact]
        public void EquallyCloseIsAmbiguous()
        {
            Assert.Equal(-1, LengthMatch.Closest(new[] { 96, 104 }, 100, 5));
        }

        [Fact]
        public void BoundaryIsInclusive()
        {
            Assert.Equal(0, LengthMatch.Closest(new[] { 100 }, 105, 5));
            Assert.Equal(-1, LengthMatch.Closest(new[] { 100 }, 106, 5));
        }

        [Fact]
        public void ImperialTwoInchesIsSixteenEighths()
        {
            Assert.Equal(0, LengthMatch.Closest(new[] { 8 * 36 }, 8 * 36 + 16, 16));
            Assert.Equal(-1, LengthMatch.Closest(new[] { 8 * 36 }, 8 * 36 + 17, 16));
        }
    }

    public class NumberIncrementTests
    {
        [Theory]
        [InlineData("Room 1", 1, "1", "Room 2")]
        [InlineData("Room 09", 1, "1", "Room 10")]
        [InlineData("Room 01", 1, "1", "Room 02")]
        [InlineData("Room 9", 1, "1", "Room 10")]
        [InlineData("A1-B2", 1, "1", "A2-B3")]
        [InlineData("No numbers", 1, "1", "No numbers")]
        [InlineData("Level 3", 2, "2", "Level 5")]
        [InlineData("Level 3", -1, "-1", "Level 2")]
        public void Whole(string text, int delta, string incrementText, string expected)
        {
            Assert.Equal(expected, NumberIncrement.Apply(text, delta, incrementText));
        }

        [Fact]
        public void KeepsLeadingZerosOnlyWhenPresent()
        {
            Assert.Equal("007", NumberIncrement.Apply("006", 1m, "1"));
            Assert.Equal("10", NumberIncrement.Apply("9", 1m, "1"));
        }

        [Fact]
        public void Decimals()
        {
            Assert.Equal("1.5", NumberIncrement.Apply("1.0", 0.5m, "0.5"));
            Assert.Equal("2.50", NumberIncrement.Apply("2.00", 0.5m, "0.5"));
        }

        [Fact]
        public void GoesNegative()
        {
            Assert.Equal("-1", NumberIncrement.Apply("1", -2m, "-2"));
        }

        [Fact]
        public void EmptyText()
        {
            Assert.Equal("", NumberIncrement.Apply("", 1m, "1"));
            Assert.Equal("", NumberIncrement.Apply(null, 1m, "1"));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class IniFileTests
    {
        [Fact]
        public void ReadsValuesAndSkipsComments()
        {
            var ini = IniFile.Parse("# comment\n; another\n\nTextHeight = 0.3\r\nWallNumbering=TopBottom\nbad line\n");
            Assert.Equal(0.3, ini.GetDouble("TextHeight", 1), 6);
            Assert.Equal("TopBottom", ini.Get("wallnumbering", "x"));
            Assert.False(ini.Has("bad line"));
        }

        [Fact]
        public void FallsBackWhenMissingOrInvalid()
        {
            var ini = IniFile.Parse("Tol=abc\nEmpty=\n");
            Assert.Equal(5, ini.GetInt("Tol", 5));
            Assert.Equal("dflt", ini.Get("Empty", "dflt"));
            Assert.Equal(2.5, ini.GetDouble("Nope", 2.5), 6);
            Assert.Equal(1, IniFile.Parse(null).GetInt("A", 1));
        }

        [Fact]
        public void FormatRoundTrips()
        {
            var text = IniFile.Format(new[] { new System.Collections.Generic.KeyValuePair<string, string[]>("A", new[] { "1", "help" }) });
            Assert.Contains("# help", text);
            Assert.Equal(1, IniFile.Parse(text).GetInt("A", 0));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class BillingTests
    {
        private static Billing.TakeoffData Sample(string name, params string[][] rows)
        {
            var t = new Billing.TakeoffData { Name = name, Headers = new[] { "Group", "Net" } };
            t.Rows.AddRange(rows);
            return t;
        }

        [Fact]
        public void QuantityIsLastCellOfGrandTotalOtherwiseTotal()
        {
            var withGrand = Sample("A", new[] { "FB-A", "5.00" }, new[] { "TOTAL", "5.00" }, new[] { "GRAND TOTAL", "12.50" });
            Assert.Equal(12.5, Billing.Quantity(withGrand).Value, 6);
            var totalOnly = Sample("B", new[] { "P-A", "3.00" }, new[] { "TOTAL", "3.00" });
            Assert.Equal(3.0, Billing.Quantity(totalOnly).Value, 6);
        }

        [Fact]
        public void QuantityIsNullWhenMissingOrNotANumber()
        {
            Assert.Null(Billing.Quantity(Sample("A", new[] { "FB-A", "5.00" })));
            Assert.Null(Billing.Quantity(Sample("A", new[] { "TOTAL", "12'-6\"" })));
        }

        [Fact]
        public void BillOnlyListsRatedTakeoffs()
        {
            var a = Sample("WallPaint", new[] { "TOTAL", "100.00" });
            var b = Sample("Areas", new[] { "TOTAL", "20.00" });
            var sheets = Billing.Sheets(new[] { a, b }, new[] { new Billing.RateData { Takeoff = "wallpaint", Unit = "m2", Rate = 25 } });
            Assert.Equal(new[] { "WallPaint", "Areas", "Bill" }, sheets.ConvertAll(s => s.Name).ToArray());
            var bill = sheets[2];
            Assert.Equal(3, bill.Rows.Count); // header, one item, total
            Assert.Equal("C2*D2", bill.Rows[1][4].Formula);
            Assert.Equal("SUM(E2:E2)", bill.Rows[2][4].Formula);
        }

        [Fact]
        public void NoBillSheetWithoutRates()
        {
            var sheets = Billing.Sheets(new[] { Sample("A", new[] { "TOTAL", "1.00" }) }, new Billing.RateData[0]);
            Assert.Single(sheets);
        }
    }

    public class XlsxWriterTests
    {
        [Theory]
        [InlineData(0, "A")]
        [InlineData(25, "Z")]
        [InlineData(26, "AA")]
        [InlineData(701, "ZZ")]
        [InlineData(702, "AAA")]
        public void ColumnNames(int index, string expected)
        {
            Assert.Equal(expected, XlsxWriter.ColumnName(index));
        }

        [Fact]
        public void SheetNamesAreCleanedAndUnique()
        {
            var names = XlsxWriter.UniqueNames(new[] { "A/B", "A-B", "", new string('x', 40), new string('x', 40) });
            Assert.Equal("A-B", names[0]);
            Assert.Equal("A-B-2", names[1]);
            Assert.Equal("Sheet", names[2]);
            Assert.Equal(31, names[3].Length);
            Assert.Equal(31, names[4].Length);
            Assert.NotEqual(names[3], names[4]);
        }

        [Fact]
        public void WritesAWorkbookThatCanBeReadBack()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hcw-test-" + System.Guid.NewGuid() + ".xlsx");
            try
            {
                var sheet = new XlsxSheet { Name = "Take-off" };
                sheet.Rows.Add(new System.Collections.Generic.List<XlsxCell> { XlsxCell.Str("Item", true), XlsxCell.Str("Qty & <more>") });
                sheet.Rows.Add(new System.Collections.Generic.List<XlsxCell> { XlsxCell.Auto("1.25"), XlsxCell.Calc("A2*2") });
                XlsxWriter.Write(path, new[] { sheet });
                using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
                {
                    Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
                    var entry = zip.GetEntry("xl/worksheets/sheet1.xml");
                    Assert.NotNull(entry);
                    string xml = new System.IO.StreamReader(entry.Open()).ReadToEnd();
                    Assert.Contains("Qty &amp; &lt;more&gt;", xml);
                    Assert.Contains("<v>1.25</v>", xml);
                    Assert.Contains("<f>A2*2</f>", xml);
                    System.Xml.Linq.XDocument.Parse(xml); // well-formed
                }
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }
    }
}

namespace HCW.Logic.Tests
{
    public class FieldsTests
    {
        [Fact]
        public void RoundTripsPipesAndBackslashes()
        {
            string line = Fields.Join("a|b", "c\\d", "", "plain");
            Assert.Equal(new[] { "a|b", "c\\d", "", "plain" }, Fields.Split(line));
        }

        [Fact]
        public void SingleEmptyField()
        {
            Assert.Equal(new[] { "" }, Fields.Split(""));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class DimChainsTests
    {
        [Fact]
        public void MergeSortsAndCollapsesNearPoints()
        {
            var merged = DimChains.Merge(new[] { 5.0, 0.0, 0.004, 3.0, 3.003, 10.0 }, 0.005);
            Assert.Equal(new[] { 0.0, 3.0, 5.0, 10.0 }, merged.ToArray());
        }

        [Fact]
        public void SegmentsAreConsecutivePairsAboveMinimum()
        {
            var segs = DimChains.Segments(new[] { 0.0, 0.2, 3.0, 5.0 }, 0.5);
            Assert.Equal(2, segs.Count);
            Assert.Equal(0.2, segs[0].Key, 6);
            Assert.Equal(3.0, segs[0].Value, 6);
            Assert.Equal(5.0, segs[1].Value, 6);
        }

        [Fact]
        public void OverallIsFirstToLast()
        {
            var overall = DimChains.Overall(new[] { 1.0, 4.0, 9.0 }, 0.5);
            Assert.Single(overall);
            Assert.Equal(1.0, overall[0].Key, 6);
            Assert.Equal(9.0, overall[0].Value, 6);
            Assert.Empty(DimChains.Overall(new[] { 1.0, 1.2 }, 0.5));
            Assert.Empty(DimChains.Overall(new[] { 1.0 }, 0.5));
        }

        [Fact]
        public void RepeatedChainIsDetected()
        {
            var a = DimChains.Overall(new[] { 0.0, 10.0 }, 0.5);
            var b = DimChains.Segments(new[] { 0.0, 10.0 }, 0.5);
            Assert.True(DimChains.Same(a, b, 0.01));
            var c = DimChains.Segments(new[] { 0.0, 4.0, 10.0 }, 0.5);
            Assert.False(DimChains.Same(a, c, 0.01));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class DimStaggerTests
    {
        private static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<double, double>> Chain(params double[] points)
        {
            return DimChains.Segments(points, 0);
        }

        [Fact]
        public void LongSegmentsStayOnTheLine()
        {
            Assert.Equal(new[] { 0, 0 }, DimChains.Rows(Chain(0, 3, 6), 1.0, 2));
        }

        [Fact]
        public void ShortSegmentsAlternateRows()
        {
            // three 0.5 wide segments with text 1.0 wide: each text overlaps the last on the same row
            var rows = DimChains.Rows(Chain(0, 0.5, 1.0, 1.5), 1.0, 2);
            Assert.Equal(new[] { 1, 2, 1 }, rows);
        }

        [Fact]
        public void ShortSegmentBesideLongOneUsesRowOne()
        {
            var rows = DimChains.Rows(Chain(0, 4, 4.3, 8), 1.0, 2);
            Assert.Equal(new[] { 0, 1, 0 }, rows);
        }

        [Fact]
        public void FarApartShortSegmentsShareRowOne()
        {
            var rows = DimChains.Rows(Chain(0, 0.4, 5, 5.4), 1.0, 2);
            Assert.Equal(new[] { 1, 0, 1 }, rows);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class DimPlannerTests
    {
        private static readonly PlanSide[] All = { PlanSide.Bottom, PlanSide.Top, PlanSide.Left, PlanSide.Right };

        // A 10 x 8 building with 0.23 thick walls drawn as faces (metres), scale 1:100 distances.
        private static PlanInput Building(double unit)
        {
            var plan = new PlanInput { Band = 0.6 * unit, Merge = 0.005 * unit, MinLength = 0.3 * unit };
            double w = 10 * unit, h = 8 * unit, t = 0.23 * unit;
            // outer and inner face corners of the four walls
            foreach (var p in new[] { new PlanPoint(0, 0), new PlanPoint(w, 0), new PlanPoint(w, h), new PlanPoint(0, h),
                                      new PlanPoint(t, t), new PlanPoint(w - t, t), new PlanPoint(w - t, h - t), new PlanPoint(t, h - t) })
                plan.Structural.Add(p);
            return plan;
        }

        [Fact]
        public void PlainBuildingGetsStructureAndOverallOnEverySide()
        {
            int repeated;
            var chains = DimPlanner.Chains(Building(1), All, out repeated);
            Assert.Equal(8, chains.Count); // structure + overall on four sides
            var bottom = chains.Where(c => c.Side == PlanSide.Bottom).ToList();
            Assert.Equal("Structure", bottom[0].Kind);
            Assert.Equal("Overall", bottom[1].Kind);
            Assert.Equal(0, bottom[0].Level);
            Assert.Equal(1, bottom[1].Level);
            // 0.23 wall pieces are below the 0.3 minimum, so structure is the clear span only
            Assert.Single(bottom[0].Segments);
            Assert.Equal(0.23, bottom[0].Segments[0].Key, 6);
            Assert.Equal(9.77, bottom[0].Segments[0].Value, 6);
            Assert.Equal(10.0, bottom[1].Segments[0].Value, 6);
        }

        [Fact]
        public void OpeningsChainIsNearestAndFollowsTheJambs()
        {
            var plan = Building(1);
            plan.Jambs.Add(new PlanPoint(2.0, 0.1));
            plan.Jambs.Add(new PlanPoint(3.0, 0.1));
            int repeated;
            var bottom = DimPlanner.Chains(plan, new[] { PlanSide.Bottom }, out repeated);
            Assert.Equal(new[] { "Openings", "Structure", "Overall" }, bottom.Select(c => c.Kind).ToArray());
            // 0.23 .. 2 .. 3 .. 9.77
            Assert.Equal(3, bottom[0].Segments.Count);
            Assert.Equal(1.0, bottom[0].Segments[1].Value - bottom[0].Segments[1].Key, 6);
        }

        [Fact]
        public void JambsOutsideTheBandAreIgnored()
        {
            var plan = Building(1);
            plan.Jambs.Add(new PlanPoint(4.0, 4.0)); // interior partition
            int repeated;
            var bottom = DimPlanner.Chains(plan, new[] { PlanSide.Bottom }, out repeated);
            Assert.DoesNotContain(bottom, c => c.Kind == "Openings");
        }

        [Fact]
        public void GridBecomesItsOwnChain()
        {
            var plan = Building(1);
            plan.GridX.AddRange(new[] { 0.115, 5.0, 9.885 });
            int repeated;
            var bottom = DimPlanner.Chains(plan, new[] { PlanSide.Bottom }, out repeated);
            var grid = bottom.Single(c => c.Kind == "Grid");
            Assert.Equal(2, grid.Segments.Count);
        }

        [Fact]
        public void ChainThatRepeatsTheOneInsideIsDropped()
        {
            // a single wall face pair with nothing between: structure equals overall
            var plan = new PlanInput { Band = 0.6, Merge = 0.005, MinLength = 0.3 };
            plan.Structural.Add(new PlanPoint(0, 0));
            plan.Structural.Add(new PlanPoint(5, 0));
            plan.Structural.Add(new PlanPoint(5, 4));
            plan.Structural.Add(new PlanPoint(0, 4));
            int repeated;
            var chains = DimPlanner.Chains(plan, new[] { PlanSide.Bottom }, out repeated);
            Assert.Single(chains);
            Assert.Equal(1, repeated);
        }

        [Fact]
        public void WorksInMillimetreUnits()
        {
            int repeated;
            var chains = DimPlanner.Chains(Building(1000), All, out repeated);
            Assert.Equal(8, chains.Count);
        }

        [Fact]
        public void LevelsCanBeLimited()
        {
            var plan = Building(1);
            plan.Levels = "Overall";
            int repeated;
            var chains = DimPlanner.Chains(plan, All, out repeated);
            Assert.Equal(4, chains.Count);
            Assert.All(chains, c => Assert.Equal("Overall", c.Kind));
        }

        [Fact]
        public void NoGeometryGivesNoChains()
        {
            int repeated;
            Assert.Empty(DimPlanner.Chains(new PlanInput(), All, out repeated));
        }

        [Fact]
        public void TinyPlanInWrongUnitsGivesNothing()
        {
            // a metre-sized minimum length (0.3) against a plan only 0.2 across
            var plan = new PlanInput { Band = 0.6, Merge = 0.005, MinLength = 0.3 };
            plan.Structural.Add(new PlanPoint(0, 0));
            plan.Structural.Add(new PlanPoint(0.2, 0));
            plan.Structural.Add(new PlanPoint(0.2, 0.2));
            plan.Structural.Add(new PlanPoint(0, 0.2));
            int repeated;
            Assert.Empty(DimPlanner.Chains(plan, All, out repeated));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class UnitScaleTests
    {
        [Theory]
        [InlineData("Millimetres", 1.0)]
        [InlineData("Centimetres", 0.1)]
        [InlineData("Metres", 0.001)]
        public void UnitsPerMillimetre(string unit, double expected)
        {
            Assert.Equal(expected, UnitScale.PerMm(unit), 9);
            Assert.Equal(0.0, UnitScale.PerMm("Parsecs"), 9);
        }

        [Theory]
        [InlineData(10.0, "Metres")]
        [InlineData(12000.0, "Millimetres")]
        [InlineData(1200.0, "Centimetres")]
        [InlineData(0.5, null)]
        public void GuessesTheUnitFromThePlanSize(double span, string expected)
        {
            Assert.Equal(expected, UnitScale.Guess(span));
        }

        [Fact]
        public void PlausibleBuildingSizes()
        {
            Assert.True(UnitScale.Plausible(10000));   // 10 m
            Assert.False(UnitScale.Plausible(10));     // 1 cm
            Assert.False(UnitScale.Plausible(5e7));    // 50 km
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LayerChoiceTests
    {
        [Fact]
        public void GuessesRolesFromNames()
        {
            var c = LayerChoice.Guess(new[] { "0", "A-WALL", "A-WALL-EXT", "A-DOOR", "A-WINDOW", "S-COLUMN", "FURNITURE", "AN-TEXT" });
            Assert.Equal(new[] { "A-WALL", "A-WALL-EXT" }, c.Walls.ToArray());
            Assert.Equal(new[] { "A-DOOR", "A-WINDOW" }, c.Windows.ToArray());
            Assert.Equal(new[] { "S-COLUMN" }, c.Columns.ToArray());
            Assert.Equal(new[] { "FURNITURE" }, c.Furniture.ToArray());
        }

        [Fact]
        public void ALayerHasOneRole()
        {
            var c = LayerChoice.Guess(new[] { "WALL-DOOR-COLUMN" });
            Assert.Single(c.Walls);
            Assert.Empty(c.Windows);
            Assert.Empty(c.Columns);
        }

        [Fact]
        public void RoundTripsThroughLines()
        {
            var c = new LayerChoice { LeaveIsolated = false };
            c.Walls.AddRange(new[] { "A|WALL", "B" });
            c.Windows.Add("WIN");
            c.Furniture.Add("FURN");
            var back = LayerChoice.FromLines(c.ToLines());
            Assert.Equal(new[] { "A|WALL", "B" }, back.Walls.ToArray());
            Assert.Equal(new[] { "WIN" }, back.Windows.ToArray());
            Assert.Empty(back.Columns);
            Assert.Equal(new[] { "FURN" }, back.Furniture.ToArray());
            Assert.False(back.LeaveIsolated);
        }

        [Fact]
        public void ShownExcludesFurniture()
        {
            var c = new LayerChoice();
            c.Walls.Add("W"); c.Windows.Add("O"); c.Columns.Add("C"); c.Furniture.Add("F");
            Assert.Equal(new[] { "W", "O", "C" }, c.Shown().ToArray());
        }

        [Fact]
        public void KeepOnlyDropsMissingLayers()
        {
            var c = new LayerChoice();
            c.Walls.AddRange(new[] { "A", "gone" });
            c.KeepOnly(new[] { "a", "B" });
            Assert.Equal(new[] { "A" }, c.Walls.ToArray());
        }

        [Fact]
        public void EmptyLinesGiveDefaults()
        {
            var c = LayerChoice.FromLines(new[] { "EMPTY" });
            Assert.False(c.Any);
            Assert.True(c.LeaveIsolated);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class OpeningClusterTests
    {
        [Fact]
        public void FrameLinesOfOneWindowBecomeOneOpening()
        {
            // three lines along a 1.2 m window in a 0.23 wall at y 0..0.23, and its two short jamb lines
            var boxes = new[]
            {
                new Box(2.0, 0.00, 3.2, 0.00), new Box(2.0, 0.115, 3.2, 0.115), new Box(2.0, 0.23, 3.2, 0.23),
                new Box(2.0, 0.00, 2.0, 0.23), new Box(3.2, 0.00, 3.2, 0.23)
            };
            var openings = OpeningClusters.Cluster(boxes, 0.02);
            Assert.Single(openings);
            Assert.Equal(2.0, openings[0].MinX, 6);
            Assert.Equal(3.2, openings[0].MaxX, 6);
        }

        [Fact]
        public void SeparateOpeningsStaySeparate()
        {
            var boxes = new[] { new Box(2, 0, 3.2, 0.23), new Box(6, 0, 7, 0.23) };
            Assert.Equal(2, OpeningClusters.Cluster(boxes, 0.02).Count);
        }

        [Fact]
        public void TouchingPiecesMerge()
        {
            var boxes = new[] { new Box(2, 0, 3, 0.23), new Box(3.005, 0, 4, 0.23) };
            var openings = OpeningClusters.Cluster(boxes, 0.02);
            Assert.Single(openings);
            Assert.Equal(4.0, openings[0].MaxX, 6);
        }

        [Fact]
        public void DoorWithSwingKeepsItsWidthAlongTheWall()
        {
            // leaf and swing arc of a 0.9 door in a horizontal wall: a 0.9 x 0.9 box
            var boxes = new[] { new Box(1.0, 0.0, 1.9, 0.0), new Box(1.0, 0.0, 1.0, 0.9), new Box(1.0, 0.0, 1.9, 0.9) };
            var door = OpeningClusters.Cluster(boxes, 0.02).Single();
            Assert.Equal(0.9, door.Width, 6);
            var walls = new[] { new WallSegment(0, 0, 10, 0), new WallSegment(0, 8, 10, 8) };
            Assert.True(OpeningClusters.InHorizontalWall(door, walls));
            var jambs = OpeningClusters.Jambs(door, true).ToList();
            Assert.Equal(1.0, jambs[0].X, 6);
            Assert.Equal(1.9, jambs[1].X, 6);
        }

        [Fact]
        public void OpeningInAVerticalWallGivesYJambs()
        {
            var door = new Box(0.0, 3.0, 0.9, 3.9);
            var walls = new[] { new WallSegment(0.2, 0, 0.2, 8), new WallSegment(0, 0, 10, 0) };
            Assert.False(OpeningClusters.InHorizontalWall(door, walls));
            var jambs = OpeningClusters.Jambs(door, false).ToList();
            Assert.Equal(3.0, jambs[0].Y, 6);
            Assert.Equal(3.9, jambs[1].Y, 6);
        }

        [Fact]
        public void WithoutWallsTheLongerSideDecides()
        {
            Assert.True(OpeningClusters.InHorizontalWall(new Box(0, 0, 1.2, 0.2), new WallSegment[0]));
            Assert.False(OpeningClusters.InHorizontalWall(new Box(0, 0, 0.2, 1.2), new WallSegment[0]));
        }

        [Fact]
        public void JambsFeedThePlannerAsOneOpening()
        {
            var plan = new PlanInput { Band = 0.6, Merge = 0.005, MinLength = 0.3 };
            foreach (var p in new[] { new PlanPoint(0, 0), new PlanPoint(10, 0), new PlanPoint(10, 8), new PlanPoint(0, 8) })
                plan.Structural.Add(p);
            var window = new Box(2.0, 0.0, 3.2, 0.23);
            plan.Jambs.AddRange(OpeningClusters.Jambs(window, true));
            int repeated;
            var bottom = DimPlanner.Chains(plan, new[] { PlanSide.Bottom }, out repeated);
            var openings = bottom.Single(c => c.Kind == "Openings");
            // 0..2, 2..3.2 (the window), 3.2..10: no spurious point inside the window
            Assert.Equal(3, openings.Segments.Count);
            Assert.Equal(1.2, openings.Segments[1].Value - openings.Segments[1].Key, 6);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ClusterEquivalenceTests
    {
        // the straightforward all-pairs grouping, as a reference
        private static int BruteGroups(System.Collections.Generic.IList<Box> boxes, double tol)
        {
            int n = boxes.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            System.Func<int, int> find = null;
            find = i => parent[i] == i ? i : (parent[i] = find(parent[i]));
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    if (a.MinX - tol <= b.MaxX && b.MinX - tol <= a.MaxX && a.MinY - tol <= b.MaxY && b.MinY - tol <= a.MaxY)
                        parent[find(i)] = find(j);
                }
            return Enumerable.Range(0, n).Select(find).Distinct().Count();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void SweepGivesTheSameGroupsAsComparingEveryPair(int seed)
        {
            var rnd = new System.Random(seed);
            var boxes = new System.Collections.Generic.List<Box>();
            for (int i = 0; i < 600; i++)
            {
                double x = rnd.NextDouble() * 40, y = rnd.NextDouble() * 30;
                boxes.Add(new Box(x, y, x + rnd.NextDouble() * 1.5, y + rnd.NextDouble() * 0.4));
            }
            Assert.Equal(BruteGroups(boxes, 0.02), OpeningClusters.Cluster(boxes, 0.02).Count);
        }

        [Fact]
        public void EmptyAndSingle()
        {
            Assert.Empty(OpeningClusters.Cluster(new Box[0], 0.02));
            Assert.Single(OpeningClusters.Cluster(new[] { new Box(0, 0, 1, 1) }, 0.02));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ElectricalTests
    {
        private static ElNode Board(string id, double x, double y) => new ElNode { Id = id, IsBoard = true, Box = new Box(x - 0.1, y - 0.1, x + 0.1, y + 0.1) };
        private static ElNode Point(string id, double x, double y) => new ElNode { Id = id, IsBoard = false, Box = new Box(x - 0.1, y - 0.1, x + 0.1, y + 0.1) };
        private static ElWire Wire(params double[] xy)
        {
            var w = new ElWire();
            for (int i = 0; i < xy.Length; i += 2) w.Points.Add(new PlanPoint(xy[i], xy[i + 1]));
            return w;
        }

        [Fact]
        public void TwoLightsOnOneBoard()
        {
            var nodes = new[] { Board("SB-01", 0, 0), Point("LP-01", 5, 0), Point("LP-02", 5, 3) };
            var wires = new[] { Wire(0.1, 0, 5, 0), Wire(0.1, 0, 0.1, 3, 5, 3) };
            var rows = ElectricalSchedule.ByBoard(nodes, ElectricalNet.Build(nodes, wires, 0.05));
            Assert.Equal(new[] { "SB-01/LP-01/Direct", "SB-01/LP-02/Direct" }, rows.Select(r => r.Board + "/" + r.Point + "/" + r.Connection).ToArray());
        }

        [Fact]
        public void DaisyChainedLightReachesTheBoardThroughItsNeighbour()
        {
            // LP-01 --- LP-02 --- SB-01
            var nodes = new[] { Board("SB-01", 10, 0), Point("LP-01", 0, 0), Point("LP-02", 5, 0) };
            var wires = new[] { Wire(0.1, 0, 4.9, 0), Wire(5.1, 0, 9.9, 0) };
            var nets = ElectricalNet.Build(nodes, wires, 0.05);
            var rows = ElectricalSchedule.ByBoard(nodes, nets);
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.Equal("SB-01", r.Board));
            Assert.All(rows, r => Assert.Equal("Direct", r.Connection));
        }

        [Fact]
        public void TwoWayPointDoesNotMixTheBoardsOtherLights()
        {
            var nodes = new[]
            {
                Board("SB-01", 0, 0), Board("SB-02", 20, 0),
                Point("LP-01", 5, 5), Point("LP-03", 10, 0)
            };
            var wires = new[]
            {
                Wire(0.1, 0, 9.9, 0),        // SB-01 to LP-03
                Wire(10.1, 0, 19.9, 0),      // LP-03 to SB-02
                Wire(0.1, 0.1, 0.1, 5, 4.9, 5) // SB-01 to LP-01
            };
            var nets = ElectricalNet.Build(nodes, wires, 0.05);
            var rows = ElectricalSchedule.ByBoard(nodes, nets);
            var text = rows.Select(r => r.Board + " " + r.Point + " " + r.Connection).ToArray();
            Assert.Equal(new[] { "SB-01 LP-01 Direct", "SB-01 LP-03 2 Way", "SB-02 LP-03 2 Way" }, text);
            var byPoint = ElectricalSchedule.ByPoint(nodes, nets);
            Assert.Equal("LP-03", byPoint[1].Key);
            Assert.Equal("SB-01, SB-02", byPoint[1].Value);
        }

        [Fact]
        public void TJunctionJoinsWires()
        {
            var nodes = new[] { Board("SB-01", 0, 0), Point("LP-01", 10, 5), Point("LP-02", 10, -5) };
            var wires = new[] { Wire(0.1, 0, 10, 0), Wire(10, 0, 10, 4.9), Wire(10, 0, 10, -4.9) };
            var rows = ElectricalSchedule.ByBoard(nodes, ElectricalNet.Build(nodes, wires, 0.05));
            Assert.Equal(2, rows.Count);
        }

        [Fact]
        public void CrossingWiresDoNotConnect()
        {
            var nodes = new[] { Board("SB-01", 0, 0), Point("LP-01", 10, 0), Board("SB-02", 5, -5), Point("LP-02", 5, 5) };
            var wires = new[] { Wire(0.1, 0, 9.9, 0), Wire(5, -4.9, 5, 4.9) };
            var rows = ElectricalSchedule.ByBoard(nodes, ElectricalNet.Build(nodes, wires, 0.05));
            var text = rows.Select(r => r.Board + " " + r.Point).ToArray();
            Assert.Equal(new[] { "SB-01 LP-01", "SB-02 LP-02" }, text);
        }

        [Fact]
        public void UnconnectedNodesAreReported()
        {
            var nodes = new[] { Board("SB-01", 0, 0), Point("LP-01", 5, 0), Point("LP-02", 50, 50) };
            var wires = new[] { Wire(0.1, 0, 4.9, 0) };
            var nets = ElectricalNet.Build(nodes, wires, 0.05);
            Assert.Equal(new[] { 2 }, ElectricalSchedule.Unconnected(nodes, nets).ToArray());
        }

        [Fact]
        public void WireToMissingBoardLeavesPointsUnconnected()
        {
            var nodes = new[] { Point("LP-01", 5, 0), Point("LP-02", 10, 0) };
            var wires = new[] { Wire(5.1, 0, 9.9, 0) };
            var nets = ElectricalNet.Build(nodes, wires, 0.05);
            Assert.Equal(2, ElectricalSchedule.Unconnected(nodes, nets).Count);
            Assert.Empty(ElectricalSchedule.ByBoard(nodes, nets));
        }

        [Fact]
        public void NaturalOrder()
        {
            var ids = new[] { "SB-10", "SB-02", "LP-01", "SB-01" };
            var sorted = ids.OrderBy(x => x, System.Collections.Generic.Comparer<string>.Create(ElectricalSchedule.NaturalCompare)).ToArray();
            Assert.Equal(new[] { "LP-01", "SB-01", "SB-02", "SB-10" }, sorted);
        }
    }

    public class ElectricalNumberingTests
    {
        private static NumberItem Item(int key, double x, double y, string existing = "") => new NumberItem { Key = key, X = x, Y = y, Existing = existing };

        [Fact]
        public void FirstNumberingIsInReadingOrder()
        {
            var ids = ElectricalNumbering.Assign(new[] { Item(0, 9, 0), Item(1, 1, 5), Item(2, 1, 2) }, "LP", false);
            Assert.Equal("LP-01", ids[2]); // x 1, y 2
            Assert.Equal("LP-02", ids[1]); // x 1, y 5
            Assert.Equal("LP-03", ids[0]);
        }

        [Fact]
        public void ExistingIdsAreKeptAndNewOnesFollow()
        {
            var ids = ElectricalNumbering.Assign(new[] { Item(0, 0, 0, "LP-05"), Item(1, 5, 0), Item(2, 9, 0, "LP-02") }, "LP", false);
            Assert.Equal("LP-05", ids[0]);
            Assert.Equal("LP-02", ids[2]);
            Assert.Equal("LP-06", ids[1]);
        }

        [Fact]
        public void CopiedBlockWithARepeatedIdGetsANewOne()
        {
            var ids = ElectricalNumbering.Assign(new[] { Item(0, 0, 0, "LP-01"), Item(1, 8, 0, "LP-01") }, "LP", false);
            Assert.Equal("LP-01", ids[0]);
            Assert.Equal("LP-02", ids[1]);
        }

        [Fact]
        public void WrongPrefixIsRenumbered()
        {
            var ids = ElectricalNumbering.Assign(new[] { Item(0, 0, 0, "FP-01") }, "LP", false);
            Assert.Equal("LP-01", ids[0]);
        }

        [Fact]
        public void RenumberAllStartsAgain()
        {
            var ids = ElectricalNumbering.Assign(new[] { Item(0, 5, 0, "LP-01"), Item(1, 0, 0, "LP-07") }, "LP", true);
            Assert.Equal("LP-01", ids[1]);
            Assert.Equal("LP-02", ids[0]);
        }

        [Fact]
        public void NumbersGrowPastNinetyNine()
        {
            Assert.Equal("LP-99", ElectricalNumbering.Format("LP", 99));
            Assert.Equal("LP-100", ElectricalNumbering.Format("LP", 100));
        }

        [Theory]
        [InlineData("LP", "LP", true)]
        [InlineData("lp", "LP", true)]
        [InlineData("LP_2W", "LP*", true)]
        [InlineData("SB", "LP*", false)]
        [InlineData("", "LP", false)]
        public void BlockNameMatching(string name, string pattern, bool expected)
        {
            Assert.Equal(expected, ElectricalNumbering.NameMatches(name, new[] { pattern }));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class StairCalcTests
    {
        private static StairSpec Dog() => new StairSpec { Kind = StairKind.DogLeg, Width = 1200, FloorHeight = 3300, TotalRisers = 20, FirstFlightRisers = 10, Going = 270, LandingLength = 1200 };

        [Fact]
        public void TheWorkedExample()
        {
            var c = StairCalc.Calculate(Dog());
            Assert.Equal(165.0, c.Rise, 6);
            Assert.Equal(600.0, c.TwoRPlusG, 6);
            Assert.Equal(new[] { 10, 10 }, c.FlightRisers);
            Assert.Equal(new[] { 9, 9 }, c.FlightTreads);
            Assert.Equal(2430.0, c.FlightLengths[0], 6);
            Assert.Equal(1650.0, c.LandingLevel, 6);
            Assert.Equal(3300.0, c.TopLevel, 6);
            Assert.True(c.CanDraw);
            Assert.All(c.Checks, x => Assert.True(x.Ok, x.Name));
        }

        [Fact]
        public void LandingLevelFollowsTheFirstFlight()
        {
            var s = Dog();
            s.FirstFlightRisers = 9;
            var c = StairCalc.Calculate(s);
            Assert.Equal(9 * 165.0, c.LandingLevel, 6);
            Assert.False(c.Checks.Single(x => x.Name == "Flight distribution").Ok); // 9 / 11 differs by two
        }

        [Fact]
        public void UnequalFlightsAreFlagged()
        {
            var s = Dog();
            s.FirstFlightRisers = 8; // 8 / 12
            var c = StairCalc.Calculate(s);
            Assert.False(c.Checks.Single(x => x.Name == "Flight distribution").Ok);
            Assert.True(c.CanDraw);
        }

        [Fact]
        public void SteepRiseFailsTheOfficeLimit()
        {
            var s = Dog();
            s.FloorHeight = 4600; // 230 mm rise
            var c = StairCalc.Calculate(s);
            Assert.False(c.Checks.Single(x => x.Name == "Rise").Ok);
            Assert.False(c.Checks.Single(x => x.Name == "2R + G").Ok);
        }

        [Fact]
        public void LimitsAreConfigurable()
        {
            var s = Dog();
            s.FloorHeight = 4200; // 210 mm rise, 2R+G 690
            var c = StairCalc.Calculate(s, new StairLimits { MaxRise = 220, Max2RG = 720 });
            Assert.True(c.Checks.Single(x => x.Name == "Rise").Ok);
            Assert.True(c.Checks.Single(x => x.Name == "2R + G").Ok);
        }

        [Fact]
        public void OneRiserFlightCannotBeDrawn()
        {
            var s = Dog();
            s.FirstFlightRisers = 1;
            Assert.False(StairCalc.Calculate(s).CanDraw);
        }

        [Fact]
        public void SingleFlightHasNoLandingLevel()
        {
            var s = new StairSpec { Kind = StairKind.Single, TotalRisers = 20 };
            var c = StairCalc.Calculate(s);
            Assert.Equal(new[] { 20 }, c.FlightRisers);
            Assert.Equal(19 * 270.0, c.FlightLengths[0], 6);
            Assert.Equal(c.TopLevel, c.LandingLevel, 6);
        }

        [Fact]
        public void LandingWidthPerType()
        {
            var s = Dog();
            Assert.Equal(2400.0, StairCalc.Calculate(s).LandingWidth, 6);
            s.Kind = StairKind.U; s.WellWidth = 200;
            Assert.Equal(2600.0, StairCalc.Calculate(s).LandingWidth, 6);
            s.Kind = StairKind.L;
            Assert.Equal(1200.0, StairCalc.Calculate(s).LandingWidth, 6);
        }

        [Fact]
        public void AutoRisersFromThePreferredRise()
        {
            Assert.Equal(20, StairSpec.AutoRisers(3300, 165, true));
            Assert.Equal(18, StairSpec.AutoRisers(3000, 165, true)); // 18.18
            Assert.Equal(10, StairSpec.AutoFirstFlight(19) - 0);
            Assert.Equal(10, StairSpec.AutoFirstFlight(20));
        }

        [Fact]
        public void SpecRoundTripsThroughLines()
        {
            var s = Dog();
            s.Kind = StairKind.U; s.WellWidth = 175.5; s.OpenWell = false; s.TurnLeft = false; s.PlanAngleDegrees = 45; s.PlanX = 12345.678; s.StartLevel = -150;
            var back = StairSpec.FromLines(s.ToLines());
            Assert.Equal(StairKind.U, back.Kind);
            Assert.Equal(175.5, back.WellWidth, 6);
            Assert.False(back.OpenWell);
            Assert.False(back.TurnLeft);
            Assert.Equal(45.0, back.PlanAngleDegrees, 6);
            Assert.Equal(12345.678, back.PlanX, 6);
            Assert.Equal(-150.0, back.StartLevel, 6);
            Assert.Equal(20, back.TotalRisers);
        }
    }

    public class StairFormatTests
    {
        [Theory]
        [InlineData(0.0, "±0.000")]
        [InlineData(1650.0, "+1.650")]
        [InlineData(-150.0, "-0.150")]
        [InlineData(3300.0, "+3.300")]
        public void MetricLevels(double mm, string expected) => Assert.Equal(expected, StairFormat.Level(mm, false));

        [Fact]
        public void MetricLengthsAreWholeMillimetres() => Assert.Equal("2430", StairFormat.Length(2430.4, false));

        [Theory]
        [InlineData(1200.0, "3'-11 1/4\"")]
        [InlineData(304.8, "1'-0\"")]
        [InlineData(152.4, "6\"")]
        [InlineData(0.0, "0\"")]
        public void ImperialLengths(double mm, string expected) => Assert.Equal(expected, StairFormat.Length(mm, true));

        [Theory]
        [InlineData("mm", 1.0)]
        [InlineData("m", 1000.0)]
        [InlineData("in", 25.4)]
        [InlineData("ft", 304.8)]
        [InlineData("furlong", 0.0)]
        public void InputUnits(string unit, double expected) => Assert.Equal(expected, StairFormat.MmPer(unit), 6);
    }
}

namespace HCW.Logic.Tests
{
    public class StairGeometryTests
    {
        private static (StairSpec, StairCalc) Make(StairKind kind, int first = 10, int total = 20)
        {
            var s = new StairSpec { Kind = kind, Width = 1200, FloorHeight = 3300, TotalRisers = total, FirstFlightRisers = first, Going = 270, LandingLength = 1200, WaistThickness = 150, LandingThickness = 150 };
            return (s, StairCalc.Calculate(s));
        }

        private static readonly StairOptions Opt = new StairOptions();

        private static bool Crosses(PlanPoint a, PlanPoint b, PlanPoint c, PlanPoint d)
        {
            Func<PlanPoint, PlanPoint, PlanPoint, double> cross = (p, q, r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            double d1 = cross(a, b, c), d2 = cross(a, b, d), d3 = cross(c, d, a), d4 = cross(c, d, b);
            return ((d1 > 1e-6 && d2 < -1e-6) || (d1 < -1e-6 && d2 > 1e-6)) && ((d3 > 1e-6 && d4 < -1e-6) || (d3 < -1e-6 && d4 > 1e-6));
        }

        /// <summary>No two edges of a closed outline cross each other (edges that only share an end point are fine).</summary>
        private static bool IsSimple(GPoly p)
        {
            int n = p.Pts.Count;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1 || (i == 0 && j == n - 1)) continue;
                    if (Crosses(p.Pts[i], p.Pts[(i + 1) % n], p.Pts[j], p.Pts[(j + 1) % n])) return false;
                }
            return true;
        }

        [Theory]
        [InlineData(StairKind.Single)]
        [InlineData(StairKind.DogLeg)]
        [InlineData(StairKind.U)]
        [InlineData(StairKind.L)]
        public void SectionOutlinesAreSimpleAndReachTheFloorLevels(StairKind kind)
        {
            var (s, c) = Make(kind, kind == StairKind.Single ? 20 : 10);
            var d = StairGeometry.Section(s, c, Opt);
            foreach (var poly in d.Polys.Where(p => p.Layer == "SECTION")) Assert.True(IsSimple(poly), kind + " outline crosses itself");
            double top = d.Polys.Where(p => p.Layer == "SECTION").SelectMany(p => p.Pts).Max(p => p.Y);
            Assert.Equal(3300.0, top, 6);
        }

        [Fact]
        public void DogLegSectionLevelsAndCounts()
        {
            var (s, c) = Make(StairKind.DogLeg);
            var d = StairGeometry.Section(s, c, Opt);
            var flights = d.Polys.Where(p => p.Layer == "SECTION").ToList();
            Assert.Equal(3, flights.Count); // lower slab, flight 1 with landing, flight 2 with upper slab
            // the landing top is at the level of the first flight
            Assert.Contains(flights[1].Pts, p => Math.Abs(p.Y - 1650) < 1e-6 && Math.Abs(p.X - (9 * 270 + 1200)) < 1e-6);
            // flight 2 starts on the landing and returns towards the start: its top nosing is at x = 9*270 - 9*270 = 0
            Assert.Contains(flights[2].Pts, p => Math.Abs(p.Y - 3300) < 1e-6 && Math.Abs(p.X) < 1e-6);
            Assert.Contains(d.Texts, t => t.Text.StartsWith("LANDING +1.650"));
            Assert.Contains(d.Texts, t => t.Text == "FFL +3.300");
            Assert.Contains(d.Texts, t => t.Text == "FFL ±0.000");
        }

        [Fact]
        public void EveryRiserAndTreadAppearsInTheFlightOutline()
        {
            var (s, c) = Make(StairKind.Single, 20, 20);
            var d = StairGeometry.Section(s, c, Opt);
            var flight = d.Polys.Where(p => p.Layer == "SECTION").ElementAt(1);
            // 20 risers: the outline steps up 20 times by 165
            var ups = 0;
            for (int i = 0; i + 1 < flight.Pts.Count; i++)
                if (Math.Abs(flight.Pts[i + 1].X - flight.Pts[i].X) < 1e-9 && Math.Abs(flight.Pts[i + 1].Y - flight.Pts[i].Y - 165) < 1e-6) ups++;
            Assert.Equal(20, ups);
        }

        [Fact]
        public void SoffitIsTheWaistThicknessBelowThePitchLine()
        {
            var (s, c) = Make(StairKind.Single, 20, 20);
            var d = StairGeometry.Section(s, c, Opt);
            var flight = d.Polys.Where(p => p.Layer == "SECTION").ElementAt(1);
            // the two soffit points are the last two before closing; the perpendicular distance from the nosing line is 150
            double theta = Math.Atan2(165, 270);
            var pts = flight.Pts;
            var a = pts[pts.Count - 1]; var b = pts[pts.Count - 2];
            double slope = (b.Y - a.Y) / (b.X - a.X);
            Assert.Equal(Math.Tan(theta), slope, 6);
            // vertical gap between the nosing line (through (0,165)) and the soffit is 150 / cos(theta)
            double nosingY = 165 + slope * (a.X - 0);
            Assert.Equal(150.0 / Math.Cos(theta), nosingY - a.Y, 6);
        }

        [Theory]
        [InlineData(StairKind.Single)]
        [InlineData(StairKind.DogLeg)]
        [InlineData(StairKind.U)]
        [InlineData(StairKind.L)]
        public void PlanCountsMatchTheSpec(StairKind kind)
        {
            var (s, c) = Make(kind, kind == StairKind.Single ? 20 : 10);
            var d = StairGeometry.Plan(s, c, Opt);
            int flights = kind == StairKind.Single ? 1 : 2;
            // interior riser lines: risers - 2 per flight
            Assert.Equal(kind == StairKind.Single ? 18 : 16, d.Polys.Count(p => p.Layer == "TREAD"));
            Assert.Equal(flights, d.Polys.Count(p => p.Layer == "ARROW" && p.Pts.Count == 2) / 3);
            Assert.All(d.Polys.Where(p => p.Closed), p => Assert.True(IsSimple(p)));
        }

        [Fact]
        public void PlanFlightLengthDimensionSaysTreadsTimesGoing()
        {
            var (s, c) = Make(StairKind.DogLeg);
            var d = StairGeometry.Plan(s, c, Opt);
            Assert.Contains(d.Dims, x => x.Text == "9 x 270 = 2430");
            Assert.Contains(d.Dims, x => x.Text == "1200"); // width and landing length
            Assert.Contains(d.Dims, x => x.Text == "2400"); // landing width
        }

        [Fact]
        public void UStairKeepsTheWellBetweenTheFlights()
        {
            var (s, c) = Make(StairKind.U);
            s.WellWidth = 200; s.OpenWell = true;
            c = StairCalc.Calculate(s);
            var d = StairGeometry.Plan(s, c, Opt);
            var well = d.Polys.Where(p => p.Layer == "WELL").ToList();
            Assert.Equal(3, well.Count); // outline and the two diagonals of an open well
            var outline = well.First(p => p.Closed);
            Assert.Equal(1200.0, outline.Pts.Min(p => p.Y), 6);
            Assert.Equal(1400.0, outline.Pts.Max(p => p.Y), 6);
            // flight 2 starts beyond the well
            Assert.Equal(2600.0, d.Polys.Where(p => p.Layer == "PLAN").SelectMany(p => p.Pts).Max(p => p.Y), 6);
        }

        [Fact]
        public void ClosedWellHasNoCross()
        {
            var (s, c) = Make(StairKind.U);
            s.OpenWell = false;
            var d = StairGeometry.Plan(s, c, Opt);
            Assert.Single(d.Polys.Where(p => p.Layer == "WELL"));
        }

        [Fact]
        public void RightTurnMirrorsTheSecondFlight()
        {
            var (s, c) = Make(StairKind.DogLeg);
            var left = StairGeometry.Plan(s, c, Opt);
            s.TurnLeft = false;
            var right = StairGeometry.Plan(s, c, Opt);
            Assert.Equal(2400.0, left.Polys.Where(p => p.Layer == "PLAN").SelectMany(p => p.Pts).Max(p => p.Y), 6);
            Assert.Equal(0.0, right.Polys.Where(p => p.Layer == "PLAN").SelectMany(p => p.Pts).Min(p => p.Y) + 1200.0, 6); // second flight now at v -1200..0
        }

        [Fact]
        public void NosingLinesAreOptional()
        {
            var (s, c) = Make(StairKind.Single, 20, 20);
            s.Nosing = 0;
            Assert.Empty(StairGeometry.Plan(s, c, Opt).Polys.Where(p => p.Layer == "NOSING"));
            s.Nosing = 25;
            Assert.Equal(19, StairGeometry.Plan(s, c, Opt).Polys.Count(p => p.Layer == "NOSING"));
        }

        [Fact]
        public void LStairPlanTurnsTheSecondFlightNinetyDegrees()
        {
            var (s, c) = Make(StairKind.L);
            var d = StairGeometry.Plan(s, c, Opt);
            var all = d.Polys.Where(p => p.Layer == "PLAN").SelectMany(p => p.Pts).ToList();
            // flight 1 runs along u for 9 treads; flight 2 runs along v for 9 treads beyond the width
            Assert.Equal(9 * 270.0 + 1200.0, all.Max(p => p.X), 6);
            Assert.Equal(1200.0 + 9 * 270.0, all.Max(p => p.Y), 6);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ElectricalMatrixTests
    {
        private static ElLink L(string board, string point, string code) => new ElLink { Board = board, Point = point, Code = code };

        [Fact]
        public void DefaultColumnsParse()
        {
            var cols = ElectricalMatrix.ParseColumns(ElectricalMatrix.DefaultColumns);
            Assert.Equal("5 Amp", cols[0].Heading);
            Assert.Equal(new[] { "LP", "FP", "P5" }, cols[0].Codes.ToArray());
            Assert.Equal("15 Amp", cols[1].Heading);
            Assert.Equal(12, cols.Count);
            Assert.Equal("TV", cols.Last().Heading);
        }

        [Fact]
        public void UnknownCodesAndEmptyItemsAreIgnored()
        {
            var cols = ElectricalMatrix.ParseColumns("A=LP,XX;;B=SB;C=GY");
            Assert.Equal(2, cols.Count);            // B lists only a board, so it has no kinds
            Assert.Equal(new[] { "LP" }, cols[0].Codes.ToArray());
            Assert.Equal("C", cols[1].Heading);
        }

        [Fact]
        public void EveryCatalogCodeIsUnique()
        {
            Assert.Equal(ElectricalKinds.All.Length, ElectricalKinds.All.Select(k => k.Code).Distinct().Count());
            Assert.Single(ElectricalKinds.All.Where(k => k.IsBoard));
        }

        [Fact]
        public void RowsListThePointNumbersPerBoard()
        {
            var cols = ElectricalMatrix.ParseColumns("5 Amp=LP,FP;15 Amp=P15;One way switches=SW1;2 way switches=SW2;Geyser=GY");
            var links = new[]
            {
                L("SB-01", "LP-01", "LP"), L("SB-01", "LP-02", "LP"), L("SB-01", "FP-01", "FP"), L("SB-01", "SW1-01", "SW1"),
                L("SB-02", "LP-03", "LP"), L("SB-01", "LP-03", "LP"), L("SB-02", "SW2-01", "SW2"), L("SB-02", "GY-01", "GY"), L("SB-02", "P15-01", "P15")
            };
            var rows = ElectricalMatrix.Build(new[] { "SB-02", "SB-01", "SB-03" }, links, cols, false);
            Assert.Equal(new[] { "SB no", "5 Amp", "15 Amp", "One way switches", "2 way switches", "Geyser" }, rows[0]);
            Assert.Equal(new[] { "SB-01", "FP-01, LP-01, LP-02, LP-03", "", "SW1-01", "", "" }, rows[1]);
            Assert.Equal(new[] { "SB-02", "LP-03", "P15-01", "", "SW2-01", "GY-01" }, rows[2]);
            Assert.Equal(new[] { "SB-03", "", "", "", "", "" }, rows[3]);   // a board with nothing wired is still listed
        }

        [Fact]
        public void TotalsCountEachPointOnce()
        {
            var cols = ElectricalMatrix.ParseColumns("5 Amp=LP,FP;Geyser=GY");
            var links = new[] { L("SB-01", "LP-03", "LP"), L("SB-02", "LP-03", "LP"), L("SB-01", "LP-01", "LP") };
            var rows = ElectricalMatrix.Build(new[] { "SB-01", "SB-02" }, links, cols, false);
            Assert.Equal(new[] { "TOTAL", "2", "" }, rows.Last());
        }

        [Fact]
        public void CountsInsteadOfNumbers()
        {
            var cols = ElectricalMatrix.ParseColumns("5 Amp=LP,FP");
            var links = new[] { L("SB-01", "LP-01", "LP"), L("SB-01", "FP-01", "FP") };
            var rows = ElectricalMatrix.Build(new[] { "SB-01" }, links, cols, true);
            Assert.Equal("2", rows[1][1]);
        }

        [Fact]
        public void LinksComeFromTheNets()
        {
            var nodes = new[]
            {
                new ElNode { Id = "SB-01", Code = "SB", IsBoard = true, Box = new Box(-0.1, -0.1, 0.1, 0.1) },
                new ElNode { Id = "GY-01", Code = "GY", Box = new Box(4.9, -0.1, 5.1, 0.1) }
            };
            var wires = new[] { new ElWire { Points = { new PlanPoint(0.1, 0), new PlanPoint(4.9, 0) } } };
            var links = ElectricalSchedule.Links(nodes, ElectricalNet.Build(nodes, wires, 0.05));
            Assert.Single(links);
            Assert.Equal("GY", links[0].Code);
            var rows = ElectricalSchedule.ByBoard(nodes, ElectricalNet.Build(nodes, wires, 0.05));
            Assert.Equal("Geyser", rows[0].Type);
        }
    }
}
