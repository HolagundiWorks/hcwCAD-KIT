using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class RoomTextFitTests
    {
        [Fact]
        public void TextThatFitsGrowsToTheCap()
        {
            // 50 high text with a 200 x 50 box, in a 1000 x 500 slot: it could be 250 high, but the cap is 100
            Assert.Equal(100, RoomTextFit.FitHeight(50, 200, 50, 1000, 500, 100), 6);
        }

        [Fact]
        public void TextThatIsTooWideShrinksUntilItFitsTheWidth()
        {
            // at height 50 the box is 400 wide; the room allows 200, so the height halves
            Assert.Equal(25, RoomTextFit.FitHeight(50, 400, 50, 200, 500, 100), 6);
        }

        [Fact]
        public void TextThatIsTooTallShrinksUntilItFitsTheHeight()
        {
            Assert.Equal(30, RoomTextFit.FitHeight(60, 100, 120, 1000, 60, 100), 6);
        }

        [Fact]
        public void LinesThatFitAreStackedCentredOnZeroWithAGap()
        {
            double scale;
            var c = RoomTextFit.Stack(new[] { 100.0, 100.0 }, 1000, out scale);
            Assert.Equal(1.0, scale, 6);
            // gap = 0.15 x 100 = 15, total 215: centres at -57.5 and +57.5
            Assert.Equal(2, c.Count);
            Assert.Equal(-57.5, c[0], 6);
            Assert.Equal(57.5, c[1], 6);
        }

        [Fact]
        public void LinesThatDoNotFitAreScaledTogetherAndStillCentred()
        {
            double scale;
            var c = RoomTextFit.Stack(new[] { 100.0, 100.0, 100.0 }, 160, out scale);
            Assert.True(scale < 1.0);
            // after scaling the group is exactly as long as the room allows and is centred
            double s = 100 * scale, gap = 0.15 * s;
            Assert.Equal(160, 3 * s + 2 * gap, 6);
            Assert.Equal(0, (c.First() + c.Last()) / 2.0, 6);
            Assert.Equal(s + gap, c[1] - c[0], 6);
        }

        [Fact]
        public void SingleLineIsCentred()
        {
            double scale;
            var c = RoomTextFit.Stack(new[] { 80.0 }, 500, out scale);
            Assert.Equal(0, c.Single(), 6);
        }

        [Theory]
        [InlineData(0.0, false)]
        [InlineData(System.Math.PI / 2, true)]
        [InlineData(3 * System.Math.PI / 2, true)]
        [InlineData(System.Math.PI / 6, false)]
        public void TextTurnedPast45DegreesIsVertical(double rotation, bool vertical) => Assert.Equal(vertical, RoomTextFit.IsVertical(rotation));

        [Fact]
        public void SpacesAndTabsAreRemoved() => Assert.Equal("MASTERBEDROOM", RoomTextFit.Clean(" MASTER BED\tROOM "));
    }
}
