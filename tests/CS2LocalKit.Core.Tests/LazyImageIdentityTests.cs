using System.Windows.Controls;
using System.Windows.Media;
using CS2LocalKit.App.Common;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Preview art belongs to the selection that asked for it. These tests watch real WPF image elements
/// through the fetch pipeline: the reported defect was that the name and the selection moved to a new
/// skin while the picture stayed on the old one, and that only shows up in the Image.Source lifecycle.
/// </summary>
[Collection(WpfArt.GlobalArt)]
public sealed class LazyImageIdentityTests : IDisposable
{
    private readonly string _root;

    public LazyImageIdentityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cs2localkit-art-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private static string Art(string tail) => "https://art.example.invalid/economy/image/" + tail + "/512x384";

    private static byte[] Artwork(Color color) => WpfArt.Png(64, 48, color);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void SelectingAnotherSkinRemovesThePreviousArtImmediately()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Serve(Art("asiimov"), Artwork(WpfArt.Green));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("x-ray"));
            host.WaitForLoaded();

            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(WpfArt.Red, WpfArt.DisplayedColor(image.Source));
            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));

            LazyImage.SetUrl(image, Art("asiimov"));

            // No pumping: the previous selection's art has to be gone the instant this element stops
            // representing it, not when the next one happens to arrive.
            Assert.Null(image.Source);
            Assert.Equal(ImageArtState.Loading, LazyImage.GetArtState(image));

            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(WpfArt.Green, WpfArt.DisplayedColor(image.Source));
        });
    }

    [Fact]
    public void SlowArtForThePreviousSelectionCannotLandOnTheNewOne()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Hold(Art("x-ray"));
            scripted.Serve(Art("wild-lotus"), Artwork(WpfArt.Green));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("x-ray"));
            host.WaitForLoaded();
            Assert.Null(image.Source);   // still queued behind the held response

            LazyImage.SetUrl(image, Art("wild-lotus"));   // served straight away
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(WpfArt.Green, WpfArt.DisplayedColor(image.Source));

            scripted.Release(Art("x-ray"));               // the abandoned selection arrives late
            WpfArt.Pump(TimeSpan.FromMilliseconds(250));

            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));
            Assert.Equal(WpfArt.Green, WpfArt.DisplayedColor(image.Source));
            Assert.Contains(Art("wild-lotus"), scripted.Requests);
        });
    }

    [Fact]
    public void FailedArtForTheNewSelectionDoesNotBringBackThePreviousOne()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Refuse(Art("asiimov"));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("x-ray"));
            host.WaitForLoaded();
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);

            LazyImage.SetUrl(image, Art("asiimov"));
            WpfArt.PumpUntil(() => LazyImage.GetArtState(image) == ImageArtState.Unavailable, Timeout);

            // PumpUntil gives up silently at the deadline, so the state has to be asserted here: an
            // element that stayed "Loading" forever would otherwise pass this test.
            Assert.Equal(ImageArtState.Unavailable, LazyImage.GetArtState(image));
            Assert.Null(image.Source);
        });
    }

    [Fact]
    public void AFailedAttemptIsUnavailableWhileTheRetryWaits()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Refuse(Art("broken"));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("broken"));
            host.WaitForLoaded();

            WpfArt.PumpUntil(() => LazyImage.GetArtState(image) == ImageArtState.Unavailable, Timeout);
            Assert.Equal(ImageArtState.Unavailable, LazyImage.GetArtState(image));
            Assert.Null(image.Source);

            // The bounded cooldown retries are still pending, but a wait for permission to ask again is
            // not progress: the element must not be relabelled "Loading" behind the viewer's back.
            WpfArt.Pump(TimeSpan.FromMilliseconds(500));
            Assert.Equal(ImageArtState.Unavailable, LazyImage.GetArtState(image));
            Assert.Null(image.Source);
        });
    }

    [Fact]
    public void RapidSelectionsEndOnTheLastOneAlone()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Hold(Art("a"));
            scripted.Hold(Art("b"));
            scripted.Serve(Art("c"), Artwork(WpfArt.Blue));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("a"));
            host.WaitForLoaded();

            LazyImage.SetUrl(image, Art("b"));
            LazyImage.SetUrl(image, Art("c"));

            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(WpfArt.Blue, WpfArt.DisplayedColor(image.Source));

            scripted.Release(Art("a"));
            scripted.Release(Art("b"));
            WpfArt.Pump(TimeSpan.FromMilliseconds(300));

            Assert.Equal(WpfArt.Blue, WpfArt.DisplayedColor(image.Source));
            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));
        });
    }

    [Fact]
    public void WaitingForArtIsNotReportedAsMissingArt()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Green));
            scripted.Hold(Art("slow"));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("slow"));
            host.WaitForLoaded();

            Assert.Null(image.Source);
            Assert.Equal(ImageArtState.Loading, LazyImage.GetArtState(image));

            scripted.Release(Art("slow"));
            WpfArt.PumpUntil(() => LazyImage.GetArtState(image) == ImageArtState.Loaded, Timeout);
            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));

            // An element with nothing to ask for is a different fact, and it is final immediately.
            LazyImage.SetUrl(image, null);
            Assert.Equal(ImageArtState.Unavailable, LazyImage.GetArtState(image));
        });
    }

    [Fact]
    public void ChangingTheDecodeWidthIsANewRequest()
    {
        WpfArt.RunOnStaThread(() =>
        {
            using var scope = WpfArt.Install(new WpfArt.Scripted(Artwork(WpfArt.Green)), _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("kit"));
            host.WaitForLoaded();
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);

            LazyImage.SetPixelWidth(image, 232);

            Assert.Null(image.Source);
            Assert.Equal(ImageArtState.Loading, LazyImage.GetArtState(image));
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));
        });
    }

    [Fact]
    public void RealDetachAndReattachRecoversTheArt()
    {
        WpfArt.RunOnStaThread(() =>
        {
            using var scope = WpfArt.Install(new WpfArt.Scripted(Artwork(WpfArt.Green)), _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("kit"));
            host.WaitForLoaded();
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(ImageArtState.Loaded, LazyImage.GetArtState(image));

            host.Detach();
            host.WaitForUnloaded();
            Assert.Equal(ImageArtState.Idle, LazyImage.GetArtState(image));

            host.Reattach();
            WpfArt.PumpUntil(() => LazyImage.GetArtState(image) == ImageArtState.Loaded, Timeout);
            Assert.Equal(WpfArt.Green, WpfArt.DisplayedColor(image.Source));
        });
    }

    [Fact]
    public void SwitchingBetweenTwoAlreadyCachedSkinsNeverShowsTheOtherOne()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var scripted = new WpfArt.Scripted(Artwork(WpfArt.Red));
            scripted.Serve(Art("first"), Artwork(WpfArt.Red));
            scripted.Serve(Art("second"), Artwork(WpfArt.Green));
            using var scope = WpfArt.Install(scripted, _root);

            var image = new Image();
            using var host = WpfArt.Host(image);
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, Art("first"));
            host.WaitForLoaded();
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);

            LazyImage.SetUrl(image, Art("second"));
            Assert.Null(image.Source);
            WpfArt.PumpUntil(() => image.Source is not null, Timeout);
            Assert.Equal(WpfArt.Green, WpfArt.DisplayedColor(image.Source));

            // "first" is already decoded, so the swap is immediate: the other selection's art is never
            // shown, and neither is a blank frame where its picture used to be.
            LazyImage.SetUrl(image, Art("first"));
            Assert.Equal(WpfArt.Red, WpfArt.DisplayedColor(image.Source));

            Assert.Equal(2, scripted.Requests.Count);   // one fetch each; nothing re-entered the network
        });
    }
}
