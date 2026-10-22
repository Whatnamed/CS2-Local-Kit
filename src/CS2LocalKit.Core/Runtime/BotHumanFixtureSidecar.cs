using System.Security.Cryptography;
using System.Text.Json;

namespace CS2LocalKit.Core.Runtime;

public sealed record HumanFixtureUpdate(string InstallId, string ManifestSha256, string BaselineSha256,
    string CurrentSha256, string Stage, string? NextSha256 = null);

/// <summary>
/// Authorizes one user-owned Human fixture to change while a preserve-Human Bot transaction is active.
/// The original manifest, record, and preimages remain immutable. The bundled old Restore tool is used
/// only after the fixture has been returned to its recorded baseline.
/// </summary>
public sealed class BotHumanFixtureSidecar
{
    public const string Target = "game/csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator/inventories.json";
    private readonly string _root, _backup, _folder, _target, _metadata, _baseline, _current, _next, _previous;
    private readonly string _installId;

    public BotHumanFixtureSidecar(string cs2Root, string backupRoot, string installId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(installId, "^[0-9a-f]{32}$")) throw new InvalidDataException("Human 更新需要精确 install ID");
        _root = Path.GetFullPath(cs2Root);
        _backup = Safe(Path.GetFullPath(backupRoot), installId);
        _folder = Safe(_backup, "human-fixture-update");
        _target = Safe(_root, Target);
        _metadata = Safe(_folder, "update.json");
        _baseline = Safe(_folder, "baseline.json");
        _current = Safe(_folder, "current.json");
        _next = Safe(_folder, "next.json");
        _previous = Safe(_folder, "previous.json");
        _installId = installId;
    }

    public static bool Exists(string backupRoot, string installId) =>
        File.Exists(Safe(Path.GetFullPath(backupRoot), installId + "/human-fixture-update/update.json"));

    private static string Safe(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException("Human 更新路径无效");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Human 更新路径越界");
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Human 更新拒绝链接路径：" + p);
        return path;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private HumanFixtureUpdate? Read()
    {
        if (!File.Exists(_metadata))
        {
            if (Directory.Exists(_folder)) throw new InvalidDataException("Human 更新记录不完整，保留现场");
            return null;
        }
        return JsonSerializer.Deserialize<HumanFixtureUpdate>(File.ReadAllText(_metadata))
            ?? throw new InvalidDataException("Human 更新记录为空");
    }
    private void Write(HumanFixtureUpdate value)
    {
        Directory.CreateDirectory(_folder);
        var temp = Safe(_folder, "update.new");
        File.WriteAllText(temp, JsonSerializer.Serialize(value));
        File.Move(temp, _metadata, true);
    }

    /// <summary>Complete read-only integrity check, with the fixture as the sole permitted postimage exception.</summary>
    public HumanFixtureUpdate? Inspect(bool allowSuspended = false)
    {
        var identity = BotExperimentIdentity.Inspect(_root, Path.GetDirectoryName(_backup));
        if (identity.InstallId != _installId || identity.State != "installed" || !identity.PreservesHuman || identity.RecoveryRequired)
            throw new InvalidDataException("活动 Bot 事务身份不允许 Human 更新");
        using var record = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "install-record.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "candidate-manifest.json")));
        var r = record.RootElement; var m = manifest.RootElement;
        var manifestHash = Hash(Safe(_backup, "candidate-manifest.json"));
        if (r.GetProperty("manifestSha256").GetString() != manifestHash ||
            m.GetProperty("humanPolicy").GetString() != "preserve" ||
            r.GetProperty("state").GetString() != "installed")
            throw new InvalidDataException("Bot manifest/record 身份漂移");
        var steam = SteamInstallState.Inspect(_root);
        var build = m.GetProperty("targetBuild");
        if (!steam.Idle || steam.BuildId != build.GetProperty("buildId").GetString() ||
            steam.PatchVersion != build.GetProperty("PatchVersion").GetString() ||
            steam.ClientVersion != build.GetProperty("ClientVersion").GetString())
            throw new InvalidDataException("Steam/CS2 build 与联合候选不匹配，保留 Human 数据");
        var baselineHash = m.GetProperty("coexistence").GetProperty("humanFiles").GetProperty(Target).GetString()!;
        var entry = r.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("target").GetString() == Target);
        if (entry.GetProperty("action").GetString() != "retained" ||
            entry.GetProperty("before").GetProperty("sha256").GetString() != baselineHash ||
            entry.GetProperty("after").GetProperty("sha256").GetString() != baselineHash)
            throw new InvalidDataException("Human fixture 不属于保留的用户文件");
        var update = Read();
        if (update is not null && (update.InstallId != _installId || update.ManifestSha256 != manifestHash || update.BaselineSha256 != baselineHash ||
            Hash(_baseline) != baselineHash || Hash(_current) != update.CurrentSha256 ||
            update.Stage is not ("ready" or "suspended") || update.Stage == "suspended" && !allowSuspended ||
            update.Stage == "ready" && File.Exists(_next)))
            throw new InvalidDataException("Human 更新侧车记录未完成或身份漂移，保留现场");
        var expected = update is { Stage: "ready" } ? update.CurrentSha256 : baselineHash;
        if (Hash(_target) != expected) throw new InvalidDataException("Human fixture 与活动事务/侧车记录不匹配");
        foreach (var item in r.GetProperty("entries").EnumerateArray())
        {
            var rel = item.GetProperty("target").GetString()!;
            if (rel == Target) continue;
            var path = Safe(_root, rel);
            var after = item.GetProperty("after");
            if (after.ValueKind == JsonValueKind.Null)
            {
                if (File.Exists(path) || Directory.Exists(path)) throw new InvalidDataException("隔离模块重新出现：" + rel);
            }
            else if (after.GetProperty("kind").GetString() != "file" || Hash(path) != after.GetProperty("sha256").GetString())
                throw new InvalidDataException("活动事务文件漂移：" + rel);
        }
        foreach (var file in m.GetProperty("coexistence").GetProperty("humanFiles").EnumerateObject())
            if (file.Name != Target && Hash(Safe(_root, file.Name)) != file.Value.GetString())
                throw new InvalidDataException("Human 共存文件漂移：" + file.Name);
        return update;
    }

    public void Begin(byte[] projected)
    {
        var existing = Inspect();
        var baselineHash = existing?.BaselineSha256;
        if (existing is null)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "candidate-manifest.json")));
            baselineHash = manifest.RootElement.GetProperty("coexistence").GetProperty("humanFiles").GetProperty(Target).GetString()!;
            Directory.CreateDirectory(_folder);
            File.Copy(_target, _baseline, false);
            File.Copy(_target, _current, false);
            var manifestHash = Hash(Safe(_backup, "candidate-manifest.json"));
            existing = new(_installId, manifestHash, baselineHash, baselineHash, "ready");
            Write(existing);
        }
        var nextHash = Convert.ToHexStringLower(SHA256.HashData(projected));
        File.Copy(_current, _previous, true);
        if (Hash(_previous) != existing.CurrentSha256) throw new IOException("Human 更新前映像校验失败");
        Write(existing with { Stage = "updating", NextSha256 = nextHash });
        File.WriteAllBytes(_next, projected);
        if (Hash(_next) != nextHash) throw new IOException("Human 更新 staging 校验失败");
    }

    public void Complete()
    {
        var value = Read() ?? throw new InvalidDataException("缺少 Human 更新记录");
        if (value.Stage != "updating" || value.NextSha256 is null || Hash(_target) != value.NextSha256 || Hash(_next) != value.NextSha256)
            throw new InvalidDataException("Human 更新后映像未验证");
        File.Copy(_next, _current, true);
        if (Hash(_current) != value.NextSha256) throw new IOException("Human 当前副本校验失败");
        Write(value with { CurrentSha256 = value.NextSha256, Stage = "ready", NextSha256 = null });
        File.Delete(_next);
        File.Delete(_previous);
    }

    public void Abort()
    {
        var value = Read();
        if (value is null || value.Stage != "updating") return;
        if (Hash(_current) != value.CurrentSha256)
        {
            if (!File.Exists(_previous) || Hash(_previous) != value.CurrentSha256) throw new InvalidDataException("Human 更新旧副本漂移，保留现场");
            File.Copy(_previous, _current, true);
        }
        File.Copy(_current, _target, true);
        if (Hash(_target) != value.CurrentSha256) throw new IOException("Human 更新回退未验证，保留现场");
        Write(value with { Stage = "ready", NextSha256 = null });
        if (File.Exists(_next)) File.Delete(_next);
        if (File.Exists(_previous)) File.Delete(_previous);
    }

    public void SuspendForExactRestore()
    {
        var value = Inspect() ?? throw new InvalidDataException("没有 Human 更新侧车");
        if (value.Stage != "ready") throw new InvalidDataException("Human 更新仍在执行");
        var temp = Safe(Path.GetDirectoryName(_target)!, "inventories.json.localkit-next");
        File.Copy(_baseline, temp, true);
        if (Hash(temp) != value.BaselineSha256) throw new IOException("Human 基线副本漂移");
        Write(value with { Stage = "suspended" });
        File.Move(temp, _target, true);
        if (Hash(_target) != value.BaselineSha256) throw new IOException("Human 基线挂起未验证，保留现场");
    }

    public int PreviewExactRestore()
    {
        Inspect();
        using var record = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "install-record.json")));
        var entries = record.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        var targets = entries.Select(e => e.GetProperty("target").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry.GetProperty("action").GetString() == "retained") continue;
            var before = entry.GetProperty("before");
            if (before.ValueKind == JsonValueKind.Null) continue;
            var rel = entry.GetProperty("target").GetString()!;
            var backup = entry.GetProperty("action").GetString() == "isolated"
                ? Safe(_backup, "isolated/" + rel) : Safe(_backup, "preimages/" + rel);
            if (before.GetProperty("kind").GetString() == "file")
            {
                if (Hash(backup) != before.GetProperty("sha256").GetString())
                    throw new InvalidDataException("精确恢复备份文件缺失/漂移：" + rel);
            }
            else if (!Directory.Exists(backup)) throw new InvalidDataException("精确恢复隔离目录缺失：" + rel);
        }
        foreach (var item in record.RootElement.GetProperty("createdDirectories").EnumerateArray())
        {
            var dir = Safe(_root, item.GetString()!);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                if (!targets.Contains(Path.GetRelativePath(_root, file).Replace('\\', '/')))
                    throw new InvalidDataException("活动目录出现未知文件：" + file);
        }
        return entries.Length;
    }

    public void AbortSuspendedRestore()
    {
        var value = Read() ?? throw new InvalidDataException("没有 Human 更新侧车");
        if (value.Stage != "suspended" || Hash(_target) != value.BaselineSha256)
            throw new InvalidDataException("Human 挂起状态漂移，保留现场");
        using var record = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "install-record.json")));
        if (record.RootElement.GetProperty("state").GetString() != "installed" ||
            BotExperimentIdentity.Inspect(_root, Path.GetDirectoryName(_backup)).InstallId != _installId)
            throw new InvalidDataException("Bot 事务已变，不能撤销 Human 挂起");
        File.Copy(_current, _target, true);
        if (Hash(_target) != value.CurrentSha256) throw new IOException("Human 挂起撤销未验证");
        Write(value with { Stage = "ready" });
    }

    public void FinishExactRestore(bool replayHuman = true)
    {
        var value = Read() ?? throw new InvalidDataException("没有 Human 更新侧车");
        if (value.Stage != "suspended" || Hash(_target) != value.BaselineSha256)
            throw new InvalidDataException("原事务恢复后 Human 基线未验证，保留当前记录");
        using var record = JsonDocument.Parse(File.ReadAllText(Safe(_backup, "install-record.json")));
        if (record.RootElement.GetProperty("state").GetString() != "restored" || BotExperimentIdentity.Inspect(_root, Path.GetDirectoryName(_backup)).BlocksHuman)
            throw new InvalidDataException("Bot 原事务尚未精确恢复，不能重放 Human 配置");
        if (!replayHuman) { Write(value with { Stage = "transferring" }); return; }
        File.Copy(_current, _target, true);
        if (Hash(_target) != value.CurrentSha256) throw new IOException("Human 配置重放未验证，保留现场");
        Write(value with { Stage = "completed" });
    }

    public void ReplayAfterFailedSwitch()
    {
        var value = Read() ?? throw new InvalidDataException("没有 Human 更新侧车");
        if (value.Stage != "transferring" || Hash(_target) != value.BaselineSha256 ||
            BotExperimentIdentity.Inspect(_root, Path.GetDirectoryName(_backup)).BlocksHuman)
            throw new InvalidDataException("组合安装未回到可验证基线，保留 Human 副本等待精确恢复");
        File.Copy(_current, _target, true);
        if (Hash(_target) != value.CurrentSha256) throw new IOException("Human 配置重放未验证");
        Write(value with { Stage = "completed" });
    }

    public void TransferTo(BotHumanFixtureSidecar next)
    {
        var value = Read() ?? throw new InvalidDataException("没有旧 Human 更新侧车");
        if (value.Stage != "transferring" || Hash(_current) != value.CurrentSha256 ||
            !string.Equals(_root, next._root, StringComparison.OrdinalIgnoreCase) ||
            Hash(_target) != value.BaselineSha256)
            throw new InvalidDataException("旧 Human 配置无法安全移交新事务");
        next.Begin(File.ReadAllBytes(_current));
        var temp = Safe(Path.GetDirectoryName(_target)!, "inventories.json.localkit-next");
        File.Copy(_current, temp, true);
        if (Hash(temp) != value.CurrentSha256) throw new IOException("Human 移交 staging 漂移");
        File.Move(temp, _target, true);
        next.Complete();
        Write(value with { Stage = "transferred" });
    }
}
