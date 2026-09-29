using WeaponDef = CS2LocalKit.Core.Catalog.CatalogIndex.WeaponDef;
using System.Collections.ObjectModel;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Catalog;

namespace CS2LocalKit.App.ViewModels;

public enum CosmeticsSubTab
{
    Weapons,
    Knife,
    Gloves,
    MusicKit
}

public sealed class CosmeticsViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;
    private bool _isCt = true;

    // Ordinary Weapons
    private string _weaponSearchText = "";
    private CosmeticsSubTab _currentSubTab = CosmeticsSubTab.Weapons;
    private WeaponItemViewModel? _selectedWeapon;
    private WeaponCosmeticDraft? _currentWeaponDraft;
    private string _paintSearchText = "";
    private CatalogPaint? _selectedPaint;

    // Knives
    private KnifePresetDraft? _selectedKnifePreset;
    private string _knifePaintSearchText = "";
    private CatalogPaint? _selectedKnifePaint;
    private WeaponDef? _knifeToAdd;

    // Gloves
    private WeaponDef? _selectedGloveDef;
    private CatalogPaint? _selectedGlovePaint;

    // Music Kit
    private string _musicKitSearchText = "";
    private CatalogMusicKit? _selectedMusicKit;
    private bool _syncingMusicKitSelection;

    // Inline feedback
    private string _statusMessage = "";
    private bool _isStatusSuccess;
    private bool _isStatusVisible;

    public PresetManagerService Manager => _manager;
    public CatalogIndex? Catalog => _manager.Catalog;

    public bool IsCt
    {
        get => _isCt;
        set
        {
            if (SetProperty(ref _isCt, value))
            {
                OnPropertyChanged(nameof(IsT));
                OnPropertyChanged(nameof(CurrentTeamDraft));
                OnPropertyChanged(nameof(CurrentGloves));
                OnPropertyChanged(nameof(CurrentKnife));
                RefreshWeaponList();
                RefreshKnifeList();
                RefreshGlovesSelection();
            }
        }
    }

    public bool IsT
    {
        get => !_isCt;
        set => IsCt = !value;
    }
    public CosmeticsSubTab CurrentSubTab
    {
        get => _currentSubTab;
        set
        {
            if (SetProperty(ref _currentSubTab, value))
            {
                OnPropertyChanged(nameof(IsWeaponsTab));
                OnPropertyChanged(nameof(IsKnifeTab));
                OnPropertyChanged(nameof(IsGlovesTab));
                OnPropertyChanged(nameof(IsMusicKitTab));
            }
        }
    }

    public bool IsWeaponsTab => CurrentSubTab == CosmeticsSubTab.Weapons;
    public bool IsKnifeTab => CurrentSubTab == CosmeticsSubTab.Knife;
    public bool IsGlovesTab => CurrentSubTab == CosmeticsSubTab.Gloves;
    public bool IsMusicKitTab => CurrentSubTab == CosmeticsSubTab.MusicKit;


    public TeamDraft? CurrentTeamDraft => _manager.Draft == null ? null : (IsCt ? _manager.Draft.Ct : _manager.Draft.T);
    public GlovesDraft? CurrentGloves => CurrentTeamDraft?.Gloves;
    public KnifeSectionDraft? CurrentKnife => CurrentTeamDraft?.Knife;

    // Collections
    public ObservableCollection<WeaponItemViewModel> FilteredWeapons { get; } = new();
    public ObservableCollection<CatalogPaint> FilteredPaints { get; } = new();
    public ObservableCollection<KnifePresetDraft> ConfiguredKnives { get; } = new();
    public ObservableCollection<WeaponDef> AvailableCatalogKnives { get; } = new();
    public ObservableCollection<CatalogPaint> FilteredKnifePaints { get; } = new();
    public ObservableCollection<WeaponDef> AvailableGloves { get; } = new();
    public ObservableCollection<CatalogPaint> AvailableGlovePaints { get; } = new();
    public ObservableCollection<CatalogMusicKit> FilteredMusicKits { get; } = new();

    public string WeaponSearchText
    {
        get => _weaponSearchText;
        set
        {
            if (SetProperty(ref _weaponSearchText, value))
            {
                RefreshWeaponList();
            }
        }
    }

    public WeaponItemViewModel? SelectedWeapon
    {
        get => _selectedWeapon;
        set
        {
            if (SetProperty(ref _selectedWeapon, value))
            {
                OnWeaponSelected();
            }
        }
    }

    public WeaponCosmeticDraft? CurrentWeaponDraft
    {
        get => _currentWeaponDraft;
        private set => SetProperty(ref _currentWeaponDraft, value);
    }

    public string PaintSearchText
    {
        get => _paintSearchText;
        set
        {
            if (SetProperty(ref _paintSearchText, value))
            {
                RefreshPaintsList();
            }
        }
    }

    public CatalogPaint? SelectedPaint
    {
        get => _selectedPaint;
        set
        {
            if (SetProperty(ref _selectedPaint, value) && value != null && CurrentWeaponDraft != null)
            {
                CurrentWeaponDraft.Paint = value.PaintIndex;
                CurrentWeaponDraft.PaintName = value.Name;
            }
        }
    }

    public KnifePresetDraft? SelectedKnifePreset
    {
        get => _selectedKnifePreset;
        set
        {
            if (SetProperty(ref _selectedKnifePreset, value))
            {
                OnKnifePresetSelected();
            }
        }
    }

    public bool IsSelectedKnifeCurrent
    {
        get
        {
            if (CurrentKnife == null || SelectedKnifePreset == null) return false;
            return CurrentKnife.Selected == SelectedKnifePreset.DefIndex;
        }
    }

    public string KnifePaintSearchText
    {
        get => _knifePaintSearchText;
        set
        {
            if (SetProperty(ref _knifePaintSearchText, value))
            {
                RefreshKnifePaintsList();
            }
        }
    }

    public CatalogPaint? SelectedKnifePaint
    {
        get => _selectedKnifePaint;
        set
        {
            if (SetProperty(ref _selectedKnifePaint, value) && value != null && SelectedKnifePreset != null)
            {
                SelectedKnifePreset.Paint = value.PaintIndex;
                SelectedKnifePreset.PaintName = value.Name;
            }
        }
    }

    public WeaponDef? KnifeToAdd
    {
        get => _knifeToAdd;
        set => SetProperty(ref _knifeToAdd, value);
    }

    public WeaponDef? SelectedGloveDef
    {
        get => _selectedGloveDef;
        set
        {
            if (SetProperty(ref _selectedGloveDef, value) && value != null && CurrentGloves != null)
            {
                CurrentGloves.DefIndex = value.DefIndex;
                CurrentGloves.GloveName = value.Name;
                RefreshGlovePaintsList();
            }
        }
    }

    public CatalogPaint? SelectedGlovePaint
    {
        get => _selectedGlovePaint;
        set
        {
            if (SetProperty(ref _selectedGlovePaint, value) && value != null && CurrentGloves != null)
            {
                CurrentGloves.Paint = value.PaintIndex;
                CurrentGloves.PaintName = value.Name;
            }
        }
    }

    public string MusicKitSearchText
    {
        get => _musicKitSearchText;
        set
        {
            if (SetProperty(ref _musicKitSearchText, value))
            {
                RefreshMusicKitsList();
            }
        }
    }

    public CatalogMusicKit? SelectedMusicKit
    {
        get => _selectedMusicKit;
        set
        {
            if (SetProperty(ref _selectedMusicKit, value))
            {
                // Only explicit user selection (not search filtering / list refresh) mutates draft
                if (!_syncingMusicKitSelection && value != null && _manager.Draft != null)
                {
                    _manager.Draft.MusicKitId = value.Id;
                    _manager.Draft.MusicKitName = value.Name;
                }
            }
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

    // Commands
    public RelayCommand AddOrConfigureWeaponCommand { get; }
    public RelayCommand RemoveWeaponCosmeticCommand { get; }
    public RelayCommand SetCurrentKnifeCommand { get; }
    public RelayCommand AddKnifePresetCommand { get; }
    public RelayCommand DeleteKnifePresetCommand { get; }
    public RelayCommand ClearMusicKitCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }

    public CosmeticsViewModel(PresetManagerService manager)
    {
        _manager = manager;

        AddOrConfigureWeaponCommand = new RelayCommand(AddOrConfigureWeapon);
        RemoveWeaponCosmeticCommand = new RelayCommand(RemoveWeaponCosmetic, () => CurrentWeaponDraft != null);
        SetCurrentKnifeCommand = new RelayCommand(SetCurrentKnife, () => SelectedKnifePreset != null && !IsSelectedKnifeCurrent);
        AddKnifePresetCommand = new RelayCommand(AddKnifePreset, () => KnifeToAdd != null);
        DeleteKnifePresetCommand = new RelayCommand(DeleteKnifePreset, () => SelectedKnifePreset != null && !IsSelectedKnifeCurrent);
        ClearMusicKitCommand = new RelayCommand(() =>
        {
            SelectedMusicKit = null;
            if (_manager.Draft != null)
            {
                _manager.Draft.MusicKitId = null;
                _manager.Draft.MusicKitName = null;
            }
        });

        SaveCommand = new RelayCommand(ExecuteSave);
        ApplyCommand = new RelayCommand(ExecuteApply, () => !_manager.Cs2Running);

        _manager.WorkingPresetChanged += (s, e) => OnPresetLoaded();
        _manager.StatusRefreshed += (s, e) => ApplyCommand.RaiseCanExecuteChanged();

        InitializeCatalogData();
        OnPresetLoaded();
    }

    private void InitializeCatalogData()
    {
        AvailableCatalogKnives.Clear();
        AvailableGloves.Clear();

        if (Catalog != null)
        {
            foreach (var k in Catalog.GetKnives()) AvailableCatalogKnives.Add(k);
            foreach (var g in Catalog.GetGloves()) AvailableGloves.Add(g);
        }
    }

    private void OnPresetLoaded()
    {
        RefreshWeaponList();
        RefreshKnifeList();
        RefreshGlovesSelection();
        RefreshMusicKitSelection();
        OnPropertyChanged(nameof(CurrentTeamDraft));
        OnPropertyChanged(nameof(CurrentGloves));
        OnPropertyChanged(nameof(CurrentKnife));
    }

    public void RefreshWeaponList()
    {
        FilteredWeapons.Clear();
        if (Catalog == null) return;

        var weapons = Catalog.SearchOrdinaryWeapons(WeaponSearchText);
        var team = CurrentTeamDraft;

        foreach (var w in weapons)
        {
            bool hasCosmetic = team != null && team.Weapons.Any(x => x.DefIndex == w.DefIndex);
            FilteredWeapons.Add(new WeaponItemViewModel(w.DefIndex, w.Name, hasCosmetic));
        }

        if (SelectedWeapon != null)
        {
            var match = FilteredWeapons.FirstOrDefault(w => w.DefIndex == SelectedWeapon.DefIndex);
            SelectedWeapon = match ?? FilteredWeapons.FirstOrDefault();
        }
        else
        {
            SelectedWeapon = FilteredWeapons.FirstOrDefault();
        }
    }

    private void OnWeaponSelected()
    {
        CurrentWeaponDraft = null;
        FilteredPaints.Clear();
        SelectedPaint = null;

        if (SelectedWeapon == null || CurrentTeamDraft == null) return;

        var existing = CurrentTeamDraft.Weapons.FirstOrDefault(w => w.DefIndex == SelectedWeapon.DefIndex);
        if (existing != null)
        {
            CurrentWeaponDraft = existing;
            RefreshPaintsList();
            SelectedPaint = FilteredPaints.FirstOrDefault(p => p.PaintIndex == existing.Paint);
        }
        else
        {
            RefreshPaintsList();
        }
        RemoveWeaponCosmeticCommand.RaiseCanExecuteChanged();
    }

    private void RefreshPaintsList()
    {
        FilteredPaints.Clear();
        if (SelectedWeapon == null || Catalog == null) return;

        var paints = Catalog.SearchPaints(SelectedWeapon.DefIndex, PaintSearchText);
        foreach (var p in paints)
        {
            FilteredPaints.Add(p);
        }
    }

    private void AddOrConfigureWeapon()
    {
        if (SelectedWeapon == null || CurrentTeamDraft == null) return;

        if (CurrentWeaponDraft == null)
        {
            var defaultPaint = FilteredPaints.FirstOrDefault();
            var draft = new WeaponCosmeticDraft
            {
                DefIndex = SelectedWeapon.DefIndex,
                WeaponName = SelectedWeapon.Name,
                Paint = defaultPaint?.PaintIndex ?? 0,
                PaintName = defaultPaint?.Name ?? "Default",
                Wear = 0.01,
                Seed = 0,
                NameTag = "",
                StatTrakEnabled = false,
                StatTrakCount = 0,
            };

            CurrentTeamDraft.Weapons.Add(draft);
            CurrentWeaponDraft = draft;
            SelectedPaint = defaultPaint;
            SelectedWeapon.HasCosmetic = true;
            RemoveWeaponCosmeticCommand.RaiseCanExecuteChanged();
        }
    }

    private void RemoveWeaponCosmetic()
    {
        if (SelectedWeapon == null || CurrentTeamDraft == null || CurrentWeaponDraft == null) return;

        CurrentTeamDraft.Weapons.Remove(CurrentWeaponDraft);
        CurrentWeaponDraft = null;
        SelectedPaint = null;
        SelectedWeapon.HasCosmetic = false;
        RemoveWeaponCosmeticCommand.RaiseCanExecuteChanged();
    }

    public void RefreshKnifeList()
    {
        ConfiguredKnives.Clear();
        if (CurrentKnife == null) return;

        foreach (var k in CurrentKnife.Presets)
        {
            ConfiguredKnives.Add(k);
        }

        if (SelectedKnifePreset != null)
        {
            SelectedKnifePreset = ConfiguredKnives.FirstOrDefault(k => k.DefIndex == SelectedKnifePreset.DefIndex) ?? ConfiguredKnives.FirstOrDefault();
        }
        else
        {
            SelectedKnifePreset = ConfiguredKnives.FirstOrDefault(k => k.DefIndex == CurrentKnife.Selected) ?? ConfiguredKnives.FirstOrDefault();
        }

        KnifeToAdd = AvailableCatalogKnives.FirstOrDefault(k => !ConfiguredKnives.Any(ck => ck.DefIndex == k.DefIndex));
    }

    private void OnKnifePresetSelected()
    {
        FilteredKnifePaints.Clear();
        SelectedKnifePaint = null;

        if (SelectedKnifePreset == null || Catalog == null)
        {
            OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
            SetCurrentKnifeCommand.RaiseCanExecuteChanged();
            DeleteKnifePresetCommand.RaiseCanExecuteChanged();
            return;
        }

        RefreshKnifePaintsList();
        SelectedKnifePaint = FilteredKnifePaints.FirstOrDefault(p => p.PaintIndex == SelectedKnifePreset.Paint);

        OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
        DeleteKnifePresetCommand.RaiseCanExecuteChanged();
    }

    private void RefreshKnifePaintsList()
    {
        FilteredKnifePaints.Clear();
        if (SelectedKnifePreset == null || Catalog == null) return;

        var paints = Catalog.SearchPaints(SelectedKnifePreset.DefIndex, KnifePaintSearchText);
        foreach (var p in paints)
        {
            FilteredKnifePaints.Add(p);
        }
    }

    private void SetCurrentKnife()
    {
        if (CurrentKnife == null || SelectedKnifePreset == null) return;

        CurrentKnife.Selected = SelectedKnifePreset.DefIndex;
        OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
        DeleteKnifePresetCommand.RaiseCanExecuteChanged();
    }

    private void AddKnifePreset()
    {
        if (CurrentKnife == null || KnifeToAdd == null || Catalog == null) return;

        var paints = Catalog.GetPaintsForWeapon(KnifeToAdd.DefIndex);
        var firstPaint = paints.FirstOrDefault();

        var newPreset = new KnifePresetDraft
        {
            DefIndex = KnifeToAdd.DefIndex,
            KnifeName = KnifeToAdd.Name,
            Paint = firstPaint?.PaintIndex ?? 0,
            PaintName = firstPaint?.Name ?? "Default",
            Wear = 0.01,
            Seed = 0,
            NameTag = "",
            StatTrakEnabled = false,
            StatTrakCount = 0,
        };

        CurrentKnife.Presets.Add(newPreset);
        RefreshKnifeList();
        SelectedKnifePreset = newPreset;
    }

    private void DeleteKnifePreset()
    {
        if (CurrentKnife == null || SelectedKnifePreset == null) return;

        if (CurrentKnife.Selected == SelectedKnifePreset.DefIndex)
        {
            _manager.DialogService.ShowError("无法删除", "不能删除当前选中的默认刀型预设。请先将另一把刀设为当前刀。");
            return;
        }

        CurrentKnife.Presets.Remove(SelectedKnifePreset);
        RefreshKnifeList();
    }

    private void RefreshGlovesSelection()
    {
        if (CurrentGloves == null) return;

        SelectedGloveDef = AvailableGloves.FirstOrDefault(g => g.DefIndex == CurrentGloves.DefIndex) ?? AvailableGloves.FirstOrDefault();
        RefreshGlovePaintsList();
    }

    private void RefreshGlovePaintsList()
    {
        AvailableGlovePaints.Clear();
        if (SelectedGloveDef == null || Catalog == null) return;

        var paints = Catalog.GetPaintsForWeapon(SelectedGloveDef.DefIndex);
        foreach (var p in paints) AvailableGlovePaints.Add(p);

        if (CurrentGloves != null)
        {
            SelectedGlovePaint = AvailableGlovePaints.FirstOrDefault(p => p.PaintIndex == CurrentGloves.Paint) ?? AvailableGlovePaints.FirstOrDefault();
        }
    }

    public void RefreshMusicKitsList()
    {
        _syncingMusicKitSelection = true;
        try
        {
            FilteredMusicKits.Clear();
            if (Catalog == null) return;

            var kits = Catalog.SearchMusicKits(MusicKitSearchText);
            foreach (var k in kits) FilteredMusicKits.Add(k);

            if (_manager.Draft?.MusicKitId is { } mid)
            {
                _selectedMusicKit = FilteredMusicKits.FirstOrDefault(m => m.Id == mid);
                OnPropertyChanged(nameof(SelectedMusicKit));
            }
            else
            {
                _selectedMusicKit = null;
                OnPropertyChanged(nameof(SelectedMusicKit));
            }
        }
        finally
        {
            _syncingMusicKitSelection = false;
        }
    }

    private void RefreshMusicKitSelection()
    {
        RefreshMusicKitsList();
    }

    public void ExecuteSave()
    {
        var result = _manager.Save();
        ShowFeedback(result.Success, result.Message);
    }

    public void ExecuteApply()
    {
        var result = _manager.Apply();
        ShowFeedback(result.Success, result.Message);
    }

    private void ShowFeedback(bool success, string message)
    {
        IsStatusSuccess = success;
        StatusMessage = message;
        IsStatusVisible = true;
    }
}

public sealed class WeaponItemViewModel : ViewModelBase
{
    private bool _hasCosmetic;

    public int DefIndex { get; }
    public string Name { get; }

    public bool HasCosmetic
    {
        get => _hasCosmetic;
        set => SetProperty(ref _hasCosmetic, value);
    }

    public WeaponItemViewModel(int defIndex, string name, bool hasCosmetic)
    {
        DefIndex = defIndex;
        Name = name;
        _hasCosmetic = hasCosmetic;
    }
}
