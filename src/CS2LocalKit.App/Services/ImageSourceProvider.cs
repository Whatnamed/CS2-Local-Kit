using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CS2LocalKit.App.Services;

/// <summary>
/// Turns cached image bytes into frozen WPF bitmaps. Decoding runs outside the UI thread and a
/// damaged or unsupported payload simply means "no image"; nothing here can fail a save or an
/// apply because the preset path never consults it.
/// </summary>
public sealed class ImageSourceProvider
{
    /// <summary>Set once at startup so view-layer code can resolve art without a DI container.</summary>
    public static ImageSourceProvider? Current { get; set; }

    private const int DecodedCacheLimit = 900;
    private readonly ImageCacheService _cache;
    private readonly ConcurrentDictionary<string, ImageSource?> _decoded = new(StringComparer.Ordinal);

    public ImageSourceProvider(ImageCacheService cache) => _cache = cache;

    public ImageCacheService Cache => _cache;

    public async Task<ImageSource?> GetAsync(string? requestUrl, int decodePixelWidth, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestUrl)) return null;
        var key = requestUrl + "@" + decodePixelWidth;
        if (_decoded.TryGetValue(key, out var cached) && cached is not null) return cached;

        ImageCacheEntry? entry;
        try
        {
            entry = await _cache.GetAsync(requestUrl, ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        if (entry is null || ct.IsCancellationRequested) return null;

        ImageSource? source;
        try
        {
            source = await Task.Run(() => Decode(entry.LocalPath, decodePixelWidth), CancellationToken.None);
        }
        catch (Exception)
        {
            source = null;
        }

        if (source is null)
        {
            // A file that will not decode is not a usable cache entry. Evict exactly this URL so the
            // next request re-fetches instead of hitting the same broken bytes on every scroll.
            _cache.Invalidate(entry.SourceUrl);
            return null;
        }

        if (_decoded.Count >= DecodedCacheLimit) _decoded.Clear();
        _decoded[key] = source;
        return source;
    }

    /// <summary>
    /// Content-sniffing decode: the pinned catalog URLs carry no file extension, and a wrong or
    /// truncated payload must never reach the view as an exception.
    /// </summary>
    public static ImageSource? Decode(string localPath, int decodePixelWidth)
    {
        try
        {
            var bytes = File.ReadAllBytes(localPath);
            if (bytes.Length == 0) return null;
            using var stream = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
