using System.IO;
using CS2LocalKit.Core;
using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.Core.Store;

namespace CS2LocalKit.App.Services;

public class AppServices
{
    public string Cs2ModRoot { get; }
    public PresetStore PresetStore { get; }
    public ActivePresetState ActivePresetState { get; }
    public CatalogIndex? Catalog { get; }
    public bool CatalogAvailable => Catalog is not null;
    public string? CatalogError { get; }
    public RuntimeStatusService RuntimeStatusService { get; }
    public FixtureApplier FixtureApplier { get; }
    public string PlayerStatePath { get; }

    public string GetSteamId64() => PlayerState.GetSteamId64(PlayerStatePath);

    public AppServices(
        string? cs2ModRoot = null,
        string? catalogCacheRoot = null,
        string? csgoDir = null,
        string? cs2Root = null,
        string? activePresetPath = null,
        string? presetsRoot = null,
        string? backupsRoot = null,
        string? playerStatePath = null,
        string? lockPath = null,
        Func<bool>? cs2RunningProbe = null)
    {
        Cs2ModRoot = cs2ModRoot ?? CorePaths.Cs2ModRoot;

        var actPresetPath = activePresetPath ?? CorePaths.ActivePresetPath;
        ActivePresetState = new ActivePresetState(actPresetPath);

        var pRoot = presetsRoot ?? CorePaths.PresetsHumanRoot;
        PresetStore = new PresetStore(pRoot);

        var cRoot = catalogCacheRoot ?? CatalogSnapshot.DefaultCacheRoot(Cs2ModRoot);
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
    }

    public static AppServices CreateDefault() => new();
}
