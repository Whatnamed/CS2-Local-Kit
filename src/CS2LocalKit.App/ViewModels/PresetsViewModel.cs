using System.IO;
using System.Collections.ObjectModel;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Store;

namespace CS2LocalKit.App.ViewModels;

public sealed class PresetsViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;
    private PresetItemViewModel? _selectedPreset;
    private string _statusMessage = "";
    private bool _isStatusSuccess;
    private bool _isStatusVisible;
    private string _newPresetNameInput = "";

    public PresetManagerService Manager => _manager;

    public ObservableCollection<PresetItemViewModel> Presets { get; } = new();

    public PresetItemViewModel? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                LoadPresetCommand.RaiseCanExecuteChanged();
                SetActiveCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                DuplicateCommand.RaiseCanExecuteChanged();
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPresetNameInput
    {
        get => _newPresetNameInput;
        set => SetProperty(ref _newPresetNameInput, value);
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

    public RelayCommand LoadPresetCommand { get; }
    public RelayCommand SetActiveCommand { get; }
    public RelayCommand CreateNewPresetCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public PresetsViewModel(PresetManagerService manager)
    {
        _manager = manager;

        LoadPresetCommand = new RelayCommand(ExecuteLoadPreset, () => SelectedPreset != null);
        SetActiveCommand = new RelayCommand(ExecuteSetActive, () => SelectedPreset != null && !SelectedPreset.IsActive);
        CreateNewPresetCommand = new RelayCommand(ExecuteCreateNewPreset);
        DuplicateCommand = new RelayCommand(ExecuteDuplicate, () => SelectedPreset != null);
        DeleteCommand = new RelayCommand(ExecuteDelete, () => SelectedPreset != null && !SelectedPreset.IsActive);
        ImportCommand = new RelayCommand(ExecuteImport);
        ExportCommand = new RelayCommand(ExecuteExport, () => SelectedPreset != null);
        RefreshCommand = new RelayCommand(RefreshPresetList);

        _manager.WorkingPresetChanged += (s, e) => RefreshPresetList();
        _manager.StatusRefreshed += (s, e) => RefreshPresetList();

        RefreshPresetList();
    }

    public void RefreshPresetList()
    {
        Presets.Clear();
        var active = _manager.ActivePresetName;
        var working = _manager.WorkingPresetName;
        var installed = System.IO.Path.GetFileName(_manager.LatestAppliedPresetName ?? "");

        try
        {
            var names = _manager.Services.PresetStore.ListNames();
            foreach (var name in names)
            {
                bool isActive = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
                bool isWorking = string.Equals(name, working, StringComparison.OrdinalIgnoreCase);
                bool isInstalled = !string.IsNullOrEmpty(installed)
                    && string.Equals(name, installed, StringComparison.OrdinalIgnoreCase);
                Presets.Add(new PresetItemViewModel(name, isActive, isWorking, isInstalled));
            }

            if (SelectedPreset != null)
            {
                SelectedPreset = Presets.FirstOrDefault(p => string.Equals(p.Name, SelectedPreset.Name, StringComparison.OrdinalIgnoreCase)) ?? Presets.FirstOrDefault();
            }
            else
            {
                SelectedPreset = Presets.FirstOrDefault(p => p.IsWorking) ?? Presets.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"读取预设列表失败: {ex.Message}");
        }
    }

    private void ExecuteLoadPreset()
    {
        if (SelectedPreset == null) return;
        var success = _manager.LoadPreset(SelectedPreset.Name);
        if (success)
        {
            ShowFeedback(true, $"已加载预设 [{SelectedPreset.Name}] 到编辑器");
            RefreshPresetList();
        }
    }

    private void ExecuteSetActive()
    {
        if (SelectedPreset == null) return;
        _manager.SetActive(SelectedPreset.Name);
        ShowFeedback(true, $"已将 [{SelectedPreset.Name}] 设为当前激活预设");
        RefreshPresetList();
    }

    private void ExecuteCreateNewPreset()
    {
        var rawName = NewPresetNameInput.Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            ShowFeedback(false, "请输入新预设文件名");
            return;
        }

        var normalizedName = rawName.EndsWith(".v1.json", StringComparison.OrdinalIgnoreCase)
            ? rawName
            : (rawName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? rawName : $"{rawName}.v1.json");

        if (_manager.Services.PresetStore.Exists(normalizedName))
        {
            ShowFeedback(false, $"预设文件 [{normalizedName}] 已存在，无法覆盖");
            return;
        }
        // Enforce unsaved changes contract before creating file or switching
        if (!_manager.ResolveUnsavedChanges())
        {
            return;
        }

        try
        {
            var template = HumanPresetTemplate.CreateMinimalValid();
            _manager.Services.PresetStore.Save(normalizedName, template);
            NewPresetNameInput = "";
            RefreshPresetList();
            _manager.LoadPreset(normalizedName, force: true);
            ShowFeedback(true, $"已成功创建新预设 [{normalizedName}] 并载入编辑器");
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"创建预设失败: {ex.Message}");
        }
    }

    private void ExecuteDuplicate()
    {
        if (SelectedPreset == null) return;
        var baseName = Path.GetFileNameWithoutExtension(SelectedPreset.Name);
        if (baseName.EndsWith(".v1", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^3];

        var destName = $"{baseName}-copy.v1.json";
        int counter = 2;
        while (_manager.Services.PresetStore.Exists(destName))
        {
            destName = $"{baseName}-copy{counter++}.v1.json";
        }

        try
        {
            _manager.Services.PresetStore.Duplicate(SelectedPreset.Name, destName);
            RefreshPresetList();
            ShowFeedback(true, $"已成功复制预设为 [{destName}]");
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"复制预设失败: {ex.Message}");
        }
    }

    private void ExecuteDelete()
    {
        if (SelectedPreset == null) return;

        if (SelectedPreset.IsActive)
        {
            _manager.DialogService.ShowError("无法删除", "不能删除当前激活的预设。请先激活另一个预设后再删除。");
            return;
        }

        var confirmed = _manager.DialogService.Confirm(
            "确认删除预设",
            $"确定要永久删除预设 [{SelectedPreset.Name}] 吗？此操作无法撤销。");

        if (!confirmed) return;

        try
        {
            _manager.Services.PresetStore.Delete(SelectedPreset.Name);
            ShowFeedback(true, $"已删除预设 [{SelectedPreset.Name}]");
            RefreshPresetList();
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"删除预设失败: {ex.Message}");
        }
    }

    private void ExecuteImport()
    {
        var filePath = _manager.DialogService.ShowOpenFileDialog("HumanPreset JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*");
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var importedName = _manager.Services.PresetStore.Import(filePath);
            RefreshPresetList();
            ShowFeedback(true, $"已成功导入预设 [{importedName}]");
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"导入预设失败: {ex.Message}");
        }
    }

    private void ExecuteExport()
    {
        if (SelectedPreset == null) return;

        var savePath = _manager.DialogService.ShowSaveFileDialog(
            SelectedPreset.Name,
            "HumanPreset JSON 文件 (*.json)|*.json");

        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            _manager.Services.PresetStore.Export(SelectedPreset.Name, savePath, overwrite: true);
            ShowFeedback(true, $"已将预设导出到: {savePath}");
        }
        catch (Exception ex)
        {
            ShowFeedback(false, $"导出预设失败: {ex.Message}");
        }
    }

    private void ShowFeedback(bool success, string message)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}

public sealed class PresetItemViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isWorking;

    public string Name { get; }

    /// <summary>Preset files are stored as &lt;name&gt;.v1.json; the list shows the readable part.</summary>
    public string FriendlyName => HeaderViewModel.Friendly(Name);

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsWorking
    {
        get => _isWorking;
        set => SetProperty(ref _isWorking, value);
    }

    /// <summary>This preset is what the game fixture currently carries (last successful apply).</summary>
    public bool IsInstalled { get; }

    public bool HasMarker => IsActive || IsWorking || IsInstalled;

    public string StateText
    {
        get
        {
            var parts = new List<string>();
            if (IsWorking) parts.Add("正在编辑");
            if (IsActive) parts.Add("游戏内当前");
            if (IsInstalled) parts.Add("已应用到 CS2");
            return parts.Count == 0 ? "未使用" : string.Join(" · ", parts);
        }
    }

    public PresetItemViewModel(string name, bool isActive, bool isWorking, bool isInstalled = false)
    {
        Name = name;
        _isActive = isActive;
        _isWorking = isWorking;
        IsInstalled = isInstalled;
    }
}
