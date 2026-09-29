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

    // C4.1 Regression Tests:

    // 15. Process state monitor transition updates runtime status and command CanExecute
    [Fact]
    public void ProcessStateMonitor_TransitionUpdatesRuntimeStatusAndCommandCanExecute()
    {
        bool probeRunning = false;
        var services = CreateTestServices(cs2RunningProbe: () => probeRunning);
        var manager = new PresetManagerService(services, new MockDialogService());
        var cosmetics = new CosmeticsViewModel(manager);
        var header = new HeaderViewModel(manager, () => { }, () => { });

        Assert.False(manager.Cs2Running);
        Assert.False(header.Cs2Running);
        Assert.Equal("CS2 已关闭", header.Cs2StatusText);
        Assert.True(cosmetics.ApplyCommand.CanExecute(null));
        Assert.True(header.ApplyCommand.CanExecute(null));

        // Process starts
        probeRunning = true;
        bool changedToRunning = manager.PollProcessState();
        Assert.True(changedToRunning);
        Assert.True(manager.Cs2Running);
        Assert.True(header.Cs2Running);
        Assert.Equal("CS2 正在运行", header.Cs2StatusText);
        Assert.False(cosmetics.ApplyCommand.CanExecute(null));
        Assert.False(header.ApplyCommand.CanExecute(null));

        // Calling again with no state change returns false
        Assert.False(manager.PollProcessState());

        // Process stops
        probeRunning = false;
        bool changedToStopped = manager.PollProcessState();
        Assert.True(changedToStopped);
        Assert.False(manager.Cs2Running);
        Assert.False(header.Cs2Running);
        Assert.Equal("CS2 已关闭", header.Cs2StatusText);
        Assert.True(cosmetics.ApplyCommand.CanExecute(null));
        Assert.True(header.ApplyCommand.CanExecute(null));
    }

    // 16. Music Kit search filtering never mutates canonical draft
    [Fact]
    public void MusicKitSelection_SearchFilterNeverMutatesCanonicalDraft()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        manager.LoadPreset("test-preset.v1.json");

        // Set initial MusicKit
        manager.Draft!.MusicKitId = 78;
        manager.Save();
        Assert.False(manager.IsDirty);
        Assert.Equal(78, manager.Draft.MusicKitId);

        var cosmetics = new CosmeticsViewModel(manager);
        Assert.Equal(78, manager.Draft.MusicKitId);
        Assert.False(manager.IsDirty);

        // Search for something that doesn't match kit 78
        cosmetics.MusicKitSearchText = "Nonexistent_Search_Filter_Mismatch";
        Assert.Empty(cosmetics.FilteredMusicKits);
        Assert.Null(cosmetics.SelectedMusicKit);

        // Crucial invariant: canonical draft MUST remain untouched and clean
        Assert.Equal(78, manager.Draft.MusicKitId);
        Assert.False(manager.IsDirty);

        // Load Preset B with search filter still active
        var presetB = HumanPresetTemplate.CreateMinimalValid();
        presetB = new HumanPreset
        {
            Kind = presetB.Kind,
            SchemaVersion = presetB.SchemaVersion,
            Ct = presetB.Ct,
            T = presetB.T,
            MusicKitId = 78,
        };
        File.WriteAllText(Path.Combine(_presetsRoot, "preset-b.v1.json"), HumanPresetJson.Write(presetB));

        manager.LoadPreset("preset-b.v1.json");
        Assert.Equal(78, manager.Draft.MusicKitId);
        Assert.False(manager.IsDirty);
    }

    // 17. New Preset respects unsaved changes contract: Cancel
    [Fact]
    public void NewPreset_RespectsUnsavedChangesContract_Cancel()
    {
        var services = CreateTestServices();
        var dialog = new MockDialogService { ResolutionToReturn = UnsavedChangesResolution.Cancel };
        var manager = new PresetManagerService(services, dialog);
        var presetsVm = new PresetsViewModel(manager);

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.MusicKitId = 78; // make dirty
        Assert.True(manager.IsDirty);

        presetsVm.NewPresetNameInput = "new-cancelled.v1.json";
        presetsVm.CreateNewPresetCommand.Execute(null);

        // Invariant: file must NOT be created, draft must NOT be lost, working preset remains
        Assert.Equal(1, dialog.ConfirmUnsavedChangesCallCount);
        Assert.False(services.PresetStore.Exists("new-cancelled.v1.json"));
        Assert.Equal("test-preset.v1.json", manager.WorkingPresetName);
        Assert.True(manager.IsDirty);
        Assert.Equal(78, manager.Draft.MusicKitId);
    }

    // 18. New Preset respects unsaved changes contract: SaveAndSwitch
    [Fact]
    public void NewPreset_RespectsUnsavedChangesContract_SaveAndSwitch()
    {
        var services = CreateTestServices();
        var dialog = new MockDialogService { ResolutionToReturn = UnsavedChangesResolution.SaveAndSwitch };
        var manager = new PresetManagerService(services, dialog);
        var presetsVm = new PresetsViewModel(manager);

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.MusicKitId = 78; // make dirty
        Assert.True(manager.IsDirty);

        presetsVm.NewPresetNameInput = "new-saved.v1.json";
        presetsVm.CreateNewPresetCommand.Execute(null);

        // Invariant: old preset was saved with change, new preset created and switched to, clean draft
        Assert.Equal(1, dialog.ConfirmUnsavedChangesCallCount);
        Assert.True(services.PresetStore.Exists("new-saved.v1.json"));
        Assert.Equal("new-saved.v1.json", manager.WorkingPresetName);
        Assert.False(manager.IsDirty);

        // Verify old preset persisted the change
        var savedOld = services.PresetStore.Load("test-preset.v1.json");
        Assert.Equal(78, savedOld.MusicKitId);
    }

    // 19. New Preset respects unsaved changes contract: DiscardAndSwitch
    [Fact]
    public void NewPreset_RespectsUnsavedChangesContract_DiscardAndSwitch()
    {
        var services = CreateTestServices();
        var dialog = new MockDialogService { ResolutionToReturn = UnsavedChangesResolution.DiscardAndSwitch };
        var manager = new PresetManagerService(services, dialog);
        var presetsVm = new PresetsViewModel(manager);

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.MusicKitId = 78; // make dirty
        Assert.True(manager.IsDirty);

        presetsVm.NewPresetNameInput = "new-discarded.v1.json";
        presetsVm.CreateNewPresetCommand.Execute(null);

        // Invariant: old preset on disk does NOT have the dirty change, new preset created and loaded
        Assert.Equal(1, dialog.ConfirmUnsavedChangesCallCount);
        Assert.True(services.PresetStore.Exists("new-discarded.v1.json"));
        Assert.Equal("new-discarded.v1.json", manager.WorkingPresetName);
        Assert.False(manager.IsDirty);

        var untouchedOld = services.PresetStore.Load("test-preset.v1.json");
        Assert.Null(untouchedOld.MusicKitId);
    }

    // 20. RuntimeHealth missing native components are Blocked
    [Fact]
    public void RuntimeHealth_MissingNativeComponents_AreBlocked()
    {
        var services = CreateTestServices();

        // 1. When MetaMod DLL is missing
        var mmDll = Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll");
        File.Delete(mmDll);
        var statusMmMissing = services.RuntimeStatusService.GetStatus();
        Assert.Equal(RuntimeHealthLevel.Blocked, statusMmMissing.HealthLevel);
        Assert.Contains(statusMmMissing.BlockedReasons, r => r.Contains("MetaMod 原生组件缺失"));

        // Restore MetaMod
        File.WriteAllText(mmDll, "");

        // 2. When CounterStrikeSharp bin directory is missing
        var cssDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64");
        Directory.Delete(cssDir);
        var statusCssMissing = services.RuntimeStatusService.GetStatus();
        Assert.Equal(RuntimeHealthLevel.Blocked, statusCssMissing.HealthLevel);
        Assert.Contains(statusCssMissing.BlockedReasons, r => r.Contains("CounterStrikeSharp 原生组件缺失"));

        // Restore CSS
        Directory.CreateDirectory(cssDir);
        var statusHealthy = services.RuntimeStatusService.GetStatus();
        Assert.Equal(RuntimeHealthLevel.Ready, statusHealthy.HealthLevel);
    }

    // 21. Header truth: the active pointer and the installed configuration are different claims.
    [Fact]
    public void Header_ActivePointerAndInstalledConfig_AreReportedSeparately()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });

        // The fixture on disk is what the scaffolding projected; a record attributes it to a.v1.json.
        WriteApplyRecord("rec-a", "a.v1.json", FixtureSha(), "2026-01-01T00:00:00+08:00");
        File.WriteAllText(Path.Combine(_presetsRoot, "b.v1.json"),
            HumanPresetJson.Write(HumanPresetTemplate.CreateMinimalValid()));
        manager.SetActive("b.v1.json");

        Assert.Equal("b", header.ActiveName);
        Assert.Equal(InstalledConfigLevel.Verified, header.InstalledConfigLevel);
        Assert.Equal("a", header.InstalledConfigName);
    }

    // 22. Header truth: drift is drift, even when the pointer and the record agree.
    [Fact]
    public void Header_FixtureDriftFromLatestRecord_IsNotPresentedAsTheRecord()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });

        WriteApplyRecord("rec-test", "test-preset.v1.json", new string('f', 64),
            "2026-01-01T00:00:00+08:00");
        manager.SetActive("test-preset.v1.json");

        Assert.Equal("test-preset", header.ActiveName);
        Assert.Equal(InstalledConfigLevel.Drifted, header.InstalledConfigLevel);
        Assert.Equal("已漂移", header.InstalledConfigName);
        Assert.Contains("不一致", header.InstalledConfigDetail);
    }

    // 23. Header truth: a missing fixture is stated as missing, whatever the records say.
    [Fact]
    public void Header_MissingFixture_IsReportedAsNotInstalled()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });

        WriteApplyRecord("rec-test", "test-preset.v1.json", FixtureSha(), "2026-01-01T00:00:00+08:00");
        File.Delete(InstalledFixturePath);
        manager.RefreshRuntimeStatus();

        Assert.Equal(InstalledConfigLevel.NotInstalled, header.InstalledConfigLevel);
        Assert.Equal("未安装", header.InstalledConfigName);
    }

    // 24. Header truth: an unattributable file stays 未知 instead of borrowing a preset name.
    [Fact]
    public void Header_FixtureWithoutApplyRecord_IsReportedAsUnknown()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });
        manager.RefreshRuntimeStatus();

        Assert.True(manager.LastStatus!.FixtureInstalled);
        Assert.Null(manager.LastStatus.LatestApply);
        Assert.Equal(InstalledConfigLevel.Unknown, header.InstalledConfigLevel);
        Assert.Equal("未知", header.InstalledConfigName);
        Assert.Contains("没有可归属的应用记录", header.InstalledConfigDetail);
    }

    // 25. A successful apply makes the working preset the verified configuration, and the Header
    // follows without an explicit re-read.
    [Fact]
    public void Header_AfterSuccessfulApply_ReportsTheWorkingPresetAsInstalled()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });

        Assert.NotEqual(InstalledConfigLevel.Verified, header.InstalledConfigLevel);

        manager.LoadPreset("test-preset.v1.json");
        manager.Draft!.MusicKitId = 78;
        var result = manager.Apply();
        Assert.True(result.Success, result.Message);

        Assert.Equal(InstalledConfigLevel.Verified, header.InstalledConfigLevel);
        Assert.Equal("test-preset", header.InstalledConfigName);
    }

    // 26. Later drift notifies: the Header re-raises its configuration facts on every refresh.
    [Fact]
    public void Header_RaisesConfigFactWhenTheFixtureDriftsAfterARefresh()
    {
        var services = CreateTestServices();
        var manager = new PresetManagerService(services, new MockDialogService());
        var header = new HeaderViewModel(manager, () => { }, () => { });

        manager.LoadPreset("test-preset.v1.json");
        Assert.True(manager.Apply().Success);
        Assert.Equal("test-preset", header.InstalledConfigName);

        var raised = new List<string?>();
        header.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        File.WriteAllText(InstalledFixturePath, "{\"tampered\":true}");
        manager.RefreshRuntimeStatus();

        Assert.Contains(nameof(HeaderViewModel.InstalledConfigName), raised);
        Assert.Equal(InstalledConfigLevel.Drifted, header.InstalledConfigLevel);
        Assert.Equal("已漂移", header.InstalledConfigName);
    }

    private string InstalledFixturePath
        => Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");

    private string FixtureSha()
    {
        using var stream = File.OpenRead(InstalledFixturePath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>An apply record for the currently installed fixture path, as FixtureApplier writes it.</summary>
    private void WriteApplyRecord(string directoryName, string presetPath, string newSha256, string createdAt)
    {
        var dir = Path.Combine(_backupsRoot, directoryName);
        Directory.CreateDirectory(dir);
        var backup = Path.Combine(dir, "inventories.json");
        File.WriteAllText(backup, "{\"previous\":true}");
        File.WriteAllText(Path.Combine(dir, "apply-record.json"), JsonSerializer.Serialize(new
        {
            kind = "cosmetics-lab-preset-apply-record",
            createdAt,
            presetPath,
            projectedSha256 = newSha256,
            installedPath = InstalledFixturePath,
            installed = new { previousSha256 = "p", newSha256 },
            backupPath = backup,
            backupSha256 = "p",
            rollback = "x",
        }));
    }
}

public sealed class MockDialogService : IDialogService
{
    public UnsavedChangesResolution ResolutionToReturn { get; set; } = UnsavedChangesResolution.Cancel;
    public bool ConfirmToReturn { get; set; } = true;
    public string? SaveFileDialogResult { get; set; }
    public string? OpenFileDialogResult { get; set; }
    public int ConfirmUnsavedChangesCallCount { get; private set; }

    public UnsavedChangesResolution ConfirmUnsavedChanges(string presetName)
    {
        ConfirmUnsavedChangesCallCount++;
        return ResolutionToReturn;
    }
    public bool Confirm(string title, string message) => ConfirmToReturn;
    public void ShowError(string title, string message) { }
    public void ShowInfo(string title, string message) { }
    public string? ShowSaveFileDialog(string defaultName, string filter) => SaveFileDialogResult;
    public string? ShowOpenFileDialog(string filter) => OpenFileDialogResult;
}
