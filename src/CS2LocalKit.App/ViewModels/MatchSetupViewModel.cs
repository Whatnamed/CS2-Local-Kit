using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.Core.MatchSetup;

namespace CS2LocalKit.App.ViewModels;

public sealed record RosterTeamChoice(string Id,string Label);
public sealed record RosterVersionChoice(string Id,string Label,string Source,string Event);
public sealed record PersonaChoice(PlayerIdentity Player)
{
    public string Label => $"{Player.Name} · {(Player.Enabled?Player.PersonaId:"disabled / 不可绑定")}";
}
public sealed class SlotEditorViewModel : ViewModelBase
{
    private string _name;private string? _personaId;
    private string _participation = "Bot";
    public string Participation { get => _participation; set => SetProperty(ref _participation, value); }
    public int Index { get; }
    public string Label => $"席位 {Index+1}";
    public string Number => $"{Index+1:00}";
    public SlotEditorViewModel(int index,PlayerSlot slot) { Index=index;_name=slot.Name;_personaId=slot.PersonaId; }
    public string Name { get=>_name;set { if(SetProperty(ref _name,value)) { _personaId=null;OnPropertyChanged(nameof(PersonaHint)); } } }
    public string PersonaHint => _personaId is null?"名称匹配 / 无唯一 ID 时降级":"persona "+_personaId;
    public PlayerSlot Snapshot() => new(Name,_personaId);
    public void Select(PlayerIdentity p) { _name=p.Name;_personaId=p.PersonaId;OnPropertyChanged(nameof(Name));OnPropertyChanged(nameof(PersonaHint)); }
}

public sealed class RosterEditorViewModel : ViewModelBase
{
    private readonly RosterCatalog _catalog;private readonly Func<MatchLibrary> _library;private readonly Action _changed;private readonly Action<MatchLibrary> _save;private readonly IDialogService _dialogs;
    private string _teamId="random",_query="",_search="",_customName="我的阵容",_name="",_logo="";
    private string? _customId;private RosterVersionChoice? _version;private PersonaChoice? _selectedPersona;private int _selectedSeat;private bool _all;
    public ObservableCollection<SlotEditorViewModel> Slots { get; }=[];
    public RosterEditorViewModel(RosterCatalog catalog,Func<MatchLibrary> library,Action<MatchLibrary> save,IDialogService dialogs,Action changed,string name)
    {
        _catalog=catalog;_library=library;_save=save;_dialogs=dialogs;_changed=changed;
        ReplaceCommand=new(()=> { if(SelectedPersona?.Player is not {Enabled:true} p) throw new InvalidDataException("请选择 enabled persona");Slots[SelectedSeat].Select(p);_changed(); },()=>SelectedPersona?.Player.Enabled==true);
        NewCustomCommand=new(()=>SaveCustom(true));CopyCustomCommand=new(()=>SaveCustom(true));SaveCustomCommand=new(()=>SaveCustom(false));
        DeleteCustomCommand=new(DeleteCustom,()=>_customId is not null);
        RandomizeCommand=new(Randomize);ExportRosterCommand=new(ExportRoster);
        LoadSelection(MatchTeamSelection.Random(name));
    }
    public IEnumerable<RosterTeamChoice> Teams => new[]{new RosterTeamChoice("random","普通 / 随机")}
        .Concat(_catalog.Teams.Where(t=>(_all||t.Recommended||!string.IsNullOrWhiteSpace(_query))&&t.Name.Contains(_query,StringComparison.OrdinalIgnoreCase)).OrderBy(t=>t.Name).Select(t=>new RosterTeamChoice(t.Id,t.Name)))
        .Concat(_library().CustomRosters.Where(c=>c.Name.Contains(_query,StringComparison.OrdinalIgnoreCase)).Select(c=>new RosterTeamChoice(c.Id,"自定义 · "+c.Name)));
    public bool ShowAll { get=>_all;set { if(SetProperty(ref _all,value))OnPropertyChanged(nameof(Teams)); } }
    public string TeamQuery { get=>_query;set { if(SetProperty(ref _query,value))OnPropertyChanged(nameof(Teams)); } }
    public string TeamId
    {
        get=>_teamId;
        set
        {
            if(string.IsNullOrEmpty(value)||value==_teamId)return;_teamId=value;_customId=null;
            var custom=_library().CustomRosters.FirstOrDefault(c=>c.Id==value);
            if(custom is not null) { _customId=custom.Id;_customName=custom.Name;_version=null;SetSlots(custom.Slots);_name=custom.Name;_logo=""; }
            else if(value=="random") { _version=null;SetSlots(MatchTeamSelection.Random("Bot").Slots);_name="普通人机";_logo=""; }
            else { var first=Versions.FirstOrDefault();_version=null;if(first is not null)Version=first; }
            Changed();
        }
    }
    public IEnumerable<RosterVersionChoice> Versions => _catalog.Rosters.Where(r=>r.TeamId==_teamId).OrderByDescending(r=>r.RepresentativeDate).Select(r=>new RosterVersionChoice(r.Id,r.Name,r.Source,r.Event));
    public bool HasVersions => Versions.Any();
    public RosterVersionChoice? Version
    {
        get=>_version;
        set
        {
            if(value is null||value.Id==_version?.Id)return;_version=value;_customId=null;
            var r=_catalog.Rosters.Single(r=>r.Id==value.Id);var team=_catalog.Teams.Single(t=>t.Id==r.TeamId);_name=team.Name;_logo=team.Logo;SetSlots(r.Slots);Changed();
        }
    }
    public string SourceText => _version is null?"个人草稿；内置版本保持只读，编辑仅影响本局/自定义副本":$"{_version.Event}\n{_version.Source}\n内置版本只读；下面修改仅影响本局草稿，可另存自定义。";
    public string Name { get=>_name;set { if(SetProperty(ref _name,value))_changed(); } }
    public string CustomName { get=>_customName;set=>SetProperty(ref _customName,value); }
    public int SelectedSeat { get=>_selectedSeat;set=>SetProperty(ref _selectedSeat,value); }
    public BotChoice<int>[] SeatChoices { get; }=Enumerable.Range(0,5).Select(i=>new BotChoice<int>(i,$"席位 {i+1}")).ToArray();
    public string PlayerQuery { get=>_search;set { if(SetProperty(ref _search,value)) { SelectedPersona=null;OnPropertyChanged(nameof(Personas));OnPropertyChanged(nameof(PlayerResultText)); } } }
    public string PlayerResultText => !Personas.Any() ? "没有匹配的选手；可直接编辑席位名称" : $"显示 {Personas.Count()} 项 · 最多 60 项，输入名称或完整 persona ID 缩小范围";
    public IEnumerable<PersonaChoice> Personas => _catalog.Search(_search).Select(p=>new PersonaChoice(p));
    public PersonaChoice? SelectedPersona { get=>_selectedPersona;set { SetProperty(ref _selectedPersona,value);ReplaceCommand.RaiseCanExecuteChanged(); } }
    public RelayCommand ReplaceCommand { get; }
    public RelayCommand NewCustomCommand { get; }
    public RelayCommand CopyCustomCommand { get; }
    public RelayCommand SaveCustomCommand { get; }
    public RelayCommand DeleteCustomCommand { get; }
    public RelayCommand RandomizeCommand { get; }
    public RelayCommand ExportRosterCommand { get; }
    public MatchTeamSelection Snapshot()=>new(_name,_logo,Slots.Select(s=>s.Snapshot()).ToImmutableArray(),_customId??_version?.Id);
    public void LoadSelection(MatchTeamSelection selection)
    {
        var builtIn=_catalog.Rosters.FirstOrDefault(r=>r.Id==selection.RosterId);var custom=_library().CustomRosters.FirstOrDefault(r=>r.Id==selection.RosterId);
        _teamId=builtIn?.TeamId??custom?.Id??"random";_customId=custom?.Id;_version=builtIn is null?null:new(builtIn.Id,builtIn.Name,builtIn.Source,builtIn.Event);
        _name=selection.Name;_logo=selection.Logo;_customName=custom?.Name??selection.Name+" 副本";SetSlots(selection.Slots);Changed();
    }
    public void RefreshLibrary() { OnPropertyChanged(nameof(Teams));DeleteCustomCommand.RaiseCanExecuteChanged(); }
    private void SetSlots(ImmutableArray<PlayerSlot> values)
    {
        foreach(var slot in Slots)slot.PropertyChanged-=SlotChanged;Slots.Clear();
        for(int i=0;i<values.Length;i++) { var s=new SlotEditorViewModel(i,values[i]);s.PropertyChanged+=SlotChanged;Slots.Add(s); }
    }
    private void SlotChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e) { if(e.PropertyName != nameof(SlotEditorViewModel.Participation)) _changed(); }
    private void Changed()
    {
        foreach(var p in new[]{nameof(TeamId),nameof(HasVersions),nameof(Versions),nameof(Version),nameof(SourceText),nameof(Name),nameof(CustomName),nameof(Slots),nameof(Teams)})OnPropertyChanged(p);
        DeleteCustomCommand.RaiseCanExecuteChanged();_changed();
    }
    private void Randomize()
    {
        var eligible=_catalog.Players.Where(p=>p.Enabled&&System.Text.Encoding.UTF8.GetByteCount(p.Name)<=26).OrderBy(_=>Random.Shared.Next()).Take(5).ToArray();
        _teamId="random";_customId=null;_version=null;_name="随机混编";_logo="";SetSlots(eligible.Select(p=>new PlayerSlot(p.Name,p.PersonaId)).ToImmutableArray());Changed();
    }
    private void SaveCustom(bool copy)
    {
        try
        {
            MatchJson.Name(CustomName,80);var lib=_library();var id=!copy&&_customId is not null?_customId:Guid.NewGuid().ToString("N");
            var custom=new CustomRoster(id,CustomName,Snapshot().Slots,_version?.Id);_save(lib with {CustomRosters=lib.CustomRosters.Where(r=>r.Id!=id).Append(custom).ToImmutableArray()});
            _customId=id;_teamId=id;_name=CustomName;_logo="";_version=null;Changed();
        }
        catch(Exception ex) { _dialogs.ShowError("自定义阵容未保存",ex.Message); }
    }
    private void DeleteCustom()
    {
        if(_customId is null||!_dialogs.Confirm("删除自定义阵容","仅删除个人目录中的这份阵容；已保存整局预设保留自己的席位快照。"))return;
        try { _save(_library() with {CustomRosters=_library().CustomRosters.Where(r=>r.Id!=_customId).ToImmutableArray()});_customId=null;_teamId="random";Changed(); }
        catch(Exception ex) { _dialogs.ShowError("未删除",ex.Message); }
    }
    private void ExportRoster()
    {
        try
        {
            var path=_dialogs.ShowSaveFileDialog("roster.json","Match setup (*.json)|*.json");if(path is null)return;
            MatchJson.Name(CustomName,80);MatchJson.Slots(Snapshot().Slots);MatchSetupStore.CheckPath(path);
            var roster=new CustomRoster(_customId??Guid.NewGuid().ToString("N"),CustomName,Snapshot().Slots,_version?.Id);
            File.WriteAllText(path,MatchJson.Write(new MatchLibrary(1,[roster],[],[])));
        }
        catch(Exception ex) { _dialogs.ShowError("导出失败",ex.Message); }
    }
}

public sealed record SetupPresetChoice(MatchSetupPreset Preset) { public string Label=>$"{(Preset.Favorite?"★ ":"")}{Preset.Name}"; }
public sealed class MatchSetupViewModel : ViewModelBase
{
    private readonly MatchSetupStore _store;private readonly RosterCatalog _catalog;private readonly IDialogService _dialogs;private readonly BotExperimentService _runtime;
    private MatchLibrary _library=MatchLibrary.Empty;private MatchSetupPreset _basis=MatchSetupPreset.Default();private string _name="我 + 4 vs 5",_feedback="",_status="尚未保存 / 排队",_preview="";private string? _queuedDigest;private bool _loaded;
    private (MatchTeamSelection Friendly, MatchTeamSelection Enemy)? _lastSwap;
    private MatchSide _side=MatchSide.CT;private int _friendly=4,_enemy=5,_replacement,_filter;private bool _favorite;private InstalledProfile _profile=InstalledProfile.Medium;private AimSetting _aim;private NadeSetting _nades=NadeSetting.Normal;private MatchPlayMode _mode=MatchPlayMode.EnhancedHuman;private string _map="de_dust2";private SetupPresetChoice? _selected;
    public RosterEditorViewModel Friendly { get; }
    public RosterEditorViewModel Enemy { get; }
    public MatchSetupViewModel(MatchSetupStore store,RosterCatalog catalog,IDialogService dialogs,BotExperimentService runtime)
    {
        _store=store;_catalog=catalog;_dialogs=dialogs;_runtime=runtime;
        try { _library=store.Load();_loaded=true; } catch(Exception ex) { _feedback="配置读取失败，保留原文件："+ex.Message; }
        Friendly=new(catalog,()=>_library,SaveLibrary,dialogs,RefreshPreview,"我方");Enemy=new(catalog,()=>_library,SaveLibrary,dialogs,RefreshPreview,"对手");
        SaveCommand=new(()=>Run(Save));QueueCommand=new(()=>Run(Queue));NewCommand=new(()=>SwitchTo(MatchSetupPreset.Default()));
        DuplicateCommand=new(()=> { _basis=_basis with {Id=Guid.NewGuid().ToString("N")};Name+=" 副本"; });
        LoadCommand=new(()=> { if(SelectedPreset is not null)SwitchTo(SelectedPreset.Preset); });
        SwapLineupsCommand=new(()=> { var old=(Friendly:Friendly.Snapshot(),Enemy:Enemy.Snapshot());_lastSwap=old;Friendly.LoadSelection(old.Enemy);Enemy.LoadSelection(old.Friendly);RefreshPreview();UndoSwapCommand?.RaiseCanExecuteChanged(); });
        UndoSwapCommand=new(()=> { if(_lastSwap is not { } old)return;Friendly.LoadSelection(old.Friendly);Enemy.LoadSelection(old.Enemy);_lastSwap=null;RefreshPreview();UndoSwapCommand?.RaiseCanExecuteChanged(); },()=>_lastSwap is not null);
        DeleteCommand=new(()=>Run(Delete));ReuseLastCommand=new(()=> { if(_library.LastUsed is not null)Load(_library.LastUsed);else _feedback="没有收到过运行时成功读回的上局配置";Notify(); });
        ImportCommand=new(()=>Run(Import));ExportCommand=new(()=>Run(Export));RefreshStatusCommand=new(RefreshRuntime);
        Load(_library.Presets.FirstOrDefault()??_basis);RefreshRuntime();
    }
    public MatchSide[] SideChoices { get; }=[MatchSide.CT,MatchSide.T];
    public BotChoice<MatchPlayMode>[] ModeChoices { get; }=[new(MatchPlayMode.NormalHuman,"普通人机"),new(MatchPlayMode.EnhancedHuman,"增强人机")];
    public BotChoice<InstalledProfile>[] DifficultyChoices { get; }=[new(InstalledProfile.Low,"简单"),new(InstalledProfile.Medium,"中等"),new(InstalledProfile.High,"困难")];
    public BotChoice<AimSetting>[] AimLabels { get; }=[new(AimSetting.Mixed,"混合"),new(AimSetting.Head,"头部"),new(AimSetting.Body,"身体")];
    public BotChoice<NadeSetting>[] NadeLabels { get; }=[new(NadeSetting.Off,"关闭"),new(NadeSetting.Less,"较少"),new(NadeSetting.Normal,"正常"),new(NadeSetting.More,"较多"),new(NadeSetting.Max,"最多")];
    public IReadOnlyList<string> MapChoices => _runtime.AvailableMaps;
    public int[] FriendlyCounts { get; }=[0,1,2,3,4];public int[] EnemyCounts { get; }=[0,1,2,3,4,5];public BotChoice<int>[] ReplacementChoices { get; }=Enumerable.Range(0,5).Select(i=>new BotChoice<int>(i,$"席位 {i+1}")).ToArray();
    public InstalledProfile[] ProfileChoices { get; }=Enum.GetValues<InstalledProfile>();public AimSetting[] AimChoices { get; }=Enum.GetValues<AimSetting>();public NadeSetting[] NadeChoices { get; }=Enum.GetValues<NadeSetting>();
    public string[] Filters { get; }=["全部预设","收藏","最近使用"];
    public IEnumerable<SetupPresetChoice> Presets => (_filter==2?_library.Presets.Where(p=>_library.Recent.Contains(p.Id)).OrderBy(p=>_library.Recent.IndexOf(p.Id)):_library.Presets.Where(p=>_filter!=1||p.Favorite)).Select(p=>new SetupPresetChoice(p));
    public int PresetFilter { get=>_filter;set { if(SetProperty(ref _filter,value))OnPropertyChanged(nameof(Presets)); } }
    public SetupPresetChoice? SelectedPreset { get=>_selected;set=>SetProperty(ref _selected,value); }
    public string Name { get=>_name;set { if(SetProperty(ref _name,value))RefreshPreview(); } }
    public MatchSide HumanSide { get=>_side;set { if(SetProperty(ref _side,value))RefreshPreview(); } }
    public int FriendlyBots { get=>_friendly;set { if(SetProperty(ref _friendly,value))RefreshPreview(); } }
    public int EnemyBots { get=>_enemy;set { if(SetProperty(ref _enemy,value))RefreshPreview(); } }
    public int ReplacedSlot { get=>_replacement;set { if(SetProperty(ref _replacement,value))RefreshPreview(); } }
    public bool Favorite { get=>_favorite;set { if(SetProperty(ref _favorite,value))RefreshPreview(); } }
    public InstalledProfile Profile { get=>_profile;set { if(SetProperty(ref _profile,value))RefreshPreview(); } }
    public MatchPlayMode Mode { get=>_mode;set { if(SetProperty(ref _mode,value))RefreshPreview(); } }
    public string Map { get=>_map;set { if(SetProperty(ref _map,value))RefreshPreview(); } }
    public AimSetting Aim { get=>_aim;set { if(SetProperty(ref _aim,value))RefreshPreview(); } }
    public NadeSetting Nades { get=>_nades;set { if(SetProperty(ref _nades,value))RefreshPreview(); } }
    public string Preview=>_preview;public string Feedback=>_feedback;public string ApplyStatus=>_status;
    public string PreparationText
    {
        get
        {
            if (!_runtime.HasMap(Map)) return "需要处理：本机没有所选地图，请更换地图";
            if (Mode==MatchPlayMode.NormalHuman) return _runtime.Identity.BlocksHuman
                ? "需要准备：关闭 CS2 后精确恢复增强组件；普通阵容自动应用仍不可用"
                : "普通人机可通过 CS2 原生方式游玩；当前阵容自动应用通道仅供增强人机使用";
            var state=_runtime.Assess(LocalPlayMode.EnhancedHuman,(BotDifficulty)(int)Profile);
            return state.Kind switch
            {
                LocalPlayReadinessKind.Ready => "组件已匹配；可将本次配置应用到下一张本地地图",
                LocalPlayReadinessKind.NeedsLocalPreparation => "需要准备："+state.Reason,
                LocalPlayReadinessKind.NeedsRepair => "需要处理："+state.Reason,
                _ => "无法确认："+state.Reason
            };
        }
    }
    public string PrepareActionText => Mode==MatchPlayMode.NormalHuman ? _runtime.Identity.BlocksHuman ? "准备普通模式" : "普通模式暂无阵容自动应用" :
        _runtime.Assess(LocalPlayMode.EnhancedHuman,(BotDifficulty)(int)Profile).Kind==LocalPlayReadinessKind.Ready ? "应用到下局" : "准备对局";
    public bool CanPrepareAction => Mode==MatchPlayMode.EnhancedHuman || _runtime.Identity.BlocksHuman;
    public string DraftState => _queuedDigest is null ? "当前编辑尚未排队" : Friendly is null || Enemy is null ? "正在准备" : MatchSetupStore.Digest(Snapshot())==_queuedDigest ? "当前编辑与已排队快照一致" : "当前编辑与排队快照不同；保存为下局后才会应用";
    public RelayCommand SaveCommand { get; }public RelayCommand QueueCommand { get; }public RelayCommand NewCommand { get; }public RelayCommand DuplicateCommand { get; }public RelayCommand LoadCommand { get; }public RelayCommand DeleteCommand { get; }public RelayCommand ReuseLastCommand { get; }public RelayCommand ImportCommand { get; }public RelayCommand ExportCommand { get; }public RelayCommand RefreshStatusCommand { get; }public RelayCommand SwapLineupsCommand { get; }public RelayCommand UndoSwapCommand { get; }
    public MatchSetupPreset Snapshot()=>_basis with {Name=Name,HumanSide=HumanSide,FriendlyBots=FriendlyBots,EnemyBots=EnemyBots,ReplacedSlot=ReplacedSlot,Friendly=Friendly.Snapshot(),Enemy=Enemy.Snapshot(),Favorite=Favorite,Profile=Profile,Aim=Aim,Nades=Nades,Mode=Mode,Map=Map};
    public void ValidateDesired()
    {
        var desired=Snapshot();_catalog.Resolve(desired);
        if (!_runtime.HasMap(desired.Map)) throw new InvalidOperationException("本机没有所选地图文件；请更换地图");
    }
    public void Load(MatchSetupPreset p)
    {
        _basis=p;_name=p.Name;_side=p.HumanSide;_friendly=p.FriendlyBots;_enemy=p.EnemyBots;_replacement=p.ReplacedSlot;_favorite=p.Favorite;_profile=p.Profile;_aim=p.Aim;_nades=p.Nades;_mode=p.Mode;_map=p.Map;
        Friendly.LoadSelection(p.Friendly);Enemy.LoadSelection(p.Enemy);Notify();RefreshPreview();
    }
    private void SwitchTo(MatchSetupPreset next)
    {
        if (MatchSetupStore.Digest(Snapshot())!=MatchSetupStore.Digest(_basis))
        {
            var decision=_dialogs.ConfirmUnsavedChanges(Name);
            if (decision==UnsavedChangesResolution.Cancel)return;
            if (decision==UnsavedChangesResolution.SaveAndSwitch)
            {
                try { Save(); } catch(Exception ex) { _feedback="保存方案失败，当前草稿保留："+ex.Message;Notify();return; }
            }
        }
        _lastSwap=null;Load(next);
    }
    public string FriendlySummary => $"{HumanSide} · 我 + {FriendlyBots} Bot";
    public string EnemySummary => $"{(HumanSide==MatchSide.CT ? MatchSide.T : MatchSide.CT)} · {EnemyBots} Bot";
    private void RefreshPreview()
    {
        if(Friendly is null||Enemy is null)return;
        try
        {
            var lineup=_catalog.Resolve(Snapshot());
            _preview=$"最终 CT {lineup.CtCount} 人 / T {lineup.TCount} 人 · 真人替代我方席位 {ReplacedSlot+1}\n"+string.Join("\n",new[]{MatchSide.CT,MatchSide.T}.Select(side=>$"{side}: "+string.Join(" · ",lineup.Bots.Where(b=>b.Side==side).Select(b=>b.Name))+(HumanSide==side?" · 我（真人）":"")));
            if(lineup.Warnings.Length>0)_preview+="\n"+string.Join("\n",lineup.Warnings.Distinct());
        }
        catch(Exception ex) { _preview="配置不可应用："+ex.Message; }
        var friendlySeats=Enumerable.Range(0,5).Where(i=>i!=ReplacedSlot).Take(FriendlyBots).ToHashSet();
        foreach(var slot in Friendly.Slots) slot.Participation=slot.Index==ReplacedSlot ? "我 · 真人" : friendlySeats.Contains(slot.Index) ? "Bot" : "不出场";
        foreach(var slot in Enemy.Slots) slot.Participation=slot.Index<EnemyBots ? "Bot" : "不出场";
        OnPropertyChanged(nameof(FriendlySummary));
        OnPropertyChanged(nameof(EnemySummary));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DraftState));
        OnPropertyChanged(nameof(PreparationText));OnPropertyChanged(nameof(PrepareActionText));OnPropertyChanged(nameof(CanPrepareAction));
    }
    private void SaveLibrary(MatchLibrary value)
    {
        if(!_loaded)throw new InvalidDataException("现有文件读取失败；请保留并修复该文件，禁止覆盖");
        _store.Save(value);_library=value;Friendly?.RefreshLibrary();Enemy?.RefreshLibrary();OnPropertyChanged(nameof(Presets));
    }
    private void Save()
    {
        var p=Snapshot();_catalog.Resolve(p);SaveLibrary(_library with {Presets=_library.Presets.Where(x=>x.Id!=p.Id).Append(p).ToImmutableArray()});_basis=p;_feedback="方案已保存；尚未应用到游戏。";
    }
    private void Queue()
    {
        ValidateDesired();var desired=Snapshot();
        if (desired.Mode!=MatchPlayMode.EnhancedHuman) throw new InvalidOperationException("普通人机尚无阵容自动应用通道；方案可保存，不能标为已准备");
        var readiness=_runtime.Assess(LocalPlayMode.EnhancedHuman,(BotDifficulty)(int)desired.Profile);
        if (readiness.Kind!=LocalPlayReadinessKind.Ready) throw new InvalidOperationException(readiness.Reason);
        MatchRuntimeContract.Validate(_runtime.Cs2Root,_runtime.DataRoot);
        Save();var r=_store.Queue(desired);_feedback="配置已准备供下一张 "+desired.Map+" 本地地图接收；尚未在游戏内应用。需要以 -insecure 启动离线游戏并加载该地图。";RefreshRuntime();
    }
    public void PrepareRequest() { Queue();Notify(); }
    private void Delete()
    {
        if(SelectedPreset is null)return;var id=SelectedPreset.Preset.Id;
        if(!_dialogs.Confirm("删除对局预设","只删除此个人预设；已排队的快照保持独立。"))return;
        SaveLibrary(_library with {Presets=_library.Presets.Where(p=>p.Id!=id).ToImmutableArray(),Recent=_library.Recent.Where(r=>r!=id).ToImmutableArray()});SelectedPreset=null;_feedback="预设已删除，旧文件备份保留。";
    }
    private void Import() { var p=_dialogs.ShowOpenFileDialog("Match setup (*.json)|*.json");if(p is null)return;MatchSetupStore.CheckPath(p);if(!_loaded)throw new InvalidDataException("现有 library 损坏，禁止覆盖");_store.Import(File.ReadAllText(p));_library=_store.Load();Friendly.RefreshLibrary();Enemy.RefreshLibrary();_feedback="导入完成；相同条目去重，内置阵容与 Human 预设不变。"; }
    private void Export() { var p=_dialogs.ShowSaveFileDialog("match-library.json","Match setup (*.json)|*.json");if(p is null)return;MatchSetupStore.CheckPath(p);File.WriteAllText(p,_store.Export());_feedback="个人阵容和整局预设已导出。"; }
    public void RefreshRuntime()
    {
        try
        {
            _library=_store.Load();var request=_store.ReadRequest();var pending=_store.ReadPendingRequest();
            _queuedDigest=pending?.PresetSha256;
            _status=request is null?"配置未准备":"配置"+(pending is null?"已消费或过期；重新准备后才能用于下一局":"已准备供下局接收 · "+pending.Preset.Map);
            if(request is not null&&File.Exists(_store.StatusPath))
            {
                MatchSetupStore.CheckPath(_store.StatusPath);var s=MatchJson.Read<MatchRuntimeStatus>(File.ReadAllText(_store.StatusPath));
                if(s.SchemaVersion==1&&s.RequestId==request.RequestId&&s.PresetSha256==request.PresetSha256&&s.UpdatedAt>=request.SavedAt)
                {
                    var processes=Process.GetProcessesByName("cs2");bool current=false;
                    try { current=processes.Any(p=>p.Id==s.ProcessId&&new DateTimeOffset(p.StartTime.ToUniversalTime())==s.ProcessStartUtc); } finally { foreach(var p in processes)p.Dispose(); }
                    var label=s.State switch { MatchApplyState.Saved=>"配置已保存",MatchApplyState.Pending=>"开局待应用",MatchApplyState.Received=>"运行时已接收",MatchApplyState.InsufficientEvidence=>"运行时已接收 / 行为验证不足",MatchApplyState.BehaviorObserved=>"内部行为已观察 / 玩法待验收",_=>"失败" };
                    _status=$"{(current?"本局":"上次会话")} · {label} · {s.Map} · {s.UpdatedAt.ToLocalTime():HH:mm:ss}\n{s.Reason}\nCT {s.CtCount} / T {s.TCount}";
                    if(s.AimReadback is not null)_status+="\nAim "+s.AimReadback;
                    if(s.NadesReadback is not null)_status+="\nNades "+s.NadesReadback;
                }
            }
            var id=_runtime.Identity.InstallId;
            if(id is not null)
            {
                using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(_runtime.BackupRoot,id,"candidate-manifest.json")));
                if(!manifest.RootElement.TryGetProperty("matchSetup",out _))_status+="\n当前安装缺少 LocalKitMatchSetup 适配层。配置可保存，需在明确授权的恢复/新候选安装后才能自动应用。";
            }
            else _status+="\n尚无活动增强候选；自动应用通道未安装。";
        }
        catch(Exception ex) { _status="无法确认运行时状态（原文件保留）："+ex.Message; }
        Notify();
    }
    private void Run(Action action) { try { action(); } catch(Exception ex) { _feedback="未完成："+ex.Message; }Notify(); }
    private void Notify()
    {
        foreach(var p in new[]{nameof(Name),nameof(HumanSide),nameof(FriendlyBots),nameof(EnemyBots),nameof(ReplacedSlot),nameof(Favorite),nameof(Profile),nameof(Mode),nameof(Map),nameof(Aim),nameof(Nades),nameof(Presets),nameof(Feedback),nameof(ApplyStatus),nameof(DraftState),nameof(PreparationText),nameof(PrepareActionText),nameof(CanPrepareAction)})OnPropertyChanged(p);
    }
}
