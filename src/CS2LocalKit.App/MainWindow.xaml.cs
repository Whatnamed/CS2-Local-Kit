using System.Windows;
using System.Windows.Threading;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App;

public partial class MainWindow : Window
{
    private DispatcherTimer? _processTimer;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Called once the pinned snapshot is loaded off the UI thread.</summary>
    public void AttachViewModel(MainViewModel viewModel)
    {
        DataContext = viewModel;
        LoadingBar.IsIndeterminate = false;
        LoadingHost.Visibility = Visibility.Collapsed;
        ShellHost.Visibility = Visibility.Visible;

        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _processTimer.Tick += (_, _) => viewModel.Manager.PollProcessState();
        _processTimer.Start();
        Closed += (_, _) => _processTimer?.Stop();
    }

    public void ReportStartupFailure(Exception error)
    {
        LoadingBar.IsIndeterminate = false;
        LoadingText.Text = "启动失败：" + error.Message;
        MessageBox.Show(this,
            "CS2 Local Kit 无法完成启动：\n" + error.Message + "\n\n可以重新打开程序再试；如果持续失败，请查看运行状态页面中的清单准备说明。",
            "启动失败", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
