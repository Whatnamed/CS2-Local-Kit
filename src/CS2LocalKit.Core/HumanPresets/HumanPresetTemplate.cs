namespace CS2LocalKit.Core.HumanPresets;

/// <summary>
/// Factory for minimal valid HumanPreset templates. Ensures that newly created presets
/// conform to both domain structure rules and pinned catalog constraints without
/// requiring manual composition of mandatory baseline fields.
/// </summary>
public static class HumanPresetTemplate
{
    // Product decision: Default knives are Karambit (defindex 507, Fade paint 38) for CT
    // and Butterfly Knife (defindex 515, Tiger Tooth paint 409) for T, matching
    // the stable catalog entries and personal preset baseline.
    public const int DefaultCtKnifeDefIndex = 507;
    public const int DefaultCtKnifePaint = 38;
    public const int DefaultTKnifeDefIndex = 515;
    public const int DefaultTKnifePaint = 409;
    public const int DefaultGlovesDefIndex = 5030; // Sport Gloves
    public const int DefaultGlovesPaint = 10037;   // Pandora's Box

    public static HumanPreset CreateMinimalValid() => new HumanPreset
    {
        Kind = HumanPreset.ExpectedKind,
        SchemaVersion = HumanPreset.ExpectedSchemaVersion,
        Ct = new TeamPreset
        {
            Weapons = Array.Empty<DefIndexPreset>(),
            Knife = new KnifeSection
            {
                Selected = DefaultCtKnifeDefIndex,
                Presets = new List<DefIndexPreset>
                {
                    new()
                    {
                        DefIndex = DefaultCtKnifeDefIndex,
                        Preset = new CosmeticPreset
                        {
                            Paint = DefaultCtKnifePaint,
                            Seed = 0,
                            Wear = 0.01,
                            NameTag = "",
                            StatTrak = null,
                        }
                    }
                }
            },
            Gloves = new GlovesPreset
            {
                Enabled = false,
                DefIndex = DefaultGlovesDefIndex,
                Paint = DefaultGlovesPaint,
                Seed = 0,
                Wear = 0.06,
            }
        },
        T = new TeamPreset
        {
            Weapons = Array.Empty<DefIndexPreset>(),
            Knife = new KnifeSection
            {
                Selected = DefaultTKnifeDefIndex,
                Presets = new List<DefIndexPreset>
                {
                    new()
                    {
                        DefIndex = DefaultTKnifeDefIndex,
                        Preset = new CosmeticPreset
                        {
                            Paint = DefaultTKnifePaint,
                            Seed = 0,
                            Wear = 0.01,
                            NameTag = "",
                            StatTrak = null,
                        }
                    }
                }
            },
            Gloves = new GlovesPreset
            {
                Enabled = false,
                DefIndex = DefaultGlovesDefIndex,
                Paint = DefaultGlovesPaint,
                Seed = 0,
                Wear = 0.06,
            }
        },
        MusicKitId = null,
    };
}
