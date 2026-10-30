using System.Text.Json;
using System.Text.Json.Nodes;
using CS2LocalKit.Core.Runtime;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public class BuildCompatibility14193Tests
{
    private static JsonDocument Review() => JsonDocument.Parse(typeof(RuntimeIntegrationGuard).Assembly.GetManifestResourceStream("CS2LocalKit.Review14193.json")!);
    [Fact]
    public void ReviewedViewKeepsFrozenSourceAndChecksExactBinaryIdentity()
    {
        using var review=Review();var r=review.RootElement;
        var files=r.GetProperty("retainedFiles").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetProperty("before").GetString());
        var raw=JsonSerializer.Serialize(new{targetBuild=r.GetProperty("previousBuild"),baselineFiles=files,coexistence=new{reviewedBuild=r.GetProperty("previousBuild")}});
        var digest=r.GetProperty("sourceManifests")[1].GetString()!;
        var steam=new SteamInstallState(true,"idle","26281801","1.41.9.3","2000938");
        string Hash(string p)=>r.GetProperty("gameBinaries").GetProperty(p).GetString()!;
        using var m=RuntimeIntegrationGuard.Reviewed14193(raw,digest,steam,r,Hash);
        Assert.Equal("26281801",m.RootElement.GetProperty("targetBuild").GetProperty("buildId").GetString());
        using var original=JsonDocument.Parse(raw);
        Assert.Equal("26144732",original.RootElement.GetProperty("targetBuild").GetProperty("buildId").GetString());
        foreach(var p in r.GetProperty("retainedFiles").EnumerateObject())
        {
            using var entry=JsonDocument.Parse(JsonSerializer.Serialize(new{target=p.Name,action="retained",before=new{kind="file",sha256=p.Value.GetProperty("before").GetString()},after=new{kind="file",sha256=p.Value.GetProperty("before").GetString()}}));
            Assert.Equal(p.Value.GetProperty("after").GetString(),RuntimeIntegrationGuard.ReviewedPostimage(m.RootElement,entry.RootElement));
            var bad=JsonNode.Parse(entry.RootElement.GetRawText())!;bad["before"]!["sha256"]="changed";
            using var badEntry=JsonDocument.Parse(bad.ToJsonString());
            Assert.Throws<InvalidDataException>(()=>RuntimeIntegrationGuard.ReviewedPostimage(m.RootElement,badEntry.RootElement));
        }
        Assert.Throws<InvalidDataException>(()=>RuntimeIntegrationGuard.Reviewed14193(raw,digest,steam,r,_=>"unknown"));
        Assert.Throws<InvalidDataException>(()=>RuntimeIntegrationGuard.Reviewed14193(raw,digest,steam with{Idle=false},r,Hash));
        using var unknown=RuntimeIntegrationGuard.Reviewed14193(raw,"unlisted",steam,r,Hash);
        Assert.False(unknown.RootElement.TryGetProperty("reviewedRetainedFiles",out _));
        using var future=RuntimeIntegrationGuard.Reviewed14193(raw,digest,steam with{PatchVersion="1.41.9.4"},r,Hash);
        Assert.False(future.RootElement.TryGetProperty("reviewedRetainedFiles",out _));
    }
}
