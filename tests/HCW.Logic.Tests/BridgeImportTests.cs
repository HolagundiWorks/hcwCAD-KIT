using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    public class JsonLiteTests
    {
        [Fact]
        public void ReadsObjectsArraysNumbersStringsAndLiterals()
        {
            var v = JsonLite.Parse("{ \"a\": [1, 2.5, -3e2], \"b\": \"x\\ny\\u0041\\\"\", \"c\": true, \"d\": null, \"e\": {} }");
            Assert.Equal(3, JsonLite.List(v, "a").Count);
            Assert.Equal(-300.0, (double)JsonLite.List(v, "a")[2]);
            Assert.Equal("x\nyA\"", JsonLite.Text(v, "b"));
            Assert.True(JsonLite.Bool(v, "c").Value);
            Assert.Null(JsonLite.Get(v, "d"));
            Assert.Empty((Dictionary<string, object>)JsonLite.Get(v, "e"));
        }

        [Theory]
        [InlineData("{ \"a\": }")]
        [InlineData("{ \"a\": 1 ")]
        [InlineData("[1, 2")]
        [InlineData("{ \"a\" 1 }")]
        [InlineData("\"unterminated")]
        [InlineData("{ \"a\": 1 } extra")]
        [InlineData("{ \"a\": \"bad \\q escape\" }")]
        [InlineData("")]
        public void BadJsonThrowsAFormatExceptionThatSaysWhere(string text)
        {
            var ex = Assert.Throws<FormatException>(() => JsonLite.Parse(text));
            Assert.Contains("JSON error", ex.Message);
        }

        [Fact]
        public void SkipsAByteOrderMarkAndRefusesVeryDeepNesting()
        {
            Assert.NotNull(JsonLite.Parse("﻿{ \"a\": 1 }"));
            Assert.Throws<FormatException>(() => JsonLite.Parse(new string('[', 200) + new string(']', 200)));
        }
    }

    public class BridgeImportTests
    {
        private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge", "fixtures", name));

        private static LevelRow Floor(string name, double ffl = 3150, double slab = 150, double ceil = 3000, double lintel = 2100) =>
            new LevelRow { Name = name, FflMm = ffl, SlabMm = slab, CeilingMm = ceil, LintelMm = lintel };

        [Fact]
        public void BothSampleFilesAreRead()
        {
            var f = BridgeImport.Read(Fixture("levels-only.json"));
            Assert.Equal(2, f.Levels.Count);
            Assert.Equal("Ground", f.Levels[0].Name);
            Assert.Equal(3150, f.Levels[0].HeightMm);
            Assert.Equal(new double[] { 300, 375, 450 }, f.BeamDepthsMm.ToArray());
            Assert.Equal("Residence for Mr Rao", f.Project.Name);
            Assert.Equal(186.5, f.Project.SiteAreaM2.Value, 6);
            Assert.Equal("6f1c1b0e-7a52-4d0a-9c1e-2d8a3f4b5c60", f.Source.DrawingId);
            Assert.NotNull(BridgeImport.Read(Fixture("small-house.json")));
        }

        [Fact]
        public void WhatTheExporterWritesTheReaderReadsBack()
        {
            var input = new BridgeInput { Unit = "in", Source = new BridgeSource { DrawingId = "id", Exported = "2026-10-07T00:00:00Z" } };
            input.Project = new BridgeProject { Name = "House \"A\"", SiteAreaM2 = 120.5 };
            input.Levels.Add(new BridgeLevel { Name = "Ground", HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 375, CeilingMm = 3000 });
            input.BeamDepthsMm.Add(375);
            var back = BridgeImport.Read(BridgeExport.ToJson(input));
            Assert.Equal("House \"A\"", back.Project.Name);
            Assert.Equal(120.5, back.Project.SiteAreaM2.Value, 6);
            Assert.Equal(375, back.Levels[0].BeamDepthMm);
            Assert.Equal(3000, back.Levels[0].CeilingMm.Value);
            Assert.Null(back.Levels[0].LintelBottomMm);
            Assert.Equal("mm", back.Unit);
        }

        [Theory]
        [InlineData("[1]", "not an object")]
        [InlineData("{ \"format\": \"other\" }", "hcw-aqc-bridge")]
        [InlineData("{ \"format\": \"hcw-aqc-bridge\", \"version\": 2, \"units\": \"mm\" }", "version 2")]
        [InlineData("{ \"format\": \"hcw-aqc-bridge\", \"version\": 1, \"units\": \"m\" }", "millimetres")]
        public void FilesWeCannotReadAreRefusedWithAReason(string json, string reason)
        {
            var ex = Assert.Throws<ArgumentException>(() => BridgeImport.Read(json));
            Assert.Contains(reason, ex.Message);
        }

        [Fact]
        public void SameDataGivesNoChanges()
        {
            var file = BridgeImport.Read(Fixture("levels-only.json"));
            var data = new ProjectData();
            data.Set("PROJECT_TITLE", "Residence for Mr Rao"); data.Set("OWNER", "S. Rao"); data.Set("ARCHITECT", "Holgundi Consulting Works");
            data.Set("ADDRESS", "12, 4th Cross, Hospet"); data.Set("PID", "12-34-56"); data.Set("SITE_AREA", "186.5"); data.Set("PLOT_USE", "Residential");
            data.BeamDepthsMm = new List<double> { 300, 375, 450 };
            data.SetFloorBeam("Ground", 450); data.SetFloorBeam("First", 450);
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground"), Floor("First") }, data);
            Assert.True(plan.IsEmpty, string.Join("; ", plan.Details.Select(d => d.Value.ToString()).Concat(plan.LevelChanges.Select(c => c.ToString()))));
        }

        [Fact]
        public void ChangedValuesAreListedWithOldAndNew()
        {
            var file = BridgeImport.Read(Fixture("levels-only.json"));
            var data = new ProjectData(); data.Set("OWNER", "Someone else");
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground", slab: 120), Floor("First") }, data);
            Assert.Contains(plan.Details, d => d.Key == "OWNER" && d.Value.From == "Someone else" && d.Value.To == "S. Rao");
            Assert.Contains(plan.Details, d => d.Key == "SITE_AREA" && d.Value.To == "186.5");
            Assert.Contains(plan.LevelChanges, c => c.What == "Ground slab thickness" && c.From == "120" && c.To == "150");
            Assert.Equal(150, plan.Floors[0].SlabMm);
            Assert.Equal(new double[] { 300, 375, 450 }, plan.BeamDepthsMm.ToArray());
        }

        [Fact]
        public void PlinthIsNeverImportedAndANewFloorIsAddedAtTheEnd()
        {
            var file = new BridgeInput();
            file.Levels.Add(new BridgeLevel { Name = "Plinth", HeightMm = 3200, SlabThicknessMm = 150, BeamDepthMm = 450 });
            file.Levels.Add(new BridgeLevel { Name = "Ground", HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 450 });
            file.Levels.Add(new BridgeLevel { Name = "Second", HeightMm = 3000, SlabThicknessMm = 125, BeamDepthMm = 300 });
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground") }, new ProjectData());
            Assert.Equal(new[] { "Ground", "Second" }, plan.Floors.Select(f => f.Name).ToArray());
            Assert.Single(plan.Added);
            Assert.Equal(300, plan.FloorBeams["Second"]);
            Assert.DoesNotContain(plan.Floors, f => f.Name == "Plinth");
        }

        [Fact]
        public void FloorsWhoseNamesDifferAreMatchedByPosition()
        {
            var file = new BridgeInput();
            file.Levels.Add(new BridgeLevel { Name = "Lvl1", HeightMm = 3300, SlabThicknessMm = 150, BeamDepthMm = 450 });
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground") }, new ProjectData());
            Assert.Single(plan.Floors);
            Assert.Equal("Ground", plan.Floors[0].Name);          // the drawing keeps its own name
            Assert.Equal(3300, plan.Floors[0].FflMm);
            Assert.Empty(plan.Added);
        }

        [Fact]
        public void ValuesMissingFromTheFileLeaveTheDrawingAlone()
        {
            var file = new BridgeInput();
            file.Levels.Add(new BridgeLevel { Name = "Ground", HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 0 });
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground", ceil: 2950, lintel: 2000) }, new ProjectData());
            Assert.Equal(2950, plan.Floors[0].CeilingMm);        // the file has no ceiling, so it stays
            Assert.Equal(2000, plan.Floors[0].LintelMm);
            Assert.True(plan.IsEmpty);
        }

        [Fact]
        public void LevelWithNoHeightIsSkippedWithAReason()
        {
            var file = new BridgeInput();
            file.Levels.Add(new BridgeLevel { Name = "Ground", HeightMm = 0 });
            var plan = BridgeImport.Compare(file, new[] { Floor("Ground") }, new ProjectData());
            Assert.Single(plan.Skipped);
        }
    }
}
