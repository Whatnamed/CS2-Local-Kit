using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CS2LocalKit.App.Services;

namespace CS2LocalKit.App.Common;

/// <summary>
/// Attached properties that make an <see cref="Image"/> fetch its art lazily: only realized
/// (scrolled-into-view) elements request bytes, and leaving the view cancels the pending request.
/// A null, unreachable or undecodable URL leaves Source null, so the template's placeholder shows.
/// </summary>
public static class LazyImage
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.RegisterAttached(
        "Url", typeof(string), typeof(LazyImage), new PropertyMetadata(null, OnRequested));

    public static readonly DependencyProperty PixelWidthProperty = DependencyProperty.RegisterAttached(
        "PixelWidth", typeof(int), typeof(LazyImage), new PropertyMetadata(232, OnRequested));

    private static readonly ConditionalWeakTable<Image, Slot> Slots = new();

    public static string? GetUrl(DependencyObject element) => (string?)element.GetValue(UrlProperty);
    public static void SetUrl(DependencyObject element, string? value) => element.SetValue(UrlProperty, value);

    public static int GetPixelWidth(DependencyObject element) => (int)element.GetValue(PixelWidthProperty);
    public static void SetPixelWidth(DependencyObject element, int value) => element.SetValue(PixelWidthProperty, value);

    private static void OnRequested(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        var slot = Slots.GetValue(image, _ => new Slot());
        if (!slot.Hooked)
        {
            image.Loaded += (_, _) => { slot.Revision++; Request(image, slot); };
            image.Unloaded += (_, _) => { slot.Revision++; slot.Cancellation?.Cancel(); };
            slot.Hooked = true;
        }

        if (image.IsLoaded) Request(image, slot);
        else image.Source = null;
    }

    /// <summary>
    /// WPF raises Loaded again for an element that is merely re-parented or re-measured, and the
    /// binding can be re-applied with the same URL. Restarting the fetch there would cancel the
    /// download that is already in progress, so an identical in-flight request is left alone.
    /// </summary>
    private static void Request(Image image, Slot slot)
    {
        var url = GetUrl(image);
        var width = GetPixelWidth(image);
        if (slot.Running && slot.Url == url && slot.Width == width) return;

        slot.Cancellation?.Cancel();
        slot.Url = url;
        slot.Width = width;
        slot.Running = true;
        _ = ApplyAsync(image, slot);
    }

    private static async Task ApplyAsync(Image image, Slot slot)
    {
        var revision = slot.Revision;
        var url = slot.Url;
        var cts = new CancellationTokenSource();
        slot.Cancellation = cts;

        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                image.Source = null;
                return;
            }

            var provider = ImageSourceProvider.Current;
            if (provider is null) return;

            ImageSource? source;
            try
            {
                source = await provider.GetAsync(url, slot.Width, cts.Token);
            }
            catch (Exception)
            {
                source = null;   // image availability is never allowed to surface in the UI
            }

            if (cts.IsCancellationRequested || revision != slot.Revision) return;
            image.Source = source;
        }
        finally
        {
            if (ReferenceEquals(slot.Cancellation, cts)) slot.Running = false;
        }
    }

    private sealed class Slot
    {
        public bool Hooked;
        public int Revision;
        public bool Running;
        public string? Url;
        public int Width;
        public CancellationTokenSource? Cancellation;
    }
}
