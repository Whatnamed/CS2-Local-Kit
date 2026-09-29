using CS2LocalKit.App.Common;

namespace CS2LocalKit.App.ViewModels;

public enum PageType
{
    Cosmetics,
    Presets,
    RuntimeStatus
}

public sealed class NavigationViewModel : ViewModelBase
{
    private PageType _currentPage = PageType.Cosmetics;

    public PageType CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsCosmeticsSelected));
            OnPropertyChanged(nameof(IsPresetsSelected));
            OnPropertyChanged(nameof(IsRuntimeStatusSelected));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public bool IsCosmeticsSelected => CurrentPage == PageType.Cosmetics;
    public bool IsPresetsSelected => CurrentPage == PageType.Presets;
    public bool IsRuntimeStatusSelected => CurrentPage == PageType.RuntimeStatus;

    public string PageTitle => CurrentPage switch
    {
        PageType.Cosmetics => "饰品",
        PageType.Presets => "预设",
        PageType.RuntimeStatus => "运行状态",
        _ => "",
    };

    public string PageSubtitle => CurrentPage switch
    {
        PageType.Cosmetics => "选择武器、刀型、手套与音乐盒的外观",
        PageType.Presets => "管理编辑中的预设与游戏内当前预设",
        PageType.RuntimeStatus => "CS2 运行组件、饰品数据与本地清单状态",
        _ => "",
    };

    public RelayCommand NavigateCosmeticsCommand { get; }
    public RelayCommand NavigatePresetsCommand { get; }
    public RelayCommand NavigateRuntimeStatusCommand { get; }

    public NavigationViewModel()
    {
        NavigateCosmeticsCommand = new RelayCommand(() => CurrentPage = PageType.Cosmetics);
        NavigatePresetsCommand = new RelayCommand(() => CurrentPage = PageType.Presets);
        NavigateRuntimeStatusCommand = new RelayCommand(() => CurrentPage = PageType.RuntimeStatus);
    }
}
