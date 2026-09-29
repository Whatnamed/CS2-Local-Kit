using CS2LocalKit.App.Common;
using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.Services;

public class PresetManagerService : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IDialogService _dialogService;
    private PresetDraft? _draft;
    private string? _workingPresetName;
    private string? _activePresetName;
    private HumanPreset? _lastSavedPreset;
    private string? _installedProjectionSha256;
    private string? _latestAppliedPresetName;
    private string? _latestAppliedTime;
    private string? _latestAppliedHash;
    private bool _rollbackCanExecute;
    private string? _rollbackBlockReason;
    private bool _cs2Running;
    private RuntimeHealthLevel _runtimeHealth = RuntimeHealthLevel.Blocked;
    private string _runtimeSummary = "正在初始化...";
    private RuntimeStatus? _lastStatus;

    public AppServices Services => _services;
    public IDialogService DialogService => _dialogService;
    public CatalogIndex? Catalog => _services.Catalog;

    public PresetDraft? Draft
    {
        get => _draft;
        private set
        {
            if (_draft is not null)
            {
                _draft.PropertyChanged -= Draft_PropertyChanged;
            }
            if (SetProperty(ref _draft, value))
            {
                if (_draft is not null)
                {
                    _draft.PropertyChanged += Draft_PropertyChanged;
                }
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public bool IsDirty => Draft?.IsDirty ?? false;

    public string? WorkingPresetName
    {
        get => _workingPresetName;
        private set => SetProperty(ref _workingPresetName, value);
    }

    public string? ActivePresetName
    {
        get => _activePresetName;
        private set => SetProperty(ref _activePresetName, value);
    }

    public HumanPreset? LastSavedPreset
    {
        get => _lastSavedPreset;
        private set => SetProperty(ref _lastSavedPreset, value);
    }

    public string? InstalledProjectionSha256
    {
        get => _installedProjectionSha256;
        private set => SetProperty(ref _installedProjectionSha256, value);
    }

    public string? LatestAppliedPresetName
    {
        get => _latestAppliedPresetName;
        private set => SetProperty(ref _latestAppliedPresetName, value);
    }

    public string? LatestAppliedTime
    {
        get => _latestAppliedTime;
        private set => SetProperty(ref _latestAppliedTime, value);
    }

    public string? LatestAppliedHash
    {
        get => _latestAppliedHash;
        private set => SetProperty(ref _latestAppliedHash, value);
    }

    public bool RollbackCanExecute
    {
        get => _rollbackCanExecute;
        private set => SetProperty(ref _rollbackCanExecute, value);
    }

    public string? RollbackBlockReason
    {
        get => _rollbackBlockReason;
        private set => SetProperty(ref _rollbackBlockReason, value);
    }

    public bool Cs2Running
    {
        get => _cs2Running;
        private set => SetProperty(ref _cs2Running, value);
    }

    public RuntimeHealthLevel RuntimeHealth
    {
        get => _runtimeHealth;
        private set => SetProperty(ref _runtimeHealth, value);
    }

    public string RuntimeSummary
    {
        get => _runtimeSummary;
        private set => SetProperty(ref _runtimeSummary, value);
    }

    public RuntimeStatus? LastStatus
    {
        get => _lastStatus;
        private set => SetProperty(ref _lastStatus, value);
    }

    public event EventHandler? WorkingPresetChanged;
    public event EventHandler? StatusRefreshed;
    public event EventHandler? CatalogReloaded;

    /// <summary>
    /// Re-reads the pinned snapshot after an explicit catalog preparation and refreshes the editor
    /// so localized display names pick up. Validation identity semantics are unchanged: the same
    /// numeric ids, with more metadata attached.
    /// </summary>
    public CatalogIndex? ReloadCatalog()
    {
        var index = _services.ReloadCatalog();
        if (index is not null && !IsDirty && WorkingPresetName is { } name && _services.PresetStore.Exists(name))
        {
            LoadPreset(name, force: true);
        }
        CatalogReloaded?.Invoke(this, EventArgs.Empty);
        return index;
    }

    public PresetManagerService(AppServices services, IDialogService dialogService)
    {
        _services = services;
        _dialogService = dialogService;
        RefreshRuntimeStatus();
        InitDefaultPreset();
    }

    private void Draft_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresetDraft.IsDirty))
        {
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    private void InitDefaultPreset()
    {
        ActivePresetName = _services.ActivePresetState.GetActive();
        var targetPreset = ActivePresetName;

        if (string.IsNullOrEmpty(targetPreset))
        {
            var presets = _services.PresetStore.ListNames();
            if (presets.Count > 0)
            {
                targetPreset = presets[0];
            }
        }

        if (!string.IsNullOrEmpty(targetPreset) && _services.PresetStore.Exists(targetPreset))
        {
            LoadPreset(targetPreset, force: true);
        }
        else
        {
            // If no preset exists at all, create a minimal template draft in memory
            var minimal = HumanPresetTemplate.CreateMinimalValid();
            Draft = PresetDraft.FromHumanPreset("untitled.v1.json", minimal, _services.Catalog);
            WorkingPresetName = "untitled.v1.json";
            Draft.MarkClean();
        }
    }

    public bool LoadPreset(string presetName, bool force = false)
    {
        if (string.Equals(WorkingPresetName, presetName, StringComparison.OrdinalIgnoreCase) && !force)
        {
            return true;
        }

        if (IsDirty && !force)
        {
            if (!ResolveUnsavedChanges())
                return false;
        }

        try
        {
            var preset = _services.PresetStore.Load(presetName);
            Draft = PresetDraft.FromHumanPreset(presetName, preset, _services.Catalog);
            WorkingPresetName = presetName;
            LastSavedPreset = preset;
            Draft.MarkClean();
            WorkingPresetChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("加载预设失败", $"无法加载预设 {presetName}:\n{ex.Message}");
            return false;
        }
    }
    public bool ResolveUnsavedChanges()
    {
        if (!IsDirty) return true;

        var resolution = _dialogService.ConfirmUnsavedChanges(WorkingPresetName ?? "当前预设");
        switch (resolution)
        {
            case UnsavedChangesResolution.SaveAndSwitch:
                var saveResult = Save();
                if (!saveResult.Success)
                {
                    _dialogService.ShowError("保存失败", saveResult.Message);
                    return false;
                }
                return true;
            case UnsavedChangesResolution.DiscardAndSwitch:
                return true;
            case UnsavedChangesResolution.Cancel:
            default:
                return false;
        }
    }


    public OperationResult Save()
    {
        if (Draft is null || string.IsNullOrEmpty(WorkingPresetName))
            return OperationResult.Failed("未加载任何可编辑预设");

        HumanPreset preset;
        try
        {
            preset = Draft.ToHumanPreset();
        }
        catch (Exception ex)
        {
            return OperationResult.Failed($"构造预设失败: {ex.Message}");
        }

        var domainProblems = HumanPresetValidator.ValidateDomain(preset);
        if (domainProblems.Count > 0)
            return OperationResult.Failed("预设结构校验未通过:\n" + string.Join("\n", domainProblems));

        if (_services.Catalog is not null)
        {
            var catalogProblems = HumanPresetValidator.Validate(preset, _services.Catalog);
            if (catalogProblems.Count > 0)
                return OperationResult.Failed("物品清单校验未通过:\n" + string.Join("\n", catalogProblems));
        }
        else
        {
            return OperationResult.Failed("皮肤数据清单不可用，无法通过保存校验");
        }

        try
        {
            _services.PresetStore.Save(WorkingPresetName, preset);
            Draft.MarkClean();
            LastSavedPreset = preset;
            OnPropertyChanged(nameof(IsDirty));
            return OperationResult.Succeeded($"预设 [{WorkingPresetName}] 保存成功");
        }
        catch (Exception ex)
        {
            return OperationResult.Failed($"写入预设文件失败: {ex.Message}");
        }
    }

    public OperationResult SaveAs(string newPresetName)
    {
        if (Draft is null)
            return OperationResult.Failed("未加载任何可编辑预设");

        if (string.IsNullOrWhiteSpace(newPresetName))
            return OperationResult.Failed("预设名称不能为空");

        var oldName = WorkingPresetName;
        WorkingPresetName = newPresetName;
        Draft.WorkingPresetName = newPresetName;

        var result = Save();
        if (!result.Success)
        {
            WorkingPresetName = oldName;
            Draft.WorkingPresetName = oldName ?? "";
            return result;
        }

        WorkingPresetChanged?.Invoke(this, EventArgs.Empty);
        return OperationResult.Succeeded($"预设另存为 [{newPresetName}] 成功");
    }

    public OperationResult Apply()
    {
        if (Draft is null || string.IsNullOrEmpty(WorkingPresetName))
            return OperationResult.Failed("未加载任何预设");

        RefreshRuntimeStatus();
        if (Cs2Running)
            return OperationResult.Failed("CS2 正在运行，无法应用修改。请先关闭 CS2 游戏。");

        if (_services.Catalog is null)
            return OperationResult.Failed("皮肤数据清单不可用，无法应用饰品配置");

        if (IsDirty)
        {
            var saveResult = Save();
            if (!saveResult.Success)
                return OperationResult.Failed($"自动保存当前修改失败，已中止应用:\n{saveResult.Message}");
        }

        try
        {
            var preset = Draft.ToHumanPreset();
            var steamId = _services.GetSteamId64();
            var record = _services.FixtureApplier.Apply(preset, steamId, WorkingPresetName);
            RefreshRuntimeStatus();

            var shortHash = record.ProjectedSha256.Length > 12 ? record.ProjectedSha256[..12] : record.ProjectedSha256;
            return OperationResult.Succeeded(
                $"成功应用预设 [{WorkingPresetName}] 到 CS2！\n" +
                $"投影哈希: {shortHash}...\n" +
                $"应用时间: {record.CreatedAt}\n" +
                $"可执行回滚: 是");
        }
        catch (ApplyException ex)
        {
            RefreshRuntimeStatus();
            return OperationResult.Failed($"应用到 CS2 失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            RefreshRuntimeStatus();
            return OperationResult.Failed($"应用发生异常: {ex.Message}");
        }
    }

    public OperationResult RestoreLatest()
    {
        RefreshRuntimeStatus();
        if (Cs2Running)
            return OperationResult.Failed("CS2 正在运行，无法恢复上一次配置。请关闭 CS2 后再试。");

        if (!RollbackCanExecute)
        {
            var reason = RollbackBlockReason ?? "当前状态不可执行恢复";
            return OperationResult.Failed($"无法恢复: {reason}");
        }

        try
        {
            var record = _services.FixtureApplier.RestoreLatest();
            RefreshRuntimeStatus();
            return OperationResult.Succeeded($"已成功恢复上一次应用 (备份时间: {record.CreatedAt})");
        }
        catch (Exception ex)
        {
            RefreshRuntimeStatus();
            return OperationResult.Failed($"恢复备份失败: {ex.Message}");
        }
    }

    public void SetActive(string presetName)
    {
        _services.ActivePresetState.SetActive(presetName);
        ActivePresetName = presetName;
        RefreshRuntimeStatus();
    }

    public void RefreshRuntimeStatus()
    {
        var status = _services.RuntimeStatusService.GetStatus();
        LastStatus = status;

        Cs2Running = status.Cs2Running;
        InstalledProjectionSha256 = status.FixtureSha256;
        ActivePresetName = status.ActivePreset;

        if (status.LatestApply is { } la)
        {
            LatestAppliedPresetName = la.PresetPath;
            LatestAppliedTime = la.CreatedAt;
            LatestAppliedHash = la.ProjectedSha256;
            RollbackCanExecute = la.RollbackCanExecute;
            RollbackBlockReason = la.RollbackBlockReason;
        }
        else
        {
            LatestAppliedPresetName = null;
            LatestAppliedTime = null;
            LatestAppliedHash = null;
            RollbackCanExecute = false;
            RollbackBlockReason = "暂无应用记录可恢复";
        }

        RuntimeHealth = status.HealthLevel;
        RuntimeSummary = status.HealthSummary;
        StatusRefreshed?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>
    /// Lightweight process state check. Queries cs2.exe status and only triggers a full
    /// runtime status refresh when running state changes.
    /// </summary>
    public bool PollProcessState()
    {
        bool isRunning = _services.RuntimeStatusService.CheckCs2Running();
        if (isRunning != Cs2Running)
        {
            RefreshRuntimeStatus();
            return true;
        }
        return false;
    }
}
