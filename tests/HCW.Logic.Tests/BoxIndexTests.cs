using System;
using System.Collections.Generic;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class BoxIndexTests
    {
        private static List<int>[] Brute(Box2?[] q, Box2?[] items, double tol) =>
            q.Select(b => Enumerable.Range(0, items.Length)
                .Where(i => !b.HasValue || !items[i].HasValue || b.Value.Near(items[i].Value, tol)).ToList()).ToArray();

        [Theory]
        [InlineData(10, 5, 1)]
        [InlineData(500, 300, 2)]
        [InlineData(2000, 400, 3)]
        public void MatchesBruteForce(int nItems, int nQueries, int seed)
        {
            var rnd = new Random(seed);
            Box2? Make(double span)
            {
                double x = rnd.NextDouble() * 1000, y = rnd.NextDouble() * 1000;
                double w = rnd.NextDouble() * span, h = rnd.NextDouble() * span;
                return new Box2(x, y, x + w, y + h);
            }
            var items = Enumerable.Range(0, nItems).Select(i => i % 97 == 0 ? (Box2?)null : i % 53 == 0 ? Make(900) : Make(20)).ToArray();
            var queries = Enumerable.Range(0, nQueries).Select(i => i % 41 == 0 ? (Box2?)null : Make(10)).ToArray();
            var got = BoxIndex.Candidates(queries, items, 0.5);
            var want = Brute(queries, items, 0.5);
            for (int q = 0; q < queries.Length; q++) Assert.Equal(want[q], got[q]);
        }

        [Fact]
        public void EmptyItems() =>
            Assert.Empty(BoxIndex.Candidates(new Box2?[] { new Box2(0, 0, 1, 1) }, new Box2?[0], 1)[0]);
    }
}

namespace HCW.Logic.Tests
{
    public class SheetFitTests
    {
        [Theory]
        [InlineData(1, 1)]
        [InlineData(73.2, 75)]
        [InlineData(100, 100)]
        [InlineData(100.5, 150)]
        [InlineData(20000, 20000)]
        public void NextStandard(double n, double want) => Assert.Equal(want, SheetFit.NextStandard(n));

        [Fact]
        public void ScaleFitsTheTighterSide()
        {
            // 400 x 150 area, 20 x 5 model: width limits (400*0.95/20 = 19), height allows 28.5
            Assert.Equal(19.0, SheetFit.ScaleToFit(400, 150, 20, 5), 6);
            Assert.Equal(1.0, SheetFit.ScaleToFit(400, 150, 0, 0));
            Assert.Equal(28.5, SheetFit.ScaleToFit(400, 150, 0, 5), 6);
        }

        [Fact]
        public void RatioRoundTrips()
        {
            // metre drawing (0.001 units per mm), mm sheet: 10 paper mm per unit is 1:100
            Assert.Equal(100.0, SheetFit.Ratio(10, 1, 0.001), 6);
            Assert.Equal(10.0, SheetFit.ViewportScale(100, 1, 0.001), 9);
            Assert.Equal(100.0, SheetFit.Ratio(SheetFit.ViewportScale(100, 1 / 25.4, 0.001), 1 / 25.4, 0.001), 6);
        }
    }
}
