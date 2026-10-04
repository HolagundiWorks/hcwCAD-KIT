using System;
using System.Collections.Generic;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class WallGeometryTests
    {
        private static double Area(IList<P2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
            return Math.Abs(a) / 2;
        }

        [Fact]
        public void StraightWallIsARectangle()
        {
            var loops = WallGeometry.Outline(new[] { new P2(0, 0), new P2(4, 0) }, false, 0.23, WallJustify.Centre);
            Assert.Single(loops);
            Assert.Equal(4, loops[0].Count);
            Assert.Equal(4 * 0.23, Area(loops[0]), 9);
        }

        [Fact]
        public void LCornerIsMitred()
        {
            var loops = WallGeometry.Outline(new[] { new P2(0, 0), new P2(4, 0), new P2(4, 3) }, false, 0.2, WallJustify.Centre);
            Assert.Single(loops);
            Assert.Equal(6, loops[0].Count);
            // Centreline length 7 x 0.2 thick; a mitred L has exactly that area.
            Assert.Equal(7 * 0.2, Area(loops[0]), 9);
        }

        [Fact]
        public void ClosedRunGivesOuterAndInner()
        {
            var sq = new[] { new P2(0, 0), new P2(4, 0), new P2(4, 4), new P2(0, 4) };
            var loops = WallGeometry.Outline(sq, true, 0.2, WallJustify.Centre);
            Assert.Equal(2, loops.Count);
            Assert.Equal(4.2 * 4.2 - 3.8 * 3.8, Math.Abs(Area(loops[0]) - Area(loops[1])), 9);
            Assert.Equal(4.2 * 4.2, Math.Max(Area(loops[0]), Area(loops[1])), 9);
        }

        [Theory]
        [InlineData(WallJustify.Left, 0.0, -0.2)]
        [InlineData(WallJustify.Right, 0.2, 0.0)]
        public void JustifyPlacesTheLine(WallJustify j, double yHi, double yLo)
        {
            var loop = WallGeometry.Outline(new[] { new P2(0, 0), new P2(1, 0) }, false, 0.2, j)[0];
            Assert.Equal(yHi, loop.Max(p => p.Y), 9);
            Assert.Equal(yLo, loop.Min(p => p.Y), 9);
        }

        [Fact]
        public void SharpTurnIsBevelled()
        {
            // A near-reversal would throw the mitre far away; it is bevelled to two points instead.
            var o = WallGeometry.Offset(new[] { new P2(0, 0), new P2(4, 0), new P2(0, 0.05) }, false, 0.1);
            Assert.True(o.All(p => Math.Abs(p.X) < 10 && Math.Abs(p.Y) < 10));
            Assert.True(o.Count >= 4);
        }

        [Fact]
        public void DegenerateInputGivesNothing()
        {
            Assert.Empty(WallGeometry.Offset(new[] { new P2(1, 1) }, false, 1));
            Assert.Empty(WallGeometry.Offset(new[] { new P2(1, 1), new P2(1, 1) }, false, 1));
            Assert.Empty(WallGeometry.Offset(new[] { new P2(0, 0), new P2(1, 0) }, true, 1));
        }

        [Fact]
        public void SimplifyDropsCollinearVertices()
        {
            var r = WallGeometry.Simplify(new[] { new P2(0, 0), new P2(2, 0), new P2(4, 0), new P2(4, 3), new P2(0, 3) }, true, 1e-9);
            Assert.Equal(4, r.Count);
        }
    }

    public class OpeningCutTests
    {
        // A 4 m wall along X, faces at y = 0 and y = 0.23.
        private static List<Seg> Wall() => new List<Seg>
        {
            new Seg(new P2(0, 0), new P2(4, 0)),
            new Seg(new P2(4, 0.23), new P2(0, 0.23)),
        };

        [Fact]
        public void FindsOppositeFaceAndCut()
        {
            string err;
            var plan = OpeningCut.Plan(Wall(), new P2(2, 0.1), 0.9, 0.05, 0.6, out err);
            Assert.NotNull(plan);
            Assert.Equal(0, plan.First);
            Assert.Equal(1, plan.Second);
            Assert.Equal(1.55, plan.T0, 9);
            Assert.Equal(2.45, plan.T1, 9);
            Assert.Equal(0.23, plan.Thickness, 9);
            Assert.Equal(1.55, plan.P2a.X, 9);
            Assert.Equal(0.23, plan.P2a.Y, 9);
            // The second face runs the other way; its interval is still low to high.
            Assert.Equal(1.55, plan.U0, 9);
            Assert.Equal(2.45, plan.U1, 9);
        }

        [Fact]
        public void PickOnTheOtherFaceStillWorks()
        {
            string err;
            var plan = OpeningCut.Plan(Wall(), new P2(1, 0.23), 0.8, 0.05, 0.6, out err);
            Assert.NotNull(plan);
            Assert.Equal(1, plan.First);
            Assert.Equal(0, plan.Second);
        }

        [Fact]
        public void RefusesWhenOffTheWallOrPastTheEnd()
        {
            string err;
            Assert.Null(OpeningCut.Plan(Wall(), new P2(2, 5), 0.9, 0.05, 0.6, out err));
            Assert.Contains("near", err);
            Assert.Null(OpeningCut.Plan(Wall(), new P2(0.3, 0.1), 0.9, 0.05, 0.6, out err));
            Assert.Contains("end", err);
        }

        [Fact]
        public void IgnoresFacesOfAnotherWall()
        {
            var segs = Wall();
            segs.Add(new Seg(new P2(0, 3), new P2(4, 3)));           // far away
            string err;
            var plan = OpeningCut.Plan(segs, new P2(2, 0.1), 0.9, 0.05, 0.6, out err);
            Assert.Equal(1, plan.Second);
        }

        [Fact]
        public void RemoveLeavesTheEnds()
        {
            var pieces = OpeningCut.Remove(new Seg(new P2(0, 0), new P2(4, 0)), 1.55, 2.45);
            Assert.Equal(2, pieces.Count);
            Assert.Equal(1.55, pieces[0].B.X, 9);
            Assert.Equal(2.45, pieces[1].A.X, 9);
            Assert.Single(OpeningCut.Remove(new Seg(new P2(0, 0), new P2(4, 0)), 0, 1));
            Assert.Empty(OpeningCut.Remove(new Seg(new P2(0, 0), new P2(1, 0)), 0, 1));
        }
    }

    public class GridModelTests
    {
        [Fact]
        public void ParsesRepeatsAndSeparators()
        {
            string err;
            var s = GridModel.ParseSpacings("4000 4500, 3*3600;2x1000", out err);
            Assert.Null(err);
            Assert.Equal(new double[] { 4000, 4500, 3600, 3600, 3600, 1000, 1000 }, s.ToArray());
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("-5")]
        [InlineData("0*100")]
        [InlineData("3*")]
        public void RejectsBadInput(string text)
        {
            string err;
            Assert.Null(GridModel.ParseSpacings(text, out err));
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void PositionsAreRunningTotals() =>
            Assert.Equal(new double[] { 0, 4, 7 }, GridModel.Positions(new[] { 4.0, 3.0 }).ToArray());

        [Fact]
        public void LettersSkipIAndO()
        {
            Assert.Equal("A", GridModel.Letter(0));
            Assert.Equal("H", GridModel.Letter(7));
            Assert.Equal("J", GridModel.Letter(8));      // I skipped
            Assert.Equal("N", GridModel.Letter(12));
            Assert.Equal("P", GridModel.Letter(13));     // O skipped
            Assert.Equal("Z", GridModel.Letter(23));
            Assert.Equal("AA", GridModel.Letter(24));
            Assert.Equal("AB", GridModel.Letter(25));
            Assert.Equal("1", GridModel.Number(0));
            Assert.Equal("5", GridModel.Number(2, 3));
        }
    }

    public class SegmentChainTests
    {
        [Fact]
        public void JoinsShuffledFlippedSegmentsIntoAClosedLoop()
        {
            var segs = new List<Seg>
            {
                new Seg(new P2(4, 0), new P2(4, 3)),
                new Seg(new P2(0, 0), new P2(4, 0)),
                new Seg(new P2(0, 3), new P2(0, 0)),
                new Seg(new P2(4, 3), new P2(0, 3)),
            };
            var runs = SegmentChain.Join(segs, 1e-6);
            Assert.Single(runs);
            Assert.True(runs[0].Closed);
            Assert.Equal(4, runs[0].Points.Count);
        }

        [Fact]
        public void KeepsSeparateRunsApartAndOpenRunsOpen()
        {
            var segs = new List<Seg>
            {
                new Seg(new P2(0, 0), new P2(1, 0)),
                new Seg(new P2(1, 0), new P2(1, 1)),
                new Seg(new P2(10, 10), new P2(11, 10)),
            };
            var runs = SegmentChain.Join(segs, 1e-6);
            Assert.Equal(2, runs.Count);
            Assert.All(runs, r => Assert.False(r.Closed));
            Assert.Contains(runs, r => r.Points.Count == 3);
        }

        [Fact]
        public void JoinsFromTheMiddle()
        {
            // The first segment listed is the middle of the run; the chain grows both ways.
            var segs = new List<Seg>
            {
                new Seg(new P2(1, 0), new P2(2, 0)),
                new Seg(new P2(0, 0), new P2(1, 0)),
                new Seg(new P2(2, 0), new P2(3, 0)),
            };
            var runs = SegmentChain.Join(segs, 1e-6);
            Assert.Single(runs);
            Assert.Equal(4, runs[0].Points.Count);
        }
    }
}
