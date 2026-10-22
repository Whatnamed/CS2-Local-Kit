using System.Text.Json;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Runtime;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class BotIntegrationTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "cs2-bot-ui-" + Guid.NewGuid().ToString("N"));
    private readonly Runner _runner = new();
    private BotCandidate _candidate = null!;
    private string Root => Path.Combine(_home,"steamapps","common","cs2");
    private string HumanLock => Path.Combine(_home,"human-lock.json");
    private const string Id = "1234567890abcdef1234567890abcdef";
    private bool _running;
    public BotIntegrationTests()
    {
        var release = Path.Combine(_home,"releases","bot-experiment","fixed-Medium"); Directory.CreateDirectory(Path.Combine(release,"tools"));
        File.WriteAllText(Path.Combine(release,"tools","Invoke-BotExperiment.ps1"),"# synthetic, never executed");
        File.WriteAllText(Path.Combine(release,"tools","bot_experiment.py"),"# synthetic, never executed");
        var bundle = Directory.GetFiles(release,"*",SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(release,p).Replace('\\','/'),BotExperimentService.HashFile);
        var targetBuild = new { PatchVersion="1.41.9.1",ClientVersion="2000933",buildId="25966348" };
        File.WriteAllText(Path.Combine(release,"experiment-manifest.json"),JsonSerializer.Serialize(new {schemaVersion=1,lane="bot-experiment",testFixture=false,botRef="e18c029a76b7515ea5cb15cb08c41f03de4c0a57",metamod=new {build=1481},cssVersion="1.0.376",profile=new {difficulty="Medium"},bundleFiles=bundle,targetBuild,humanPolicy="preserve",coexistence=new {status="ready-for-manual-test",reviewedBuild=targetBuild}}));
        _candidate = new(BotDifficulty.Medium,"fixed-Medium",BotExperimentService.HashFile(Path.Combine(release,"experiment-manifest.json")),true);
        Directory.CreateDirectory(Path.Combine(Root,"game","csgo"));
        File.WriteAllText(Path.Combine(Root,"game/csgo/steam.inf"),"PatchVersion=1.41.9.1\nClientVersion=2000933\n");
        File.WriteAllText(Path.Combine(Root,"game/csgo/gameinfo.gi"),"SearchPaths\n{\n Game csgo/addons/metamod\n}\n");
        File.WriteAllText(Path.GetFullPath(Path.Combine(Root,"../..","appmanifest_730.acf")),"\"AppState\" { \"StateFlags\" \"4\" \"buildid\" \"25966348\" }");
        string Seed(string path)
        {
            var file=Path.Combine(Root,"game/csgo",path);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,"synthetic-"+path);return BotExperimentService.HashFile(file);
        }
        var mm=new Dictionary<string,string>{["addons/metamod/bin/win64/metamod.2.cs2.dll"]=Seed("addons/metamod/bin/win64/metamod.2.cs2.dll"),["addons/metamod/bin/win64/server.dll"]=Seed("addons/metamod/bin/win64/server.dll")};
        var css=new Dictionary<string,string>{["addons/counterstrikesharp/bin/win64/counterstrikesharp.dll"]=Seed("addons/counterstrikesharp/bin/win64/counterstrikesharp.dll"),["addons/counterstrikesharp/api/CounterStrikeSharp.API.dll"]=Seed("addons/counterstrikesharp/api/CounterStrikeSharp.API.dll"),["addons/metamod/counterstrikesharp.vdf"]=Seed("addons/metamod/counterstrikesharp.vdf")};
        var human=Seed("addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll");
        File.WriteAllText(HumanLock,JsonSerializer.Serialize(new {repository="synthetic",@ref="synthetic",tag="test",status="accepted-as-patched",acceptedRuntime=new {patchedDllSha256=human},framework=new {metamod=new {version="1473",installedFiles=mm},counterstrikesharp=new {version="376",installedFiles=css}}}));
    }
    private BotExperimentService Service(IReadOnlyList<BotCandidate>? candidates=null) => new(_home,Root,()=>_running,_runner,candidates ?? [_candidate],HumanLock);
    private void Active(BotExperimentService service, string state="installed", bool marker=true, bool preservesHuman=true)
    {
        var backup = Path.Combine(service.BackupRoot,Id); Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup,"install-record.json"),JsonSerializer.Serialize(new {schemaVersion=1,lane="bot-experiment",installId=Id,cs2Root=Root,manifestSha256=_candidate.ManifestSha256,state,profile=new {difficulty="Medium"},humanPolicy=preservesHuman?"preserve":"isolate"}));
        if (marker) File.WriteAllText(Path.Combine(Root,"game","csgo",".cs2-local-kit-bot-experiment.json"),JsonSerializer.Serialize(new {installId=Id,backupRoot=service.BackupRoot,manifestSha256=_candidate.ManifestSha256,humanPolicy=preservesHuman?"preserve":"isolate"}));
    }
    public void Dispose() { Directory.Delete(_home,true); }
    private sealed class Runner : IBotProcessRunner
    {
        public List<(string Exe,string[] Args)> Calls { get; } = [];
        public Func<string[],Task<BotProcessResult>>? Handler { get; set; }
        public Task<BotProcessResult> RunAsync(string executable,IReadOnlyList<string> arguments)
        {
            Calls.Add((executable,arguments.ToArray()));
            return executable=="python" ? Task.FromResult(new BotProcessResult(0,"Python 3.12.10", "")) : Handler?.Invoke(arguments.ToArray()) ?? Task.FromResult(new BotProcessResult(0,"{\"state\":\"preview\",\"entries\":[]}",""));
        }
    }

    [Fact] public void NavigationAndCommandsAreExplicitPendingFacts()
    {
        var nav = new NavigationViewModel(); nav.NavigateBotCommand.Execute(null);
        Assert.True(nav.IsBotSelected); Assert.False(nav.IsHumanPage); Assert.Equal("人机",nav.PageTitle);
        var vm = new BotViewModel(Service(),new MockDialogService(),()=>{},text=>Assert.Equal("bot_aim head; bot_nades max",text));
        Assert.Equal(BotDifficulty.Medium,vm.Difficulty);
        Assert.Equal(3,vm.DifficultyChoices.Count); Assert.Equal(3,vm.AimChoices.Count); Assert.Equal(5,vm.NadesChoices.Count);
        vm.Aim=BotAim.Head;vm.Nades=BotNades.Max;vm.CopyCommandsCommand.Execute(null);
        Assert.Contains("尚未确认游戏中生效",vm.Feedback);Assert.Empty(_runner.Calls);
        var reloaded = Service().LoadPreferences();Assert.Equal(BotAim.Head,reloaded.Aim); Assert.Equal(BotNades.Max,reloaded.Nades);
        vm.Setup.Aim=CS2LocalKit.Core.MatchSetup.AimSetting.Body;
        Assert.Equal("bot_aim body; bot_nades max",vm.ConsoleCommands);
    }
    [Fact] public void ShippedCandidateCatalogHasAllThreeExactVariants()
    {
        var service=new BotExperimentService(_home,Root,()=>false,_runner);
        Assert.Equal(6,service.Candidates.Count);
        Assert.Equal(3,service.Candidates.Select(c=>c.Difficulty).Distinct().Count());
        Assert.All(service.Candidates,c=>Assert.Matches("^[0-9a-f]{64}$",c.ManifestSha256));
        Assert.Equal(3,service.Candidates.Count(c=>!c.PreservesHuman));
        foreach (var level in Enum.GetValues<BotDifficulty>())
        {
            Assert.Single(service.Candidates,c=>c.Difficulty==level && c.PreservesHuman);
            Assert.True(service.Candidate(level).PreservesHuman);
        }
    }
    [Fact] public async Task LegacyIsolationCandidateCannotEnableDailyJointMode()
    {
        var service=Service([_candidate with {PreservesHuman=false}]);
        Assert.False(service.Readiness(BotDifficulty.Medium).CanEnable);
        var vm=new BotViewModel(service,new MockDialogService(),()=>{},_=>{});
        vm.TargetMode=LocalPlayMode.EnhancedHuman;
        Assert.Contains("普通人机 + 我的饰品",vm.CurrentMode);
        Assert.Contains("隔离 Human",vm.AvailabilityText);
        Assert.False(vm.PreviewModeCommand.CanExecute(null));Assert.False(vm.SwitchModeCommand.CanExecute(null));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ExecuteAsync("Install",BotDifficulty.Medium,true));
        Assert.Empty(_runner.Calls);
    }
    [Fact] public void TargetPreferenceAndRestoredHistoryDoNotClaimActiveMode()
    {
        var service=Service();Active(service,"restored",false);
        service.SavePreferences(new(LastInstallId:Id,TargetMode:LocalPlayMode.EnhancedHuman));
        var vm=new BotViewModel(service,new MockDialogService(),()=>{},_=>{});
        Assert.Equal(LocalPlayMode.EnhancedHuman,vm.TargetMode);
        Assert.Equal("普通人机 + 我的饰品",vm.CurrentMode);Assert.Equal("—",vm.InstallId);
        Assert.False(vm.DiagnosticsCommand.CanExecute(null));
        Assert.False(service.Identity.BlocksHuman);
    }
    [Fact] public async Task BuildDriftAndSteamUpdateBlockBeforeAnyExternalProcess()
    {
        var steam=Path.GetFullPath(Path.Combine(Root,"../..","appmanifest_730.acf"));
        var original=File.ReadAllText(steam);
        File.WriteAllText(steam,original.Replace("25966348","26087496"));
        Assert.Contains("过期",Service().Readiness(BotDifficulty.Medium).Reason);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ExecuteAsync("Preview",BotDifficulty.Medium));
        File.WriteAllText(steam,original.Replace("\"4\"","\"1026\""));
        Assert.False(Service().SteamState.Idle);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ExecuteAsync("Restore",BotDifficulty.Medium,true,Id));
        Assert.Empty(_runner.Calls);
    }
    [Fact] public async Task GameStartingAfterPreviewCannotReachApply()
    {
        _runner.Handler=_=>{_running=true;return Task.FromResult(new BotProcessResult(0,"{\"entries\":[]}",""));};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ExecuteAsync("Install",BotDifficulty.Medium,true));
        Assert.DoesNotContain(_runner.Calls,c=>c.Args.Contains("-Apply"));
    }
    [Fact] public async Task SuccessfulProcessWithoutInstalledRecordIsNotSuccessfulSwitch()
    {
        _runner.Handler=_=>Task.FromResult(new BotProcessResult(0,$"{{\"state\":\"installed\",\"installId\":\"{Id}\"}}",""));
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>Service().ExecuteAsync("Install",BotDifficulty.Medium,true));
        Assert.Contains("磁盘/事务状态未确认",error.Message);Assert.False(Service().Identity.BlocksHuman);
    }
    [Fact] public void JointIdentityKeepsHumanButFreezesItsWritesUntilExactRestore()
    {
        var service=Service();Active(service);
        Assert.True(service.Identity.PreservesHuman);Assert.True(service.Identity.BlocksHuman);
        var vm=new BotViewModel(service,new MockDialogService(),()=>{},_=>{});
        Assert.Contains("增强人机 + 我的饰品",vm.CurrentMode);Assert.Contains("Human 保留",vm.StateText);
        var applier=new FixtureApplier(new(){CsgoDir=Path.Combine(Root,"game/csgo"),BotExperimentBackupRoot=service.BackupRoot,Cs2RunningProbe=()=>false});
        Assert.Throws<ApplyException>(()=>applier.Apply(HumanPresetTemplate.CreateMinimalValid(),TestFixtures.FakeSteamId64));
        Assert.Contains("先恢复普通模式",service.Identity.Reason);
    }
    [Theory] [InlineData("0")] [InlineData("1026")] [InlineData("6")]
    public void SteamStateFailsClosedForNonIdleStates(string flags)
    {
        var path=Path.GetFullPath(Path.Combine(Root,"../..","appmanifest_730.acf"));
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"4\"","\""+flags+"\""));
        Assert.False(SteamInstallState.Inspect(Root).Idle);
    }
    [Fact] public async Task ExactPublicEntryUsesArgumentListAndFreshPreviewBeforeApply()
    {
        var service=Service();
        _runner.Handler=args=>
        {
            if(args.Contains("-Apply")) {Active(service);return Task.FromResult(new BotProcessResult(0,$"{{\"state\":\"installed\",\"installId\":\"{Id}\"}}",""));}
            return Task.FromResult(new BotProcessResult(0,"{\"entries\":[]}",""));
        };
        await service.ExecuteAsync("Install",BotDifficulty.Medium,true);
        var calls=_runner.Calls.Where(c=>c.Exe=="pwsh").ToArray();Assert.Equal(3,calls.Length);
        Assert.DoesNotContain("-Apply",calls[0].Args);Assert.Contains("-Apply",calls[1].Args);
        Assert.Contains("-PreserveHumanCosmetics",calls[0].Args);Assert.DoesNotContain("-IsolateHumanCosmetics",calls[0].Args);Assert.Contains(_candidate.ManifestSha256,calls[0].Args);
        Assert.Contains("Diagnostics",calls[2].Args);
        Assert.Contains(Root,calls[0].Args);Assert.Contains("-File",calls[0].Args);
        Assert.DoesNotContain(calls[0].Args,a=>a is "-Command" or "-AllowFakeTree");
    }
    [Fact] public async Task CandidateHashOrScriptDriftRefusesBeforeProcessExecution()
    {
        File.AppendAllText(Path.Combine(_home,"releases/bot-experiment/fixed-Medium/tools/Invoke-BotExperiment.ps1"),"drift");
        await Assert.ThrowsAsync<InvalidDataException>(()=>Service().ExecuteAsync("Preview",BotDifficulty.Medium));Assert.Empty(_runner.Calls);
    }
    [Theory] [InlineData("../outside")] [InlineData("C:\\outside")] [InlineData(".")]
    public async Task UnlistedOrEscapingCandidateRefused(string directory)
    {
        var service=Service([_candidate with {Directory=directory}]);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.ExecuteAsync("Preview",BotDifficulty.Medium));Assert.Empty(_runner.Calls);
    }
    [Fact] public async Task RunningAndMissingDifficultyCannotInvokeInstaller()
    {
        _running=true;await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ExecuteAsync("Install",BotDifficulty.Medium,true));
        _running=false;await Assert.ThrowsAsync<InvalidOperationException>(()=>Service().ExecuteAsync("Preview",BotDifficulty.High));Assert.Empty(_runner.Calls);
    }
    [Fact] public async Task ConcurrentOperationRefusedAndFailurePropagates()
    {
        var entered=new TaskCompletionSource();var resume=new TaskCompletionSource();
        _runner.Handler=async _=>{entered.SetResult();await resume.Task;return new(9,"","synthetic refusal: baseline drift");};
        var service=Service();var first=service.ExecuteAsync("Preview",BotDifficulty.Medium);await entered.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.ExecuteAsync("Install",BotDifficulty.Medium,true));
        resume.SetResult();var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>first);Assert.Contains("baseline drift",error.Message);
    }
    [Fact] public async Task RestoreRequiresExactIdRootAndWhitelistedManifest()
    {
        var service=Service();Active(service);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.ExecuteAsync("Restore",BotDifficulty.Medium,true,"latest"));
        await service.ExecuteAsync("Diagnostics",BotDifficulty.High,installId:Id);
        Assert.Contains(Id,_runner.Calls.Last().Args);Assert.Contains(service.BackupRoot,_runner.Calls.Last().Args);
        var file=Path.Combine(service.BackupRoot,Id,"install-record.json");File.WriteAllText(file,File.ReadAllText(file).Replace(_candidate.ManifestSha256,new string('0',64)));
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.ExecuteAsync("Restore",BotDifficulty.Medium,true,Id));
    }
    [Fact] public async Task ViewModelPreviewInvalidationAndBusyGate()
    {
        var vm=new BotViewModel(Service(),new MockDialogService(),()=>{},_=>{});
        Assert.False(vm.InstallCommand.CanExecute(null));await vm.PreviewAsync();Assert.True(vm.InstallCommand.CanExecute(null));Assert.Contains("待切换",vm.StateText);
        vm.Difficulty=BotDifficulty.High;Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.PreviewAsync();Assert.Contains("阻断",vm.StateText);Assert.Contains("没有已固定候选",vm.Feedback);
    }
    [Fact] public void CorruptMarkerShowsRecoveryWithoutClaimingInstalledMode()
    {
        File.WriteAllText(Path.Combine(Root,"game/csgo/.cs2-local-kit-bot-experiment.json"),"{broken");
        var vm=new BotViewModel(Service(),new MockDialogService(),()=>{},_=>{});
        Assert.Contains("需恢复",vm.CurrentMode);Assert.Equal("—",vm.InstallId);
        Assert.False(vm.SwitchModeCommand.CanExecute(null));
    }
    [Fact] public async Task ActiveInterruptedAndDriftStatesDoNotClaimLoadedOrPermitHumanWrites()
    {
        var service=Service();Active(service,"restoring",false);
        var vm=new BotViewModel(service,new MockDialogService(),()=>{},_=>{});
        Assert.Contains("中断",vm.StateText);Assert.Equal(Id,vm.InstallId);Assert.False(vm.CanSelectDifficulty);Assert.False(vm.InstallCommand.CanExecute(null));
        _runner.Handler=_=>Task.FromResult(new BotProcessResult(2,"","backup corrupt"));await vm.DiagnosticsAsync();
        Assert.Contains("阻断",vm.StateText);Assert.False(vm.RestoreCommand.CanExecute(null));Assert.Contains("backup corrupt",vm.Feedback);
    }
    [Fact] public async Task ViewModelInstallExactRestoreAndConfirmationCancellation()
    {
        var service=Service();var dialogs=new MockDialogService { ConfirmToReturn=false };
        var vm=new BotViewModel(service,dialogs,()=>{},_=>{});
        _runner.Handler=args=>
        {
            var action=args[Array.IndexOf(args,"-Action")+1];
            if (args.Contains("-Apply") && action=="Install")
            {
                Active(service);
                return Task.FromResult(new BotProcessResult(0,$"{{\"state\":\"installed\",\"installId\":\"{Id}\",\"entries\":[]}}",""));
            }
            if (args.Contains("-Apply") && action=="Restore")
            {
                File.Delete(Path.Combine(Root,"game/csgo/.cs2-local-kit-bot-experiment.json"));
                var record=Path.Combine(service.BackupRoot,Id,"install-record.json");File.WriteAllText(record,File.ReadAllText(record).Replace("installed","restored"));
            }
            return Task.FromResult(new BotProcessResult(0,"{\"state\":\"preview\",\"entries\":[]}",""));
        };
        await vm.PreviewAsync();var calls=_runner.Calls.Count;await vm.InstallAsync();Assert.Equal(calls,_runner.Calls.Count);
        dialogs.ConfirmToReturn=true;await vm.InstallAsync();Assert.Equal(Id,vm.InstallId);Assert.Contains("已安装待实机验证",vm.StateText);
        Assert.False(vm.CanSelectDifficulty);Assert.Equal("Medium",vm.InstalledDifficulty);Assert.False(vm.RestoreCommand.CanExecute(null));
        await vm.DiagnosticsAsync();Assert.True(vm.RestoreCommand.CanExecute(null));await vm.RestoreAsync();Assert.Contains("已恢复",vm.StateText);Assert.True(vm.CanSelectDifficulty);
        Assert.Equal(Id,service.LoadPreferences().LastInstallId);
    }
    [Fact] public void HumanCoreMutationAndStatusHonorActiveExperiment()
    {
        var service=Service();Active(service,preservesHuman:false);
        var applier=new FixtureApplier(new(){CsgoDir=Path.Combine(Root,"game","csgo"),BotExperimentBackupRoot=service.BackupRoot,Cs2RunningProbe=()=>false});
        Assert.Throws<ApplyException>(()=>applier.Apply(HumanPresetTemplate.CreateMinimalValid(),TestFixtures.FakeSteamId64));
        Assert.Throws<ApplyException>(()=>applier.RestoreLatest());
        var repair=new MetaModStartupRepairService(new(){Cs2Root=Root,BotExperimentBackupRoot=service.BackupRoot,Cs2RunningProbe=()=>false});
        Assert.Throws<MetaModStartupRepairException>(()=>repair.Repair());
        var status=new RuntimeStatusService(new(){Cs2Root=Root,BotExperimentBackupRoot=service.BackupRoot,Cs2RunningProbe=()=>false}).GetStatus();
        Assert.True(status.BotExperiment.BlocksHuman);Assert.Single(status.BlockedReasons);Assert.Contains("隔离",status.HealthSummary);Assert.DoesNotContain("缺失",status.HealthSummary);
    }
    [Fact] public void AmbiguousHistoryCannotGuessLatestBackup()
    {
        var service=Service();Active(service,"installing",false);
        var other=Path.Combine(service.BackupRoot,new string('a',32));Directory.CreateDirectory(other);
        var text=File.ReadAllText(Path.Combine(service.BackupRoot,Id,"install-record.json")).Replace(Id,new string('a',32));File.WriteAllText(Path.Combine(other,"install-record.json"),text);
        Assert.True(service.Identity.BlocksHuman);Assert.Null(service.Identity.InstallId);Assert.Contains("多个",service.Identity.Reason);
    }
}
