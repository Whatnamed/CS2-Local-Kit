using CS2LocalKit.Core.Catalog;

namespace CS2LocalKit.Core.HumanPresets;

/// <summary>
/// Semantic validation of HumanPreset objects. Deliberately separate from
/// HumanPresetJson.Parse(): the JSON format gate only proves a file conforms to the
/// v1 schema, while the C4 UI will construct/modify HumanPreset objects in memory -
/// so mutation boundaries (PresetStore.Save, FixtureApplier.Apply) must re-validate
/// the object itself, and Apply additionally verifies catalog membership against the
/// pinned ByMykel/CSGO-API snapshot (same provenance as the C2-accepted pipeline).
/// </summary>
public static class HumanPresetValidator
{
    /// <summary>Domain/structure rules that need no external data.</summary>
    public static IReadOnlyList<string> ValidateDomain(HumanPreset? preset)
    {
        var problems = new List<string>();
        if (preset is null)
        {
            problems.Add("preset is null");
            return problems;
        }

        if (preset.Kind != HumanPreset.ExpectedKind)
            problems.Add($"kind must be '{HumanPreset.ExpectedKind}' (got '{preset.Kind}')");
        if (preset.SchemaVersion != HumanPreset.ExpectedSchemaVersion)
            problems.Add($"schemaVersion must be {HumanPreset.ExpectedSchemaVersion} (got '{preset.SchemaVersion}')");

        problems.AddRange(ValidateTeam("ct", preset.Ct));
        problems.AddRange(ValidateTeam("t", preset.T));

        if (preset.MusicKitId is { } music && music < 0)
            problems.Add($"musicKitId must be a non-negative integer or null (got '{music}')");

        return problems;
    }

    /// <summary>Domain rules + catalog membership against the pinned snapshot.</summary>
    public static IReadOnlyList<string> Validate(HumanPreset? preset, CatalogIndex? catalog)
    {
        var problems = new List<string>();
        if (catalog is null)
        {
            problems.Add("catalog snapshot is not available");
            return problems;
        }

        problems.AddRange(ValidateDomain(preset));
        if (preset is null || problems.Count > 0)
            return problems; // catalog checks are meaningless on a structurally broken preset

        foreach (var (team, t) in new[] { ("ct", preset.Ct), ("t", preset.T) })
        {
            foreach (var w in t.Weapons)
            {
                if (!catalog.TryGetWeapon(w.DefIndex, out var weaponDef))
                {
                    problems.Add($"{team}.weapons defindex {w.DefIndex} does not exist in the catalog");
                    continue;
                }
                if (weaponDef.IsKnife)
                    problems.Add($"{team}.weapons defindex {w.DefIndex} is a knife - knife cosmetics belong in {team}.knife.presets (identity override is not supported)");
                else if (!catalog.HasPaint(w.DefIndex, w.Preset.Paint))
                    problems.Add($"{team}.weapons defindex {w.DefIndex} ({weaponDef.Name}) has no paint kit {w.Preset.Paint}");
            }

            foreach (var k in t.Knife.Presets)
            {
                if (!catalog.TryGetWeapon(k.DefIndex, out var knifeDef))
                    problems.Add($"{team}.knife.presets defindex {k.DefIndex} does not exist in the catalog");
                else if (!knifeDef.IsKnife)
                    problems.Add($"{team}.knife.presets defindex {k.DefIndex} ({knifeDef.Name}) is not a knife");
                else if (!catalog.HasPaint(k.DefIndex, k.Preset.Paint))
                    problems.Add($"{team}.knife.presets defindex {k.DefIndex} ({knifeDef.Name}) has no paint kit {k.Preset.Paint}");
            }

            if (t.Knife.Presets.Count > 0 && t.Knife.Presets.All(x => x.DefIndex != t.Knife.Selected))
                problems.Add($"{team}.knife.selected ({t.Knife.Selected}) has no preset in {team}.knife.presets");

            if (t.Gloves.Enabled)
            {
                if (!catalog.TryGetWeapon(t.Gloves.DefIndex, out var gloveDef))
                    problems.Add($"{team}.gloves defindex {t.Gloves.DefIndex} does not exist in the catalog");
                else if (!gloveDef.IsGloves)
                    problems.Add($"{team}.gloves defindex {t.Gloves.DefIndex} ({gloveDef.Name}) is not a gloves item");
                else if (!catalog.HasPaint(t.Gloves.DefIndex, t.Gloves.Paint))
                    problems.Add($"{team}.gloves defindex {t.Gloves.DefIndex} ({gloveDef.Name}) has no paint kit {t.Gloves.Paint}");
            }
        }

        if (preset.MusicKitId is { } music && !catalog.TryGetMusicKit(music, out _))
            problems.Add($"musicKitId {music} does not exist in the catalog");

        return problems;
    }

    /// <summary>Validates domain rules; throws HumanPresetValidationException on any problem.</summary>
    public static void EnsureDomainValid(HumanPreset? preset)
    {
        var problems = ValidateDomain(preset);
        if (problems.Count > 0) throw new HumanPresetValidationException(problems);
    }

    /// <summary>Validates domain + catalog membership; throws HumanPresetValidationException on any problem.</summary>
    public static void EnsureValid(HumanPreset? preset, CatalogIndex? catalog)
    {
        var problems = Validate(preset, catalog);
        if (problems.Count > 0) throw new HumanPresetValidationException(problems);
    }

    private static IEnumerable<string> ValidateTeam(string team, TeamPreset? t)
    {
        var problems = new List<string>();
        if (t is null)
        {
            problems.Add($"missing team object '{team}'");
            return problems;
        }

        if (t.Weapons is null)
        {
            problems.Add($"{team}.weapons missing");
        }
        else
        {
            foreach (var g in t.Weapons.GroupBy(w => w.DefIndex).Where(g => g.Count() > 1))
                problems.Add($"{team}.weapons contains duplicate defindex {g.Key}");
            foreach (var w in t.Weapons)
                problems.AddRange(ValidateCosmetic($"{team}.weapons[{w.DefIndex}]", w.DefIndex, w.Preset));
        }

        if (t.Knife is null)
        {
            problems.Add($"{team}.knife missing");
        }
        else if (t.Knife.Presets is null)
        {
            problems.Add($"{team}.knife.presets missing");
        }
        else
        {
            if (t.Knife.Selected <= 0)
                problems.Add($"{team}.knife.selected must be a positive defindex (got '{t.Knife.Selected}')");
            if (t.Knife.Presets.Count == 0)
                problems.Add($"{team}.knife.presets is empty - a knife preset must contain at least the selected knife");
            if (t.Knife.Presets.All(x => x.DefIndex != t.Knife.Selected))
                problems.Add($"{team}.knife.selected ({t.Knife.Selected}) has no preset in {team}.knife.presets");
            foreach (var g in t.Knife.Presets.GroupBy(w => w.DefIndex).Where(g => g.Count() > 1))
                problems.Add($"{team}.knife.presets contains duplicate defindex {g.Key}");
            foreach (var k in t.Knife.Presets)
                problems.AddRange(ValidateCosmetic($"{team}.knife.presets[{k.DefIndex}]", k.DefIndex, k.Preset));
        }

        problems.AddRange(ValidateGloves(team, t.Gloves));
        return problems;
    }

    private static IEnumerable<string> ValidateCosmetic(string path, int defIndex, CosmeticPreset c)
    {
        var problems = new List<string>();
        if (defIndex <= 0)
            problems.Add($"{path}: defindex must be a positive integer (got '{defIndex}')");
        if (c is null)
        {
            problems.Add($"{path}: missing cosmetic preset");
            return problems;
        }
        if (c.Paint < 0) problems.Add($"{path}.paint must be a non-negative integer");
        if (c.Seed < 0) problems.Add($"{path}.seed must be a non-negative integer");
        if (c.Wear is < 0 or > 1) problems.Add($"{path}.wear out of range 0..1");
        if (c.StatTrak is { } st && st < 0) problems.Add($"{path}.statTrak must be null or a non-negative integer");
        return problems;
    }

    private static IEnumerable<string> ValidateGloves(string team, GlovesPreset? g)
    {
        var problems = new List<string>();
        if (g is null)
        {
            problems.Add($"{team}.gloves missing");
            return problems;
        }
        if (!g.Enabled) return problems;

        if (g.DefIndex <= 0) problems.Add($"{team}.gloves.defindex must be a positive defindex when gloves are enabled");
        if (g.Paint <= 0) problems.Add($"{team}.gloves.paint must be a positive paint kit when gloves are enabled");
        if (g.Seed < 0) problems.Add($"{team}.gloves.seed must be a non-negative integer");
        if (g.Wear is < 0 or > 1) problems.Add($"{team}.gloves.wear out of range 0..1");
        return problems;
    }
}

/// <summary>Thrown when a HumanPreset object fails domain or catalog validation.</summary>
public sealed class HumanPresetValidationException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public HumanPresetValidationException(IReadOnlyList<string> problems)
        : base("invalid HumanPreset:\n  " + string.Join("\n  ", problems))
    {
        Problems = problems;
    }
}
