using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.ViewModels;

public sealed class RuntimeStatusViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;
    private string _statusMessage = "";
    private bool _isStatusSuccess;
    private bool _isStatusVisible;

    public PresetManagerService Manager => _manager;

    public RuntimeStatus? Status => _manager.LastStatus;

    public RuntimeHealthLevel HealthLevel => _manager.RuntimeHealth;
    public string HealthSummary => _manager.RuntimeSummary;

    // CS2
    public bool Cs2Detected => Status?.Cs2Detected ?? false;
    public string Cs2Path => Status?.Cs2Root ?? "未找到 CS2 安装路径";
    public bool Cs2Running => Status?.Cs2Running ?? false;
    public string Cs2RunningText => Cs2Running ? "正在运行 (禁止修改运行配置)" : "已关闭";
    public string PatchVersion => Status?.PatchVersion ?? "-";
    public string ClientVersion => Status?.ClientVersion ?? "-";
    public string BuildId => Status?.BuildId ?? "-";
    public string TestedBuildMatch => Status?.TestedBuildMatch ?? "unknown";

    // Components
    public bool GameinfoHasMetamod => Status?.GameinfoHasMetamod ?? false;
    public string MetaModStatus => Status?.MetaModNativeStatus ?? "missing";
    public string CssStatus => Status?.CounterStrikeSharpNativeStatus ?? "missing";
    public bool InventorySimulatorPluginPresent => Status?.InventorySimulatorPluginPresent ?? false;
    public string InventorySimulatorStatusText => InventorySimulatorPluginPresent ? "已安装" : "未安装";
    public string PatchedDllMatch => Status?.PatchedDllMatch ?? "unknown";
    public string PatchedDllHash => Status?.InventorySimulatorDllSha256 ?? "-";
    public bool FixtureInstalled => Status?.FixtureInstalled ?? false;
    public string FixtureStatusText => FixtureInstalled ? "已安装" : "未安装";
    public string FixtureHash => Status?.FixtureSha256 ?? "-";

    // Active preset
    public string ActivePreset => Status?.ActivePreset ?? "未激活任何预设";
    public bool ActivePresetExists => Status?.ActivePresetExists ?? false;
    public bool IsWorkingPresetDirty => _manager.IsDirty;

    // Latest Apply & Rollback
    public bool LatestApplyExists => Status?.LatestApply != null;
    public string LatestApplyPreset => Status?.LatestApply?.PresetPath ?? "-";
    public string LatestApplyTime
    {
        get
        {
            var raw = Status?.LatestApply?.CreatedAt;
            return DateTimeOffset.TryParse(raw, out var time)
                ? time.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : raw ?? "-";
        }
    }
    public string LatestApplyHash => Status?.LatestApply?.ProjectedSha256 ?? "-";
    public bool BackupPresent => Status?.LatestApply?.BackupPresent ?? false;
    public string BackupStatusText => !LatestApplyExists ? "无应用记录" : BackupPresent ? "就绪" : "缺失";
    public bool CurrentMatchesLatestHash => Status?.LatestApply?.CurrentMatchesNewSha256 ?? false;
    public string LatestHashStatusText => !LatestApplyExists ? "无应用记录" : CurrentMatchesLatestHash ? "匹配" : "不匹配";
    public bool BlockedByCs2Running => Status?.LatestApply?.BlockedByCs2Running ?? false;
    public bool RollbackCanExecute => Status?.LatestApply?.RollbackCanExecute ?? false;
    public string RollbackReason => Status?.LatestApply?.RollbackBlockReason ?? (RollbackCanExecute ? "可安全恢复" : "不可恢复");

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusSuccess
    {
        get => _isStatusSuccess;
        set => SetProperty(ref _isStatusSuccess, value);
    }

    public bool IsStatusVisible
    {
        get => _isStatusVisible;
        set => SetProperty(ref _isStatusVisible, value);
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand RestoreLatestCommand { get; }

    public RuntimeStatusViewModel(PresetManagerService manager)
    {
        _manager = manager;

        RefreshCommand = new RelayCommand(ExecuteRefresh);
        RestoreLatestCommand = new RelayCommand(ExecuteRestoreLatest, () => RollbackCanExecute);

        _manager.StatusRefreshed += (s, e) => RefreshProperties();
        _manager.WorkingPresetChanged += (s, e) => RefreshProperties();
    }

    private void ExecuteRefresh()
    {
        _manager.RefreshRuntimeStatus();
        ShowFeedback(true, "运行状态已刷新");
    }

    private void ExecuteRestoreLatest()
    {
        var result = _manager.RestoreLatest();
        ShowFeedback(result.Success, result.Message);
    }

    private void RefreshProperties()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HealthLevel));
        OnPropertyChanged(nameof(HealthSummary));
        OnPropertyChanged(nameof(Cs2Detected));
        OnPropertyChanged(nameof(Cs2Path));
        OnPropertyChanged(nameof(Cs2Running));
        OnPropertyChanged(nameof(Cs2RunningText));
        OnPropertyChanged(nameof(PatchVersion));
        OnPropertyChanged(nameof(ClientVersion));
        OnPropertyChanged(nameof(BuildId));
        OnPropertyChanged(nameof(TestedBuildMatch));
        OnPropertyChanged(nameof(GameinfoHasMetamod));
        OnPropertyChanged(nameof(MetaModStatus));
        OnPropertyChanged(nameof(CssStatus));
        OnPropertyChanged(nameof(InventorySimulatorPluginPresent));
        OnPropertyChanged(nameof(InventorySimulatorStatusText));
        OnPropertyChanged(nameof(PatchedDllMatch));
        OnPropertyChanged(nameof(PatchedDllHash));
        OnPropertyChanged(nameof(FixtureInstalled));
        OnPropertyChanged(nameof(FixtureStatusText));
        OnPropertyChanged(nameof(FixtureHash));
        OnPropertyChanged(nameof(ActivePreset));
        OnPropertyChanged(nameof(ActivePresetExists));
        OnPropertyChanged(nameof(IsWorkingPresetDirty));
        OnPropertyChanged(nameof(LatestApplyExists));
        OnPropertyChanged(nameof(LatestApplyPreset));
        OnPropertyChanged(nameof(LatestApplyTime));
        OnPropertyChanged(nameof(LatestApplyHash));
        OnPropertyChanged(nameof(BackupPresent));
        OnPropertyChanged(nameof(BackupStatusText));
        OnPropertyChanged(nameof(CurrentMatchesLatestHash));
        OnPropertyChanged(nameof(LatestHashStatusText));
        OnPropertyChanged(nameof(BlockedByCs2Running));
        OnPropertyChanged(nameof(RollbackCanExecute));
        OnPropertyChanged(nameof(RollbackReason));

        RestoreLatestCommand.RaiseCanExecuteChanged();
    }

    private void ShowFeedback(bool success, string message)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}
