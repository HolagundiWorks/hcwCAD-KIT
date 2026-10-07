using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    /// <summary>
    /// Checks the hcw-aqc-bridge v1 sample files (docs/bridge/fixtures) against the rules the contract states: the right format and units, every level
    /// reference points at a level in the file, refs are unique, openings point at walls, and the standard opening marks agree with their widths.
    /// </summary>
    public class BridgeContractTests
    {
        private static string Path(params string[] parts) => System.IO.Path.Combine(new[] { AppContext.BaseDirectory, "bridge" }.Concat(parts).ToArray());

        public static readonly object[][] Fixtures =
        {
            new object[] { "levels-only.json" },
            new object[] { "small-house.json" },
        };

        private static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path("fixtures", name))).RootElement;

        private static JsonElement[] Items(JsonElement root, string section) =>
            root.TryGetProperty(section, out var a) ? a.EnumerateArray().ToArray() : new JsonElement[0];

        [Fact]
        public void SchemaIsValidJsonAndNamesTheRequiredTopLevelFields()
        {
            var schema = JsonDocument.Parse(File.ReadAllText(Path("hcw-aqc-bridge.schema.json"))).RootElement;
            var required = schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Contains("format", required);
            Assert.Contains("version", required);
            Assert.Contains("units", required);
            Assert.Contains("source", required);
            Assert.Equal("mm", schema.GetProperty("properties").GetProperty("units").GetProperty("const").GetString());
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void FileDeclaresTheFormatVersionUnitsAndSource(string file)
        {
            var root = Load(file);
            Assert.Equal("hcw-aqc-bridge", root.GetProperty("format").GetString());
            Assert.Equal(1, root.GetProperty("version").GetInt32());
            Assert.Equal("mm", root.GetProperty("units").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("source").GetProperty("drawing_id").GetString()));
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("source").GetProperty("exported").GetString(), out _));
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void OnlyKnownTopLevelSectionsAreUsed(string file)
        {
            var known = new[] { "format", "version", "source", "units", "project", "levels", "beam_depths_mm", "walls", "openings", "opening_schedule", "columns", "lintels", "rooms", "slabs" };
            foreach (var p in Load(file).EnumerateObject()) Assert.Contains(p.Name, known);
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void LevelsHaveUniqueNamesAndPositiveHeights(string file)
        {
            var levels = Items(Load(file), "levels");
            Assert.NotEmpty(levels);
            Assert.Equal(levels.Length, levels.Select(l => l.GetProperty("name").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var l in levels)
            {
                Assert.True(l.GetProperty("height_mm").GetDouble() > 0);
                Assert.True(l.GetProperty("slab_thickness_mm").GetDouble() >= 0);
                Assert.True(l.GetProperty("beam_depth_mm").GetDouble() >= 0);
                // AQC's clear column height is height - slab - beam depth, and it must stay positive
                Assert.True(l.GetProperty("height_mm").GetDouble() - l.GetProperty("slab_thickness_mm").GetDouble() - l.GetProperty("beam_depth_mm").GetDouble() > 0);
            }
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void EveryRowPointsAtALevelInTheFileAndRefsAreUnique(string file)
        {
            var root = Load(file);
            var levelNames = Items(root, "levels").Select(l => l.GetProperty("name").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var section in new[] { "walls", "openings", "columns", "lintels", "rooms", "slabs" })
            {
                var rows = Items(root, section);
                foreach (var r in rows) Assert.Contains(r.GetProperty("level").GetString(), levelNames);
                var refs = rows.Select(r => r.GetProperty("ref").GetString()).ToList();
                Assert.Equal(refs.Count, refs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            }
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void OpeningsPointAtWallsAndAreDoorsOrWindows(string file)
        {
            var root = Load(file);
            var walls = Items(root, "walls").Select(w => w.GetProperty("ref").GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Items(root, "openings"))
            {
                Assert.Contains(o.GetProperty("kind").GetString(), new[] { "Door", "Window" });
                if (o.TryGetProperty("wall_ref", out var w) && !string.IsNullOrEmpty(w.GetString())) Assert.Contains(w.GetString(), walls);
            }
        }

        [Theory, MemberData(nameof(Fixtures))]
        public void StandardMarksAgreeWithTheirWidths(string file)
        {
            var root = Load(file);
            foreach (var o in Items(root, "openings").Concat(Items(root, "opening_schedule")))
            {
                string mark = o.GetProperty("mark").GetString();
                double? std = OpeningStandards.WidthOf(mark);
                if (std.HasValue) Assert.Equal(std.Value, o.GetProperty("width_mm").GetDouble(), 6);
            }
        }

        [Fact]
        public void ScheduleCountsMatchTheOpeningsInTheSmallHouse()
        {
            var root = Load("small-house.json");
            var openings = Items(root, "openings");
            foreach (var s in Items(root, "opening_schedule"))
            {
                int n = openings.Count(o => o.GetProperty("mark").GetString() == s.GetProperty("mark").GetString()
                    && Math.Abs(o.GetProperty("width_mm").GetDouble() - s.GetProperty("width_mm").GetDouble()) < 0.5
                    && Math.Abs(o.GetProperty("height_mm").GetDouble() - s.GetProperty("height_mm").GetDouble()) < 0.5);
                Assert.Equal(s.GetProperty("nos").GetInt32(), n);
            }
        }

        [Fact]
        public void SmallHouseMasonryIsWhatAqcMeasuresAsItsOwnBuilds()
        {
            // AQC reads a thickness of 120 mm or under as its 110 mm wall; the 230 walls stay brick 230
            var walls = Items(Load("small-house.json"), "walls");
            Assert.Contains(walls, w => Math.Abs(w.GetProperty("thickness_mm").GetDouble() - 230) < 0.5);
            Assert.Contains(walls, w => w.GetProperty("thickness_mm").GetDouble() <= 120);
        }
    }
}
