using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// The browser is allowed to look at a draft but never to write into it while it is only working
/// out what to display. These tests pin that boundary, including the gloves case where an
/// unresolvable legacy defIndex used to be silently rewritten to the first glove in the catalog.
/// </summary>
public sealed class UiPresentationStateTests : IDisposable
{
    private readonly string _work;
    private readonly string _presetsRoot;
    private readonly string _playerStatePath;

    public UiPresentationStateTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"cs2localkit-ui-state-{Guid.NewGuid():N}");
        _presetsRoot = Path.Combine(_work, "presets");
        _playerStatePath = Path.Combine(_work, "player-state.json");
        Directory.CreateDirectory(_presetsRoot);
        File.WriteAllText(_playerStatePath, $"{{\"steamId64\":\"{TestFixtures.FakeSteamId64}\"}}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* temp */ }
    }

    private AppServices CreateServices() => new(
        cs2ModRoot: _work,
        catalogCacheRoot: TestFixtures.CatalogDir,
        csgoDir: Path.Combine(_work, "csgo"),
        cs2Root: null,
        activePresetPath: Path.Combine(_work, "active-preset.json"),
        presetsRoot: _presetsRoot,
        backupsRoot: Path.Combine(_work, "backups"),
        playerStatePath: _playerStatePath,
        lockPath: Path.Combine(_work, "lock.json"),
        cs2RunningProbe: () => false,
        imageCacheRoot: Path.Combine(_work, "images"));

    private PresetManagerService LoadPreset(HumanPreset preset, string name = "state.v1.json")
    {
        File.WriteAllText(Path.Combine(_presetsRoot, name), HumanPresetJson.Write(preset));
        var manager = new PresetManagerService(CreateServices(), new MockDialogService());
        Assert.True(manager.LoadPreset(name));
        return manager;
    }

    private static HumanPreset Rebuild(
        HumanPreset from,
        GlovesPreset? ctGloves = null,
        GlovesPreset? tGloves = null,
        KnifeSection? ctKnife = null,
        IReadOnlyList<DefIndexPreset>? ctWeapons = null) => new()
    {
        Kind = from.Kind,
        SchemaVersion = from.SchemaVersion,
        Ct = new TeamPreset
        {
            Weapons = ctWeapons ?? from.Ct.Weapons,
            Knife = ctKnife ?? from.Ct.Knife,
            Gloves = ctGloves ?? from.Ct.Gloves,
        },
        T = new TeamPreset
        {
            Weapons = from.T.Weapons,
            Knife = from.T.Knife,
            Gloves = tGloves ?? from.T.Gloves,
        },
        MusicKitId = from.MusicKitId,
    };

    private static DefIndexPreset WithWear(DefIndexPreset preset, double wear) => new()
    {
        DefIndex = preset.DefIndex,
        Preset = new CosmeticPreset
        {
            Paint = preset.Preset.Paint,
            Seed = preset.Preset.Seed,
            Wear = wear,
            NameTag = preset.Preset.NameTag,
            StatTrak = preset.Preset.StatTrak,
        },
    };

    [Fact]
    public void DisabledGlovesWithUnresolvableIdentity_SurviveEveryPresentationRefresh()
    {
        var manager = LoadPreset(Rebuild(
            HumanPresetTemplate.CreateMinimalValid(),
            ctGloves: new GlovesPreset { Enabled = false, DefIndex = 999999, Paint = 999999, Wear = 0.42, Seed = 777 },
            tGloves: new GlovesPreset { Enabled = false, DefIndex = 5030, Paint = 10037, Wear = 0.11, Seed = 3 }));
        var draft = manager.Draft!;
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Gloves;
        Assert.False(vm.GlovesEnabled);
        Assert.NotEmpty(vm.Items);                       // the catalog still offers real gloves

        // The refreshes that used to rewrite the draft: browse, filter, switch team, re-filter, re-enter.
        vm.SelectedItem = vm.Items[0];
        vm.ItemSearchText = "中文";
        vm.ItemSearchText = "";
        vm.IsCt = false;
        vm.IsCt = true;
        vm.OnlyConfigured = true;
        vm.OnlyConfigured = false;
        vm.RebuildRows();
        vm.Section = CosmeticsSection.Weapons;
        vm.Section = CosmeticsSection.Gloves;

        Assert.Equal(999999, draft.Ct.Gloves.DefIndex);
        Assert.Equal(999999, draft.Ct.Gloves.Paint);
        Assert.Equal(0.42, draft.Ct.Gloves.Wear, 4);
        Assert.Equal(777, draft.Ct.Gloves.Seed);
        Assert.False(draft.Ct.Gloves.Enabled);
        Assert.False(manager.IsDirty);
    }

    [Fact]
    public void GlovesAreOnlyWrittenByAnExplicitUserCommand()
    {
        var manager = LoadPreset(Rebuild(
            HumanPresetTemplate.CreateMinimalValid(),
            ctGloves: new GlovesPreset { Enabled = true, DefIndex = 5030, Paint = 10037, Wear = 0.2, Seed = 5 }));
        var draft = manager.Draft!;
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Gloves;
        vm.SelectedItem = vm.Items.FirstOrDefault(i => i.DefIndex == 5030) ?? vm.Items[0];
        var card = vm.Skins.FirstOrDefault();
        Assert.NotNull(card);

        vm.SelectSkinCommand.Execute(card!);

        Assert.True(draft.IsDirty);
        Assert.Equal(vm.SelectedItem!.DefIndex, draft.Ct.Gloves.DefIndex);
        Assert.Equal(card!.PaintIndex, draft.Ct.Gloves.Paint);
    }

    [Fact]
    public void FilteringAndSelectingRowsNeverMutateTheDraft()
    {
        var manager = LoadPreset(HumanPresetTemplate.CreateMinimalValid());
        var draft = manager.Draft!;
        var snapshot = HumanPresetJson.Write(draft.ToHumanPreset());
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Weapons;
        vm.ItemSearchText = "AWP";
        foreach (var item in vm.Items.Take(5))
        {
            vm.SelectedItem = item;
            vm.SelectedSkin = vm.Skins.FirstOrDefault();
        }
        vm.SkinSearchText = "red";
        vm.SelectedSkin = null;
        vm.IsCt = false;
        vm.IsCt = true;

        Assert.False(manager.IsDirty);
        Assert.Equal(snapshot, HumanPresetJson.Write(draft.ToHumanPreset()));
    }

    [Fact]
    public void ChoosingASkinIsTheOnlyWayAnEntryAppears()
    {
        var manager = LoadPreset(HumanPresetTemplate.CreateMinimalValid());
        var draft = manager.Draft!;
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Weapons;
        vm.SelectedItem = vm.Items.First(i => !i.IsConfigured);
        var target = vm.SelectedItem;
        var card = vm.Skins.First();

        Assert.Empty(draft.Ct.Weapons);
        vm.SelectSkinCommand.Execute(card);

        var entry = Assert.Single(draft.Ct.Weapons);
        Assert.Equal(target.DefIndex, entry.DefIndex);
        Assert.Equal(card.PaintIndex, entry.Paint);
        Assert.True(manager.IsDirty);
    }

    [Fact]
    public void TeamSwitchingPreservesEachSidesOwnSelection()
    {
        var template = HumanPresetTemplate.CreateMinimalValid();
        var manager = LoadPreset(Rebuild(
            template,
            ctKnife: new KnifeSection
            {
                Selected = template.Ct.Knife.Selected,
                Presets = template.Ct.Knife.Presets.Select(p => WithWear(p, 0.03)).ToList(),
            },
            tGloves: template.T.Gloves));
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Knives;
        vm.IsCt = true;
        var ctEntry = vm.Manager.Draft!.Ct.Knife.Presets[0];
        vm.IsCt = false;
        var tEntry = vm.Manager.Draft!.T.Knife.Presets[0];

        Assert.Equal(0.03, ctEntry.Wear, 4);
        Assert.NotEqual(0.03, tEntry.Wear);
        Assert.False(manager.IsDirty);
    }

    [Fact]
    public void DetailArtFollowsTheActualSelection()
    {
        var manager = LoadPreset(HumanPresetTemplate.CreateMinimalValid());
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Weapons;
        vm.SelectedItem = vm.Items.First(i => !i.IsConfigured);
        Assert.Null(vm.DetailImageUrl);            // nothing configured yet, so no stale art

        var card = vm.Skins.First();
        vm.SelectSkinCommand.Execute(card);
        Assert.Equal(card.DetailUrl, vm.DetailImageUrl);

        // Moving to another unconfigured weapon must clear the art rather than keep the old one.
        vm.SelectedItem = vm.Items.First(i => i != vm.SelectedItem && !i.IsConfigured);
        Assert.Null(vm.DetailImageUrl);
    }

    [Fact]
    public void MusicKitArtFollowsTheSelectedKit()
    {
        var manager = LoadPreset(HumanPresetTemplate.CreateMinimalValid());
        var draft = manager.Draft!;
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.MusicKit;
        Assert.Null(vm.MusicKitImageUrl);
        Assert.Equal("未选择音乐盒", vm.MusicKitName);

        var card = vm.FilteredMusicKits.FirstOrDefault(c => c.DetailUrl is not null) ?? vm.FilteredMusicKits.First();
        vm.SelectMusicKitCommand.Execute(card);

        Assert.Equal(card.Id, draft.MusicKitId);
        Assert.Equal(card.DisplayName, vm.MusicKitName);
        Assert.Equal(card.DetailUrl, vm.MusicKitImageUrl);

        vm.ClearMusicKitCommand.Execute(null);
        Assert.Null(draft.MusicKitId);
        Assert.Null(vm.MusicKitImageUrl);
    }

    [Fact]
    public void UnresolvablePaintIsNotSilentlyHighlightedAsTheFirstCard()
    {
        var template = HumanPresetTemplate.CreateMinimalValid();
        var weapon = CatalogIndex.Load(TestFixtures.CatalogDir).GetOrdinaryWeapons().First(w => w.Paints.Count > 0);
        var manager = LoadPreset(Rebuild(
            template,
            ctWeapons: [.. template.Ct.Weapons, new DefIndexPreset
            {
                DefIndex = weapon.DefIndex,
                Preset = new CosmeticPreset { Paint = 999999, Wear = 0.33, Seed = 9 },
            }]));
        var vm = new CosmeticsViewModel(manager);

        vm.Section = CosmeticsSection.Weapons;
        vm.SelectedItem = vm.Items.First(i => i.IsConfigured);

        Assert.NotNull(vm.DetailEntry);
        Assert.Null(vm.SelectedSkin);           // no match means no selection, never the first card
        Assert.Equal("未选择皮肤", vm.DetailPaintName);
        Assert.Equal(999999, manager.Draft!.Ct.Weapons[0].Paint);
        Assert.False(manager.IsDirty);
    }
}
