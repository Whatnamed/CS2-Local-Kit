using System.Windows;
using Microsoft.Win32;

namespace CS2LocalKit.App.Common;

public class WpfDialogService : IDialogService
{
    private static Views.DecisionWindow Dialog(string title,string message,bool unsaved=false,bool informational=false)
    {
        var dialog = new Views.DecisionWindow(title,message,unsaved,informational);
        if(Application.Current?.MainWindow is { IsVisible:true } owner) dialog.Owner=owner;
        return dialog;
    }
    public UnsavedChangesResolution ConfirmUnsavedChanges(string presetName)
    {
        var dialog=Dialog("未保存的更改",$"预设「{presetName}」有未保存的修改。\n\n保存并切换：保留修改后载入目标预设。\n放弃并切换：放弃当前草稿，载入目标预设。\n取消：继续编辑当前草稿。",unsaved:true);
        dialog.ShowDialog();return dialog.Resolution;
    }
    public bool Confirm(string title,string message) => Dialog(title,message).ShowDialog()==true;
    public void ShowError(string title,string message) => Dialog(title,message,informational:true).ShowDialog();
    public void ShowInfo(string title,string message) => Dialog(title,message,informational:true).ShowDialog();

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
