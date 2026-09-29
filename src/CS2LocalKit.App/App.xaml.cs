using System.Windows;
using CS2LocalKit.App.ViewModels;

namespace CS2LocalKit.App;

public partial class App : Application
{
    /// <summary>
    /// A desktop tool must not vanish mid-edit. Report the failure, keep the log next to the other
    /// diagnostics, and let the user decide whether to continue.
    /// </summary>
    private void OnUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.Combine(CS2LocalKit.Core.CorePaths.AppDataRoot, "diagnostics");
            System.IO.Directory.CreateDirectory(dir);
            var log = System.IO.Path.Combine(dir, "controller-error.log");
            System.IO.File.AppendAllText(log,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception.GetType().Name}: {e.Exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* reporting must never replace the original failure */ }

        MessageBox.Show(
            "界面出现未处理的错误：" + Environment.NewLine + e.Exception.Message + Environment.NewLine + Environment.NewLine
            + "详细信息已写入：" + Environment.NewLine
            + System.IO.Path.Combine(CS2LocalKit.Core.CorePaths.AppDataRoot, "diagnostics", "controller-error.log"),
            "CS2 Local Kit", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow();
        window.Show();

        // Reading the pinned snapshot is real disk and JSON work; do it off the UI thread so the
        // window is interactive and shows its own preparation state instead of a dead frame.
        Task.Run(() =>
        {
            MainViewModel? model = null;
            Exception? error = null;
            try
            {
                model = MainViewModel.CreateDefault();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (error is not null)
                {
                    window.ReportStartupFailure(error);
                    return;
                }
                window.AttachViewModel(model!);
            });
        });
    }
}
