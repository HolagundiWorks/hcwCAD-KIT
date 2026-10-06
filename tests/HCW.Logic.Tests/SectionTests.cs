using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class SectionBuilderTests
    {
        private static LevelRow L(double ffl = 3000, double slab = 150, double lintel = 2100) => new LevelRow { FflMm = ffl, SlabMm = slab, LintelMm = lintel };

        [Fact]
        public void SlabsSitUnderEachFloorAndOnTheRoof()
        {
            var r = SectionBuilder.Build(new[] { L(), L() }, new[] { new SectionWall { S0 = 0, S1 = 230 } }, null, 0, 1000);
            var slabs = r.Where(x => x.Kind == "Slab").OrderBy(x => x.Y0).ToList();
            Assert.Equal(3, slabs.Count);
            Assert.Equal(-150, slabs[0].Y0); Assert.Equal(0, slabs[0].Y1);
            Assert.Equal(2850, slabs[1].Y0); Assert.Equal(3000, slabs[1].Y1);
            Assert.Equal(5850, slabs[2].Y0); Assert.Equal(6000, slabs[2].Y1);
        }

        [Fact]
        public void WallStandsToTheUndersideOfTheSlabAbove()
        {
            var r = SectionBuilder.Build(new[] { L(), L() }, new[] { new SectionWall { S0 = 0, S1 = 230 } }, null, 0, 1000);
            var w = r.Where(x => x.Kind == "Wall" && x.Floor == 0).Single();
            Assert.Equal(0, w.Y0); Assert.Equal(2850, w.Y1);
        }

        [Fact]
        public void DoorLeavesAGapAndALintelBandAbove()
        {
            var r = SectionBuilder.Build(new[] { L() }, new[] { new SectionWall { S0 = 0, S1 = 2000 } },
                new[] { new SectionOpening { S0 = 500, S1 = 1400, SillMm = 0, HeightMm = 2100, Door = true } }, 0, 2000);
            Assert.Equal(2, r.Count(x => x.Kind == "Wall"));
            var lin = r.Single(x => x.Kind == "Lintel");
            Assert.Equal(2100, lin.Y0); Assert.Equal(2850, lin.Y1); Assert.Equal(500, lin.X0); Assert.Equal(1400, lin.X1);
        }

        [Fact]
        public void WindowKeepsWallBelowTheSill()
        {
            var r = SectionBuilder.Build(new[] { L() }, new[] { new SectionWall { S0 = 0, S1 = 2000 } },
                new[] { new SectionOpening { S0 = 500, S1 = 1400, SillMm = 900, HeightMm = 1200 } }, 0, 2000);
            var below = r.Single(x => x.Kind == "Wall" && x.X0 == 500);
            Assert.Equal(0, below.Y0); Assert.Equal(900, below.Y1);
            Assert.Equal(2100, r.Single(x => x.Kind == "Lintel").Y0);
        }

        [Fact]
        public void TallOpeningStopsAtTheLintelBottom()
        {
            var r = SectionBuilder.Build(new[] { L() }, new[] { new SectionWall { S0 = 0, S1 = 2000 } },
                new[] { new SectionOpening { S0 = 500, S1 = 1400, SillMm = 0, HeightMm = 2400, Door = true } }, 0, 2000);
            Assert.Equal(2100, r.Single(x => x.Kind == "Lintel").Y0);
        }

        [Fact]
        public void TouchingWallSpansAreMerged()
        {
            var m = SectionBuilder.Merge(new[] { new SectionWall { S0 = 0, S1 = 230 }, new SectionWall { S0 = 230, S1 = 460 }, new SectionWall { S0 = 900, S1 = 1130 } });
            Assert.Equal(2, m.Count); Assert.Equal(460, m[0].S1);
        }

        [Fact]
        public void CrossingsPairIntoWallsButRoomWidthsAreNot()
        {
            var w = SectionBuilder.PairCrossings(new double[] { 0, 230, 3230, 3460, 6000 }, 600);
            Assert.Equal(2, w.Count);
            Assert.Equal(3230, w[1].S0);
        }

        [Fact]
        public void LineIsClippedToTheOpeningFootprint()
        {
            double t0, t1;
            // a 1000 x 230 footprint, line runs across its width at mid depth
            bool hit = SectionBuilder.ClipLine(-500, 115, 1500, 115, new double[] { 0, 1000, 1000, 0 }, new double[] { 0, 0, 230, 230 }, out t0, out t1);
            Assert.True(hit);
            Assert.Equal(1000, (t1 - t0) * 2000, 3);
            Assert.False(SectionBuilder.ClipLine(-500, 500, 1500, 500, new double[] { 0, 1000, 1000, 0 }, new double[] { 0, 0, 230, 230 }, out t0, out t1));
        }

        [Fact]
        public void LineAcrossTheWallGivesThicknessOfTheOpening()
        {
            double t0, t1;
            Assert.True(SectionBuilder.ClipLine(500, -1000, 500, 1000, new double[] { 0, 1000, 1000, 0 }, new double[] { 0, 0, 230, 230 }, out t0, out t1));
            Assert.Equal(230, (t1 - t0) * 2000, 3);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ColumnQuantityTests
    {
        [Fact]
        public void RectangularColumnConcreteAndShuttering()
        {
            var c = new HCW.AutoCAD.Plugin.Logic.ColumnLine { WidthMm = 230, DepthMm = 450, HeightMm = 3000, Count = 4 };
            Assert.Equal(0.23 * 0.45 * 3.0 * 4, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.Concrete(c), 6);
            Assert.Equal(2 * (0.23 + 0.45) * 3.0 * 4, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.Shuttering(c), 6);
        }

        [Fact]
        public void RoundColumnUsesDiameter()
        {
            var c = new HCW.AutoCAD.Plugin.Logic.ColumnLine { WidthMm = 400, DepthMm = 0, HeightMm = 3000, Count = 1 };
            Assert.Equal(System.Math.PI * 0.2 * 0.2 * 3.0, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.Concrete(c), 6);
            Assert.Equal(System.Math.PI * 0.4 * 3.0, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.Shuttering(c), 6);
        }

        [Fact]
        public void HeightIsFloorToFloorLessTheSlabAbove()
        {
            var lv = new[] { new HCW.AutoCAD.Plugin.Logic.LevelRow { Name = "G", FflMm = 3150, SlabMm = 150 }, new HCW.AutoCAD.Plugin.Logic.LevelRow { Name = "1", FflMm = 3000, SlabMm = 120 } };
            Assert.Equal(3030, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.HeightFromLevels(lv, "G"));
            Assert.Equal(2880, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.HeightFromLevels(lv, "1"));
            Assert.Equal(3030, HCW.AutoCAD.Plugin.Logic.ColumnQuantity.HeightFromLevels(lv, ""));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LintelGeometryTests
    {
        [Fact]
        public void LintelIsOpeningPlusBearingEachSideAndAsWideAsTheWall()
        {
            var r = HCW.AutoCAD.Plugin.Logic.LintelGeometry.Outline(new HCW.AutoCAD.Plugin.Logic.P2(1000, 500), new HCW.AutoCAD.Plugin.Logic.P2(1, 0), 900, 230, 230);
            Assert.Equal(1360, HCW.AutoCAD.Plugin.Logic.LintelGeometry.Length(900, 230));
            Assert.Equal(1000 - 680, r[0].X, 6); Assert.Equal(1000 + 680, r[1].X, 6);
            Assert.Equal(500 - 115, r[0].Y, 6); Assert.Equal(500 + 115, r[2].Y, 6);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ElectricalRoomsTests
    {
        private static HCW.AutoCAD.Plugin.Logic.RoomInput Sq(double x, double y, double s) => new HCW.AutoCAD.Plugin.Logic.RoomInput
        {
            Outline = new System.Collections.Generic.List<HCW.AutoCAD.Plugin.Logic.P2> { new HCW.AutoCAD.Plugin.Logic.P2(x, y), new HCW.AutoCAD.Plugin.Logic.P2(x + s, y), new HCW.AutoCAD.Plugin.Logic.P2(x + s, y + s), new HCW.AutoCAD.Plugin.Logic.P2(x, y + s) }
        };

        [Fact]
        public void PointTakesTheSmallestRoomAroundIt()
        {
            var rooms = new[] { Sq(0, 0, 100), Sq(10, 10, 20) };
            var names = new[] { "HALL", "STORE" };
            Assert.Equal("STORE", HCW.AutoCAD.Plugin.Logic.ElectricalRooms.RoomOf(new HCW.AutoCAD.Plugin.Logic.P2(15, 15), rooms, names));
            Assert.Equal("HALL", HCW.AutoCAD.Plugin.Logic.ElectricalRooms.RoomOf(new HCW.AutoCAD.Plugin.Logic.P2(60, 60), rooms, names));
            Assert.Equal(HCW.AutoCAD.Plugin.Logic.ElectricalRooms.NoRoom, HCW.AutoCAD.Plugin.Logic.ElectricalRooms.RoomOf(new HCW.AutoCAD.Plugin.Logic.P2(500, 500), rooms, names));
        }

        [Fact]
        public void UnnamedRoomGetsANumber()
        {
            Assert.Equal("ROOM 1", HCW.AutoCAD.Plugin.Logic.ElectricalRooms.RoomOf(new HCW.AutoCAD.Plugin.Logic.P2(5, 5), new[] { Sq(0, 0, 10) }, new[] { "" }));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class QuantitySummaryTests
    {
        [Fact]
        public void StairsAndColumnsAreAddedIntoOneTotal()
        {
            var stair = new HCW.AutoCAD.Plugin.Logic.SavedTakeoff { Name = "Stair ST-01" };
            stair.Rows.Add(new[] { "Concrete - waist slab", "1.000", "m3", "" });
            stair.Rows.Add(new[] { "CONCRETE TOTAL", "2.500", "m3", "" });
            stair.Rows.Add(new[] { "SHUTTERING TOTAL", "12.00", "m2", "" });
            var bars = new HCW.AutoCAD.Plugin.Logic.SavedTakeoff { Name = "Stair ST-01 bars" };
            bars.Rows.Add(new[] { "CONCRETE TOTAL", "99", "m3", "" });
            var cols = new HCW.AutoCAD.Plugin.Logic.SavedTakeoff { Name = "Columns", Headers = HCW.AutoCAD.Plugin.Logic.ColumnQuantity.Headers };
            cols.Rows.Add(new[] { "C1", "230 x 450", "3000", "", "4", "1.242", "8.16" });
            cols.Rows.Add(new[] { "TOTAL", "", "", "", "4", "1.242", "8.16" });
            var rows = HCW.AutoCAD.Plugin.Logic.QuantitySummary.Build(new[] { stair, bars, cols });
            Assert.Equal(3, rows.Count);
            Assert.Equal("3.742", rows[2][2]); Assert.Equal("20.16", rows[2][3]);
        }

        [Fact]
        public void NothingSavedGivesNoRows()
        {
            Assert.Empty(HCW.AutoCAD.Plugin.Logic.QuantitySummary.Build(new HCW.AutoCAD.Plugin.Logic.SavedTakeoff[0]));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class CentreFacesTests
    {
        [Fact]
        public void HorizontalLineGivesFacesAboveAndBelow()
        {
            var f = HCW.AutoCAD.Plugin.Logic.CentreFaces.Of(new HCW.AutoCAD.Plugin.Logic.P2(0, 0), new HCW.AutoCAD.Plugin.Logic.P2(1000, 0), 115, 1);
            Assert.Equal(2, f.Length);
            Assert.Equal(115, f[0][0].Y, 6); Assert.Equal(-115, f[1][1].Y, 6);
        }

        [Fact]
        public void AngledLineFacesAreHalfAThicknessAwayAndParallel()
        {
            var a = new HCW.AutoCAD.Plugin.Logic.P2(0, 0); var b = new HCW.AutoCAD.Plugin.Logic.P2(1000, 1000);
            var f = HCW.AutoCAD.Plugin.Logic.CentreFaces.Of(a, b, 115, 1);
            double dist = System.Math.Abs((f[0][0].X - a.X) * 1 - (f[0][0].Y - a.Y) * 1) / System.Math.Sqrt(2);
            Assert.Equal(115, dist, 6);
            Assert.Equal((f[0][1] - f[0][0]).Length, (b - a).Length, 6);
        }

        [Fact]
        public void TinyLineGivesNothing()
        {
            Assert.Null(HCW.AutoCAD.Plugin.Logic.CentreFaces.Of(new HCW.AutoCAD.Plugin.Logic.P2(0, 0), new HCW.AutoCAD.Plugin.Logic.P2(0.1, 0), 115, 1));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LintelDepthTests
    {
        private static System.Collections.Generic.List<HCW.AutoCAD.Plugin.Logic.LintelDepth.Step> T() => HCW.AutoCAD.Plugin.Logic.LintelDepth.Parse(HCW.AutoCAD.Plugin.Logic.LintelDepth.Default);

        [Theory]
        [InlineData(900, 152.4)]
        [InlineData(1219.2, 152.4)]     // exactly 4 ft is still 6 in
        [InlineData(1220, 228.6)]
        [InlineData(1828.8, 228.6)]     // exactly 6 ft is still 9 in
        [InlineData(2400, 304.8)]
        [InlineData(3048, 304.8)]       // exactly 10 ft is 1 ft
        public void DepthFollowsTheOpeningWidth(double width, double depth)
        {
            bool beyond;
            Assert.Equal(depth, HCW.AutoCAD.Plugin.Logic.LintelDepth.For(width, T(), out beyond), 6);
            Assert.False(beyond);
        }

        [Fact]
        public void WiderThanTheTableUsesTheDeepestAndSaysSo()
        {
            bool beyond;
            Assert.Equal(304.8, HCW.AutoCAD.Plugin.Logic.LintelDepth.For(3500, T(), out beyond), 6);
            Assert.True(beyond);
        }

        [Fact]
        public void BadRowsAreSkippedAndEmptyTableGivesZero()
        {
            Assert.Equal(1, HCW.AutoCAD.Plugin.Logic.LintelDepth.Parse("bad; 1000=150; x=1; -5=2").Count);
            bool beyond;
            Assert.Equal(0, HCW.AutoCAD.Plugin.Logic.LintelDepth.For(500, HCW.AutoCAD.Plugin.Logic.LintelDepth.Parse(""), out beyond));
        }

        [Fact]
        public void LintelsOfTheSameSizeShareAMarkAndGiveConcreteAndShuttering()
        {
            var a = new HCW.AutoCAD.Plugin.Logic.LintelLine { OpeningMm = 900, LengthMm = 1360, ThicknessMm = 230, DepthMm = 152.4 };
            var rows = HCW.AutoCAD.Plugin.Logic.LintelQuantity.Rows(new[] { a, a, new HCW.AutoCAD.Plugin.Logic.LintelLine { OpeningMm = 1500, LengthMm = 1960, ThicknessMm = 230, DepthMm = 228.6 } });
            Assert.Equal(3, rows.Count);
            Assert.Equal("LT1", rows[0][0]); Assert.Equal("1500", rows[0][1]);            // widest first
            Assert.Equal("2", rows[1][5]);
            Assert.Equal((1.36 * 0.23 * 0.1524 * 2).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), rows[1][6]);
            Assert.Equal("TOTAL", rows[2][0]); Assert.Equal("3", rows[2][5]);
        }
    }
}
