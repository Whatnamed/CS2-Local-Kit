using System.Text.Json;
using System.Windows;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;

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
    public BotViewModel(BotExperimentService service, IDialogService dialogs, Action refreshHuman, Action<string>? clipboard = null)
    {
        _service = service; _dialogs = dialogs; _refreshHuman = refreshHuman; _clipboard = clipboard ?? Clipboard.SetText;
        PreviewCommand = new(() => _ = PreviewAsync(), () => !IsBusy && !_active && !_running);
        InstallCommand = new(() => _ = InstallAsync(), () => !IsBusy && !_active && !_running && _previewReady);
        DiagnosticsCommand = new(() => _ = DiagnosticsAsync(), () => !IsBusy && _installId is not null && !_running);
        RestoreCommand = new(() => _ = RestoreAsync(), () => !IsBusy && _active && !_running && _restoreReady);
        CopyCommandsCommand = new(CopyCommands, () => !IsBusy);
        OpenLogsCommand = new(() => OpenFolder(_service.LogsRoot));
        OpenBackupCommand = new(() => OpenFolder(BackupPath));
        try { _preferences = service.LoadPreferences(); } catch (Exception ex) { _feedback = "偏好读取失败（保留原文件）：" + ex.Message; }
        Observe();
    }
    public IReadOnlyList<BotChoice<BotDifficulty>> DifficultyChoices { get; } = [new(BotDifficulty.Low,"低 · Low"),new(BotDifficulty.Medium,"中 · Medium"),new(BotDifficulty.High,"高 · High")];
    public IReadOnlyList<BotChoice<BotAim>> AimChoices { get; } = [new(BotAim.Mixed,"混合 · Mixed"),new(BotAim.Head,"头部 · Head"),new(BotAim.Body,"身体 · Body")];
    public IReadOnlyList<BotChoice<BotNades>> NadesChoices { get; } = [new(BotNades.Off,"关闭"),new(BotNades.Less,"较少"),new(BotNades.Normal,"正常"),new(BotNades.More,"较多"),new(BotNades.Max,"最多")];
    public BotDifficulty Difficulty { get => _preferences.Difficulty; set { if (!Enum.IsDefined(value) || value == Difficulty) return; _preferences = _preferences with { Difficulty = value }; _previewReady = false; Persist(); Notify(); } }
    public BotAim Aim { get => _preferences.Aim; set { if (!Enum.IsDefined(value) || value == Aim) return; _preferences = _preferences with { Aim = value }; Persist(); Notify(); } }
    public BotNades Nades { get => _preferences.Nades; set { if (!Enum.IsDefined(value) || value == Nades) return; _preferences = _preferences with { Nades = value }; Persist(); Notify(); } }
    public bool IsBusy => _busy;
    public bool CanSelectDifficulty => !_busy && !_active;
    public bool CanEditCommands => !_busy;
    public string StateText => _running ? "游戏运行不可操作（文件操作已禁用）" : _busy ? "检查 / 操作中…" : _state;
    public string ProcessText => _running ? "CS2 正在运行" : "CS2 已关闭";
    public string CandidateIdentity
    {
        get { try { var c = _service.Candidate(Difficulty); return $"{c.Directory}\nmanifest {c.ManifestSha256}"; } catch (Exception ex) { return "候选不可用：" + ex.Message; } }
    }
    public string TargetBuild
    {
        get
        {
            try
            {
                var inf = File.ReadAllLines(Path.Combine(_service.Cs2Root,"game/csgo/steam.inf"));
                var acf = File.ReadAllText(Path.GetFullPath(Path.Combine(_service.Cs2Root,"../..","appmanifest_730.acf")));
                var build = System.Text.RegularExpressions.Regex.Match(acf,"\"buildid\"\\s+\"(\\d+)\"").Groups[1].Value;
                return "本机 " + string.Join(" · ",inf.Where(s => s.StartsWith("PatchVersion=") || s.StartsWith("ClientVersion="))) + " · Build " + build + "\n目标 1.41.9.1 / 2000933 / 25966348；完整身份以预览校验为准";
            }
            catch { return "本机 build 未读取；目标 1.41.9.1 / 2000933 / 25966348，禁止跳过预览"; }
        }
    }
    public string InstalledDifficulty => _installedDifficulty;
    public string InstallId => _installId ?? "—";
    public string BackupPath => _installId is null ? _service.BackupRoot : Path.Combine(_service.BackupRoot, _installId);
    public string Feedback => _feedback;
    public string PlanText => _plan;
    public string ConsoleCommands => _preferences.ConsoleCommands;
    public string DifficultyHint => _active ? "已安装的 profile 不可热改；先恢复原环境，再选择难度重新安装。" : "安装前选择；Low / Medium / High 分别使用固定上游 profile，默认 Medium。";
    public string ActionHint => _running ? "请先关闭 CS2，再检查安装或恢复条件。" : _active ? "Human 暂时隔离；请先检查恢复条件，再按精确 install ID 恢复。" : _previewReady ? "安装预览通过；安装前会再次完整校验。" : "先检查/预览安装；缺包、文件漂移或版本不匹配时拒绝。";
    public RelayCommand PreviewCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand DiagnosticsCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand CopyCommandsCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenBackupCommand { get; }

    private void Persist() { try { _service.SavePreferences(_preferences); } catch (Exception ex) { _feedback = "偏好未保存：" + ex.Message; } }
    public void Observe()
    {
        _running = _service.Cs2Running;
        var identity = _service.Identity;
        var wasActive = _active;
        _active = identity.BlocksHuman;
        _installId = identity.InstallId ?? _preferences.LastInstallId;
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
                    _state = r.GetProperty("state").GetString() == "installed" ? "已安装待实机验证 · Human 暂时隔离" : "安装/恢复中断 · 保留备份并检查恢复条件";
                }
                catch (Exception ex) { _state = "文件/记录漂移阻断：" + ex.Message; }
            }
        }
        else if (wasActive) { _state = "已恢复 · Human 实机呈现仍需检查"; _installedDifficulty = "—"; }
        if (_blocked) _state = "文件漂移 / 条件阻断（见下方反馈）";
        Notify();
    }
    public Task PreviewAsync() => Run(async () =>
    {
        _previewReady = false; _restoreReady = false;
        var result = await _service.ExecuteAsync("Preview", Difficulty);
        _plan = Describe(result); _previewReady = true; _state = "待安装 · 完整预览通过"; _feedback = "预览完成，未修改游戏文件。";
    });
    public Task InstallAsync() => Run(async () =>
    {
        if (!_previewReady || _active) throw new InvalidOperationException("先完成当前难度的完整安装预览");
        if (!_dialogs.Confirm("安装 Bot 实验版", $"{Difficulty} · e18c029 · MetaMod 1481 / CSS 376\n{CandidateIdentity}\n\n{_plan.Split('\n')[0]}（完整路径见页面预览）\n\nHuman 和其他可执行插件将暂时隔离；同一事务先备份再写入。恢复后回到 MetaMod 1473。游戏内生效与性能尚未验证。是否安装？")) return;
        _previewReady = false;
        var result = await _service.ExecuteAsync("Install", Difficulty, true);
        _installId = result.GetProperty("installId").GetString();
        _preferences = _preferences with { LastInstallId = _installId }; Persist();
        _feedback = "已安装待实机验证；未启动 CS2。请保留 install ID 和备份。";
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
        if (!_dialogs.Confirm("恢复原环境", $"install ID: {_installId}\n备份: {BackupPath}\n{_plan.Split('\n')[0]}（完整路径见页面预览）\n\n恢复本事务记录的 MetaMod 1473、Human 和 gameinfo；文件漂移将拒绝恢复。是否继续？")) return;
        _restoreReady = false;
        await _service.ExecuteAsync("Restore", Difficulty, true, _installId);
        _state = "已恢复 · Human 实机呈现仍需检查"; _installedDifficulty = "—"; _feedback = "原文件已按精确事务恢复。Bot 本局偏好保留。";
    });
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
        foreach (var name in new[] { nameof(Difficulty),nameof(Aim),nameof(Nades),nameof(IsBusy),nameof(CanSelectDifficulty),nameof(CanEditCommands),nameof(StateText),nameof(ProcessText),nameof(TargetBuild),nameof(CandidateIdentity),nameof(InstalledDifficulty),nameof(InstallId),nameof(BackupPath),nameof(Feedback),nameof(PlanText),nameof(ConsoleCommands),nameof(DifficultyHint),nameof(ActionHint) }) OnPropertyChanged(name);
        foreach (var cmd in new[] { PreviewCommand,InstallCommand,DiagnosticsCommand,RestoreCommand,CopyCommandsCommand }) cmd.RaiseCanExecuteChanged();
    }
}
