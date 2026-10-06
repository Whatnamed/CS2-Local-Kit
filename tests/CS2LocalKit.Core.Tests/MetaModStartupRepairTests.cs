using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CS2LocalKit.Core.Runtime;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class MetaModStartupRepairTests : IDisposable
{
    private readonly string _work;
    private readonly string _cs2Root;
    private readonly string _csgoDir;
    private readonly string _backupsRoot;
    private readonly string _lockPath;

    private const string ValidMm1 = "mm-native-bytes";
    private const string ValidMm2 = "mm-server-bytes";
    private const string ValidCss1 = "css-native-bytes";
    private const string ValidCss2 = "css-api-bytes";
    private const string ValidCss3 = "css-vdf-bytes";
    private const string ValidPatchedDll = "patched-invsim-dll-bytes";

    public MetaModStartupRepairTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"cs2localkit-repairtests-{Guid.NewGuid():N}");
        _cs2Root = Path.Combine(_work, "cs2");
        _csgoDir = Path.Combine(_cs2Root, "game", "csgo");
        _backupsRoot = Path.Combine(_work, "backups");
        _lockPath = Path.Combine(_work, "runtime", "inventory-simulator.lock.json");

        Directory.CreateDirectory(_csgoDir);
        Directory.CreateDirectory(_backupsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);

        SetupAcceptedFrameworkFiles();
        SetupLockFile();
    }

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            try { Directory.Delete(_work, recursive: true); } catch { }
        }
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private void SetupAcceptedFrameworkFiles()
    {
        var mmDir = Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64");
        Directory.CreateDirectory(mmDir);
        File.WriteAllText(Path.Combine(mmDir, "metamod.2.cs2.dll"), ValidMm1);
        File.WriteAllText(Path.Combine(mmDir, "server.dll"), ValidMm2);

        var cssDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64");
        Directory.CreateDirectory(cssDir);
        File.WriteAllText(Path.Combine(cssDir, "counterstrikesharp.dll"), ValidCss1);

        var cssApiDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "api");
        Directory.CreateDirectory(cssApiDir);
        File.WriteAllText(Path.Combine(cssApiDir, "CounterStrikeSharp.API.dll"), ValidCss2);

        File.WriteAllText(Path.Combine(_csgoDir, "addons", "metamod", "counterstrikesharp.vdf"), ValidCss3);

        var pluginDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "InventorySimulator.dll"), ValidPatchedDll);
    }

    private void SetupLockFile()
    {
        var lockObj = new
        {
            schemaVersion = 1,
            component = "InventorySimulator",
            testedCs2Build = new { patchVersion = "1.41.8.8", clientVersion = "2000922", buildId = "25640462" },
            acceptedRuntime = new { patchedDllSha256 = Hash(ValidPatchedDll) },
            framework = new
            {
                metamod = new
                {
                    version = "2.0.0-git1473",
                    installedFiles = new Dictionary<string, string>
                    {
                        ["addons/metamod/bin/win64/metamod.2.cs2.dll"] = Hash(ValidMm1),
                        ["addons/metamod/bin/win64/server.dll"] = Hash(ValidMm2),
                    }
                },
                counterstrikesharp = new
                {
                    version = "v1.0.376",
                    installedFiles = new Dictionary<string, string>
                    {
                        ["addons/counterstrikesharp/bin/win64/counterstrikesharp.dll"] = Hash(ValidCss1),
                        ["addons/counterstrikesharp/api/CounterStrikeSharp.API.dll"] = Hash(ValidCss2),
                        ["addons/metamod/counterstrikesharp.vdf"] = Hash(ValidCss3),
                    }
                }
            }
        };

        File.WriteAllText(_lockPath, JsonSerializer.Serialize(lockObj, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void WriteGameinfo(string content)
    {
        File.WriteAllText(Path.Combine(_csgoDir, "gameinfo.gi"), content);
    }

    private MetaModStartupRepairService CreateService(Func<bool>? cs2RunningProbe = null)
    {
        return new MetaModStartupRepairService(new MetaModStartupRepairOptions
        {
            Cs2Root = _cs2Root,
            LockPath = _lockPath,
            BackupsRoot = _backupsRoot,
            Cs2RunningProbe = cs2RunningProbe ?? (() => false),
        });
    }

    [Fact]
    public void NormalMissing_RepairsGameinfo_RestoresStartupEntry()
    {
        const string initialGameinfo = """"
            "GameInfo"
            {
            	FileSystem
            	{
            		SearchPaths
            		{
            			Game_LowViolence	csgo_lv // Perfect World content override

            			Game	csgo
            			Game	csgo_imported
            			Game	csgo_core
            			Game	core
            		}
            	}
            }
            """";
        WriteGameinfo(initialGameinfo);
        var initialSha = HashFile(Path.Combine(_csgoDir, "gameinfo.gi"));

        var service = CreateService();
        var result = service.Repair();

        Assert.True(result.Success);
        Assert.True(result.Modified);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(initialSha, result.OriginalSha256);
        Assert.NotEqual(initialSha, result.RepairedSha256);

        // Verify backup content matches pre-repair file exactly
        Assert.Equal(initialSha, HashFile(result.BackupPath));

        // Verify repair-record.json in backup directory
        var recordPath = Path.Combine(Path.GetDirectoryName(result.BackupPath)!, "repair-record.json");
        Assert.True(File.Exists(recordPath));
        using (var doc = JsonDocument.Parse(File.ReadAllText(recordPath)))
        {
            Assert.Equal("repair-metamod-startup", doc.RootElement.GetProperty("action").GetString());
            Assert.Equal(initialSha, doc.RootElement.GetProperty("originalSha256").GetString());
        }

        // Verify file on disk now has the entry
        var repairedText = File.ReadAllText(Path.Combine(_csgoDir, "gameinfo.gi"));
        var mmMatches = Regex.Matches(repairedText, @"^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*\r?$", RegexOptions.Multiline);
        Assert.Single(mmMatches);

        // Verify RuntimeStatus sees GameinfoHasMetamod == true
        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = _cs2Root,
            LockPath = _lockPath,
            Cs2RunningProbe = () => false,
        }).GetStatus();
        Assert.True(status.GameinfoHasMetamod);
    }

    [Fact]
    public void AlreadyPresent_IsIdempotent_DoesNotModifyFileOrMakeBackup()
    {
        const string existingGameinfo = """"
            "GameInfo"
            {
            	FileSystem
            	{
            		SearchPaths
            		{
            			Game	csgo/addons/metamod
            			Game_LowViolence	csgo_lv

            			Game	csgo
            		}
            	}
            }
            """";
        WriteGameinfo(existingGameinfo);
        var initialSha = HashFile(Path.Combine(_csgoDir, "gameinfo.gi"));

        var service = CreateService();
        var result = service.Repair();

        Assert.True(result.Success);
        Assert.False(result.Modified);
        Assert.Equal(initialSha, result.OriginalSha256);
        Assert.Equal(initialSha, result.RepairedSha256);
        Assert.Equal(initialSha, HashFile(Path.Combine(_csgoDir, "gameinfo.gi")));

        // No backup directory should have been created
        Assert.Empty(Directory.GetDirectories(_backupsRoot));
    }

    [Fact]
    public void Cs2Running_FailsClosed_RejectsModification()
    {
        WriteGameinfo("SearchPaths\n{\nGame csgo\n}\n");
        var initialSha = HashFile(Path.Combine(_csgoDir, "gameinfo.gi"));

        var service = CreateService(cs2RunningProbe: () => true);

        var ex = Assert.Throws<MetaModStartupRepairException>(() => service.Repair());
        Assert.Contains("正在运行", ex.Message);
        Assert.Equal(initialSha, HashFile(Path.Combine(_csgoDir, "gameinfo.gi")));
    }

    [Theory]
    [InlineData("missing-metamod")]
    [InlineData("mismatched-metamod")]
    [InlineData("missing-css")]
    [InlineData("mismatched-css")]
    [InlineData("missing-invsim")]
    [InlineData("mismatched-invsim")]
    public void FrameworkIdentityMismatched_FailsClosed(string faultType)
    {
        WriteGameinfo("SearchPaths\n{\nGame csgo\n}\n");
        var initialSha = HashFile(Path.Combine(_csgoDir, "gameinfo.gi"));

        switch (faultType)
        {
            case "missing-metamod":
                File.Delete(Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll"));
                break;
            case "mismatched-metamod":
                File.WriteAllText(Path.Combine(_csgoDir, "addons", "metamod", "bin", "win64", "server.dll"), "tampered");
                break;
            case "missing-css":
                File.Delete(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "bin", "win64", "counterstrikesharp.dll"));
                break;
            case "mismatched-css":
                File.WriteAllText(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "api", "CounterStrikeSharp.API.dll"), "tampered");
                break;
            case "missing-invsim":
                File.Delete(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll"));
                break;
            case "mismatched-invsim":
                File.WriteAllText(Path.Combine(_csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll"), "tampered");
                break;
        }

        var service = CreateService();
        Assert.Throws<MetaModStartupRepairException>(() => service.Repair());
        Assert.Equal(initialSha, HashFile(Path.Combine(_csgoDir, "gameinfo.gi")));
    }

    [Theory]
    [InlineData("GameInfo { FileSystem { } }")]
    [InlineData("SearchPaths { Game csgo } SearchPaths { Game csgo }")]
    [InlineData("SearchPaths { Game csgo")]
    [InlineData("SearchPaths\n{\nGame csgo/addons/metamod\nGame csgo/addons/metamod\n}")]
    public void MalformedOrAmbiguousGameinfo_FailsClosed(string content)
    {
        WriteGameinfo(content);
        var initialSha = HashFile(Path.Combine(_csgoDir, "gameinfo.gi"));

        var service = CreateService();
        var ex = Assert.Throws<MetaModStartupRepairException>(() => service.Repair());
        Assert.Contains("结构异常", ex.Message);
        Assert.Equal(initialSha, HashFile(Path.Combine(_csgoDir, "gameinfo.gi")));
    }

    [Fact]
    public void CommentedOutMetaModEntry_IsRepairedWithActiveEntry()
    {
        const string commentGameinfo = """"
            "GameInfo"
            {
            	FileSystem
            	{
            		SearchPaths
            		{
            			// Game	csgo/addons/metamod
            			Game	csgo
            		}
            	}
            }
            """";
        WriteGameinfo(commentGameinfo);

        var service = CreateService();
        var result = service.Repair();

        Assert.True(result.Success);
        Assert.True(result.Modified);

        var repairedText = File.ReadAllText(Path.Combine(_csgoDir, "gameinfo.gi"));
        var activeMatches = Regex.Matches(repairedText, @"^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*\r?$", RegexOptions.Multiline);
        Assert.Single(activeMatches);
    }

    [Fact]
    public void ScopeVerification_TouchesNoOtherFiles()
    {
        WriteGameinfo("SearchPaths\n{\n\tGame\tcsgo\n}\n");

        // Plant extra files in the tree
        var fixtureDir = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator");
        Directory.CreateDirectory(fixtureDir);
        var fixturePath = Path.Combine(fixtureDir, "inventories.json");
        File.WriteAllText(fixturePath, "{\"fake\":\"fixture\"}");

        var cfgPath = Path.Combine(_csgoDir, "cfg", "autoexec.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)!);
        File.WriteAllText(cfgPath, "echo test");

        // Snapshot all files and their hashes before repair
        var filesBefore = Directory.EnumerateFiles(_work, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("gameinfo.gi", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f, HashFile);

        var service = CreateService();
        var result = service.Repair();

        Assert.True(result.Success);
        Assert.True(result.Modified);

        // Verify that all non-gameinfo files that existed before repair are completely unchanged
        foreach (var (filePath, expectedSha) in filesBefore)
        {
            Assert.True(File.Exists(filePath), $"File disappeared: {filePath}");
            Assert.Equal(expectedSha, HashFile(filePath));
        }

        // Verify that the ONLY changes in _work are gameinfo.gi and the new backup folder
        var nonBackupNewFiles = Directory.EnumerateFiles(_work, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(_backupsRoot, StringComparison.OrdinalIgnoreCase) && !f.EndsWith("gameinfo.gi", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(filesBefore.Keys.OrderBy(x => x), nonBackupNewFiles.OrderBy(x => x));
    }
}
