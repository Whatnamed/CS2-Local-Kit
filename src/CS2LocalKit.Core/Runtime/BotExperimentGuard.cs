using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Observation only. Installation/restoration remain owned by the experimental transaction.</summary>
public sealed record BotExperimentIdentity(bool BlocksHuman, string Reason, string? InstallId = null, string? BackupRoot = null,
    bool PreservesHuman = false, string? State = null, bool RecoveryRequired = false)
{
    public static BotExperimentIdentity Inspect(string cs2Root, string? backupRoot = null)
    {
        backupRoot ??= Path.Combine(CorePaths.Cs2ModRoot, "backups", "bot-experiment");
        try
        {
            BotExperimentIdentity FromRecord(string id, bool missingMarker, JsonElement? markerData = null)
            {
                var dir = Path.Combine(backupRoot, id);
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "install-record.json")));
                var r = doc.RootElement;
                if (r.GetProperty("installId").GetString() != id || !Path.GetFullPath(r.GetProperty("cs2Root").GetString()!).Equals(Path.GetFullPath(cs2Root), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("实验记录的 root/ID 不匹配");
                var state = r.GetProperty("state").GetString();
                var preserve = r.TryGetProperty("humanPolicy", out var policy) && policy.GetString() == "preserve";
                if (markerData is { } m && (m.GetProperty("manifestSha256").GetString() != r.GetProperty("manifestSha256").GetString()
                    || (m.TryGetProperty("humanPolicy", out var p) && p.GetString() == "preserve") != preserve))
                    throw new InvalidDataException("marker 与事务身份不一致");
                var recovery = missingMarker || state != "installed";
                return new(true, recovery ? "事务未完整结束；请按精确 ID 检查恢复条件" : preserve
                    ? "增强人机 + 我的饰品；Human 修改须经活动事务与侧车校验"
                    : "旧 Bot 实验隔离 Human；请先按精确 ID 恢复", id, backupRoot, preserve, state, recovery);
            }
            var integratedMarker = Path.Combine(cs2Root, "game", "csgo", ".cs2-local-kit-runtime-integration.json");
            var marker = File.Exists(integratedMarker) ? integratedMarker : Path.Combine(cs2Root, "game", "csgo", ".cs2-local-kit-bot-experiment.json");
            // A missing integration marker must not silently expose its still-active parent.
            if (!File.Exists(integratedMarker) && Directory.Exists(backupRoot))
                foreach (var folder in Directory.EnumerateDirectories(backupRoot))
                {
                    var manifest = Path.Combine(folder,"candidate-manifest.json");
                    var record = Path.Combine(folder,"install-record.json");
                    if (!File.Exists(manifest) || !File.Exists(record)) continue;
                    using var m = JsonDocument.Parse(File.ReadAllText(manifest));
                    using var r = JsonDocument.Parse(File.ReadAllText(record));
                    if (m.RootElement.TryGetProperty("runtimeIntegration",out _) &&
                        r.RootElement.GetProperty("cs2Root").GetString() == Path.GetFullPath(cs2Root) &&
                        r.RootElement.GetProperty("state").GetString() is not ("restored" or "rolled-back"))
                        return FromRecord(Path.GetFileName(folder),true);
                }
            if (File.Exists(marker))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                var id = doc.RootElement.GetProperty("installId").GetString()!;
                if (!Regex.IsMatch(id, "^[0-9a-f]{32}$")) throw new InvalidDataException("Invalid install ID");
                var declaredRoot = doc.RootElement.GetProperty("backupRoot").GetString()!;
                if (!Path.GetFullPath(declaredRoot).Equals(Path.GetFullPath(backupRoot), StringComparison.OrdinalIgnoreCase))
                    return new(true, "实验 marker 的备份位置不匹配；保留记录并检查人机页", RecoveryRequired: true);
                return FromRecord(id, false, doc.RootElement);
            }
            var active = new List<string>();
            if (Directory.Exists(backupRoot))
                foreach (var dir in Directory.EnumerateDirectories(backupRoot))
                {
                    var file = Path.Combine(dir, "install-record.json");
                    if (!File.Exists(file)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var r = doc.RootElement;
                    if (!Path.GetFullPath(r.GetProperty("cs2Root").GetString()!).Equals(Path.GetFullPath(cs2Root), StringComparison.OrdinalIgnoreCase)) continue;
                    if (r.GetProperty("state").GetString() is "restored" or "rolled-back") continue;
                    var id = r.GetProperty("installId").GetString()!;
                    if (!Regex.IsMatch(id, "^[0-9a-f]{32}$") || Path.GetFileName(dir) != id) throw new InvalidDataException("Invalid history ID");
                    active.Add(id);
                }
            return active.Count switch
            {
                0 => new(false, ""),
                1 => FromRecord(active[0], true),
                _ => new(true, "存在多个未恢复实验事务，禁止猜测最近备份", RecoveryRequired: true)
            };
        }
        catch (Exception ex) { return new(true, "实验记录无法验证，阻止 Human 写入：" + ex.Message, RecoveryRequired: true); }
    }
}
