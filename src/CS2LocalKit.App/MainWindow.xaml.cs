using System.Windows;
using System.Windows.Threading;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer? _processTimer;

    public MainWindow() : this(MainViewModel.CreateDefault())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        _processTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _processTimer.Tick += (s, e) => viewModel.Manager.PollProcessState();
        _processTimer.Start();

        Closed += (s, e) =>
        {
            _processTimer?.Stop();
        };
    }
}
