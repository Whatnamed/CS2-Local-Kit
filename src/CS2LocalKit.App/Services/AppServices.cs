using System.IO;
using System.Net.Http;
using CS2LocalKit.Core;
using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.Core.Store;

namespace CS2LocalKit.App.Services;

public class AppServices : IDisposable
{
    public string Cs2ModRoot { get; }
    public string PresetsRoot { get; }
    public PresetStore PresetStore { get; }
    public ActivePresetState ActivePresetState { get; }
    public CatalogIndex? Catalog { get; private set; }
    public bool CatalogAvailable => Catalog is not null;
    public string? CatalogError { get; private set; }
    public string CatalogCacheRoot { get; }
    public CatalogCacheStatus CatalogStatus => CatalogSyncService.Inspect(CatalogCacheRoot);
    public ImageCacheService Images { get; }
    public ImageSourceProvider ImageSources { get; }
    public RuntimeStatusService RuntimeStatusService { get; }
    public FixtureApplier FixtureApplier { get; }
    public MetaModStartupRepairService MetaModStartupRepairService { get; }
    public string PlayerStatePath { get; }

    public string GetSteamId64() => PlayerState.GetSteamId64(PlayerStatePath);

    public AppServices(
        string? cs2ModRoot = null,
        string? catalogCacheRoot = null,
        string? imageCacheRoot = null,
        string? csgoDir = null,
        string? cs2Root = null,
        string? activePresetPath = null,
        string? presetsRoot = null,
        string? backupsRoot = null,
        string? playerStatePath = null,
        string? lockPath = null,
        HttpMessageHandler? imageHandler = null,
        Func<bool>? cs2RunningProbe = null)
    {
        Cs2ModRoot = cs2ModRoot ?? CorePaths.Cs2ModRoot;

        var actPresetPath = activePresetPath ?? CorePaths.ActivePresetPath;
        ActivePresetState = new ActivePresetState(actPresetPath);

        var pRoot = presetsRoot ?? CorePaths.PresetsHumanRoot;
        PresetsRoot = pRoot;
        PresetStore = new PresetStore(pRoot);

        var cRoot = catalogCacheRoot ?? CatalogSnapshot.DefaultCacheRoot(Cs2ModRoot);
        CatalogCacheRoot = cRoot;
        try
        {
            Catalog = CatalogSnapshot.LoadCachedIndex(cRoot);
            CatalogError = null;
        }
        catch (CatalogCacheException ex)
        {
            Catalog = null;
            CatalogError = ex.Message;
        }
        catch (Exception ex)
        {
            Catalog = null;
            CatalogError = $"加载物品清单失败: {ex.Message}";
        }

        // Preview art is a presentation resource: it is prepared lazily, cached persistently, and
        // can never block or invalidate catalog validation, save or apply.
        var iRoot = imageCacheRoot ?? Path.Combine(Cs2ModRoot, "app-data", "cosmetics-lab", "image-cache");
        Images = new ImageCacheService(iRoot, imageHandler);
        ImageSources = new ImageSourceProvider(Images);

        var plPath = playerStatePath ?? CorePaths.PlayerStatePath;
        PlayerStatePath = plPath;

        var lPath = lockPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "inventory-simulator.lock.json");
        if (!File.Exists(lPath))
        {
            var fallback = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "runtime", "inventory-simulator.lock.json"));
            if (File.Exists(fallback)) lPath = fallback;
        }

        var bRoot = backupsRoot ?? CorePaths.FixtureBackupRoot;

        RuntimeStatusService = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            Cs2Root = cs2Root,
            LockPath = lPath,
            BackupsRoot = bRoot,
            PresetsRoot = pRoot,
            ActivePresetPath = actPresetPath,
            Cs2RunningProbe = cs2RunningProbe,
        });

        FixtureApplier = new FixtureApplier(new FixtureApplierOptions
        {
            CsgoDir = csgoDir,
            BackupsRoot = bRoot,
            Catalog = Catalog,
            Cs2RunningProbe = cs2RunningProbe,
        });

        MetaModStartupRepairService = new MetaModStartupRepairService(new MetaModStartupRepairOptions
        {
            Cs2Root = cs2Root,
            LockPath = lPath,
            BackupsRoot = Path.Combine(Cs2ModRoot, "backups", "metamod-startup-repair"),
            Cs2RunningProbe = cs2RunningProbe,
        });
    }

    public static AppServices CreateDefault() => new();

    /// <summary>
    /// Prepares the pinned catalog snapshot for every supported locale. Safe to call repeatedly:
    /// cached files are reused and nothing is re-downloaded.
    /// </summary>
    public Task<CatalogSyncReport> SyncCatalogAsync(CancellationToken ct = default)
        => new CatalogSyncService(CatalogCacheRoot).SyncAsync(ct);

    /// <summary>Re-reads the pinned snapshot from disk after a catalog sync.</summary>
    public CatalogIndex? ReloadCatalog()
    {
        try
        {
            Catalog = CatalogSnapshot.LoadCachedIndex(CatalogCacheRoot);
            CatalogError = null;
        }
        catch (Exception ex)
        {
            Catalog = null;
            CatalogError = ex.Message;
        }
        return Catalog;
    }

    public void Dispose()
    {
        Images.Dispose();
        GC.SuppressFinalize(this);
    }
}
