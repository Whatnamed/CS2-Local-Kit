using System.Text.Json;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.Core.MatchSetup;

public sealed record DailyFileState(InstalledProfile Profile, MatchPlayMode? Mode);

/// <summary>Finite, manifest-owned images. Historical manifests and install records never change.</summary>
public static class DailyPlayFiles
{
    public const string ProfileTarget = "game/csgo/overrides/botprofile.vpk";
    public const string DisabledSuffix = ".localkit-disabled";
    public static readonly string[] BotLoaders = new[] { "BotAI", "BotAimImprover", "BotBuy", "BotControllerImpl", "BotHiderImpl", "BotRandomizer", "BotState", "NadeSystem" }
        .Select(n => $"game/csgo/addons/counterstrikesharp/plugins/{n}/{n}.dll")
        .Concat(new[] { "BotController", "BotHider", "BotVision" }.Select(n => $"game/csgo/addons/metamod/{n}.vdf")).Concat(new[] { "game/csgo/cfg/my_bot_normal_config.cfg", "game/csgo/cfg/my_bot_ffa_config.cfg" }).Append(ProfileTarget).ToArray();

    public static bool Supports(JsonElement manifest) => manifest.TryGetProperty("dailyPlay", out _);
    public static void ValidateContract(JsonElement manifest)
    {
        if (!Supports(manifest)) return;
        var c = manifest.GetProperty("dailyPlay");
        if (c.GetProperty("schemaVersion").GetInt32() != 1 ||
            !c.GetProperty("modeFiles").EnumerateArray().Select(v => v.GetString()!).ToHashSet().SetEquals(BotLoaders) ||
            !c.GetProperty("profiles").EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(new[] { "Low", "Medium", "High" }))
            throw new InvalidDataException("日常配置 ownership 合约无效");
        var files = manifest.GetProperty("files").EnumerateArray().ToDictionary(e => e.GetProperty("target").GetString()!);
        foreach (var loader in BotLoaders) if (!files.ContainsKey(loader)) throw new InvalidDataException("缺少 Bot 加载文件：" + loader);
        foreach (var p in c.GetProperty("profiles").EnumerateObject())
        {
            var target = $"game/csgo/overrides/localkit-profiles/{p.Name}.vpk";
            if (p.Value.GetProperty("target").GetString() != target || !files.TryGetValue(target, out var file) ||
                file.GetProperty("sha256").GetString() != p.Value.GetProperty("sha256").GetString())
                throw new InvalidDataException("日常 profile 来源无效");
        }
    }
    public static string ImagePath(string root, JsonElement manifest, string target)
    {
        var path = RuntimeIntegrationGuard.Safe(root, target);
        if (!Supports(manifest) || !BotLoaders.Contains(target)) return path;
        var disabled = RuntimeIntegrationGuard.Safe(root, target + DisabledSuffix);
        if (Directory.Exists(path) || Directory.Exists(disabled) || File.Exists(path) == File.Exists(disabled)) throw new InvalidDataException("Bot 加载文件缺失或同时存在：" + target);
        return File.Exists(path) ? path : disabled;
    }
    public static string ExpectedHash(string root, JsonElement manifest, string target, string immutableHash)
    {
        if (!Supports(manifest)) return immutableHash;
        if (target != ProfileTarget) return immutableHash;
        var actual = RuntimeIntegrationGuard.Hash(ImagePath(root, manifest, target));
        if (!manifest.GetProperty("dailyPlay").GetProperty("profiles").EnumerateObject().Any(p => p.Value.GetProperty("sha256").GetString() == actual))
            throw new InvalidDataException("未知 profile 文件；保留现场，禁止覆盖");
        return actual;
    }
    public static DailyFileState Read(string root, JsonElement manifest)
    {
        ValidateContract(manifest);
        if (!Supports(manifest)) throw new InvalidDataException("当前安装需要一次性迁移到日常配置底座");
        var hash = RuntimeIntegrationGuard.Hash(ImagePath(root, manifest, ProfileTarget));
        var level = manifest.GetProperty("dailyPlay").GetProperty("profiles").EnumerateObject().SingleOrDefault(p => p.Value.GetProperty("sha256").GetString() == hash);
        if (level.Name is null) throw new InvalidDataException("未知 profile 文件");
        var active = BotLoaders.Count(p => ImagePath(root, manifest, p) == RuntimeIntegrationGuard.Safe(root, p));
        return new(Enum.Parse<InstalledProfile>(level.Name), active == BotLoaders.Length ? MatchPlayMode.EnhancedHuman : active == 0 ? MatchPlayMode.NormalHuman : null);
    }
    public static void VerifyModules(string root, JsonElement manifest, JsonElement expected)
    {
        if (!Supports(manifest)) return;
        foreach (var group in expected.EnumerateObject())
        {
            var actual=new Dictionary<string,(string Kind,string? Hash)>(StringComparer.Ordinal);
            var folder=RuntimeIntegrationGuard.Safe(root,group.Name=="valveServerPluginDeclarations"?"game/csgo/addons":group.Name);
            if (Directory.Exists(folder))
            {
                var paths=group.Name=="valveServerPluginDeclarations" ? Directory.EnumerateFiles(folder,"*.vdf") : Directory.EnumerateFileSystemEntries(folder,"*",SearchOption.AllDirectories);
                foreach (var path in paths)
                {
                    MatchSetupStore.CheckPath(path);
                    var relative=Path.GetRelativePath(folder,path).Replace('\\','/');
                    if (relative.EndsWith(DisabledSuffix) && BotLoaders.Contains(group.Name+"/"+relative[..^DisabledSuffix.Length])) relative=relative[..^DisabledSuffix.Length];
                    if (!actual.TryAdd(relative,Directory.Exists(path)?("directory",null):("file",RuntimeIntegrationGuard.Hash(path)))) throw new InvalidDataException("重复 Bot 加载文件");
                }
            }
            foreach (var file in group.Value.EnumerateObject())
                if (!actual.Remove(file.Name,out var value) || value.Kind!=file.Value.GetProperty("kind").GetString() || value.Kind=="file" && value.Hash!=file.Value.GetProperty("sha256").GetString()) throw new InvalidDataException("模块清单漂移："+file.Name);
            if (actual.Count!=0) throw new InvalidDataException("发现未知模块文件："+actual.Keys.First());
        }
        var ini=RuntimeIntegrationGuard.Safe(root,"game/csgo/addons/metamod/metaplugins.ini");
        if (File.Exists(ini) && File.ReadLines(ini).Any(s=>s.Trim().Length>0 && !s.TrimStart().StartsWith(';'))) throw new InvalidDataException("未核查的 metaplugins.ini 加载项");
    }
    // Caller owns the cross-process Human gate and full installation validation.
    public static DailyFileState Apply(string root, JsonElement manifest, InstalledProfile profile, MatchPlayMode mode, Action checkWritable)
    {
        if (!Enum.IsDefined(profile) || !Enum.IsDefined(mode)) throw new InvalidDataException("未知日常设置");
        Read(root, manifest);
        var source = manifest.GetProperty("dailyPlay").GetProperty("profiles").GetProperty(profile.ToString());
        var bytes = File.ReadAllBytes(RuntimeIntegrationGuard.Safe(root, source.GetProperty("target").GetString()!));
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != source.GetProperty("sha256").GetString()) throw new InvalidDataException("profile 来源漂移");
        checkWritable();
        var destination = ImagePath(root, manifest, ProfileTarget);
        if (RuntimeIntegrationGuard.Hash(destination) != source.GetProperty("sha256").GetString())
        {
            var before=RuntimeIntegrationGuard.Hash(destination);
            var temporary = destination + ".new"; MatchSetupStore.CheckPath(temporary);var created=false;
            try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { created=true;stream.Write(bytes); stream.Flush(true); } checkWritable(); if (RuntimeIntegrationGuard.Hash(destination)!=before) throw new IOException("profile 在写入前变化"); File.Move(temporary, destination, true); }
            finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
        }
        foreach (var target in BotLoaders)
        {
            checkWritable();
            var current = ImagePath(root, manifest, target);
            var expected=manifest.GetProperty("files").EnumerateArray().Single(e=>e.GetProperty("target").GetString()==target).GetProperty("sha256").GetString()!;
            if (RuntimeIntegrationGuard.Hash(current)!=ExpectedHash(root,manifest,target,expected)) throw new IOException("Bot 文件在切换前变化："+target);
            var next = RuntimeIntegrationGuard.Safe(root, target + (mode == MatchPlayMode.NormalHuman ? DisabledSuffix : ""));
            if (current != next) File.Move(current, next);
        }
        var result = Read(root, manifest);
        if (result.Profile != profile || result.Mode != mode) throw new IOException("日常配置文件读回失败");
        return result;
    }
}
