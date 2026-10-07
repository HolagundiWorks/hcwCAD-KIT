using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class ProjectDataTests
    {
        [Fact]
        public void DetailsRoundTripThroughLines()
        {
            var p = new ProjectData();
            p.Set("PROJECT_TITLE", "Residence for Mr Rao");
            p.Set("OWNER", "S. Rao");
            p.Set("ADDRESS", "12, 4th Cross | Jayanagar");           // a bar inside a value is kept
            p.BeamDepthsMm = new System.Collections.Generic.List<double> { 450, 300 };
            var back = ProjectData.FromLines(p.ToLines());
            Assert.Equal("Residence for Mr Rao", back.Get("PROJECT_TITLE"));
            Assert.Equal("S. Rao", back.Get("OWNER"));
            Assert.Equal("12, 4th Cross | Jayanagar", back.Get("ADDRESS"));
            Assert.Equal("", back.Get("ARCHITECT"));
        }

        [Fact]
        public void BlankValueRemovesTheDetail()
        {
            var p = new ProjectData();
            p.Set("OWNER", "S. Rao");
            p.Set("OWNER", "   ");
            Assert.DoesNotContain(p.ToLines(), l => l.StartsWith("OWNER"));
        }

        [Fact]
        public void OnlyDetailsWithATitleFieldAndAValueFillTheTitleBlock()
        {
            var p = new ProjectData();
            p.Set("PROJECT_TITLE", "House");
            p.Set("PID", "12-34");
            p.Set("ADDRESS", "Somewhere");          // no title block field
            var t = p.TitleValues();
            Assert.Equal("House", t["PROJECT_TITLE"]);
            Assert.Equal("12-34", t["PID"]);
            Assert.False(t.ContainsKey("ADDRESS"));
            Assert.False(t.ContainsKey("OWNER"));
        }

        [Fact]
        public void BeamDepthsParseSortedWithoutRepeatsOrJunk()
        {
            Assert.Equal(new[] { 230.0, 300.0, 450.0, 600.0 }, ProjectData.ParseDepths("450, 300; 230 600 300 abc -5 0").ToArray());
            Assert.Equal("230, 300, 450", ProjectData.FormatDepths(new[] { 230.0, 300.0, 450.0 }));
        }

        [Fact]
        public void BeamDepthsSurviveTheLines()
        {
            var p = new ProjectData { BeamDepthsMm = ProjectData.ParseDepths("600, 300, 450") };
            var back = ProjectData.FromLines(p.ToLines());
            Assert.Equal(new[] { 300.0, 450.0, 600.0 }, back.BeamDepthsMm.ToArray());
        }

        [Theory]
        [InlineData(0, "Ground")]
        [InlineData(1, "First")]
        [InlineData(3, "Third")]
        [InlineData(4, "Fourth")]
        [InlineData(5, "Floor 6")]
        public void FloorsAreNamedInOrder(int i, string name) => Assert.Equal(name, ProjectData.FloorName(i));

        [Fact]
        public void BeamDepthIsTheSmallestStandardDepthDeepEnoughForTheSpan()
        {
            var depths = new[] { 300.0, 375.0, 450.0, 600.0 };
            Assert.Equal(375, ProjectData.BeamDepthFor(depths, 4500, 12), 6);      // 4500 / 12 = 375
            Assert.Equal(450, ProjectData.BeamDepthFor(depths, 4600, 12), 6);
            Assert.Equal(600, ProjectData.BeamDepthFor(depths, 9000, 12), 6);      // deeper than any: the largest
            Assert.Equal(0, ProjectData.BeamDepthFor(new double[0], 4500, 12), 6);
        }
    }
}
