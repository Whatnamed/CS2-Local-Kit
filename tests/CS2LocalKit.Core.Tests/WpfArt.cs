using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CS2LocalKit.App.Services;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Shared plumbing for the tests that have to watch a real WPF <see cref="Image"/> go through the
/// art pipeline. Nothing here is a helper for asserting: it exists so the lifecycle tests exercise
/// the same Loaded/Unloaded and dispatcher behaviour the running app has, rather than strings.
/// </summary>
internal static class WpfArt
{
    /// <summary>
    /// The view layer resolves <see cref="ImageSourceProvider.Current"/> globally, so every class that
    /// installs a fake provider has to take turns with it.
    /// </summary>
    public const string GlobalArt = "global-art-provider";

    /// <summary>Installs a provider over an injected handler, so no test can reach the internet.</summary>
    public static Scope Install(HttpMessageHandler handler, string root) => new(handler, root);

    internal sealed class Scope : IDisposable
    {
        private readonly ImageSourceProvider? _previous;
        private readonly ImageCacheService _cache;

        internal Scope(HttpMessageHandler handler, string root)
        {
            _previous = ImageSourceProvider.Current;
            _cache = new ImageCacheService(root, handler);
            ImageSourceProvider.Current = new ImageSourceProvider(_cache);
        }

        public void Dispose()
        {
            ImageSourceProvider.Current = _previous;
            _cache.Dispose();
        }
    }

    /// <summary>
    /// WPF elements need STA, and an image fetch that resumes on the captured context only finishes
    /// while that thread pumps its dispatcher - which is exactly the situation preview art runs in.
    /// </summary>
    public static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            try { action(); }
            catch (Exception ex) { failure = ex; }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    /// <summary>Runs the dispatcher until the condition holds, so awaited image work can finish.</summary>
    public static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        if (condition()) return;
        var deadline = DateTime.UtcNow + timeout;
        var frame = new DispatcherFrame();
        dispatcher.InvokeAsync(async () =>
        {
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            frame.Continue = false;
        });
        Dispatcher.PushFrame(frame);
    }

    public static void Pump(TimeSpan duration) => PumpUntil(() => false, duration);

    /// <summary>A real PNG of the given native size and flat colour, so a displayed bitmap can be identified.</summary>
    public static byte[] Png(int width, int height, Color? color = null)
    {
        var fill = color ?? Color.FromRgb(0x30, 0x60, 0x90);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = fill.B;
            pixels[i + 1] = fill.G;
            pixels[i + 2] = fill.R;
            pixels[i + 3] = 0xFF;
        }
        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    public static byte[] Png(int width, int height, byte red, byte green, byte blue)
        => Png(width, height, Color.FromRgb(red, green, blue));

    /// <summary>
    /// The colour actually on screen. Sizes cannot identify a displayed bitmap, because WPF decodes
    /// to the requested pixel width whether the payload was bigger or smaller than that.
    /// </summary>
    public static Color DisplayedColor(ImageSource? source)
    {
        Assert.NotNull(source);
        var converted = new FormatConvertedBitmap((BitmapSource)source!, PixelFormats.Bgra32, null, 0);
        var buffer = new byte[4];
        converted.CopyPixels(new Int32Rect(0, 0, 1, 1), buffer, 4, 0);
        return Color.FromRgb(buffer[2], buffer[1], buffer[0]);
    }

    public static Color Red => Color.FromRgb(0xE0, 0x20, 0x20);
    public static Color Green => Color.FromRgb(0x20, 0xC0, 0x40);
    public static Color Blue => Color.FromRgb(0x20, 0x40, 0xE0);

    /// <summary>
    /// Puts an element in a real, unseen window so WPF raises the genuine Loaded/Unloaded sequence
    /// and <see cref="FrameworkElement.IsLoaded"/> is true. Identity rules that only hold while the
    /// element is on screen cannot be tested any other way.
    /// </summary>
    public static HostedWindow Host(FrameworkElement element)
    {
        var panel = new StackPanel();
        panel.Children.Add(element);
        var window = new Window
        {
            Content = panel,
            Width = 320,
            Height = 320,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Opacity = 0,
            Left = -8000,
            Top = -8000,
        };
        window.Show();
        return new HostedWindow(window, panel, element);
    }

    internal sealed class HostedWindow : IDisposable
    {
        private readonly Window _window;
        private readonly StackPanel _panel;
        private readonly FrameworkElement _element;

        internal HostedWindow(Window window, StackPanel panel, FrameworkElement element)
        {
            _window = window;
            _panel = panel;
            _element = element;
        }

        public FrameworkElement Element => _element;

        public void WaitForLoaded() => PumpUntil(() => _element.IsLoaded, TimeSpan.FromSeconds(5));

        /// <summary>WPF raises Unloaded on the next layout pass, so detaching has to be pumped.</summary>
        public void WaitForUnloaded() => PumpUntil(() => !_element.IsLoaded, TimeSpan.FromSeconds(5));

        /// <summary>Takes the element out of the tree, which is what really raises Unloaded.</summary>
        public void Detach() => _panel.Children.Remove(_element);

        /// <summary>Puts it back, which is what really raises Loaded again.</summary>
        public void Reattach()
        {
            _panel.Children.Add(_element);
            WaitForLoaded();
        }

        public void Dispose() => _window.Close();
    }

    /// <summary>Exposes the re-attach lifecycle WPF drives internally, without needing a window.</summary>
    public sealed class TestImage : System.Windows.Controls.Image
    {
        public void RaiseReattach() => RaiseEvent(new RoutedEventArgs(LoadedEvent));
        public void RaiseDetach() => RaiseEvent(new RoutedEventArgs(UnloadedEvent));
    }

    /// <summary>One gated URL: the response is held until <see cref="Release"/>.</summary>
    public sealed class GateHandler : HttpMessageHandler
    {
        private readonly Task _gate;
        private readonly byte[] _payload;

        public GateHandler(Task gate, byte[] payload)
        {
            _gate = gate;
            _payload = payload;
        }

        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            await Wait(gate: _gate, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_payload) };
        }
    }

    /// <summary>
    /// A transport that honours cancellation, like the real handler does: a download nobody waits for
    /// any more has to stop, or the tests would prove nothing about releasing the shared slot.
    /// </summary>
    private static async Task Wait(Task gate, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            await gate;
            return;
        }
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = ct.Register(() => abandoned.TrySetResult());
        await Task.WhenAny(gate, abandoned.Task);
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Per-URL script: hold a response, serve it, or make the endpoint unreachable.</summary>
    public sealed class Scripted : HttpMessageHandler
    {
        private readonly Dictionary<string, TaskCompletionSource<byte[]?>> _byUrl = new(StringComparer.Ordinal);

        public Scripted(byte[] defaultPayload) => Default = defaultPayload;

        public byte[] Default { get; set; }
        public List<string> Requests { get; } = new();

        public void Hold(string url) => _byUrl[url] = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(string url) => Complete(url, Default);
        public void Serve(string url, byte[] payload) => Complete(url, payload);
        public void Refuse(string url) => Complete(url, null);

        private void Complete(string url, byte[]? payload)
        {
            if (!_byUrl.TryGetValue(url, out var tcs))
            {
                _byUrl[url] = tcs = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            tcs.TrySetResult(payload);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            lock (Requests) Requests.Add(url);
            if (_byUrl.TryGetValue(url, out var tcs))
            {
                await Wait(tcs.Task, ct);
                var payload = tcs.Task.Result;
                if (payload is null) throw new HttpRequestException("simulated unreachable endpoint");
                return Ok(payload);
            }
            return Ok(Default);
        }

        private static HttpResponseMessage Ok(byte[] payload)
            => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
    }
}
