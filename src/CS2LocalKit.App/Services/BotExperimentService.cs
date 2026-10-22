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
public sealed record BotCandidate(BotDifficulty Difficulty, string Directory, string ManifestSha256, bool PreservesHuman = false, bool MatchSetup = false);
public sealed record BotModeReadiness(bool CanEnable, string Reason, string CurrentBuild, string CandidateBuild, string BotRef, bool HumanFilesValid);
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
    public bool Cs2Running => _running();
    public SteamInstallState SteamState => SteamInstallState.Inspect(Cs2Root);
    public BotExperimentIdentity Identity => BotExperimentIdentity.Inspect(Cs2Root, BackupRoot);
    public IReadOnlyList<BotCandidate> Candidates => _candidates;

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

    public BotCandidate Candidate(BotDifficulty difficulty) => _candidates.Where(c => c.Difficulty == difficulty).OrderByDescending(c => c.PreservesHuman).ThenByDescending(c => c.MatchSetup).FirstOrDefault() ?? throw new InvalidOperationException("该难度没有已固定候选，禁止安装");
    private BotCandidate JointCandidate(BotDifficulty difficulty) => _candidates.Where(c => c.Difficulty == difficulty && c.PreservesHuman).OrderByDescending(c => c.MatchSetup).FirstOrDefault()
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

    public BotModeReadiness Readiness(BotDifficulty difficulty)
    {
        var steam = SteamState; var human = HumanBaseline();
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
            if (!human.Valid) throw new InvalidOperationException(human.Reason);
            if (build != steam.BuildText) throw new InvalidOperationException($"候选 build 已过期（{build}）；禁止套用到当前 build");
            if (!candidate.PreservesHuman) throw new InvalidOperationException("现有候选会隔离 Human，不能用于“增强人机 + 我的饰品”");
            if (m.GetProperty("humanPolicy").GetString() != "preserve" || m.GetProperty("coexistence").GetProperty("status").GetString() != "ready-for-manual-test")
                throw new InvalidDataException("联合兼容性门禁尚未通过");
            return new(true, "联合候选可检查切换预览；实机效果尚未验收", steam.BuildText, build, botRef, human.Valid);
        }
        catch (Exception ex) { return new(false, ex.Message, steam.BuildText, build, botRef, human.Valid); }
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
            if (apply && action == "Restore" && (Identity.BlocksHuman || ReadRecordState(installId!) != "restored"))
                throw new InvalidDataException("恢复入口结束，但原环境状态未确认；保留精确备份");
            return result;
        }
        finally { _operation.Release(); }
    }
    private string? ReadRecordState(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SafePath(BackupRoot, id + "/install-record.json")));
        return doc.RootElement.GetProperty("state").GetString();
    }
}
