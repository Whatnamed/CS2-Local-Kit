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
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsCosmeticsSelected));
                OnPropertyChanged(nameof(IsPresetsSelected));
                OnPropertyChanged(nameof(IsRuntimeStatusSelected));
            }
        }
    }

    public bool IsCosmeticsSelected => CurrentPage == PageType.Cosmetics;
    public bool IsPresetsSelected => CurrentPage == PageType.Presets;
    public bool IsRuntimeStatusSelected => CurrentPage == PageType.RuntimeStatus;

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
