using System.Windows;
using System.Windows.Media.Imaging;
using CS2LocalKit.App.Catalog;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// The detail preview is presentation only: a kit that is selected must stay visibly selected even
/// when its art cannot be fetched, and re-attaching an image element must never throw away the
/// download that is already running for it.
/// </summary>
[Collection(WpfArt.GlobalArt)]
public sealed class MusicKitPreviewTests : IDisposable
{
    private readonly string _root;

    public MusicKitPreviewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"cs2localkit-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    // --- the reported defect: a second Loaded discarded the decoded detail image ---

    [Fact]
    public void LazyImage_ReattachWhileFetchIsInFlight_StillAppliesTheImage()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new WpfArt.GateHandler(gate.Task, WpfArt.Png(256, 198));
            using var scope = WpfArt.Install(handler, _root);

            var image = new WpfArt.TestImage();
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, "https://example.invalid/economy/image/kit/512x384");

            // The first attach starts the fetch; the second one is WPF re-raising Loaded for the
            // same element. Neither may invalidate the result of the first.
            image.RaiseReattach();
            image.RaiseReattach();
            Assert.Null(image.Source);

            gate.SetResult();
            WpfArt.PumpUntil(() => image.Source is not null, TimeSpan.FromSeconds(5));

            Assert.NotNull(image.Source);
            Assert.IsType<BitmapImage>(image.Source);
            Assert.Single(handler.Requests);
        });
    }

    [Fact]
    public void LazyImage_DetachedThenReattached_StartsAFreshFetchRatherThanWaitingOnTheOldOne()
    {
        WpfArt.RunOnStaThread(() =>
        {
            var handler = new WpfArt.GateHandler(Task.CompletedTask, WpfArt.Png(64, 64));
            using var scope = WpfArt.Install(handler, _root);

            var image = new WpfArt.TestImage();
            LazyImage.SetPixelWidth(image, 512);
            LazyImage.SetUrl(image, "https://example.invalid/economy/image/first/512x384");
            image.RaiseReattach();
            WpfArt.PumpUntil(() => image.Source is not null, TimeSpan.FromSeconds(5));
            var first = image.Source;
            Assert.NotNull(first);

            // Detaching cancels the pending apply, so the slot must not stay "running" waiting for
            // a result that can never be applied: the next attach has to be able to fetch again.
            image.RaiseDetach();
            LazyImage.SetUrl(image, "https://example.invalid/economy/image/second/512x384");
            Assert.Null(image.Source);

            image.RaiseReattach();
            WpfArt.PumpUntil(() => !ReferenceEquals(image.Source, first) && image.Source is not null, TimeSpan.FromSeconds(5));
            Assert.NotNull(image.Source);
            Assert.Equal(2, handler.Requests.Count);
        });
    }

    // --- the decode layer really can display the shape Steam returns for a music kit ---

    [Fact]
    public void DetailImage_SteamSizedSmallerThanRequested_StillDecodes()
    {
        // The pinned catalog asks for /512x384 but the CDN caps music kit art at its native size.
        var path = Path.Combine(_root, "music-detail.png");
        File.WriteAllBytes(path, WpfArt.Png(256, 198));

        var source = ImageSourceProvider.Decode(path, 512);

        Assert.NotNull(source);
        Assert.True(((BitmapImage)source!).PixelWidth > 0);
    }

    [Fact]
    public void UndecodablePayload_IsNoArtRatherThanAnError()
    {
        var path = Path.Combine(_root, "broken.png");
        File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);

        Assert.Null(ImageSourceProvider.Decode(path, 512));
    }

    // --- placeholder semantics: "no art" and "no selection" are different states ---

    [Fact]
    public void SelectedKit_StaysSelectedRegardlessOfArtAvailability()
    {
        var manager = LoadPreset(WithMusicKit(78));
        var cosmetics = new CosmeticsViewModel(manager);

        // The selection facts and the preview facts are separate: nothing here consults the cache.
        Assert.True(cosmetics.IsMusicKitConfigured);
        Assert.Equal("Music Kit | Austin Wintory, The Devil Went Clubbing In Georgia", cosmetics.MusicKitName);
        Assert.Equal("#78", cosmetics.MusicKitIdText);
        Assert.NotNull(cosmetics.MusicKitImageUrl);
    }

    [Fact]
    public void NoKitSelected_ReportsNotSelectedAndRequestsNoArt()
    {
        var manager = LoadPreset(WithMusicKit(null));
        var cosmetics = new CosmeticsViewModel(manager);

        Assert.False(cosmetics.IsMusicKitConfigured);
        Assert.Equal("未选择音乐盒", cosmetics.MusicKitName);
        Assert.Null(cosmetics.MusicKitImageUrl);
        Assert.Equal("", cosmetics.MusicKitIdText);
    }

    [Fact]
    public void DetailAndThumbnailBothFollowTheSelectedKit()
    {
        var manager = LoadPreset(WithMusicKit(78));
        var cosmetics = new CosmeticsViewModel(manager);
        var detail = cosmetics.MusicKitDetail!;

        Assert.NotNull(detail.DetailUrl);
        Assert.EndsWith("/512x384", detail.DetailUrl);
        Assert.EndsWith("/232x176", detail.ThumbnailUrl);
        Assert.Equal(cosmetics.MusicKitImageUrl, detail.DetailUrl);

        // Clearing the selection removes both, so no stale preview can be shown.
        manager.Draft!.MusicKitId = null;
        Assert.Null(cosmetics.MusicKitImageUrl);
        Assert.Null(cosmetics.MusicKitDetail?.ThumbnailUrl);
    }

    [Fact]
    public void UnavailableArt_NeverTouchesTheDraft()
    {
        var manager = LoadPreset(WithMusicKit(78));
        manager.Draft!.MusicKitId = null;
        Assert.True(manager.IsDirty);

        var cosmetics = new CosmeticsViewModel(manager);
        Assert.False(cosmetics.IsMusicKitConfigured);
        Assert.Null(cosmetics.MusicKitImageUrl);

        // Reading the preview surface cannot decide anything about the draft.
        _ = cosmetics.MusicKitName;
        _ = cosmetics.MusicKitDetail;
        Assert.True(manager.IsDirty);
        Assert.Null(manager.Draft.MusicKitId);

        cosmetics.Section = CosmeticsSection.MusicKit;
        Assert.True(manager.IsDirty);
        Assert.Null(manager.Draft.MusicKitId);
    }

    private static HumanPreset WithMusicKit(int? musicKitId)
    {
        var source = TestFixtures.ExamplePreset();
        return new HumanPreset
        {
            Kind = source.Kind,
            SchemaVersion = source.SchemaVersion,
            Ct = source.Ct,
            T = source.T,
            MusicKitId = musicKitId,
        };
    }

    private PresetManagerService LoadPreset(HumanPreset preset)
    {
        var presetsRoot = Path.Combine(_root, "presets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(presetsRoot);
        var name = "preview.v1.json";
        File.WriteAllText(Path.Combine(presetsRoot, name), HumanPresetJson.Write(preset));

        var services = new AppServices(
            cs2ModRoot: _root,
            catalogCacheRoot: TestFixtures.CatalogDir,
            csgoDir: Path.Combine(_root, "csgo"),
            cs2Root: Path.Combine(_root, "cs2"),
            activePresetPath: Path.Combine(_root, "active-preset.json"),
            presetsRoot: presetsRoot,
            backupsRoot: Path.Combine(_root, "backups"),
            playerStatePath: Path.Combine(_root, "player-state.json"),
            lockPath: Path.Combine(_root, "lock.json"),
            cs2RunningProbe: () => false,
            imageCacheRoot: Path.Combine(_root, "images-" + Guid.NewGuid().ToString("N")));
        var manager = new PresetManagerService(services, new MockDialogService());
        Assert.True(manager.LoadPreset(name));
        return manager;
    }
}
