namespace CS2LocalKit.App.Common;

public enum UnsavedChangesResolution
{
    SaveAndSwitch,
    DiscardAndSwitch,
    Cancel
}

public interface IDialogService
{
    UnsavedChangesResolution ConfirmUnsavedChanges(string presetName);
    bool Confirm(string title, string message);
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    string? ShowSaveFileDialog(string defaultName, string filter);
    string? ShowOpenFileDialog(string filter);
}
