using System.Text.Json;
using System.Windows;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.ViewModels;

public sealed record BotChoice<T>(T Value, string Label);
public sealed class BotViewModel : ViewModelBase
{
    private readonly BotExperimentService _service;
    private readonly IDialogService _dialogs;
    private readonly Action _refreshHuman;
    private readonly Action<string> _clipboard;
    private BotPreferences _preferences = new();
    private bool _busy, _previewReady, _restoreReady, _active, _running, _blocked;
    private string? _installId;
    private string _state = "未安装 / 请检查安装条件", _feedback = "", _plan = "尚未预览", _installedDifficulty = "—";
    private BotExperimentIdentity _identity = new(false, "");
    private BotModeReadiness _readiness = new(false, "尚未检查", "未读取", "未提供", "未提供", false);
    public BotViewModel(BotExperimentService service, IDialogService dialogs, Action refreshHuman, Action<string>? clipboard = null)
    {
        _service = service; _dialogs = dialogs; _refreshHuman = refreshHuman; _clipboard = clipboard ?? Clipboard.SetText;
        PreviewCommand = new(() => _ = PreviewAsync(), () => !IsBusy && !_active && !_running && _readiness.CanEnable);
        InstallCommand = new(() => _ = InstallAsync(), () => !IsBusy && !_active && !_running && _previewReady);
        DiagnosticsCommand = new(() => _ = DiagnosticsAsync(), () => !IsBusy && _active && _installId is not null && !_running && _service.SteamState.Idle);
        RestoreCommand = new(() => _ = RestoreAsync(), () => !IsBusy && _active && !_running && _restoreReady);
        CopyCommandsCommand = new(CopyCommands, () => !IsBusy);
        OpenLogsCommand = new(() => OpenFolder(_service.LogsRoot));
        OpenBackupCommand = new(() => OpenFolder(BackupPath));
        PreviewModeCommand = new(() => _ = PreviewModeAsync(), () => TargetMode == LocalPlayMode.EnhancedHuman ? PreviewCommand.CanExecute(null) : DiagnosticsCommand.CanExecute(null));
        SwitchModeCommand = new(() => _ = SwitchModeAsync(), () => TargetMode == LocalPlayMode.EnhancedHuman ? InstallCommand.CanExecute(null) : RestoreCommand.CanExecute(null));
        RefreshCommand = new(Observe, () => !IsBusy);
        try { _preferences = service.LoadPreferences(); } catch (Exception ex) { _feedback = "偏好读取失败（保留原文件）：" + ex.Message; }
        Observe();
    }
    public IReadOnlyList<BotChoice<BotDifficulty>> DifficultyChoices { get; } = [new(BotDifficulty.Low,"低 · Low"),new(BotDifficulty.Medium,"中 · Medium"),new(BotDifficulty.High,"高 · High")];
    public IReadOnlyList<BotChoice<LocalPlayMode>> ModeChoices { get; } = [new(LocalPlayMode.NormalHuman,"普通人机 + 我的饰品"),new(LocalPlayMode.EnhancedHuman,"增强人机 + 我的饰品")];
    public LocalPlayMode TargetMode { get => _preferences.TargetMode; set { if (!Enum.IsDefined(value) || value == TargetMode) return; _preferences = _preferences with { TargetMode = value }; _previewReady = _restoreReady = false; _plan = "目标模式已更改，请重新预览。"; Persist(); Notify(); } }
    public IReadOnlyList<BotChoice<BotAim>> AimChoices { get; } = [new(BotAim.Mixed,"混合 · Mixed"),new(BotAim.Head,"头部 · Head"),new(BotAim.Body,"身体 · Body")];
    public IReadOnlyList<BotChoice<BotNades>> NadesChoices { get; } = [new(BotNades.Off,"关闭"),new(BotNades.Less,"较少"),new(BotNades.Normal,"正常"),new(BotNades.More,"较多"),new(BotNades.Max,"最多")];
    public BotDifficulty Difficulty { get => _preferences.Difficulty; set { if (!Enum.IsDefined(value) || value == Difficulty) return; _preferences = _preferences with { Difficulty = value }; _previewReady = false; Persist(); Observe(); } }
    public BotAim Aim { get => _preferences.Aim; set { if (!Enum.IsDefined(value) || value == Aim) return; _preferences = _preferences with { Aim = value }; Persist(); Notify(); } }
    public BotNades Nades { get => _preferences.Nades; set { if (!Enum.IsDefined(value) || value == Nades) return; _preferences = _preferences with { Nades = value }; Persist(); Notify(); } }
    public bool IsBusy => _busy;
    public bool CanSelectDifficulty => !_busy && !_active;
    public bool CanSelectMode => !_busy;
    public bool CanEditCommands => !_busy;
    public string StateText => _running ? "游戏运行不可切换" : _busy ? "检查 / 切换中…" : _state;
    public string CurrentMode => _active ? _identity.RecoveryRequired ? "需恢复 · 事务未完整结束" : _identity.PreservesHuman ? "增强人机 + 我的饰品（文件安装态）" : "旧隔离实验 · Human 暂时隔离" : _readiness.HumanFilesValid ? "普通人机 + 我的饰品" : "环境身份尚未确认";
    public string AvailabilityText => _readiness.Reason;
    public string HumanStatusText => _active ? _identity.PreservesHuman ? "Human 文件与预设保留；先恢复普通模式再写入饰品配置。" : "旧实验隔离了 Human，请通过原事务恢复。" : _readiness.HumanFilesValid ? "Human 文件身份匹配；当前 build 的实机呈现尚未重新验收。" : "Human 文件身份未通过检查，请查看运行状态。";
    public string ProcessText => _running ? "CS2 正在运行" : "CS2 已关闭";
    public string CandidateIdentity
    {
        get { try { var c = _service.Candidate(Difficulty); return $"{(c.PreservesHuman ? "联合候选" : "历史隔离候选，仅供恢复")} · ref {_readiness.BotRef}\n{c.Directory}\nmanifest {c.ManifestSha256}"; } catch (Exception ex) { return "候选不可用：" + ex.Message; } }
    }
    public string TargetBuild
    {
        get
        {
            return $"当前 CS2 {_readiness.CurrentBuild}\n候选 CS2 {_readiness.CandidateBuild}";
        }
    }
    public string InstalledDifficulty => _installedDifficulty;
    public string InstallId => _installId ?? "—";
    public string BackupPath => _installId is null ? _service.BackupRoot : Path.Combine(_service.BackupRoot, _installId);
    public string Feedback => _feedback;
    public string PlanText => _plan;
    public string ConsoleCommands => _preferences.ConsoleCommands;
    public string DifficultyHint => _active ? "已安装的 profile 不可热改；先恢复原环境，再选择难度重新安装。" : "安装前选择；Low / Medium / High 分别使用固定上游 profile，默认 Medium。";
    public string ActionHint => _running ? "请先退出 CS2，再检查切换条件。" : TargetMode == LocalPlayMode.NormalHuman ? _active ? "检查恢复预览后，按精确事务恢复普通模式。" : "当前无需切换；普通模式保留现有 Human 文件。" : _active ? "先恢复普通模式，再更换联合候选或难度。" : !_readiness.CanEnable ? _readiness.Reason : _previewReady ? "切换预览通过；确认后会再次完整校验。" : "先检查切换预览，确认文件清单后再执行。";
    public string SwitchButtonText => TargetMode == LocalPlayMode.NormalHuman ? "恢复普通模式" : "启用增强模式";
    public RelayCommand PreviewCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand DiagnosticsCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand CopyCommandsCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenBackupCommand { get; }
    public RelayCommand PreviewModeCommand { get; }
    public RelayCommand SwitchModeCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private void Persist() { try { _service.SavePreferences(_preferences); } catch (Exception ex) { _feedback = "偏好未保存：" + ex.Message; } }
    public void Observe()
    {
        _running = _service.Cs2Running;
        var identity = _service.Identity;
        _identity = identity;
        _readiness = _service.Readiness(Difficulty);
        if (!_readiness.CanEnable) _previewReady = false;
        var wasActive = _active;
        _active = identity.BlocksHuman;
        if (_installId != identity.InstallId) _restoreReady = false;
        _installId = identity.InstallId;
        if (_active)
        {
            _state = identity.Reason;
            if (identity.InstallId is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(BackupPath,"install-record.json")));
                    var r = doc.RootElement;
                    _installedDifficulty = r.TryGetProperty("profile",out var p) ? p.GetProperty("difficulty").GetString()! : "Medium (legacy)";
                    _state = identity.RecoveryRequired ? "安装/恢复中断 · 保留备份并检查恢复条件" : identity.PreservesHuman ? "已安装待实机验证 · Human 保留" : "已安装待实机验证 · 旧实验隔离 Human";
                }
                catch (Exception ex) { _state = "文件/记录漂移阻断：" + ex.Message; }
            }
        }
        else if (wasActive) { _state = "已恢复 · Human 实机呈现仍需检查"; _installedDifficulty = "—"; }
        else _state = _previewReady ? "待切换 · 完整预览通过" : _readiness.HumanFilesValid ? "普通模式 · Human 实机呈现仍需检查" : "环境条件未通过检查";
        if (_blocked) _state = "文件漂移 / 条件阻断（见下方反馈）";
        Notify();
    }
    public Task PreviewAsync() => Run(async () =>
    {
        _previewReady = false; _restoreReady = false;
        var result = await _service.ExecuteAsync("Preview", Difficulty);
        _plan = Describe(result); _previewReady = true; _state = "待切换 · 完整预览通过"; _feedback = "预览完成，未修改游戏文件。";
    });
    public Task InstallAsync() => Run(async () =>
    {
        if (!_previewReady || _active) throw new InvalidOperationException("先完成当前难度的完整安装预览");
        if (!_dialogs.Confirm("启用增强人机 + 我的饰品", $"{Difficulty}\n{CandidateIdentity}\n\n{_plan.Split('\n')[0]}（完整路径见页面预览）\n\n保留 Human 插件、fixture 与私人预设；按同一事务备份并切换。当前 build 的文件、Steam 更新和恢复门禁会再次检查。游戏内效果与性能待实机验证。是否继续？")) return;
        _previewReady = false;
        var result = await _service.ExecuteAsync("Install", Difficulty, true);
        _installId = result.GetProperty("installId").GetString();
        _preferences = _preferences with { LastInstallId = _installId }; Persist();
        _feedback = "联合模式文件已安装并复核；未启动 CS2。请保留 install ID 和备份，实机效果待验证。";
    });
    public Task DiagnosticsAsync() => Run(async () =>
    {
        _restoreReady = false;
        var result = await _service.ExecuteAsync("Diagnostics", Difficulty, installId: _installId);
        _plan = Describe(result); _restoreReady = true; _feedback = "恢复预览通过，未修改游戏文件；恢复时会再次完整校验。";
    });
    public Task RestoreAsync() => Run(async () =>
    {
        if (!_restoreReady || !_active || _installId is null) throw new InvalidOperationException("先检查精确事务的恢复条件");
        if (!_dialogs.Confirm("恢复普通人机 + 我的饰品", $"install ID: {_installId}\n备份: {BackupPath}\n{_plan.Split('\n')[0]}（完整路径见页面预览）\n\n精确恢复本事务记录的 framework 与 gameinfo，保留 Human 和预设；文件漂移将拒绝恢复。是否继续？")) return;
        _restoreReady = false;
        await _service.ExecuteAsync("Restore", Difficulty, true, _installId);
        _state = "已恢复 · Human 实机呈现仍需检查"; _installedDifficulty = "—"; _feedback = "原文件已按精确事务恢复。Bot 本局偏好保留。";
    });
    public Task PreviewModeAsync() => TargetMode == LocalPlayMode.NormalHuman ? DiagnosticsAsync() : PreviewAsync();
    public Task SwitchModeAsync() => TargetMode == LocalPlayMode.NormalHuman ? RestoreAsync() : InstallAsync();
    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; Notify();
        try { _blocked = false; Observe(); if (_running) throw new InvalidOperationException("CS2 正在运行，禁止文件操作"); await action(); }
        catch (Exception ex) { _blocked = true; _previewReady = _restoreReady = false; _state = "文件漂移 / 条件阻断"; _feedback = ex.Message; }
        finally { _busy = false; Observe(); _refreshHuman(); Notify(); }
    }
    private static string Describe(JsonElement result)
    {
        if (!result.TryGetProperty("entries",out var entries)) return result.GetRawText();
        var groups = entries.EnumerateArray().GroupBy(e => e.GetProperty("action").GetString());
        return string.Join(" · ", groups.Select(g => $"{g.Key}: {g.Count()}")) + "\n" + string.Join("\n",entries.EnumerateArray().Select(e => $"{e.GetProperty("action").GetString()}  {e.GetProperty("target").GetString()}"));
    }
    private void CopyCommands()
    {
        try { _clipboard(ConsoleCommands); _feedback = "复制成功；请在本地游戏控制台执行。尚未确认游戏中生效（Head 待实机验证）。"; }
        catch (Exception ex) { _feedback = "复制失败：" + ex.Message; }
        Notify();
    }
    private void OpenFolder(string path)
    {
        try { BotExperimentService.CheckNoLinks(path); if (!Directory.Exists(path)) throw new DirectoryNotFoundException("位置尚未创建：" + path); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false }); }
        catch (Exception ex) { _feedback = ex.Message; Notify(); }
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(TargetMode),nameof(CurrentMode),nameof(CanSelectMode),nameof(AvailabilityText),nameof(HumanStatusText),nameof(SwitchButtonText),nameof(Difficulty),nameof(Aim),nameof(Nades),nameof(IsBusy),nameof(CanSelectDifficulty),nameof(CanEditCommands),nameof(StateText),nameof(ProcessText),nameof(TargetBuild),nameof(CandidateIdentity),nameof(InstalledDifficulty),nameof(InstallId),nameof(BackupPath),nameof(Feedback),nameof(PlanText),nameof(ConsoleCommands),nameof(DifficultyHint),nameof(ActionHint) }) OnPropertyChanged(name);
        foreach (var cmd in new[] { PreviewCommand,InstallCommand,DiagnosticsCommand,RestoreCommand,CopyCommandsCommand,PreviewModeCommand,SwitchModeCommand,RefreshCommand }) cmd.RaiseCanExecuteChanged();
    }
}
