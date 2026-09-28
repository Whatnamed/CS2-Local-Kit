using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.Catalog;
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

    private FixtureApplier Applier(bool? cs2Running = null) => new(new FixtureApplierOptions
    {
        CsgoDir = _csgoDir,
        BackupsRoot = _backupsRoot,
        SkipCs2ProcessCheck = cs2Running is null,
        Cs2RunningProbe = cs2Running is null ? null : () => cs2Running.Value,
        Catalog = CatalogIndex.Load(TestFixtures.CatalogDir),
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
    public void Apply_RefusesWhileCs2Running()
    {
        var ex = Assert.Throws<ApplyException>(() =>
            Applier(cs2Running: true).Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64));
        Assert.Contains("cs2.exe is running", ex.Message);
        Assert.Equal(OldFixture, File.ReadAllText(_installedPath));
        Assert.Empty(Directory.Exists(_backupsRoot) ? Directory.EnumerateDirectories(_backupsRoot) : []);
    }

    [Fact]
    public void RestoreLatest_RefusesWhileCs2Running()
    {
        Applier().Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        var appliedContent = File.ReadAllText(_installedPath);

        var ex = Assert.Throws<ApplyException>(() => Applier(cs2Running: true).RestoreLatest());
        Assert.Contains("cs2.exe is running", ex.Message);
        // The applied state must be untouched by the refused restore.
        Assert.Equal(appliedContent, File.ReadAllText(_installedPath));
    }

    [Fact]
    public void RestoreLatest_RefusesWhenCurrentFixtureDrifted()
    {
        Applier().Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        var drifted = "{ \"someone-else\": true }\r\n";
        File.WriteAllText(_installedPath, drifted);

        var ex = Assert.Throws<ApplyException>(() => Applier().RestoreLatest());
        // Fail closed: both hashes reported, current file byte-identical, no .new residue.
        Assert.Contains("no longer matches the latest apply record", ex.Message);
        Assert.Contains(ApplyRecordSha(drifted), ex.Message);
        Assert.Equal(drifted, File.ReadAllText(_installedPath));
        Assert.False(File.Exists(_installedPath + ".new"));
    }

    [Fact]
    public void Apply_MetadataWriteFailure_RestoresPreviousFixture()
    {
        var applier = new RecordWriteFailingApplier(new FixtureApplierOptions
        {
            CsgoDir = _csgoDir,
            BackupsRoot = _backupsRoot,
            SkipCs2ProcessCheck = true,
            Catalog = CatalogIndex.Load(TestFixtures.CatalogDir),
        });

        var ex = Assert.Throws<ApplyException>(() =>
            applier.Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64));
        Assert.Contains("rolled back to the previous state", ex.Message);
        // The caller saw a failure AND the installed fixture is the previous state.
        Assert.Equal(OldFixture, File.ReadAllText(_installedPath));
        Assert.False(File.Exists(_installedPath + ".new"));
        // The backup copy itself still exists for manual recovery; only the record write failed.
        var backupDir = Directory.EnumerateDirectories(_backupsRoot).Single();
        Assert.True(File.Exists(Path.Combine(backupDir, "inventories.json")));
        Assert.False(File.Exists(Path.Combine(backupDir, "apply-record.json")));
    }

    [Fact]
    public void Apply_RapidTwoApplies_CreateDistinctBackupRecords()
    {
        var applier = Applier();
        var projected = new InventorySimulatorProjector().Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);

        var record1 = applier.Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64, "example.v1.json");
        Thread.Sleep(20); // cross a system-clock tick: DateTime.Now granularity is ~15.6ms on Windows
        var record2 = applier.Apply(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64, "example.v1.json");

        Assert.NotEqual(record1.BackupPath, record2.BackupPath);
        Assert.True(Directory.EnumerateDirectories(_backupsRoot).Count() >= 2);
        Assert.Equal(OldFixture, File.ReadAllText(record1.BackupPath));
        Assert.Equal(projected, File.ReadAllText(record2.BackupPath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(record1.BackupPath)!, "apply-record.json")));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(record2.BackupPath)!, "apply-record.json")));

        // RestoreLatest always targets the newest applicable record and is idempotent:
        // restoring record2 leaves the fixture matching record2.NewSha256, so a second
        // call legitimately restores the same record again (no pop semantics).
        var restored2 = applier.RestoreLatest();
        Assert.Equal(record2.PreviousSha256, restored2.PreviousSha256);
        Assert.Equal(projected, File.ReadAllText(_installedPath));
        var again = applier.RestoreLatest();
        Assert.Equal(restored2.BackupPath, again.BackupPath);
        Assert.Equal(projected, File.ReadAllText(_installedPath));

        // Once the newest record no longer applies (here: removed), the previous
        // record becomes the latest applicable one and restores the pre-apply state.
        Directory.Delete(Path.GetDirectoryName(record2.BackupPath)!, recursive: true);
        var restored1 = applier.RestoreLatest();
        Assert.Equal(record1.PreviousSha256, restored1.PreviousSha256);
        Assert.Equal(OldFixture, File.ReadAllText(_installedPath));
    }

    [Fact]
    public void Apply_PresetWithWrongPaintMembership_FailsClosedWithoutTouchingFixture()
    {
        // Schema-valid (parses fine) but P250 (defindex 36) assigned paint 3, which
        // belongs to no P250 finish - the kind of mistake an in-memory C4 UI edit can
        // produce. Apply must reject it and leave the installed fixture byte-identical.
        var bad = TestFixtures.PersonalShapedPreset(paintOverride: 3);
        Assert.Empty(HumanPresetValidator.ValidateDomain(bad));         // schema/domain is fine

        var before = File.ReadAllText(_installedPath);
        var ex = Assert.Throws<HumanPresetValidationException>(() =>
            Applier().Apply(bad, TestFixtures.FakeSteamId64));

        Assert.Contains("no paint kit", ex.Message);
        Assert.Equal(before, File.ReadAllText(_installedPath));
        Assert.Empty(Directory.Exists(_backupsRoot) ? Directory.EnumerateDirectories(_backupsRoot) : []);
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

    private sealed class RecordWriteFailingApplier(FixtureApplierOptions options) : FixtureApplier(options)
    {
        internal override void WriteApplyRecord(string backupDir, ApplyRecord record)
            => throw new IOException("simulated apply-record.json write failure");
    }

    private static string ApplyRecordSha(string text)
    {
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ms)).ToLowerInvariant();
    }
}
