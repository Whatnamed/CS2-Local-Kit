using System.Text.Json;
using CS2LocalKit.App.Services;
using CS2LocalKit.App.Common;
using CS2LocalKit.App.ViewModels;
using CS2LocalKit.Core.MatchSetup;
using CS2LocalKit.Core.Runtime;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class DailyPlayTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "daily-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(_home,"steamapps/common/cs2");
    private string Data => Path.Combine(_home,"data");
    private const string Id = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private bool _running;
    private BotExperimentService Service => new(Data,Root,()=>_running,candidates:[]);
    private static string Hash(string file) => RuntimeIntegrationGuard.Hash(file);
    private void Write(string rel,string text) { var path=Path.Combine(Root,rel);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,text); }
    public DailyPlayTests()
    {
        Write("../../appmanifest_730.acf","\"buildid\" \"26334290\" \"StateFlags\" \"4\"");
        Write("game/csgo/steam.inf","PatchVersion=1.41.9.4\nClientVersion=2000939\n");
        var binaries=new[]{"game/csgo/bin/win64/server.dll","game/bin/win64/engine2.dll","game/csgo/bin/win64/client.dll","game/bin/win64/schemasystem.dll","game/bin/win64/cs2.exe"};
        foreach(var b in binaries)Write(b,"synthetic "+b);
        var files=new List<object>();
        foreach(var loader in DailyPlayFiles.BotLoaders) { Write(loader,"synthetic "+loader);files.Add(new {target=loader,sha256=Hash(Path.Combine(Root,loader))}); }
        var profiles=new Dictionary<string,object>();
        foreach(var level in Enum.GetValues<InstalledProfile>())
        {
            var target=$"game/csgo/overrides/localkit-profiles/{level}.vpk";
            Write(target,level==InstalledProfile.Medium?"synthetic "+DailyPlayFiles.ProfileTarget:level.ToString());
            var hash=Hash(Path.Combine(Root,target));profiles.Add(level.ToString(),new {target,sha256=hash});files.Add(new {target,sha256=hash});
        }
        Write("game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll","Human DLL");
        var human="game/csgo/addons/counterstrikesharp/configs/plugins/InventorySimulator/inventories.json";
        Write(human,"private Human fixture");
        var humanFiles=new Dictionary<string,string>{{human,Hash(Path.Combine(Root,human))},{"game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll",Hash(Path.Combine(Root,"game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll"))}};
        var manifest=new {schemaVersion=1,lane="bot-experiment",humanPolicy="preserve",testFixture=false,cssVersion="1.0.376",metamod=new {build=1481},botRef="e18c029a76b7515ea5cb15cb08c41f03de4c0a57",matchSetup=new {schemaVersion=1},
            targetBuild=new {buildId="26334290",ClientVersion="2000939",PatchVersion="1.41.9.4"},gameBinaries=binaries.ToDictionary(b=>b,b=>Hash(Path.Combine(Root,b))),files,baselineFiles=new Dictionary<string,string>(),coexistence=new {humanFiles},profile=new {difficulty="Medium"},
            dailyPlay=new {schemaVersion=1,profiles,modeFiles=DailyPlayFiles.BotLoaders}};
        var backup=Path.Combine(Data,"backups/bot-experiment",Id);Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup,"candidate-manifest.json"),JsonSerializer.Serialize(manifest));
        var sha=Hash(Path.Combine(backup,"candidate-manifest.json"));
        var entries=files.Select(f=>JsonSerializer.SerializeToElement(f)).Select(e=>new {target=e.GetProperty("target").GetString()!,sha256=e.GetProperty("sha256").GetString()!}).Concat(humanFiles.Select(e=>new {target=e.Key,sha256=e.Value})).Select(e=>new {e.target,action="retained",before=new {kind="file",e.sha256},after=new {kind="file",e.sha256}}).ToArray();
        var moduleInventoryAfter=new Dictionary<string,Dictionary<string,object>>();
        foreach(var folder in new[]{"game/csgo/addons/counterstrikesharp/plugins","game/csgo/addons/metamod"})
            moduleInventoryAfter[folder]=Directory.GetFileSystemEntries(Path.Combine(Root,folder),"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(Path.Combine(Root,folder),p).Replace('\\','/'),p=>Directory.Exists(p)?(object)new {kind="directory"}:new {kind="file",sha256=Hash(p)});
        moduleInventoryAfter["valveServerPluginDeclarations"]=[];
        File.WriteAllText(Path.Combine(backup,"install-record.json"),JsonSerializer.Serialize(new {installId=Id,cs2Root=Root,state="installed",manifestSha256=sha,humanPolicy="preserve",entries,moduleInventoryAfter}));
        Write("game/csgo/.cs2-local-kit-bot-experiment.json",JsonSerializer.Serialize(new {installId=Id,backupRoot=Path.GetDirectoryName(backup),manifestSha256=sha,humanPolicy="preserve"}));
    }
    public void Dispose() => Directory.Delete(_home,true);
    private DailyPlaySettings Settings(InstalledProfile level,MatchPlayMode mode) => new(1,"daily",DateTimeOffset.UtcNow,false,MatchSetupPreset.Default() with {Profile=level,Mode=mode});

    [Fact] public async Task ProfilesAndModesUseSameInstallationAndPreserveHumanAndRecords()
    {
        var before=Directory.GetFiles(Path.Combine(Data,"backups"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
        var humanBefore=Directory.GetFiles(Path.Combine(Root,"game/csgo/addons/counterstrikesharp"),"*",SearchOption.AllDirectories).Where(p=>p.Contains("InventorySimulator")).ToDictionary(p=>p,File.ReadAllBytes);
        foreach(var mode in new[]{MatchPlayMode.EnhancedHuman,MatchPlayMode.NormalHuman,MatchPlayMode.EnhancedHuman})
            foreach(var level in new[]{InstalledProfile.High,InstalledProfile.Low,InstalledProfile.Medium})
            {
                Assert.Contains("已写入",await Service.ApplyDailyAsync(Settings(level,mode)));
                var observed=MatchRuntimeContract.Validate(Root,Data);
                Assert.Equal(Id,observed.InstallId);Assert.Equal(level,observed.Profile);Assert.Equal(mode,observed.Mode);
                Assert.Equal(mode==MatchPlayMode.EnhancedHuman,File.Exists(Path.Combine(Root,"game/csgo/cfg/my_bot_normal_config.cfg")));
            }
        Assert.All(before,p=>Assert.Equal(p.Value,File.ReadAllBytes(p.Key)));Assert.All(humanBefore,p=>Assert.Equal(p.Value,File.ReadAllBytes(p.Key)));
    }
    [Fact] public async Task RunningGameDefersWithoutWritingAndClosedGameApplies()
    {
        var path=Path.Combine(Root,DailyPlayFiles.ProfileTarget);var before=File.ReadAllBytes(path);_running=true;
        Assert.Contains("待关闭",await Service.ApplyDailyAsync(Settings(InstalledProfile.High,MatchPlayMode.NormalHuman)));
        Assert.Equal(before,File.ReadAllBytes(path));_running=false;
        Assert.Contains("已写入",await Service.ApplyDailyAsync(Settings(InstalledProfile.High,MatchPlayMode.NormalHuman)));
    }
    [Theory] [InlineData("6","26334290")] [InlineData("4","99999999")]
    public async Task SteamUpdateOrDifferentBuildBlocksDailyWrites(string flags,string build)
    {
        var path=Path.Combine(Root,DailyPlayFiles.ProfileTarget);var before=File.ReadAllBytes(path);
        Write("../../appmanifest_730.acf",$"\"buildid\" \"{build}\" \"StateFlags\" \"{flags}\"");
        await Assert.ThrowsAnyAsync<Exception>(()=>Service.ApplyDailyAsync(Settings(InstalledProfile.High,MatchPlayMode.NormalHuman)));
        Assert.Equal(before,File.ReadAllBytes(path));
    }
    [Theory] [InlineData("game/csgo/overrides/botprofile.vpk")] [InlineData("game/csgo/addons/counterstrikesharp/plugins/BotAI/BotAI.dll")] [InlineData("game/bin/win64/engine2.dll")] [InlineData("game/csgo/addons/counterstrikesharp/plugins/Alien.dll")]
    public async Task UnknownImagesNeverGetOverwritten(string relative)
    {
        Write(relative,"unknown user bytes");
        await Assert.ThrowsAnyAsync<Exception>(()=>Service.ApplyDailyAsync(Settings(InstalledProfile.High,MatchPlayMode.NormalHuman)));
        Assert.Equal("unknown user bytes",File.ReadAllText(Path.Combine(Root,relative)));
    }
    [Fact] public async Task InterruptedModeConvergesOnlyThroughValidatedOwnedImages()
    {
        var path=Path.Combine(Root,DailyPlayFiles.BotLoaders[0]);File.Move(path,path+DailyPlayFiles.DisabledSuffix);
        Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,Data));
        Assert.Contains("已写入",await Service.ApplyDailyAsync(Settings(InstalledProfile.Low,MatchPlayMode.NormalHuman)));
        Assert.Equal(MatchPlayMode.NormalHuman,MatchRuntimeContract.Validate(Root,Data).Mode);
    }
    [Fact] public async Task AmbiguousLoaderPositionsNeverOverwriteEitherImage()
    {
        var path=Path.Combine(Root,DailyPlayFiles.BotLoaders[0]);var before=File.ReadAllBytes(path);
        File.Copy(path,path+DailyPlayFiles.DisabledSuffix);
        await Assert.ThrowsAsync<InvalidDataException>(()=>Service.ApplyDailyAsync(Settings(InstalledProfile.High,MatchPlayMode.NormalHuman)));
        Assert.Equal(before,File.ReadAllBytes(path));Assert.Equal(before,File.ReadAllBytes(path+DailyPlayFiles.DisabledSuffix));
    }
    [Fact] public async Task HumanSidecarRemainsValidAcrossBothModesAndProfileChanges()
    {
        var sidecar=new BotHumanFixtureSidecar(Root,Path.Combine(Data,"backups/bot-experiment"),Id);
        var bytes=System.Text.Encoding.UTF8.GetBytes("new private Human preset");sidecar.Begin(bytes);
        File.WriteAllBytes(Path.Combine(Root,BotHumanFixtureSidecar.Target),bytes);sidecar.Complete();
        foreach(var mode in new[]{MatchPlayMode.NormalHuman,MatchPlayMode.EnhancedHuman})
        {
            await Service.ApplyDailyAsync(Settings(InstalledProfile.High,mode));
            Assert.NotNull(sidecar.Inspect());Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(Root,BotHumanFixtureSidecar.Target)));
        }
        sidecar.SuspendForExactRestore();Assert.NotNull(sidecar.Inspect(allowSuspended:true));sidecar.AbortSuspendedRestore();
        Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(Root,BotHumanFixtureSidecar.Target)));
    }
    [Fact] public void IndependentCapabilitiesApplyWithoutAnyRosterOrMapRequest()
    {
        foreach(var map in new[]{"de_dust2","de_mirage","de_dust2"})
        {
            string mode="mixed";long epoch=0;
            var capability=Tuple.Create<Action<string>,Func<string>>(v=>{mode=v;epoch++;},()=>JsonSerializer.Serialize(new {Mode=mode,Epoch=epoch,Count=0}));
            var behavior=new DailyMapBehavior(AimSetting.Head,NadeSetting.Max);
            behavior.Aim.Apply(capability,false);behavior.Nades.Apply(null,true);
            Assert.Equal("head",mode);Assert.Contains("读回通过",behavior.Aim.State);Assert.Contains("模块缺失",behavior.Nades.State);
            behavior.Aim.Apply(capability,true);Assert.Equal(1,epoch);
            epoch++;behavior.Aim.Observe(capability);Assert.Contains("无法确认",behavior.Aim.State);Assert.Null(behavior.Aim.Readback);
        }
    }
    [Theory] [InlineData("write")] [InlineData("read")] [InlineData("mode")] [InlineData("epoch")]
    public void FirstCapabilityFailureRetriesIndependentlyAndStopsOnSuccess(string failure)
    {
        int aimCalls=0,nadeCalls=0;bool first=true;
        var aim=Tuple.Create<Action<string>,Func<string>>(_=> { aimCalls++;if(first&&failure=="write")throw new IOException("not ready"); },
            ()=>first?failure switch { "read"=>throw new IOException("not ready"),"mode"=>"{\"Mode\":\"body\",\"Epoch\":1}","epoch"=>"{\"Mode\":\"head\",\"Epoch\":0}",_=>"{}" }:"{\"Mode\":\"head\",\"Epoch\":2}");
        var nades=Tuple.Create<Action<string>,Func<string>>(_=>nadeCalls++,()=>"{\"Mode\":\"off\",\"Epoch\":1}");
        var behavior=new DailyMapBehavior(AimSetting.Head,NadeSetting.Off);
        behavior.Aim.Apply(aim,false);behavior.Nades.Apply(nades,false);
        Assert.False(behavior.Aim.Finished);Assert.Null(behavior.Aim.Readback);Assert.Contains("等待重试",behavior.Aim.State);Assert.True(behavior.Nades.Finished);
        first=false;behavior.Aim.Apply(aim,false);behavior.Nades.Apply(nades,false);
        Assert.True(behavior.Finished);Assert.Contains("读回通过",behavior.Aim.State);
        behavior.Aim.Apply(aim,true);Assert.Equal(2,aimCalls);Assert.Equal(1,nadeCalls);
    }
    [Fact] public void PersistentCapabilityFailureStopsAtTheEighthMapAttempt()
    {
        int calls=0,reads=0;
        var capability=Tuple.Create<Action<string>,Func<string>>(_=>calls++,()=>{reads++;return "{\"Mode\":\"body\",\"Epoch\":1}";});
        var setting=new DailyMapBehavior.Setting("head");
        for(int i=1;i<=8;i++) { setting.Apply(capability,i==8);Assert.Equal(i==8,setting.Finished); }
        Assert.Contains("窗口已用尽",setting.State);Assert.Null(setting.Readback);
        setting.Apply(capability,true);setting.Observe(capability);Assert.Equal(8,calls);Assert.Equal(8,reads);
    }
    private MatchSetupViewModel ViewModel() => new(new MatchSetupStore(Path.Combine(Data,"app-data/match-setup"),RosterCatalog.LoadBuiltIn()),RosterCatalog.LoadBuiltIn(),new MockDialogService(),Service);
    [Fact] public async Task PollRetriesTemporaryIoFailureAfterTheGateIsReleased()
    {
        var vm=ViewModel();vm.Profile=InstalledProfile.High;
        using(BotHumanOperationGate.Acquire(Root)) { await vm.PollDailyAsync();Assert.Contains("有限自动重试",vm.PreparationText); }
        await vm.PollDailyAsync();Assert.Contains("已写入",vm.PreparationText);Assert.Equal(InstalledProfile.High,MatchRuntimeContract.Validate(Root,Data).Profile);
        using(BotHumanOperationGate.Acquire(Root)) { await vm.PollDailyAsync();Assert.Contains("已写入",vm.PreparationText); }
    }
    [Fact] public async Task PollStopsAfterThreeIoFailuresAndExplicitCheckStartsANewAttempt()
    {
        var vm=ViewModel();vm.Profile=InstalledProfile.High;
        using(BotHumanOperationGate.Acquire(Root))
        {
            for(int i=0;i<3;i++)await vm.PollDailyAsync();Assert.Contains("已停止",vm.PreparationText);
        }
        for(int i=0;i<4;i++)await vm.PollDailyAsync();Assert.Equal(InstalledProfile.Medium,MatchRuntimeContract.Validate(Root,Data).Profile);
        await vm.CheckStatusAsync();Assert.Contains("已写入",vm.PreparationText);Assert.Equal(InstalledProfile.High,MatchRuntimeContract.Validate(Root,Data).Profile);
    }
    [Fact] public async Task PollDoesNotAutomaticallyRepeatPermanentFailureButExplicitCheckRevalidates()
    {
        var vm=ViewModel();vm.Profile=InstalledProfile.High;var path=Path.Combine(Root,DailyPlayFiles.BotLoaders[0]);var original=File.ReadAllBytes(path);File.WriteAllText(path,"unknown bytes");
        await vm.PollDailyAsync();Assert.Contains("已停止",vm.PreparationText);Assert.Contains("漂移",vm.PreparationText);
        File.WriteAllBytes(path,original);await vm.PollDailyAsync();Assert.Equal(InstalledProfile.Medium,MatchRuntimeContract.Validate(Root,Data).Profile);
        await vm.CheckStatusAsync();Assert.Equal(InstalledProfile.High,MatchRuntimeContract.Validate(Root,Data).Profile);
    }
}
