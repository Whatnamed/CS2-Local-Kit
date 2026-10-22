using CS2LocalKit.App.Common;

namespace CS2LocalKit.App.ViewModels;

public enum PageType
{
    MatchSetup,
    Cosmetics,
    Presets,
    Bot,
    RuntimeStatus
}

public sealed class NavigationViewModel : ViewModelBase
{
    private PageType _currentPage = PageType.MatchSetup;

    public PageType CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsMatchSetupSelected));
            OnPropertyChanged(nameof(IsCosmeticsSelected));
            OnPropertyChanged(nameof(IsPresetsSelected));
            OnPropertyChanged(nameof(IsRuntimeStatusSelected));
            OnPropertyChanged(nameof(IsBotSelected));
            OnPropertyChanged(nameof(IsHumanPage));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public bool IsMatchSetupSelected { get => CurrentPage == PageType.MatchSetup; set { if(value) CurrentPage=PageType.MatchSetup; } }
    public bool IsCosmeticsSelected { get => CurrentPage == PageType.Cosmetics; set { if(value) CurrentPage=PageType.Cosmetics; } }
    public bool IsBotSelected { get => CurrentPage == PageType.Bot; set { if(value) CurrentPage=PageType.Bot; } }
    public bool IsHumanPage => IsCosmeticsSelected || IsPresetsSelected;
    public bool IsPresetsSelected { get => CurrentPage == PageType.Presets; set { if(value) CurrentPage=PageType.Presets; } }
    public bool IsRuntimeStatusSelected { get => CurrentPage == PageType.RuntimeStatus; set { if(value) CurrentPage=PageType.RuntimeStatus; } }

    public string PageTitle => CurrentPage switch
    {
        PageType.MatchSetup => "准备下一局",
        PageType.Cosmetics => "真人饰品",
        PageType.Bot => "Bot 安装",
        PageType.Presets => "饰品预设",
        PageType.RuntimeStatus => "环境与恢复",
        _ => "",
    };

    public string PageSubtitle => CurrentPage switch
    {
        PageType.MatchSetup => "选择双方阵容与开局规则，保存后交给下一张本地地图",
        PageType.Cosmetics => "按真实武器配置外观 · CT / T 独立保存",
        PageType.Bot => "检查实际安装，再预览目标模式与精确恢复范围",
        PageType.Presets => "管理编辑中的预设与激活预设",
        PageType.RuntimeStatus => "CS2 运行组件、饰品数据与本地清单状态",
        _ => "",
    };

    public RelayCommand NavigateMatchSetupCommand { get; }
    public RelayCommand NavigateCosmeticsCommand { get; }
    public RelayCommand NavigateBotCommand { get; }
    public RelayCommand NavigatePresetsCommand { get; }
    public RelayCommand NavigateRuntimeStatusCommand { get; }

    public NavigationViewModel()
    {
        NavigateMatchSetupCommand = new RelayCommand(() => CurrentPage = PageType.MatchSetup);
        NavigateCosmeticsCommand = new RelayCommand(() => CurrentPage = PageType.Cosmetics);
        NavigateBotCommand = new RelayCommand(() => CurrentPage = PageType.Bot);
        NavigatePresetsCommand = new RelayCommand(() => CurrentPage = PageType.Presets);
        NavigateRuntimeStatusCommand = new RelayCommand(() => CurrentPage = PageType.RuntimeStatus);
    }
}
