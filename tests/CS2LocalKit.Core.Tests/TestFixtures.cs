using CS2LocalKit.Core.HumanPresets;

namespace CS2LocalKit.Core.Tests;

/// <summary>Shared fixture loading + a canonical example preset builder.</summary>
internal static class TestFixtures
{
    public static string FixturesDir
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir is not null && !Directory.Exists(Path.Combine(dir, "fixtures")))
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            return Path.Combine(dir!, "fixtures");
        }
    }

    public static string PresetPath(string name) => Path.Combine(FixturesDir, "presets", name);
    public static string CatalogDir => Path.Combine(FixturesDir, "catalog");
    public static string CatalogPath(string name) => Path.Combine(CatalogDir, name);
    public static string GoldenPath(string name) => Path.Combine(FixturesDir, name);

    public const string FakeSteamId64 = "76561190000000000";

    /// <summary>Canonical example preset matching fixtures/presets/example.v1.json.</summary>
    public static HumanPreset ExamplePreset() => HumanPresetJson.Parse(File.ReadAllText(PresetPath("example.v1.json")));

    public static HumanPreset PersonalShapedPreset(int paintOverride = 0) => new()
    {
        Kind = HumanPreset.ExpectedKind,
        SchemaVersion = 1,
        Ct = new TeamPreset
        {
            Weapons =
            [
                new DefIndexPreset
                {
                    DefIndex = 36,
                    Preset = new CosmeticPreset { Paint = paintOverride == 0 ? 258 : paintOverride, Seed = 0, Wear = 0.06 },
                },
            ],
            Knife = new KnifeSection
            {
                Selected = 507,
                Presets = [new DefIndexPreset { DefIndex = 507, Preset = new CosmeticPreset { Paint = 38, Seed = 0, Wear = 0.01, NameTag = "蓝钢" } }],
            },
            Gloves = new GlovesPreset { Enabled = true, DefIndex = 5030, Paint = 10037, Seed = 0, Wear = 0.06 },
        },
        T = new TeamPreset
        {
            Weapons =
            [
                new DefIndexPreset
                {
                    DefIndex = 7,
                    Preset = new CosmeticPreset { Paint = 316, Seed = 0, Wear = 0.07 },
                },
            ],
            Knife = new KnifeSection
            {
                Selected = 515,
                Presets = [new DefIndexPreset { DefIndex = 515, Preset = new CosmeticPreset { Paint = 409, Seed = 0, Wear = 0.01 } }],
            },
            Gloves = new GlovesPreset { Enabled = false, DefIndex = 5031, Paint = 10016, Seed = 0, Wear = 0.1 },
        },
        MusicKitId = 78,
    };
}
