using System.Text.Json;

namespace CS2LocalKit.Core.Catalog;

/// <summary>
/// Pinned ByMykel/CSGO-API snapshot access. The catalog commit is pinned (same provenance
/// as the accepted C2 migration); normal operation reads the local cache and never floats
/// to latest. The snapshot only provides definitions/membership/display metadata.
///
/// Cache layout (per locale, so English and Simplified Chinese never overwrite each other):
///   &lt;commit&gt;/en/skins.json, &lt;commit&gt;/en/music_kits.json
///   &lt;commit&gt;/zh-CN/skins.json, &lt;commit&gt;/zh-CN/music_kits.json
/// The pre-C4.2 layout (&lt;commit&gt;/skins.json at the cache root) stays readable as English,
/// so an existing install keeps working until it explicitly syncs the localized files.
/// </summary>
public static class CatalogSnapshot
{
    public const string PinnedCommit = "8a71e35c0489ac3093661af713525f2f0ebe1ad7";

    /// <summary>Locale that defines catalog identity (which defIndex/paintIndex exist).</summary>
    public const string IdentityLocale = "en";

    /// <summary>Simplified Chinese display metadata locale.</summary>
    public const string ChineseLocale = "zh-CN";

    public static readonly IReadOnlyList<string> Locales = [IdentityLocale, ChineseLocale];

    public const string RawApiRoot =
        "https://raw.githubusercontent.com/ByMykel/CSGO-API/" + PinnedCommit + "/public/api";

    public static string RawBaseFor(string locale) => $"{RawApiRoot}/{locale}";

    public static string DefaultCacheRoot(string cs2ModRoot)
        => Path.Combine(cs2ModRoot, "app-data", "cosmetics-lab", "catalog", PinnedCommit);

    public static string LocaleDir(string cacheRoot, string locale) => Path.Combine(cacheRoot, locale);

    public static string CachePath(string cacheRoot, string locale, string file)
        => Path.Combine(LocaleDir(cacheRoot, locale), file);

    /// <summary>
    /// Resolves the directory holding a locale's files: the locale subfolder when it exists,
    /// otherwise the cache root itself for the legacy English-only layout.
    /// </summary>
    public static string? ResolveLocaleDir(string cacheRoot, string locale)
    {
        var localized = LocaleDir(cacheRoot, locale);
        if (File.Exists(Path.Combine(localized, "skins.json"))) return localized;
        if (locale == IdentityLocale && File.Exists(Path.Combine(cacheRoot, "skins.json"))) return cacheRoot;
        return null;
    }

    public static bool IsLocaleCached(string cacheRoot, string locale) => ResolveLocaleDir(cacheRoot, locale) is not null;

    /// <summary>
    /// Downloads the pinned snapshot for every locale into the cache layout, filling in only
    /// what is missing. Existing legacy English files are copied into en/ rather than re-fetched.
    /// </summary>
    public static async Task<CatalogSyncReport> SyncAsync(string cacheRoot, ICatalogFetcher? fetcher = null,
        CancellationToken ct = default)
    {
        var report = await new CatalogSyncService(cacheRoot, fetcher ?? new HttpCatalogFetcher()).SyncAsync(ct);
        return report;
    }

    /// <summary>Legacy compatibility shim: makes sure the English snapshot exists locally.</summary>
    public static (string Skins, string MusicKits) EnsureCached(string cacheRoot)
    {
        var report = SyncAsync(cacheRoot, new HttpCatalogFetcher()).GetAwaiter().GetResult();
        var enDir = report.LocaleDirs[IdentityLocale]
                    ?? throw new CatalogCacheException($"English catalog sync failed: {report.FailedMessages}");
        return (Path.Combine(enDir, "skins.json"), Path.Combine(enDir, "music_kits.json"));
    }

    /// <summary>
    /// Loads the locally cached pinned snapshot (default: E:\CS2MOD\app-data\cosmetics-lab\catalog\&lt;commit&gt;).
    /// Never fetches from the network. Throws CatalogCacheException when the cache is absent -
    /// callers that need catalog validation must fail closed, not skip validation.
    /// </summary>
    public static CatalogIndex LoadCachedIndex(string? cacheRoot = null)
    {
        var root = cacheRoot ?? CatalogSnapshot.DefaultCacheRoot(CorePaths.Cs2ModRoot);
        if (ResolveLocaleDir(root, IdentityLocale) is null)
            throw new CatalogCacheException(
                $"Pinned catalog cache not found under {root} (expected {IdentityLocale}/skins.json, or the " +
                "legacy skins.json + music_kits.json at the cache root). Run the catalog sync to prepare it.");
        if (!File.Exists(Path.Combine(ResolveLocaleDir(root, IdentityLocale)!, "music_kits.json")))
            throw new CatalogCacheException(
                $"Pinned catalog cache under {root} is missing music_kits.json. Run the catalog sync to prepare it.");
        return CatalogIndex.Load(root);
    }
}

public sealed class CatalogCacheException : Exception
{
    public CatalogCacheException(string message) : base(message) { }
}

/// <summary>
/// Fetch abstraction so catalog preparation is testable without a real network, while the
/// product path keeps using one pinned HTTPS source.
/// </summary>
public interface ICatalogFetcher
{
    Task<byte[]> FetchAsync(string url, CancellationToken ct);
}

public sealed class HttpCatalogFetcher : ICatalogFetcher
{
    private readonly HttpClient _http;

    public HttpCatalogFetcher(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<byte[]> FetchAsync(string url, CancellationToken ct) => await _http.GetByteArrayAsync(url, ct);
}

/// <summary>
/// Explicit catalog preparation workflow: downloads the pinned snapshot per locale, reuses the
/// legacy English cache, and reports what is present so the UI can tell the user whether the
/// localized metadata is available.
/// </summary>
public sealed class CatalogSyncService
{
    private readonly string _cacheRoot;
    private readonly ICatalogFetcher _fetcher;

    public CatalogSyncService(string cacheRoot, ICatalogFetcher? fetcher = null)
    {
        _cacheRoot = cacheRoot;
        _fetcher = fetcher ?? new HttpCatalogFetcher();
    }

    public async Task<CatalogSyncReport> SyncAsync(CancellationToken ct = default, IEnumerable<string>? locales = null)
    {
        Directory.CreateDirectory(_cacheRoot);
        var report = new CatalogSyncReport(_cacheRoot);

        foreach (var locale in locales ?? CatalogSnapshot.Locales)
        {
            var targetDir = CatalogSnapshot.LocaleDir(_cacheRoot, locale);
            Directory.CreateDirectory(targetDir);

            // English cache written by pre-C4.2 builds is the same pinned content: relocate it
            // into the locale layout instead of re-downloading.
            if (locale == CatalogSnapshot.IdentityLocale && !File.Exists(Path.Combine(targetDir, "skins.json")))
            {
                foreach (var file in new[] { "skins.json", "music_kits.json" })
                {
                    var legacy = Path.Combine(_cacheRoot, file);
                    if (File.Exists(legacy))
                    {
                        File.Copy(legacy, Path.Combine(targetDir, file), overwrite: false);
                        report.Add(file, "migrated-legacy-cache");
                    }
                }
            }

            foreach (var (file, remoteName) in new[] { ("skins.json", "skins.json"), ("music_kits.json", "music_kits.json") })
            {
                var path = Path.Combine(targetDir, file);
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    report.Add(file, "already-cached", path);
                    continue;
                }
                try
                {
                    var bytes = await _fetcher.FetchAsync($"{CatalogSnapshot.RawBaseFor(locale)}/{remoteName}", ct);
                    File.WriteAllBytes(path, bytes);
                    report.Add(file, "downloaded", path);
                }
                catch (Exception ex)
                {
                    report.AddFailure(locale, file, ex.Message);
                }
            }

            if (report.Ok(locale)) report.AddLocaleDir(locale, targetDir);
        }

        return report;
    }

    /// <summary>Locales present on disk right now, without downloading anything.</summary>
    public static CatalogCacheStatus Inspect(string cacheRoot)
    {
        var present = new List<string>();
        var missing = new List<string>();
        foreach (var locale in CatalogSnapshot.Locales)
        {
            if (CatalogSnapshot.IsLocaleCached(cacheRoot, locale)) present.Add(locale);
            else missing.Add(locale);
        }
        var musicCached = CatalogSnapshot.ResolveLocaleDir(cacheRoot, CatalogSnapshot.IdentityLocale) is { } enDir
                          && File.Exists(Path.Combine(enDir, "music_kits.json"));
        return new CatalogCacheStatus(cacheRoot, present, missing, musicCached);
    }
}

public sealed record CatalogCacheStatus(string CacheRoot, IReadOnlyList<string> LocalesPresent,
    IReadOnlyList<string> LocalesMissing, bool MusicKitsCached);

public sealed class CatalogSyncReport
{
    private readonly Dictionary<string, string> _dirs = new();
    private readonly List<string> _messages = new();
    private readonly HashSet<(string locale, string file)> _failed = new();
    private readonly List<string> _okLocales = new();

    public CatalogSyncReport(string cacheRoot) => CacheRoot = cacheRoot;

    public string CacheRoot { get; }
    public IReadOnlyDictionary<string, string> LocaleDirs => _dirs;
    public string FailedMessages => string.Join("; ", _messages);
    public IReadOnlyList<string> Messages => _messages;
    public IReadOnlyList<string> LocalesReady => _okLocales;

    public void Add(string file, string status, string? path = null) => _messages.Add($"{file}: {status}{(path is null ? "" : " -> " + path)}");

    public void AddFailure(string locale, string file, string error)
    {
        _failed.Add((locale, file));
        _messages.Add($"{locale}/{file} failed: {error}");
    }

    public bool Ok(string locale) => !_failed.Any(f => f.locale == locale);

    public void AddLocaleDir(string locale, string dir)
    {
        _dirs[locale] = dir;
        _okLocales.Add(locale);
    }

    public bool IdentityReady => _dirs.ContainsKey(CatalogSnapshot.IdentityLocale);
}
