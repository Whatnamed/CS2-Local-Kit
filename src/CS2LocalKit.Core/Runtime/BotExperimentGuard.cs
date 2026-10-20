using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Observation only. Installation/restoration remain owned by the experimental transaction.</summary>
public sealed record BotExperimentIdentity(bool BlocksHuman, string Reason, string? InstallId = null, string? BackupRoot = null)
{
    public static BotExperimentIdentity Inspect(string cs2Root, string? backupRoot = null)
    {
        backupRoot ??= Path.Combine(CorePaths.Cs2ModRoot, "backups", "bot-experiment");
        try
        {
            var marker = Path.Combine(cs2Root, "game", "csgo", ".cs2-local-kit-bot-experiment.json");
            if (File.Exists(marker))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(marker));
                var id = doc.RootElement.GetProperty("installId").GetString()!;
                if (!Regex.IsMatch(id, "^[0-9a-f]{32}$")) throw new InvalidDataException("Invalid install ID");
                var declaredRoot = doc.RootElement.GetProperty("backupRoot").GetString()!;
                if (!Path.GetFullPath(declaredRoot).Equals(Path.GetFullPath(backupRoot), StringComparison.OrdinalIgnoreCase))
                    return new(true, "实验 marker 的备份位置不匹配；保留记录并检查人机页");
                return new(true, "Bot 实验占用：Human 暂时隔离，请先在人机页按精确 ID 恢复", id, backupRoot);
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
                1 => new(true, "实验事务未恢复（marker 缺失/中断）；请按精确 ID 检查恢复条件", active[0], backupRoot),
                _ => new(true, "存在多个未恢复实验事务，禁止猜测最近备份")
            };
        }
        catch (Exception ex) { return new(true, "实验记录无法验证，阻止 Human 写入：" + ex.Message); }
    }
}
