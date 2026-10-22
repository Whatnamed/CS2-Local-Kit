using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CS2LocalKit.Core.MatchSetup;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class PreMatchSessionTests
{
    private static PreMatchSession Session(MatchSide side=MatchSide.CT,int friendly=4,int enemy=5)
    {
        var c=RosterCatalog.LoadBuiltIn();var p=MatchSetupPreset.Default() with {HumanSide=side,FriendlyBots=friendly,EnemyBots=enemy};
        return new(new(1,"request",DateTimeOffset.UtcNow,MatchSetupStore.Digest(p),p),c.Resolve(p));
    }
    private static PreMatchObservation Ready(MatchSide side=MatchSide.CT,int ct=0,int t=0)=>new(true,true,1,side,ct,t,true);
    [Theory] [InlineData(MatchSide.CT)] [InlineData(MatchSide.T)]
    public void PhaseOrderIsExplicitAndEveryMutationIsOneShot(MatchSide side)
    {
        var s=Session(side);var o=Ready(side);
        Assert.Equal(PreMatchStep.ClearDefaultBots,s.Advance(o));Assert.Equal(PreMatchStep.AddBots,s.Advance(o));
        var counts=side==MatchSide.CT?o with {CtBots=4,TBots=5}:o with {CtBots=5,TBots=4};
        Assert.Equal(PreMatchStep.StageIdentities,s.Advance(counts));
        Assert.Equal(PreMatchStep.Wait,s.Advance(counts));
        Assert.Equal(PreMatchStep.BindIdentities,s.Advance(counts with {StagedIdentitiesMatch=true}));
        for(int i=0;i<5;i++)Assert.Equal(PreMatchStep.Wait,s.Advance(counts));
        Assert.Equal(PreMatchStep.ApplyBehavior,s.Advance(counts with {IdentitiesMatch=true}));
        Assert.Equal(PreMatchStep.Wait,s.Advance(counts with {IdentitiesMatch=true}));
        Assert.Equal(PreMatchStep.Observe,s.Advance(counts with {IdentitiesMatch=true,ModesMatch=true}));
        for(int i=0;i<10;i++)Assert.Equal(PreMatchStep.Observe,s.Advance(counts with {SafeBeforeScore=false,HumanSide=side==MatchSide.CT?MatchSide.T:MatchSide.CT}));
    }
    [Fact] public void PluginsAndHumanSelectionGateAllMutation()
    {
        var s=Session();Assert.Equal(PreMatchStep.Wait,s.Advance(Ready() with {PluginsReady=false}));
        Assert.Equal(PreMatchStep.Wait,s.Advance(Ready() with {HumanCount=0}));
        Assert.Equal(PreMatchStep.Wait,s.Advance(Ready() with {HumanSide=null}));
        Assert.Equal(PreMatchPhase.Waiting,s.Phase);
    }
    [Fact] public void HumanSideIsAlignedOnlyOnceBeforeBots()
    {
        var s=Session();Assert.Equal(PreMatchStep.SelectHumanSide,s.Advance(Ready(MatchSide.T)));
        for(int i=0;i<5;i++)Assert.Equal(PreMatchStep.Wait,s.Advance(Ready(MatchSide.T)));
        Assert.Equal(PreMatchStep.ClearDefaultBots,s.Advance(Ready()));
    }
    [Fact] public void ClearingWaitsWithoutRepeatedKickAndWrongCountsNeverReAdd()
    {
        var s=Session();var existing=Ready(ct:5,t:5);Assert.Equal(PreMatchStep.ClearDefaultBots,s.Advance(existing));
        for(int i=0;i<5;i++)Assert.Equal(PreMatchStep.Wait,s.Advance(existing));
        Assert.Equal(PreMatchStep.AddBots,s.Advance(Ready()));
        for(int i=0;i<5;i++)Assert.Equal(PreMatchStep.Wait,s.Advance(Ready(ct:2,t:1)));
        Assert.Equal(PreMatchStep.Stop,s.Fail("timeout"));Assert.Equal(PreMatchStep.Stop,s.Advance(Ready()));
    }
    [Theory] [InlineData(false,1,true)] [InlineData(true,2,true)] [InlineData(true,1,false)]
    public void DriftAdditionalHumanAndScoringFailClosed(bool environment,int humans,bool beforeScore)
    {
        var s=Session();Assert.Equal(PreMatchStep.Stop,s.Advance(Ready() with {EnvironmentValid=environment,HumanCount=humans,SafeBeforeScore=beforeScore}));
        Assert.Equal(PreMatchStep.Stop,s.Advance(Ready()));
    }
    [Fact] public void FirstLiveRoundInterruptsPartialInitializationWithoutRecoveryKick()
    {
        var s=Session();s.Advance(Ready());s.Advance(Ready());Assert.Equal(PreMatchStep.Stop,s.Advance(Ready(ct:2,t:3) with {SafeBeforeScore=false}));
        Assert.Equal(PreMatchStep.Stop,s.Advance(Ready(ct:4,t:5)));
    }
    [Theory] [InlineData(0,0)] [InlineData(1,3)] [InlineData(4,5)]
    public void CountsMatchPlanWithoutExtraBots(int friendly,int enemy)
    {
        var s=Session(friendly:friendly,enemy:enemy);s.Advance(Ready());s.Advance(Ready());
        Assert.Equal(PreMatchStep.StageIdentities,s.Advance(Ready(ct:friendly,t:enemy)));
    }
    [Fact] public void IdentityDriftWhileWaitingForModesIsTerminal()
    {
        var s=Session();s.Advance(Ready());s.Advance(Ready());s.Advance(Ready(ct:4,t:5));s.Advance(Ready(ct:4,t:5) with {StagedIdentitiesMatch=true});s.Advance(Ready(ct:4,t:5) with {IdentitiesMatch=true});
        Assert.Equal(PreMatchStep.Stop,s.Advance(Ready(ct:4,t:5) with {ModesMatch=true}));
    }
    [Fact] public void ANewMapHasItsOwnOneShotSession()
    {
        var a=Session();a.Fail("score lock");var b=Session();Assert.Equal(PreMatchStep.ClearDefaultBots,b.Advance(Ready()));Assert.Equal(PreMatchPhase.Failed,a.Phase);
    }
}

public sealed class MatchRuntimeContractTests : IDisposable
{
    private readonly string _data=Path.Combine(Path.GetTempPath(),"match-contract-"+Guid.NewGuid().ToString("N"));
    private string Root=>Path.Combine(_data,"steamapps/common/cs2");private string Backup=>Path.Combine(_data,"backups/bot-experiment/id");
    private readonly Dictionary<string,object> _manifest;
    private string Write(string path,string content) { Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,content);return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))); }
    public MatchRuntimeContractTests()
    {
        Write(Path.Combine(_data,"steamapps/appmanifest_730.acf"),"\"AppState\" {\"StateFlags\" \"4\" \"buildid\" \"1\"}");
        Write(Path.Combine(Root,"game/csgo/steam.inf"),"PatchVersion=test\nClientVersion=1\n");
        var binary="game/csgo/bin/win64/server.dll";var payload="game/csgo/addons/counterstrikesharp/plugins/LocalKitMatchSetup/LocalKitMatchSetup.dll";var human="game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll";
        _manifest=new(){["matchSetup"]=new{schemaVersion=1},["botRef"]="e18c029a76b7515ea5cb15cb08c41f03de4c0a57",["targetBuild"]=new{buildId="1",ClientVersion="1",PatchVersion="test"},["gameBinaries"]=new Dictionary<string,string>{{binary,Write(Path.Combine(Root,binary),"binary")}},["files"]=new[]{new{target=payload,sha256=Write(Path.Combine(Root,payload),"adapter")}},["coexistence"]=new{humanFiles=new Dictionary<string,string>{{human,Write(Path.Combine(Root,human),"human")}}},["baselineFiles"]=new Dictionary<string,string>(),["profile"]=new{difficulty="Medium"}};
        Seal();
    }
    private void Seal()
    {
        _manifest["schemaVersion"]=1;_manifest["lane"]="bot-experiment";_manifest["humanPolicy"]="preserve";_manifest["testFixture"]=false;_manifest["cssVersion"]="1.0.376";_manifest["metamod"]=new{build=1481};
        var binaries=(Dictionary<string,string>)_manifest["gameBinaries"];
        foreach(var rel in new[]{"game/bin/win64/engine2.dll","game/csgo/bin/win64/client.dll","game/bin/win64/schemasystem.dll","game/bin/win64/cs2.exe"})binaries[rel]=Write(Path.Combine(Root,rel),"binary");
        var sha=Write(Path.Combine(Backup,"candidate-manifest.json"),JsonSerializer.Serialize(_manifest));
        Write(Path.Combine(Backup,"install-record.json"),JsonSerializer.Serialize(new{installId="id",cs2Root=Root,state="installed",manifestSha256=sha,humanPolicy="preserve"}));
        Write(Path.Combine(Root,"game/csgo/.cs2-local-kit-bot-experiment.json"),JsonSerializer.Serialize(new{installId="id",backupRoot=Path.Combine(_data,"backups/bot-experiment"),manifestSha256=sha}));
    }
    public void Dispose()=>Directory.Delete(_data,true);
    [Fact] public void ExactInstalledContractReadsWithoutWritingHumanOrGame()
    {
        var before=Directory.GetFiles(_data,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>File.ReadAllText(p));
        Assert.Equal(InstalledProfile.Medium,MatchRuntimeContract.Validate(Root,_data).Profile);
        Assert.All(before,p=>Assert.Equal(p.Value,File.ReadAllText(p.Key)));
    }
    [Theory] [InlineData("game/csgo/bin/win64/server.dll")] [InlineData("game/csgo/addons/counterstrikesharp/plugins/InventorySimulator/InventorySimulator.dll")] [InlineData("game/csgo/addons/counterstrikesharp/plugins/LocalKitMatchSetup/LocalKitMatchSetup.dll")]
    public void BinaryPayloadAndHumanDriftStop(string path) { Write(Path.Combine(Root,path),"drift");Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,_data)); }
    [Fact] public void GameUpdateAndMissingAdapterStop()
    {
        Write(Path.Combine(Root,"game/csgo/steam.inf"),"PatchVersion=updated\nClientVersion=2\n");Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,_data));
        _manifest.Remove("matchSetup");Seal();Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,_data));
    }
    [Fact] public void IncompleteInstallAndWrongManifestCannotApply()
    {
        var path=Path.Combine(Backup,"install-record.json");File.WriteAllText(path,File.ReadAllText(path).Replace("installed","installing"));Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,_data));
        Seal();File.AppendAllText(Path.Combine(Backup,"candidate-manifest.json")," ");Assert.Throws<InvalidDataException>(()=>MatchRuntimeContract.Validate(Root,_data));
    }
}
