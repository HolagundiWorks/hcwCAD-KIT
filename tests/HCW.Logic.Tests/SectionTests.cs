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
            Assert.Single(HCW.AutoCAD.Plugin.Logic.LintelDepth.Parse("bad; 1000=150; x=1; -5=2"));
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

namespace HCW.Logic.Tests
{
    public class WallRegenTests
    {
        private static HCW.AutoCAD.Plugin.Logic.P2 P(double x, double y) => new HCW.AutoCAD.Plugin.Logic.P2(x, y);
        private static HCW.AutoCAD.Plugin.Logic.Seg S(double x1, double y1, double x2, double y2) => new HCW.AutoCAD.Plugin.Logic.Seg(P(x1, y1), P(x2, y2));
        private static HCW.AutoCAD.Plugin.Logic.RegenSeg R(double x1, double y1, double x2, double y2, double t, bool outer = false) =>
            new HCW.AutoCAD.Plugin.Logic.RegenSeg { A = P(x1, y1), B = P(x2, y2), ThicknessMm = t, Outer = outer };

        [Fact]
        public void RectangleIsOuterAndThePartitionInsideIsNot()
        {
            var segs = new[] { S(0, 0, 4000, 0), S(4000, 0, 4000, 3000), S(4000, 3000, 0, 3000), S(0, 3000, 0, 0), S(2000, 0, 2000, 3000) };
            var o = HCW.AutoCAD.Plugin.Logic.WallRegen.Outer(segs);
            Assert.True(o[0] && o[1] && o[2] && o[3]);
            Assert.False(o[4]);
        }

        [Fact]
        public void PartitionWithAGapIsExtendedOntoTheWallItMeets()
        {
            // a 4.5 in partition stopping 40 mm short of the wall along y = 3000
            var input = new[] { R(0, 3000, 4000, 3000, 228.6, true), R(2000, 0, 2000, 2960, 114.3) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            var part = res.Single(r => r.ThicknessMm == 114.3);
            Assert.Equal(3000, System.Math.Max(part.A.Y, part.B.Y), 6);
            Assert.Equal(1, rep.TJunctions);
        }

        [Fact]
        public void PartitionRunningPastTheWallIsCutBackToIt()
        {
            var input = new[] { R(0, 3000, 4000, 3000, 228.6, true), R(2000, 0, 2000, 3080, 114.3) };
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, new HCW.AutoCAD.Plugin.Logic.RegenReport());
            var part = res.Single(r => r.ThicknessMm == 114.3);
            Assert.Equal(3000, System.Math.Max(part.A.Y, part.B.Y), 6);
        }

        [Fact]
        public void CornerWithAGapMeetsAtTheCrossingOfTheTwoLines()
        {
            var input = new[] { R(0, 0, 3960, 0, 228.6, true), R(4000, 40, 4000, 3000, 228.6, true) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            Assert.Equal(4000, res[0].B.X, 6); Assert.Equal(0, res[0].B.Y, 6);
            Assert.Equal(0, res[1].A.Y, 6);
            Assert.Equal(1, rep.LCorners);
        }

        [Fact]
        public void CornerBetweenTwoThicknessesIsSquaredByHalfTheOtherWall()
        {
            var input = new[] { R(0, 0, 4000, 0, 228.6, true), R(4000, 0, 4000, 2000, 114.3) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            Assert.Equal(4000 + 114.3 / 2, res[0].B.X, 6);     // the 9 in wall runs half of 4.5 in further
            Assert.Equal(-228.6 / 2, res[1].A.Y, 6);           // the 4.5 in wall runs half of 9 in below the corner
            Assert.Equal(1, rep.SquaredCorners);
        }

        [Fact]
        public void TwoFacesMakeOneCentreLineMidwayWithTheGapAsThickness()
        {
            int unpaired;
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.CentreLines(new[] { S(0, 0, 4000, 0), S(0, 230, 4000, 230) }, 60, 600, 1, out unpaired);
            var w = Assert.Single(res);
            Assert.Equal(230, w.ThicknessMm, 6);
            Assert.Equal(115, w.A.Y, 6); Assert.Equal(115, w.B.Y, 6);
            Assert.Equal(0, System.Math.Min(w.A.X, w.B.X), 6); Assert.Equal(4000, System.Math.Max(w.A.X, w.B.X), 6);
            Assert.Equal(0, unpaired);
        }

        [Fact]
        public void FacesOfARoomRingGiveFourCentreLinesAndNoneAcrossTheRoom()
        {
            // outer face 4000 x 3000, inner face 230 in from it: the room (3540 wide) is wider than the wall limit, so only the four walls pair up
            var faces = new[]
            {
                S(0, 0, 4000, 0), S(4000, 0, 4000, 3000), S(4000, 3000, 0, 3000), S(0, 3000, 0, 0),
                S(230, 230, 3770, 230), S(3770, 230, 3770, 2770), S(3770, 2770, 230, 2770), S(230, 2770, 230, 230)
            };
            int unpaired;
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.CentreLines(faces, 60, 600, 1, out unpaired);
            Assert.Equal(4, res.Count);
            Assert.All(res, r => Assert.Equal(230, r.ThicknessMm, 6));
            Assert.Equal(0, unpaired);
            // the centre lines stop short of the corner by half a wall; the corner logic then meets them where the two centre lines cross
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var walls = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(res, 300, 1, 0.5, rep);
            Assert.Equal(4, rep.LCorners);
            var chains = HCW.AutoCAD.Plugin.Logic.WallRegen.Chains(walls, 0.5);
            var ring = Assert.Single(chains);
            Assert.True(ring.Value.Closed);
            Assert.Contains(ring.Value.Points, p => System.Math.Abs(p.X - 115) < 1e-6 && System.Math.Abs(p.Y - 115) < 1e-6);
            Assert.Contains(ring.Value.Points, p => System.Math.Abs(p.X - 3885) < 1e-6 && System.Math.Abs(p.Y - 2885) < 1e-6);
        }

        [Fact]
        public void ThinPartitionBesideAThickWallPairsWithItsOwnFaceNotTheWall()
        {
            // a 112 partition's two faces, and the 230 wall's inner face 770 away: only the partition pairs
            var faces = new[] { S(230, 1000, 3770, 1000), S(230, 1112, 3770, 1112), S(230, 230, 3770, 230) };
            int unpaired;
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.CentreLines(faces, 60, 600, 1, out unpaired);
            var w = Assert.Single(res);
            Assert.Equal(112, w.ThicknessMm, 6);
            Assert.Equal(1056, w.A.Y, 6);
            Assert.Equal(1, unpaired);
        }

        [Fact]
        public void FacesInAnInchDrawingGiveTheThicknessInMillimetres()
        {
            double mm = 1 / 25.4; int unpaired;
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.CentreLines(new[] { S(0, 0, 100, 0), S(0, 9, 100, 9) }, 60 * mm, 600 * mm, mm, out unpaired);
            var w = Assert.Single(res);
            Assert.Equal(228.6, w.ThicknessMm, 6);
            Assert.Equal(4.5, w.A.Y, 6);
        }

        [Fact]
        public void SquaredCornerInAnInchDrawingRunsHalfTheWallInInches()
        {
            // the command passes units per mm (1/25.4 for inches); the corner must run 4.5 in / 2 = 2.25 in past the point, not thousands of inches
            double mm = 1 / 25.4;
            var input = new[] { R(0, 0, 4000 * mm, 0, 228.6, true), R(4000 * mm, 0, 4000 * mm, 2000 * mm, 114.3) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300 * mm, mm, 0.5 * mm, rep);
            Assert.Equal(4000 * mm + 2.25, res[0].B.X, 6);
            Assert.Equal(-4.5, res[1].A.Y, 6);
            Assert.Equal(1, rep.SquaredCorners);
        }

        [Fact]
        public void SameThicknessCornerIsLeftToTheChainToMitre()
        {
            var input = new[] { R(0, 0, 4000, 0, 228.6), R(4000, 0, 4000, 2000, 228.6) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            Assert.Equal(0, rep.SquaredCorners);
            Assert.Equal(4000, res[0].B.X, 6);
            var chains = HCW.AutoCAD.Plugin.Logic.WallRegen.Chains(res, 0.5);
            Assert.Single(chains);
            Assert.Equal(3, chains[0].Value.Points.Count);
        }

        [Fact]
        public void CrossingAndFreeEndsAreCounted()
        {
            var input = new[] { R(0, 1000, 4000, 1000, 114.3), R(2000, 0, 2000, 2000, 114.3) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            Assert.Equal(1, rep.Crossings);
            Assert.Equal(4, rep.FreeEnds);
        }

        [Fact]
        public void ThreeEndsAtOnePointAreSquaredByTheThickestOther()
        {
            var input = new[] { R(0, 0, 2000, 0, 228.6), R(2000, 0, 4000, 0, 114.3), R(2000, 0, 2000, 1500, 114.3) };
            var rep = new HCW.AutoCAD.Plugin.Logic.RegenReport();
            var res = HCW.AutoCAD.Plugin.Logic.WallRegen.Resolve(input, 300, 1, 0.5, rep);
            Assert.Equal(1, rep.SquaredCorners);
            Assert.Equal(2000 + 114.3 / 2, res[0].B.X, 6);      // the thickest of the other two ends is 4.5 in
        }
    }
}
