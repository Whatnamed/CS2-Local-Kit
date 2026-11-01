using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CS2LocalKit.Core.Runtime;

/// <summary>Read-only authority for the immutable playable substrate.</summary>
public static class RuntimeIntegrationGuard
{
    public const string Marker = "game/csgo/.cs2-local-kit-runtime-integration.json";
    public static bool IsReviewedSource(string digest, string patchVersion)
    {
        using var review = Review(patchVersion);
        return review is not null && review.RootElement.GetProperty("sourceManifests").EnumerateArray().Any(v=>v.GetString()==digest);
    }
    private static JsonDocument? Review(string patchVersion)
    {
        var resource = patchVersion switch { "1.41.9.3" => "Review14193", "1.41.9.4" => "Review14194", _ => null };
        return resource is null ? null : JsonDocument.Parse(typeof(RuntimeIntegrationGuard).Assembly.GetManifestResourceStream("CS2LocalKit."+resource+".json")!);
    }
    public static JsonDocument ReadManifest(string root, string path)
    {
        var raw = File.ReadAllText(path);
        var steam = SteamInstallState.Inspect(root);
        using var review = Review(steam.PatchVersion);
        return review is null ? JsonDocument.Parse(raw) : ReviewedBuild(raw, Hash(path), steam, review.RootElement, p=>Hash(Safe(root,p)));
    }
    internal static JsonDocument ReviewedBuild(string raw, string digest, SteamInstallState steam, JsonElement review, Func<string,string> hash)
    {
        if (steam.PatchVersion!=review.GetProperty("targetBuild").GetProperty("PatchVersion").GetString() || !review.GetProperty("sourceManifests").EnumerateArray().Any(v=>v.GetString()==digest)) return JsonDocument.Parse(raw);
        var m = JsonNode.Parse(raw)!;
        var target = review.GetProperty("targetBuild");
        if (!steam.Idle || steam.BuildId!=target.GetProperty("buildId").GetString() || steam.ClientVersion!=target.GetProperty("ClientVersion").GetString() ||
            !JsonNode.DeepEquals(m["targetBuild"],JsonNode.Parse(review.GetProperty("previousBuild").GetRawText())))
            throw new InvalidDataException("已核查构建身份不匹配");
        foreach (var p in review.GetProperty("gameBinaries").EnumerateObject())
            if(hash(p.Name)!=p.Value.GetString())throw new InvalidDataException("已核查游戏二进制漂移："+p.Name);
        foreach (var p in review.GetProperty("retainedFiles").EnumerateObject())
        {
            if(m["baselineFiles"]![p.Name]!.GetValue<string>()!=p.Value.GetProperty("before").GetString())throw new InvalidDataException("已核查旧基线不匹配");
            m["baselineFiles"]![p.Name]=p.Value.GetProperty("after").GetString();
        }
        m["targetBuild"]=JsonNode.Parse(target.GetRawText());
        m["gameBinaries"]=JsonNode.Parse(review.GetProperty("gameBinaries").GetRawText());
        m["coexistence"]!["reviewedBuild"]=JsonNode.Parse(target.GetRawText());
        m["reviewedRetainedFiles"]=JsonNode.Parse(review.GetProperty("retainedFiles").GetRawText());
        return JsonDocument.Parse(m.ToJsonString());
    }
    public static string ReviewedPostimage(JsonElement manifest, JsonElement entry)
    {
        var expected=entry.GetProperty("after").GetProperty("sha256").GetString()!;
        if(entry.GetProperty("action").GetString()=="retained" && manifest.TryGetProperty("reviewedRetainedFiles",out var files) &&
            files.TryGetProperty(entry.GetProperty("target").GetString()!,out var update))
        {
            if(expected!=update.GetProperty("before").GetString() || entry.GetProperty("before").GetProperty("sha256").GetString()!=expected)
                throw new InvalidDataException("已核查保留文件记录不匹配");
            return update.GetProperty("after").GetString()!;
        }
        return expected;
    }
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
        using var m=ReadManifest(root,Path.Combine(backup,id,"candidate-manifest.json"));
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
            using var m=ReadManifest(root,Path.Combine(dir,"candidate-manifest.json"));
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
