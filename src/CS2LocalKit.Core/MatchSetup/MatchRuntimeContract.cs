using System.Security.Cryptography;
using System.Text.Json;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.Core.MatchSetup;

public sealed record MatchInstallation(string InstallId, string ManifestSha256, InstalledProfile Profile);
public static class MatchRuntimeContract
{
    public static MatchInstallation Validate(string cs2Root, string dataRoot)
    {
        static string Hash(string path) { MatchSetupStore.CheckPath(path); using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream)); }
        string Safe(string root,string rel)
        {
            if (string.IsNullOrWhiteSpace(rel) || rel.Contains('\\') || rel.Contains(':') || rel.Split('/').Any(p=>p is "" or "." or "..")) throw new InvalidDataException("安装合约路径无效");
            var path=Path.GetFullPath(Path.Combine(root,rel));MatchSetupStore.CheckPath(path);return path;
        }
        var markerPath=Safe(cs2Root,"game/csgo/.cs2-local-kit-bot-experiment.json");
        var markerHash=Hash(markerPath);
        using var marker=JsonDocument.Parse(File.ReadAllText(markerPath));var id=marker.RootElement.GetProperty("installId").GetString()!;MatchJson.Id(id);
        var backupRoot=Path.Combine(dataRoot,"backups/bot-experiment");
        if (!Path.GetFullPath(marker.RootElement.GetProperty("backupRoot").GetString()!).Equals(Path.GetFullPath(backupRoot),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("非当前数据根目录的活动安装");
        var manifestPath=Safe(backupRoot,id+"/candidate-manifest.json");var sha=Hash(manifestPath);
        if (sha!=marker.RootElement.GetProperty("manifestSha256").GetString()) throw new InvalidDataException("活动 manifest checksum 漂移");
        var recordPath=Safe(backupRoot,id+"/install-record.json");using var record=JsonDocument.Parse(File.ReadAllText(recordPath));var r=record.RootElement;
        if (r.GetProperty("installId").GetString()!=id || !Path.GetFullPath(r.GetProperty("cs2Root").GetString()!).Equals(Path.GetFullPath(cs2Root),StringComparison.OrdinalIgnoreCase) || r.GetProperty("state").GetString()!="installed" || r.GetProperty("manifestSha256").GetString()!=sha || r.GetProperty("humanPolicy").GetString()!="preserve") throw new InvalidDataException("当前安装未完整结束或不是 Human-preserve 候选");
        using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));var m=manifest.RootElement;
        if (m.GetProperty("schemaVersion").GetInt32()!=1 || m.GetProperty("lane").GetString()!="bot-experiment" || m.GetProperty("humanPolicy").GetString()!="preserve" || m.GetProperty("testFixture").GetBoolean() || m.GetProperty("cssVersion").GetString()!="1.0.376" || m.GetProperty("metamod").GetProperty("build").GetInt32()!=1481) throw new InvalidDataException("运行时 manifest lane/framework/policy 无效");
        var binaries=m.GetProperty("gameBinaries").EnumerateObject().Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        if (!binaries.SetEquals(new[]{"game/csgo/bin/win64/server.dll","game/bin/win64/engine2.dll","game/csgo/bin/win64/client.dll","game/bin/win64/schemasystem.dll","game/bin/win64/cs2.exe"})) throw new InvalidDataException("缺少五项游戏 binary 身份");
        if (!m.TryGetProperty("matchSetup",out var contract) || contract.GetProperty("schemaVersion").GetInt32()!=1 || m.GetProperty("botRef").GetString()!="e18c029a76b7515ea5cb15cb08c41f03de4c0a57") throw new InvalidDataException("当前安装缺少受控开局适配层，保存的配置不会自动应用");
        var steam=SteamInstallState.Inspect(cs2Root);var target=m.GetProperty("targetBuild");
        if (!steam.Idle || steam.BuildId!=target.GetProperty("buildId").GetString() || steam.ClientVersion!=target.GetProperty("ClientVersion").GetString() || steam.PatchVersion!=target.GetProperty("PatchVersion").GetString()) throw new InvalidDataException("Steam/游戏 build 漂移，禁止自动应用");
        void Verify(string rel,string expected) { if (Hash(Safe(cs2Root,rel))!=expected) throw new InvalidDataException("运行时文件漂移："+rel); }
        foreach (var binary in m.GetProperty("gameBinaries").EnumerateObject()) Verify(binary.Name,binary.Value.GetString()!);
        foreach (var e in m.GetProperty("files").EnumerateArray()) Verify(e.GetProperty("target").GetString()!,e.GetProperty("sha256").GetString()!);
        var humanUpdate = BotHumanFixtureSidecar.Exists(backupRoot,id)
            ? new BotHumanFixtureSidecar(cs2Root,backupRoot,id).Inspect() : null;
        foreach (var h in m.GetProperty("coexistence").GetProperty("humanFiles").EnumerateObject())
            Verify(h.Name,h.Name==BotHumanFixtureSidecar.Target && humanUpdate is not null?humanUpdate.CurrentSha256:h.Value.GetString()!);
        foreach (var p in m.GetProperty("baselineFiles").EnumerateObject().Where(p=>p.Name.Contains("/counterstrikesharp/api/") || p.Name.Contains("/counterstrikesharp/bin/") || p.Name.EndsWith("/counterstrikesharp.vdf"))) Verify(p.Name,p.Value.GetString()!);
        using var fresh=JsonDocument.Parse(File.ReadAllText(recordPath));
        if (fresh.RootElement.GetProperty("state").GetString()!="installed" || Hash(manifestPath)!=sha || Hash(markerPath)!=markerHash || !SteamInstallState.Inspect(cs2Root).Equals(steam)) throw new InvalidDataException("校验期间安装或 Steam 状态变化");
        var profile=Enum.Parse<InstalledProfile>(m.GetProperty("profile").GetProperty("difficulty").GetString()!);
        return new(id,sha,profile);
    }
}
