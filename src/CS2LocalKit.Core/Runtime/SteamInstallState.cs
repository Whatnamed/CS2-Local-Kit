using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Observation-only update gate shared by mode preview and execution.</summary>
public sealed record SteamInstallState(bool Idle, string Reason, string BuildId = "", string PatchVersion = "", string ClientVersion = "")
{
    public string BuildText => string.IsNullOrEmpty(BuildId) ? "未读取" : $"{PatchVersion} / {ClientVersion} / {BuildId}";
    public static SteamInstallState Inspect(string cs2Root)
    {
        try
        {
            var text = File.ReadAllText(Path.GetFullPath(Path.Combine(cs2Root, "../..", "appmanifest_730.acf")));
            string? Value(string key, bool required = false)
            {
                var matches = Regex.Matches(text, "\"" + Regex.Escape(key) + "\"\\s+\"([^\"\\r\\n]*)\"");
                if (matches.Count > 1 || required && matches.Count != 1) throw new InvalidDataException("Steam 状态缺失或有歧义：" + key);
                return matches.Count == 1 ? matches[0].Groups[1].Value : null;
            }
            var build = Value("buildid", true)!;
            var inf = File.ReadAllLines(Path.Combine(cs2Root, "game/csgo/steam.inf"));
            string Inf(string key) => inf.SingleOrDefault(s => s.StartsWith(key + "="))?[(key.Length + 1)..].Trim() ?? "";
            var patch = Inf("PatchVersion"); var client = Inf("ClientVersion");
            string? reason = Value("StateFlags", true) != "4" ? "Steam 正在更新/安装或状态不可确认" : null;
            var target = Value("TargetBuildID");
            if (target is not (null or "" or "0") && target != build) reason = "Steam 目标 build 正在变化";
            foreach (var (total, done) in new[] { ("BytesToDownload", "BytesDownloaded"), ("BytesToStage", "BytesStaged") })
                if (Value(total) != Value(done)) reason = "Steam 下载/暂存尚未完成";
            return new(reason is null, reason ?? "Steam 安装状态稳定", build, patch, client);
        }
        catch (Exception ex) { return new(false, "无法确认 Steam/CS2 更新状态：" + ex.Message); }
    }
}
