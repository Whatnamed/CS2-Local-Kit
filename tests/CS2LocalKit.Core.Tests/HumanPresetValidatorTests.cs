using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// HumanPresetValidator: domain rules that need no external data, and catalog
/// membership against the pinned snapshot. These run at the mutation boundaries
/// (PresetStore.Save, FixtureApplier.Apply) because a C4 UI can construct preset
/// objects in memory that never went through JSON parsing.
/// </summary>
public sealed class HumanPresetValidatorTests
{
    private static CatalogIndex Catalog() => CatalogIndex.Load(TestFixtures.CatalogDir);

    [Fact]
    public void AcceptedFixtures_PassDomainAndCatalogValidation()
    {
        Assert.Empty(HumanPresetValidator.ValidateDomain(TestFixtures.ExamplePreset()));
        Assert.Empty(HumanPresetValidator.ValidateDomain(TestFixtures.PersonalShapedPreset()));
        Assert.Empty(HumanPresetValidator.Validate(TestFixtures.ExamplePreset(), Catalog()));
        Assert.Empty(HumanPresetValidator.Validate(TestFixtures.PersonalShapedPreset(), Catalog()));
    }

    [Fact]
    public void Domain_WithoutCatalog_StillValidates()
    {
        // Domain validation is available where no catalog is configured (PresetStore.Save).
        var problems = HumanPresetValidator.ValidateDomain(TestFixtures.PersonalShapedPreset(paintOverride: 3));
        Assert.Empty(problems); // paint membership is a catalog concern, not a domain one
    }

    [Theory]
    [InlineData(-0.1)]  // below range
    [InlineData(1.5)]   // above range
    public void Domain_WearOutOfRange_IsRejected(double wear)
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var weapons = preset.Ct.Weapons
            .Select(w => w.DefIndex == 36
                ? new DefIndexPreset { DefIndex = 36, Preset = new CosmeticPreset { Paint = 258, Seed = 0, Wear = wear } }
                : w)
            .ToList();
        var broken = Preset(preset, ctWeapons: weapons);
        Assert.Contains(HumanPresetValidator.ValidateDomain(broken), p => p.Contains("wear"));
    }

    [Fact]
    public void Domain_NegativeValues_AreRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset,
            musicKitId: -1,
            ctWeapon: new DefIndexPreset { DefIndex = -5, Preset = new CosmeticPreset { Paint = -1, Seed = -1, Wear = 0.1, StatTrak = -2 } });
        var problems = HumanPresetValidator.ValidateDomain(broken);
        Assert.Contains(problems, p => p.Contains("defindex"));
        Assert.Contains(problems, p => p.Contains("paint"));
        Assert.Contains(problems, p => p.Contains("seed"));
        Assert.Contains(problems, p => p.Contains("statTrak"));
        Assert.Contains(problems, p => p.Contains("musicKitId"));
    }

    [Fact]
    public void Domain_KnifeSelectedWithoutPreset_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctKnife: new KnifeSection { Selected = 520, Presets = [preset.Ct.Knife.Presets[0]] });
        var problems = HumanPresetValidator.ValidateDomain(broken);
        Assert.Contains(problems, p => p.Contains("knife.selected (520) has no preset"));
    }

    [Fact]
    public void Domain_EmptyKnifeSection_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctKnife: new KnifeSection { Selected = 507, Presets = [] });
        Assert.Contains(HumanPresetValidator.ValidateDomain(broken), p => p.Contains("knife.presets is empty"));
    }

    [Fact]
    public void Domain_DuplicateDefindexes_AreRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var weapons = preset.Ct.Weapons.Concat(preset.Ct.Weapons).ToList();
        var broken = Preset(preset, ctWeapons: weapons);
        Assert.Contains(HumanPresetValidator.ValidateDomain(broken), p => p.Contains("duplicate defindex"));
    }

    [Fact]
    public void Domain_GlovesEnabledWithoutDefindex_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctGloves: new GlovesPreset { Enabled = true, DefIndex = 0, Paint = 0, Seed = 0, Wear = 0.1 });
        var problems = HumanPresetValidator.ValidateDomain(broken);
        Assert.Contains(problems, p => p.Contains("gloves.defindex"));
        Assert.Contains(problems, p => p.Contains("gloves.paint"));
    }

    [Fact]
    public void Domain_WrongKindOrSchema_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = new HumanPreset
        {
            Kind = "something/else",
            SchemaVersion = 2,
            Ct = preset.Ct,
            T = preset.T,
            MusicKitId = preset.MusicKitId,
        };
        var problems = HumanPresetValidator.ValidateDomain(broken);
        Assert.Contains(problems, p => p.Contains("kind"));
        Assert.Contains(problems, p => p.Contains("schemaVersion"));
    }

    [Fact]
    public void Catalog_WrongPaintMembership_IsRejected()
    {
        var problems = HumanPresetValidator.Validate(TestFixtures.PersonalShapedPreset(paintOverride: 3), Catalog());
        Assert.Contains(problems, p => p.Contains("no paint kit 3"));
    }

    [Fact]
    public void Catalog_UnknownDefindex_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctWeapon: new DefIndexPreset { DefIndex = 12345, Preset = new CosmeticPreset { Paint = 1, Seed = 0, Wear = 0.1 } });
        var problems = HumanPresetValidator.Validate(broken, Catalog());
        Assert.Contains(problems, p => p.Contains("defindex 12345 does not exist"));
    }

    [Fact]
    public void Catalog_KnifeDefindexInWeaponsSection_IsRejectedAsIdentityOverride()
    {
        // Putting a knife defindex into the weapons section is exactly the identity
        // override shape the canonical scope forbids - rejected at validation.
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctWeapon: new DefIndexPreset { DefIndex = 507, Preset = new CosmeticPreset { Paint = 38, Seed = 0, Wear = 0.01 } });
        var problems = HumanPresetValidator.Validate(broken, Catalog());
        Assert.Contains(problems, p => p.Contains("is a knife"));
    }

    [Fact]
    public void Catalog_GunDefindexInKnifeSection_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, ctKnife: new KnifeSection
        {
            Selected = 36,
            Presets = [new DefIndexPreset { DefIndex = 36, Preset = new CosmeticPreset { Paint = 258, Seed = 0, Wear = 0.01 } }],
        });
        var problems = HumanPresetValidator.Validate(broken, Catalog());
        Assert.Contains(problems, p => p.Contains("is not a knife"));
    }

    [Fact]
    public void Catalog_GlovesMembership_IsEnforced()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        // A gun paint on an enabled glove.
        var broken = Preset(preset, ctGloves: new GlovesPreset { Enabled = true, DefIndex = 5030, Paint = 258, Seed = 0, Wear = 0.1 });
        Assert.Contains(HumanPresetValidator.Validate(broken, Catalog()), p => p.Contains("has no paint kit 258"));

        // A gun defindex as gloves.
        var broken2 = Preset(preset, ctGloves: new GlovesPreset { Enabled = true, DefIndex = 36, Paint = 258, Seed = 0, Wear = 0.1 });
        Assert.Contains(HumanPresetValidator.Validate(broken2, Catalog()), p => p.Contains("is not a gloves item"));
    }

    [Fact]
    public void Catalog_DisabledGloves_IgnoreLeftoverFields()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, tGloves: new GlovesPreset { Enabled = false, DefIndex = 12345, Paint = 999, Seed = 0, Wear = 0.1 });
        Assert.Empty(HumanPresetValidator.Validate(broken, Catalog()));
    }

    [Fact]
    public void Catalog_UnknownMusicKit_IsRejected()
    {
        var preset = TestFixtures.PersonalShapedPreset();
        var broken = Preset(preset, musicKitId: 999);
        Assert.Contains(HumanPresetValidator.Validate(broken, Catalog()), p => p.Contains("musicKitId 999 does not exist"));
    }

    [Fact]
    public void EnsureValid_ThrowsWithProblemList()
    {
        var ex = Assert.Throws<HumanPresetValidationException>(() =>
            HumanPresetValidator.EnsureValid(TestFixtures.PersonalShapedPreset(paintOverride: 3), Catalog()));
        Assert.NotEmpty(ex.Problems);

        Assert.Throws<HumanPresetValidationException>(() =>
            HumanPresetValidator.EnsureValid(TestFixtures.ExamplePreset(), null)); // no catalog -> fail closed
    }

    private static HumanPreset Preset(
        HumanPreset basePreset,
        IReadOnlyList<DefIndexPreset>? ctWeapons = null,
        DefIndexPreset? ctWeapon = null,
        KnifeSection? ctKnife = null,
        GlovesPreset? ctGloves = null,
        KnifeSection? tKnife = null,
        GlovesPreset? tGloves = null,
        int? musicKitId = null)
    {
        var ctWeaponsFinal = ctWeapons ?? basePreset.Ct.Weapons;
        if (ctWeapon is not null) ctWeaponsFinal = ctWeaponsFinal.Append(ctWeapon).ToList();
        return new HumanPreset
        {
            Kind = basePreset.Kind,
            SchemaVersion = basePreset.SchemaVersion,
            Ct = new TeamPreset
            {
                Weapons = ctWeaponsFinal,
                Knife = ctKnife ?? basePreset.Ct.Knife,
                Gloves = ctGloves ?? basePreset.Ct.Gloves,
            },
            T = new TeamPreset
            {
                Weapons = basePreset.T.Weapons,
                Knife = tKnife ?? basePreset.T.Knife,
                Gloves = tGloves ?? basePreset.T.Gloves,
            },
            MusicKitId = musicKitId ?? basePreset.MusicKitId,
        };
    }
}
