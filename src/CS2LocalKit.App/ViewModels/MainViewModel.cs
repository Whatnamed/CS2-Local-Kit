using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Catalog;

namespace CS2LocalKit.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private bool _isSyncingCatalog;
    private string _catalogSyncMessage = "";

    public AppServices Services { get; }
    public PresetManagerService Manager { get; }
    public HeaderViewModel Header { get; }
    public NavigationViewModel Navigation { get; }
    public CosmeticsViewModel Cosmetics { get; }
    public PresetsViewModel Presets { get; }
    public RuntimeStatusViewModel RuntimeStatus { get; }

    public MainViewModel(AppServices services, IDialogService dialogService)
    {
        Services = services;
        Manager = new PresetManagerService(services, dialogService);

        // The view layer resolves preview art through this single entry point.
        ImageSourceProvider.Current = services.ImageSources;

        Cosmetics = new CosmeticsViewModel(Manager);
        Presets = new PresetsViewModel(Manager);
        RuntimeStatus = new RuntimeStatusViewModel(Manager);

        Header = new HeaderViewModel(Manager,
            onSave: () => Cosmetics.ExecuteSave(),
            onApply: () => Cosmetics.ExecuteApply());

        Navigation = new NavigationViewModel();

        SyncCatalogCommand = new RelayCommand(() => _ = SyncCatalogAsync(), () => !IsSyncingCatalog);
    }

    public bool CatalogUnavailable => !Services.CatalogAvailable;
    public string CatalogMessage => Services.CatalogError ?? "";
    public string CatalogCacheRoot => Services.CatalogCacheRoot;

    public bool IsSyncingCatalog
    {
        get => _isSyncingCatalog;
        private set
        {
            if (SetProperty(ref _isSyncingCatalog, value))
            {
                SyncCatalogCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CatalogSyncButtonText));
            }
        }
    }

    public string CatalogSyncButtonText => IsSyncingCatalog ? "正在准备清单…" : "准备本地清单";

    public string CatalogSyncMessage
    {
        get => _catalogSyncMessage;
        private set => SetProperty(ref _catalogSyncMessage, value);
    }

    public RelayCommand SyncCatalogCommand { get; }

    /// <summary>
    /// Explicit catalog preparation: downloads the pinned English and Simplified Chinese snapshot
    /// into app-data, then re-reads it. Never touches the game fixture.
    /// </summary>
    public async Task SyncCatalogAsync()
    {
        if (IsSyncingCatalog) return;
        IsSyncingCatalog = true;
        CatalogSyncMessage = "";
        try
        {
            var report = await Services.SyncCatalogAsync();
            if (!report.IdentityReady)
            {
                CatalogSyncMessage = "清单准备失败：" + report.FailedMessages;
                return;
            }

            var reloaded = Manager.ReloadCatalog();
            var chinese = reloaded is not null && reloaded.HasChinese;
            CatalogSyncMessage = reloaded is null
                ? "清单文件已就绪，但读取失败，请重启程序。"
                : $"清单已就绪（{string.Join(" + ", reloaded.LocalesLoaded)}）：{reloaded.WeaponCount} 个武器 · {reloaded.PaintCount} 个皮肤 · {reloaded.MusicKitCount} 个音乐盒"
                  + (chinese ? "" : "；缺少中文数据，界面将回退英文显示");
            OnPropertyChanged(nameof(CatalogUnavailable));
            OnPropertyChanged(nameof(CatalogMessage));
            RuntimeStatus.RefreshAfterCatalogChange();
        }
        catch (Exception ex)
        {
            CatalogSyncMessage = "清单准备失败: " + ex.Message;
        }
        finally
        {
            IsSyncingCatalog = false;
        }
    }

    public static MainViewModel CreateDefault()
    {
        var services = AppServices.CreateDefault();
        var dialogService = new WpfDialogService();
        return new MainViewModel(services, dialogService);
    }
}
