using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CS2LocalKit.App.Common;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;

namespace CS2LocalKit.App.Services;

public sealed class PresetDraft : ViewModelBase
{
    private string _workingPresetName = "";
    private bool _isDirty;
    private int? _musicKitId;
    private string? _musicKitName;
    private bool _suppressDirty;

    public string WorkingPresetName
    {
        get => _workingPresetName;
        set => SetProperty(ref _workingPresetName, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public TeamDraft Ct { get; }
    public TeamDraft T { get; }

    public int? MusicKitId
    {
        get => _musicKitId;
        set
        {
            if (SetProperty(ref _musicKitId, value))
            {
                OnDirtyChanged();
            }
        }
    }

    public string? MusicKitName
    {
        get => _musicKitName;
        set => SetProperty(ref _musicKitName, value);
    }

    public PresetDraft()
    {
        Ct = new TeamDraft(this);
        T = new TeamDraft(this);
    }

    internal void OnDirtyChanged()
    {
        if (!_suppressDirty)
        {
            IsDirty = true;
        }
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public HumanPreset ToHumanPreset()
    {
        return new HumanPreset
        {
            Kind = HumanPreset.ExpectedKind,
            SchemaVersion = HumanPreset.ExpectedSchemaVersion,
            Ct = Ct.ToTeamPreset(),
            T = T.ToTeamPreset(),
            MusicKitId = MusicKitId,
        };
    }

    public static PresetDraft FromHumanPreset(string presetName, HumanPreset preset, CatalogIndex? catalog)
    {
        var draft = new PresetDraft();
        draft._suppressDirty = true;
        try
        {
            draft.WorkingPresetName = presetName;
            draft.MusicKitId = preset.MusicKitId;
            if (preset.MusicKitId is { } mid && catalog is not null && catalog.TryGetMusicKit(mid, out var mName))
            {
                draft.MusicKitName = mName;
            }

            draft.Ct.LoadFrom(preset.Ct, catalog);
            draft.T.LoadFrom(preset.T, catalog);
            draft.IsDirty = false;
            return draft;
        }
        finally
        {
            draft._suppressDirty = false;
        }
    }
}

public sealed class TeamDraft : ViewModelBase
{
    private readonly PresetDraft _root;
    public ObservableCollection<WeaponCosmeticDraft> Weapons { get; } = new();
    public KnifeSectionDraft Knife { get; }
    public GlovesDraft Gloves { get; }

    public TeamDraft(PresetDraft root)
    {
        _root = root;
        Knife = new KnifeSectionDraft(root);
        Gloves = new GlovesDraft(root);

        Weapons.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (WeaponCosmeticDraft item in e.NewItems)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (WeaponCosmeticDraft item in e.OldItems)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                }
            }
            _root.OnDirtyChanged();
        };
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _root.OnDirtyChanged();
    }

    public void LoadFrom(TeamPreset team, CatalogIndex? catalog)
    {
        Weapons.Clear();
        foreach (var w in team.Weapons.OrderBy(x => x.DefIndex))
        {
            string weaponName = $"Weapon #{w.DefIndex}";
            string paintName = $"Paint #{w.Preset.Paint}";
            if (catalog is not null && catalog.TryGetWeapon(w.DefIndex, out var def))
            {
                weaponName = def.Name;
                if (def.Paints.TryGetValue(w.Preset.Paint, out var pn)) paintName = pn;
            }

            Weapons.Add(new WeaponCosmeticDraft
            {
                DefIndex = w.DefIndex,
                WeaponName = weaponName,
                Paint = w.Preset.Paint,
                PaintName = paintName,
                Wear = w.Preset.Wear,
                Seed = w.Preset.Seed,
                NameTag = w.Preset.NameTag ?? "",
                StatTrakEnabled = w.Preset.StatTrak.HasValue,
                StatTrakCount = w.Preset.StatTrak ?? 0,
            });
        }

        Knife.LoadFrom(team.Knife, catalog);
        Gloves.LoadFrom(team.Gloves, catalog);
    }

    public TeamPreset ToTeamPreset()
    {
        var weapons = Weapons
            .OrderBy(w => w.DefIndex)
            .Select(w => new DefIndexPreset
            {
                DefIndex = w.DefIndex,
                Preset = new CosmeticPreset
                {
                    Paint = w.Paint,
                    Seed = Math.Max(0, w.Seed),
                    Wear = Math.Clamp(Math.Round(w.Wear, 4), 0.0, 1.0),
                    NameTag = w.NameTag ?? "",
                    StatTrak = w.StatTrakEnabled ? Math.Max(0, w.StatTrakCount) : null,
                }
            })
            .ToList();

        return new TeamPreset
        {
            Weapons = weapons,
            Knife = Knife.ToKnifeSection(),
            Gloves = Gloves.ToGlovesPreset(),
        };
    }
}

public sealed class WeaponCosmeticDraft : ViewModelBase
{
    private int _defIndex;
    private string _weaponName = "";
    private int _paint;
    private string _paintName = "";
    private double _wear = 0.01;
    private int _seed;
    private string _nameTag = "";
    private bool _statTrakEnabled;
    private int _statTrakCount;

    public int DefIndex
    {
        get => _defIndex;
        set => SetProperty(ref _defIndex, value);
    }

    public string WeaponName
    {
        get => _weaponName;
        set => SetProperty(ref _weaponName, value);
    }

    public int Paint
    {
        get => _paint;
        set => SetProperty(ref _paint, value);
    }

    public string PaintName
    {
        get => _paintName;
        set => SetProperty(ref _paintName, value);
    }

    public double Wear
    {
        get => _wear;
        set
        {
            var clamped = double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
            SetProperty(ref _wear, clamped);
        }
    }

    public int Seed
    {
        get => _seed;
        set => SetProperty(ref _seed, Math.Max(0, value));
    }

    public string NameTag
    {
        get => _nameTag;
        set => SetProperty(ref _nameTag, value ?? "");
    }

    public bool StatTrakEnabled
    {
        get => _statTrakEnabled;
        set => SetProperty(ref _statTrakEnabled, value);
    }

    public int StatTrakCount
    {
        get => _statTrakCount;
        set => SetProperty(ref _statTrakCount, Math.Max(0, value));
    }
}

public sealed class KnifeSectionDraft : ViewModelBase
{
    private readonly PresetDraft _root;
    private int _selected;

    public int Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                _root.OnDirtyChanged();
            }
        }
    }

    public ObservableCollection<KnifePresetDraft> Presets { get; } = new();

    public KnifeSectionDraft(PresetDraft root)
    {
        _root = root;
        Presets.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (KnifePresetDraft item in e.NewItems)
                {
                    item.PropertyChanged += Item_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (KnifePresetDraft item in e.OldItems)
                {
                    item.PropertyChanged -= Item_PropertyChanged;
                }
            }
            _root.OnDirtyChanged();
        };
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _root.OnDirtyChanged();
    }

    public void LoadFrom(KnifeSection knife, CatalogIndex? catalog)
    {
        Selected = knife.Selected;
        Presets.Clear();
        foreach (var k in knife.Presets.OrderBy(x => x.DefIndex))
        {
            string knifeName = $"Knife #{k.DefIndex}";
            string paintName = $"Paint #{k.Preset.Paint}";
            if (catalog is not null && catalog.TryGetWeapon(k.DefIndex, out var def))
            {
                knifeName = def.Name;
                if (def.Paints.TryGetValue(k.Preset.Paint, out var pn)) paintName = pn;
            }

            Presets.Add(new KnifePresetDraft
            {
                DefIndex = k.DefIndex,
                KnifeName = knifeName,
                Paint = k.Preset.Paint,
                PaintName = paintName,
                Wear = k.Preset.Wear,
                Seed = k.Preset.Seed,
                NameTag = k.Preset.NameTag ?? "",
                StatTrakEnabled = k.Preset.StatTrak.HasValue,
                StatTrakCount = k.Preset.StatTrak ?? 0,
            });
        }
    }

    public KnifeSection ToKnifeSection()
    {
        var presets = Presets
            .OrderBy(k => k.DefIndex)
            .Select(k => new DefIndexPreset
            {
                DefIndex = k.DefIndex,
                Preset = new CosmeticPreset
                {
                    Paint = k.Paint,
                    Seed = Math.Max(0, k.Seed),
                    Wear = Math.Clamp(Math.Round(k.Wear, 4), 0.0, 1.0),
                    NameTag = k.NameTag ?? "",
                    StatTrak = k.StatTrakEnabled ? Math.Max(0, k.StatTrakCount) : null,
                }
            })
            .ToList();

        return new KnifeSection
        {
            Selected = Selected,
            Presets = presets,
        };
    }
}

public sealed class KnifePresetDraft : ViewModelBase
{
    private int _defIndex;
    private string _knifeName = "";
    private int _paint;
    private string _paintName = "";
    private double _wear = 0.01;
    private int _seed;
    private string _nameTag = "";
    private bool _statTrakEnabled;
    private int _statTrakCount;

    public int DefIndex
    {
        get => _defIndex;
        set => SetProperty(ref _defIndex, value);
    }

    public string KnifeName
    {
        get => _knifeName;
        set => SetProperty(ref _knifeName, value);
    }

    public int Paint
    {
        get => _paint;
        set => SetProperty(ref _paint, value);
    }

    public string PaintName
    {
        get => _paintName;
        set => SetProperty(ref _paintName, value);
    }

    public double Wear
    {
        get => _wear;
        set
        {
            var clamped = double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
            SetProperty(ref _wear, clamped);
        }
    }

    public int Seed
    {
        get => _seed;
        set => SetProperty(ref _seed, Math.Max(0, value));
    }

    public string NameTag
    {
        get => _nameTag;
        set => SetProperty(ref _nameTag, value ?? "");
    }

    public bool StatTrakEnabled
    {
        get => _statTrakEnabled;
        set => SetProperty(ref _statTrakEnabled, value);
    }

    public int StatTrakCount
    {
        get => _statTrakCount;
        set => SetProperty(ref _statTrakCount, Math.Max(0, value));
    }
}

public sealed class GlovesDraft : ViewModelBase
{
    private readonly PresetDraft _root;
    private bool _enabled;
    private int _defIndex;
    private string _gloveName = "";
    private int _paint;
    private string _paintName = "";
    private double _wear = 0.06;
    private int _seed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value)) _root.OnDirtyChanged();
        }
    }

    public int DefIndex
    {
        get => _defIndex;
        set
        {
            if (SetProperty(ref _defIndex, value)) _root.OnDirtyChanged();
        }
    }

    public string GloveName
    {
        get => _gloveName;
        set => SetProperty(ref _gloveName, value);
    }

    public int Paint
    {
        get => _paint;
        set
        {
            if (SetProperty(ref _paint, value)) _root.OnDirtyChanged();
        }
    }

    public string PaintName
    {
        get => _paintName;
        set => SetProperty(ref _paintName, value);
    }

    public double Wear
    {
        get => _wear;
        set
        {
            var clamped = double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref _wear, clamped)) _root.OnDirtyChanged();
        }
    }

    public int Seed
    {
        get => _seed;
        set
        {
            if (SetProperty(ref _seed, Math.Max(0, value))) _root.OnDirtyChanged();
        }
    }

    public GlovesDraft(PresetDraft root)
    {
        _root = root;
    }

    public void LoadFrom(GlovesPreset gloves, CatalogIndex? catalog)
    {
        Enabled = gloves.Enabled;
        DefIndex = gloves.DefIndex;
        Paint = gloves.Paint;
        Wear = gloves.Wear;
        Seed = gloves.Seed;

        string gloveName = $"Gloves #{gloves.DefIndex}";
        string paintName = $"Paint #{gloves.Paint}";
        if (catalog is not null && catalog.TryGetWeapon(gloves.DefIndex, out var def))
        {
            gloveName = def.Name;
            if (def.Paints.TryGetValue(gloves.Paint, out var pn)) paintName = pn;
        }

        GloveName = gloveName;
        PaintName = paintName;
    }

    public GlovesPreset ToGlovesPreset()
    {
        return new GlovesPreset
        {
            Enabled = Enabled,
            DefIndex = DefIndex,
            Paint = Paint,
            Seed = Math.Max(0, Seed),
            Wear = Math.Clamp(Math.Round(Wear, 4), 0.0, 1.0),
        };
    }
}
