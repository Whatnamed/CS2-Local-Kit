using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CS2LocalKit.App.Catalog;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.Catalog;
using ICosmeticEntry = CS2LocalKit.App.Services.ICosmeticEntry;

namespace CS2LocalKit.App.ViewModels;

public enum CosmeticsSection
{
    Weapons,
    Knives,
    Gloves,
    MusicKit,
}

/// <summary>
/// The cosmetics editor: a three-part browser (model list -> visual finish grid -> editing detail)
/// shared by weapons, knives and gloves, plus the global music-kit grid.
///
/// Presentation refresh never writes the draft. Team switching, searching and list rebuilds only
/// describe what the draft already says; the draft changes exclusively through an explicit user
/// action - clicking a finish card, moving the wear slider, editing seed, or toggling StatTrak.
/// </summary>
public sealed class CosmeticsViewModel : ViewModelBase
{
    private readonly PresetManagerService _manager;
    private bool _isCt = true;
    private CosmeticsSection _section = CosmeticsSection.Weapons;
    private string _itemSearchText = "";
    private string _skinSearchText = "";
    private string _musicKitSearchText = "";
    private bool _onlyConfigured;
    private int _columnCount = 4;
    private CatalogItemViewModel? _selectedItem;
    private SkinCardViewModel? _selectedSkin;
    private MusicKitCardViewModel? _selectedMusicKit;
    private ICosmeticEntry? _detailEntry;
    private SkinCardViewModel? _detailCard;
    private bool _isStatusVisible;
    private bool _isStatusSuccess;
    private string _statusMessage = "";

    public CosmeticsViewModel(PresetManagerService manager)
    {
        _manager = manager;

        RemoveCosmeticCommand = new RelayCommand(RemoveCosmetic, () => DetailEntry is not null);
        SetCurrentKnifeCommand = new RelayCommand(SetCurrentKnife,
            () => Section == CosmeticsSection.Knives && SelectedItem is { IsConfigured: true } && !IsSelectedKnifeCurrent);
        ClearMusicKitCommand = new RelayCommand(ClearMusicKit, () => _manager.Draft?.MusicKitId is not null);
        EnableGlovesCommand = new RelayCommand(EnableGloves);
        SaveCommand = new RelayCommand(ExecuteSave);
        ApplyCommand = new RelayCommand(ExecuteApply, () => !_manager.Cs2Running);
        SelectSkinCommand = new RelayCommand<SkinCardViewModel>(ApplySkinChoice);
        SelectMusicKitCommand = new RelayCommand<MusicKitCardViewModel>(SelectMusicKit);
        SelectSectionCommand = new RelayCommand<CosmeticsSection?>(section =>
        {
            if (section.HasValue) Section = section.Value;
        });

        _manager.WorkingPresetChanged += (_, _) => OnPresetLoaded();
        _manager.CatalogReloaded += (_, _) => OnPresetLoaded();
        _manager.StatusRefreshed += (_, _) =>
        {
            ApplyCommand.RaiseCanExecuteChanged();
            ClearMusicKitCommand.RaiseCanExecuteChanged();
        };
        _manager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PresetManagerService.IsDirty) && _manager.IsDirty) IsStatusVisible = false;
        };

        OnPresetLoaded();
    }

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
                Rebuild();
            }
        }
    }

    public bool IsT { get => !_isCt; set => IsCt = !value; }

    public CosmeticsSection Section
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            OnPropertyChanged(nameof(IsWeaponsSection));
            OnPropertyChanged(nameof(IsKnivesSection));
            OnPropertyChanged(nameof(IsGlovesSection));
            OnPropertyChanged(nameof(IsMusicKitSection));
            OnPropertyChanged(nameof(ShowsTeamContext));
            OnPropertyChanged(nameof(SkinsHintText));
            OnPropertyChanged(nameof(GridPlaceholderText));
            ItemSearchText = "";
            SkinSearchText = "";
            Rebuild();
        }
    }

    public bool IsWeaponsSection => _section == CosmeticsSection.Weapons;
    public bool IsKnivesSection => _section == CosmeticsSection.Knives;
    public bool IsGlovesSection => _section == CosmeticsSection.Gloves;
    public bool IsMusicKitSection => _section == CosmeticsSection.MusicKit;

    /// <summary>Music kits are global, so the CT/T context is hidden there.</summary>
    public bool ShowsTeamContext => _section != CosmeticsSection.MusicKit;

    public string SkinsHintText => Section switch
    {
        CosmeticsSection.Knives => "点击皮肤卡片为该刀型选择外观；“设为当前刀”决定进服时手持的刀。",
        CosmeticsSection.Gloves => GlovesEnabled
            ? "点击卡片为该手套选择外观；手套的启用状态由右上角开关整体控制。"
            : "先启用手套，再点击卡片选择型号与外观。",
        CosmeticsSection.MusicKit => "点击卡片选择全局音乐盒，双方共用。",
        _ => "点击卡片选择皮肤，右侧调整磨损、磨损编号与 StatTrak。",
    };

    public string GridPlaceholderText => Section switch
    {
        CosmeticsSection.MusicKit when FilteredMusicKits.Count > 0 => "",
        CosmeticsSection.MusicKit => "没有匹配的音乐盒",
        _ when Section != CosmeticsSection.MusicKit && Skins.Count > 0 => "",
        _ when SelectedItem is null => "先在左侧选择一把武器",
        _ => "没有匹配的皮肤",
    };

    public string TeamLabel => IsCt ? "反恐精英 (CT)" : "恐怖分子 (T)";

    /// <summary>One-line orientation for the current team; numbers only, no technical terms.</summary>
    public string TeamSummaryText
    {
        get
        {
            var team = CurrentTeam;
            if (team is null) return "尚未载入预设";
            var weapons = team.Weapons.Count;
            var knives = team.Knife.Presets.Count;
            var gloves = team.Gloves.Enabled ? "已启用" : "未启用";
            var current = CurrentKnifeName(team);
            return $"{weapons} 件武器 · {knives} 个刀型 · 手套{gloves} · 当前刀 {current}";
        }
    }

    private string CurrentKnifeName(TeamDraft team)
    {
        var preset = team.Knife.Presets.FirstOrDefault(k => k.DefIndex == team.Knife.Selected);
        if (preset is not null) return preset.KnifeName;
        return Catalog is { } c && c.TryGetWeapon(team.Knife.Selected, out var def)
            ? CatalogDisplay.Primary(def.ChineseName, def.Name)
            : "未设置";
    }

    // ------- collections -------

    public ObservableCollection<CatalogItemViewModel> Items { get; } = new();
    public ObservableCollection<SkinCardViewModel> Skins { get; } = new();
    public ObservableCollection<CardRowViewModel<SkinCardViewModel>> SkinRows { get; } = new();
    public ObservableCollection<MusicKitCardViewModel> FilteredMusicKits { get; } = new();
    public ObservableCollection<CardRowViewModel<MusicKitCardViewModel>> MusicKitRows { get; } = new();

    /// <summary>Cards per row, reported by the view once its width is known.</summary>
    public int ColumnCount
    {
        get => _columnCount;
        set
        {
            if (!SetProperty(ref _columnCount, Math.Max(1, value))) return;
            RebuildRows();
        }
    }

    public int SkinCardWidth => 168;

    /// <summary>Chunks the flat card lists into rows so only visible rows build card elements.</summary>
    public void RebuildRows()
    {
        SkinRows.Clear();
        for (var i = 0; i < Skins.Count; i += ColumnCount)
            SkinRows.Add(new CardRowViewModel<SkinCardViewModel>(Skins.Skip(i).Take(ColumnCount).ToList()));

        MusicKitRows.Clear();
        for (var i = 0; i < FilteredMusicKits.Count; i += ColumnCount)
            MusicKitRows.Add(new CardRowViewModel<MusicKitCardViewModel>(FilteredMusicKits.Skip(i).Take(ColumnCount).ToList()));
    }

    public string ItemSearchText
    {
        get => _itemSearchText;
        set
        {
            if (SetProperty(ref _itemSearchText, value)) RefreshItems();
        }
    }

    public string SkinSearchText
    {
        get => _skinSearchText;
        set
        {
            if (SetProperty(ref _skinSearchText, value)) RefreshSkins();
        }
    }

    public string MusicKitSearchText
    {
        get => _musicKitSearchText;
        set
        {
            if (SetProperty(ref _musicKitSearchText, value)) RefreshMusicKitsList();
        }
    }

    public bool OnlyConfigured
    {
        get => _onlyConfigured;
        set
        {
            if (SetProperty(ref _onlyConfigured, value)) RefreshItems();
        }
    }

    public CatalogItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            DetailEntry = value is null ? null : EntryFor(value.DefIndex);
            OnPropertyChanged(nameof(DetailItem));
            RefreshSkins();
            RaiseDetailPaintProperties();
            SetCurrentKnifeCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Grid highlight. Presentation state only: refresh, filtering and team switching set it to
    /// whatever the draft already says, and it never writes back.
    /// </summary>
    public SkinCardViewModel? SelectedSkin
    {
        get => _selectedSkin;
        set => SetProperty(ref _selectedSkin, value);
    }

    public MusicKitCardViewModel? SelectedMusicKit
    {
        get => _selectedMusicKit;
        set => SetProperty(ref _selectedMusicKit, value);
    }

    // ------- detail -------

    public CatalogItemViewModel? DetailItem => SelectedItem;

    public ICosmeticEntry? DetailEntry
    {
        get => _detailEntry;
        private set
        {
            if (ReferenceEquals(_detailEntry, value)) return;
            if (_detailEntry is not null) _detailEntry.PropertyChanged -= OnDetailEntryChanged;
            _detailEntry = value;
            if (_detailEntry is not null) _detailEntry.PropertyChanged += OnDetailEntryChanged;
            OnPropertyChanged();
            RaiseDetailPaintProperties();
            RemoveCosmeticCommand.RaiseCanExecuteChanged();
            SetCurrentKnifeCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The finish the draft actually points at, resolved by paint index.</summary>
    public CatalogPaint? DetailPaint
    {
        get
        {
            if (DetailEntry is null || SelectedItem is null || Catalog is null) return null;
            return FindPaint(SelectedItem.DefIndex, DetailEntry.Paint);
        }
    }

    public bool IsEntryConfigured => DetailEntry is not null;

    /// <summary>Weapons and knives also carry a name tag and StatTrak; gloves do not.</summary>
    public bool EntrySupportsDetails => DetailEntry is IDetailedEntry;

    /// <summary>
    /// The finish the draft points at, as a card. The detail panel reads its names, art and rarity
    /// from here, so the preview always follows the selected cosmetic rather than the filter state.
    /// </summary>
    public SkinCardViewModel? DetailCard => _detailCard;

    public string DetailWeaponName => SelectedItem?.DisplayName ?? "";
    public string DetailWeaponSecondaryName => SelectedItem?.SecondaryName ?? "";
    public string DetailPaintName => _detailCard?.DisplayName ?? "未选择皮肤";
    public string DetailPaintSecondary => _detailCard?.SecondaryName ?? "";
    public string DetailPaintIndex => _detailCard?.PaintIndexText ?? "";
    public string DetailRarityLabel => _detailCard?.RarityLabel ?? "";
    public string DetailFloatRange => _detailCard?.FloatRangeText ?? "";
    public string? DetailImageUrl => _detailCard?.DetailUrl;
    public string? DetailThumbUrl => _detailCard?.ThumbnailUrl;

    public Brush? DetailRarityBrush
    {
        get
        {
            var color = CatalogDisplay.ParseColor(_detailCard?.RarityColor);
            if (color is null) return null;
            var brush = new SolidColorBrush(color.Value);
            brush.Freeze();
            return brush;
        }
    }

    public string WearBucketText => DetailEntry is { } e ? CatalogDisplay.WearBucket(e.Wear) : "";
    public string WearBucketRange => DetailEntry is { } e ? CatalogDisplay.WearBucketRange(e.Wear) : "";
    public string WearValueText => DetailEntry is { } e ? e.Wear.ToString("0.0000", CultureInfo.InvariantCulture) : "";

    public bool IsSelectedKnifeCurrent => Section == CosmeticsSection.Knives
        && SelectedItem is { } item && CurrentTeam?.Knife.Selected == item.DefIndex;

    public bool GlovesEnabled
    {
        get => CurrentTeam?.Gloves.Enabled ?? false;
        set
        {
            if (CurrentTeam is { } team && team.Gloves.Enabled != value)
            {
                team.Gloves.Enabled = value;
                OnPropertyChanged();
                Rebuild();
            }
        }
    }

    public bool ShowsGlovesSwitch => Section == CosmeticsSection.Gloves;
    public bool IsGlovesDisabledNotice => Section == CosmeticsSection.Gloves && !GlovesEnabled;

    public MusicKitCardViewModel? MusicKitDetail
    {
        get
        {
            if (Catalog is null || _manager.Draft?.MusicKitId is not { } id) return null;
            return Catalog.TryGetMusic(id, out var kit) ? new MusicKitCardViewModel(kit) : null;
        }
    }

    public bool IsMusicKitConfigured => _manager.Draft?.MusicKitId is not null;
    public string MusicKitName => MusicKitDetail?.DisplayName ?? "未选择音乐盒";
    public string MusicKitSecondaryName => MusicKitDetail?.SecondaryName ?? "";
    public string? MusicKitImageUrl => MusicKitDetail?.DetailUrl;
    public string MusicKitIdText => MusicKitDetail is { } m ? "#" + m.Id.ToString() : "";

    // ------- inline feedback -------

    public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }
    public bool IsStatusSuccess { get => _isStatusSuccess; set => SetProperty(ref _isStatusSuccess, value); }
    public bool IsStatusVisible { get => _isStatusVisible; set => SetProperty(ref _isStatusVisible, value); }

    // ------- commands -------

    public RelayCommand RemoveCosmeticCommand { get; }
    public RelayCommand SetCurrentKnifeCommand { get; }
    public RelayCommand ClearMusicKitCommand { get; }
    public RelayCommand EnableGlovesCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand<SkinCardViewModel> SelectSkinCommand { get; }
    public RelayCommand<MusicKitCardViewModel> SelectMusicKitCommand { get; }
    public RelayCommand<CosmeticsSection?> SelectSectionCommand { get; }

    /// <summary>Explicit user click on a finish card: the only path that writes a finish choice.</summary>
    public void ApplySkinChoice(SkinCardViewModel? card)
    {
        if (card is null || SelectedItem is null || CurrentTeam is null) return;
        if (Section == CosmeticsSection.Gloves && !GlovesEnabled) return;

        var entry = EnsureEntry(SelectedItem, card);
        if (entry is null) return;
        entry.Paint = card.PaintIndex;
        entry.PaintName = CatalogDisplay.FinishPrimary(card.Paint);

        DetailEntry = entry;
        HighlightSkin(card);
        RaiseDetailPaintProperties();
        SyncItemStates();
        OnPropertyChanged(nameof(TeamSummaryText));
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
    }

    private void SelectMusicKit(MusicKitCardViewModel? card)
    {
        if (card is null || _manager.Draft is not { } draft) return;
        draft.MusicKitId = card.Id;
        draft.MusicKitName = card.DisplayName;
        HighlightMusicKit(card);
        OnPropertyChanged(nameof(MusicKitDetail));
        RaiseMusicKitDetailProperties();
        ClearMusicKitCommand.RaiseCanExecuteChanged();
    }

    private void EnableGloves()
    {
        if (CurrentTeam is not { } team) return;
        team.Gloves.Enabled = true;
        OnPropertyChanged(nameof(GlovesEnabled));
        Rebuild();
    }

    private void HighlightSkin(SkinCardViewModel? card)
    {
        if (_selectedSkin is not null && !ReferenceEquals(_selectedSkin, card)) _selectedSkin.IsSelected = false;
        _selectedSkin = card;
        if (card is not null) card.IsSelected = true;
        OnPropertyChanged(nameof(SelectedSkin));
    }

    private void HighlightMusicKit(MusicKitCardViewModel? card)
    {
        if (_selectedMusicKit is not null && !ReferenceEquals(_selectedMusicKit, card)) _selectedMusicKit.IsSelected = false;
        _selectedMusicKit = card;
        if (card is not null) card.IsSelected = true;
        OnPropertyChanged(nameof(SelectedMusicKit));
    }

    // ------- draft access -------

    private TeamDraft? CurrentTeam => _manager.Draft is null ? null : (IsCt ? _manager.Draft.Ct : _manager.Draft.T);

    private ICosmeticEntry? EntryFor(int defIndex) => Section switch
    {
        CosmeticsSection.Weapons => CurrentTeam?.Weapons.FirstOrDefault(w => w.DefIndex == defIndex),
        CosmeticsSection.Knives => CurrentTeam?.Knife.Presets.FirstOrDefault(k => k.DefIndex == defIndex),
        CosmeticsSection.Gloves => CurrentTeam is { } team && team.Gloves.DefIndex == defIndex ? team.Gloves : null,
        _ => null,
    };

    /// <summary>
    /// Returns the entry for a model, creating one only as the direct result of the user picking a
    /// finish. Disabled gloves are never created or rewritten implicitly.
    /// </summary>
    private ICosmeticEntry? EnsureEntry(CatalogItemViewModel item, SkinCardViewModel card)
    {
        var team = CurrentTeam;
        if (team is null) return null;

        var existing = EntryFor(item.DefIndex);
        if (existing is not null) return existing;

        switch (Section)
        {
            case CosmeticsSection.Weapons:
            {
                var created = new WeaponCosmeticDraft
                {
                    DefIndex = item.DefIndex,
                    WeaponName = item.DisplayName,
                    Paint = card.PaintIndex,
                    Wear = 0.08,
                    Seed = 0,
                    NameTag = "",
                    StatTrakEnabled = false,
                    StatTrakCount = 0,
                };
                team.Weapons.Add(created);
                return created;
            }
            case CosmeticsSection.Knives:
            {
                var created = new KnifePresetDraft
                {
                    DefIndex = item.DefIndex,
                    KnifeName = item.DisplayName,
                    Paint = card.PaintIndex,
                    Wear = 0.08,
                    Seed = 0,
                    NameTag = "",
                    StatTrakEnabled = false,
                    StatTrakCount = 0,
                };
                team.Knife.Presets.Add(created);
                return created;
            }
            case CosmeticsSection.Gloves:
            {
                if (!team.Gloves.Enabled) return null;
                team.Gloves.DefIndex = item.DefIndex;
                team.Gloves.Paint = card.PaintIndex;
                return team.Gloves;
            }
            default:
                return null;
        }
    }

    private void RemoveCosmetic()
    {
        var team = CurrentTeam;
        if (team is null || SelectedItem is null) return;

        switch (Section)
        {
            case CosmeticsSection.Weapons:
            {
                var entry = team.Weapons.FirstOrDefault(w => w.DefIndex == SelectedItem.DefIndex);
                if (entry is null) return;
                team.Weapons.Remove(entry);
                break;
            }
            case CosmeticsSection.Knives:
            {
                var entry = team.Knife.Presets.FirstOrDefault(k => k.DefIndex == SelectedItem.DefIndex);
                if (entry is null) return;
                if (team.Knife.Selected == entry.DefIndex)
                {
                    _manager.DialogService.ShowError("无法删除", "不能删除当前使用的刀型。请先将另一个刀型设为当前刀。");
                    return;
                }
                team.Knife.Presets.Remove(entry);
                break;
            }
            case CosmeticsSection.Gloves:
            {
                // Gloves are one slot: "remove" turns the slot off and keeps the stored model and
                // finish exactly as they were.
                team.Gloves.Enabled = false;
                OnPropertyChanged(nameof(GlovesEnabled));
                break;
            }
        }

        DetailEntry = EntryFor(SelectedItem.DefIndex);
        SyncItemStates();
        RaiseDetailPaintProperties();
        RevealSkin(DetailEntry?.Paint);
        OnPropertyChanged(nameof(TeamSummaryText));
        OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
    }

    private void SetCurrentKnife()
    {
        var team = CurrentTeam;
        if (team is null || SelectedItem is null) return;
        if (team.Knife.Presets.All(k => k.DefIndex != SelectedItem.DefIndex)) return;
        team.Knife.Selected = SelectedItem.DefIndex;
        SyncItemStates();
        OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
        OnPropertyChanged(nameof(TeamSummaryText));
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
    }

    private void ClearMusicKit()
    {
        if (_manager.Draft is not { } draft) return;
        draft.MusicKitId = null;
        draft.MusicKitName = null;
        HighlightMusicKit(null);
        OnPropertyChanged(nameof(MusicKitDetail));
        RaiseMusicKitDetailProperties();
        ClearMusicKitCommand.RaiseCanExecuteChanged();
    }

    private void OnDetailEntryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ICosmeticEntry.Wear))
        {
            OnPropertyChanged(nameof(WearBucketText));
            OnPropertyChanged(nameof(WearBucketRange));
            OnPropertyChanged(nameof(WearValueText));
        }
        else if (e.PropertyName == nameof(ICosmeticEntry.Paint))
        {
            RaiseDetailPaintProperties();
            SyncItemStates();
            RevealSkin(DetailEntry?.Paint);
        }
    }

    private void RaiseDetailPaintProperties()
    {
        _detailCard = DetailPaint is null ? null : new SkinCardViewModel(DetailPaint);
        OnPropertyChanged(nameof(DetailPaint));
        OnPropertyChanged(nameof(DetailCard));
        OnPropertyChanged(nameof(IsEntryConfigured));
        OnPropertyChanged(nameof(EntrySupportsDetails));
        OnPropertyChanged(nameof(DetailItem));
        OnPropertyChanged(nameof(DetailWeaponName));
        OnPropertyChanged(nameof(DetailWeaponSecondaryName));
        OnPropertyChanged(nameof(DetailPaintName));
        OnPropertyChanged(nameof(DetailPaintSecondary));
        OnPropertyChanged(nameof(DetailPaintIndex));
        OnPropertyChanged(nameof(DetailRarityLabel));
        OnPropertyChanged(nameof(DetailFloatRange));
        OnPropertyChanged(nameof(DetailImageUrl));
        OnPropertyChanged(nameof(DetailThumbUrl));
        OnPropertyChanged(nameof(DetailRarityBrush));
        OnPropertyChanged(nameof(WearBucketText));
        OnPropertyChanged(nameof(WearBucketRange));
        OnPropertyChanged(nameof(WearValueText));
        OnPropertyChanged(nameof(IsSelectedKnifeCurrent));
        OnPropertyChanged(nameof(IsGlovesDisabledNotice));
    }

    private void RaiseMusicKitDetailProperties()
    {
        OnPropertyChanged(nameof(IsMusicKitConfigured));
        OnPropertyChanged(nameof(MusicKitName));
        OnPropertyChanged(nameof(MusicKitSecondaryName));
        OnPropertyChanged(nameof(MusicKitImageUrl));
        OnPropertyChanged(nameof(MusicKitIdText));
    }

    // ------- build / refresh (read-only with respect to the draft) -------

    private void OnPresetLoaded() => Rebuild();

    private void Rebuild()
    {
        IsStatusVisible = false;
        OnPropertyChanged(nameof(TeamLabel));
        OnPropertyChanged(nameof(TeamSummaryText));
        OnPropertyChanged(nameof(ShowsGlovesSwitch));
        OnPropertyChanged(nameof(IsGlovesDisabledNotice));
        OnPropertyChanged(nameof(GlovesEnabled));
        OnPropertyChanged(nameof(SkinsHintText));
        OnPropertyChanged(nameof(GridPlaceholderText));
        OnPropertyChanged(nameof(IsMusicKitConfigured));
        RefreshItems();
        RefreshMusicKitsList();
        OnPropertyChanged(nameof(MusicKitDetail));
        RaiseMusicKitDetailProperties();
    }

    private void RefreshItems()
    {
        var keepDefIndex = _selectedItem?.DefIndex;
        Items.Clear();
        if (Catalog is null)
        {
            SelectedItem = null;
            return;
        }

        var team = CurrentTeam;
        IEnumerable<WeaponDef> source = Section switch
        {
            CosmeticsSection.Weapons => Catalog.SearchOrdinaryWeapons(ItemSearchText),
            CosmeticsSection.Knives => Catalog.SearchKnives(ItemSearchText),
            CosmeticsSection.Gloves => Catalog.SearchGloves(ItemSearchText),
            _ => Array.Empty<WeaponDef>(),
        };

        var configured = Section switch
        {
            CosmeticsSection.Weapons => team?.Weapons.Select(w => w.DefIndex).ToHashSet() ?? new HashSet<int>(),
            CosmeticsSection.Knives => team?.Knife.Presets.Select(k => k.DefIndex).ToHashSet() ?? new HashSet<int>(),
            CosmeticsSection.Gloves => team is { } t ? new HashSet<int> { t.Gloves.DefIndex } : new HashSet<int>(),
            _ => new HashSet<int>(),
        };

        var built = new List<CatalogItemViewModel>();
        foreach (var def in source)
        {
            if (OnlyConfigured && !configured.Contains(def.DefIndex)) continue;
            built.Add(new CatalogItemViewModel(def));
        }

        foreach (var item in built
                     .OrderBy(i => Section == CosmeticsSection.Gloves ? 0 : i.CategoryOrder)
                     .ThenBy(i => i.IsConfigured ? 0 : 1)
                     .ThenBy(i => i.SortKey, ZhComparer))
        {
            Items.Add(item);
        }

        SyncItemStates();

        var restored = keepDefIndex is { } id ? Items.FirstOrDefault(i => i.DefIndex == id) : null;
        restored ??= Items.FirstOrDefault();
        _selectedItem = restored;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(DetailItem));
        DetailEntry = restored is null ? null : EntryFor(restored.DefIndex);
        RefreshSkins();
        RaiseDetailPaintProperties();
        SetCurrentKnifeCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Recomputes list badges and thumbnails from the draft (presentation only).</summary>
    private void SyncItemStates()
    {
        if (Catalog is null) return;
        var team = CurrentTeam;
        foreach (var item in Items)
        {
            var entry = EntryFor(item.DefIndex);
            item.IsConfigured = entry is not null;
            var paint = entry is null ? null : FindPaint(item.DefIndex, entry.Paint);
            item.Summary = entry switch
            {
                null => null,
                { Paint: 0 } when paint is null => "默认外观",
                _ => paint is null ? $"#{entry!.Paint}" : CatalogDisplay.FinishPrimary(paint)
            };
            item.ImageUrl = paint is null ? null : CatalogDisplay.ThumbnailUrl(paint.ImageUrl);
            item.IsCurrent = Section == CosmeticsSection.Knives && team?.Knife.Selected == item.DefIndex;
        }
    }

    private CatalogPaint? FindPaint(int defIndex, int paintIndex)
        => Catalog?.GetPaintsForWeapon(defIndex).FirstOrDefault(p => p.PaintIndex == paintIndex);

    private void RefreshSkins()
    {
        Skins.Clear();
        if (Catalog is null || SelectedItem is null || Section == CosmeticsSection.MusicKit)
        {
            HighlightSkin(null);
            RebuildRows();
            OnPropertyChanged(nameof(GridPlaceholderText));
            return;
        }

        foreach (var paint in Catalog.SearchPaints(SelectedItem.DefIndex, SkinSearchText))
            Skins.Add(new SkinCardViewModel(paint));

        RebuildRows();
        RevealSkin(DetailEntry?.Paint);
        OnPropertyChanged(nameof(GridPlaceholderText));
    }

    /// <summary>
    /// Highlights the card the draft points at. A value that is not in the current view (filtered
    /// out by search) leaves nothing highlighted - it never rewrites the draft and never falls back
    /// to the first card.
    /// </summary>
    private void RevealSkin(int? paintIndex)
    {
        if (DetailEntry is null && SelectedItem is { } item) DetailEntry = EntryFor(item.DefIndex);
        var match = paintIndex is { } id ? Skins.FirstOrDefault(s => s.PaintIndex == id) : null;
        HighlightSkin(match);
        RaiseDetailPaintProperties();
    }

    public void RefreshMusicKitsList()
    {
        FilteredMusicKits.Clear();
        if (Catalog is not null)
        {
            foreach (var kit in Catalog.SearchMusicKits(MusicKitSearchText))
                FilteredMusicKits.Add(new MusicKitCardViewModel(kit));
        }

        RebuildRows();
        var currentId = _manager.Draft?.MusicKitId;
        var match = currentId is { } id ? FilteredMusicKits.FirstOrDefault(m => m.Id == id) : null;
        HighlightMusicKit(match);
        OnPropertyChanged(nameof(GridPlaceholderText));
    }

    private static readonly StringComparer ZhComparer = CreateZhComparer();

    private static StringComparer CreateZhComparer()
    {
        try
        {
            return StringComparer.Create(new CultureInfo("zh-Hans-CN"), ignoreCase: false);
        }
        catch (Exception)
        {
            return StringComparer.OrdinalIgnoreCase;
        }
    }

    // ------- save / apply (semantics unchanged by the redesign) -------

    public void ExecuteSave()
    {
        var result = _manager.Save();
        ShowFeedback(result.Success, result.Message);
        OnPropertyChanged(nameof(TeamSummaryText));
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
