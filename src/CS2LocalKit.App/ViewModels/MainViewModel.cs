using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;

namespace CS2LocalKit.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
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

        Cosmetics = new CosmeticsViewModel(Manager);
        Presets = new PresetsViewModel(Manager);
        RuntimeStatus = new RuntimeStatusViewModel(Manager);

        Header = new HeaderViewModel(Manager,
            onSave: () => Cosmetics.ExecuteSave(),
            onApply: () => Cosmetics.ExecuteApply());

        Navigation = new NavigationViewModel();
    }

    public static MainViewModel CreateDefault()
    {
        var services = AppServices.CreateDefault();
        var dialogService = new WpfDialogService();
        return new MainViewModel(services, dialogService);
    }
}
