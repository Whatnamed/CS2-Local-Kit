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
        vm.QueueCommand.Execute(null);Assert.Null(Store.ReadRequest());Assert.Contains("本机没有所选地图",vm.Feedback);
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
    [Fact] public void InvalidDraftDoesNotQueueOrClaimSavedSuccess()
    {
        var vm=Vm();vm.Enemy.Slots[0].Name="bad;bot_kick";vm.QueueCommand.Execute(null);
        Assert.False(File.Exists(Store.RequestPath));Assert.Contains("未完成",vm.Feedback);Assert.Contains("不可应用",vm.Preview);
    }
    [Fact] public void OldOrWrongRequestStatusIsNeverCurrentAcceptance()
    {
        var vm=Vm();var request=Store.Queue(vm.Snapshot());
        Store.WriteStatus(new(1,"old","different",request.PresetSha256,"de_mirage",DateTimeOffset.UtcNow,MatchApplyState.BehaviorObserved,"old success",5,5));
        vm.RefreshRuntime();Assert.DoesNotContain("old success",vm.ApplyStatus);
        Store.WriteStatus(new(1,"old",request.RequestId,request.PresetSha256,"de_mirage",DateTimeOffset.UtcNow,MatchApplyState.InsufficientEvidence,"received only",5,5));
        vm.RefreshRuntime();Assert.Contains("上次会话",vm.ApplyStatus);Assert.Contains("行为验证不足",vm.ApplyStatus);
    }
    [Fact] public void CorruptLibraryDoesNotGetOverwrittenByQueueOrCustomSave()
    {
        Directory.CreateDirectory(Store.Root);File.WriteAllText(Store.LibraryPath,"corrupt");var vm=Vm();vm.QueueCommand.Execute(null);vm.Friendly.NewCustomCommand.Execute(null);
        Assert.Equal("corrupt",File.ReadAllText(Store.LibraryPath));Assert.False(File.Exists(Store.RequestPath));
    }
    [Fact] public void OrdinaryModeCanRandomizeIndependentSides()
    {
        var vm=Vm();vm.Friendly.RandomizeCommand.Execute(null);vm.Enemy.RandomizeCommand.Execute(null);
        Assert.All(vm.Snapshot().Friendly.Slots,s=>Assert.NotNull(s.PersonaId));Assert.All(vm.Snapshot().Enemy.Slots,s=>Assert.NotNull(s.PersonaId));
        Assert.Equal(9,_catalog.Resolve(vm.Snapshot()).Bots.Length);
    }
    [Fact] public void DraftChangesDoNotMutateQueuedSnapshot()
    {
        var vm=Vm();var request=Store.Queue(vm.Snapshot());vm.RefreshRuntime();
        Assert.Contains("一致",vm.DraftState);vm.Aim=AimSetting.Body;
        Assert.Contains("不同",vm.DraftState);Assert.Equal(request.RequestId,Store.ReadRequest()!.RequestId);Assert.Equal(request.PresetSha256,Store.ReadRequest()!.PresetSha256);
        Store.Queue(vm.Snapshot());vm.RefreshRuntime();Assert.Equal(AimSetting.Body,Store.ReadRequest()!.Preset.Aim);Assert.Contains("一致",vm.DraftState);
    }
    [Fact] public void PersonalLibraryExportImportDeduplicatesThroughUi()
    {
        var path=Path.Combine(_root,"export.json");var dialog=new MockDialogService {SaveFileDialogResult=path,OpenFileDialogResult=path};var vm=Vm(dialog);
        vm.Friendly.CustomName="exported";vm.Friendly.NewCustomCommand.Execute(null);vm.SaveCommand.Execute(null);vm.ExportCommand.Execute(null);
        Assert.True(File.Exists(path));vm.ImportCommand.Execute(null);vm.ImportCommand.Execute(null);
        Assert.Single(Store.Load().Presets);Assert.Single(Store.Load().CustomRosters);Assert.Contains("导入完成",vm.Feedback);
    }
    [Fact] public void QueuedRequestIsOneShotForItsExactMapAndExpires()
    {
        var store=Store;var request=store.Queue(MatchSetupPreset.Default());
        Assert.Null(store.ClaimForMap("de_mirage","session-one"));
        Assert.Equal(request.RequestId,store.ClaimForMap("de_dust2","session-one")!.RequestId);
        Assert.Null(store.ClaimForMap("de_dust2","session-two"));
        Assert.Null(store.ReadPendingRequest());
        var next=store.Queue(MatchSetupPreset.Default());
        Assert.NotEqual(request.RequestId,store.ReadPendingRequest()!.RequestId);
        File.WriteAllText(store.RequestPath,MatchJson.Write(next with {SavedAt=DateTimeOffset.UtcNow.AddHours(-3)}));
        Assert.Null(store.ReadPendingRequest());
    }
    [Fact] public void MapNameCannotCarryConsoleCommands()
    {
        Assert.Throws<InvalidDataException>(()=>_catalog.Resolve(MatchSetupPreset.Default() with {Map="de_dust2;bot_kick"}));
    }
}
