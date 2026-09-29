using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CS2LocalKit.App.Services;

/// <summary>One persisted remote image: what it was requested as, where it came from, and its bytes.</summary>
public sealed record ImageCacheEntry(
    string SourceUrl,
    string ResolvedUrl,
    string LocalPath,
    string ContentSha256,
    long Bytes,
    DateTimeOffset FetchedAt);

/// <summary>
/// Persistent, lazy image cache for catalog preview art.
///
/// Images are presentation resources only: nothing here participates in catalog identity,
/// validation, preset save or apply, and every failure path resolves to "no image" instead of
/// an error. Downloads happen on demand, off the UI thread, with bounded concurrency, one
/// in-flight request per URL, and an explicit equivalent-host retry list for Valve's community
/// CDN because those endpoints serve identical content and individual hosts are frequently
/// unreachable for players. Once a file is on disk it is reused without touching the network.
/// </summary>
public sealed class ImageCacheService : IDisposable
{
    public const int DefaultMaxConcurrency = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ManifestFlushDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Valve/Steam CDN endpoints that serve the same /economy/image and /apps asset paths.
    /// The URL from the pinned catalog snapshot is always tried first.
    /// </summary>
    internal static readonly string[] EquivalentCdnHosts =
    [
        "community.akamai.steamstatic.com",
        "community.cloudflare.steamstatic.com",
        "community.steamstatic.com",
        "steamcommunity-a.akamaihd.net",
    ];

    private readonly string _root;
    private readonly string _imagesDir;
    private readonly string _manifestPath;
    private readonly HttpClient _proxied;
    private readonly HttpClient _direct;
    private int _preferDirect;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentDictionary<string, Task<ImageCacheEntry?>> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ImageCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _imageFiles = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private int _manifestDirty;
    private bool _disposed;

    public ImageCacheService(string cacheRoot, HttpMessageHandler? handler = null, int maxConcurrency = DefaultMaxConcurrency)
    {
        _root = cacheRoot;
        _imagesDir = Path.Combine(cacheRoot, "images");
        _manifestPath = Path.Combine(cacheRoot, "index.json");
        _gate = new SemaphoreSlim(Math.Max(1, maxConcurrency), Math.Max(1, maxConcurrency));
        // An injected handler is a test seam and stands in for both clients, so no code path in a
        // test can reach the public internet.
        if (handler is null)
        {
            _proxied = new HttpClient();
            // Public CDN art with no credentials: when the machine's system proxy cannot reach it,
            // a direct attempt is the fallback. The first proxy failure makes direct the default.
            _direct = new HttpClient(new HttpClientHandler { UseProxy = false });
        }
        else
        {
            _proxied = new HttpClient(handler, disposeHandler: true);
            _direct = new HttpClient(handler, disposeHandler: true);
        }
        foreach (var client in new[] { _proxied, _direct })
        {
            client.Timeout = RequestTimeout;
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "CS2LocalKit-Controller/1.0");
        }

        Directory.CreateDirectory(_imagesDir);
        LoadManifest();
        _ = ManifestFlushLoop(_lifetime.Token);
    }

    public string CacheRoot => _root;
    public string ImagesDirectory => _imagesDir;
    public string ManifestPath => _manifestPath;
    public int CachedImageCount => _entries.Count;
    public IEnumerable<ImageCacheEntry> Entries => _entries.Values;

    /// <summary>Stable, filesystem-safe cache key for a source URL.</summary>
    public static string KeyFor(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();

    public bool TryGetCached(string? url, out ImageCacheEntry entry)
    {
        entry = null!;
        if (url is null || !_entries.TryGetValue(url, out var found)) return false;
        if (!File.Exists(found.LocalPath)) return false;
        entry = found;
        return true;
    }

    /// <summary>
    /// Resolves a URL to a local file, downloading it when needed. Returns null for every
    /// non-success outcome (no URL, network failure, cancelled, empty or unusable payload).
    /// </summary>
    public Task<ImageCacheEntry?> GetAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !IsHttp(url)) return Task.FromResult<ImageCacheEntry?>(null);
        if (TryGetCached(url, out var cached)) return Task.FromResult<ImageCacheEntry?>(cached);
        if (ct.IsCancellationRequested) return Task.FromResult<ImageCacheEntry?>(null);

        // A concurrent request for the same URL shares one download.
        return _inFlight.GetOrAdd(url, u => DownloadTrackedAsync(u));
    }

    /// <summary>
    /// The shared download is bound to the cache lifetime, never to one caller's token: a card
    /// scrolled out of view must not cancel art that other cards - or the same card a moment
    /// later - still needs. Callers that lose interest simply stop awaiting.
    /// </summary>
    private async Task<ImageCacheEntry?> DownloadTrackedAsync(string url)
    {
        try
        {
            return await DownloadAsync(url, _lifetime.Token);
        }
        finally
        {
            _inFlight.TryRemove(url, out _);
        }
    }

    private async Task<ImageCacheEntry?> DownloadAsync(string url, CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            await _gate.WaitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;   // cache torn down while a view was still realizing
        }

        try
        {
            // Another request may have completed while this one waited for a slot.
            if (TryGetCached(url, out var existing)) return existing;

            var directFirst = Volatile.Read(ref _preferDirect) == 1;
            foreach (var candidate in CandidateUrls(url))
            {
                if (linked.Token.IsCancellationRequested) return null;
                var clients = directFirst ? new[] { _direct, _proxied } : new[] { _proxied, _direct };
                foreach (var client in clients)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = await client.GetByteArrayAsync(candidate, linked.Token);
                    }
                    catch (OperationCanceledException) when (!linked.Token.IsCancellationRequested)
                    {
                        continue;   // HttpClient timeout: treat as an unreachable source
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (Exception)
                    {
                        // Unreachable host or a proxy that cannot tunnel it: try direct, then the
                        // next equivalent CDN endpoint.
                        if (ReferenceEquals(client, _proxied)) Interlocked.Exchange(ref _preferDirect, 1);
                        continue;
                    }

                    if (bytes.Length < 12) break;   // empty body or an error page too small to be art
                    try
                    {
                        return await WriteAsync(url, candidate, bytes);
                    }
                    catch (Exception)
                    {
                        return null;   // an unwritable cache is a missing image, never an error
                    }
                }
            }
            return null;
        }
        finally
        {
            try
            {
                _gate.Release();
                linked.Dispose();
            }
            catch (ObjectDisposedException) { /* teardown */ }
        }
    }

    /// <summary>
    /// The catalog URL first, then the same path on Valve's equivalent CDN hosts when the source
    /// host belongs to that group. Never rewrites non-Steam hosts (for example the pinned
    /// counter-strike-image-tracker URLs on raw.githubusercontent.com).
    /// </summary>
    internal static IReadOnlyList<string> CandidateUrls(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return [url];
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !EquivalentCdnHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return [url];

        var result = new List<string> { url };
        foreach (var host in EquivalentCdnHosts)
        {
            if (string.Equals(host, uri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            var builder = new UriBuilder(uri) { Host = host };
            result.Add(builder.Uri.ToString());
        }
        return result;
    }

    private async Task<ImageCacheEntry> WriteAsync(string sourceUrl, string resolvedUrl, byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var fileName = KeyFor(sourceUrl) + ExtensionFor(bytes, resolvedUrl);
        var finalPath = Path.Combine(_imagesDir, fileName);

        if (!File.Exists(finalPath))
        {
            var tempPath = finalPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes);
                File.Move(tempPath, finalPath, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
                throw;
            }
        }

        var entry = new ImageCacheEntry(sourceUrl, resolvedUrl, finalPath, hash, bytes.Length, DateTimeOffset.UtcNow);
        _entries[sourceUrl] = entry;
        _imageFiles[hash] = finalPath;
        Interlocked.Exchange(ref _manifestDirty, 1);
        return entry;
    }

    /// <summary>Sniffs the real format so a wrong or missing URL extension never breaks decoding.</summary>
    internal static string ExtensionFor(byte[] bytes, string url)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes.Length >= 4 && bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return ".webp";
        var ext = Path.GetExtension(new Uri(url, UriKind.Absolute).AbsolutePath);
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase) ? ext : ".img";
    }

    private void LoadManifest()
    {
        if (!File.Exists(_manifestPath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_manifestPath));
            if (!doc.RootElement.TryGetProperty("images", out var images)) return;
            foreach (var item in images.EnumerateArray())
            {
                var source = Str(item, "sourceUrl");
                var local = Str(item, "localPath");
                if (source is null || local is null) continue;
                if (!File.Exists(local)) continue;
                _entries[source] = new ImageCacheEntry(
                    source,
                    Str(item, "resolvedUrl") ?? source,
                    local,
                    Str(item, "contentSha256") ?? "",
                    item.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var l) ? l : new FileInfo(local).Length,
                    item.TryGetProperty("fetchedAt", out var f) && DateTimeOffset.TryParse(f.GetString(), out var t) ? t : DateTimeOffset.UtcNow);
                _imageFiles[Str(item, "contentSha256") ?? ""] = local;
            }
        }
        catch
        {
            // A damaged manifest is presentation-only: fall back to re-downloading.
        }
    }

    private async Task ManifestFlushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ManifestFlushDelay, ct);
                if (Interlocked.Exchange(ref _manifestDirty, 0) == 1) WriteManifest();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void WriteManifest()
    {
        var payload = new
        {
            kind = "cs2-local-kit/image-cache",
            pinnedCatalogCommit = Core.Catalog.CatalogSnapshot.PinnedCommit,
            images = _entries.Values
                .OrderBy(e => e.SourceUrl, StringComparer.Ordinal)
                .Select(e => new
                {
                    sourceUrl = e.SourceUrl,
                    resolvedUrl = e.ResolvedUrl,
                    localPath = e.LocalPath,
                    contentSha256 = e.ContentSha256,
                    bytes = e.Bytes,
                    fetchedAt = e.FetchedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                })
                .ToList(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var tempPath = _manifestPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _manifestPath, overwrite: true);
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsHttp(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Interlocked.Exchange(ref _manifestDirty, 0) == 1) WriteManifest();
        }
        catch { /* never fail on teardown */ }
        _lifetime.Cancel();
        _lifetime.Dispose();
        _proxied.Dispose();
        _direct.Dispose();
        _gate.Dispose();
    }
}
