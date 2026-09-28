using System.Text.Json;

namespace CS2LocalKit.Core.Catalog;

/// <summary>
/// Pinned ByMykel/CSGO-API snapshot access. The catalog commit is pinned (same provenance
/// as the accepted C2 migration); normal operation reads the local cache and never floats
/// to latest. The snapshot only provides definitions/membership/display metadata.
/// </summary>
public static class CatalogSnapshot
{
    public const string PinnedCommit = "8a71e35c0489ac3093661af713525f2f0ebe1ad7";
    public const string RawBase = "https://raw.githubusercontent.com/ByMykel/CSGO-API/" + PinnedCommit + "/public/api/en";

    public static string DefaultCacheRoot(string cs2ModRoot)
        => Path.Combine(cs2ModRoot, "app-data", "cosmetics-lab", "catalog", PinnedCommit);

    public static (string Skins, string MusicKits) EnsureCached(string cacheRoot)
    {
        Directory.CreateDirectory(cacheRoot);
        var skins = Path.Combine(cacheRoot, "skins.json");
        var music = Path.Combine(cacheRoot, "music_kits.json");
        using var http = new HttpClient();
        if (!File.Exists(skins)) File.WriteAllBytes(skins, http.GetByteArrayAsync($"{RawBase}/skins.json").GetAwaiter().GetResult());
        if (!File.Exists(music)) File.WriteAllBytes(music, http.GetByteArrayAsync($"{RawBase}/music_kits.json").GetAwaiter().GetResult());
        return (skins, music);
    }
}

/// <summary>
/// Read-only index over a pinned catalog snapshot: weapon definitions with paint-kit
/// membership, and music kits. Catalog-driven classification (knife/gloves) is display
/// metadata only - preset validation never depends on it.
/// </summary>
public sealed class CatalogIndex
{
    private readonly Dictionary<int, WeaponDef> _byWeapon;
    private readonly Dictionary<int, string> _musicById;

    public string Commit { get; }

    public CatalogIndex(Dictionary<int, WeaponDef> byWeapon, Dictionary<int, string> musicById, string commit)
    {
        _byWeapon = byWeapon;
        _musicById = musicById;
        Commit = commit;
    }

    public static CatalogIndex Load(string cacheDir, string commit = CatalogSnapshot.PinnedCommit)
    {
        var byWeapon = new Dictionary<int, WeaponDef>();
        foreach (var s in JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(cacheDir, "skins.json"))) ?? [])
        {
            if (!s.TryGetProperty("weapon", out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("weapon_id", out var widEl)) continue;
            var defIndex = widEl.GetInt32();
            var name = w.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var categoryId = s.TryGetProperty("category", out var c) && c.TryGetProperty("id", out var cid) ? cid.GetString() ?? "" : "";
            if (!byWeapon.TryGetValue(defIndex, out var def))
            {
                def = new WeaponDef(defIndex, name, categoryId, new Dictionary<int, string>());
                byWeapon[defIndex] = def;
            }
            if (s.TryGetProperty("paint_index", out var pi))
            {
                // ByMykel data carries paint_index as a string in some snapshots and as a
                // number in others - accept both.
                int? paint = pi.ValueKind switch
                {
                    JsonValueKind.Number when pi.TryGetInt32(out var i) => i,
                    JsonValueKind.String when int.TryParse(pi.GetString(), out var i2) => i2,
                    _ => null,
                };
                if (paint is { } p && !def._paints.ContainsKey(p)) def._paints[p] = s.GetProperty("name").GetString() ?? "";
            }
        }

        var musicById = new Dictionary<int, string>();
        foreach (var m in JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(Path.Combine(cacheDir, "music_kits.json"))) ?? [])
        {
            if (!m.TryGetProperty("def_index", out var di)) continue;
            var id = int.Parse(di.GetString() ?? "");
            var isStatTrak = m.TryGetProperty("market_hash_name", out var mh) && (mh.GetString() ?? "").StartsWith("StatTrak");
            if (!musicById.ContainsKey(id) && !isStatTrak) musicById[id] = m.GetProperty("name").GetString() ?? "";
            else if (!musicById.ContainsKey(id)) musicById[id] = m.GetProperty("name").GetString() ?? "";
        }

        return new CatalogIndex(byWeapon, musicById, commit);
    }

    public bool TryGetWeapon(int defIndex, out WeaponDef def) => _byWeapon.TryGetValue(defIndex, out def!);

    /// <summary>defIndex exists AND the paint kit belongs to that defIndex.</summary>
    public bool HasPaint(int defIndex, int paint)
        => _byWeapon.TryGetValue(defIndex, out var def) && def.Paints.ContainsKey(paint);

    public bool TryGetMusicKit(int id, out string name) => _musicById.TryGetValue(id, out name!);

    public sealed class WeaponDef(int defIndex, string name, string categoryId, Dictionary<int, string> paints)
    {
        public int DefIndex { get; } = defIndex;
        public string Name { get; } = name;
        public string CategoryId { get; } = categoryId;
        internal readonly Dictionary<int, string> _paints = paints;
        public IReadOnlyDictionary<int, string> Paints => _paints;
        public bool IsKnife => CategoryId.Contains("melee", StringComparison.OrdinalIgnoreCase) || CategoryId.Contains("knife", StringComparison.OrdinalIgnoreCase);
        public bool IsGloves => CategoryId.Contains("gloves", StringComparison.OrdinalIgnoreCase);
    }
}
