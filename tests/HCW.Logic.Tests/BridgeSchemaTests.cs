using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HCW.AutoCAD.Plugin.Logic;
using Xunit;

namespace HCW.Logic.Tests
{
    /// <summary>
    /// A small checker for the JSON Schema keywords docs/bridge/hcw-aqc-bridge.schema.json uses (type, required, properties, additionalProperties, items, enum,
    /// const, minimum, exclusiveMinimum, minLength), written here because the tests may not add a JSON Schema library. It is used to check the sample files and
    /// what the exporter writes against the schema, so the contract and the code cannot drift apart.
    /// </summary>
    internal static class MiniSchema
    {
        public static List<string> Validate(JsonElement schema, JsonElement value, string path = "$")
        {
            var errors = new List<string>();
            Check(schema, value, path, errors);
            return errors;
        }

        private static void Check(JsonElement s, JsonElement v, string path, List<string> e)
        {
            JsonElement t;
            if (s.TryGetProperty("type", out t))
            {
                string type = t.GetString();
                bool ok = type == "object" ? v.ValueKind == JsonValueKind.Object
                    : type == "array" ? v.ValueKind == JsonValueKind.Array
                    : type == "string" ? v.ValueKind == JsonValueKind.String
                    : type == "boolean" ? (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                    : type == "number" ? v.ValueKind == JsonValueKind.Number
                    : type == "integer" ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _)
                    : true;
                if (!ok) { e.Add(path + ": expected " + type + " but found " + v.ValueKind); return; }
            }
            JsonElement c;
            if (s.TryGetProperty("const", out c) && c.ToString() != v.ToString()) e.Add(path + ": must be " + c);
            if (s.TryGetProperty("enum", out c) && !c.EnumerateArray().Any(x => x.ToString() == v.ToString())) e.Add(path + ": " + v + " is not one of " + c);
            if (v.ValueKind == JsonValueKind.Number)
            {
                if (s.TryGetProperty("minimum", out c) && v.GetDouble() < c.GetDouble()) e.Add(path + ": " + v + " is below " + c);
                if (s.TryGetProperty("exclusiveMinimum", out c) && v.GetDouble() <= c.GetDouble()) e.Add(path + ": " + v + " must be above " + c);
            }
            if (v.ValueKind == JsonValueKind.String && s.TryGetProperty("minLength", out c) && v.GetString().Length < c.GetInt32()) e.Add(path + ": too short");
            if (v.ValueKind == JsonValueKind.Object)
            {
                if (s.TryGetProperty("required", out c))
                    foreach (var r in c.EnumerateArray()) if (!v.TryGetProperty(r.GetString(), out _)) e.Add(path + ": missing " + r.GetString());
                JsonElement props;
                bool hasProps = s.TryGetProperty("properties", out props);
                foreach (var p in v.EnumerateObject())
                {
                    JsonElement sub;
                    if (hasProps && props.TryGetProperty(p.Name, out sub)) Check(sub, p.Value, path + "." + p.Name, e);
                    else if (s.TryGetProperty("additionalProperties", out c) && c.ValueKind == JsonValueKind.False) e.Add(path + ": unexpected property " + p.Name);
                }
            }
            if (v.ValueKind == JsonValueKind.Array && s.TryGetProperty("items", out c))
            {
                int i = 0;
                foreach (var item in v.EnumerateArray()) Check(c, item, path + "[" + i++ + "]", e);
            }
        }
    }

    public class BridgeSchemaTests
    {
        private static JsonElement Schema() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge", "hcw-aqc-bridge.schema.json"))).RootElement;
        private static JsonElement Fixture(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bridge", "fixtures", name))).RootElement;

        [Theory]
        [InlineData("levels-only.json")]
        [InlineData("small-house.json")]
        public void SampleFilesValidateAgainstTheSchema(string file)
        {
            var errors = MiniSchema.Validate(Schema(), Fixture(file));
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        }

        [Theory]
        [InlineData("mm")]
        [InlineData("in")]
        [InlineData("ft")]
        public void WhatTheExporterWritesValidatesAgainstTheSchema(string unit)
        {
            double k = 1000.0 / BridgeExport.MmPerUnit(unit);   // metres -> drawing units
            double k2 = k * k;
            var input = new BridgeInput
            {
                Unit = unit,
                Source = new BridgeSource { DrawingId = "id", Exported = "2026-10-07T00:00:00Z" },
                Project = new BridgeProject { Name = "P" },
                Levels = { new BridgeLevel { Name = "Ground", HeightMm = 3150, SlabThicknessMm = 150, BeamDepthMm = 450 } },
                Walls = { new BridgeWall { Ref = "W1", Level = "Ground", Length = 5 * k, Height = 3 * k, Thickness = 0.23 * k } },
                Openings = { new BridgeOpening { Ref = "A", Kind = "Door", Mark = "D2", Level = "Ground", Width = 0.9 * k, Height = 2.1 * k, Sill = 0, Type = "single", WallRef = "W1" } },
                OpeningSchedule = { new BridgeScheduleRow { Mark = "D2", Kind = "Door", Width = 0.9 * k, Height = 2.1 * k, Nos = 1 } },
                Columns = { new BridgeColumn { Ref = "C", Mark = "C1", Level = "Ground", Width = 0.23 * k, Depth = 0.45 * k } },
                Lintels = { new BridgeLintel { Ref = "L", Level = "Ground", Opening = 0.9 * k, Width = 0.23 * k, Depth = 0.15 * k } },
                Rooms = { new BridgeRoom { Ref = "R", Name = "LIVING", Level = "Ground", Area = 22.5 * k2 } },
                Slabs = { new BridgeSlab { Ref = "S", Level = "Ground", Area = 54 * k2, Thickness = 0.15 * k, Rectangular = true } },
            };
            var errors = MiniSchema.Validate(Schema(), JsonDocument.Parse(BridgeExport.ToJson(input)).RootElement);
            Assert.True(errors.Count == 0, string.Join("; ", errors));
        }

        [Fact]
        public void TheCheckerFindsMissingFieldsWrongTypesAndBadNumbers()
        {
            var bad = JsonDocument.Parse(@"{ ""format"": ""hcw-aqc-bridge"", ""version"": 1, ""units"": ""cm"", ""source"": { ""app"": ""x"" },
                ""levels"": [ { ""name"": """", ""height_mm"": 0, ""slab_thickness_mm"": -1, ""beam_depth_mm"": ""450"" } ] }").RootElement;
            var errors = MiniSchema.Validate(Schema(), bad);
            Assert.Contains(errors, x => x.Contains("units"));
            Assert.Contains(errors, x => x.Contains("missing drawing_id"));
            Assert.Contains(errors, x => x.Contains("missing exported"));
            Assert.Contains(errors, x => x.Contains("name") && x.Contains("short"));
            Assert.Contains(errors, x => x.Contains("height_mm"));
            Assert.Contains(errors, x => x.Contains("slab_thickness_mm"));
            Assert.Contains(errors, x => x.Contains("beam_depth_mm") && x.Contains("expected number"));
        }
    }
}
