using System.Windows;
using Microsoft.Win32;

namespace CS2LocalKit.App.Common;

public class WpfDialogService : IDialogService
{
    public UnsavedChangesResolution ConfirmUnsavedChanges(string presetName)
    {
        var result = MessageBox.Show(
            $"预设 \"{presetName}\" 已修改但尚未保存。\n\n是否在切换前保存当前修改？\n- 点击“是”：保存并切换\n- 点击“否”：放弃修改并切换\n- 点击“取消”：留在当前预设",
            "未保存的更改",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesResolution.SaveAndSwitch,
            MessageBoxResult.No => UnsavedChangesResolution.DiscardAndSwitch,
            _ => UnsavedChangesResolution.Cancel,
        };
    }

    public bool Confirm(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public string? ShowSaveFileDialog(string defaultName, string filter)
    {
        var sfd = new SaveFileDialog
        {
            FileName = defaultName,
            Filter = filter,
            DefaultExt = ".json"
        };
        return sfd.ShowDialog() == true ? sfd.FileName : null;
    }

    public string? ShowOpenFileDialog(string filter)
    {
        var ofd = new OpenFileDialog
        {
            Filter = filter,
            DefaultExt = ".json"
        };
        return ofd.ShowDialog() == true ? ofd.FileName : null;
    }
}
