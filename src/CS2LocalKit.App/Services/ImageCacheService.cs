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
///
/// Two rules keep a real browsing session usable, both of them measured rather than assumed:
/// a download is only cancelled once no view still wants it, and the host plus transport that
/// last served a payload is tried first, because walking the whole fallback matrix costs tens
/// of seconds per image.
/// </summary>
public sealed class ImageCacheService : IDisposable
{
    public const int DefaultMaxConcurrency = 4;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ManifestFlushDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long one URL stays "known bad" after every candidate failed. Long enough that scrolling
    /// back and forth over an unreachable card cannot restart the matrix, short enough that a
    /// transient CDN outage recovers without restarting the app.
    /// </summary>
    internal static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

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

    /// <summary>A host and transport that served art before: the cheapest way to start the next download.</summary>
    internal sealed record CdnRoute(string Host, bool Proxied);

    private readonly string _root;
    private readonly string _imagesDir;
    private readonly string _manifestPath;
    private readonly HttpClient _proxied;
    private readonly HttpClient _direct;
    private volatile CdnRoute? _route;
    private readonly SemaphoreSlim _gate;
    private readonly ConcurrentDictionary<string, Pending> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ImageCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _imageFiles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _unavailableUntil = new(StringComparer.Ordinal);
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
            // Public CDN art with no credentials: when the machine's system proxy cannot reach it,
            // a direct attempt is the fallback. A doomed endpoint is given up on after a short
            // connect attempt instead of the full body timeout, because the fallback matrix is
            // traversed while holding one of the shared download slots.
            _proxied = new HttpClient(new SocketsHttpHandler { ConnectTimeout = ConnectTimeout });
            _direct = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = ConnectTimeout });
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
    public async Task<ImageCacheEntry?> GetAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url) || !IsHttp(url)) return null;
        if (TryGetCached(url, out var cached)) return cached;
        if (ct.IsCancellationRequested) return null;

        // A URL that just failed is answered from the cooldown rather than re-running the fallback
        // matrix, which is what keeps a failing card from turning browsing into a network storm.
        if (_unavailableUntil.TryGetValue(url, out var until) && until > DateTimeOffset.UtcNow) return null;

        Pending pending;
        Task<ImageCacheEntry?> completion;
        while (true)
        {
            pending = _inFlight.GetOrAdd(url, static u => new Pending(u));
            lock (pending)
            {
                // A concurrent request for the same URL shares one download.
                if (!pending.Retired)
                {
                    pending.Waiters++;
                    completion = pending.Completion ??= StartDownload(pending);
                    break;
                }
            }

            // Someone retired this instance while it was still in the map; claim a fresh one.
            _inFlight.TryRemove(new KeyValuePair<string, Pending>(url, pending));
        }

        try
        {
            return await completion.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Release(pending);
        }
    }

    /// <summary>
    /// The shared download belongs to the views that asked for it. Once the last one stops waiting -
    /// scrolled away, or the card was re-pointed at different art - the request is cancelled so its
    /// slot is returned immediately and the cards still on screen are not queued behind work nobody
    /// wants any more. A card that comes back later simply asks again.
    /// </summary>
    private void Release(Pending pending)
    {
        lock (pending)
        {
            if (--pending.Waiters > 0) return;
            pending.Retired = true;
            _inFlight.TryRemove(new KeyValuePair<string, Pending>(pending.Url, pending));
            if (!pending.CompletionIsSettled) pending.Cancel();
        }
    }

    private Task<ImageCacheEntry?> StartDownload(Pending pending)
    {
        // Deliberately not disposed: a waiter can still hold this instance, and cancelling a
        // CancellationTokenSource that someone is awaiting would surface as a decode miss.
        pending.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        return DownloadAsync(pending.Url, pending.Cancellation.Token);
    }

    private async Task<ImageCacheEntry?> DownloadAsync(string url, CancellationToken ct)
    {
        try
        {
            await _gate.WaitAsync(ct);
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

            foreach (var (candidate, proxied) in Attempts(url))
            {
                if (ct.IsCancellationRequested) return null;
                var client = proxied ? _proxied : _direct;
                byte[] bytes;
                try
                {
                    bytes = await client.GetByteArrayAsync(candidate, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    continue;   // HttpClient timeout: treat as an unreachable source
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception)
                {
                    continue;   // unreachable host, or a proxy that cannot tunnel it
                }

                // Only bytes WPF can actually decode become cache content. An error or challenge
                // page is a failed attempt, never a permanent cache hit that decodes to nothing.
                if (!IsDecodableImage(bytes)) continue;

                try
                {
                    var entry = await WriteAsync(url, candidate, bytes);
                    _route = new CdnRoute(new Uri(candidate).Host, proxied);
                    _unavailableUntil.TryRemove(url, out _);
                    return entry;
                }
                catch (Exception)
                {
                    return null;   // an unwritable cache is a missing image, never an error
                }
            }

            _unavailableUntil[url] = DateTimeOffset.UtcNow + FailureCooldown;
            return null;
        }
        finally
        {
            try
            {
                _gate.Release();
            }
            catch (ObjectDisposedException) { /* teardown */ }
        }
    }

    /// <summary>
    /// Every candidate host with both transports, bounded at one pass per pair. The host and
    /// transport that served the previous image lead the list: on a machine where three of the four
    /// Steam endpoints are unreachable, that is the difference between roughly two seconds per card
    /// and roughly twenty-five.
    /// </summary>
    internal IReadOnlyList<(string Url, bool Proxied)> Attempts(string url)
    {
        var candidates = CandidateUrls(url);
        var result = new List<(string, bool)>(candidates.Count * 2);
        var route = _route;

        if (route is { } known)
        {
            foreach (var candidate in candidates)
                if (string.Equals(new Uri(candidate).Host, known.Host, StringComparison.OrdinalIgnoreCase))
                    result.Add((candidate, known.Proxied));
        }

        foreach (var candidate in candidates)
        {
            var host = new Uri(candidate).Host;
            foreach (var proxied in new[] { true, false })
            {
                if (route is { } k
                    && string.Equals(host, k.Host, StringComparison.OrdinalIgnoreCase)
                    && proxied == k.Proxied) continue;
                result.Add((candidate, proxied));
            }
        }
        return result;
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

    /// <summary>
    /// Drop one entry after its bytes turned out to be undecodable, so the next request re-fetches
    /// instead of hitting the same broken file forever. Only that URL is touched; healthy entries,
    /// including other URLs stored under the same content hash, keep working without the network.
    /// </summary>
    public void Invalidate(string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl)) return;
        if (!_entries.TryRemove(sourceUrl, out var entry)) return;
        if (string.Equals(entry.ContentSha256, "", StringComparison.Ordinal)
            || !_imageFiles.TryRemove(entry.ContentSha256, out _)) { /* shared or unhashed payload */ }
        try
        {
            if (File.Exists(entry.LocalPath)) File.Delete(entry.LocalPath);
        }
        catch (IOException)
        {
            // A locked file stays on disk; the manifest entry is already gone, so it is never read back.
        }
        _unavailableUntil.TryRemove(sourceUrl, out _);
        Interlocked.Exchange(ref _manifestDirty, 1);
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

    /// <summary>
    /// The formats WPF can decode. A signature the decoder cannot handle must not be persisted,
    /// because a cache file that never yields a bitmap is worse than no cache file: every later
    /// request would hit it and give up.
    /// </summary>
    internal static bool IsDecodableImage(byte[] bytes)
        => bytes.Length >= 12
           && (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47   // PNG
               || bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF                    // JPEG
               || bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46                    // GIF
               || bytes[0] == 0x42 && bytes[1] == 0x4D);                                     // BMP

    /// <summary>Sniffs the real format so a wrong or missing URL extension never breaks decoding.</summary>
    internal static string ExtensionFor(byte[] bytes, string url)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
        if (bytes.Length >= 4 && bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
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
            // The endpoint that served this machine last time is remembered because discovering it
            // costs tens of seconds: most of Valve's equivalent hosts are simply unreachable here.
            var host = Str(doc.RootElement, "preferredCdnHost");
            if (host is not null)
                _route = new CdnRoute(host, doc.RootElement.TryGetProperty("preferredCdnProxied", out var p)
                                           && p.ValueKind == JsonValueKind.True);
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
            preferredCdnHost = _route?.Host,
            preferredCdnProxied = _route?.Proxied,
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

    /// <summary>One shared download, plus the views still waiting for it.</summary>
    private sealed class Pending
    {
        public Pending(string url) => Url = url;

        public string Url { get; }
        public CancellationTokenSource? Cancellation;
        public Task<ImageCacheEntry?>? Completion;
        public int Waiters;

        /// <summary>Set once the last waiter left: nobody may join this download any more.</summary>
        public bool Retired;

        public bool CompletionIsSettled => Completion is { IsCompleted: true };

        public void Cancel() => Cancellation?.Cancel();
    }

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
