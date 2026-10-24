using System.Security.Cryptography;
using System.Text.Json;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Read-only authority for the immutable playable substrate.</summary>
public static class RuntimeIntegrationGuard
{
    public const string Marker = "game/csgo/.cs2-local-kit-runtime-integration.json";
    public static void VerifyParents(string root, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("runtimeIntegration",out var c)) return;
        if (c.GetProperty("schemaVersion").GetInt32()!=1 ||
            !Path.GetFullPath(c.GetProperty("cs2Root").GetString()!).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("整合基线目标不匹配");
        var parents=c.GetProperty("parents").EnumerateObject().ToArray();
        if (!parents.Select(p=>p.Name).ToHashSet().SetEquals(new[]{"bot","compat","nades"}))
            throw new InvalidDataException("整合基线安装链不完整");
        foreach (var parent in parents)
        {
            var p=parent.Value;var folder=p.GetProperty("backupDir").GetString()!;
            MatchSetup.MatchSetupStore.CheckPath(folder);
            var expected=p.GetProperty("inventory").EnumerateObject().ToDictionary(v=>v.Name,v=>v.Value);
            void Walk(string dir)
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(dir))
                {
                    MatchSetup.MatchSetupStore.CheckPath(path);
                    var rel=Path.GetRelativePath(folder,path).Replace('\\','/');
                    if (!expected.Remove(rel,out var e)) throw new InvalidDataException("安装备份出现未知文件："+rel);
                    if (Directory.Exists(path))
                    {
                        if(e.GetProperty("kind").GetString()!="directory") throw new InvalidDataException("安装备份类型漂移");
                        Walk(path);
                    }
                    else if(e.GetProperty("kind").GetString()!="file" || Hash(path)!=e.GetProperty("sha256").GetString())
                        throw new InvalidDataException("安装备份内容漂移："+rel);
                }
            }
            Walk(folder);if(expected.Count!=0)throw new InvalidDataException("安装备份缺失");
            var marker=Safe(root,p.GetProperty("marker").GetString()!);
            if(Hash(marker)!=p.GetProperty("markerIdentity").GetProperty("sha256").GetString())throw new InvalidDataException("父安装标记漂移");
            using var r=JsonDocument.Parse(File.ReadAllText(Safe(folder,p.GetProperty("recordName").GetString()!)));
            using var mark=JsonDocument.Parse(File.ReadAllText(marker));
            if(r.RootElement.GetProperty("state").GetString()!="installed" || r.RootElement.GetProperty("installId").GetString()!=p.GetProperty("installId").GetString() ||
                !Path.GetFullPath(r.RootElement.GetProperty("cs2Root").GetString()!).Equals(Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase) ||
                Hash(Safe(folder,p.GetProperty("manifestName").GetString()!))!=r.RootElement.GetProperty("manifestSha256").GetString() ||
                mark.RootElement.GetProperty("installId").GetString()!=p.GetProperty("installId").GetString() ||
                mark.RootElement.GetProperty("manifestSha256").GetString()!=r.RootElement.GetProperty("manifestSha256").GetString())
                throw new InvalidDataException("父安装记录不匹配");
        }
    }
    public static string Safe(string root,string relative)
    {
        if(Path.IsPathRooted(relative)||relative.Contains(':')||relative.Contains('\\')||relative.Split('/').Any(p=>p is "" or "." or ".."))throw new InvalidDataException("基线路径无效");
        var path=Path.GetFullPath(Path.Combine(root,relative));MatchSetup.MatchSetupStore.CheckPath(path);return path;
    }
    public static string Hash(string path)
    {
        using var stream=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    public static bool IsRestoredSubstrate(string root,string backup,string id)
    {
        using var r=JsonDocument.Parse(File.ReadAllText(Path.Combine(backup,id,"install-record.json")));
        using var m=JsonDocument.Parse(File.ReadAllText(Path.Combine(backup,id,"candidate-manifest.json")));
        if(r.RootElement.GetProperty("state").GetString()!="restored" || !m.RootElement.TryGetProperty("runtimeIntegration",out _))return false;
        VerifyParents(root,m.RootElement);
        foreach(var p in m.RootElement.GetProperty("baselineFiles").EnumerateObject())
            if(Hash(Safe(root,p.Name))!=p.Value.GetString())return false;
        return !File.Exists(Safe(root,Marker));
    }
    public static string? RestoredHumanHandoff(string root,string backups,JsonElement manifest)
    {
        var baseline=manifest.GetProperty("baselineFiles").GetProperty(BotHumanFixtureSidecar.Target).GetString();
        var current=Hash(Safe(root,BotHumanFixtureSidecar.Target));
        if(current==baseline)return null;
        foreach(var dir in Directory.Exists(backups)?Directory.EnumerateDirectories(backups).OrderBy(p=>p):Enumerable.Empty<string>())
        {
            var path=Path.Combine(dir,"human-fixture-update/update.json");
            if(!File.Exists(path))continue;MatchSetup.MatchSetupStore.CheckPath(path);
            var value=JsonSerializer.Deserialize<HumanFixtureUpdate>(File.ReadAllText(path))!;
            if(value.Stage!="completed"||value.CurrentSha256!=current||value.BaselineSha256!=baseline)continue;
            using var r=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"install-record.json")));
            using var m=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"candidate-manifest.json")));
            if(value.InstallId!=Path.GetFileName(dir)||r.RootElement.GetProperty("state").GetString()!="restored"||
                r.RootElement.GetProperty("cs2Root").GetString()!=Path.GetFullPath(root)||
                Hash(Path.Combine(dir,"candidate-manifest.json"))!=value.ManifestSha256||r.RootElement.GetProperty("manifestSha256").GetString()!=value.ManifestSha256||
                !m.RootElement.TryGetProperty("runtimeIntegration",out var c)||!JsonElement.DeepEquals(c,manifest.GetProperty("runtimeIntegration"))||
                !JsonElement.DeepEquals(m.RootElement.GetProperty("baselineFiles"),manifest.GetProperty("baselineFiles"))||
                Hash(Path.Combine(dir,"human-fixture-update/baseline.json"))!=baseline||Hash(Path.Combine(dir,"human-fixture-update/current.json"))!=current)
                throw new InvalidDataException("已恢复的 Human 移交记录漂移");
            return value.InstallId;
        }
        throw new InvalidDataException("饰品文件变化没有可核实的整合恢复记录");
    }
}
