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
