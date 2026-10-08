using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class WallFaceLayersTests
    {
        [Theory]
        [InlineData("MEASURE-LINEAR", true)]
        [InlineData("measure-deduct", true)]
        [InlineData("MEASURE-LINTEL", true)]
        [InlineData("A-WALL", false)]
        [InlineData("HCW", false)]
        [InlineData("MEASUREMENT", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyTakeOffLayersAreExcluded(string layer, bool expected) =>
            Assert.Equal(expected, WallFaceLayers.IsTakeOff(layer));
    }
}
