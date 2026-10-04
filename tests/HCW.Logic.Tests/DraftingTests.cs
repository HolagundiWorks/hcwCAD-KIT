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

namespace HCW.Logic.Tests
{
    public class ColumnLogicTests
    {
        [Theory]
        [InlineData("230x450", false, 230, 450, "230x450")]
        [InlineData("230 X 450", false, 230, 450, "230x450")]
        [InlineData("230*450", false, 230, 450, "230x450")]
        [InlineData("300", false, 300, 300, "300x300")]
        [InlineData("D450", true, 450, 450, "Ø450")]
        [InlineData("dia 450", true, 450, 450, "Ø450")]
        [InlineData("%%c450", true, 450, 450, "Ø450")]
        [InlineData("Ø300", true, 300, 300, "Ø300")]
        [InlineData("225.5x300", false, 225.5, 300, "225.5x300")]
        public void ParsesSizes(string text, bool round, double w, double d, string label)
        {
            string err;
            var s = ColumnSize.Parse(text, out err);
            Assert.NotNull(s);
            Assert.Equal(round, s.Round);
            Assert.Equal(w, s.W);
            Assert.Equal(d, s.D);
            Assert.Equal(label, s.Label);
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("0x300")]
        [InlineData("230x")]
        [InlineData("-5")]
        [InlineData("D")]
        public void RejectsBadSizes(string text)
        {
            string err;
            Assert.Null(ColumnSize.Parse(text, out err));
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void AreaOfRectAndCircle()
        {
            string err;
            Assert.Equal(230.0 * 450, ColumnSize.Parse("230x450", out err).Area);
            Assert.Equal(Math.PI * 100 * 100, ColumnSize.Parse("D200", out err).Area, 6);
        }

        private static List<Seg> Grid(int cols, int rows, double bay)
        {
            var segs = new List<Seg>();
            for (int c = 0; c < cols; c++) segs.Add(new Seg(new P2(c * bay, -1), new P2(c * bay, (rows - 1) * bay + 1)));
            for (int r = 0; r < rows; r++) segs.Add(new Seg(new P2(-1, r * bay), new P2((cols - 1) * bay + 1, r * bay)));
            return segs;
        }

        [Fact]
        public void GridGivesOnePointPerCrossing()
        {
            var pts = GridIntersections.Find(Grid(4, 3, 4.0), 1e-6);
            Assert.Equal(12, pts.Count);
            Assert.All(pts, p => Assert.Equal(0.0, p.Angle, 9));
            Assert.Contains(pts, p => Math.Abs(p.Pt.X - 12) < 1e-9 && Math.Abs(p.Pt.Y - 8) < 1e-9);
        }

        [Fact]
        public void LinesThatDoNotReachEachOtherDoNotCross()
        {
            var vertical = new Seg(new P2(0, 0), new P2(0, 4));
            var shortRight = new Seg(new P2(2, 2), new P2(6, 2));     // starts to the right of the vertical line
            var parallel = new Seg(new P2(10, 0), new P2(10, 4));     // parallel to the vertical line
            Assert.Empty(GridIntersections.Find(new List<Seg> { vertical, shortRight }, 1e-6));
            Assert.Empty(GridIntersections.Find(new List<Seg> { vertical, parallel }, 1e-6));
            Assert.Single(GridIntersections.Find(new List<Seg> { parallel, new Seg(new P2(8, 2), new P2(12, 2)) }, 1e-6));
        }

        [Fact]
        public void TiltedGridKeepsTheColumnAlongTheGrid()
        {
            double a = 30 * Math.PI / 180;
            var u = new P2(Math.Cos(a), Math.Sin(a)); var n = new P2(-u.Y, u.X);
            var segs = new List<Seg> { new Seg(u * -5, u * 5), new Seg(n * -5, n * 5) };
            var pts = GridIntersections.Find(segs, 1e-6);
            Assert.Single(pts);
            Assert.Equal(30 * Math.PI / 180, pts[0].Angle, 9);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(90, 0)]
        [InlineData(45, 45)]
        [InlineData(50, -40)]
        [InlineData(-30, -30)]
        [InlineData(180, 0)]
        public void FoldKeepsAnglesWithin45(double deg, double want) =>
            Assert.Equal(want, GridIntersections.Fold(deg * Math.PI / 180) * 180 / Math.PI, 6);

        [Fact]
        public void MarksGoToTheBiggestSectionFirst()
        {
            string err;
            var sizes = new[] { "230x450", "300x300", "230x450", "D400", "230x450", "300x300" }
                .Select(t => ColumnSize.Parse(t, out err)).ToList();
            var rows = ColumnMarks.Assign(sizes);
            Assert.Equal(new[] { "C1", "C2", "C3" }, rows.Select(r => r.Mark).ToArray());
            Assert.Equal("Ø400", rows[0].Label);             // area 125,664 beats 103,500 and 90,000
            Assert.Equal("230x450", rows[1].Label);
            Assert.Equal(new[] { 1, 3, 2 }, rows.Select(r => r.Count).ToArray());
        }

        [Fact]
        public void SizesAreRoundedBeforeTheyAreGrouped()
        {
            var rows = ColumnMarks.Assign(new[]
            {
                new ColumnSize { W = 229.8, D = 450.2 }, new ColumnSize { W = 230.2, D = 449.9 },
            });
            Assert.Single(rows);
            Assert.Equal(2, rows[0].Count);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class OpeningFrameTests
    {
        private static bool Same(P2 a, P2 b) => a.DistanceTo(b) < 1e-9;
        private static bool SameSet(P2 a1, P2 a2, P2 b1, P2 b2) => (Same(a1, b1) && Same(a2, b2)) || (Same(a1, b2) && Same(a2, b1));

        // A 0.23 thick wall along a direction at the given angle; faces drawn in opposite directions like a closed outline.
        private static List<Seg> Wall(double deg)
        {
            double a = deg * Math.PI / 180;
            var u = new P2(Math.Cos(a), Math.Sin(a)); var n = new P2(-u.Y, u.X);
            var o = new P2(3, 2);
            return new List<Seg>
            {
                new Seg(o, o + u * 5),
                new Seg(o + u * 5 + n * 0.23, o + n * 0.23),
            };
        }

        [Theory]
        [InlineData(true, 0, 0.1, 5, false)]
        [InlineData(true, 0, 0.1, -5, false)]
        [InlineData(true, 0, 0.1, 5, true)]
        [InlineData(true, 30, 0.1, -5, true)]
        [InlineData(true, 200, 0.23, 5, false)]
        [InlineData(false, 0, 0.1, 5, false)]
        [InlineData(false, 125, 0.23, -5, false)]
        public void BlockInsertionGivesBackTheCutCorners(bool door, double deg, double pickAlongNormal, double sideAlongNormal, bool flip)
        {
            double a = deg * Math.PI / 180;
            var u = new P2(Math.Cos(a), Math.Sin(a)); var n = new P2(-u.Y, u.X);
            var segs = Wall(deg);
            var pick = new P2(3, 2) + u * 2.5 + n * pickAlongNormal;
            string err;
            var plan = OpeningCut.Plan(segs, pick, 0.9, 0.05, 0.6, out err);
            Assert.NotNull(plan);

            var side = pick + n * sideAlongNormal;
            var pl = OpeningFrame.Compute(door, plan, side, flip);
            var c = OpeningFrame.FromBlock(door, pl.Origin, pl.Angle, pl.Sx, pl.Sy, 0.9, plan.Thickness);

            // Both faces' corners come back, in either order.
            Assert.True(SameSet(c.FaceAStart, c.FaceAEnd, plan.P1a, plan.P1b) && SameSet(c.FaceBStart, c.FaceBEnd, plan.P2a, plan.P2b)
                     || SameSet(c.FaceAStart, c.FaceAEnd, plan.P2a, plan.P2b) && SameSet(c.FaceBStart, c.FaceBEnd, plan.P1a, plan.P1b));
            Assert.True(Same(c.Centre, (plan.P1a + plan.P1b) * 0.5) || Same(c.Centre, (plan.P2a + plan.P2b) * 0.5));
            Assert.Equal(flip && door, c.Flipped);
            if (door)
            {
                // The leaf swings to the side that was picked, away from the wall body.
                Assert.True(P2.Dot(c.Swing, n * sideAlongNormal) > 0);
                Assert.Equal(1.0, c.Swing.Length, 9);
            }
            else Assert.Equal(0.0, c.Swing.Length, 9);
        }

        [Theory]
        [InlineData("HCW_D_900x230", true, 900, 230)]
        [InlineData("hcw_w_1200x115", false, 1200, 115)]
        [InlineData("HCW_W_1200.5x230", false, 1200.5, 230)]
        public void ParsesBlockNames(string name, bool door, double w, double t)
        {
            bool d; double ww, tt;
            Assert.True(OpeningFrame.TryParseName(name, out d, out ww, out tt));
            Assert.Equal(door, d); Assert.Equal(w, ww); Assert.Equal(t, tt);
        }

        [Theory]
        [InlineData("")]
        [InlineData("DOOR")]
        [InlineData("HCW_D_900")]
        [InlineData("HCW_X_900x230")]
        [InlineData("HCW_D_900x230_2")]
        public void RejectsOtherNames(string name)
        {
            bool d; double w, t;
            Assert.False(OpeningFrame.TryParseName(name, out d, out w, out t));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class SymbolMathTests
    {
        [Theory]
        [InlineData(0, "±0.000")]
        [InlineData(0.0004, "±0.000")]
        [InlineData(3.15, "+3.150")]
        [InlineData(-0.45, "-0.450")]
        [InlineData(0.0005, "+0.001")]
        [InlineData(-12.3456, "-12.346")]
        public void LevelText(double v, string want) => Assert.Equal(want, SymbolMath.LevelText(v));

        [Fact]
        public void LevelFromDatum()
        {
            // metre drawing: 1 unit per metre
            Assert.Equal(3.15, SymbolMath.LevelFromDatum(0, 10, 13.15, 1), 9);
            // millimetre drawing: 1000 units per metre; datum +1.200 at y 5000, point 450 mm lower
            Assert.Equal(0.75, SymbolMath.LevelFromDatum(1.2, 5000, 4550, 1000), 9);
            Assert.Throws<ArgumentOutOfRangeException>(() => SymbolMath.LevelFromDatum(0, 0, 0, 0));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(45, 45)]
        [InlineData(90, 90)]
        [InlineData(135, -45)]
        [InlineData(180, 0)]
        [InlineData(-90, 90)]
        [InlineData(-135, 45)]
        [InlineData(270, 90)]
        [InlineData(360, 0)]
        public void ReadableAngle(double deg, double want) =>
            Assert.Equal(want, SymbolMath.ReadableAngle(deg * Math.PI / 180) * 180 / Math.PI, 6);

        [Fact]
        public void LookNormalPointsToTheChosenSide()
        {
            var a = new P2(0, 0); var b = new P2(4, 0);
            var up = SymbolMath.LookNormal(a, b, new P2(2, 3));
            var down = SymbolMath.LookNormal(a, b, new P2(2, -3));
            Assert.Equal(1.0, up.Y, 9); Assert.Equal(0.0, up.X, 9);
            Assert.Equal(-1.0, down.Y, 9);
            // On the line: the left normal.
            Assert.Equal(1.0, SymbolMath.LookNormal(a, b, new P2(2, 0)).Y, 9);
            // Degenerate line.
            Assert.Equal(1.0, SymbolMath.LookNormal(a, a, new P2(1, 1)).Y, 9);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class RailLayoutTests
    {
        [Fact]
        public void ExactMultipleGivesPostsAtTheSpacing()
        {
            var posts = RailLayout.PostPoints(new[] { new P2(0, 0), new P2(2.4, 0) }, false, 1.2);
            Assert.Equal(new[] { 0.0, 1.2, 2.4 }, posts.Select(p => p.X).ToArray());
        }

        [Fact]
        public void AnInexactRunIsDividedEqually()
        {
            // 2.5 m with posts no further than 1.2 m: three equal bays of 0.8333 m
            var posts = RailLayout.PostPoints(new[] { new P2(0, 0), new P2(2.5, 0) }, false, 1.2);
            Assert.Equal(4, posts.Count);
            for (int i = 1; i < posts.Count; i++)
                Assert.Equal(2.5 / 3, posts[i].DistanceTo(posts[i - 1]), 9);
            Assert.Equal(2.5, posts[3].X, 9);
        }

        [Fact]
        public void CornersAlwaysGetAPost()
        {
            var posts = RailLayout.PostPoints(new[] { new P2(0, 0), new P2(1, 0), new P2(1, 1) }, false, 5);
            Assert.Equal(3, posts.Count);
            Assert.Contains(posts, p => p.DistanceTo(new P2(1, 0)) < 1e-9);
        }

        [Fact]
        public void AClosedRunHasNoEndPost()
        {
            var sq = new[] { new P2(0, 0), new P2(4, 0), new P2(4, 4), new P2(0, 4) };
            var posts = RailLayout.PostPoints(sq, true, 2);
            Assert.Equal(8, posts.Count);                    // 2 per side
            Assert.Equal(8, posts.Select(p => Math.Round(p.X, 6) + "," + Math.Round(p.Y, 6)).Distinct().Count());
        }

        [Fact]
        public void EdgeCases()
        {
            Assert.Empty(RailLayout.PostPoints(new P2[0], false, 1));
            Assert.Single(RailLayout.PostPoints(new[] { new P2(1, 1) }, false, 1));
            Assert.Equal(2, RailLayout.PostPoints(new[] { new P2(0, 0), new P2(3, 0) }, false, 0).Count);   // no spacing: ends only
            Assert.Equal(2, RailLayout.PostPoints(new[] { new P2(0, 0), new P2(0, 0), new P2(3, 0) }, false, 5).Count);  // repeated point ignored
        }
    }
}
