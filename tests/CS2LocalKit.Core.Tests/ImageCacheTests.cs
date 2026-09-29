using System.Net;
using System.Net.Http;
using System.Text.Json;
using CS2LocalKit.App.Services;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Art is a presentation resource, so the cache has exactly one job: hand back bytes when they are
/// on disk, fetch them once when they are not, and turn every failure into "no image".
/// All traffic here goes through an injected handler - no test reaches the internet.
/// </summary>
public sealed class ImageCacheTests : IDisposable
{
    private readonly string _root;

    public ImageCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cs2localkit-img-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    private static string Url(string tail) => "https://art.example.invalid/economy/image/" + tail;

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;

        public FakeHandler(Func<string, HttpResponseMessage> respond) => _respond = respond;

        public List<string> Requests { get; } = new();
        public TimeSpan Delay { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, CancellationToken.None);
            return _respond(url);
        }
    }

    private static HttpResponseMessage Ok(byte[] payload)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    private ImageCacheService NewService(FakeHandler handler, int maxConcurrency = ImageCacheService.DefaultMaxConcurrency)
        => new(_root, handler, maxConcurrency);

    [Fact]
    public void KeyFor_IsLowercaseSha256OfTheUrl()
    {
        var url = Url("abc");
        var key = ImageCacheService.KeyFor(url);

        Assert.Equal(64, key.Length);
        Assert.Equal(key, key.ToLowerInvariant());
        Assert.NotEqual(key, ImageCacheService.KeyFor(Url("abd")));
    }

    [Fact]
    public async Task CacheMissDownloadsOnceAndSniffsTheExtension()
    {
        var handler = new FakeHandler(url => url.EndsWith("paint-2") ? Ok(JpegBytes) : Ok(PngBytes));
        using var service = NewService(handler);

        var entry = await service.GetAsync(Url("paint-1"));

        Assert.NotNull(entry);
        Assert.Single(handler.Requests);
        Assert.EndsWith(".png", entry!.LocalPath);
        Assert.True(File.Exists(entry.LocalPath));
        Assert.Equal(entry.SourceUrl, Url("paint-1"));
        Assert.Equal(PngBytes.LongLength, entry.Bytes);

        var jpg = await service.GetAsync(Url("paint-2"), CancellationToken.None);
        Assert.NotNull(jpg);
        Assert.EndsWith(".jpg", jpg!.LocalPath);
    }

    [Fact]
    public async Task CacheHitNeverTouchesTheNetworkAgain()
    {
        var url = Url("paint-3");
        var first = new FakeHandler(_ => Ok(PngBytes));
        using (var service = NewService(first))
        {
            Assert.NotNull(await service.GetAsync(url));
        }

        var second = new FakeHandler(_ => Ok(PngBytes));
        using (var reopened = NewService(second))
        {
            Assert.True(reopened.TryGetCached(url, out var cached));
            Assert.NotNull(await reopened.GetAsync(url));
            Assert.Equal(url, cached.SourceUrl);
        }

        Assert.Single(first.Requests);
        Assert.Empty(second.Requests);   // the manifest on disk is enough
    }

    [Fact]
    public async Task ConcurrentRequestsForOneUrlShareASingleDownload()
    {
        var handler = new FakeHandler(_ => Ok(PngBytes)) { Delay = TimeSpan.FromMilliseconds(120) };
        using var service = NewService(handler);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => service.GetAsync(Url("shared"))));

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OneCallerCancellingDoesNotCancelADownloadAnotherStillWants()
    {
        var url = Url("contested");
        var handler = new FakeHandler(_ => Ok(PngBytes)) { Delay = TimeSpan.FromMilliseconds(150) };
        using var service = NewService(handler);

        using var abandoning = new CancellationTokenSource();
        var gone = service.GetAsync(url, abandoning.Token);
        var staying = service.GetAsync(url);   // joins the same download before the first one leaves
        abandoning.Cancel();

        var result = await staying;
        Assert.NotNull(result);
        Assert.Null(await gone);
        Assert.Single(handler.Requests);
        Assert.True(File.Exists(result!.LocalPath));
    }

    [Fact]
    public async Task AbandoningTheLastWaiterReleasesTheUrlRatherThanHoldingTheQueue()
    {
        // The point of cancelling for want of a waiter: a card that scrolled away must not keep a
        // shared download slot, or the cards that are actually on screen wait behind it.
        var url = Url("abandoned");
        var handler = new FakeHandler(_ => Ok(PngBytes)) { Delay = TimeSpan.FromMilliseconds(150) };
        using var service = NewService(handler);

        using var only = new CancellationTokenSource();
        var gone = service.GetAsync(url, only.Token);
        only.Cancel();
        Assert.Null(await gone);

        var retried = await service.GetAsync(url);
        Assert.NotNull(retried);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(File.Exists(retried!.LocalPath));
    }

    [Fact]
    public async Task HttpFailureBecomesNoImageNotAnError()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("unreachable"));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(Url("missing")));
        Assert.Equal(0, service.CachedImageCount);
    }

    [Fact]
    public async Task StatusCodeErrorsAndTinyPayloadsAreNotCached()
    {
        var handler = new FakeHandler(url => url.EndsWith("not-found")
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]) }
            : Ok([1, 2, 3]));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(Url("not-found")));
        Assert.Null(await service.GetAsync(Url("tiny")));
        Assert.Equal(0, service.CachedImageCount);
    }

    [Fact]
    public async Task NonHttpAndBlankUrlsNeverReachTheNetwork()
    {
        var handler = new FakeHandler(_ => Ok(PngBytes));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(null));
        Assert.Null(await service.GetAsync("   "));
        Assert.Null(await service.GetAsync("file:///C:/Windows/explorer.exe"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AlreadyCancelledRequestDoesNotStartADownload()
    {
        var handler = new FakeHandler(_ => Ok(PngBytes));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(Url("late"), new CancellationToken(canceled: true)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SteamCdnUrlsFallBackToAnEquivalentHost()
    {
        const string source = "https://community.akamai.steamstatic.com/economy/image/abc/232x176";
        var handler = new FakeHandler(url => url.Contains("community.akamai.")
            ? throw new HttpRequestException("that edge is down")
            : Ok(PngBytes));
        using var service = NewService(handler);

        var entry = await service.GetAsync(source);

        Assert.NotNull(entry);
        Assert.NotEqual(source, entry!.ResolvedUrl);
        Assert.Equal(source, entry.SourceUrl);   // the cache key stays the catalog's own URL
    }

    [Fact]
    public async Task UndecodableBytesAreHandledAsNoImage()
    {
        var url = Url("corrupt");
        var handler = new FakeHandler(_ => Ok(PngBytes));   // a PNG magic header with no real image
        using var service = NewService(handler);
        var provider = new ImageSourceProvider(service);

        Assert.NotNull(await service.GetAsync(url));
        Assert.Null(await provider.GetAsync(url, 232));
        Assert.Null(ImageSourceProvider.Decode(Path.Combine(_root, "does-not-exist.png"), 232));
    }

    [Fact]
    public async Task BytesThatWillNotDecodeAreNotLeftAsAPermanentCacheHit()
    {
        // The failure the user would otherwise live with forever: a bad file that decodes to nothing
        // on every scroll, because the manifest still claims the URL is cached.
        var url = Url("poisoned");
        var handler = new FakeHandler(_ => Ok(PngBytes));
        using var service = NewService(handler);
        var provider = new ImageSourceProvider(service);

        var poisoned = await service.GetAsync(url);
        Assert.NotNull(poisoned);
        Assert.Null(await provider.GetAsync(url, 232));

        Assert.False(service.TryGetCached(url, out _));
        Assert.False(File.Exists(poisoned!.LocalPath));
        Assert.Equal(0, service.CachedImageCount);

        Assert.NotNull(await service.GetAsync(url));
        Assert.Equal(2, handler.Requests.Count);   // it asked again rather than re-reading the bad file
    }

    [Fact]
    public async Task EvictingOneUrlLeavesItsNeighboursCached()
    {
        var handler = new FakeHandler(_ => Ok(PngBytes));
        using var service = NewService(handler);
        var keep = Url("keep");
        var drop = Url("drop");
        await service.GetAsync(keep);
        await service.GetAsync(drop);

        service.Invalidate(drop);

        Assert.False(service.TryGetCached(drop, out _));
        Assert.True(service.TryGetCached(keep, out _));
        Assert.NotNull(await service.GetAsync(keep));
        Assert.Single(handler.Requests, r => r == keep);
    }

    [Fact]
    public async Task AnErrorPageIsNeverStoredAsArt()
    {
        var url = Url("challenge");
        var payload = System.Text.Encoding.UTF8.GetBytes("<html><body>Too Many Requests</body></html>");
        var handler = new FakeHandler(_ => Ok(payload));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(url));
        Assert.Equal(0, service.CachedImageCount);
        Assert.Empty(Directory.GetFiles(service.ImagesDirectory));

        // A healthy neighbour is unaffected: refusing to store a bad payload is not a blanket stop.
        payload = PngBytes;
        Assert.NotNull(await service.GetAsync(Url("healthy-neighbour")));
        Assert.Equal(1, service.CachedImageCount);
    }

    [Fact]
    public async Task OneTransientFailureDoesNotKeepReRunningTheWholeFallbackMatrix()
    {
        var url = Url("flaky");
        var handler = new FakeHandler(_ => throw new HttpRequestException("unreachable"));
        using var service = NewService(handler);

        Assert.Null(await service.GetAsync(url));
        var afterFailure = handler.Requests.Count;

        Assert.Null(await service.GetAsync(url));
        Assert.Equal(afterFailure, handler.Requests.Count);
    }

    [Fact]
    public async Task TheEndpointThatServedArtIsTheNextOneTried()
    {
        const string first = "https://community.akamai.steamstatic.com/economy/image/one/232x176";
        const string second = "https://community.akamai.steamstatic.com/economy/image/two/232x176";
        var handler = new FakeHandler(url => url.Contains("community.")
            ? throw new HttpRequestException("that edge is down")
            : Ok(PngBytes));
        using var service = NewService(handler);

        Assert.NotNull(await service.GetAsync(first));
        var discoveryCost = handler.Requests.Count;
        Assert.True(discoveryCost > 1);

        Assert.NotNull(await service.GetAsync(second));

        // Every image after the first starts at the endpoint that already worked, so a player whose
        // three other Steam hosts are unreachable does not pay the discovery walk per card.
        Assert.Equal(discoveryCost + 1, handler.Requests.Count);
        Assert.EndsWith("steamcommunity-a.akamaihd.net/economy/image/two/232x176", handler.Requests[^1]);
    }

    [Fact]
    public async Task ACancelledDownloadDoesNotHoldTheQueueAwayFromAVisibleCard()
    {
        var scripted = new WpfArt.Scripted(PngBytes);
        scripted.Hold(Url("scrolled-past"));   // only this one endpoint stalls
        using var service = new ImageCacheService(_root, scripted, maxConcurrency: 1);

        using var abandoning = new CancellationTokenSource();
        var offscreen = service.GetAsync(Url("scrolled-past"), abandoning.Token);
        await Task.Delay(50);   // it has the only slot
        abandoning.Cancel();
        Assert.Null(await offscreen);

        var visible = await service.GetAsync(Url("on-screen")).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(visible);
        scripted.Release(Url("scrolled-past"));
    }

    [Fact]
    public async Task ManifestStaysInsideTheCacheRootAndCarriesNoPlayerIdentity()
    {
        var handler = new FakeHandler(_ => Ok(PngBytes));
        using (var service = NewService(handler))
        {
            await service.GetAsync(Url("manifest-check"));
            service.WriteManifest();
        }

        var path = Path.Combine(_root, "index.json");
        Assert.True(File.Exists(path));
        Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(path));

        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        var image = doc.RootElement.GetProperty("images")[0];

        Assert.Equal(
            new[] { "bytes", "contentSha256", "fetchedAt", "localPath", "resolvedUrl", "sourceUrl" },
            image.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(TestFixtures.FakeSteamId64, text);
        Assert.DoesNotContain("steamId", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("cs2-local-kit/image-cache", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void DamagedManifestDegradesToAnEmptyCache()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.json"), "{ this is not json ");

        var handler = new FakeHandler(_ => Ok(PngBytes));
        using var service = NewService(handler);

        Assert.Equal(0, service.CachedImageCount);
    }
}
