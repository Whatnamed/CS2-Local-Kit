using System.IO;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.ViewModels;

/// <summary>
/// The persistent preset context shown in the window bar: which preset is being edited, whether it
/// is dirty, which one the game is set to use, and what is currently installed in CS2. These stay
/// four separate facts - the editor never collapses them into one "current".
/// </summary>
public sealed class HeaderViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;

    public HeaderViewModel(PresetManagerService manager, Action onSave, Action onApply)
    {
        _manager = manager;
        SaveCommand = new RelayCommand(onSave, () => _manager.Draft is not null);
        ApplyCommand = new RelayCommand(onApply, () => !_manager.Cs2Running && _manager.Draft is not null);
        DiscardCommand = new RelayCommand(Discard, () => _manager.IsDirty && _manager.WorkingPresetName is not null);

        _manager.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PresetManagerService.WorkingPresetName):
                case nameof(PresetManagerService.IsDirty):
                case nameof(PresetManagerService.ActivePresetName):
                case nameof(PresetManagerService.LatestAppliedPresetName):
                case nameof(PresetManagerService.LatestAppliedTime):
                case nameof(PresetManagerService.LatestAppliedHash):
                    RaisePresetFacts();
                    break;
                case nameof(PresetManagerService.Cs2Running):
                    OnPropertyChanged(nameof(Cs2Running));
                    OnPropertyChanged(nameof(Cs2StatusText));
                    ApplyCommand.RaiseCanExecuteChanged();
                    break;
                case nameof(PresetManagerService.RuntimeHealth):
                case nameof(PresetManagerService.RuntimeSummary):
                    OnPropertyChanged(nameof(HealthLevel));
                    OnPropertyChanged(nameof(HealthSummary));
                    break;
                case nameof(PresetManagerService.Draft):
                    SaveCommand.RaiseCanExecuteChanged();
                    ApplyCommand.RaiseCanExecuteChanged();
                    break;
            }
        };
    }

    public string AppTitle => "CS2 Local Kit";

    public string EditingName => Friendly(_manager.WorkingPresetName);
    public string EditingFileName => _manager.WorkingPresetName ?? "未选择预设";
    public bool IsDirty => _manager.IsDirty;
    public string DraftStatusText => _manager.IsDirty ? "有未保存更改" : "已保存";
    public string ActiveName => Friendly(_manager.ActivePresetName);
    public string ActiveFileName => _manager.ActivePresetName ?? "未选择";
    public string InstalledName => Friendly(_manager.LatestAppliedPresetName);
    public bool IsEditingSameAsActive => !string.IsNullOrEmpty(_manager.WorkingPresetName)
        && string.Equals(_manager.WorkingPresetName, _manager.ActivePresetName, StringComparison.OrdinalIgnoreCase);
    public string InstalledTimeText => FormatTime(_manager.LatestAppliedTime);
    public string InstalledHashText => Short(_manager.LatestAppliedHash);
    public bool HasInstalledProjection => !string.IsNullOrEmpty(_manager.LatestAppliedHash);

    public bool Cs2Running => _manager.Cs2Running;
    public string Cs2StatusText => _manager.Cs2Running ? "CS2 正在运行" : "CS2 已关闭";
    public RuntimeHealthLevel HealthLevel => _manager.RuntimeHealth;
    public string HealthSummary => _manager.RuntimeSummary;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand DiscardCommand { get; }

    private void Discard()
    {
        var name = _manager.WorkingPresetName;
        if (string.IsNullOrEmpty(name)) return;
        _manager.LoadPreset(name, force: true);
    }

    private void RaisePresetFacts()
    {
        OnPropertyChanged(nameof(EditingName));
        OnPropertyChanged(nameof(EditingFileName));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DraftStatusText));
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(ActiveFileName));
        OnPropertyChanged(nameof(InstalledName));
        OnPropertyChanged(nameof(IsEditingSameAsActive));
        OnPropertyChanged(nameof(InstalledTimeText));
        OnPropertyChanged(nameof(InstalledHashText));
        OnPropertyChanged(nameof(HasInstalledProjection));
        SaveCommand.RaiseCanExecuteChanged();
        DiscardCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Preset files are stored as &lt;name&gt;.v1.json; the bar shows the readable part.</summary>
    public static string Friendly(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "未选择";
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".v1.json", StringComparison.OrdinalIgnoreCase)) return name[..^".v1.json".Length];
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return name[..^".json".Length];
        return name;
    }

    private static string FormatTime(string? raw)
        => DateTimeOffset.TryParse(raw, out var time) ? time.ToLocalTime().ToString("MM-dd HH:mm") : "—";

    private static string Short(string? hash)
        => string.IsNullOrEmpty(hash) ? "—" : hash.Length > 12 ? hash[..12] : hash;
}
