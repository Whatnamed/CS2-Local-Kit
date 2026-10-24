using System.Text.Json;
using System.Text.Json.Nodes;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.App.Services;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class RuntimeIntegrationTests : IDisposable
{
    private readonly string _home=Path.Combine(Path.GetTempPath(),"runtime-integration-"+Guid.NewGuid().ToString("N"));
    private string Root=>Path.GetFullPath(Path.Combine(_home,"steamapps/common/cs2"));
    private string Backups=>Path.Combine(_home,"backups/bot-experiment");
    private readonly string _id=Guid.NewGuid().ToString("N");
    private string Backup=>Path.Combine(Backups,_id);
    private JsonObject _manifest=null!;
    private readonly Dictionary<string,string> _parentFolders=new();
    private static void Put(string p,string value){Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,value);}
    private static string Hash(string p)=>RuntimeIntegrationGuard.Hash(p);
    private static JsonElement Element(object value)=>JsonSerializer.SerializeToElement(value);
    public RuntimeIntegrationTests()
    {
        var parents=new Dictionary<string,object>();
        foreach(var name in new[]{"bot","compat","nades"})
        {
            var id=Guid.NewGuid().ToString("N");var folder=name=="bot"?Path.Combine(Backups,id):Path.Combine(_home,"parents",name,id);_parentFolders[name]=folder;
            var marker=name=="bot"?"game/csgo/.cs2-local-kit-bot-experiment.json":"game/csgo/."+name+".json";
            var record="install-record.json";var manifest="candidate-manifest.json";
            Put(Path.Combine(folder,manifest),"{\"synthetic\":true}");
            Put(Path.Combine(folder,record),JsonSerializer.Serialize(new {installId=id,cs2Root=Root,state="installed",humanPolicy="preserve",manifestSha256=Hash(Path.Combine(folder,manifest))}));
            Put(Path.Combine(folder,"preimages/sentinel"),"original backup");
            Put(Path.Combine(Root,marker),JsonSerializer.Serialize(new {installId=id,manifestSha256=Hash(Path.Combine(folder,manifest)),backupRoot=Backups,humanPolicy="preserve"}));
            var inventory=new Dictionary<string,object>{[manifest]=new{kind="file",sha256=Hash(Path.Combine(folder,manifest))},[record]=new{kind="file",sha256=Hash(Path.Combine(folder,record))},["preimages"]=new{kind="directory"},["preimages/sentinel"]=new{kind="file",sha256=Hash(Path.Combine(folder,"preimages/sentinel"))}};
            parents[name]=new {installId=id,backupDir=folder,recordName=record,manifestName=manifest,marker,markerIdentity=new{kind="file",sha256=Hash(Path.Combine(Root,marker))},inventory};
        }
        Put(Path.Combine(Root,BotHumanFixtureSidecar.Target),"original private fixture");
        Put(Path.Combine(Root,"game/csgo/steam.inf"),"PatchVersion=1.41.9.2\nClientVersion=2000935\n");
        Put(Path.Combine(_home,"steamapps/appmanifest_730.acf"),"\"StateFlags\" \"4\" \"buildid\" \"26144732\"");
        var baseline=new Dictionary<string,string>{[BotHumanFixtureSidecar.Target]=Hash(Path.Combine(Root,BotHumanFixtureSidecar.Target))};
        _manifest=JsonSerializer.SerializeToNode(new {schemaVersion=1,humanPolicy="preserve",targetBuild=new {PatchVersion="1.41.9.2",ClientVersion="2000935",buildId="26144732"},baselineFiles=baseline,coexistence=new{humanFiles=baseline},runtimeIntegration=new{schemaVersion=1,cs2Root=Root,parents}})!.AsObject();
        SaveActive();
    }
    private void SaveActive()
    {
        var manifest=Path.Combine(Backup,"candidate-manifest.json");Put(manifest,_manifest.ToJsonString());
        Put(Path.Combine(Root,RuntimeIntegrationGuard.Marker),JsonSerializer.Serialize(new {installId=_id,manifestSha256=Hash(manifest),backupRoot=Backups,humanPolicy="preserve"}));
        var fixture=Path.Combine(Root,BotHumanFixtureSidecar.Target);var marker=Path.Combine(Root,RuntimeIntegrationGuard.Marker);
        Put(Path.Combine(Backup,"install-record.json"),JsonSerializer.Serialize(new {installId=_id,cs2Root=Root,state="installed",profile=new{difficulty="Medium"},humanPolicy="preserve",manifestSha256=Hash(manifest),createdDirectories=Array.Empty<string>(),entries=new object[]{
            new{target=BotHumanFixtureSidecar.Target,action="retained",before=new{kind="file",sha256=Hash(fixture)},after=new{kind="file",sha256=Hash(fixture)}},
            new{target=RuntimeIntegrationGuard.Marker,action="created",before=(object?)null,after=new{kind="file",sha256=Hash(marker)}}}}));
    }
    private JsonElement Manifest=>JsonSerializer.SerializeToElement(_manifest);
    public void Dispose()=>Directory.Delete(_home,true);
    [Fact]public void OverlayIdentityTakesPrecedenceOverProtectedParent()
    {
        var identity=BotExperimentIdentity.Inspect(Root,Backups);Assert.Equal(_id,identity.InstallId);Assert.False(identity.RecoveryRequired);
        RuntimeIntegrationGuard.VerifyParents(Root,Manifest);
    }
    [Fact]public void MissingOverlayMarkerDoesNotExposeApparentlyHealthyParent()
    {
        File.Delete(Path.Combine(Root,RuntimeIntegrationGuard.Marker));
        var identity=BotExperimentIdentity.Inspect(Root,Backups);Assert.Equal(_id,identity.InstallId);Assert.True(identity.RecoveryRequired);
    }
    [Theory][InlineData("preimages/sentinel")][InlineData("install-record.json")]
    public void ParentDriftBlocksBothIntegrationAndHumanUpdates(string relative)
    {
        Put(Path.Combine(_parentFolders["compat"],relative),"changed");
        Assert.Throws<InvalidDataException>(()=>RuntimeIntegrationGuard.VerifyParents(Root,Manifest));
        Assert.Throws<InvalidDataException>(()=>new BotHumanFixtureSidecar(Root,Backups,_id).Begin("new private fixture"u8.ToArray()));
        Assert.Equal("original private fixture",File.ReadAllText(Path.Combine(Root,BotHumanFixtureSidecar.Target)));
    }
    [Fact]public void HumanUpdateRestoresToPlayableSubstrateAndCanPrepareAgain()
    {
        var fixture=Path.Combine(Root,BotHumanFixtureSidecar.Target);var sidecar=new BotHumanFixtureSidecar(Root,Backups,_id);
        sidecar.Begin("edited private fixture"u8.ToArray());File.WriteAllText(fixture,"edited private fixture");sidecar.Complete();
        sidecar.SuspendForExactRestore();
        var record=Path.Combine(Backup,"install-record.json");Put(record,File.ReadAllText(record).Replace("\"installed\"","\"restored\""));File.Delete(Path.Combine(Root,RuntimeIntegrationGuard.Marker));
        sidecar.FinishExactRestore();Assert.Equal("edited private fixture",File.ReadAllText(fixture));
        Assert.Equal(_id,RuntimeIntegrationGuard.RestoredHumanHandoff(Root,Backups,Manifest));
        sidecar.PrepareRestoredTransfer();Assert.Equal("original private fixture",File.ReadAllText(fixture));
        sidecar.ReplayAfterFailedSwitch();Assert.Equal("edited private fixture",File.ReadAllText(fixture));
    }
    [Fact]public async Task UnifiedPrepareNeverRestoresProtectedParents()
    {
        var record=Path.Combine(Backup,"install-record.json");Put(record,File.ReadAllText(record).Replace("\"installed\"","\"restored\""));File.Delete(Path.Combine(Root,RuntimeIntegrationGuard.Marker));
        var package=Path.Combine(_home,"releases/bot-experiment/integrated-medium");
        Put(Path.Combine(package,"tools/Invoke-BotExperiment.ps1"),"# synthetic never executed");Put(Path.Combine(package,"tools/bot_experiment.py"),"# synthetic never executed");
        _manifest["lane"]="bot-experiment";_manifest["testFixture"]=false;_manifest["botRef"]="e18c029a76b7515ea5cb15cb08c41f03de4c0a57";
        _manifest["cssVersion"]="1.0.376";_manifest["metamod"]=JsonSerializer.SerializeToNode(new{build=1481});
        _manifest["profile"]=JsonSerializer.SerializeToNode(new{difficulty="Medium"});_manifest["files"]=new JsonArray();
        _manifest["matchSetup"]=JsonSerializer.SerializeToNode(new{schemaVersion=1,adapter="LocalKitMatchSetup"});
        _manifest["coexistence"]!["status"]="ready-for-manual-test";
        _manifest["bundleFiles"]=JsonSerializer.SerializeToNode(Directory.GetFiles(package,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(package,p).Replace('\\','/'),Hash));
        Put(Path.Combine(package,"experiment-manifest.json"),_manifest.ToJsonString());
        var candidate=new BotCandidate(BotDifficulty.Medium,"integrated-medium",Hash(Path.Combine(package,"experiment-manifest.json")),true,true,true);
        var runner=new Runner((action,apply)=>
        {
            Assert.NotEqual("Restore",action);
            if(action=="Install"&&apply){SaveActive();return JsonSerializer.Serialize(new{state="installed",installId=_id});}
            return "{\"state\":\"preview\",\"entries\":[]}";
        });
        var service=new BotExperimentService(_home,Root,()=>false,runner,[candidate]);
        var plan=await service.PreviewSwitchAsync(BotDifficulty.Medium);Assert.True(plan.ProtectsExistingLayers);
        var journal=await service.ApplySwitchAsync(plan);Assert.Equal("completed",journal.Stage);Assert.Equal(_id,journal.NewInstallId);
        Assert.Contains(runner.Actions,a=>a=="Install");
        using var parent=JsonDocument.Parse(File.ReadAllText(Path.Combine(_parentFolders["bot"],"install-record.json")));
        Assert.Equal("installed",parent.RootElement.GetProperty("state").GetString());
    }
    private sealed class Runner(Func<string,bool,string> result):IBotProcessRunner
    {
        public List<string> Actions{get;}=[];
        public Task<BotProcessResult> RunAsync(string executable,IReadOnlyList<string> args)
        {
            if(executable=="python")return Task.FromResult(new BotProcessResult(0,"Python 3.12.10",""));
            var action=args[args.ToList().IndexOf("-Action")+1];Actions.Add(action);
            return Task.FromResult(new BotProcessResult(0,result(action,args.Contains("-Apply")),""));
        }
    }
}
