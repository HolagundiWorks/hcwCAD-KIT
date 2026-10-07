using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    /// <summary>The exporter's output for the sample inputs is the sample file docs/bridge/fixtures/small-house.json, whatever unit the drawing is in.</summary>
    public class BridgeExportTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge", "fixtures", name)).Replace("\r\n", "\n").TrimEnd() + "\n";

        /// <summary>The small house as the drawing would hold it, in the given unit (the millimetre sample values divided by mm per unit).</summary>
        private static BridgeInput SmallHouse(string unit)
        {
            double u = BridgeExport.MmPerUnit(unit);
            Func<double, double> d = mm => mm / u;
            Func<double, double> a = m2 => m2 * 1e6 / (u * u);       // square metres to drawing units squared
            return new BridgeInput
            {
                Unit = unit,
                Source = new BridgeSource { App = "hcwCAD-KIT", Host = "AutoCAD 2022", Drawing = "small house.dwg", DrawingId = "0b7d9c34-1e6f-4f0b-8a13-9b2c7d5e41aa", Exported = "2026-10-07T11:00:00Z" },
                Project = new BridgeProject { Name = "Small house", ClientName = "A. Kumar", Location = "Hospet", SiteAreaM2 = 120, PlotUse = "Residential" },
                Levels = { new BridgeLevel { Name = "Ground", HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 450, CeilingMm = 3000, LintelBottomMm = 2100 } },
                BeamDepthsMm = { 300, 450 },
                Walls =
                {
                    new BridgeWall { Ref = "W0001", Level = "Ground", Length = d(9000), Height = d(3000), Thickness = d(230), Layer = "A-WALL" },
                    new BridgeWall { Ref = "W0002", Level = "Ground", Length = d(6000), Height = d(3000), Thickness = d(230), Layer = "A-WALL" },
                    new BridgeWall { Ref = "W0003", Level = "Ground", Length = d(4500), Height = d(3000), Thickness = d(114.3), Layer = "A-WALL" },
                },
                Openings =
                {
                    new BridgeOpening { Ref = "2A1", Kind = "Door", Mark = "D3", Level = "Ground", Width = d(1200), Height = d(2100), Sill = 0, Type = "double", WallRef = "W0001" },
                    new BridgeOpening { Ref = "2A2", Kind = "Door", Mark = "D2", Level = "Ground", Width = d(900), Height = d(2100), Sill = 0, Type = "single", WallRef = "W0003" },
                    new BridgeOpening { Ref = "2A3", Kind = "Door", Mark = "D2", Level = "Ground", Width = d(900), Height = d(2100), Sill = 0, Type = "single", WallRef = "W0003" },
                    new BridgeOpening { Ref = "2B1", Kind = "Window", Mark = "W3", Level = "Ground", Width = d(1200), Height = d(1200), Sill = d(900), WallRef = "W0002" },
                    new BridgeOpening { Ref = "2B2", Kind = "Window", Mark = "V1", Level = "Ground", Width = d(600), Height = d(450), Sill = d(1800), WallRef = "W0002" },
                },
                OpeningSchedule =
                {
                    new BridgeScheduleRow { Mark = "D2", Kind = "Door", Width = d(900), Height = d(2100), Sill = 0, Nos = 2, Type = "single" },
                    new BridgeScheduleRow { Mark = "D3", Kind = "Door", Width = d(1200), Height = d(2100), Sill = 0, Nos = 1, Type = "double" },
                    new BridgeScheduleRow { Mark = "W3", Kind = "Window", Width = d(1200), Height = d(1200), Sill = d(900), Nos = 1 },
                    new BridgeScheduleRow { Mark = "V1", Kind = "Window", Width = d(600), Height = d(450), Sill = d(1800), Nos = 1 },
                },
                Columns =
                {
                    new BridgeColumn { Ref = "3C1", Mark = "C1", Level = "Ground", Width = d(230), Depth = d(450) },
                    new BridgeColumn { Ref = "3C2", Mark = "C1", Level = "Ground", Width = d(230), Depth = d(450) },
                },
                Lintels = { new BridgeLintel { Ref = "4D1", Mark = "L1", Level = "Ground", Opening = d(1200), Bearing = d(150), Width = d(230), Depth = d(150) } },
                Rooms =
                {
                    new BridgeRoom { Ref = "5E1", Name = "LIVING", Level = "Ground", Area = a(22.5), Length = d(5000), Breadth = d(4500) },
                    new BridgeRoom { Ref = "5E2", Name = "KITCHEN", Level = "Ground", Area = a(9), Length = d(3000), Breadth = d(3000) },
                },
                Slabs = { new BridgeSlab { Ref = "6F1", Level = "Ground", Area = a(54), Thickness = d(150), Length = d(9000), Breadth = d(6000), Rectangular = true } },
            };
        }

        [Theory]
        [InlineData("mm")]
        [InlineData("cm")]
        [InlineData("m")]
        [InlineData("in")]
        [InlineData("ft")]
        public void SmallHouseInAnyDrawingUnitIsTheSampleFile(string unit)
        {
            Assert.Equal(Fixture("small-house.json"), BridgeExport.ToJson(SmallHouse(unit)));
        }

        [Fact]
        public void LevelsAndProjectOnlyIsTheOtherSampleFile()
        {
            var input = new BridgeInput
            {
                Source = new BridgeSource { Host = "AutoCAD 2022", Drawing = "building permission.dwg", DrawingId = "6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60", Exported = "2026-10-07T10:30:00Z" },
                Project = new BridgeProject { Name = "Residence for Mr Rao", ClientName = "S. Rao", CompanyName = "Holgundi Consulting Works", Location = "Hospet", Address = "12, 4th Cross, Hospet", Pid = "12-34-56", SiteAreaM2 = 186.5, PlotUse = "Residential" },
                BeamDepthsMm = { 300, 375, 450 },
            };
            foreach (var n in new[] { "Ground", "First" })
                input.Levels.Add(new BridgeLevel { Name = n, HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 450, CeilingMm = 3000, LintelBottomMm = 2100 });
            Assert.Equal(Fixture("levels-only.json"), BridgeExport.ToJson(input));       // sections with no rows are left out
        }

        [Theory]
        [InlineData("in", 25.4)]
        [InlineData("ft", 304.8)]
        [InlineData("cm", 10)]
        [InlineData("m", 1000)]
        [InlineData("mm", 1)]
        public void LengthsConvertToMillimetres(string unit, double mmPerUnit)
        {
            Assert.Equal(mmPerUnit, BridgeExport.MmPerUnit(unit), 9);
            Assert.Equal(5 * mmPerUnit, BridgeExport.ToMm(5, unit), 9);
        }

        [Fact]
        public void AreasConvertToSquareMetres()
        {
            Assert.Equal(1.0, BridgeExport.ToSquareMetres(1, "m"), 9);
            Assert.Equal(0.09290304, BridgeExport.ToSquareMetres(1, "ft"), 9);
            Assert.Equal(1.0, BridgeExport.ToSquareMetres(1e6, "mm"), 9);
        }

        [Fact]
        public void UnknownUnitIsRefused()
        {
            Assert.Throws<ArgumentException>(() => BridgeExport.MmPerUnit("furlong"));
            Assert.Throws<ArgumentException>(() => BridgeExport.ToJson(new BridgeInput { Unit = "yd" }));
        }

        [Fact]
        public void TextIsEscapedAndTheResultIsValidJson()
        {
            var input = new BridgeInput { Source = new BridgeSource { Drawing = "a \"quoted\" \\ name\tx.dwg", DrawingId = "id", Exported = "2026-10-07T11:00:00Z" } };
            input.Levels.Add(new BridgeLevel { Name = "Level \"A\"", HeightMm = 3000, SlabThicknessMm = 150, BeamDepthMm = 450 });
            var doc = JsonDocument.Parse(BridgeExport.ToJson(input)).RootElement;
            Assert.Equal("a \"quoted\" \\ name\tx.dwg", doc.GetProperty("source").GetProperty("drawing").GetString());
            Assert.Equal("Level \"A\"", doc.GetProperty("levels")[0].GetProperty("name").GetString());
        }

        [Fact]
        public void OptionalFieldsAreLeftOutAndNumbersAreTrimmed()
        {
            var input = new BridgeInput { Source = new BridgeSource { DrawingId = "id", Exported = "2026-10-07T11:00:00Z" } };
            input.Levels.Add(new BridgeLevel { Name = "G", HeightMm = 3150.0004, SlabThicknessMm = 150, BeamDepthMm = 450 });
            input.Walls.Add(new BridgeWall { Ref = "W1", Level = "G", Length = 1000.12349, Height = 3000, Thickness = 230 });
            var text = BridgeExport.ToJson(input);
            Assert.DoesNotContain("ceiling_mm", text);
            Assert.DoesNotContain("layer", text);
            Assert.Contains("\"height_mm\": 3150,", text);
            Assert.Contains("\"length_mm\": 1000.123,", text);
            Assert.DoesNotContain(": -0", text);
        }

        [Fact]
        public void ExportedFileSatisfiesTheContractRules()
        {
            var root = JsonDocument.Parse(BridgeExport.ToJson(SmallHouse("in"))).RootElement;
            Assert.Equal("hcw-aqc-bridge", root.GetProperty("format").GetString());
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal("mm", root.GetProperty("units").GetString());
            var levelNames = root.GetProperty("levels").EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToHashSet();
            foreach (var w in root.GetProperty("walls").EnumerateArray()) Assert.Contains(w.GetProperty("level").GetString(), levelNames);
        }
    }
}
