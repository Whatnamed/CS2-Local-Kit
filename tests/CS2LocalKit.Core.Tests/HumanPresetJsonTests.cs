using CS2LocalKit.Core.HumanPresets;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public class HumanPresetJsonTests
{
    [Fact]
    public void RoundTrip_ExampleFile_IsSemanticallyStable()
    {
        var text = File.ReadAllText(TestFixtures.PresetPath("example.v1.json"));
        var parsed = HumanPresetJson.Parse(text);
        var written = HumanPresetJson.Write(parsed);
        var reparsed = HumanPresetJson.Parse(written);
        var rewritten = HumanPresetJson.Write(reparsed);

        Assert.Equal(written, rewritten);
        Assert.Equal(HumanPreset.ExpectedKind, parsed.Kind);
        Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal(78, parsed.MusicKitId);
        Assert.Equal(507, parsed.Ct.Knife.Selected);
        Assert.Equal("", parsed.Ct.Knife.Presets.Single(x => x.DefIndex == 507).Preset.NameTag);
        Assert.False(parsed.T.Gloves.Enabled);
        Assert.True(parsed.Ct.Gloves.Enabled);

        // Personal-shaped preset carries a real nameTag through the round trip.
        var personal = TestFixtures.PersonalShapedPreset();
        var personalText = HumanPresetJson.Write(personal);
        var personalReparsed = HumanPresetJson.Parse(personalText);
        Assert.Equal("蓝钢", personalReparsed.Ct.Knife.Presets.Single(x => x.DefIndex == 507).Preset.NameTag);
        Assert.Equal(personalText, HumanPresetJson.Write(personalReparsed));
    }

    [Fact]
    public void Parse_RejectsLoadoutIdentity()
    {
        const string json = """
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "ct": { "weapons": {}, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false },
                  "loadoutIdentity": { "sharedWeaponLinks": { "36": true } } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "musicKitId": null
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("loadoutIdentity is not part of HumanPreset v1", ex.Message);
    }

    [Fact]
    public void Parse_RejectsUnknownTopLevelProperty_AndRuntimeDetails()
    {
        const string json = """
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "steamId64": "76561190000000000",
          "ct": { "weapons": {}, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } }
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("steamId64", ex.Message);
    }

    [Fact]
    public void Parse_RejectsNonNumericWeaponKey()
    {
        const string json = """
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "ct": { "weapons": { "USP-S": { "paint": 1, "seed": 0, "wear": 0.1 } }, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } }
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("not a numeric defindex", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void Parse_RejectsWearOutOfRange(double wear)
    {
        var json = $$"""
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "ct": { "weapons": { "36": { "paint": 258, "seed": 0, "wear": {{wear}} } }, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } }
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("wear out of range", ex.Message);
    }

    [Fact]
    public void Parse_RejectsNegativeStatTrak()
    {
        const string json = """
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "ct": { "weapons": { "36": { "paint": 258, "seed": 0, "wear": 0.1, "statTrak": -5 } }, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } }
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("statTrak must be null or a non-negative integer", ex.Message);
    }

    [Fact]
    public void Parse_RejectsKnifeSelectedWithoutPreset()
    {
        const string json = """
        {
          "kind": "cs2-local-kit/human-preset",
          "schemaVersion": 1,
          "ct": { "weapons": {}, "knife": { "selected": 509, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } }
        }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Contains("has no preset", ex.Message);
    }

    [Fact]
    public void Parse_RejectsWrongKindAndSchemaVersion()
    {
        const string json = """
        { "kind": "something-else", "schemaVersion": 99,
          "ct": { "weapons": {}, "knife": { "selected": 507, "presets": { "507": { "paint": 38, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } },
          "t": { "weapons": {}, "knife": { "selected": 515, "presets": { "515": { "paint": 409, "seed": 0, "wear": 0.01 } } }, "gloves": { "enabled": false } } }
        """;
        var ex = Assert.Throws<HumanPresetFormatException>(() => HumanPresetJson.Parse(json));
        Assert.Matches("kind must be", ex.Message);
        Assert.Matches("schemaVersion must be", ex.Message);
    }
}
