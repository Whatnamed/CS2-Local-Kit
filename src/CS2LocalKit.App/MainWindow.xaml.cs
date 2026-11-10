using System.Windows;
using System.Windows.Threading;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App;

public partial class MainWindow : Window
{
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

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        var closed = false;
        timer.Tick += async (_, _) => await PollRuntimeAsync();
        Closed += (_, _) => { closed = true; timer.Stop(); };
        _ = PollRuntimeAsync();

        async Task PollRuntimeAsync()
        {
            timer.Stop();
            try
            {
                await viewModel.Manager.PollProcessStateAsync();
                await viewModel.Bot.Setup.PollDailyAsync();
                await viewModel.Bot.Setup.RefreshRuntimeAsync();
            }
            finally
            {
                if (!closed) timer.Start();
            }
        }
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
