using System.Collections.Immutable;
using System.Security.Cryptography;

namespace CS2LocalKit.Core.MatchSetup;

public sealed record MatchLibrary(int SchemaVersion, ImmutableArray<CustomRoster> CustomRosters, ImmutableArray<MatchSetupPreset> Presets, ImmutableArray<string> Recent, MatchSetupPreset? LastUsed = null)
{
    public static MatchLibrary Empty => new(1, [], [], []);
}
public sealed record MatchSetupRequest(int SchemaVersion, string RequestId, DateTimeOffset SavedAt, string PresetSha256, MatchSetupPreset Preset);

/// <summary>Only owns app-data/match-setup. Never touches Human presets or game files.</summary>
public sealed class MatchSetupStore(string root, RosterCatalog catalog)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string LibraryPath => Path.Combine(Root, "library.json");
    public string RequestPath => Path.Combine(Root, "next-match.json");
    public string StatusPath => Path.Combine(Root, "runtime-status.json");
    public static string Digest(MatchSetupPreset preset) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(MatchJson.Write(preset))));
    public MatchLibrary Load()
    {
        CheckPath(LibraryPath);
        if (!File.Exists(LibraryPath)) return MatchLibrary.Empty;
        var value = MatchJson.Read<MatchLibrary>(File.ReadAllText(LibraryPath)); Validate(value); return value;
    }
    public void Validate(MatchLibrary value)
    {
        if (value.SchemaVersion != 1 || value.CustomRosters.IsDefault || value.Presets.IsDefault || value.Recent.IsDefault) throw new InvalidDataException("不支持的 library schemaVersion；禁止自动覆盖或猜测迁移");
        if (value.CustomRosters.Select(r => r.Id).Distinct().Count() != value.CustomRosters.Length || value.Presets.Select(p => p.Id).Distinct().Count() != value.Presets.Length) throw new InvalidDataException("文件内 ID 冲突，请先消歧");
        foreach (var r in value.CustomRosters)
        {
            MatchJson.Id(r.Id); MatchJson.Name(r.Name, 120); MatchJson.Slots(r.Slots);
            if (catalog.Rosters.Any(b => b.Id == r.Id)) throw new InvalidDataException("内置版本只读，请复制为自定义阵容");
        }
        foreach (var p in value.Presets) catalog.Resolve(p);
        if (value.LastUsed is not null) catalog.Resolve(value.LastUsed);
    }
    public void Save(MatchLibrary value) { Validate(value); Write(LibraryPath, MatchJson.Write(value)); }
    public MatchSetupRequest Queue(MatchSetupPreset preset)
    {
        catalog.Resolve(preset);
        var request = new MatchSetupRequest(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, Digest(preset), preset);
        Write(RequestPath, MatchJson.Write(request)); return request;
    }
    public MatchSetupRequest? ReadRequest()
    {
        CheckPath(RequestPath); if (!File.Exists(RequestPath)) return null;
        var r = MatchJson.Read<MatchSetupRequest>(File.ReadAllText(RequestPath));
        MatchJson.Id(r.RequestId);
        if (r.SchemaVersion != 1 || Digest(r.Preset) != r.PresetSha256) throw new InvalidDataException("开局请求版本或校验和不匹配");
        catalog.Resolve(r.Preset); return r;
    }
    public void Import(string json)
    {
        var incoming = MatchJson.Read<MatchLibrary>(json); Validate(incoming);
        var existing = Load();
        static ImmutableArray<T> Merge<T>(ImmutableArray<T> current, ImmutableArray<T> incoming, Func<T,string> id)
        {
            var result = current.ToDictionary(id);
            foreach (var item in incoming)
            {
                if (result.TryGetValue(id(item), out var old) && MatchJson.Write(old) != MatchJson.Write(item)) throw new InvalidDataException($"导入 ID 冲突：{id(item)}；保留现有文件，请复制或更改 ID 后导入");
                result[id(item)] = item;
            }
            return result.Values.ToImmutableArray();
        }
        Save(existing with { CustomRosters = Merge(existing.CustomRosters, incoming.CustomRosters, r => r.Id), Presets = Merge(existing.Presets, incoming.Presets, p => p.Id) });
    }
    public string Export() => MatchJson.Write(Load());
    public void WriteStatus(MatchRuntimeStatus status) => Write(StatusPath, MatchJson.Write(status), backup: false);
    public static void CheckPath(string path)
    {
        for (var p = new FileInfo(Path.GetFullPath(path)) as FileSystemInfo; p is not null; p = p is FileInfo f ? f.Directory : ((DirectoryInfo)p).Parent)
            if ((p.Exists || Directory.Exists(p.FullName)) && p.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("拒绝链接路径：" + p.FullName);
    }
    private void Write(string path, string text, bool backup = true)
    {
        CheckPath(path); Directory.CreateDirectory(Root);
        var temp = path + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, text);
            if (backup && File.Exists(path)) File.Copy(path, path + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff"), false);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
