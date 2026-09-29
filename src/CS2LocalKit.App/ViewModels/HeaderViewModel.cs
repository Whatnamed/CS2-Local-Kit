using System.IO;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.ViewModels;

/// <summary>
/// The persistent preset context shown in the window bar: which preset is being edited, whether it
/// is dirty, which one the game is pointed at, and what the installed fixture is proven to carry.
/// These stay separate facts and each keeps its own evidence level - the editor never collapses
/// them into one "current".
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
                case nameof(PresetManagerService.LastStatus):
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

    /// <summary>The active-preset pointer the next CS2 start reads. Says nothing about the files.</summary>
    public string ActiveName => Friendly(_manager.ActivePresetName);
    public string ActiveFileName => _manager.ActivePresetName ?? "未选择";

    private (InstalledConfigLevel Level, string? PresetPath) InstalledConfig
        => InstalledConfigResolver.Resolve(_manager.LastStatus, _manager.WorkingPresetName);

    public InstalledConfigLevel InstalledConfigLevel => InstalledConfig.Level;

    public string InstalledConfigName => InstalledConfig.Level switch
    {
        InstalledConfigLevel.Verified => Friendly(InstalledConfig.PresetPath),
        InstalledConfigLevel.Drifted => "已漂移",
        InstalledConfigLevel.Unknown => "未知",
        _ => "未安装",
    };

    /// <summary>The apply record behind the claim, kept as history rather than as the headline.</summary>
    public string InstalledConfigDetail => InstalledConfig.Level switch
    {
        InstalledConfigLevel.Verified => $"由 {FormatTime(_manager.LastStatus?.LatestApply?.CreatedAt)} 的应用记录确认"
            + $"（预设 {Friendly(_manager.LatestAppliedPresetName)} · 投影哈希 {Short(_manager.LatestAppliedHash)}）",
        InstalledConfigLevel.Drifted => $"运行文件与最近一次应用记录不一致（记录: {Friendly(_manager.LatestAppliedPresetName)}"
            + $" · {FormatTime(_manager.LastStatus?.LatestApply?.CreatedAt)} · 哈希 {Short(_manager.LatestAppliedHash)}）",
        InstalledConfigLevel.Unknown => _manager.LastStatus?.FixtureInstalled == true
            ? "运行文件存在，但没有可归属的应用记录"
            : "无法确定游戏内配置",
        _ => "CS2 中不存在饰品运行文件",
    };

    public bool IsEditingSameAsActive => !string.IsNullOrEmpty(_manager.WorkingPresetName)
        && string.Equals(_manager.WorkingPresetName, _manager.ActivePresetName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Projection hash of the newest apply record, shown next to the record's own history.</summary>
    public string InstalledHashText => Short(_manager.LatestAppliedHash);

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
        OnPropertyChanged(nameof(InstalledConfigLevel));
        OnPropertyChanged(nameof(InstalledConfigName));
        OnPropertyChanged(nameof(InstalledConfigDetail));
        OnPropertyChanged(nameof(IsEditingSameAsActive));
        OnPropertyChanged(nameof(InstalledHashText));
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

public enum InstalledConfigLevel
{
    /// <summary>No inventories.json in the detected install.</summary>
    NotInstalled,

    /// <summary>The file exists but no apply record can be attributed to it.</summary>
    Unknown,

    /// <summary>The latest record for this path no longer matches the file on disk.</summary>
    Drifted,

    /// <summary>The installed file's SHA256 equals what the newest applicable record wrote.</summary>
    Verified,
}

/// <summary>
/// The only rule the UI may use to claim what CS2 is currently configured with. A file is an
/// unknown input unless an apply record for that same installed path hashes to it, so the claim is
/// derived from the observed status instead of from a second file scan.
/// </summary>
public static class InstalledConfigResolver
{
    public static (InstalledConfigLevel Level, string? PresetPath) Resolve(RuntimeStatus? status, string? workingPresetName)
    {
        if (status is null || !status.FixtureInstalled || status.FixtureSha256 is null)
            return (InstalledConfigLevel.NotInstalled, null);

        var apply = status.LatestApply;
        if (apply is null || string.IsNullOrWhiteSpace(apply.NewSha256))
            return (InstalledConfigLevel.Unknown, null);

        if (!apply.CurrentMatchesNewSha256)
            return (InstalledConfigLevel.Drifted, apply.PresetPath);

        var presetPath = string.IsNullOrWhiteSpace(apply.PresetPath) ? workingPresetName : apply.PresetPath;
        return string.IsNullOrWhiteSpace(presetPath)
            ? (InstalledConfigLevel.Unknown, null)
            : (InstalledConfigLevel.Verified, presetPath);
    }
}
