using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>
/// Observation-only runtime/compatibility status for Controller/UI consumption. Reads
/// files and process state; never repairs, updates or writes anything.
/// </summary>
public sealed partial class RuntimeStatusService
{
    public sealed class Options
    {
        public string? Cs2Root { get; init; }
        public string? LockPath { get; init; }
        public string? BackupsRoot { get; init; }
        public string? PresetsRoot { get; init; }
        public string? ActivePresetPath { get; init; }
        public Func<bool>? Cs2RunningProbe { get; init; }
    }

    private readonly Options _options;

    public RuntimeStatusService(Options? options = null)
    {
        _options = options ?? new Options();
    }
    /// <summary>Lightweight check for process state without full file/hash scan.</summary>
    public bool CheckCs2Running() => (_options.Cs2RunningProbe ?? Cs2Locator.IsCs2Running)();


    [GeneratedRegex(@"^(PatchVersion|ClientVersion)=(.*)$", RegexOptions.Multiline)]
    private static partial Regex SteamInfLine();

    public RuntimeStatus GetStatus(InventorySimulatorLock? lockSnapshot = null)
    {
        var status = new RuntimeStatus();

        string? cs2Root = _options.Cs2Root;
        if (cs2Root is null)
        {
            try { cs2Root = Cs2Locator.FindCs2Root(); }
            catch { cs2Root = null; }
        }
        status.Cs2Detected = cs2Root is not null;
        status.Cs2Root = cs2Root;
        status.Cs2Running = (_options.Cs2RunningProbe ?? Cs2Locator.IsCs2Running)();

        // Lock / expected baseline
        var lockPath = _options.LockPath;
        InventorySimulatorLock? pin = lockSnapshot;
        if (pin is null && lockPath is not null && File.Exists(lockPath))
            pin = InventorySimulatorLock.Load(lockPath);
        status.Lock = pin;

        if (cs2Root is not null)
        {
            var csgoDir = Path.Combine(cs2Root, "game", "csgo");

            var steamInf = Path.Combine(csgoDir, "steam.inf");
            if (File.Exists(steamInf))
            {
                foreach (Match m in SteamInfLine().Matches(File.ReadAllText(steamInf)))
                {
                    if (m.Groups[1].Value == "PatchVersion") status.PatchVersion = m.Groups[2].Value.Trim();
                    else status.ClientVersion = m.Groups[2].Value.Trim();
                }
            }
            var acf = Path.Combine(cs2Root, "..", "..", "appmanifest_730.acf");
            if (File.Exists(acf))
            {
                var m2 = Regex.Match(File.ReadAllText(acf), @"""buildid""\s+""(\d+)""");
                if (m2.Success) status.BuildId = m2.Groups[1].Value;
            }

            if (pin?.PatchVersion is { } expPatch)
                status.TestedBuildMatch =
                    status.PatchVersion == expPatch
                    && (pin.ClientVersion is null || status.ClientVersion == pin.ClientVersion)
                    && (pin.BuildId is null || status.BuildId == pin.BuildId)
                        ? "match"
                        : "changed";
            else
                status.TestedBuildMatch = "unknown";

            var gameinfo = Path.Combine(csgoDir, "gameinfo.gi");
            var searchPaths = File.Exists(gameinfo)
                ? Regex.Match(File.ReadAllText(gameinfo), @"\bSearchPaths\s*\{([^}]*)\}", RegexOptions.Singleline).Groups[1].Value
                : "";
            status.GameinfoHasMetamod = Regex.IsMatch(searchPaths, @"^[ \t]*Game[ \t]+csgo/addons/metamod[ \t]*\r?$", RegexOptions.Multiline);
            // Historical locks support presence only. Explicit compatibility candidates
            // additionally verify their recorded loader/native/API file hashes below.
            status.MetaModNativeStatus = File.Exists(Path.Combine(csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll"))
                ? "present-unverified"
                : "missing";
            status.CounterStrikeSharpNativeStatus = Directory.Exists(Path.Combine(csgoDir, "addons", "counterstrikesharp", "bin", "win64"))
                ? "present-unverified"
                : "missing";
            if (pin?.CandidatePatchVersion is not null
                && status.PatchVersion == pin.CandidatePatchVersion
                && status.ClientVersion == pin.CandidateClientVersion
                && status.BuildId == pin.CandidateBuildId)
            {
                status.FrameworkCandidate = true;
                status.MetaModNativeStatus = VerifyFrameworkFiles(csgoDir, pin.CandidateMetaModFiles, pin.CandidateMetaModVersion);
                status.CounterStrikeSharpNativeStatus = VerifyFrameworkFiles(csgoDir, pin.CandidateCounterStrikeSharpFiles, pin.CandidateCounterStrikeSharpVersion);
            }

            var isDll = Path.Combine(csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll");
            status.InventorySimulatorPluginPresent = File.Exists(isDll);
            if (status.InventorySimulatorPluginPresent)
            {
                using var fs = File.OpenRead(isDll);
                status.InventorySimulatorDllSha256 = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                status.PatchedDllMatch = pin?.PatchedDllSha256 is { } expected
                    ? (status.InventorySimulatorDllSha256 == expected ? "match" : "mismatch")
                    : "unknown";
            }

            var fixture = Path.Combine(csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");
            status.FixtureInstalled = File.Exists(fixture);
            if (status.FixtureInstalled)
            {
                using var fs = File.OpenRead(fixture);
                status.FixtureSha256 = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }

            // Only records for the CURRENT installed fixture path are rollback-relevant;
            // a record left over from a different install location cannot be RestoreLatest'd.
            status.LatestApply = FindLatestApply(
                _options.BackupsRoot ?? CorePaths.FixtureBackupRoot,
                Path.GetFullPath(fixture),
                status.FixtureSha256,
                status.Cs2Running);
        }

        var presetsRoot = _options.PresetsRoot ?? CorePaths.PresetsHumanRoot;
        var active = new Store.ActivePresetState(_options.ActivePresetPath).GetActive();
        status.ActivePreset = active;
        status.ActivePresetExists = active is not null && File.Exists(Path.Combine(presetsRoot, active));

        return status;
    }

    private static string VerifyFrameworkFiles(string csgoDir, IReadOnlyDictionary<string, string> files, string? version)
    {
        if (files.Count == 0) return "present-unverified";
        var root = Path.GetFullPath(csgoDir) + Path.DirectorySeparatorChar;
        foreach (var (relativePath, expectedHash) in files)
        {
            var path = Path.GetFullPath(Path.Combine(csgoDir, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return "missing";
            using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return "hash-mismatch";
        }
        return $"candidate-hash-match ({version}; 实机待验收)";
    }

    private static LatestApplyInfo? FindLatestApply(string backupsRoot, string installedFixturePath, string? currentFixtureSha, bool cs2Running)
    {
        if (!Directory.Exists(backupsRoot)) return null;
        LatestApplyInfo? latest = null;
        foreach (var recordPath in Directory.EnumerateFiles(backupsRoot, "apply-record.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
                var r = doc.RootElement;
                var installedPath = r.TryGetProperty("installedPath", out var i) ? i.GetString() ?? "" : "";
                if (!installedPath.Equals(installedFixturePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var backupPath = r.TryGetProperty("backupPath", out var b) ? b.GetString() ?? "" : "";
                var backupPresent = !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath);
                var previousSha256 = r.TryGetProperty("installed", out var inst) && inst.TryGetProperty("previousSha256", out var ps) ? ps.GetString() ?? "" : "";
                var newSha256 = inst.ValueKind == JsonValueKind.Object && inst.TryGetProperty("newSha256", out var ns) ? ns.GetString() ?? "" : "";
                var currentMatchesNewSha256 = currentFixtureSha is not null && string.Equals(currentFixtureSha, newSha256, StringComparison.OrdinalIgnoreCase);

                bool canExecute = true;
                string? blockReason = null;
                if (cs2Running)
                {
                    canExecute = false;
                    blockReason = "CS2 正在运行，关闭游戏后可恢复";
                }
                else if (!backupPresent)
                {
                    canExecute = false;
                    blockReason = "备份文件不存在";
                }
                else if (!currentMatchesNewSha256)
                {
                    canExecute = false;
                    blockReason = currentFixtureSha is null
                        ? "当前未安装饰品运行文件，与最近应用记录不匹配"
                        : "当前安装的文件哈希与最近应用记录不一致（已被修改或已漂移）";
                }

                var info = new LatestApplyInfo
                {
                    RecordPath = recordPath,
                    CreatedAt = r.TryGetProperty("createdAt", out var c) ? c.GetString() ?? "" : "",
                    PresetPath = r.TryGetProperty("presetPath", out var p) ? p.GetString() ?? "" : "",
                    ProjectedSha256 = r.TryGetProperty("projectedSha256", out var s) ? s.GetString() ?? "" : "",
                    InstalledPath = installedPath,
                    RollbackAvailable = backupPresent,
                    BackupPresent = backupPresent,
                    BackupPath = backupPath,
                    PreviousSha256 = previousSha256,
                    NewSha256 = newSha256,
                    CurrentMatchesNewSha256 = currentMatchesNewSha256,
                    BlockedByCs2Running = cs2Running,
                    RollbackCanExecute = canExecute,
                    RollbackBlockReason = blockReason,
                };
                if (latest is null
                    || string.CompareOrdinal(info.CreatedAt, latest.CreatedAt) > 0
                    || (info.CreatedAt == latest.CreatedAt && string.CompareOrdinal(recordPath, latest.RecordPath) > 0))
                    latest = info;
            }
            catch (JsonException) { /* skip unreadable records */ }
        }
        return latest;
    }
}

public enum RuntimeHealthLevel
{
    Ready,
    Attention,
    Blocked
}

public sealed class RuntimeStatus
{
    public bool Cs2Detected { get; set; }
    public string? Cs2Root { get; set; }
    public bool Cs2Running { get; set; }
    public string? PatchVersion { get; set; }
    public string? ClientVersion { get; set; }
    public string? BuildId { get; set; }
    /// <summary>"match" | "changed" | "unknown" against the lock's tested build.</summary>
    public string TestedBuildMatch { get; set; } = "unknown";
    public bool GameinfoHasMetamod { get; set; }
    public bool FrameworkCandidate { get; set; }
    /// <summary>"present-unverified" | "missing" | "hash-mismatch" | "candidate-hash-match (...)".
    /// Candidate hashes prove file identity, never real-game acceptance.</summary>
    public string MetaModNativeStatus { get; set; } = "missing";
    /// <summary>Same file-identity semantics as MetaModNativeStatus.</summary>
    public string CounterStrikeSharpNativeStatus { get; set; } = "missing";
    public bool InventorySimulatorPluginPresent { get; set; }
    public string? InventorySimulatorDllSha256 { get; set; }
    /// <summary>"match" | "mismatch" | "unknown" against the lock's patched DLL hash.</summary>
    public string PatchedDllMatch { get; set; } = "unknown";
    public bool FixtureInstalled { get; set; }
    public string? FixtureSha256 { get; set; }
    public string? ActivePreset { get; set; }
    public bool ActivePresetExists { get; set; }
    /// <summary>Latest apply record whose InstalledPath equals the CURRENT installed fixture path.</summary>
    public LatestApplyInfo? LatestApply { get; set; }
    public InventorySimulatorLock? Lock { get; set; }
    public RuntimeHealthLevel HealthLevel
    {
        get
        {
            if (BlockedReasons.Count > 0) return RuntimeHealthLevel.Blocked;
            if (AttentionReasons.Count > 0) return RuntimeHealthLevel.Attention;
            return RuntimeHealthLevel.Ready;
        }
    }

    public IReadOnlyList<string> BlockedReasons
    {
        get
        {
            var list = new List<string>();
            if (!Cs2Detected) list.Add("未检测到 CS2 安装路径");
            if (!GameinfoHasMetamod && Cs2Detected) list.Add("gameinfo.gi 未配置 MetaMod 启动项");
            if (MetaModNativeStatus == "missing" && Cs2Detected) list.Add("MetaMod 原生组件缺失");
            if (CounterStrikeSharpNativeStatus == "missing" && Cs2Detected) list.Add("CounterStrikeSharp 原生组件缺失");
            if (MetaModNativeStatus == "hash-mismatch") list.Add("MetaMod candidate 文件哈希不匹配");
            if (CounterStrikeSharpNativeStatus == "hash-mismatch") list.Add("CounterStrikeSharp candidate 文件哈希不匹配");
            if (!InventorySimulatorPluginPresent && Cs2Detected) list.Add("InventorySimulator 插件文件缺失");
            if (PatchedDllMatch == "mismatch") list.Add("InventorySimulator patched DLL 校验不匹配");
            if (!FixtureInstalled && Cs2Detected) list.Add("饰品运行文件 (inventories.json) 未安装");
            return list;
        }
    }
    public IReadOnlyList<string> AttentionReasons
    {
        get
        {
            var list = new List<string>();
            if (Cs2Detected && TestedBuildMatch == "changed") list.Add("CS2 客户端版本已更新，与当前测试基线不同");
            if (FrameworkCandidate) list.Add("当前 framework 为 compatibility candidate，文件校验不代表实机验收通过");
            if (string.IsNullOrEmpty(ActivePreset) || !ActivePresetExists) list.Add("未选择或未找到已激活预设");
            return list;
        }
    }

    public string HealthSummary => HealthLevel switch
    {
        RuntimeHealthLevel.Ready => "运行环境就绪",
        RuntimeHealthLevel.Attention => string.Join("; ", AttentionReasons),
        RuntimeHealthLevel.Blocked => string.Join("; ", BlockedReasons),
        _ => "未知状态"
    };
}

public sealed class LatestApplyInfo
{
    public required string RecordPath { get; init; }
    public required string CreatedAt { get; init; }
    public required string PresetPath { get; init; }
    public required string ProjectedSha256 { get; init; }
    public required string InstalledPath { get; init; }
    public required bool RollbackAvailable { get; init; }
    public bool BackupPresent { get; init; }
    public string? BackupPath { get; init; }
    public string? PreviousSha256 { get; init; }
    public string? NewSha256 { get; init; }
    public bool CurrentMatchesNewSha256 { get; init; }
    public bool BlockedByCs2Running { get; init; }
    public bool RollbackCanExecute { get; init; }
    public string? RollbackBlockReason { get; init; }
}
