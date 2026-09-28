using System.Text.Json;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Projection;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.Core.Store;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class UiIntegrationTests : IDisposable
{
    private readonly string _work;
    private readonly string _cs2Root;
    private readonly string _csgoDir;
    private readonly string _presetsRoot;
    private readonly string _backupsRoot;
    private readonly string _activePresetPath;
    private readonly string _playerStatePath;
    private readonly string _catalogCacheRoot;
    private readonly string _lockPath;

    public UiIntegrationTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"cs2localkit-ui-test-{Guid.NewGuid():N}");
        _cs2Root = Path.Combine(_work, "cs2");
        _csgoDir = Path.Combine(_cs2Root, "game", "csgo");
        _presetsRoot = Path.Combine(_work, "presets");
        _backupsRoot = Path.Combine(_work, "backups");
        _activePresetPath = Path.Combine(_work, "active-preset.json");
        _playerStatePath = Path.Combine(_work, "player-state.json");
        _catalogCacheRoot = TestFixtures.CatalogDir;
        _lockPath = Path.Combine(_work, "lock.json");

        Directory.CreateDirectory(_csgoDir);
        Directory.CreateDirectory(_presetsRoot);
        Directory.CreateDirectory(_backupsRoot);

        // Player state with fake steamId
        File.WriteAllText(_playerStatePath, JsonSerializer.Serialize(new { steamId64 = TestFixtures.FakeSteamId64 }));

        // Plugin dll matching lock
        var pluginDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator");
        Directory.CreateDirectory(pluginDir);
        var dummyDll = new byte[] { 1, 2, 3 };
        var dllPath = Path.Combine(pluginDir, "InventorySimulator.dll");
        File.WriteAllBytes(dllPath, dummyDll);
        var dllSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dummyDll)).ToLowerInvariant();

        // Lock file
        File.WriteAllText(_lockPath, $$"""
        {
          "testedCs2Build": { "patchVersion": "1.41.8.5", "clientVersion": "2000918", "buildId": null },
          "acceptedRuntime": { "patchedDllSha256": "{{dllSha}}" }
        }
        """);

        // Metamod and CS-Sharp native components
        var mmDir = Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64");
        Directory.CreateDirectory(mmDir);
        File.WriteAllText(Path.Combine(mmDir, "metamod.2.cs2.dll"), "");
        Directory.CreateDirectory(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64"));

        // Active preset
        new ActivePresetState(_activePresetPath).SetActive("test-preset.v1.json");

        // Steam.inf
        var steamInf = Path.Combine(_csgoDir, "steam.inf");
        File.WriteAllText(steamInf, "PatchVersion=1.41.8.5\r\nClientVersion=2000918\r\n");

        // Gameinfo
        var gameinfo = Path.Combine(_csgoDir, "gameinfo.gi");
        File.WriteAllText(gameinfo, "Game_LowViolence\tcsgo_lv\r\nGame\tcsgo\r\nGame\tcsgo/addons/metamod\r\n");

        // Existing preset
        var minimal = HumanPresetTemplate.CreateMinimalValid();
        File.WriteAllText(Path.Combine(_presetsRoot, "test-preset.v1.json"), HumanPresetJson.Write(minimal));

        // Installed fixture file
        var fixtureDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator");
        Directory.CreateDirectory(fixtureDir);
        var fixturePath = Path.Combine(fixtureDir, "inventories.json");
        var projector = new InventorySimulatorProjector();
        File.WriteAllText(fixturePath, projector.Project(minimal, TestFixtures.FakeSteamId64));
    }

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            try { Directory.Delete(_work, recursive: true); } catch { }
        }
    }

    private AppServices CreateTestServices(Func<bool>? cs2RunningProbe = null)
    {
        return new AppServices(
            cs2ModRoot: _work,
            catalogCacheRoot: _catalogCacheRoot,
            csgoDir: _csgoDir,
            cs2Root: _cs2Root,
            activePresetPath: _activePresetPath,
            presetsRoot: _presetsRoot,
            backupsRoot: _backupsRoot,
            playerStatePath: _playerStatePath,
            lockPath: _lockPath,
            cs2RunningProbe: cs2RunningProbe ?? (() => false));
    }

    // 1. Catalog Enumeration and Filtering
    [Fact]
    public void CatalogEnumerationAndFilter_SeparatesCategoriesCorrectly()
    {
        var catalog = CatalogIndex.Load(_catalogCacheRoot);

        var ordinary = catalog.GetOrdinaryWeapons();
        Assert.NotEmpty(ordinary);
        Assert.All(ordinary, w => Assert.True(w.IsOrdinaryWeapon && !w.IsKnife && !w.IsGloves));

        var knives = catalog.GetKnives();
        Assert.NotEmpty(knives);
        Assert.All(knives, k => Assert.True(k.IsKnife));

        var gloves = catalog.GetGloves();
        Assert.NotEmpty(gloves);
        Assert.All(gloves, g => Assert.True(g.IsGloves));

        var musicKits = catalog.GetMusicKits();
        Assert.NotEmpty(musicKits);

        // Paints for Karambit (507)
        var paints = catalog.GetPaintsForWeapon(507);
        Assert.Contains(paints, p => p.PaintIndex == 38); // Fade

        // Search
        var searched = catalog.SearchOrdinaryWeapons("P250");
        Assert.Contains(searched, w => w.DefIndex == 36);

        var searchedMusic = catalog.SearchMusicKits("Austin");
        Assert.Contains(searchedMusic, m => m.Id == 78);
    }

    // 2. New Preset Template is Valid
    [Fact]
    public void NewPresetTemplate_IsValidUnderDomainAndCatalog()
    {
        var catalog = CatalogIndex.Load(_catalogCacheRoot);
        var preset = HumanPresetTemplate.CreateMinimalValid();

        var domainProblems = HumanPresetValidator.ValidateDomain(preset);
        Assert.Empty(domainProblems);

        var catalogProblems = HumanPresetValidator.Validate(preset, catalog);
        Assert.Empty(catalogProblems);

        // Verification of product decisions
        Assert.Equal(HumanPresetTemplate.DefaultCtKnifeDefIndex, preset.Ct.Knife.Selected);
        Assert.Equal(HumanPresetTemplate.DefaultTKnifeDefIndex, preset.T.Knife.Selected);
        Assert.False(preset.Ct.Gloves.Enabled);
        Assert.False(preset.T.Gloves.Enabled);
        Assert.Empty(preset.Ct.Weapons);
        Assert.Empty(preset.T.Weapons);
        Assert.Null(preset.MusicKitId);
    }

    // 3. Editor Draft -> Valid HumanPreset
    [Fact]
    public void EditorDraft_ToHumanPreset_ProducesValidStructure()
    {
        var catalog = CatalogIndex.Load(_catalogCacheRoot);
        var services = CreateTestServices();
        var dialog = new MockDialogService();
        var manager = new PresetManagerService(services, dialog);

        manager.LoadPreset("test-preset.v1.json");
        var draft = manager.Draft!;
        Assert.NotNull(draft);

        // Add a weapon cosmetic (P250 defindex 36, paint 258)
        draft.Ct.Weapons.Add(new WeaponCosmeticDraft
        {
            DefIndex = 36,
            WeaponName = "P250",
            Paint = 258,
            PaintName = "Mehndi",
            Wear = 0.05,
            Seed = 123,
            NameTag = "TestGun",
            StatTrakEnabled = true,
            StatTrakCount = 42,
        });

        var converted = draft.ToHumanPreset();
        var problems = HumanPresetValidator.Validate(converted, catalog);
        Assert.Empty(problems);

        var p250 = Assert.Single(converted.Ct.Weapons);
        Assert.Equal(36, p250.DefIndex);
        Assert.Equal(258, p250.Preset.Paint);
        Assert.Equal(42, p250.Preset.StatTrak);
        Assert.Equal("TestGun", p250.Preset.NameTag);
    }

    // 4. Invalid Wear / Seed / StatTrak handling
    [Fact]
    public void DraftValidation_ClampsOrRejectsInvalidValues()
    {
        var weaponDraft = new WeaponCosmeticDraft
        {
            DefIndex = 36,
            Wear = 1.5, // over 1.0
            Seed = -10, // negative seed
            StatTrakCount = -5, // negative StatTrak
        };

        // Property setter clamps wear to 1.0 and seed to 0
        Assert.Equal(1.0, weaponDraft.Wear);
        Assert.Equal(0, weaponDraft.Seed);
        Assert.Equal(0, weaponDraft.StatTrakCount);

        weaponDraft.Wear = double.NaN;
        Assert.Equal(0.0, weaponDraft.Wear);
    }

    // 5. Selected Knife Invariant
    [Fact]
    public void SelectedKnife_MustExistInPresetsCollection()
    {
        var preset = HumanPresetTemplate.CreateMinimalValid();
        // Modify selected to a defIndex that has no preset
        var broken = new HumanPreset
        {
            Kind = preset.Kind,
            SchemaVersion = preset.SchemaVersion,
            Ct = new TeamPreset
            {
                Weapons = preset.Ct.Weapons,
                Knife = new KnifeSection
                {
                    Selected = 999, // not in presets
                    Presets = preset.Ct.Knife.Presets,
                },
                Gloves = preset.Ct.Gloves,
            },
            T = preset.T,
        };

        var problems = HumanPresetValidator.ValidateDomain(broken);
        Assert.Contains(problems, p => p.Contains("knife.selected (999) has no preset in ct.knife.presets"));
    }

    // 6. Disabled Gloves Preservation
    [Fact]
    public void DisabledGloves_PreserveValuesWithoutProjecting()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        manager.LoadPreset("test-preset.v1.json");

        var draft = manager.Draft!;
        draft.Ct.Gloves.Enabled = false;
        draft.Ct.Gloves.DefIndex = 5030;
        draft.Ct.Gloves.Paint = 10037;
        draft.Ct.Gloves.Wear = 0.06;

        var converted = draft.ToHumanPreset();
        Assert.False(converted.Ct.Gloves.Enabled);
        Assert.Equal(5030, converted.Ct.Gloves.DefIndex);
        Assert.Equal(10037, converted.Ct.Gloves.Paint);

        // Verify projector omits disabled gloves
        var projector = new InventorySimulatorProjector();
        var projectedJson = projector.Project(converted, TestFixtures.FakeSteamId64);
        using var doc = JsonDocument.Parse(projectedJson);
        var inv = doc.RootElement.GetProperty(TestFixtures.FakeSteamId64);
        // CT gloves key is 3; with gloves disabled, team 3 glove should not be projected
        Assert.False(inv.GetProperty("gloves").TryGetProperty("3", out _));
    }

    // 7. Dirty Tracking
    [Fact]
    public void DirtyTracking_DetectsEditsAndSupportsCleanReset()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        manager.LoadPreset("test-preset.v1.json");

        var draft = manager.Draft!;
        Assert.False(draft.IsDirty);
        Assert.False(manager.IsDirty);

        // Edit knife wear
        draft.Ct.Knife.Presets[0].Wear = 0.03;
        Assert.True(draft.IsDirty);
        Assert.True(manager.IsDirty);

        draft.MarkClean();
        Assert.False(draft.IsDirty);

        // Edit music kit
        draft.MusicKitId = 78;
        Assert.True(draft.IsDirty);
    }

    // 8. Preset Switching Unsaved Resolution Logic
    [Fact]
    public void PresetSwitching_UnsavedResolution_SaveAndSwitch()
    {
        var services = CreateTestServices();
        var dialog = new MockDialogService();
        var manager = new PresetManagerService(services, dialog);

        // Create second preset file
        var second = HumanPresetTemplate.CreateMinimalValid();
        File.WriteAllText(Path.Combine(_presetsRoot, "second.v1.json"), HumanPresetJson.Write(second));

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.Ct.Knife.Presets[0].Wear = 0.04;
        Assert.True(manager.IsDirty);

        dialog.ResolutionToReturn = UnsavedChangesResolution.SaveAndSwitch;
        var switched = manager.LoadPreset("second.v1.json");

        Assert.True(switched);
        Assert.Equal("second.v1.json", manager.WorkingPresetName);
        Assert.False(manager.IsDirty);

        // Verify the first preset was saved with wear 0.04
        var reloadedFirst = services.PresetStore.Load("test-preset.v1.json");
        Assert.Equal(0.04, reloadedFirst.Ct.Knife.Presets[0].Preset.Wear);
    }

    [Fact]
    public void PresetSwitching_UnsavedResolution_Cancel()
    {
        var services = CreateTestServices();
        var dialog = new MockDialogService();
        var manager = new PresetManagerService(services, dialog);

        var second = HumanPresetTemplate.CreateMinimalValid();
        File.WriteAllText(Path.Combine(_presetsRoot, "second.v1.json"), HumanPresetJson.Write(second));

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.Ct.Knife.Presets[0].Wear = 0.04;

        dialog.ResolutionToReturn = UnsavedChangesResolution.Cancel;
        var switched = manager.LoadPreset("second.v1.json");

        Assert.False(switched);
        Assert.Equal("test-preset.v1.json", manager.WorkingPresetName);
        Assert.True(manager.IsDirty);
    }

    // 9. Save and Apply Orchestration
    [Fact]
    public void SaveAndApply_AutoSavesDirtyDraft_AndAppliesToFixture()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        manager.LoadPreset("test-preset.v1.json");

        // Make a change
        manager.Draft!.MusicKitId = 78;
        Assert.True(manager.IsDirty);

        var result = manager.Apply();
        Assert.True(result.Success, result.Message);
        Assert.False(manager.IsDirty);

        // Fixture was updated and backup was created
        Assert.NotNull(manager.InstalledProjectionSha256);
        Assert.NotNull(manager.LatestAppliedHash);
        Assert.Equal(manager.InstalledProjectionSha256, manager.LatestAppliedHash);
        Assert.True(manager.RollbackCanExecute);
    }

    // 10. Apply Blocked while CS2 Running
    [Fact]
    public void Apply_IsBlocked_WhenCs2IsRunning()
    {
        var services = CreateTestServices(cs2RunningProbe: () => true);
        var manager = new PresetManagerService(services, new MockDialogService());
        manager.LoadPreset("test-preset.v1.json");

        var result = manager.Apply();
        Assert.False(result.Success);
        Assert.Contains("CS2 正在运行", result.Message);
    }

    // 11. Active Preset Semantics
    [Fact]
    public void SetActive_UpdatesPointerAndRuntimeStatus()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());

        manager.SetActive("test-preset.v1.json");
        Assert.Equal("test-preset.v1.json", manager.ActivePresetName);

        var status = services.RuntimeStatusService.GetStatus();
        Assert.Equal("test-preset.v1.json", status.ActivePreset);
        Assert.True(status.ActivePresetExists);
    }

    // 12. Runtime Status to UI Mapping
    [Fact]
    public void RuntimeStatus_MapsToCorrectHealthLevels()
    {
        var services = CreateTestServices();
        var status = services.RuntimeStatusService.GetStatus();

        // With cs2 detected and valid tree
        Assert.Equal(RuntimeHealthLevel.Ready, status.HealthLevel);
        Assert.Contains("就绪", status.HealthSummary);

        // When active preset is missing
        services.ActivePresetState.SetActive("nonexistent.json");
        var attentionStatus = services.RuntimeStatusService.GetStatus();
        Assert.Equal(RuntimeHealthLevel.Attention, attentionStatus.HealthLevel);

        // When DLL is mismatch
        var mismatchLock = Path.Combine(_work, "mismatch-lock.json");
        File.WriteAllText(mismatchLock, """
        {
          "testedCs2Build": { "patchVersion": "1.41.8.5", "clientVersion": "2000918", "buildId": "18000000" },
          "acceptedRuntime": { "patchedDllSha256": "wrong_hash" }
        }
        """);
        var blockedStatus = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = mismatchLock,
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
        }).GetStatus();

        Assert.Equal(RuntimeHealthLevel.Blocked, blockedStatus.HealthLevel);
    }

    // 13. RollbackCanExecute Truth Table
    [Fact]
    public void RollbackCanExecute_FollowsTruthTableStrictly()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());

        // Case A: No apply record yet -> false
        manager.RefreshRuntimeStatus();
        Assert.False(manager.RollbackCanExecute);

        // Apply a preset to produce a record
        manager.LoadPreset("test-preset.v1.json");
        var applyResult = manager.Apply();
        Assert.True(applyResult.Success);

        // Case B: Clean state -> true
        manager.RefreshRuntimeStatus();
        Assert.True(manager.RollbackCanExecute);
        Assert.Null(manager.RollbackBlockReason);

        // Case C: CS2 is running -> false
        var runningServices = CreateTestServices(cs2RunningProbe: () => true);
        var runningManager = new PresetManagerService(runningServices, new MockDialogService());
        Assert.False(runningManager.RollbackCanExecute);
        Assert.Contains("CS2 正在运行", runningManager.RollbackBlockReason);

        // Case D: Backup file deleted -> false
        var record = services.RuntimeStatusService.GetStatus().LatestApply!;
        File.Delete(record.BackupPath!);
        manager.RefreshRuntimeStatus();
        Assert.False(manager.RollbackCanExecute);
        Assert.Contains("备份文件不存在", manager.RollbackBlockReason);

        // Case E: Fixture modified (drift) -> false
        // Case E: Fixture modified (drift) -> false (first restore backup so backup-missing doesn't shadow drift)
        File.WriteAllText(record.BackupPath!, "{}");
        File.WriteAllText(record.InstalledPath, "{\"tampered\": true}");
        manager.RefreshRuntimeStatus();
        Assert.False(manager.RollbackCanExecute);
        Assert.Contains("不一致", manager.RollbackBlockReason);
    }

    // 14. Import and Export Workflow
    [Fact]
    public void ImportAndExport_RoundTripPreservesPresetContent()
    {
        var services = CreateTestServices();
        var exportPath = Path.Combine(_work, "exported.json");

        services.PresetStore.Export("test-preset.v1.json", exportPath);
        Assert.True(File.Exists(exportPath));

        var importedName = services.PresetStore.Import(exportPath, "imported-preset.v1.json");
        Assert.Equal("imported-preset.v1.json", importedName);
        Assert.True(services.PresetStore.Exists("imported-preset.v1.json"));

        var original = services.PresetStore.Load("test-preset.v1.json");
        var imported = services.PresetStore.Load(importedName);
        Assert.Equal(HumanPresetJson.Write(original), HumanPresetJson.Write(imported));
    }
}

public sealed class MockDialogService : IDialogService
{
    public UnsavedChangesResolution ResolutionToReturn { get; set; } = UnsavedChangesResolution.Cancel;
    public bool ConfirmToReturn { get; set; } = true;
    public string? SaveFileDialogResult { get; set; }
    public string? OpenFileDialogResult { get; set; }

    public UnsavedChangesResolution ConfirmUnsavedChanges(string presetName) => ResolutionToReturn;
    public bool Confirm(string title, string message) => ConfirmToReturn;
    public void ShowError(string title, string message) { }
    public void ShowInfo(string title, string message) { }
    public string? ShowSaveFileDialog(string defaultName, string filter) => SaveFileDialogResult;
    public string? ShowOpenFileDialog(string filter) => OpenFileDialogResult;
}
