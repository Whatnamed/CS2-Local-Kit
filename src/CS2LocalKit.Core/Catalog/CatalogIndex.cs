using System.Text.Json;

namespace CS2LocalKit.Core.Catalog;

/// <summary>
/// Read-only index over a pinned catalog snapshot: weapon definitions with paint-kit
/// membership, and music kits. Knife and glove classification participates in
/// HumanPreset catalog validation and UI enumeration.
///
/// Identity is numeric and comes from the English snapshot only (defIndex / paintIndex /
/// musicKit def_index). Simplified Chinese, when cached, is merged onto those same numeric
/// keys as display metadata; it never adds, removes or renames an identity, and localized
/// names are never used as join keys or lookup authorities.
/// </summary>
public sealed class CatalogIndex
{
    private readonly Dictionary<int, WeaponDef> _byWeapon;
    private readonly Dictionary<int, CatalogMusicKit> _musicById;

    public string Commit { get; }

    /// <summary>Locales merged into this index, identity locale first (for example "en", "zh-CN").</summary>
    public IReadOnlyList<string> LocalesLoaded { get; }

    public bool HasChinese => LocalesLoaded.Contains(CatalogSnapshot.ChineseLocale);

    public int WeaponCount => _byWeapon.Count;
    public int PaintCount => _byWeapon.Values.Sum(w => w.Paints.Count);
    public int MusicKitCount => _musicById.Count;
    /// <summary>How many paint entries carry localized Chinese metadata.</summary>
    public int LocalizedPaintCount => _byWeapon.Values.Sum(w => w.Paints.Values.Count(p => p.ChineseName is not null));
    public int LocalizedMusicKitCount => _musicById.Values.Count(m => m.ChineseName is not null);

    public CatalogIndex(Dictionary<int, WeaponDef> byWeapon, Dictionary<int, CatalogMusicKit> musicById,
        string commit, IReadOnlyList<string>? localesLoaded = null)
    {
        _byWeapon = byWeapon;
        _musicById = musicById;
        Commit = commit;
        LocalesLoaded = localesLoaded ?? [CatalogSnapshot.IdentityLocale];
    }

    /// <summary>
    /// Loads the cache at <paramref name="cacheDir"/>, honouring both the per-locale layout and
    /// the legacy English-only layout. Chinese metadata is merged when cached, absent otherwise.
    /// </summary>
    public static CatalogIndex Load(string cacheDir, string commit = CatalogSnapshot.PinnedCommit)
    {
        var enDir = CatalogSnapshot.ResolveLocaleDir(cacheDir, CatalogSnapshot.IdentityLocale)
            ?? throw new CatalogCacheException($"No English catalog snapshot found under {cacheDir}.");
        var zhDir = CatalogSnapshot.ResolveLocaleDir(cacheDir, CatalogSnapshot.ChineseLocale);

        var byWeapon = LoadSkins(Path.Combine(enDir, "skins.json"));
        var locales = new List<string> { CatalogSnapshot.IdentityLocale };

        if (zhDir is not null)
        {
            MergeSkinsChinese(byWeapon, Path.Combine(zhDir, "skins.json"));
            locales.Add(CatalogSnapshot.ChineseLocale);
        }

        var music = LoadMusicKits(Path.Combine(enDir, "music_kits.json"));
        if (zhDir is not null && File.Exists(Path.Combine(zhDir, "music_kits.json")))
            MergeMusicKitsChinese(music, Path.Combine(zhDir, "music_kits.json"));

        return new CatalogIndex(byWeapon, music, commit, locales);
    }

    private static Dictionary<int, WeaponDef> LoadSkins(string skinsPath)
    {
        var byWeapon = new Dictionary<int, WeaponDef>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(skinsPath));
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            if (!s.TryGetProperty("weapon", out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetInt(w, "weapon_id", out var defIndex)) continue;
            if (!byWeapon.TryGetValue(defIndex, out var def))
            {
                def = new WeaponDef(defIndex, GetString(w, "name"), GetNestedId(s, "category"),
                    new Dictionary<int, CatalogPaint>(), GetString(w, "id"));
                byWeapon[defIndex] = def;
            }
            if (!s.TryGetProperty("paint_index", out var pi)) continue;
            var paint = ReadInt(pi);
            if (paint is null || def._paints.ContainsKey(paint.Value)) continue;
            def._paints[paint.Value] = new CatalogPaint(
                PaintIndex: paint.Value,
                Name: GetString(s, "name"),
                PatternName: GetNestedIdName(s, "pattern"),
                ImageUrl: GetString(s, "image"),
                RarityId: GetNestedRaw(s, "rarity", "id"),
                RarityName: GetNestedRaw(s, "rarity", "name"),
                RarityColor: GetNestedRaw(s, "rarity", "color"),
                MinFloat: GetDouble(s, "min_float"),
                MaxFloat: GetDouble(s, "max_float"));
        }
        return byWeapon;
    }

    /// <summary>
    /// Second pass over the localized snapshot: for numeric keys that already exist in the
    /// identity index, attach Chinese names. Keys unknown to the identity snapshot are ignored.
    /// </summary>
    private static void MergeSkinsChinese(Dictionary<int, WeaponDef> byWeapon, string zhSkinsPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(zhSkinsPath));
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            if (!s.TryGetProperty("weapon", out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetInt(w, "weapon_id", out var defIndex)) continue;
            if (!byWeapon.TryGetValue(defIndex, out var def)) continue;

            var paint = s.TryGetProperty("paint_index", out var pi) ? ReadInt(pi) : null;
            if (paint is null)
            {
                // Weapon-level record: only the localized weapon display name is attached.
                def.SetChineseName(GetString(w, "name"));
                continue;
            }
            if (!def._paints.TryGetValue(paint.Value, out var existing)) continue;
            def._paints[paint.Value] = existing with
            {
                ChineseName = GetString(s, "name"),
                PatternChineseName = GetNestedIdName(s, "pattern"),
                RarityChineseName = GetNestedRaw(s, "rarity", "name"),
                ImageUrl = existing.ImageUrl ?? GetString(s, "image"),
            };
            def.SetChineseName(GetString(w, "name"));
        }
    }

    private static Dictionary<int, CatalogMusicKit> LoadMusicKits(string musicPath)
    {
        var music = new Dictionary<int, CatalogMusicKit>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(musicPath));
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetInt(m, "def_index", out var id)) continue;
            if (music.ContainsKey(id)) continue;
            music[id] = new CatalogMusicKit(
                Id: id,
                Name: GetString(m, "name"),
                ImageUrl: GetString(m, "image"),
                RarityId: GetNestedRaw(m, "rarity", "id"),
                RarityName: GetNestedRaw(m, "rarity", "name"),
                RarityColor: GetNestedRaw(m, "rarity", "color"));
        }
        return music;
    }

    private static void MergeMusicKitsChinese(Dictionary<int, CatalogMusicKit> music, string zhMusicPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(zhMusicPath));
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            if (!TryGetInt(m, "def_index", out var id)) continue;
            if (!music.TryGetValue(id, out var kit)) continue;
            music[id] = kit with
            {
                ChineseName = GetString(m, "name"),
                RarityChineseName = GetNestedRaw(m, "rarity", "name"),
                ImageUrl = kit.ImageUrl ?? GetString(m, "image"),
            };
        }
    }

    // --- JSON helpers. paint_index / def_index arrive as strings in some pinned snapshots and
    // as numbers in others, so both shapes are accepted. ---

    private static int? ReadInt(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number when el.TryGetInt32(out var i) => i,
        JsonValueKind.String when int.TryParse(el.GetString(), out var s) => s,
        _ => null,
    };

    private static bool TryGetInt(JsonElement owner, string name, out int value)
    {
        value = 0;
        if (!owner.TryGetProperty(name, out var el)) return false;
        if (ReadInt(el) is not { } i) return false;
        value = i;
        return true;
    }

    private static string GetString(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";

    private static double? GetDouble(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d) ? d : null;

    /// <summary>Reads a string from a nested object, tolerating both a bare string and {id/name}.</summary>
    private static string? GetNestedRaw(JsonElement owner, string objectName, string property)
    {
        if (!owner.TryGetProperty(objectName, out var nested)) return null;
        if (nested.ValueKind == JsonValueKind.String) return nested.GetString();
        if (nested.ValueKind != JsonValueKind.Object) return null;
        return nested.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static string GetNestedId(JsonElement owner, string objectName)
        => GetNestedRaw(owner, objectName, "id") ?? "";

    private static string GetNestedIdName(JsonElement owner, string objectName)
        => GetNestedRaw(owner, objectName, "name") ?? "";

    // --- Lookup APIs: numeric identity only. ---

    public bool TryGetWeapon(int defIndex, out WeaponDef def) => _byWeapon.TryGetValue(defIndex, out def!);

    /// <summary>defIndex exists AND the paint kit belongs to that defIndex.</summary>
    public bool HasPaint(int defIndex, int paint)
        => _byWeapon.TryGetValue(defIndex, out var def) && def.Paints.ContainsKey(paint);

    public bool TryGetMusicKit(int id, out string name)
    {
        if (_musicById.TryGetValue(id, out var kit)) { name = kit.Name; return true; }
        name = "";
        return false;
    }

    /// <summary>Full music-kit metadata including localized display name.</summary>
    public bool TryGetMusic(int id, out CatalogMusicKit kit) => _musicById.TryGetValue(id, out kit!);

    public IReadOnlyList<WeaponDef> GetOrdinaryWeapons()
        => _byWeapon.Values.Where(w => w.IsOrdinaryWeapon).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<WeaponDef> GetKnives()
        => _byWeapon.Values.Where(w => w.IsKnife).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<WeaponDef> GetGloves()
        => _byWeapon.Values.Where(w => w.IsGloves).OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public IReadOnlyList<CatalogPaint> GetPaintsForWeapon(int defIndex)
    {
        if (_byWeapon.TryGetValue(defIndex, out var def))
        {
            return def.Paints.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return Array.Empty<CatalogPaint>();
    }

    public IReadOnlyList<CatalogMusicKit> GetMusicKits()
        => _musicById.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

    // --- Search: Chinese name, English name, or numeric id. Missing Chinese simply never
    // matches, so entries without localized metadata stay findable by their English name. ---

    public static bool Matches(string? chinese, string english, int id, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var q = query.Trim();
        return english.Contains(q, StringComparison.OrdinalIgnoreCase)
               || (chinese is not null && chinese.Contains(q, StringComparison.Ordinal))
               || id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<WeaponDef> SearchOrdinaryWeapons(string? query)
    {
        var items = GetOrdinaryWeapons();
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(w => Matches(w.ChineseName, w.Name, w.DefIndex, query)).ToList();
    }

    public IReadOnlyList<WeaponDef> SearchKnives(string? query)
    {
        var items = GetKnives();
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(w => Matches(w.ChineseName, w.Name, w.DefIndex, query)).ToList();
    }

    public IReadOnlyList<WeaponDef> SearchGloves(string? query)
    {
        var items = GetGloves();
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(w => Matches(w.ChineseName, w.Name, w.DefIndex, query)).ToList();
    }

    public IReadOnlyList<CatalogPaint> SearchPaints(int defIndex, string? query)
    {
        var items = GetPaintsForWeapon(defIndex);
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(p => Matches(p.ChineseName ?? p.PatternChineseName, p.Name, p.PaintIndex, query)).ToList();
    }

    public IReadOnlyList<CatalogMusicKit> SearchMusicKits(string? query)
    {
        var items = GetMusicKits();
        if (string.IsNullOrWhiteSpace(query)) return items;
        return items.Where(m => Matches(m.ChineseName, m.Name, m.Id, query)).ToList();
    }

    public sealed class WeaponDef
    {
        private string? _chineseName;

        public int DefIndex { get; }
        public string Name { get; }
        /// <summary>Upstream stable item id (for example "leather_handwraps"); auxiliary only.</summary>
        public string UpstreamId { get; }
        public string CategoryId { get; }
        public string? ChineseName => _chineseName;
        internal readonly Dictionary<int, CatalogPaint> _paints;
        public IReadOnlyDictionary<int, CatalogPaint> Paints => _paints;
        public bool IsKnife => CategoryId.Contains("melee", StringComparison.OrdinalIgnoreCase) || CategoryId.Contains("knife", StringComparison.OrdinalIgnoreCase);
        public bool IsGloves => CategoryId.Contains("gloves", StringComparison.OrdinalIgnoreCase);
        public bool IsOrdinaryWeapon => !IsKnife && !IsGloves;

        public WeaponDef(int defIndex, string name, string categoryId, Dictionary<int, CatalogPaint> paints,
            string upstreamId = "")
        {
            DefIndex = defIndex;
            Name = name;
            CategoryId = categoryId;
            _paints = paints;
            UpstreamId = upstreamId;
        }

        internal void SetChineseName(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) _chineseName = value;
        }
    }
}

/// <summary>
/// One paint kit of a specific weapon. <see cref="Name"/> keeps the historical meaning: the
/// English catalog display name ("AK-47 | Redline"). Everything after it is optional localized
/// or remote presentation metadata and never participates in identity or validation.
/// </summary>
public sealed record CatalogPaint(
    int PaintIndex,
    string Name,
    string? ChineseName = null,
    string? PatternName = null,
    string? PatternChineseName = null,
    string? ImageUrl = null,
    string? RarityId = null,
    string? RarityName = null,
    string? RarityChineseName = null,
    string? RarityColor = null,
    double? MinFloat = null,
    double? MaxFloat = null);

/// <summary>Music kit metadata keyed by the numeric def_index used by HumanPreset.</summary>
public sealed record CatalogMusicKit(
    int Id,
    string Name,
    string? ChineseName = null,
    string? ImageUrl = null,
    string? RarityId = null,
    string? RarityName = null,
    string? RarityChineseName = null,
    string? RarityColor = null);
