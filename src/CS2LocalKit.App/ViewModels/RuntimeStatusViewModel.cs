using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Catalog;
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

    /// <summary>What the installed fixture is proven to carry; see <see cref="InstalledConfigResolver"/>.</summary>
    public string InstalledConfigText
    {
        get
        {
            var (level, presetPath) = InstalledConfigResolver.Resolve(Status, _manager.WorkingPresetName);
            return level switch
            {
                InstalledConfigLevel.Verified => HeaderViewModel.Friendly(presetPath),
                InstalledConfigLevel.Drifted => "已漂移",
                InstalledConfigLevel.Unknown => "未知",
                _ => "未安装",
            };
        }
    }

    public string InstalledConfigEvidence
    {
        get
        {
            var (level, _) = InstalledConfigResolver.Resolve(Status, _manager.WorkingPresetName);
            return level switch
            {
                InstalledConfigLevel.Verified => "当前文件哈希 = 最近应用记录写入的哈希",
                InstalledConfigLevel.Drifted => "当前文件哈希 ≠ 最近应用记录写入的哈希",
                InstalledConfigLevel.Unknown => Status?.LatestApply is null
                    ? "没有对应此安装路径的应用记录"
                    : "应用记录缺少 installed.newSha256",
                _ => "运行文件不存在",
            };
        }
    }

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

    public bool IsAcceptedFrameworkValid =>
        (Status?.MetaModNativeStatus?.StartsWith("hash-match") ?? false)
        && (Status?.CounterStrikeSharpNativeStatus?.StartsWith("hash-match") ?? false)
        && (Status?.InventorySimulatorPluginPresent ?? false)
        && (Status?.PatchedDllMatch == "match");

    public bool CanRepairMetaModStartup =>
        Cs2Detected
        && !Cs2Running
        && !GameinfoHasMetamod
        && IsAcceptedFrameworkValid;

    public string RepairMetaModStartupToolTip
    {
        get
        {
            if (!Cs2Detected) return "未检测到 CS2 安装路径";
            if (Cs2Running) return "CS2 正在运行，请先关闭游戏";
            if (GameinfoHasMetamod) return "MetaMod 启动项已正确配置";
            if (!IsAcceptedFrameworkValid)
            {
                if (!(Status?.MetaModNativeStatus?.StartsWith("hash-match") ?? false))
                    return $"MetaMod 原生组件异常 ({MetaModStatus})，拒绝修复";
                if (!(Status?.CounterStrikeSharpNativeStatus?.StartsWith("hash-match") ?? false))
                    return $"CounterStrikeSharp 组件异常 ({CssStatus})，拒绝修复";
                if (!InventorySimulatorPluginPresent)
                    return "InventorySimulator 插件文件缺失，拒绝修复";
                if (PatchedDllMatch != "match")
                    return "InventorySimulator patched DLL 校验不匹配，拒绝修复";
                return "运行组件身份未通过校验，拒绝修复启动项";
            }
            return "在 gameinfo.gi 的 SearchPaths 中恢复 MetaMod 启动项";
        }
    }

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
    public RelayCommand RepairMetaModStartupCommand { get; }

    /// <summary>Local catalog snapshot state. Data preparation lives here, not in the game tree.</summary>
    public string CatalogCommit => CatalogSnapshot.PinnedCommit;
    public string CatalogCacheRoot => _manager.Services.CatalogCacheRoot;
    public string CatalogLocales
    {
        get
        {
            var catalog = _manager.Catalog;
            return catalog is null ? "不可用" : string.Join(" + ", catalog.LocalesLoaded);
        }
    }
    public bool CatalogAvailable => _manager.Catalog is not null;
    public string CatalogCounts
    {
        get
        {
            var c = _manager.Catalog;
            return c is null ? _manager.Services.CatalogError ?? "清单不可用，保存与应用会被阻断"
                : $"{c.WeaponCount} 个武器 · {c.PaintCount} 个皮肤（{c.LocalizedPaintCount} 个含中文名）· {c.MusicKitCount} 个音乐盒";
        }
    }
    public string MusicKitCatalogState => _manager.Catalog is null ? "未载入" : $"{_manager.Catalog.MusicKitCount} 条";
    public string ChineseCoverage
    {
        get
        {
            var c = _manager.Catalog;
            if (c is null || c.PaintCount == 0) return "—";
            return $"{(double)c.LocalizedPaintCount / c.PaintCount:P0}";
        }
    }
    public string ImageCacheRoot => _manager.Services.Images.CacheRoot;
    public string ImageCacheState => $"{_manager.Services.Images.CachedImageCount} 张已缓存";

    public RuntimeStatusViewModel(PresetManagerService manager)
    {
        _manager = manager;

        RefreshCommand = new RelayCommand(ExecuteRefresh);
        RestoreLatestCommand = new RelayCommand(ExecuteRestoreLatest, () => RollbackCanExecute);
        RepairMetaModStartupCommand = new RelayCommand(ExecuteRepairMetaModStartup, () => CanRepairMetaModStartup);

        _manager.StatusRefreshed += (s, e) => RefreshProperties();
        _manager.WorkingPresetChanged += (s, e) => RefreshProperties();
    }

    /// <summary>Called after an explicit catalog preparation so the data section refreshes.</summary>
    public void RefreshAfterCatalogChange() => RefreshProperties();

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

    private void ExecuteRepairMetaModStartup()
    {
        var result = _manager.RepairMetaModStartup();
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
        OnPropertyChanged(nameof(InstalledConfigText));
        OnPropertyChanged(nameof(InstalledConfigEvidence));
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
        OnPropertyChanged(nameof(IsAcceptedFrameworkValid));
        OnPropertyChanged(nameof(CanRepairMetaModStartup));
        OnPropertyChanged(nameof(RepairMetaModStartupToolTip));
        OnPropertyChanged(nameof(CatalogAvailable));
        OnPropertyChanged(nameof(CatalogLocales));
        OnPropertyChanged(nameof(CatalogCounts));
        OnPropertyChanged(nameof(MusicKitCatalogState));
        OnPropertyChanged(nameof(ChineseCoverage));
        OnPropertyChanged(nameof(ImageCacheState));
        OnPropertyChanged(nameof(ImageCacheRoot));

        RestoreLatestCommand.RaiseCanExecuteChanged();
        RepairMetaModStartupCommand.RaiseCanExecuteChanged();
    }

    private void ShowFeedback(bool success, string message)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}
