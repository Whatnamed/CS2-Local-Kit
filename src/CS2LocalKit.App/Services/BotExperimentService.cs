using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.Services;

public enum BotDifficulty { Low, Medium, High }
public enum BotAim { Mixed, Head, Body }
public enum BotNades { Off, Less, Normal, More, Max }
public sealed record BotPreferences(BotDifficulty Difficulty = BotDifficulty.Medium, BotAim Aim = BotAim.Mixed, BotNades Nades = BotNades.Normal, string? LastInstallId = null)
{
    public string ConsoleCommands => $"bot_aim {Aim.ToString().ToLowerInvariant()}; bot_nades {Nades.ToString().ToLowerInvariant()}";
}
public sealed record BotCandidate(BotDifficulty Difficulty, string Directory, string ManifestSha256);
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
    public string Cs2Root { get; }
    public string ReleasesRoot { get; }
    public string BackupRoot { get; }
    public string LogsRoot { get; }
    public string PreferencesPath { get; }
    public bool Cs2Running => _running();
    public BotExperimentIdentity Identity => BotExperimentIdentity.Inspect(Cs2Root, BackupRoot);
    public IReadOnlyList<BotCandidate> Candidates => _candidates;

    public BotExperimentService(string dataRoot, string cs2Root, Func<bool>? running = null, IBotProcessRunner? runner = null, IReadOnlyList<BotCandidate>? candidates = null)
    {
        Cs2Root = Path.GetFullPath(cs2Root);
        ReleasesRoot = Path.Combine(dataRoot, "releases", "bot-experiment");
        BackupRoot = Path.Combine(dataRoot, "backups", "bot-experiment");
        LogsRoot = Path.Combine(dataRoot, "diagnostics", "bot-experiment");
        PreferencesPath = Path.Combine(dataRoot, "app-data", "bot-experiment", "preferences.json");
        _runner = runner ?? new BotProcessRunner(); _running = running ?? Cs2Locator.IsCs2Running;
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
        if (!Enum.IsDefined(p.Difficulty) || !Enum.IsDefined(p.Aim) || !Enum.IsDefined(p.Nades) || p.LastInstallId is not null && !Regex.IsMatch(p.LastInstallId, "^[0-9a-f]{32}$")) throw new InvalidDataException("Bot 偏好值无效");
        return p;
    }

    public void SavePreferences(BotPreferences value)
    {
        if (!Enum.IsDefined(value.Difficulty) || !Enum.IsDefined(value.Aim) || !Enum.IsDefined(value.Nades)) throw new InvalidDataException("未知 Bot 选项");
        CheckNoLinks(PreferencesPath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        var tmp = PreferencesPath + ".new";
        CheckNoLinks(tmp);
        File.WriteAllText(tmp, JsonSerializer.Serialize(value));
        File.Move(tmp, PreferencesPath, true);
    }

    public BotCandidate Candidate(BotDifficulty difficulty) => _candidates.SingleOrDefault(c => c.Difficulty == difficulty) ?? throw new InvalidOperationException("该难度没有已固定候选，禁止安装");
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
            BotCandidate candidate;
            if (action is "Restore" or "Diagnostics")
            {
                if (!Regex.IsMatch(installId ?? "", "^[0-9a-f]{32}$")) throw new InvalidDataException("必须指定精确 install ID");
                var record = SafePath(BackupRoot, installId + "/install-record.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(record)); var r = doc.RootElement;
                if (r.GetProperty("installId").GetString() != installId || !Path.GetFullPath(r.GetProperty("cs2Root").GetString()!).Equals(Cs2Root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装记录目标/ID 不匹配");
                candidate = _candidates.SingleOrDefault(c => c.ManifestSha256 == r.GetProperty("manifestSha256").GetString()) ?? throw new InvalidDataException("安装记录不属于固定候选；保留备份并使用原候选恢复入口");
            }
            else candidate = Candidate(difficulty);
            await Task.Run(() => Validate(candidate));
            var python = await _runner.RunAsync("python", ["--version"]);
            var match = Regex.Match(python.Stdout + python.Stderr, @"Python (\d+)\.(\d+)");
            if (python.ExitCode != 0 || !match.Success || new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) < new Version(3, 12)) throw new InvalidOperationException("需要已有 Python 3.12+，未安装任何依赖");
            async Task<JsonElement> Invoke(bool write)
            {
                var args = new List<string> { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", SafePath(CandidatePath(candidate), "tools/Invoke-BotExperiment.ps1"), "-Action", action, "-BackupRoot", BackupRoot, "-PythonExe", "python" };
                if (action is "Preview" or "Install") args.AddRange(["-ReleaseDir", CandidatePath(candidate), "-ManifestSha256", candidate.ManifestSha256, "-Cs2Root", Cs2Root, "-IsolateHumanCosmetics"]);
                else args.AddRange(["-InstallId", installId!]);
                if (write) args.Add("-Apply");
                var result = await _runner.RunAsync("pwsh", args);
                CheckNoLinks(LogsRoot); System.IO.Directory.CreateDirectory(LogsRoot);
                File.WriteAllText(Path.Combine(LogsRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json"), JsonSerializer.Serialize(new { action, apply = write, candidate.ManifestSha256, installId, result.ExitCode, result.Stdout, result.Stderr }));
                if (result.ExitCode != 0) throw new InvalidOperationException($"实验入口拒绝/失败 ({result.ExitCode})：\n{result.Stderr}\n{result.Stdout}");
                using var doc = JsonDocument.Parse(result.Stdout);
                return doc.RootElement.Clone();
            }
            // Both mutations always get a fresh full preview in the same serialized operation.
            if (apply) await Invoke(false);
            return await Invoke(apply);
        }
        finally { _operation.Release(); }
    }
}
