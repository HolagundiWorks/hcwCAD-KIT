using System.Collections.Generic;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class BridgeMapTests
    {
        private static WallRecord Wall(string id, params double[] xy)
        {
            var w = new WallRecord { Id = id };
            for (int i = 0; i < xy.Length; i += 2) w.Points.Add(new P2(xy[i], xy[i + 1]));
            return w;
        }

        [Fact]
        public void WallLengthIsTheCentreLineAndClosedRunsCloseUp()
        {
            Assert.Equal(7000, BridgeMap.WallLength(Wall("W1", 0, 0, 4000, 0, 4000, 3000)), 6);
            var ring = Wall("W2", 0, 0, 4000, 0, 4000, 3000, 0, 3000); ring.Closed = true;
            Assert.Equal(14000, BridgeMap.WallLength(ring), 6);
        }

        [Fact]
        public void NearestWallIsTheOneWithinReach()
        {
            var walls = new[] { Wall("W1", 0, 0, 4000, 0), Wall("W2", 0, 3000, 4000, 3000) };
            Assert.Equal("W1", BridgeMap.NearestWall(new P2(2000, 100), walls, 300));
            Assert.Equal("W2", BridgeMap.NearestWall(new P2(2000, 2900), walls, 300));
            Assert.Null(BridgeMap.NearestWall(new P2(2000, 1500), walls, 300));
        }

        [Fact]
        public void MarkComesFromTheScheduleThenTheStandardsThenAFallback()
        {
            var sched = new[] { new BridgeMap.ScheduleEntry { Mark = "D7", Kind = "Door", WidthMm = 1000, HeightMm = 2100 } };
            Assert.Equal("D7", BridgeMap.OpeningMark(true, 1000, 2100, 0, sched, 600));
            Assert.Equal("D2", BridgeMap.OpeningMark(true, 900, 2100, 0, sched, 600));
            Assert.Equal("W3", BridgeMap.OpeningMark(false, 1200, 1200, 900, sched, 600));
            Assert.Equal("V1", BridgeMap.OpeningMark(false, 600, 450, 1800, sched, 600));
            Assert.Equal("D-1050", BridgeMap.OpeningMark(true, 1050, 2100, 0, sched, 600));
            Assert.Equal("W-1350", BridgeMap.OpeningMark(false, 1350, 1200, 900, null, 600));
        }

        [Fact]
        public void ScheduleCountsOpeningsByMarkAndSizeDoorsFirst()
        {
            var rows = new List<BridgeOpening>
            {
                new BridgeOpening { Mark = "W3", Kind = "Window", Width = 1200, Height = 1200, Sill = 900 },
                new BridgeOpening { Mark = "D2", Kind = "Door", Width = 900, Height = 2100, Sill = 0, Type = "single" },
                new BridgeOpening { Mark = "D2", Kind = "Door", Width = 900, Height = 2100, Sill = 0, Type = "single" },
                new BridgeOpening { Mark = "D10", Kind = "Door", Width = 1000, Height = 2100, Sill = 0 },
            };
            var s = BridgeMap.Schedule(rows, 1.0);
            Assert.Equal(new[] { "D2", "D10", "W3" }, s.Select(r => r.Mark).ToArray());     // natural order, doors first
            Assert.Equal(2, s[0].Nos);
            Assert.Equal(1, s[2].Nos);
        }

        [Fact]
        public void ProjectDetailsMapAndSiteAreaIsReadFromText()
        {
            var d = new ProjectData();
            d.Set("PROJECT_TITLE", "Residence"); d.Set("OWNER", "S. Rao"); d.Set("ARCHITECT", "Holgundi"); d.Set("PID", "12-34"); d.Set("SITE_AREA", "186.5 sq m"); d.Set("PLOT_USE", "Residential");
            var p = BridgeMap.Project(d);
            Assert.Equal("Residence", p.Name); Assert.Equal("S. Rao", p.ClientName); Assert.Equal("Holgundi", p.CompanyName); Assert.Equal("12-34", p.Pid);
            Assert.Equal(186.5, p.SiteAreaM2.Value, 6);
            Assert.Null(BridgeMap.Project(new ProjectData()).SiteAreaM2);
            Assert.Null(BridgeMap.Project(new ProjectData()).Name);
        }

        [Fact]
        public void LevelsCarryTheBeamDepthOfTheFloorAndProblemsAreFound()
        {
            var d = new ProjectData(); d.SetFloorBeam("Ground", 375);
            var floors = new[] { new LevelRow { Name = "Ground", FflMm = 3150, SlabMm = 150, CeilingMm = 3000, LintelMm = 2100 }, new LevelRow { Name = "Low", FflMm = 500, SlabMm = 150, CeilingMm = 0, LintelMm = 0 } };
            var l = BridgeMap.Levels(floors, d);
            Assert.Equal(375, l[0].BeamDepthMm); Assert.Equal(450, l[1].BeamDepthMm);
            Assert.Null(l[1].CeilingMm);
            var problems = BridgeMap.LevelProblems(l);
            Assert.Single(problems);
            Assert.StartsWith("Low", problems[0]);
        }
    
        [Fact]
        public void CopiesOfOneRoomOutlineShareAKeyAndDifferentRoomsDoNot()
        {
            var a = new List<P2> { new P2(0, 0), new P2(5000, 0), new P2(5000, 4000), new P2(0, 4000) };
            var copy = new List<P2> { new P2(0, 0), new P2(5000, 0), new P2(5000, 4000), new P2(0, 4000) };
            var other = new List<P2> { new P2(6000, 0), new P2(9000, 0), new P2(9000, 3000), new P2(6000, 3000) };
            Assert.Equal(BridgeMap.OutlineKey(a, 20e6, 1), BridgeMap.OutlineKey(copy, 20e6, 1));
            Assert.NotEqual(BridgeMap.OutlineKey(a, 20e6, 1), BridgeMap.OutlineKey(other, 9e6, 1));
        }
    }
}
