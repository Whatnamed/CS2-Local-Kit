namespace CS2LocalKit.Core.HumanPresets;

/// <summary>
/// Canonical HumanPreset v1 domain. One file holds both CT and T. All persistent IDs are
/// numeric; the canonical format carries no SteamID and no InventorySimulator runtime
/// details (uid/hash/team bytes). Ordinary weapon identity is decided by CS2's own
/// loadout - cosmetics never rewrite defindexes - so there is deliberately no
/// loadoutIdentity / cosmetic-sharing model and no knife rotation model in v1.
/// Weapon/knife preset collections are kept sorted by numeric defindex.
/// </summary>
public sealed class HumanPreset
{
    public const string ExpectedKind = "cs2-local-kit/human-preset";
    public const int ExpectedSchemaVersion = 1;

    public required string Kind { get; init; }
    public required int SchemaVersion { get; init; }
    public required TeamPreset Ct { get; init; }
    public required TeamPreset T { get; init; }
    public int? MusicKitId { get; init; }
}

public sealed class TeamPreset
{
    public required IReadOnlyList<DefIndexPreset> Weapons { get; init; }
    public required KnifeSection Knife { get; init; }
    public required GlovesPreset Gloves { get; init; }
}

/// <summary>A cosmetic preset bound to one real weapon/knife defindex.</summary>
public sealed class DefIndexPreset
{
    public required int DefIndex { get; init; }
    public required CosmeticPreset Preset { get; init; }
}

public sealed class CosmeticPreset
{
    public required int Paint { get; init; }
    public required int Seed { get; init; }
    public required double Wear { get; init; }
    public string NameTag { get; init; } = "";
    public int? StatTrak { get; init; }
}

public sealed class KnifeSection
{
    /// <summary>The knife identity the preset currently selects. Must be a key of Presets.</summary>
    public required int Selected { get; init; }
    public required IReadOnlyList<DefIndexPreset> Presets { get; init; }
}

public sealed class GlovesPreset
{
    public required bool Enabled { get; init; }
    public int DefIndex { get; init; }
    public int Paint { get; init; }
    public int Seed { get; init; }
    public double Wear { get; init; }
}
