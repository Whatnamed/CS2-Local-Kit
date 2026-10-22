using CS2LocalKit.App.Common;

namespace CS2LocalKit.App.ViewModels;

public enum PageType { MatchSetup, Cosmetics, SystemSettings }

public sealed class NavigationViewModel : ViewModelBase
{
    private PageType _currentPage = PageType.MatchSetup;
    private PageType _returnPage = PageType.MatchSetup;
    public PageType CurrentPage
    {
        get => _currentPage;
        set
        {
            if (value == PageType.SystemSettings && _currentPage != PageType.SystemSettings) _returnPage = _currentPage;
            if (!SetProperty(ref _currentPage, value)) return;
            OnPropertyChanged(nameof(IsMatchSetupSelected));
            OnPropertyChanged(nameof(IsCosmeticsSelected));
            OnPropertyChanged(nameof(IsSettingsSelected));
            OnPropertyChanged(nameof(IsHumanPage));
            OnPropertyChanged(nameof(PageTitle));
        }
    }
    public bool IsMatchSetupSelected { get => CurrentPage == PageType.MatchSetup; set { if (value) CurrentPage = PageType.MatchSetup; } }
    public bool IsCosmeticsSelected { get => CurrentPage == PageType.Cosmetics; set { if (value) CurrentPage = PageType.Cosmetics; } }
    public bool IsSettingsSelected => CurrentPage == PageType.SystemSettings;
    public bool IsHumanPage => IsCosmeticsSelected;
    public string PageTitle => CurrentPage switch
    {
        PageType.MatchSetup => "对局配置",
        PageType.Cosmetics => "饰品配置",
        _ => "系统设置"
    };
    public RelayCommand NavigateMatchSetupCommand { get; }
    public RelayCommand NavigateCosmeticsCommand { get; }
    public RelayCommand NavigateSettingsCommand { get; }
    public RelayCommand ReturnFromSettingsCommand { get; }
    public NavigationViewModel()
    {
        NavigateMatchSetupCommand = new(() => CurrentPage = PageType.MatchSetup);
        NavigateCosmeticsCommand = new(() => CurrentPage = PageType.Cosmetics);
        NavigateSettingsCommand = new(() => CurrentPage = PageType.SystemSettings);
        ReturnFromSettingsCommand = new(() => CurrentPage = _returnPage);
    }
}
