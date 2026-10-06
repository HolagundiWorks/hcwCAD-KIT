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
