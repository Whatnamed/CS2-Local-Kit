using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.ViewModels;

public sealed class HeaderViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;

    public string AppTitle => "CS2 Local Kit";

    public string DisplayPresetName
    {
        get
        {
            var name = _manager.WorkingPresetName ?? "未选择预设";
            return _manager.IsDirty ? $"{name} *" : name;
        }
    }

    public bool IsDirty => _manager.IsDirty;
    public string DraftStatusText => _manager.IsDirty ? "有未保存更改" : "已保存";

    public bool Cs2Running => _manager.Cs2Running;

    public string Cs2StatusText => _manager.Cs2Running ? "CS2 正在运行" : "CS2 已关闭";

    public RuntimeHealthLevel HealthLevel => _manager.RuntimeHealth;

    public string HealthSummary => _manager.RuntimeSummary;

    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }

    public HeaderViewModel(PresetManagerService manager, Action onSave, Action onApply)
    {
        _manager = manager;
        SaveCommand = new RelayCommand(onSave);
        ApplyCommand = new RelayCommand(onApply, () => !_manager.Cs2Running);

        _manager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PresetManagerService.WorkingPresetName)
                || e.PropertyName == nameof(PresetManagerService.IsDirty))
            {
                OnPropertyChanged(nameof(DisplayPresetName));
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(DraftStatusText));
            }
            else if (e.PropertyName == nameof(PresetManagerService.Cs2Running))
            {
                OnPropertyChanged(nameof(Cs2Running));
                OnPropertyChanged(nameof(Cs2StatusText));
                ApplyCommand.RaiseCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(PresetManagerService.RuntimeHealth)
                     || e.PropertyName == nameof(PresetManagerService.RuntimeSummary))
            {
                OnPropertyChanged(nameof(HealthLevel));
                OnPropertyChanged(nameof(HealthSummary));
            }
        };
    }
}
