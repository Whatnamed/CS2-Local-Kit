using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2LocalKit.App.Services;

namespace CS2LocalKit.App.Common;

/// <summary>
/// What an <see cref="Image"/> operated on by <see cref="LazyImage"/> is currently able to show.
/// The distinction matters to the viewer: waiting for art, having none, and failing to fetch it are
/// three different facts, and only the last two may be presented as "no preview".
/// </summary>
public enum ImageArtState
{
    /// <summary>Detached, or nothing was ever requested for this element.</summary>
    Idle,

    /// <summary>An attempt for this element's current URL is running.</summary>
    Loading,

    /// <summary>Source is the art for the current URL.</summary>
    Loaded,

    /// <summary>The current URL has no art: no URL at all, or it could not be fetched or decoded.</summary>
    Unavailable,
}

/// <summary>
/// Attached properties that make an <see cref="Image"/> fetch its art lazily: only realized
/// (scrolled-into-view) elements request bytes, and leaving the view cancels the pending request.
///
/// The element's art is bound to a request identity (URL + decode width). When that identity
/// changes, the previous result loses the right to touch the element and <see cref="System.Windows.Controls.Image.Source"/>
/// is cleared immediately, so a card can never keep showing the art of the selection it no longer
/// represents. A null, unreachable or undecodable URL leaves Source null and reports
/// <see cref="ImageArtState.Unavailable"/>, so the template's placeholder shows.
/// </summary>
public static class LazyImage
{
    /// <summary>Extra attempts a single selection gets after its art could not be fetched.</summary>
    private const int MaxRetries = 2;

    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(LazyImage), new PropertyMetadata(null, OnRequested));

    public static readonly DependencyProperty PixelWidthProperty = DependencyProperty.RegisterAttached(
        "PixelWidth", typeof(int), typeof(LazyImage), new PropertyMetadata(232, OnRequested));

    public static readonly DependencyProperty ArtStateProperty = DependencyProperty.RegisterAttached(
        "ArtState", typeof(ImageArtState), typeof(LazyImage), new PropertyMetadata(ImageArtState.Idle));

    private static readonly ConditionalWeakTable<Image, Slot> Slots = new();

    public static string? GetUrl(DependencyObject element) => (string?)element.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject element, string? value) => element.SetValue(UrlProperty, value);

    public static int GetPixelWidth(DependencyObject element) => (int)element.GetValue(PixelWidthProperty);
    public static void SetPixelWidth(DependencyObject element, int value) => element.SetValue(PixelWidthProperty, value);

    public static ImageArtState GetArtState(DependencyObject element) => (ImageArtState)element.GetValue(ArtStateProperty);

    private static void SetArtState(Image image, ImageArtState state)
    {
        if (GetArtState(image) != state) image.SetValue(ArtStateProperty, state);
    }

    private static void OnRequested(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        var slot = Slots.GetValue(image, _ => new Slot());
        if (!slot.Hooked)
        {
            // WPF raises Loaded again for an element it merely re-attaches, so a second Loaded must
            // not invalidate the fetch already running for the same URL: bumping the revision here is
            // what used to throw away a decoded image and leave the element blank until something else
            // changed the URL. Only a real detach may discard an in-flight result.
            image.Loaded += (_, _) => Request(image, slot);
            image.Unloaded += (_, _) => Detach(image, slot);
            slot.Hooked = true;
        }

        if (image.IsLoaded) Request(image, slot);
        else image.Source = null;
    }

    private static void Detach(Image image, Slot slot)
    {
        slot.Generation++;
        slot.Cancellation?.Cancel();
        slot.Cancellation = null;
        slot.Running = false;
        slot.HasResult = false;
        SetArtState(image, ImageArtState.Idle);
    }

    /// <summary>
    /// WPF raises Loaded again for an element that is merely re-parented or re-measured, and the
    /// binding can be re-applied with the same URL. Restarting the fetch there would cancel the
    /// download that is already in progress, so an identical request that is already running or
    /// already settled is left alone. A changed URL or width is a different selection and always
    /// discards the previous art.
    /// </summary>
    private static void Request(Image image, Slot slot)
    {
        var url = GetUrl(image);
        var width = GetPixelWidth(image);
        var identityChanged = slot.DesiredUrl != url || slot.DesiredWidth != width;

        if (!identityChanged && (slot.Running || slot.HasResult)) return;

        if (identityChanged)
        {
            slot.Generation++;
            slot.DesiredUrl = url;
            slot.DesiredWidth = width;
            slot.Cancellation?.Cancel();
            slot.Cancellation = null;
            slot.Running = false;
            slot.HasResult = false;
            slot.RetriesUsed = 0;

            // The old art describes the old selection. Clearing it here is what stops "new name,
            // previous picture" while the new fetch is queued, slow, or already known to have failed.
            image.Source = null;
        }

        slot.Running = true;
        SetArtState(image, string.IsNullOrWhiteSpace(url) ? ImageArtState.Unavailable : ImageArtState.Loading);
        _ = ApplyAsync(image, slot);
    }

    private static async Task ApplyAsync(Image image, Slot slot)
    {
        var generation = slot.Generation;
        var url = slot.DesiredUrl;
        var width = slot.DesiredWidth;
        var cts = new CancellationTokenSource();
        slot.Cancellation = cts;

        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetArtState(image, ImageArtState.Unavailable);
                return;
            }

            var provider = ImageSourceProvider.Current;
            if (provider is null)
            {
                SetArtState(image, ImageArtState.Unavailable);
                return;
            }

            ImageSource? source = null;
            try
            {
                source = await provider.GetAsync(url, width, cts.Token);
            }
            catch (Exception)
            {
                source = null;   // image availability is never allowed to surface in the UI
            }

            if (cts.IsCancellationRequested || generation != slot.Generation) return;
            // A superseded request may not relabel this element: whoever owns the current generation
            // decides what the viewer sees, otherwise a late A could overwrite a fresh C.

            if (source is null && TryAgain(image, slot)) return;   // the retry owns the element now

            image.Source = source;
            slot.HasResult = source is not null;
            SetArtState(image, source is not null ? ImageArtState.Loaded : ImageArtState.Unavailable);
        }
        finally
        {
            if (ReferenceEquals(slot.Cancellation, cts)) slot.Running = false;
        }
    }

    /// <summary>
    /// Schedules the next allowed attempt for the current selection and reports whether it was
    /// scheduled. A CDN endpoint that was unreachable a moment ago is not a fact about this skin, so
    /// an element that is still on screen gets another try once the cache is willing to ask for that
    /// URL again. The budget is fixed per selection, so this is a recovery path and not a standing
    /// timer or a retry storm.
    /// </summary>
    private static bool TryAgain(Image image, Slot slot)
    {
        if (slot.RetriesUsed >= MaxRetries) return false;
        slot.RetriesUsed++;

        var retry = new CancellationTokenSource();
        slot.Cancellation = retry;

        // Still "running": the element owns a pending attempt, so a re-raised Loaded must not start a
        // competing fetch while the cooldown is elapsing.
        slot.Running = true;
        _ = RetryAsync(image, slot, slot.Generation, retry);
        return true;
    }

    private static async Task RetryAsync(Image image, Slot slot, long generation, CancellationTokenSource retry)
    {
        try
        {
            await Task.Delay(ImageCacheService.FailureCooldown, retry.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (retry.IsCancellationRequested || generation != slot.Generation || !image.IsLoaded) return;
        slot.Running = false;
        Request(image, slot);
    }

    private sealed class Slot
    {
        public bool Hooked;
        public long Generation;
        public bool Running;
        public bool HasResult;
        public int RetriesUsed;
        public string? DesiredUrl;
        public int DesiredWidth;
        public CancellationTokenSource? Cancellation;
    }
}
