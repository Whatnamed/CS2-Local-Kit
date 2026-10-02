using System.Text.Json;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.Core.Store;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class RuntimeStatusTests : IDisposable
{
    private readonly string _work;
    private readonly string _csgoDir;
    private readonly string _cs2Root;
    private readonly string _backupsRoot;
    private readonly string _presetsRoot;
    private readonly string _activePresetPath;

    private const string LockJson = """
    {
      "schemaVersion": 1,
      "component": "InventorySimulator",
      "repository": "ianlucas/cs2-css-inventory-simulator",
      "ref": "fade4449aaa6d5153261c855d0cc12d7dacfefdd",
      "tag": "3.3.0",
      "status": "accepted-as-patched",
      "testedCs2Build": { "patchVersion": "1.41.8.5", "clientVersion": "2000918", "buildId": "25537370" },
      "acceptedRuntime": { "patchedDllSha256": "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef" },
      "framework": { "metamod": { "version": "2.0.0-git1469" }, "counterstrikesharp": { "version": "v1.0.376" } }
    }
    """;

    private const string SteamInfMatch = "PatchVersion=1.41.8.5\r\nClientVersion=2000918\r\n";
    private const string SteamInfChanged = "PatchVersion=1.41.9.0\r\nClientVersion=2000999\r\n";

    public RuntimeStatusTests()
    {
        _work = Path.Combine(Path.GetTempPath(), "cs2localkit-statustests-" + Guid.NewGuid().ToString("N"));
        _cs2Root = Path.Combine(_work, "steamapps", "common", "Counter-Strike Global Offensive");
        _csgoDir = Path.Combine(_cs2Root, "game", "csgo");
        _backupsRoot = Path.Combine(_work, "backups");
        // Preset state is read from disk, so it is pointed at the temp tree as well: left to the
        // defaults these tests would report whoever happens to have a preset on this machine.
        _presetsRoot = Path.Combine(_work, "presets", "human");
        _activePresetPath = Path.Combine(_work, "presets", "active-preset.json");
        Directory.CreateDirectory(_presetsRoot);
        Directory.CreateDirectory(_csgoDir);
        File.WriteAllText(Path.Combine(_csgoDir, "steam.inf"), SteamInfMatch);
        // appmanifest lives two levels above the install dir: <root>\..\..\ = <library>\steamapps
        var manifestPath = Path.GetFullPath(Path.Combine(_cs2Root, "..", "..", "appmanifest_730.acf"));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, "\"buildid\"\t\t\"25537370\"");
        File.WriteAllText(Path.Combine(_csgoDir, "gameinfo.gi"), "SearchPaths\n{\n\tGame\tcsgo/addons/metamod\n}\n");
        Directory.CreateDirectory(Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64"));
        File.WriteAllText(Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll"), "mm");
        Directory.CreateDirectory(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64"));
        var isDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator");
        Directory.CreateDirectory(isDir);
        File.WriteAllText(Path.Combine(isDir, "InventorySimulator.dll"), "patched-plugin-bytes");
        Directory.CreateDirectory(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator"));
        File.WriteAllText(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true);
    }

    private InventorySimulatorLock Lock() => InventorySimulatorLock.Load(WriteLock(LockJson));

    private string WriteLock(string json)
    {
        var p = Path.Combine(_work, "inventory-simulator.lock.json");
        File.WriteAllText(p, json);
        return p;
    }

    private string LockWithDllSha()
    {
        // The lock must record the actual hash of the fake patched plugin bytes.
        var bytes = System.Text.Encoding.UTF8.GetBytes("patched-plugin-bytes");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        return LockJson.Replace("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef", sha);
    }

    [Fact]
    public void Status_HealthyFakeTree()
    {
        var lockPath = WriteLock(LockWithDllSha());
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = lockPath,
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
        }).GetStatus();

        Assert.True(status.Cs2Detected);
        Assert.False(status.Cs2Running); // test process is not cs2
        Assert.Equal("1.41.8.5", status.PatchVersion);
        Assert.Equal("2000918", status.ClientVersion);
        Assert.Equal("25537370", status.BuildId);
        Assert.Equal("match", status.TestedBuildMatch);
        Assert.True(status.GameinfoHasMetamod);
        // Presence only - there is no reliable version/hash verification for the
        // framework builds, so the status must not claim a version match.
        Assert.Equal("present-unverified", status.MetaModNativeStatus);
        Assert.Equal("present-unverified", status.CounterStrikeSharpNativeStatus);
        Assert.True(status.InventorySimulatorPluginPresent);
        Assert.Equal("match", status.PatchedDllMatch);
        Assert.True(status.FixtureInstalled);
        Assert.Null(status.ActivePreset);
        Assert.Null(status.LatestApply);
    }

    [Fact]
    public void Status_MissingFrameworkComponents_AreReportedAsMissing()
    {
        File.Delete(Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll"));
        Directory.Delete(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64"));
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(LockWithDllSha()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
        }).GetStatus();
        Assert.Equal("missing", status.MetaModNativeStatus);
        Assert.Equal("missing", status.CounterStrikeSharpNativeStatus);
    }

    [Fact]
    public void Status_BuildChanged_IsReported()
    {
        File.WriteAllText(Path.Combine(_csgoDir, "steam.inf"), SteamInfChanged);
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(LockWithDllSha()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
        }).GetStatus();
        Assert.Equal("changed", status.TestedBuildMatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateFramework_VerifiesEntireLoaderChain_WithoutAcceptingNewBuild(bool tamperLoader)
    {
        const string native = "addons/metamod/bin/win64/metamod.2.cs2.dll";
        const string loader = "addons/metamod/bin/win64/server.dll";
        File.WriteAllText(Path.Combine(_csgoDir, loader), "candidate-loader");
        string Hash(string rel) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(_csgoDir, rel)))).ToLowerInvariant();
        var json = System.Text.Json.Nodes.JsonNode.Parse(LockWithDllSha())!;
        json["compatibilityCandidate"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new
        {
            status = "candidate",
            targetCs2Build = new { patchVersion = "1.41.8.8", clientVersion = "2000922", buildId = "25640462" },
            framework = new { metamod = new { version = "2.0.0-git1473", installedFiles = new Dictionary<string, string> { [native] = Hash(native), [loader] = Hash(loader) } } },
        }));
        File.WriteAllText(Path.Combine(_csgoDir, "steam.inf"), "PatchVersion=1.41.8.8\nClientVersion=2000922\n");
        File.WriteAllText(Path.GetFullPath(Path.Combine(_cs2Root, "..", "..", "appmanifest_730.acf")), "\"buildid\" \"25640462\"");
        if (tamperLoader) File.WriteAllText(Path.Combine(_csgoDir, loader), "other-loader");
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(json.ToJsonString()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
            Cs2RunningProbe = () => false,
        }).GetStatus();
        Assert.Equal("changed", status.TestedBuildMatch);
        Assert.True(status.FrameworkCandidate);
        Assert.Equal("1.41.8.5", status.Lock!.PatchVersion);
        if (tamperLoader)
        {
            Assert.Equal("hash-mismatch", status.MetaModNativeStatus);
            Assert.Equal(RuntimeHealthLevel.Blocked, status.HealthLevel);
        }
        else
        {
            Assert.Contains("candidate-hash-match", status.MetaModNativeStatus);
            Assert.Equal(RuntimeHealthLevel.Attention, status.HealthLevel);
        }
    }

    [Fact]
    public void Status_PatchedDllMismatch_IsReported()
    {
        File.WriteAllText(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll"), "tampered");
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(LockJson), // hash no longer matches the tampered dll
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
        }).GetStatus();
        Assert.Equal("mismatch", status.PatchedDllMatch);
    }

    [Fact]
    public void LoaderReferenceInComment_IsNotAStartupEntry()
    {
        File.WriteAllText(Path.Combine(_csgoDir, "gameinfo.gi"), "// Game csgo/addons/metamod\nSearchPaths\n{\n Game csgo\n}\n");
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(LockWithDllSha()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
            Cs2RunningProbe = () => false,
        }).GetStatus();
        Assert.False(status.GameinfoHasMetamod);
        Assert.Equal(RuntimeHealthLevel.Blocked, status.HealthLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedFramework_StillRequiresMatchingInstalledIdentity(bool tamper)
    {
        const string rel = "addons/metamod/bin/win64/metamod.2.cs2.dll";
        var path = Path.Combine(_csgoDir, rel);
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var json = System.Text.Json.Nodes.JsonNode.Parse(LockWithDllSha())!;
        json["framework"]!["metamod"]!["installedFiles"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { [rel] = sha }));
        if (tamper) File.WriteAllText(path, "different-installed-loader");
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(json.ToJsonString()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = _presetsRoot,
            ActivePresetPath = _activePresetPath,
            Cs2RunningProbe = () => false,
        }).GetStatus();
        Assert.False(status.FrameworkCandidate);
        Assert.Equal("match", status.TestedBuildMatch);
        Assert.Equal(tamper ? "hash-mismatch" : "hash-match (2.0.0-git1469)", status.MetaModNativeStatus);
        if (tamper) Assert.Equal(RuntimeHealthLevel.Blocked, status.HealthLevel);
    }

    [Fact]
    public void Status_ActivePreset_And_LatestApply()
    {
        var presetsRoot = Path.Combine(_work, "presets");
        Directory.CreateDirectory(presetsRoot);
        File.WriteAllText(Path.Combine(presetsRoot, "personal-default.v1.json"), "{}");
        new ActivePresetState(Path.Combine(_work, "active-preset.json")).SetActive("personal-default.v1.json");

        var installedFixturePath = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");
        var applyDir = Path.Combine(_backupsRoot, "20260101-000000-preset-apply");
        Directory.CreateDirectory(applyDir);
        var backupFile = Path.Combine(applyDir, "inventories.json");
        File.WriteAllText(backupFile, "{}");
        var record = new
        {
            kind = "cosmetics-lab-preset-apply-record",
            createdAt = "2026-01-01T00:00:00+08:00",
            presetPath = "personal-default.v1.json",
            projectedSha256 = "abc",
            installedPath = installedFixturePath,
            installed = new { previousSha256 = "p", newSha256 = "n" },
            backupPath = backupFile,
            backupSha256 = "p",
            rollback = "x",
        };
        File.WriteAllText(Path.Combine(applyDir, "apply-record.json"),
            JsonSerializer.Serialize(record));

        // A NEWER record for a DIFFERENT install path must be ignored: it is not
        // restorable for the current fixture, so it must not surface as LatestApply.
        var otherDir = Path.Combine(_backupsRoot, "20260102-000000-preset-apply");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "apply-record.json"),
            JsonSerializer.Serialize(record with
            {
                createdAt = "2026-01-02T00:00:00+08:00",
                installedPath = @"D:\elsewhere\game\csgo\addons\counterstrikesharp\configs\plugins\InventorySimulator\inventories.json",
            }));

        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = WriteLock(LockWithDllSha()),
            BackupsRoot = _backupsRoot,
            PresetsRoot = presetsRoot,
            ActivePresetPath = Path.Combine(_work, "active-preset.json"),
        }).GetStatus();

        Assert.Equal("personal-default.v1.json", status.ActivePreset);
        Assert.True(status.ActivePresetExists);
        Assert.NotNull(status.LatestApply);
        Assert.Equal(installedFixturePath, status.LatestApply!.InstalledPath); // the matching record, not the newer unrelated one
        Assert.Equal("2026-01-01T00:00:00+08:00", status.LatestApply.CreatedAt);
        Assert.True(status.LatestApply.RollbackAvailable);
    }
}
