using System.Collections.Immutable;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.MatchSetup;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class MatchSetupUiTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"match-ui-"+Guid.NewGuid().ToString("N"));
    private readonly RosterCatalog _catalog=RosterCatalog.LoadBuiltIn();
    private MatchSetupStore Store=>new(Path.Combine(_root,"app-data/match-setup"),_catalog);
    private MatchSetupViewModel Vm(IDialogService? dialog=null)=>new(Store,_catalog,dialog??new MockDialogService(),new BotExperimentService(_root,Path.Combine(_root,"no-game"),()=>false));
    public void Dispose() { if(Directory.Exists(_root))Directory.Delete(_root,true); }
    [Fact] public void TeamsVersionsHumanReplacementSaveButCannotPrepareWithoutGameAndAdapter()
    {
        var vm=Vm();vm.Friendly.TeamId="team-13";vm.Friendly.Version=vm.Friendly.Versions.Single(v=>v.Id=="team-13-2024-03-31");
        vm.Enemy.TeamId="team-2";vm.HumanSide=MatchSide.T;vm.ReplacedSlot=3;vm.Aim=AimSetting.Head;vm.Nades=NadeSetting.Max;
        Assert.Contains("最终 CT 5 人 / T 5 人",vm.Preview);Assert.Contains("席位 4",vm.Preview);
        vm.SaveCommand.Execute(null);var saved=Store.Load().Presets.Single();
        Assert.Equal(MatchSide.T,saved.HumanSide);Assert.Equal(3,saved.ReplacedSlot);Assert.Equal(AimSetting.Head,saved.Aim);Assert.Equal(NadeSetting.Max,saved.Nades);
        Assert.False(File.Exists(Path.Combine(Store.Root,"next-match.json")));Assert.Equal(AimSetting.Head,Store.ReadDaily()!.Preset.Aim);
    }
    [Fact] public void ExplicitPersonaSearchReplacesSelectedSeatAndTypingClearsBinding()
    {
        var vm=Vm();vm.Friendly.PlayerQuery="ropz";vm.Friendly.SelectedPersona=vm.Friendly.Personas.First(p=>p.Player.Enabled);vm.Friendly.SelectedSeat=2;
        vm.Friendly.ReplaceCommand.Execute(null);Assert.NotNull(vm.Snapshot().Friendly.Slots[2].PersonaId);
        vm.Friendly.Slots[2].Name="my local bot";Assert.Null(vm.Snapshot().Friendly.Slots[2].PersonaId);
    }
    [Fact] public void DisabledNamesVisibleButCannotBeBound()
    {
        var vm=Vm();var p=_catalog.Players.First(p=>!p.Enabled);vm.Enemy.PlayerQuery=p.Name;vm.Enemy.SelectedPersona=vm.Enemy.Personas.First(p=>!p.Player.Enabled);
        Assert.False(vm.Enemy.ReplaceCommand.CanExecute(null));
    }
    [Fact] public void CustomRosterCreateCopyRenameAndDeleteKeepBuiltInUnchanged()
    {
        var dialog=new MockDialogService {ConfirmToReturn=true};var vm=Vm(dialog);vm.Friendly.TeamId="team-13";var before=_catalog.Rosters.First(r=>r.TeamId=="team-13").Slots;
        vm.Friendly.CustomName="自由队";vm.Friendly.NewCustomCommand.Execute(null);var first=Store.Load().CustomRosters.Single();
        vm.Friendly.Slots[0].Name="custom player";vm.Friendly.CustomName="自由队改名";vm.Friendly.SaveCustomCommand.Execute(null);
        Assert.Equal(first.Id,Store.Load().CustomRosters.Single().Id);Assert.Equal("自由队改名",Store.Load().CustomRosters.Single().Name);
        vm.Friendly.CustomName="copy";vm.Friendly.CopyCustomCommand.Execute(null);Assert.Equal(2,Store.Load().CustomRosters.Length);
        vm.Friendly.DeleteCustomCommand.Execute(null);Assert.Single(Store.Load().CustomRosters);Assert.Equal(before,_catalog.Rosters.First(r=>r.TeamId=="team-13").Slots);
    }
    [Fact] public void FavoritesPresetCopyRecentAndLastReuseAreRealLibraryState()
    {
        var vm=Vm();vm.Favorite=true;vm.SaveCommand.Execute(null);vm.PresetFilter=1;Assert.Single(vm.Presets);
        vm.DuplicateCommand.Execute(null);vm.SaveCommand.Execute(null);Assert.Equal(2,Store.Load().Presets.Length);
        var last=Store.Load().Presets[0] with {Aim=AimSetting.Body};Store.Save(Store.Load() with {LastUsed=last,Recent=[last.Id]});
        vm.RefreshRuntime();vm.PresetFilter=2;Assert.Single(vm.Presets);vm.ReuseLastCommand.Execute(null);Assert.Equal(AimSetting.Body,vm.Aim);
    }
    [Fact] public void DisabledInvalidRosterKeepsDraftAndSavesIndependentPreferencesWithoutEnablingIt()
    {
        var vm=Vm();vm.Enemy.Slots[0].Name="bad;bot_kick";
        vm.Profile=InstalledProfile.High;vm.Aim=AimSetting.Head;vm.Nades=NadeSetting.Max;
        var saved=Store.ReadDaily()!;
        Assert.False(saved.LineupEnabled);Assert.Equal("bad;bot_kick",saved.Preset.Enemy.Slots[0].Name);
        Assert.Equal(InstalledProfile.High,saved.Preset.Profile);Assert.Equal(AimSetting.Head,saved.Preset.Aim);Assert.Equal(NadeSetting.Max,saved.Preset.Nades);
        Assert.Contains("未启用",vm.Preview);Assert.Equal("bad;bot_kick",Vm().Enemy.Slots[0].Name);
        vm.LineupEnabled=true;Assert.False(Store.ReadDaily()!.LineupEnabled);Assert.Equal(saved.Revision,Store.ReadDaily()!.Revision);Assert.Contains("未保存",vm.PreparationText);
        vm.Enemy.Slots[0].Name="fixed player";Assert.True(Store.ReadDaily()!.LineupEnabled);
    }
    [Fact] public void OldOrWrongRequestStatusIsNeverCurrentAcceptance()
    {
        var vm=Vm();var request=Store.ReadDaily()!;
        Store.WriteStatus(new(1,"old","different",MatchSetupStore.Digest(request.Preset),"de_mirage",DateTimeOffset.UtcNow,MatchApplyState.BehaviorObserved,"old success",5,5));
        vm.RefreshRuntime();Assert.DoesNotContain("old success",vm.ApplyStatus);
        Store.WriteStatus(new(1,"old",request.Revision,MatchSetupStore.Digest(request.Preset),"de_mirage",DateTimeOffset.UtcNow,MatchApplyState.InsufficientEvidence,"received only",5,5,DailyRevision:request.Revision));
        vm.RefreshRuntime();Assert.Contains("上次地图报告",vm.ApplyStatus);Assert.Contains("received only",vm.ApplyStatus);
    }
    [Fact] public void CorruptLibraryDoesNotGetOverwrittenByQueueOrCustomSave()
    {
        Directory.CreateDirectory(Store.Root);File.WriteAllText(Store.LibraryPath,"corrupt");var vm=Vm();vm.Aim=AimSetting.Head;vm.Friendly.NewCustomCommand.Execute(null);
        Assert.Equal("corrupt",File.ReadAllText(Store.LibraryPath));Assert.False(File.Exists(Store.DailyPath));
    }
    [Fact] public void OrdinaryModeCanRandomizeIndependentSides()
    {
        var vm=Vm();vm.Friendly.RandomizeCommand.Execute(null);vm.Enemy.RandomizeCommand.Execute(null);
        Assert.All(vm.Snapshot().Friendly.Slots,s=>Assert.NotNull(s.PersonaId));Assert.All(vm.Snapshot().Enemy.Slots,s=>Assert.NotNull(s.PersonaId));
        Assert.Equal(9,_catalog.Resolve(vm.Snapshot()).Bots.Length);
    }
    [Fact] public void DailyChangesPersistWithoutChangingSavedLibraryOrLegacyRequest()
    {
        var vm=Vm();vm.SaveCommand.Execute(null);var library=File.ReadAllBytes(Store.LibraryPath);
        var legacy=Path.Combine(Store.Root,"next-match.json");File.WriteAllText(legacy,"old one shot request");
        vm.Aim=AimSetting.Body;vm.LineupEnabled=true;
        Assert.Equal(AimSetting.Body,Store.ReadDaily()!.Preset.Aim);Assert.True(Store.ReadDaily()!.LineupEnabled);
        Assert.Equal(library,File.ReadAllBytes(Store.LibraryPath));Assert.Equal("old one shot request",File.ReadAllText(legacy));
        Assert.Equal(AimSetting.Body,Vm().Aim);Assert.True(Vm().LineupEnabled);
    }
    [Fact] public void PersonalLibraryExportImportDeduplicatesThroughUi()
    {
        var path=Path.Combine(_root,"export.json");var dialog=new MockDialogService {SaveFileDialogResult=path,OpenFileDialogResult=path};var vm=Vm(dialog);
        vm.Friendly.CustomName="exported";vm.Friendly.NewCustomCommand.Execute(null);vm.SaveCommand.Execute(null);vm.ExportCommand.Execute(null);
        Assert.True(File.Exists(path));vm.ImportCommand.Execute(null);vm.ImportCommand.Execute(null);
        Assert.Single(Store.Load().Presets);Assert.Single(Store.Load().CustomRosters);Assert.Contains("导入完成",vm.Feedback);
    }
    [Fact] public void DailySettingsSurviveMapChoiceAndTimeWithoutClaims()
    {
        var store=Store;var value=store.SaveDaily(MatchSetupPreset.Default(),false);
        File.WriteAllText(store.DailyPath,MatchJson.Write(value with {SavedAt=DateTimeOffset.UtcNow.AddDays(-5)}));
        Assert.Equal(value.Revision,store.ReadDaily()!.Revision);Assert.False(Directory.Exists(Path.Combine(store.Root,"claims")));
    }
    [Fact] public void MapNameCannotCarryConsoleCommands()
    {
        Assert.Throws<InvalidDataException>(()=>_catalog.Resolve(MatchSetupPreset.Default() with {Map="de_dust2;bot_kick"}));
    }
}
