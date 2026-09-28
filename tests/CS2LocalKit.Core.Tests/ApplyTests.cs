using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Projection;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class ApplyTests : IDisposable
{
    private readonly string _work;
    private readonly string _csgoDir;
    private readonly string _installedPath;
    private readonly string _backupsRoot;
    private const string OldFixture = "{ \"old\": true }\r\n";

    public ApplyTests()
    {
        _work = Path.Combine(Path.GetTempPath(), "cs2localkit-applytests-" + Guid.NewGuid().ToString("N"));
        _csgoDir = Path.Combine(_work, "game", "csgo");
        _installedPath = Path.Combine(_csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");
        _backupsRoot = Path.Combine(_work, "backups");
        Directory.CreateDirectory(Path.GetDirectoryName(_installedPath)!);
        File.WriteAllText(_installedPath, OldFixture);
    }

    public void Dispose()
    {
        if (Directory.Exists(_work)) Directory.Delete(_work, recursive: true);
    }

    private FixtureApplier Applier() => new(new FixtureApplierOptions
    {
        CsgoDir = _csgoDir,
        BackupsRoot = _backupsRoot,
        SkipCs2ProcessCheck = true,
    });

    [Fact]
    public void Apply_BackupReplaceVerify_AndRestoreLatest()
    {
        var projected = new InventorySimulatorProjector().Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        var record = Applier().Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64, "example.v1.json");

        Assert.Equal(projected, File.ReadAllText(_installedPath));
        Assert.Equal(ApplyRecordSha(OldFixture), record.PreviousSha256);
        Assert.Equal(ApplyRecordSha(projected), record.NewSha256);
        Assert.True(File.Exists(record.BackupPath));
        Assert.Equal(OldFixture, File.ReadAllText(record.BackupPath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(record.BackupPath)!, "apply-record.json")));

        // restore-latest must return the fixture to the pre-apply state, byte-for-byte.
        var restored = Applier().RestoreLatest();
        Assert.Equal(ApplyRecordSha(OldFixture), restored.PreviousSha256);
        Assert.Equal(OldFixture, File.ReadAllText(_installedPath));
    }

    [Fact]
    public void Apply_FailureRollback_RestoresPreviousState()
    {
        // Drive ReplaceVerified with a deliberately wrong expected hash: the post-replace
        // verification must fail and auto-rollback must restore the previous fixture.
        var projectedPath = Path.Combine(_work, "staged.json");
        File.WriteAllText(projectedPath, "{ \"corrupted\": true }\r\n");

        var ex = Assert.Throws<ApplyException>(() =>
            Applier().ReplaceVerified(projectedPath, "0" + new string('0', 63), _installedPath,
                BackupOne(), ApplyRecordSha(OldFixture)));

        Assert.Contains("auto-rolled back", ex.Message);
        Assert.Equal(OldFixture, File.ReadAllText(_installedPath));
        Assert.False(File.Exists(_installedPath + ".new"));
    }

    [Fact]
    public void Apply_MissingInstalledFixture_FailsWithoutSideEffects()
    {
        File.Delete(_installedPath);
        Assert.Throws<ApplyException>(() => Applier().Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64));
        Assert.Empty(Directory.Exists(_backupsRoot) ? Directory.EnumerateDirectories(_backupsRoot) : []);
    }

    private string BackupOne()
    {
        var dir = Path.Combine(_backupsRoot, "20260101-000000-preset-apply");
        Directory.CreateDirectory(dir);
        File.Copy(_installedPath, Path.Combine(dir, "inventories.json"), overwrite: true);
        return Path.Combine(dir, "inventories.json");
    }

    private static string ApplyRecordSha(string text)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms)).ToLowerInvariant();
    }
}
