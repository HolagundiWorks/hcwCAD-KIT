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

namespace HCW.Logic.Tests
{
    public class AreaStatementTests
    {
        private static List<FloorInput> Floors() => new List<FloorInput>
        {
            new FloorInput { Name = "GROUND", Gross = 100, Deduction = 10 },
            new FloorInput { Name = "FIRST", Gross = 90, Deduction = 12.5 },
        };

        [Fact]
        public void NetTotalsFarAndGroundCover()
        {
            var s = AreaStatement.Compute(Floors(), 200);
            Assert.Equal(90, s.Floors[0].Net);
            Assert.Equal(77.5, s.Floors[1].Net);
            Assert.Equal(190, s.TotalGross);
            Assert.Equal(22.5, s.TotalDeduction);
            Assert.Equal(167.5, s.TotalNet);
            Assert.Equal(167.5 / 200 * 100, s.FarPercent.Value, 9);
            Assert.Equal(100, s.GroundCover);
            Assert.Equal(50, s.GroundCoverPercent.Value, 9);
            Assert.Empty(s.Warnings);
        }

        [Fact]
        public void FieldsAreFormattedAndUnusedFloorsAreDashed()
        {
            var f = AreaStatement.Compute(Floors(), 200).ToFields();
            Assert.Equal("GROUND", f["FL1"]);
            Assert.Equal("100.00", f["GROSS1"]);
            Assert.Equal("12.50", f["DED2"]);
            Assert.Equal("77.50", f["NET2"]);
            Assert.Equal("--", f["FL3"]);
            Assert.Equal("--", f["NET4"]);
            Assert.Equal("167.50", f["TOT_NET"]);
            Assert.Equal("200.00", f["SITE_AREA"]);
            Assert.Equal("83.75", f["FAR_ACH"]);
            Assert.Equal("100.00", f["GC_ACH"]);
            Assert.Equal("50.00", f["GC_PCT"]);
        }

        [Fact]
        public void NoSiteAreaLeavesRatiosOut()
        {
            var s = AreaStatement.Compute(Floors(), 0);
            Assert.Null(s.FarPercent);
            Assert.Null(s.GroundCoverPercent);
            Assert.Single(s.Warnings);
            var f = s.ToFields();
            Assert.Equal("--", f["FAR_ACH"]);
            Assert.Equal("--", f["SITE_AREA"]);
            Assert.Equal("190.00", f["TOT_GROSS"]);
        }

        [Fact]
        public void DeductionsBeyondTheFloorAreWarned()
        {
            var s = AreaStatement.Compute(new[] { new FloorInput { Name = "TERRACE", Gross = 5, Deduction = 8 } }, 100);
            Assert.Equal(0, s.Floors[0].Net);
            Assert.Contains(s.Warnings, w => w.Contains("TERRACE"));
        }

        [Fact]
        public void MoreFloorsThanSlotsStillCountInTotals()
        {
            var floors = Enumerable.Range(1, 6).Select(i => new FloorInput { Name = "F" + i, Gross = 10, Deduction = 0 }).ToList();
            var s = AreaStatement.Compute(floors, 100);
            var f = s.ToFields(4);
            Assert.Equal(60, s.TotalGross);
            Assert.Equal("60.00", f["TOT_GROSS"]);
            Assert.False(f.ContainsKey("FL5"));
        }

        [Fact]
        public void GroundCoverOverTheSiteIsWarned()
        {
            var s = AreaStatement.Compute(new[] { new FloorInput { Name = "G", Gross = 150, Deduction = 0 } }, 100);
            Assert.Contains(s.Warnings, w => w.Contains("Ground cover"));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LiftLayoutTests
    {
        private static double Area(IList<P2> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[(i + 1) % p.Count]; a += p[i].X * q.Y - q.X * p[i].Y; }
            return Math.Abs(a) / 2;
        }

        private static LiftLayout Ok()
        {
            string err;
            var l = LiftLayout.Build(1800, 2000, 230, 1100, 1400, 800, 30, out err);
            Assert.Null(err);
            Assert.NotNull(l);
            return l;
        }

        [Fact]
        public void WallRingAreaIsTheRingLessTheDoorGap()
        {
            var l = Ok();
            double outer = (1800 + 460.0) * (2000 + 460.0);
            double inner = 1800.0 * 2000;
            double gap = 800 * 230;
            Assert.Equal(12, l.WallRing.Count);
            Assert.Equal(outer - inner - gap, Area(l.WallRing), 6);
        }

        [Fact]
        public void ClearShaftAndCarGeometry()
        {
            var l = Ok();
            Assert.Equal(1800.0 * 2000, Area(l.Clear), 6);
            Assert.Equal(1100.0 * 1400, Area(l.Car), 6);
            Assert.Equal(-1000 + 30, l.Car.Min(p => p.Y), 9);          // car front sits 30 mm back from the front wall's inner face
            Assert.Equal(0.0, l.Car.Average(p => p.X), 9);              // centred
            Assert.Equal(800.0, l.LandingDoorA.DistanceTo(l.LandingDoorB), 9);
            Assert.Equal(l.Car.Min(p => p.Y), l.CarDoorA.Y, 9);
        }

        [Fact]
        public void DoorOpeningIsCentredInTheFrontWall()
        {
            var l = Ok();
            Assert.Contains(l.WallRing, p => Math.Abs(p.X + 400) < 1e-9 && Math.Abs(p.Y + 1230) < 1e-9);
            Assert.Contains(l.WallRing, p => Math.Abs(p.X - 400) < 1e-9 && Math.Abs(p.Y + 1000) < 1e-9);
        }

        [Theory]
        [InlineData(1800, 2000, 230, 1800, 1400, 800, "narrower")]
        [InlineData(1800, 2000, 230, 1100, 1980, 800, "depth")]
        [InlineData(1800, 2000, 230, 1100, 1400, 1200, "wider than the car")]
        [InlineData(1800, 2000, 0, 1100, 1400, 800, "wall thickness")]
        [InlineData(1300, 2000, 230, 1250, 1400, 1150, "100 mm")]
        [InlineData(1800, 0, 230, 1100, 1400, 800, "more than 0")]
        public void RefusesSizesThatDoNotWork(double cw, double cd, double wall, double carW, double carD, double door, string reason)
        {
            string err;
            Assert.Null(LiftLayout.Build(cw, cd, wall, carW, carD, door, 30, out err));
            Assert.Contains(reason, err);
        }

        [Theory]
        [InlineData("1800x2000", 1800, 2000)]
        [InlineData("1800 X 2000", 1800, 2000)]
        [InlineData("1800*2000", 1800, 2000)]
        [InlineData("1550.5x1900", 1550.5, 1900)]
        public void ParsesSizes(string text, double w, double d)
        {
            double ww, dd; string err;
            Assert.True(LiftLayout.ParseSize(text, out ww, out dd, out err));
            Assert.Equal(w, ww); Assert.Equal(d, dd);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1800")]
        [InlineData("x2000")]
        [InlineData("1800x")]
        [InlineData("0x2000")]
        [InlineData("axb")]
        public void RejectsBadSizes(string text)
        {
            double w, d; string err;
            Assert.False(LiftLayout.ParseSize(text, out w, out d, out err));
            Assert.False(string.IsNullOrEmpty(err));
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LineCleanupTests
    {
        private static CleanLine L(double x1, double y1, double x2, double y2, string key = "A-WALL") =>
            new CleanLine { Seg = new Seg(new P2(x1, y1), new P2(x2, y2)), Key = key };

        [Fact]
        public void ZeroLengthLinesGo()
        {
            var r = LineCleanup.Run(new[] { L(1, 1, 1, 1), L(0, 0, 5, 0) }, 1e-6, true);
            Assert.Equal(new[] { 0 }, r.Erase.ToArray());
            Assert.Equal(1, r.ZeroLength);
            Assert.Empty(r.Replace);
        }

        [Fact]
        public void DuplicatesEitherWayRoundGo()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 5, 0), L(5, 0, 0, 0), L(0, 0, 5, 0) }, 1e-6, true);
            Assert.Equal(new[] { 1, 2 }, r.Erase.ToArray());
            Assert.Equal(2, r.Duplicates);
            Assert.Equal(0, r.Merged);
            Assert.Empty(r.Replace);
        }

        [Fact]
        public void TouchingCollinearLinesJoin()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3, 0, 8, 0), L(8, 0, 10, 0) }, 1e-6, true);
            Assert.Equal(new[] { 1, 2 }, r.Erase.ToArray());
            Assert.Equal(2, r.Merged);
            var merged = r.Replace[0];
            Assert.Equal(0.0, Math.Min(merged.A.X, merged.B.X), 9);
            Assert.Equal(10.0, Math.Max(merged.A.X, merged.B.X), 9);
        }

        [Fact]
        public void OverlappingLinesJoinAndAnInnerOneIsADuplicate()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 6, 0), L(4, 0, 9, 0), L(1, 0, 2, 0) }, 1e-6, true);
            Assert.Equal(new[] { 1, 2 }, r.Erase.ToArray());
            Assert.Equal(1, r.Merged);          // the 4..9 line extends the first
            Assert.Equal(1, r.Duplicates);      // the 1..2 line lies inside it
            Assert.Equal(9.0, Math.Max(r.Replace[0].A.X, r.Replace[0].B.X), 9);
        }

        [Fact]
        public void LinesAGapApartStaySeparate()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3.5, 0, 8, 0) }, 0.1, true);
            Assert.Empty(r.Erase);
            Assert.Empty(r.Replace);
            // ...unless the gap is within tolerance.
            var r2 = LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3.05, 0, 8, 0) }, 0.1, true);
            Assert.Single(r2.Erase);
        }

        [Fact]
        public void DifferentKeysOrOffsetsOrDirectionsDoNotJoin()
        {
            Assert.Empty(LineCleanup.Run(new[] { L(0, 0, 3, 0, "A"), L(3, 0, 8, 0, "B") }, 1e-6, true).Erase);
            Assert.Empty(LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3, 0.5, 8, 0.5) }, 1e-6, true).Erase);        // parallel, offset
            Assert.Empty(LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3, 0, 3, 5) }, 1e-6, true).Erase);            // at right angles
        }

        [Fact]
        public void DiagonalLinesJoinToo()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 2, 2), L(2, 2, 5, 5), L(7, 7, 5, 5) }, 1e-6, true);
            Assert.Equal(new[] { 1, 2 }, r.Erase.ToArray());
            var m = r.Replace[0];
            Assert.Equal(7.0, Math.Max(m.A.X, m.B.X), 9);
            Assert.Equal(0.0, Math.Min(m.A.X, m.B.X), 9);
        }

        [Fact]
        public void KeeperIsTheLowestNumberedLine()
        {
            // The line listed first is in the middle of the run; it should keep its identity.
            var r = LineCleanup.Run(new[] { L(3, 0, 6, 0), L(0, 0, 3, 0), L(6, 0, 9, 0) }, 1e-6, true);
            Assert.Equal(new[] { 1, 2 }, r.Erase.ToArray());
            Assert.True(r.Replace.ContainsKey(0));
            Assert.Equal(9.0, Math.Max(r.Replace[0].A.X, r.Replace[0].B.X), 9);
            Assert.Equal(0.0, Math.Min(r.Replace[0].A.X, r.Replace[0].B.X), 9);
        }

        [Fact]
        public void DuplicatesOnlyModeLeavesTouchingLinesAlone()
        {
            var r = LineCleanup.Run(new[] { L(0, 0, 3, 0), L(3, 0, 8, 0), L(8, 0, 3, 0) }, 1e-6, false);
            Assert.Equal(new[] { 2 }, r.Erase.ToArray());
            Assert.Equal(1, r.Duplicates);
            Assert.Empty(r.Replace);
        }

        [Fact]
        public void DirectionNearZeroAndPiAreTheSameLine()
        {
            // 0 and almost 180 degrees: the same straight line drawn in opposite directions with a hair of tilt.
            var r = LineCleanup.Run(new[] { L(0, 0, 4, 0), L(8, 1e-12, 4, 0) }, 1e-6, true);
            Assert.Single(r.Erase);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class PlanarRoomsTests
    {
        private static IEnumerable<Seg> Rect(double x0, double y0, double x1, double y1)
        {
            yield return new Seg(new P2(x0, y0), new P2(x1, y0));
            yield return new Seg(new P2(x1, y0), new P2(x1, y1));
            yield return new Seg(new P2(x1, y1), new P2(x0, y1));
            yield return new Seg(new P2(x0, y1), new P2(x0, y0));
        }

        private static double Area(List<P2> p) => Math.Abs(PlanarRooms.SignedArea(p));

        [Fact]
        public void FindsASimpleRoom()
        {
            string err;
            var room = PlanarRooms.Find(Rect(0, 0, 4, 3).ToList(), new P2(2, 1.5), 1e-6, out err);
            Assert.NotNull(room);
            Assert.Equal(4, room.Count);
            Assert.Equal(12.0, Area(room), 9);
            Assert.True(PlanarRooms.SignedArea(room) > 0);     // counter-clockwise
        }

        [Fact]
        public void PointOutsideEverythingFindsNothing()
        {
            string err;
            Assert.Null(PlanarRooms.Find(Rect(0, 0, 4, 3).ToList(), new P2(10, 10), 1e-6, out err));
            Assert.Contains("not inside", err);
            Assert.Null(PlanarRooms.Find(new List<Seg>(), new P2(0, 0), 1e-6, out err));
        }

        [Fact]
        public void RoomInsideATwoLoopWallRingIsTheInnerLoop()
        {
            // Outer face 0..4.46 x 0..3.46, inner face 0.23..4.23 x 0.23..3.23: the room is the inner loop, not the wall body.
            var segs = Rect(0, 0, 4.46, 3.46).Concat(Rect(0.23, 0.23, 4.23, 3.23)).ToList();
            string err;
            var room = PlanarRooms.Find(segs, new P2(2, 2), 1e-6, out err);
            Assert.Equal(4.0 * 3.0, Area(room), 9);
            // A point in the wall body itself finds the body's outer loop (the smallest closed region holding it).
            var body = PlanarRooms.Find(segs, new P2(0.1, 0.1), 1e-6, out err);
            Assert.Equal(4.46 * 3.46, Area(body), 9);
        }

        [Fact]
        public void TwoRoomsShareAWallAndCrossingsAreCut()
        {
            // A 8 x 4 box with a partition line across the middle that runs the full height and overshoots both ends.
            var segs = Rect(0, 0, 8, 4).ToList();
            segs.Add(new Seg(new P2(4, -1), new P2(4, 5)));
            string err;
            var left = PlanarRooms.Find(segs, new P2(2, 2), 1e-6, out err);
            var right = PlanarRooms.Find(segs, new P2(6, 2), 1e-6, out err);
            Assert.Equal(16.0, Area(left), 9);
            Assert.Equal(16.0, Area(right), 9);
        }

        [Fact]
        public void CrossedLinesMakeFourRooms()
        {
            var segs = Rect(0, 0, 6, 6).ToList();
            segs.Add(new Seg(new P2(3, 0), new P2(3, 6)));
            segs.Add(new Seg(new P2(0, 3), new P2(6, 3)));
            string err;
            foreach (var p in new[] { new P2(1, 1), new P2(5, 1), new P2(1, 5), new P2(5, 5) })
                Assert.Equal(9.0, Area(PlanarRooms.Find(segs, p, 1e-6, out err)), 9);
        }

        [Fact]
        public void LShapedRoomKeepsItsCorner()
        {
            var l = new[] { new P2(0, 0), new P2(6, 0), new P2(6, 2), new P2(2, 2), new P2(2, 5), new P2(0, 5) };
            var segs = l.Select((p, i) => new Seg(p, l[(i + 1) % l.Length])).ToList();
            string err;
            var room = PlanarRooms.Find(segs, new P2(1, 1), 1e-6, out err);
            Assert.Equal(6.0 * 2 + 2.0 * 3, Area(room), 9);
            Assert.Equal(6, room.Count);
        }

        [Fact]
        public void SpursInsideTheRoomAreIgnored()
        {
            var segs = Rect(0, 0, 4, 3).ToList();
            segs.Add(new Seg(new P2(0, 1.5), new P2(2, 1.5)));       // a stub from the wall into the room
            string err;
            var room = PlanarRooms.Find(segs, new P2(3, 1), 1e-6, out err);
            Assert.Equal(12.0, Area(room), 9);
            Assert.Equal(4, room.Count);                              // the stub's point is simplified away
        }

        [Fact]
        public void EndsThatOnlyJustMissAreJoinedWithinTolerance()
        {
            var segs = new List<Seg>
            {
                new Seg(new P2(0, 0), new P2(4, 0)),
                new Seg(new P2(4.0005, 0), new P2(4, 3)),     // starts 0.5 mm off
                new Seg(new P2(4, 3), new P2(0, 3)),
                new Seg(new P2(0, 3), new P2(0, 0)),
            };
            string err;
            Assert.NotNull(PlanarRooms.Find(segs, new P2(2, 1), 0.001, out err));
            Assert.Null(PlanarRooms.Find(segs, new P2(2, 1), 0.0001, out err));    // tighter than the miss: the room is open
        }

        [Fact]
        public void AnOpenRoomFallsIntoTheRegionAroundIt()
        {
            // The partition stops short of the north wall: the two halves are one room.
            var segs = Rect(0, 0, 8, 4).ToList();
            segs.Add(new Seg(new P2(4, 0), new P2(4, 3)));
            string err;
            var room = PlanarRooms.Find(segs, new P2(2, 2), 1e-6, out err);
            Assert.Equal(32.0, Area(room), 9);
        }

        [Fact]
        public void CollinearOverlappingFacesDoNotBreakIt()
        {
            // The south wall drawn as two overlapping lines, plus a duplicate of the east wall.
            var segs = new List<Seg>
            {
                new Seg(new P2(0, 0), new P2(3, 0)), new Seg(new P2(2, 0), new P2(4, 0)),
                new Seg(new P2(4, 0), new P2(4, 3)), new Seg(new P2(4, 3), new P2(4, 0)),
                new Seg(new P2(4, 3), new P2(0, 3)), new Seg(new P2(0, 3), new P2(0, 0)),
            };
            string err;
            var room = PlanarRooms.Find(segs, new P2(2, 1), 1e-6, out err);
            Assert.Equal(12.0, Area(room), 9);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class CentroidTests
    {
        [Fact]
        public void CentroidOfARectangleAndAnL()
        {
            var r = PlanarRooms.Centroid(new[] { new P2(0, 0), new P2(4, 0), new P2(4, 2), new P2(0, 2) });
            Assert.Equal(2.0, r.X, 9); Assert.Equal(1.0, r.Y, 9);
            // L: 6x2 bar plus 2x3 block above its left end; centroid by area weighting
            var l = PlanarRooms.Centroid(new[] { new P2(0, 0), new P2(6, 0), new P2(6, 2), new P2(2, 2), new P2(2, 5), new P2(0, 5) });
            double a1 = 12, a2 = 6;
            Assert.Equal((a1 * 3 + a2 * 1) / (a1 + a2), l.X, 9);
            Assert.Equal((a1 * 1 + a2 * 3.5) / (a1 + a2), l.Y, 9);
            // No area: average of the corners.
            var line = PlanarRooms.Centroid(new[] { new P2(0, 0), new P2(2, 0), new P2(4, 0) });
            Assert.Equal(2.0, line.X, 9);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ElectricalLoadTests
    {
        private static ElLink Link(string board, string point, string code) => new ElLink { Board = board, Point = point, Code = code };
        private static readonly Dictionary<string, double> Watts = ElectricalLoad.ParseWatts(ElectricalLoad.DefaultWatts);
        private static readonly HashSet<string> Dedicated = ElectricalLoad.ParseCodes(ElectricalLoad.DefaultDedicated);

        [Fact]
        public void ParsesWattsAndCodes()
        {
            var w = ElectricalLoad.ParseWatts("LP=20; fp=75 , XX=5 ; P5=abc; P15=-3; AC=1800");
            Assert.Equal(20, w["LP"]);
            Assert.Equal(75, w["FP"]);
            Assert.Equal(1800, w["AC"]);
            Assert.False(w.ContainsKey("XX"));
            Assert.False(w.ContainsKey("P5"));
            Assert.False(w.ContainsKey("P15"));
            Assert.Equal(3, ElectricalLoad.ParseCodes("ac; GY ,oven,SB,OV").Count);     // AC, GY, OV; "oven" and the board are ignored
        }

        [Fact]
        public void LoadAndCircuitsPerBoard()
        {
            var links = new List<ElLink>
            {
                Link("SB-01", "LP-01", "LP"), Link("SB-01", "LP-02", "LP"), Link("SB-01", "FP-01", "FP"), Link("SB-01", "SW1-01", "SW1"),
                Link("SB-01", "P5-01", "P5"), Link("SB-01", "AC-01", "AC"), Link("SB-01", "GY-01", "GY"),
                Link("SB-02", "LP-03", "LP"),
            };
            LoadRow total;
            var rows = ElectricalLoad.Build(new[] { "SB-02", "SB-01" }, links, Watts, 1000, 3000, Dedicated, out total);
            Assert.Equal(new[] { "SB-01", "SB-02" }, rows.Select(r => r.Board).ToArray());

            var a = rows[0];
            Assert.Equal(3, a.LightingPoints);                    // 2 lights + 1 fan; the switch has no load
            Assert.Equal(15 + 15 + 60, a.LightingWatts);
            Assert.Equal(3, a.PowerPoints);
            Assert.Equal(100 + 1500 + 2000, a.PowerWatts);
            Assert.Equal(1, a.LightingCircuits);
            Assert.Equal(2 + 1, a.PowerCircuits);                 // AC and geyser dedicated, the socket on a shared circuit
            Assert.Equal(90 + 3600, a.TotalWatts);

            Assert.Equal(1, rows[1].LightingPoints);
            Assert.Equal(0, rows[1].PowerCircuits);
            Assert.Equal(4, total.LightingPoints);
            Assert.Equal(105 + 3600, total.TotalWatts);
            Assert.Equal(1 + 1, total.LightingCircuits);
            Assert.Equal(3, total.PowerCircuits);
        }

        [Fact]
        public void APointOnTwoBoardsCountsOnEachButOnceInTheTotal()
        {
            var links = new List<ElLink> { Link("SB-01", "LP-01", "LP"), Link("SB-02", "LP-01", "LP") };
            LoadRow total;
            var rows = ElectricalLoad.Build(new[] { "SB-01", "SB-02" }, links, Watts, 1000, 3000, Dedicated, out total);
            Assert.Equal(15, rows[0].LightingWatts);
            Assert.Equal(15, rows[1].LightingWatts);
            Assert.Equal(15, total.LightingWatts);
            Assert.Equal(1, total.LightingPoints);
        }

        [Fact]
        public void CircuitsRoundUpAndZeroLimitMeansOnePerGroup()
        {
            var many = Enumerable.Range(1, 30).Select(i => Link("SB-01", "FP-" + i, "FP")).ToList();   // 30 x 60 W = 1800 W
            LoadRow total;
            Assert.Equal(2, ElectricalLoad.Build(new[] { "SB-01" }, many, Watts, 1000, 3000, Dedicated, out total)[0].LightingCircuits);
            Assert.Equal(1, ElectricalLoad.Build(new[] { "SB-01" }, many, Watts, 0, 3000, Dedicated, out total)[0].LightingCircuits);
            // exactly on the limit is one circuit
            var ten = Enumerable.Range(1, 10).Select(i => Link("SB-01", "x" + i, "FP")).ToList();
            Assert.Equal(1, ElectricalLoad.Build(new[] { "SB-01" }, ten, new Dictionary<string, double> { { "FP", 100 } }, 1000, 3000, null, out total)[0].LightingCircuits);
        }

        [Fact]
        public void BoardsWithNothingWiredAreStillListed()
        {
            LoadRow total;
            var rows = ElectricalLoad.Build(new[] { "SB-01" }, new ElLink[0], Watts, 1000, 3000, Dedicated, out total);
            Assert.Single(rows);
            var cells = ElectricalLoad.ToCells(rows[0]);
            Assert.Equal("SB-01", cells[0]);
            Assert.All(cells.Skip(1), c => Assert.Equal("", c));
            Assert.Equal(ElectricalLoad.Header.Length, cells.Length);
        }

        [Fact]
        public void CellsAreFormatted()
        {
            var r = new LoadRow { Board = "SB-01", LightingPoints = 3, LightingWatts = 90, PowerPoints = 2, PowerWatts = 3500, LightingCircuits = 1, PowerCircuits = 2, DemandWatts = 2300 };
            var c = ElectricalLoad.ToCells(r);
            Assert.Equal(new[] { "SB-01", "3", "90", "2", "3500", "3590", "3.59", "2.30", "1+2 = 3" }, c);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class CableLengthTests
    {
        private static ElWire Wire(double x1, double y1, double x2, double y2) =>
            new ElWire { Points = new List<PlanPoint> { new PlanPoint(x1, y1), new PlanPoint(x2, y2) } };

        private static ElNode Board(string id) => new ElNode { Id = id, Code = "SB", IsBoard = true };
        private static ElNode Point(string id) => new ElNode { Id = id, Code = "LP" };

        [Fact]
        public void WireLengthUsesTrueLengthWhenKnown()
        {
            var w = Wire(0, 0, 3, 4);
            Assert.Equal(5.0, w.Length, 9);
            w.TrueLength = 7.5;
            Assert.Equal(7.5, w.Length, 9);
            var poly = new ElWire { Points = new List<PlanPoint> { new PlanPoint(0, 0), new PlanPoint(3, 0), new PlanPoint(3, 4) } };
            Assert.Equal(7.0, poly.Length, 9);
            Assert.Equal(0.0, new ElWire().Length);
        }

        [Fact]
        public void LengthPlusDropsPlusAllowance()
        {
            // 10 m of wire in drawing units of mm: 10000; two points on it, 1.5 m drop each; 10 % allowance
            var nodes = new List<ElNode> { Board("SB-01"), Point("LP-01"), Point("LP-02") };
            var wires = new List<ElWire> { Wire(0, 0, 6000, 0), Wire(6000, 0, 6000, 4000) };
            var nets = new List<ElNet> { new ElNet { Wires = { 0, 1 }, Boards = { 0 }, Points = { 1, 2 } } };
            double un;
            var r = CableLength.PerBoard(nodes, nets, wires, 1000, 10, 1.5, out un);
            Assert.Equal((10 + 3) * 1.1, r["SB-01"], 9);
            Assert.Equal(0, un);
        }

        [Fact]
        public void ANetOnTwoBoardsIsSharedAndOneOnNoneIsUnattached()
        {
            var nodes = new List<ElNode> { Board("SB-01"), Board("SB-02"), Point("LP-01"), Point("LP-02") };
            var wires = new List<ElWire> { Wire(0, 0, 8, 0), Wire(0, 5, 2, 5) };
            var nets = new List<ElNet>
            {
                new ElNet { Wires = { 0 }, Boards = { 0, 1 }, Points = { 2 } },
                new ElNet { Wires = { 1 }, Points = { 3 } },
            };
            double un;
            var r = CableLength.PerBoard(nodes, nets, wires, 1, 0, 0, out un);
            Assert.Equal(4.0, r["SB-01"], 9);
            Assert.Equal(4.0, r["SB-02"], 9);
            Assert.Equal(2.0, un, 9);
        }

        [Fact]
        public void BoardsWithNoWireAreListedAtZeroAndBadInputIsRefused()
        {
            double un;
            var r = CableLength.PerBoard(new List<ElNode> { Board("SB-01") }, new List<ElNet>(), new List<ElWire>(), 1000, 10, 0, out un);
            Assert.Equal(0, r["SB-01"]);
            Assert.Throws<ArgumentOutOfRangeException>(() => CableLength.PerBoard(new List<ElNode>(), new List<ElNet>(), new List<ElWire>(), 0, 0, 0, out un));
            // a negative allowance or drop is taken as 0
            var nodes = new List<ElNode> { Board("SB-01"), Point("LP-01") };
            var nets = new List<ElNet> { new ElNet { Wires = { 0 }, Boards = { 0 }, Points = { 1 } } };
            var r2 = CableLength.PerBoard(nodes, nets, new List<ElWire> { Wire(0, 0, 5, 0) }, 1, -50, -2, out un);
            Assert.Equal(5.0, r2["SB-01"], 9);
        }

        [Fact]
        public void TableHasARowPerBoardAndATotal()
        {
            var lt = new Dictionary<string, double> { { "SB-02", 12.34 }, { "SB-01", 5 } };
            var pw = new Dictionary<string, double> { { "SB-01", 20.06 } };
            var t = CableLength.Table(new[] { "SB-02", "SB-01", "SB-10" }, lt, pw);
            Assert.Equal(4, t.Count);
            Assert.Equal(new[] { "SB-01", "5.0", "20.1", "25.1" }, t[0]);
            Assert.Equal(new[] { "SB-02", "12.3", "", "12.3" }, t[1]);
            Assert.Equal(new[] { "SB-10", "", "", "" }, t[2]);
            Assert.Equal(new[] { "TOTAL", "17.3", "20.1", "37.4" }, t[3]);
            Assert.Equal(CableLength.Header.Length, t[0].Length);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class StairQuantityTests
    {
        private static StairQuantities Q(StairSpec s) => StairQuantities.Compute(s, StairCalc.Calculate(s));

        private static StairSpec Single() => new StairSpec
        {
            Kind = StairKind.Single, Width = 1200, FloorHeight = 1500, TotalRisers = 10, Going = 250, WaistThickness = 150,
        };

        [Fact]
        public void SingleFlightQuantities()
        {
            var q = Q(Single());                       // rise 150, 9 treads: run 2.25 m, height 1.5 m
            double slope = Math.Sqrt(2.25 * 2.25 + 1.5 * 1.5);
            Assert.Equal(1.2 * 0.15 * slope, q.WaistConcrete, 9);
            Assert.Equal(0.5 * 0.25 * 0.15 * 10 * 1.2, q.StepsConcrete, 9);
            Assert.Equal(0, q.LandingConcrete);
            Assert.Equal(1.2 * slope, q.Soffit, 9);
            Assert.Equal(2 * (0.15 * slope + 0.5 * 0.25 * 0.15 * 10), q.FlightSides, 9);
            Assert.Equal(1.2 * 0.15 * 10, q.RiserShuttering, 9);
            Assert.Equal(1.2 * 0.25 * 9, q.TreadFinish, 9);
            Assert.Equal(1.2 * 0.15 * 10, q.RiserFinish, 9);
            Assert.Equal(0, q.LandingFinish);
            Assert.Equal(2 * slope, q.Skirting, 9);
            Assert.Equal(q.WaistConcrete + q.StepsConcrete, q.Concrete, 9);
        }

        [Fact]
        public void DogLegAddsALandingAndASecondFlight()
        {
            var s = new StairSpec
            {
                Kind = StairKind.DogLeg, Width = 1200, FloorHeight = 3000, TotalRisers = 20, FirstFlightRisers = 10, Going = 250,
                LandingLength = 1200, WaistThickness = 150, LandingThickness = 150,
            };
            var q = Q(s);
            // two flights of 10 risers (9 treads), landing 2.4 x 1.2 m
            double slope = Math.Sqrt(2.25 * 2.25 + 1.5 * 1.5);
            Assert.Equal(2 * 1.2 * 0.15 * slope, q.WaistConcrete, 9);
            Assert.Equal(2.4 * 1.2 * 0.15, q.LandingConcrete, 9);
            Assert.Equal(2.4 * 1.2, q.LandingSoffit, 9);
            Assert.Equal(2.4 * 1.2, q.LandingFinish, 9);
            Assert.Equal(4 * slope, q.Skirting, 9);
            Assert.Equal(q.Soffit + q.FlightSides + q.RiserShuttering + q.LandingSoffit, q.Shuttering, 9);
            Assert.Equal(q.TreadFinish + q.RiserFinish + q.LandingFinish, q.Finishes, 9);
        }

        [Fact]
        public void UnevenFlightsAreWorkedSeparately()
        {
            var s = new StairSpec
            {
                Kind = StairKind.L, Width = 1000, FloorHeight = 3000, TotalRisers = 20, FirstFlightRisers = 8, Going = 250,
                LandingLength = 1000, WaistThickness = 150, LandingThickness = 150,
            };
            var q = Q(s);
            double s1 = Math.Sqrt(Math.Pow(7 * 0.25, 2) + Math.Pow(8 * 0.15, 2));
            double s2 = Math.Sqrt(Math.Pow(11 * 0.25, 2) + Math.Pow(12 * 0.15, 2));
            Assert.Equal(1.0 * 0.15 * (s1 + s2), q.WaistConcrete, 9);
            Assert.Equal(2 * (s1 + s2), q.Skirting, 9);
            Assert.Equal(1.0 * 0.15 * 1.0, q.LandingConcrete, 9);        // L landing: stair width x landing length x thickness
        }

        [Fact]
        public void RowsListEveryItemWithUnits()
        {
            var rows = Q(Single()).Rows();
            Assert.Equal(14, rows.Count);
            Assert.All(rows, r => Assert.Equal(StairQuantities.Headers.Length, r.Length));
            Assert.Contains(rows, r => r[0] == "CONCRETE TOTAL" && r[2] == "m3");
            Assert.Contains(rows, r => r[0] == "Skirting" && r[2] == "m");
            Assert.Equal("0.000", rows.First(r => r[0] == "Concrete - landing")[1]);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class ElectricalCircuitTests
    {
        private static ElLink Link(string board, string point, string code) => new ElLink { Board = board, Point = point, Code = code };
        private static readonly Dictionary<string, double> Watts = ElectricalLoad.ParseWatts(ElectricalLoad.DefaultWatts);
        private static readonly HashSet<string> Dedicated = ElectricalLoad.ParseCodes(ElectricalLoad.DefaultDedicated);

        [Fact]
        public void LightingIsPackedInPointOrder()
        {
            // 25 fans of 60 W: a circuit takes 16 (960 W) at a 1000 W limit
            var links = Enumerable.Range(1, 25).Select(i => Link("SB-01", "FP-" + i.ToString("00"), "FP")).ToList();
            var c = ElectricalLoad.CircuitsOf("SB-01", links, Watts, 1000, 3000, Dedicated);
            Assert.Equal(2, c.Count);
            Assert.Equal("L1", c[0].Name); Assert.Equal(16, c[0].Points.Count); Assert.Equal(960, c[0].Watts);
            Assert.Equal("L2", c[1].Name); Assert.Equal(9, c[1].Points.Count);
            Assert.Equal("FP-01", c[0].Points[0]);
            Assert.Equal("FP-17", c[1].Points[0]);
            Assert.Equal("SB-01/L2", c[1].FullName);
        }

        [Fact]
        public void DedicatedPowerFirstThenSharedAndSwitchesOnNone()
        {
            var links = new List<ElLink>
            {
                Link("SB-01", "P5-01", "P5"), Link("SB-01", "P5-02", "P5"), Link("SB-01", "AC-01", "AC"), Link("SB-01", "AC-02", "AC"),
                Link("SB-01", "SW1-01", "SW1"), Link("SB-01", "LP-01", "LP"),
            };
            var c = ElectricalLoad.CircuitsOf("SB-01", links, Watts, 1000, 3000, Dedicated);
            Assert.Equal(new[] { "L1", "P1", "P2", "P3" }, c.Select(x => x.Name).ToArray());
            Assert.Equal("Air conditioner", c[1].Kind);
            Assert.Equal(new[] { "AC-01" }, c[1].Points.ToArray());
            Assert.Equal("Power", c[3].Kind);
            Assert.Equal(new[] { "P5-01", "P5-02" }, c[3].Points.ToArray());
            Assert.DoesNotContain(c, x => x.Points.Contains("SW1-01"));
        }

        [Fact]
        public void APointBiggerThanTheLimitHasItsOwnCircuitAndZeroLimitMeansOne()
        {
            var big = new Dictionary<string, double> { { "LP", 1500 } };
            var links = new List<ElLink> { Link("SB-01", "LP-01", "LP"), Link("SB-01", "LP-02", "LP") };
            Assert.Equal(2, ElectricalLoad.CircuitsOf("SB-01", links, big, 1000, 3000, null).Count);
            Assert.Single(ElectricalLoad.CircuitsOf("SB-01", links, big, 0, 3000, null));
        }

        [Fact]
        public void CircuitCountsInTheLoadScheduleMatchTheCircuitList()
        {
            var links = new List<ElLink>
            {
                Link("SB-02", "LP-03", "LP"), Link("SB-01", "LP-01", "LP"), Link("SB-01", "GY-01", "GY"), Link("SB-01", "P5-01", "P5"),
            };
            LoadRow total;
            var rows = ElectricalLoad.Build(new[] { "SB-01", "SB-02" }, links, Watts, 1000, 3000, Dedicated, out total);
            var list = ElectricalLoad.Circuits(new[] { "SB-01", "SB-02" }, links, Watts, 1000, 3000, Dedicated);
            Assert.Equal(list.Count(c => c.Board == "SB-01"), rows[0].Circuits);
            Assert.Equal(list.Count, total.Circuits);
            Assert.Equal(new[] { "SB-01/L1", "SB-01/P1", "SB-01/P2", "SB-02/L1" }, list.Select(c => c.FullName).ToArray());
            var cells = ElectricalLoad.ToCells(list[1]);
            Assert.Equal(new[] { "SB-01/P1", "Geyser", "1: GY-01", "2000" }, cells);
        }

        [Fact]
        public void DiversityAppliesPerGroup()
        {
            double lt, pw;
            ElectricalLoad.ParseDiversity("LT=0.8; PW=0.5", out lt, out pw);
            Assert.Equal(0.8, lt); Assert.Equal(0.5, pw);
            ElectricalLoad.ParseDiversity("LT=2;PW=-1;XX=0.3;PW=abc", out lt, out pw);
            Assert.Equal(1, lt); Assert.Equal(1, pw);

            var links = new List<ElLink> { Link("SB-01", "LP-01", "LP"), Link("SB-01", "AC-01", "AC") };       // 15 W + 1500 W
            LoadRow total;
            var rows = ElectricalLoad.Build(new[] { "SB-01" }, links, Watts, 1000, 3000, Dedicated, out total, 0.8, 0.5);
            Assert.Equal(15 * 0.8 + 1500 * 0.5, rows[0].DemandWatts, 9);
            Assert.Equal(1515, rows[0].TotalWatts);
        }

        [Fact]
        public void NewKindsAreKnown()
        {
            Assert.Equal("LT", ElectricalKinds.Find("EF").Group);
            Assert.Equal("PW", ElectricalKinds.Find("INV").Group);
            Assert.Equal(40, Watts["EF"]);
            Assert.Equal(0, Watts["INV"]);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class StairRebarTests
    {
        private static readonly RebarOptions Opts = new RebarOptions { MainDia = 12, MainSpacing = 150, DistDia = 8, DistSpacing = 200, Cover = 25, AnchorageDiameters = 40 };

        [Theory]
        [InlineData(1150, 150, 8)]      // 1150 / 150 = 7.67 -> 7 bays, 8 bars
        [InlineData(1200, 150, 9)]      // exactly 8 bays
        [InlineData(0, 150, 0)]
        [InlineData(-5, 150, 0)]
        [InlineData(1000, 0, 0)]
        [InlineData(100, 150, 1)]
        public void BarCountAcrossASpan(double span, double spacing, int want) => Assert.Equal(want, StairRebar.Count(span, spacing));

        [Fact]
        public void SingleFlightBars()
        {
            var s = new StairSpec { Kind = StairKind.Single, Width = 1200, FloorHeight = 1500, TotalRisers = 10, Going = 250, WaistThickness = 150 };
            var bars = StairRebar.Compute(s, StairCalc.Calculate(s), Opts);
            Assert.Equal(new[] { "M1", "D1" }, bars.Select(b => b.Mark).ToArray());
            double slope = Math.Sqrt(2250.0 * 2250 + 1500.0 * 1500);
            Assert.Equal(8, bars[0].Nos);                                       // (1200 - 50) / 150 -> 7 bays
            Assert.Equal((slope + 2 * 40 * 12) / 1000, bars[0].Length, 9);
            Assert.Equal((int)Math.Floor((slope - 50) / 200) + 1, bars[1].Nos);
            Assert.Equal(1.15, bars[1].Length, 9);
            // weight: d^2 / 162 per metre
            Assert.Equal(bars[0].Nos * bars[0].Length * 144 / 162.0, bars[0].Weight, 9);
        }

        [Fact]
        public void DogLegAddsLandingBarsAndASecondFlight()
        {
            var s = new StairSpec
            {
                Kind = StairKind.DogLeg, Width = 1200, FloorHeight = 3000, TotalRisers = 20, FirstFlightRisers = 10, Going = 250,
                LandingLength = 1200, WaistThickness = 150, LandingThickness = 150,
            };
            var bars = StairRebar.Compute(s, StairCalc.Calculate(s), Opts);
            Assert.Equal(new[] { "M1", "D1", "M2", "D2", "ML", "DL" }, bars.Select(b => b.Mark).ToArray());
            var ml = bars.First(b => b.Mark == "ML");
            Assert.Equal(StairRebar.Count(2400 - 50, 150), ml.Nos);                  // landing width 2400 across the main bars
            Assert.Equal((1200 - 50) / 1000.0, ml.Length, 9);
            var dl = bars.First(b => b.Mark == "DL");
            Assert.Equal(StairRebar.Count(1200 - 50, 200), dl.Nos);
            Assert.Equal((2400 - 50) / 1000.0, dl.Length, 9);
            Assert.True(StairRebar.TotalWeight(bars) > 0);
        }

        [Fact]
        public void ScheduleRowsHaveATotalAndSteelPerCubicMetre()
        {
            var s = new StairSpec { Kind = StairKind.Single, Width = 1200, FloorHeight = 1500, TotalRisers = 10, Going = 250, WaistThickness = 150 };
            var bars = StairRebar.Compute(s, StairCalc.Calculate(s), Opts);
            var rows = StairRebar.Rows(bars, 0.7);
            Assert.All(rows, r => Assert.Equal(StairRebar.Headers.Length, r.Length));
            Assert.Equal("TOTAL", rows[rows.Count - 2][0]);
            Assert.EndsWith("kg/m3", rows.Last()[6]);
            Assert.Equal(bars.Count + 2, rows.Count);
            Assert.Equal(bars.Count + 1, StairRebar.Rows(bars, 0).Count);               // no per-m3 row without concrete
        }
    }
}

namespace HCW.Logic.Tests
{
    public class AreaPermissibleTests
    {
        private static List<FloorInput> Floors() => new List<FloorInput>
        {
            new FloorInput { Name = "GROUND", Gross = 100, Deduction = 10 },
            new FloorInput { Name = "FIRST", Gross = 90, Deduction = 12.5 },
        };

        [Fact]
        public void PermissibleValuesFillTheFieldsAndPassWhenInside()
        {
            var s = AreaStatement.Compute(Floors(), 200, 125, 60);            // FAR 83.75 %, cover 50 %
            Assert.Empty(s.Warnings);
            Assert.Equal(120, s.GroundCoverPermitted.Value, 9);
            var f = s.ToFields();
            Assert.Equal("125.00", f["FAR_PERM"]);
            Assert.Equal("120.00", f["GC_PERM"]);
        }

        [Fact]
        public void GoingOverIsWarned()
        {
            var s = AreaStatement.Compute(Floors(), 200, 80, 40);
            Assert.Contains(s.Warnings, w => w.Contains("F.A.R.") && w.Contains("80.00"));
            Assert.Contains(s.Warnings, w => w.Contains("Ground cover") && w.Contains("40.00"));
            // exactly on the limit is not over
            Assert.Empty(AreaStatement.Compute(Floors(), 200, 83.75, 50).Warnings);
        }

        [Fact]
        public void NotGivenLeavesTheFieldsOutSoTypedValuesStay()
        {
            var f = AreaStatement.Compute(Floors(), 200).ToFields();
            Assert.False(f.ContainsKey("FAR_PERM"));
            Assert.False(f.ContainsKey("GC_PERM"));
        }

        [Fact]
        public void NoSiteMeansNoPermittedGroundCoverArea()
        {
            var s = AreaStatement.Compute(Floors(), 0, 125, 60);
            Assert.Null(s.GroundCoverPermitted);
            Assert.False(s.ToFields().ContainsKey("GC_PERM"));
            Assert.Equal("125.00", s.ToFields()["FAR_PERM"]);
        }
    }
}

namespace HCW.Logic.Tests
{
    public class LevelScheduleTests
    {
        [Theory]
        [InlineData("+3.150", 3.15)]
        [InlineData("-0.450", -0.45)]
        [InlineData("±0.000", 0.0)]
        [InlineData("3.15", 3.15)]
        [InlineData(" +12.5 ", 12.5)]
        public void ParsesLevels(string text, double want) => Assert.Equal(want, LevelSchedule.Parse(text).Value, 9);

        [Theory]
        [InlineData("")]
        [InlineData("FFL")]
        [InlineData(null)]
        public void NonNumbersAreNull(string text) => Assert.Null(LevelSchedule.Parse(text));

        [Fact]
        public void HighestFirstWithCounts()
        {
            var rows = LevelSchedule.Build(new[] { "+3.150", "±0.000", "+3.150", "-0.450", "+6.300", "FFL", " ", "+3.150" });
            Assert.Equal(new[] { "+6.300", "+3.150", "±0.000", "-0.450", "FFL" }, rows.Select(r => r.Level).ToArray());
            Assert.Equal(new[] { 1, 3, 1, 1, 1 }, rows.Select(r => r.Marks).ToArray());
        }

        [Fact]
        public void NothingGivesNoRows() => Assert.Empty(LevelSchedule.Build(new string[0]));
    }
}

namespace HCW.Logic.Tests
{
    public class CornerFitTests
    {
        private static bool Near(P2 a, P2 b) => a.DistanceTo(b) < 1e-9;

        [Fact]
        public void TwoShortLinesAreExtendedToTheCorner()
        {
            // horizontal 0..3 and vertical 5..8 at x = 4: they meet at (4, 0)
            string err;
            var r = CornerFit.Fit(new Seg(new P2(0, 0), new P2(3, 0)), new P2(1, 0), new Seg(new P2(4, 5), new P2(4, 8)), new P2(4, 7), out err);
            Assert.NotNull(r);
            Assert.True(Near(r.Corner, new P2(4, 0)));
            Assert.True(Near(r.A.A, new P2(0, 0)) && Near(r.A.B, new P2(4, 0)));              // extended to the right
            Assert.True(Near(r.B.A, new P2(4, 0)) && Near(r.B.B, new P2(4, 8)));              // extended down
        }

        [Fact]
        public void LinesThatCrossAreTrimmedToTheSideYouPicked()
        {
            // a cross at (2, 2): keep the left and bottom arms
            string err;
            var h = new Seg(new P2(0, 2), new P2(5, 2));
            var v = new Seg(new P2(2, 0), new P2(2, 5));
            var r = CornerFit.Fit(h, new P2(0.5, 2), v, new P2(2, 0.5), out err);
            Assert.True(Near(r.A.A, new P2(0, 2)) && Near(r.A.B, new P2(2, 2)));
            Assert.True(Near(r.B.A, new P2(2, 0)) && Near(r.B.B, new P2(2, 2)));
            // keep the right and top arms instead
            var r2 = CornerFit.Fit(h, new P2(4.5, 2), v, new P2(2, 4.5), out err);
            Assert.True(Near(r2.A.A, new P2(2, 2)) && Near(r2.A.B, new P2(5, 2)));
            Assert.True(Near(r2.B.A, new P2(2, 2)) && Near(r2.B.B, new P2(2, 5)));
        }

        [Fact]
        public void AnExtendedLineKeepsItsOwnStartWhenPickedNearIt()
        {
            // a line drawn right to left meeting a wall at x = 10: keep the start (picked end), extend the far end to the corner
            string err;
            var r = CornerFit.Fit(new Seg(new P2(8, 1), new P2(2, 1)), new P2(3, 1), new Seg(new P2(10, 0), new P2(10, 5)), new P2(10, 4), out err);
            Assert.True(Near(r.Corner, new P2(10, 1)));
            Assert.True(Near(r.A.A, new P2(10, 1)) && Near(r.A.B, new P2(2, 1)));              // the near end moved to the corner, the picked end stays
        }

        [Fact]
        public void ParallelZeroLengthAndFarCornersAreRefused()
        {
            string err;
            Assert.Null(CornerFit.Fit(new Seg(new P2(0, 0), new P2(1, 0)), new P2(0, 0), new Seg(new P2(0, 1), new P2(1, 1)), new P2(0, 1), out err));
            Assert.Contains("parallel", err);
            Assert.Null(CornerFit.Fit(new Seg(new P2(0, 0), new P2(0, 0)), new P2(0, 0), new Seg(new P2(0, 1), new P2(1, 1)), new P2(0, 1), out err));
            Assert.Contains("no length", err);
            Assert.Null(CornerFit.Fit(new Seg(new P2(0, 0), new P2(1, 0)), new P2(0, 0), new Seg(new P2(0, 1), new P2(1, 1.0000001)), new P2(0, 1), out err));
            Assert.Contains("far", err);
        }

        [Fact]
        public void AngledLinesMeetAtTheirCorner()
        {
            string err;
            var r = CornerFit.Fit(new Seg(new P2(0, 0), new P2(1, 1)), new P2(0.5, 0.5), new Seg(new P2(6, 0), new P2(5, 1)), new P2(5.5, 0.5), out err);
            Assert.True(Near(r.Corner, new P2(3, 3)));
            Assert.True(Near(r.A.B, new P2(3, 3)) && Near(r.B.B, new P2(3, 3)) || Near(r.B.A, new P2(3, 3)));
        }
    }
}
