using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.Services;

public enum BotDifficulty { Low, Medium, High }
public enum BotAim { Mixed, Head, Body }
public enum BotNades { Off, Less, Normal, More, Max }
public enum LocalPlayMode { NormalHuman, EnhancedHuman }
public sealed record BotPreferences(BotDifficulty Difficulty = BotDifficulty.Medium, BotAim Aim = BotAim.Mixed, BotNades Nades = BotNades.Normal, string? LastInstallId = null, LocalPlayMode TargetMode = LocalPlayMode.NormalHuman)
{
    public string ConsoleCommands => $"bot_aim {Aim.ToString().ToLowerInvariant()}; bot_nades {Nades.ToString().ToLowerInvariant()}";
}
public sealed record BotCandidate(BotDifficulty Difficulty, string Directory, string ManifestSha256, bool PreservesHuman = false, bool MatchSetup = false, bool RuntimeIntegration = false);
public sealed record BotModeReadiness(bool CanEnable, string Reason, string CurrentBuild, string CandidateBuild, string BotRef, bool HumanFilesValid);
public sealed record BotInstalledSnapshot(bool Valid, string Reason, BotDifficulty? Difficulty = null, bool HasMatchSetup = false);
public enum LocalPlayReadinessKind { Ready, NeedsLocalPreparation, NeedsRepair, Unknown }
public sealed record LocalPlayReadiness(LocalPlayReadinessKind Kind, string Reason, BotDifficulty? InstalledDifficulty = null, bool HasMatchSetup = false);
public sealed record BotSwitchPlan(string? OldInstallId, BotDifficulty TargetDifficulty, string TargetManifestSha256, bool AlreadyPrepared, string Summary, bool ProtectsExistingLayers = false, string? HumanHandoffInstallId = null);
public sealed record BotPrepareJournal(string OperationId, string? OldInstallId, BotDifficulty TargetDifficulty,
    string TargetManifestSha256, string Stage, string? NewInstallId = null, string? Error = null);
public sealed record BotProcessResult(int ExitCode, string Stdout, string Stderr);
public interface IBotProcessRunner { Task<BotProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments); }
public sealed class BotProcessRunner : IBotProcessRunner
{
    public async Task<BotProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        info.StandardErrorEncoding = System.Text.Encoding.UTF8;
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException("无法启动受约束的实验入口");
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        // Never kill a mutating transaction on an ordinary UI timeout/cancellation.
        await p.WaitForExitAsync();
        return new(p.ExitCode, await stdout, await stderr);
    }
}

/// <summary>Constrained adapter. No game-file writes; the versioned candidate transaction owns every mutation.</summary>
public sealed class BotExperimentService
{
    private readonly IBotProcessRunner _runner;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly Func<bool> _running;
    private readonly IReadOnlyList<BotCandidate> _candidates;
    private readonly string _humanLockPath;
    public string DataRoot { get; }
    public string Cs2Root { get; }
    public string ReleasesRoot { get; }
    public string BackupRoot { get; }
    public string LogsRoot { get; }
    public string PreferencesPath { get; }
    public string PrepareJournalPath => Path.Combine(DataRoot, "app-data", "bot-experiment", "prepare-operation.json");
    public bool Cs2Running => _running();
    public SteamInstallState SteamState => SteamInstallState.Inspect(Cs2Root);
    public BotExperimentIdentity Identity => BotExperimentIdentity.Inspect(Cs2Root, BackupRoot);
    public IReadOnlyList<BotCandidate> Candidates => _candidates;
    public IReadOnlyList<string> AvailableMaps => System.IO.Directory.Exists(Path.Combine(Cs2Root, "game", "csgo", "maps"))
        ? System.IO.Directory.EnumerateFiles(Path.Combine(Cs2Root, "game", "csgo", "maps"), "*.vpk", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).Where(name => name is not null && !name.EndsWith("_vanity", StringComparison.Ordinal)
                && Regex.IsMatch(name, "^(de_|cs_|ar_)[a-z0-9_]{1,40}$"))
            .Select(name => name!).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray()
        : [];
    public bool HasMap(string map) => AvailableMaps.Contains(map, StringComparer.Ordinal);

    public BotExperimentService(string dataRoot, string cs2Root, Func<bool>? running = null, IBotProcessRunner? runner = null, IReadOnlyList<BotCandidate>? candidates = null, string? humanLockPath = null)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        Cs2Root = Path.GetFullPath(cs2Root);
        ReleasesRoot = Path.Combine(dataRoot, "releases", "bot-experiment");
        BackupRoot = Path.Combine(dataRoot, "backups", "bot-experiment");
        LogsRoot = Path.Combine(dataRoot, "diagnostics", "bot-experiment");
        PreferencesPath = Path.Combine(dataRoot, "app-data", "bot-experiment", "preferences.json");
        _runner = runner ?? new BotProcessRunner(); _running = running ?? Cs2Locator.IsCs2Running;
        _humanLockPath = humanLockPath ?? Path.Combine(AppContext.BaseDirectory, "runtime", "inventory-simulator.lock.json");
        if (candidates is not null) _candidates = candidates;
        else
        {
            using var stream = typeof(BotExperimentService).Assembly.GetManifestResourceStream("CS2LocalKit.App.BotCandidates.json") ?? throw new InvalidDataException("缺少固定候选白名单");
            _candidates = JsonSerializer.Deserialize<List<BotCandidate>>(stream) ?? [];
        }
    }

    public BotPreferences LoadPreferences()
    {
        if (!File.Exists(PreferencesPath)) return new();
        var p = JsonSerializer.Deserialize<BotPreferences>(File.ReadAllText(PreferencesPath)) ?? throw new InvalidDataException("Bot 偏好无效");
        if (!Enum.IsDefined(p.Difficulty) || !Enum.IsDefined(p.Aim) || !Enum.IsDefined(p.Nades) || !Enum.IsDefined(p.TargetMode) || p.LastInstallId is not null && !Regex.IsMatch(p.LastInstallId, "^[0-9a-f]{32}$")) throw new InvalidDataException("Bot 偏好值无效");
        return p;
    }

    public void SavePreferences(BotPreferences value)
    {
        if (!Enum.IsDefined(value.Difficulty) || !Enum.IsDefined(value.Aim) || !Enum.IsDefined(value.Nades) || !Enum.IsDefined(value.TargetMode)) throw new InvalidDataException("未知 Bot 选项");
        CheckNoLinks(PreferencesPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        var tmp = PreferencesPath + ".new";
        CheckNoLinks(tmp);
        File.WriteAllText(tmp, JsonSerializer.Serialize(value));
        File.Move(tmp, PreferencesPath, true);
    }

    public BotCandidate Candidate(BotDifficulty difficulty) => _candidates.Where(c => c.Difficulty == difficulty).OrderBy(c => c.PreservesHuman).ThenBy(c => c.MatchSetup).LastOrDefault() ?? throw new InvalidOperationException("该难度没有已固定候选，禁止安装");
    private BotCandidate JointCandidate(BotDifficulty difficulty) => _candidates.Where(c => c.Difficulty == difficulty && c.PreservesHuman).OrderBy(c => c.MatchSetup).LastOrDefault()
        ?? throw new InvalidOperationException("暂无通过门禁的 Bot + Human 联合候选；旧隔离实验仅保留精确恢复入口");

    private (bool Valid, string Reason) HumanBaseline()
    {
        try
        {
            var pin = InventorySimulatorLock.Load(_humanLockPath);
            var files = pin.MetaModFiles.Concat(pin.CounterStrikeSharpFiles).ToDictionary(p => p.Key, p => p.Value);
            if (files.Count < 5 || pin.PatchedDllSha256 is null) return (false, "Human/framework 固定身份不完整");
            files.Add("addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll", pin.PatchedDllSha256);
            using var doc = JsonDocument.Parse(File.ReadAllText(_humanLockPath));
            if (doc.RootElement.TryGetProperty("gamedataCompatibilityCandidate", out var gd) && gd.TryGetProperty("installed", out var installed) && installed.GetBoolean())
                files.Add("addons/counterstrikesharp/gamedata/inventory-simulator.json", gd.GetProperty("artifact").GetProperty("sha256").GetString()!);
            foreach (var (relative, hash) in files)
                if (HashFile(SafePath(Path.Combine(Cs2Root, "game/csgo"), relative)) != hash) return (false, "Human/framework 文件身份不匹配：" + relative);
            var gameinfo = File.ReadAllText(Path.Combine(Cs2Root, "game/csgo/gameinfo.gi"));
            if (Regex.Matches(gameinfo, @"(?m)^\s*Game\s+csgo/addons/metamod\s*$").Count != 1) return (false, "MetaMod 启动项缺失或有歧义");
            return (true, "Human 文件身份匹配；当前 build 的游戏内呈现仍待人工验证");
        }
        catch (Exception ex) { return (false, "无法核实 Human 基线：" + ex.Message); }
    }

    /// <summary>Read the active transaction against its own postimages. The pure Human lock describes only the restored baseline.</summary>
    public BotInstalledSnapshot InspectInstalled()
    {
        var identity = Identity;
        if (!identity.BlocksHuman) return new(false, "没有活动增强人机事务");
        if (identity.InstallId is null || identity.RecoveryRequired || !identity.PreservesHuman)
            return new(false, identity.Reason);
        try
        {
            if (IsPlayableSubstrate()) return new(true,"已核实当前三层可玩基线；准备整合候选后才能自动应用阵容",BotDifficulty.Medium,false);
            var backup = SafePath(BackupRoot, identity.InstallId);
            var recordPath = SafePath(backup, "install-record.json");
            var manifestPath = SafePath(backup, "candidate-manifest.json");
            using var record = JsonDocument.Parse(File.ReadAllText(recordPath));
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var r = record.RootElement;
            var m = manifest.RootElement;
            RuntimeIntegrationGuard.VerifyParents(Cs2Root,m);
            if (r.GetProperty("state").GetString() != "installed" ||
                r.GetProperty("installId").GetString() != identity.InstallId ||
                r.GetProperty("manifestSha256").GetString() != HashFile(manifestPath) ||
                m.GetProperty("humanPolicy").GetString() != "preserve")
                throw new InvalidDataException("活动事务记录与 manifest 不匹配");
            var steam = SteamState;
            var build = m.GetProperty("targetBuild");
            if (!steam.Idle || steam.BuildId != build.GetProperty("buildId").GetString() ||
                steam.PatchVersion != build.GetProperty("PatchVersion").GetString() ||
                steam.ClientVersion != build.GetProperty("ClientVersion").GetString())
                throw new InvalidDataException("活动事务与当前 Steam/CS2 build 不匹配");
            var humanUpdate = BotHumanFixtureSidecar.Exists(BackupRoot, identity.InstallId)
                ? new BotHumanFixtureSidecar(Cs2Root, BackupRoot, identity.InstallId).Inspect() : null;
            var entries = r.GetProperty("entries").EnumerateArray().ToArray();
            var targets = entries.Select(e => e.GetProperty("target").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (entries.Length == 0 || !m.GetProperty("files").EnumerateArray().All(e => targets.Contains(e.GetProperty("target").GetString()!)))
                throw new InvalidDataException("活动事务缺少候选文件清单");
            foreach (var entry in entries)
            {
                var path = SafePath(Cs2Root, entry.GetProperty("target").GetString()!);
                var after = entry.GetProperty("after");
                if (after.ValueKind == JsonValueKind.Null)
                {
                    if (File.Exists(path) || System.IO.Directory.Exists(path)) throw new InvalidDataException("隔离文件重新出现：" + entry.GetProperty("target").GetString());
                    continue;
                }
                var expected = entry.GetProperty("target").GetString() == BotHumanFixtureSidecar.Target && humanUpdate is not null
                    ? humanUpdate.CurrentSha256 : after.GetProperty("sha256").GetString();
                if (after.GetProperty("kind").GetString() != "file" || HashFile(path) != expected)
                    throw new InvalidDataException("活动事务文件漂移：" + entry.GetProperty("target").GetString());
            }
            foreach (var file in m.GetProperty("coexistence").GetProperty("humanFiles").EnumerateObject())
                if (HashFile(SafePath(Cs2Root, file.Name)) != (file.Name == BotHumanFixtureSidecar.Target && humanUpdate is not null ? humanUpdate.CurrentSha256 : file.Value.GetString()))
                    throw new InvalidDataException("Human 文件漂移：" + file.Name);
            var level = Enum.Parse<BotDifficulty>(r.GetProperty("profile").GetProperty("difficulty").GetString()!);
            return new(true, "活动联合安装文件身份匹配；游戏内行为待验证", level, m.TryGetProperty("matchSetup", out _));
        }
        catch (Exception ex) { return new(false, "无法确认活动安装：" + ex.Message); }
    }

    public LocalPlayReadiness Assess(LocalPlayMode mode, BotDifficulty desired)
    {
        var identity = Identity;
        if (identity.BlocksHuman)
        {
            if (identity.RecoveryRequired || identity.InstallId is null)
                return new(LocalPlayReadinessKind.Unknown, identity.Reason);
            var installed = InspectInstalled();
            if (!installed.Valid) return new(LocalPlayReadinessKind.NeedsRepair, installed.Reason);
            if (mode == LocalPlayMode.NormalHuman)
                return new(LocalPlayReadinessKind.NeedsLocalPreparation, "关闭 CS2 后预览并精确恢复活动增强组件", installed.Difficulty, installed.HasMatchSetup);
            if (installed.Difficulty != desired || !installed.HasMatchSetup)
                return new(LocalPlayReadinessKind.NeedsLocalPreparation, "当前增强组件与对局配置不匹配；关闭 CS2 后预览安全切换", installed.Difficulty, installed.HasMatchSetup);
            return new(LocalPlayReadinessKind.Ready, "增强组件文件身份匹配；本局应用仍需会话读回", installed.Difficulty, true);
        }
        var baseline = HumanBaseline();
        if (!baseline.Valid) return new(LocalPlayReadinessKind.NeedsRepair, baseline.Reason);
        if (mode == LocalPlayMode.NormalHuman) return new(LocalPlayReadinessKind.Ready, "普通人机与 Human 基线文件身份匹配");
        var candidate = Readiness(desired);
        return candidate.CanEnable
            ? new(LocalPlayReadinessKind.NeedsLocalPreparation, "增强组件尚未安装；先检查候选并准备对局")
            : new(LocalPlayReadinessKind.NeedsRepair, candidate.Reason);
    }

    public BotModeReadiness Readiness(BotDifficulty difficulty)
    {
        var steam = SteamState;
        var active = Identity;
        var human = active.BlocksHuman
            ? (Valid: InspectInstalled().Valid, Reason: "活动联合安装需先精确恢复，才可安装目标候选")
            : HumanBaseline();
        var build = "未提供"; var botRef = "未提供";
        try
        {
            var candidate = Candidate(difficulty);
            using var doc = JsonDocument.Parse(File.ReadAllText(SafePath(CandidatePath(candidate), "experiment-manifest.json")));
            var m = doc.RootElement; botRef = m.GetProperty("botRef").GetString()!;
            var target = m.GetProperty("targetBuild");
            build = $"{target.GetProperty("PatchVersion").GetString()} / {target.GetProperty("ClientVersion").GetString()} / {target.GetProperty("buildId").GetString()}";
            if (Cs2Running) throw new InvalidOperationException("CS2 正在运行，请退出游戏后切换");
            if (!steam.Idle) throw new InvalidOperationException(steam.Reason);
            if (active.BlocksHuman && !(candidate.RuntimeIntegration && IsPlayableSubstrate())) throw new InvalidOperationException(human.Reason);
            if (!human.Valid) throw new InvalidOperationException(human.Reason);
            if (build != steam.BuildText) throw new InvalidOperationException($"候选 build 已过期（{build}）；禁止套用到当前 build");
            if (!candidate.PreservesHuman) throw new InvalidOperationException("现有候选会隔离 Human，不能用于“增强人机 + 我的饰品”");
            if (m.GetProperty("humanPolicy").GetString() != "preserve" || m.GetProperty("coexistence").GetProperty("status").GetString() != "ready-for-manual-test")
                throw new InvalidDataException("联合兼容性门禁尚未通过");
            return new(true, "联合候选可检查切换预览；实机效果尚未验收", steam.BuildText, build, botRef, human.Valid);
        }
        catch (Exception ex) { return new(false, ex.Message, steam.BuildText, build, botRef, human.Valid); }
    }
    private bool IsPlayableSubstrate()
    {
        if (File.Exists(Path.Combine(Cs2Root,RuntimeIntegrationGuard.Marker))) return false;
        var candidate=_candidates.LastOrDefault(c=>c.RuntimeIntegration);
        if(candidate is null)return false;
        var file=SafePath(CandidatePath(candidate),"experiment-manifest.json");
        if(HashFile(file)!=candidate.ManifestSha256)throw new InvalidDataException("整合候选身份漂移");
        using var doc=JsonDocument.Parse(File.ReadAllText(file));var m=doc.RootElement;
        RuntimeIntegrationGuard.VerifyParents(Cs2Root,m);
        var steam=SteamState;var build=m.GetProperty("targetBuild");
        if(!steam.Idle||steam.BuildId!=build.GetProperty("buildId").GetString()||steam.PatchVersion!=build.GetProperty("PatchVersion").GetString()||steam.ClientVersion!=build.GetProperty("ClientVersion").GetString())return false;
        foreach(var p in m.GetProperty("baselineFiles").EnumerateObject())
            if(HashFile(SafePath(Cs2Root,p.Name))!=p.Value.GetString())
            {
                if(p.Name!=BotHumanFixtureSidecar.Target)return false;
                RuntimeIntegrationGuard.RestoredHumanHandoff(Cs2Root,BackupRoot,m);
            }
        return Identity.InstallId==m.GetProperty("runtimeIntegration").GetProperty("parents").GetProperty("bot").GetProperty("installId").GetString();
    }
    public string CandidatePath(BotCandidate c)
    {
        if (c.Directory != Path.GetFileName(c.Directory) || c.Directory is "" or "." or "..") throw new InvalidDataException("候选路径不在白名单目录");
        return SafePath(ReleasesRoot, c.Directory);
    }
    public static string HashFile(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }
    public static void CheckNoLinks(string path)
    {
        for (var p = Path.GetFullPath(path); !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || System.IO.Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("拒绝 reparse/junction 路径：" + p);
    }
    private static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split('/', '\\').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.'))) throw new InvalidDataException("不安全的候选相对路径");
        var p = Path.GetFullPath(Path.Combine(root, relative));
        if (!p.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("候选路径逃逸");
        CheckNoLinks(p); return p;
    }
    private void Validate(BotCandidate candidate)
    {
        var root = CandidatePath(candidate);
        var manifest = SafePath(root, "experiment-manifest.json");
        if (!Regex.IsMatch(candidate.ManifestSha256, "^[0-9a-f]{64}$") || HashFile(manifest) != candidate.ManifestSha256) throw new InvalidDataException("固定候选 manifest hash 不匹配");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest)); var m = doc.RootElement;
        if (m.GetProperty("schemaVersion").GetInt32() != 1 || m.GetProperty("lane").GetString() != "bot-experiment" || m.GetProperty("testFixture").GetBoolean() || m.GetProperty("botRef").GetString() != "e18c029a76b7515ea5cb15cb08c41f03de4c0a57" || m.GetProperty("metamod").GetProperty("build").GetInt32() != 1481 || m.GetProperty("cssVersion").GetString() != "1.0.376" || m.GetProperty("profile").GetProperty("difficulty").GetString() != candidate.Difficulty.ToString()) throw new InvalidDataException("候选身份/profile 不匹配");
        if (candidate.PreservesHuman && (m.GetProperty("humanPolicy").GetString() != "preserve" || m.GetProperty("coexistence").GetProperty("status").GetString() != "ready-for-manual-test")) throw new InvalidDataException("联合候选身份不匹配");
        if (candidate.MatchSetup && (!m.TryGetProperty("matchSetup",out var setup) || setup.GetProperty("schemaVersion").GetInt32()!=1 || setup.GetProperty("adapter").GetString()!="LocalKitMatchSetup")) throw new InvalidDataException("候选缺少固定开局配置适配层");
        var expected = m.GetProperty("bundleFiles").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
        void Walk(string directory)
        {
            CheckNoLinks(directory);
            foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
            {
                CheckNoLinks(entry);
                if (System.IO.Directory.Exists(entry)) { Walk(entry); continue; }
                var rel = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (rel is "experiment-manifest.json" or "MANIFEST.sha256") continue;
                if (!expected.Remove(rel, out var hash) || HashFile(entry) != hash) throw new InvalidDataException("Bundle 内容/hash 漂移：" + rel);
            }
        }
        foreach (var entry in expected.Keys) SafePath(root, entry);
        Walk(root);
        if (expected.Count != 0) throw new InvalidDataException("候选 bundle 缺文件");
        SafePath(root, "tools/Invoke-BotExperiment.ps1"); SafePath(root, "tools/bot_experiment.py");
    }

    public async Task<JsonElement> ExecuteAsync(string action, BotDifficulty difficulty, bool apply = false, string? installId = null)
    {
        if (!await _operation.WaitAsync(0)) throw new InvalidOperationException("另一个 Bot 操作正在执行");
        try
        {
            using var gameWrite = apply ? BotHumanOperationGate.Acquire(Cs2Root) : null;
            return await ExecuteWithHumanUpdateAsync(action, difficulty, apply, installId);
        }
        finally { _operation.Release(); }
    }

    private async Task<JsonElement> ExecuteWithHumanUpdateAsync(string action, BotDifficulty difficulty, bool apply, string? installId)
    {
        if (installId is null || action is not ("Diagnostics" or "Restore") ||
            !BotHumanFixtureSidecar.Exists(BackupRoot, installId))
            return await ExecuteCoreAsync(action, difficulty, apply, installId);
        if (Cs2Running || !SteamState.Idle) throw new InvalidOperationException("CS2 运行或 Steam 更新状态未稳定，禁止恢复");
        var sidecar = new BotHumanFixtureSidecar(Cs2Root, BackupRoot, installId);
        var count = sidecar.PreviewExactRestore();
        if (action == "Diagnostics" || !apply)
        {
            using var record = JsonDocument.Parse(File.ReadAllText(SafePath(BackupRoot, installId + "/install-record.json")));
            return JsonSerializer.SerializeToElement(new { state = "sidecar-preview", count,
                entries = record.RootElement.GetProperty("entries").Clone() });
        }
        sidecar.SuspendForExactRestore();
        try
        {
            var result = await ExecuteCoreAsync("Restore", difficulty, true, installId);
            sidecar.FinishExactRestore();
            return result;
        }
        catch
        {
            // Only reconcile a stage whose exact Bot record proves the corresponding disk state.
            var state = ReadRecordState(installId);
            if (state == "installed" && Identity.InstallId == installId) sidecar.AbortSuspendedRestore();
            else if (state == "restored" && (!Identity.BlocksHuman || RuntimeIntegrationGuard.IsRestoredSubstrate(Cs2Root,BackupRoot,installId))) sidecar.FinishExactRestore();
            throw;
        }
    }

    private async Task<JsonElement> ExecuteCoreAsync(string action, BotDifficulty difficulty, bool apply = false, string? installId = null)
    {
            if (action is not ("Preview" or "Install" or "Diagnostics" or "Restore") || apply && action is not ("Install" or "Restore")) throw new InvalidOperationException("未知实验操作");
            if (Cs2Running) throw new InvalidOperationException("CS2 正在运行，关闭游戏后再操作");
            if (!SteamState.Idle) throw new InvalidOperationException(SteamState.Reason);
            BotCandidate candidate;
            if (action is "Restore" or "Diagnostics")
            {
                if (!Regex.IsMatch(installId ?? "", "^[0-9a-f]{32}$")) throw new InvalidDataException("必须指定精确 install ID");
                var record = SafePath(BackupRoot, installId + "/install-record.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(record)); var r = doc.RootElement;
                if (r.GetProperty("installId").GetString() != installId || !Path.GetFullPath(r.GetProperty("cs2Root").GetString()!).Equals(Cs2Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装记录目标/ID 不匹配");
                candidate = _candidates.SingleOrDefault(c => c.ManifestSha256 == r.GetProperty("manifestSha256").GetString()) ?? throw new InvalidDataException("安装记录不属于固定候选；保留备份并使用原候选恢复入口");
            }
            else
            {
                // Old isolation candidates remain readable for recovery, never for daily activation.
                CandidatePath(Candidate(difficulty));
                var readiness = Readiness(difficulty);
                if (!readiness.CanEnable) throw new InvalidOperationException(readiness.Reason);
                candidate = JointCandidate(difficulty);
            }
            await Task.Run(() => Validate(candidate));
            var python = await _runner.RunAsync("python", ["--version"]);
            var match = Regex.Match(python.Stdout + python.Stderr, @"Python (\d+)\.(\d+)");
            if (python.ExitCode != 0 || !match.Success || new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) < new Version(3, 12)) throw new InvalidOperationException("需要已有 Python 3.12+，未安装任何依赖");
            async Task<JsonElement> Invoke(string invokeAction, bool write)
            {
                if (Cs2Running || !SteamState.Idle) throw new InvalidOperationException("CS2 运行或 Steam 更新状态变化，已停止操作");
                var args = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", SafePath(CandidatePath(candidate), "tools/Invoke-BotExperiment.ps1"), "-Action", invokeAction, "-BackupRoot", BackupRoot, "-PythonExe", "python" };
                if (invokeAction is "Preview" or "Install") args.AddRange(["-ReleaseDir", CandidatePath(candidate), "-ManifestSha256", candidate.ManifestSha256, "-Cs2Root", Cs2Root, "-PreserveHumanCosmetics"]);
                else args.AddRange(["-InstallId", installId!]);
                if (write) args.Add("-Apply");
                var result = await _runner.RunAsync("pwsh", args);
                CheckNoLinks(LogsRoot); System.IO.Directory.CreateDirectory(LogsRoot);
                File.WriteAllText(Path.Combine(LogsRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"), JsonSerializer.Serialize(new { action = invokeAction, apply = write, candidate.ManifestSha256, installId, result.ExitCode, result.Stdout, result.Stderr }));
                if (result.ExitCode != 0) throw new InvalidOperationException($"实验入口拒绝/失败 ({result.ExitCode})：\n{result.Stderr}\n{result.Stdout}");
                using var doc = JsonDocument.Parse(result.Stdout);
                return doc.RootElement.Clone();
            }
            // Both mutations always get a fresh full preview in the same serialized operation.
            if (apply) await Invoke(action, false);
            var result = await Invoke(action, apply);
            if (apply && action == "Install")
            {
                installId = result.GetProperty("installId").GetString();
                var identity = Identity;
                if (identity.InstallId != installId || !identity.PreservesHuman || identity.State != "installed" || identity.RecoveryRequired)
                    throw new InvalidDataException("安装入口结束，但磁盘/事务状态未确认；请检查精确恢复记录");
                await Invoke("Diagnostics", false);
            }
            if (apply && action == "Restore" && ((Identity.BlocksHuman && !(candidate.RuntimeIntegration && IsPlayableSubstrate())) || ReadRecordState(installId!) != "restored"))
                throw new InvalidDataException("恢复入口结束，但原环境状态未确认；保留精确备份");
            return result;
    }

    public BotPrepareJournal? ReadPrepareJournal()
    {
        CheckNoLinks(PrepareJournalPath);
        return File.Exists(PrepareJournalPath)
            ? JsonSerializer.Deserialize<BotPrepareJournal>(File.ReadAllText(PrepareJournalPath)) ?? throw new InvalidDataException("准备记录为空")
            : null;
    }

    private void WritePrepareJournal(BotPrepareJournal journal)
    {
        CheckNoLinks(PrepareJournalPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(PrepareJournalPath)!);
        var temp = PrepareJournalPath + ".new";
        CheckNoLinks(temp);
        File.WriteAllText(temp, JsonSerializer.Serialize(journal));
        File.Move(temp, PrepareJournalPath, true);
    }

    public async Task<BotSwitchPlan> PreviewSwitchAsync(BotDifficulty difficulty)
    {
        if (!await _operation.WaitAsync(0)) throw new InvalidOperationException("另一个 Bot 操作正在执行");
        try { return await PreviewSwitchCoreAsync(difficulty); }
        finally { _operation.Release(); }
    }

    private async Task<BotSwitchPlan> PreviewSwitchCoreAsync(BotDifficulty difficulty)
    {
        if (Cs2Running || !SteamState.Idle) throw new InvalidOperationException("CS2 运行或 Steam 更新状态未稳定，禁止准备");
        var target = JointCandidate(difficulty);
        if (!target.MatchSetup) throw new InvalidOperationException("目标候选没有开局配置适配层");
        Validate(target);
        var current = Identity;
        if(target.RuntimeIntegration && IsPlayableSubstrate())
        {
            await ExecuteCoreAsync("Preview",difficulty);
            using var baseline=JsonDocument.Parse(File.ReadAllText(SafePath(CandidatePath(target),"experiment-manifest.json")));
            return new(current.InstallId,difficulty,target.ManifestSha256,false,"保留当前已验证的三层安装与备份，安装可独立撤回的整合组件；恢复时回到当前可玩基线",true,
                RuntimeIntegrationGuard.RestoredHumanHandoff(Cs2Root,BackupRoot,baseline.RootElement));
        }
        if (!current.BlocksHuman)
        {
            await ExecuteCoreAsync("Preview", difficulty);
            return new(null, difficulty, target.ManifestSha256, false, "当前无活动增强安装；目标候选完整预览通过，待确认安装");
        }
        if (current.InstallId is null || current.RecoveryRequired || !current.PreservesHuman)
            throw new InvalidOperationException(current.Reason);
        var installed = InspectInstalled();
        if (!installed.Valid) throw new InvalidOperationException(installed.Reason);
        await ExecuteWithHumanUpdateAsync("Diagnostics", difficulty, false, current.InstallId);
        using var activeRecord = JsonDocument.Parse(File.ReadAllText(SafePath(BackupRoot, current.InstallId + "/install-record.json")));
        if (installed.Difficulty == difficulty && installed.HasMatchSetup &&
            activeRecord.RootElement.GetProperty("manifestSha256").GetString() == target.ManifestSha256)
            return new(current.InstallId, difficulty, target.ManifestSha256, true, "当前增强安装与所选难度匹配；无需重复安装");

        // The target's full live preflight is impossible while the old package owns overlapping paths.
        // Compare immutable restored preimages now; Install still performs a fresh full preflight after Restore.
        using var old = JsonDocument.Parse(File.ReadAllText(SafePath(BackupRoot, current.InstallId + "/candidate-manifest.json")));
        using var next = JsonDocument.Parse(File.ReadAllText(SafePath(CandidatePath(target), "experiment-manifest.json")));
        foreach (var key in new[] { "targetBuild", "baselineFiles", "gameBinaries", "moduleInventory" })
            if (old.RootElement.GetProperty(key).GetRawText() != next.RootElement.GetProperty(key).GetRawText())
                throw new InvalidDataException("旧事务恢复基线与目标候选不一致：" + key);
        if (old.RootElement.GetProperty("coexistence").GetProperty("humanFiles").GetRawText() !=
            next.RootElement.GetProperty("coexistence").GetProperty("humanFiles").GetRawText())
            throw new InvalidDataException("旧事务与目标候选的 Human 文件身份不一致");
        return new(current.InstallId, difficulty, target.ManifestSha256, false,
            "原事务精确恢复预检通过；目标与恢复后的固定基线一致。确认后先恢复，重新完整预检，再安装目标候选");
    }

    public async Task<BotPrepareJournal> ApplySwitchAsync(BotSwitchPlan approved)
    {
        if (!await _operation.WaitAsync(0)) throw new InvalidOperationException("另一个 Bot 操作正在执行");
        try
        {
            using var gameWrite = BotHumanOperationGate.Acquire(Cs2Root);
            var previous = ReadPrepareJournal();
            if (previous is not null && previous.Stage != "completed" && !PreviousPrepareSettled(previous))
                throw new InvalidOperationException("上次准备操作未完成；保留现场并检查记录，不自动重试");
            var fresh = await PreviewSwitchCoreAsync(approved.TargetDifficulty);
            if (fresh.OldInstallId != approved.OldInstallId || fresh.TargetManifestSha256 != approved.TargetManifestSha256 || fresh.AlreadyPrepared != approved.AlreadyPrepared || fresh.ProtectsExistingLayers != approved.ProtectsExistingLayers || fresh.HumanHandoffInstallId != approved.HumanHandoffInstallId)
                throw new InvalidOperationException("准备预览后安装身份变化，请重新检查并确认");
            if (fresh.AlreadyPrepared) return new(Guid.NewGuid().ToString("N"), fresh.OldInstallId,
                fresh.TargetDifficulty, fresh.TargetManifestSha256, "completed", fresh.OldInstallId);
            var journal = new BotPrepareJournal(Guid.NewGuid().ToString("N"), fresh.OldInstallId,
                fresh.TargetDifficulty, fresh.TargetManifestSha256, "previewed");
            WritePrepareJournal(journal);
            BotHumanFixtureSidecar? handoff = null;
            try
            {
                if(fresh.HumanHandoffInstallId is not null)
                {
                    handoff=new BotHumanFixtureSidecar(Cs2Root,BackupRoot,fresh.HumanHandoffInstallId);
                    handoff.PrepareRestoredTransfer();
                }
                if (fresh.OldInstallId is not null && !fresh.ProtectsExistingLayers)
                {
                    journal = journal with { Stage = "restoring" }; WritePrepareJournal(journal);
                    if (BotHumanFixtureSidecar.Exists(BackupRoot, fresh.OldInstallId))
                    {
                        handoff = new BotHumanFixtureSidecar(Cs2Root, BackupRoot, fresh.OldInstallId);
                        handoff.PreviewExactRestore();
                        handoff.SuspendForExactRestore();
                        try
                        {
                            await ExecuteCoreAsync("Restore", fresh.TargetDifficulty, true, fresh.OldInstallId);
                            handoff.FinishExactRestore(replayHuman: false);
                        }
                        catch
                        {
                            if (ReadRecordState(fresh.OldInstallId) == "installed" && Identity.InstallId == fresh.OldInstallId)
                                handoff.AbortSuspendedRestore();
                            else if (ReadRecordState(fresh.OldInstallId) == "restored" && (!Identity.BlocksHuman || RuntimeIntegrationGuard.IsRestoredSubstrate(Cs2Root,BackupRoot,fresh.OldInstallId)))
                                handoff.FinishExactRestore(replayHuman: false);
                            throw;
                        }
                    }
                    else await ExecuteCoreAsync("Restore", fresh.TargetDifficulty, true, fresh.OldInstallId);
                    journal = journal with { Stage = "restored" }; WritePrepareJournal(journal);
                }
                journal = journal with { Stage = "installing" }; WritePrepareJournal(journal);
                var installed = await ExecuteCoreAsync("Install", fresh.TargetDifficulty, true);
                var newId = installed.GetProperty("installId").GetString();
                if (handoff is not null)
                {
                    if (newId is null) throw new InvalidDataException("新事务缺少 install ID，不能移交 Human 配置");
                    handoff.TransferTo(new BotHumanFixtureSidecar(Cs2Root, BackupRoot, newId));
                }
                var snapshot = InspectInstalled();
                if (!snapshot.Valid || !snapshot.HasMatchSetup || snapshot.Difficulty != fresh.TargetDifficulty || Identity.InstallId != newId)
                    throw new InvalidDataException("新安装事务结束后身份仍无法确认；保留现场");
                journal = journal with { Stage = "completed", NewInstallId = newId }; WritePrepareJournal(journal);
                return journal;
            }
            catch (Exception ex)
            {
                var reason = ex.Message;
                if (handoff is not null && (!Identity.BlocksHuman || IsPlayableSubstrate()))
                {
                    try { handoff.ReplayAfterFailedSwitch(); }
                    catch (Exception recovery) { reason += "；Human 配置仍保存在旧事务侧车：" + recovery.Message; }
                }
                WritePrepareJournal(journal with { Stage = "blocked", Error = reason });
                throw;
            }
        }
        finally { _operation.Release(); }
    }
    private bool PreviousPrepareSettled(BotPrepareJournal previous)
    {
        if(IsPlayableSubstrate())return true;
        // This only permits a new user-confirmed plan. It never resumes or retries an unknown write.
        if (previous.OldInstallId is not null && Identity.InstallId == previous.OldInstallId && InspectInstalled().Valid)
            return true;
        if (previous.OldInstallId is null && !Identity.BlocksHuman && HumanBaseline().Valid)
            return true;
        if (!Identity.BlocksHuman && previous.OldInstallId is not null &&
            ReadRecordState(previous.OldInstallId) == "restored" && HumanBaseline().Valid)
        {
            var update = SafePath(BackupRoot, previous.OldInstallId + "/human-fixture-update/update.json");
            if (!File.Exists(update)) return true;
            using var doc = JsonDocument.Parse(File.ReadAllText(update));
            return doc.RootElement.GetProperty("Stage").GetString() == "completed";
        }
        return false;
    }
    private string? ReadRecordState(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SafePath(BackupRoot, id + "/install-record.json")));
        return doc.RootElement.GetProperty("state").GetString();
    }
}
