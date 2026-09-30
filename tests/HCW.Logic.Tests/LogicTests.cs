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
