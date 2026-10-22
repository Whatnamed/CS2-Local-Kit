using System.Security.Cryptography;
using System.Text.Json;
using CS2LocalKit.Core.Runtime;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class BotHumanFixtureSidecarTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "human-sidecar-" + Guid.NewGuid().ToString("N"));
    private readonly string _id = Guid.NewGuid().ToString("N");
    private string Root => Path.Combine(_home, "steamapps", "common", "cs2");
    private string Backups => Path.Combine(_home, "backups", "bot-experiment");
    private string Backup => Path.Combine(Backups, _id);
    private string Fixture => Path.Combine(Root, BotHumanFixtureSidecar.Target.Replace('/', Path.DirectorySeparatorChar));
    private string Other => Path.Combine(Root, "game/csgo/addons/metamod/bin/win64/server.dll");
    private string Marker => Path.Combine(Root, "game/csgo/.cs2-local-kit-bot-experiment.json");
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Put(string path, string data) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, data); }
    public BotHumanFixtureSidecarTests()
    {
        Put(Fixture, "private original fixture");
        Put(Other, "joint loader");
        Put(Path.Combine(Root, "game/csgo/steam.inf"), "PatchVersion=1.41.9.1\nClientVersion=2000934\n");
        Put(Path.Combine(_home, "steamapps/appmanifest_730.acf"), "\"StateFlags\" \"4\" \"buildid\" \"26087496\"");
        Directory.CreateDirectory(Backup);
        var manifest = new { humanPolicy = "preserve", targetBuild = new { PatchVersion = "1.41.9.1", ClientVersion = "2000934", buildId = "26087496" }, coexistence = new { humanFiles = new Dictionary<string,string>
            { [BotHumanFixtureSidecar.Target] = Hash(Fixture) } } };
        var manifestPath = Path.Combine(Backup, "candidate-manifest.json");
        Put(manifestPath, JsonSerializer.Serialize(manifest));
        Put(Marker, JsonSerializer.Serialize(new { installId = _id, backupRoot = Backups, manifestSha256 = Hash(manifestPath), humanPolicy = "preserve" }));
        Put(Path.Combine(Backup, "preimages", "game/csgo/addons/metamod/bin/win64/server.dll"), "pure loader");
        var entries = new object[]
        {
            new { target = BotHumanFixtureSidecar.Target, action = "retained", before = new { kind = "file", sha256 = Hash(Fixture) }, after = new { kind = "file", sha256 = Hash(Fixture) } },
            new { target = "game/csgo/addons/metamod/bin/win64/server.dll", action = "overwritten", before = new { kind = "file", sha256 = Hash(Path.Combine(Backup, "preimages", "game/csgo/addons/metamod/bin/win64/server.dll")) }, after = new { kind = "file", sha256 = Hash(Other) } },
            new { target = "game/csgo/.cs2-local-kit-bot-experiment.json", action = "created", before = (object?)null, after = new { kind = "file", sha256 = Hash(Marker) } }
        };
        Put(Path.Combine(Backup, "install-record.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, lane = "bot-experiment", installId = _id, cs2Root = Root,
            manifestSha256 = Hash(manifestPath), state = "installed", humanPolicy = "preserve",
            entries, createdDirectories = Array.Empty<string>()
        }));
    }
    public void Dispose() { if (Directory.Exists(_home)) Directory.Delete(_home, true); }
    private BotHumanFixtureSidecar Sidecar() => new(Root, Backups, _id);

    [Fact] public void UpdateAndExactRestorePreserveOriginalRecordAndLatestHumanFixture()
    {
        var originalRecord = File.ReadAllBytes(Path.Combine(Backup, "install-record.json"));
        var sidecar = Sidecar(); Assert.Null(sidecar.Inspect());
        var updated = System.Text.Encoding.UTF8.GetBytes("private edited fixture");
        sidecar.Begin(updated);
        File.WriteAllBytes(Fixture, updated);
        sidecar.Complete();
        Assert.Equal(Hash(Fixture), sidecar.Inspect()!.CurrentSha256);
        Assert.True(sidecar.PreviewExactRestore() >= 3);
        Assert.Equal(originalRecord, File.ReadAllBytes(Path.Combine(Backup, "install-record.json")));
        sidecar.SuspendForExactRestore();
        Assert.Equal("private original fixture", File.ReadAllText(Fixture));
        sidecar.AbortSuspendedRestore();
        Assert.Equal("private edited fixture", File.ReadAllText(Fixture));
        sidecar.SuspendForExactRestore();
        var path = Path.Combine(Backup, "install-record.json");
        Put(path, File.ReadAllText(path).Replace("\"installed\"", "\"restored\""));
        File.Delete(Marker);
        sidecar.FinishExactRestore();
        Assert.Equal("private edited fixture", File.ReadAllText(Fixture));
    }

    [Fact] public void ExternalDriftDoesNotGetWhitelistedByTheSidecar()
    {
        var sidecar = Sidecar(); sidecar.Begin(System.Text.Encoding.UTF8.GetBytes("new"));
        Put(Fixture, "new"); sidecar.Complete();
        Put(Other, "unknown loader");
        Assert.Throws<InvalidDataException>(() => sidecar.Inspect());
        Assert.Equal("new", File.ReadAllText(Fixture));
    }
    [Fact] public void InterruptedUpdateKeepsTheOriginalHumanImageAndRecord()
    {
        var sidecar = Sidecar();
        var record = File.ReadAllBytes(Path.Combine(Backup, "install-record.json"));
        sidecar.Begin(System.Text.Encoding.UTF8.GetBytes("unfinished update"));
        Put(Fixture, "unfinished update");
        Assert.Throws<InvalidDataException>(() => sidecar.Inspect());
        sidecar.Abort();
        Assert.Equal("private original fixture", File.ReadAllText(Fixture));
        Assert.Equal(record, File.ReadAllBytes(Path.Combine(Backup, "install-record.json")));
    }
    [Fact] public void GameWriteGateSerializesControllerOperations()
    {
        using (BotHumanOperationGate.Acquire(Root))
            Assert.Throws<IOException>(() => BotHumanOperationGate.Acquire(Root));
        using var after = BotHumanOperationGate.Acquire(Root);
    }
}
