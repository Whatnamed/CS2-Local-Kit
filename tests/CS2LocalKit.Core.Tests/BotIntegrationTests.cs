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
    private string Root => Path.Combine(_home,"game-root");
    private const string Id = "1234567890abcdef1234567890abcdef";
    private bool _running;
    public BotIntegrationTests()
    {
        var release = Path.Combine(_home,"releases","bot-experiment","fixed-Medium"); Directory.CreateDirectory(Path.Combine(release,"tools"));
        File.WriteAllText(Path.Combine(release,"tools","Invoke-BotExperiment.ps1"),"# synthetic, never executed");
        File.WriteAllText(Path.Combine(release,"tools","bot_experiment.py"),"# synthetic, never executed");
        var bundle = Directory.GetFiles(release,"*",SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(release,p).Replace('\\','/'),BotExperimentService.HashFile);
        File.WriteAllText(Path.Combine(release,"experiment-manifest.json"),JsonSerializer.Serialize(new {schemaVersion=1,lane="bot-experiment",testFixture=false,botRef="e18c029a76b7515ea5cb15cb08c41f03de4c0a57",metamod=new {build=1481},cssVersion="1.0.376",profile=new {difficulty="Medium"},bundleFiles=bundle}));
        _candidate = new(BotDifficulty.Medium,"fixed-Medium",BotExperimentService.HashFile(Path.Combine(release,"experiment-manifest.json")));
        Directory.CreateDirectory(Path.Combine(Root,"game","csgo"));
    }
    private BotExperimentService Service(IReadOnlyList<BotCandidate>? candidates=null) => new(_home,Root,()=>_running,_runner,candidates ?? [_candidate]);
    private void Active(BotExperimentService service, string state="installed", bool marker=true)
    {
        var backup = Path.Combine(service.BackupRoot,Id); Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup,"install-record.json"),JsonSerializer.Serialize(new {schemaVersion=1,lane="bot-experiment",installId=Id,cs2Root=Root,manifestSha256=_candidate.ManifestSha256,state,profile=new {difficulty="Medium"}}));
        if (marker) File.WriteAllText(Path.Combine(Root,"game","csgo",".cs2-local-kit-bot-experiment.json"),JsonSerializer.Serialize(new {installId=Id,backupRoot=service.BackupRoot,manifestSha256=_candidate.ManifestSha256}));
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
    }
    [Fact] public async Task ExactPublicEntryUsesArgumentListAndFreshPreviewBeforeApply()
    {
        await Service().ExecuteAsync("Install",BotDifficulty.Medium,true);
        var calls=_runner.Calls.Where(c=>c.Exe=="pwsh").ToArray();Assert.Equal(2,calls.Length);
        Assert.DoesNotContain("-Apply",calls[0].Args);Assert.Contains("-Apply",calls[1].Args);
        Assert.Contains("-IsolateHumanCosmetics",calls[0].Args);Assert.Contains(_candidate.ManifestSha256,calls[0].Args);
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
        Assert.False(vm.InstallCommand.CanExecute(null));await vm.PreviewAsync();Assert.True(vm.InstallCommand.CanExecute(null));
        vm.Difficulty=BotDifficulty.High;Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.PreviewAsync();Assert.Contains("阻断",vm.StateText);Assert.Contains("没有已固定候选",vm.Feedback);
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
        var service=Service();Active(service);
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
