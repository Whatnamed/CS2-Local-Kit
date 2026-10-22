using System.Windows;
using CS2LocalKit.App.Common;

namespace CS2LocalKit.App.Views;

/// <summary>Presentation only. Callers still own operation gates and execute only after acceptance.</summary>
public partial class DecisionWindow : Window
{
    public string Heading { get; }
    public string Message { get; }
    public bool IsUnsaved { get; }
    public string AcceptLabel { get; }
    public UnsavedChangesResolution Resolution { get; private set; } = UnsavedChangesResolution.Cancel;
    public DecisionWindow(string title,string message,bool unsaved=false,bool informational=false)
    {
        Heading=title;Message=message;IsUnsaved=unsaved;
        AcceptLabel=unsaved ? "保存并切换" : informational ? "知道了" : "确认继续";
        InitializeComponent();DataContext=this;
    }
    private void CancelClick(object sender,RoutedEventArgs e) => DialogResult=false;
    private void DiscardClick(object sender,RoutedEventArgs e) {Resolution=UnsavedChangesResolution.DiscardAndSwitch;DialogResult=true;}
    private void AcceptClick(object sender,RoutedEventArgs e) {Resolution=UnsavedChangesResolution.SaveAndSwitch;DialogResult=true;}
}
