using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class OpeningStandardsTests
    {
        [Theory]
        [InlineData("D1", 800)]
        [InlineData("D2", 900)]
        [InlineData("D3", 1200)]
        [InlineData("W1", 600)]
        [InlineData("W2", 900)]
        [InlineData("W3", 1200)]
        [InlineData("W4", 1500)]
        [InlineData("W5", 2000)]
        [InlineData("V1", 600)]
        public void EveryStandardMarkHasItsWidth(string code, double width)
        {
            Assert.Equal(width, OpeningStandards.WidthOf(code));
            Assert.Equal(width, OpeningStandards.WidthOf(code.ToLowerInvariant()));
        }

        [Fact]
        public void UnknownMarkHasNoStandardWidth() => Assert.Null(OpeningStandards.WidthOf("D9"));

        [Theory]
        [InlineData(800, "D1")]
        [InlineData(900, "D2")]
        [InlineData(1200, "D3")]
        [InlineData(1000, null)]
        public void DoorMarkFollowsTheWidth(double width, string code) => Assert.Equal(code, OpeningStandards.DoorCode(width));

        [Theory]
        [InlineData(600, 1200, "W1")]
        [InlineData(900, 1200, "W2")]
        [InlineData(1200, 1200, "W3")]
        [InlineData(1500, 1200, "W4")]
        [InlineData(2000, 1200, "W5")]
        [InlineData(600, 450, "V1")]          // a 600 wide window no taller than the ventilator limit
        [InlineData(600, 600, "V1")]
        [InlineData(600, 900, "W1")]          // taller than the limit: a window
        [InlineData(1800, 1200, null)]
        public void WindowMarkFollowsTheWidthAndTheVentilatorHeight(double width, double height, string code) =>
            Assert.Equal(code, OpeningStandards.WindowCode(width, height, 600));

        [Fact]
        public void NewEntryTakesTheStandardMarkWhenFree()
        {
            Assert.Equal("D2", OpeningStandards.NewMark("D2", "D", new string[0]));
            Assert.Equal("W5", OpeningStandards.NewMark("W5", "W", new[] { "W1" }));
        }

        [Fact]
        public void SameWidthAtAnotherHeightGetsTheNextFreeNumberAfterTheStandardOnes()
        {
            Assert.Equal("D4", OpeningStandards.NewMark("D2", "D", new[] { "D2" }));          // doors: D1 to D3 are the standards
            Assert.Equal("W6", OpeningStandards.NewMark("W2", "W", new[] { "W2" }));          // windows: W1 to W5
            Assert.Equal("V2", OpeningStandards.NewMark("V1", "V", new[] { "V1" }));
        }

        [Fact]
        public void SizeNotInTheTableGetsTheNextNumberAfterTheStandardsAndAfterThoseInUse()
        {
            Assert.Equal("D4", OpeningStandards.NewMark(null, "D", new string[0]));
            Assert.Equal("D6", OpeningStandards.NewMark(null, "D", new[] { "D1", "D5" }));
            Assert.Equal("W6", OpeningStandards.NewMark(null, "W", new[] { "W1" }));
        }
    }
}
