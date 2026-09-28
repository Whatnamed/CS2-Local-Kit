using System.Text.Json;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Projection;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.Core.Application;

public sealed class FixtureApplierOptions
{
    /// <summary>Overrides the csgo directory (fake-tree tests). Default: real install.</summary>
    public string? CsgoDir { get; init; }
    /// <summary>Overrides the backup root. Default: E:\CS2MOD\backups\cosmetics-lab.</summary>
    public string? BackupsRoot { get; init; }
    /// <summary>Skips the cs2.exe process check (fake-tree tests only).</summary>
    public bool SkipCs2ProcessCheck { get; init; }
    /// <summary>
    /// Process probe override for tests; default Cs2Locator.IsCs2Running. Apply and
    /// RestoreLatest share the same policy: refuse while CS2 runs.
    /// </summary>
    public Func<bool>? Cs2RunningProbe { get; init; }
    /// <summary>
    /// Pinned catalog snapshot used for preset validation. Apply must run catalog
    /// membership validation; without a catalog it fails closed.
    /// </summary>
    public CatalogIndex? Catalog { get; init; }
}

/// <summary>
/// Apply/rollback domain service for the Human cosmetics fixture. Ported from the
/// C2-accepted Apply-HumanCosmeticsPreset.ps1 semantics: refuse while CS2 runs, validate
/// (domain + pinned catalog membership), back up the installed fixture, atomic
/// same-volume replace, post-write hash verification with automatic rollback on failure,
/// rollback metadata whose write is part of the transaction (a failed record write rolls
/// the fixture back). This service never touches MetaMod, CSS, the InventorySimulator
/// DLL, gameinfo.gi or anything else.
/// </summary>
public class FixtureApplier
{
    private readonly FixtureApplierOptions _options;
    private readonly InventorySimulatorProjector _projector = new();

    public FixtureApplier(FixtureApplierOptions? options = null)
    {
        _options = options ?? new FixtureApplierOptions();
    }

    private bool Cs2Running =>
        !_options.SkipCs2ProcessCheck && (_options.Cs2RunningProbe ?? Cs2Locator.IsCs2Running)();

    public string InstalledFixturePath
    {
        get
        {
            var csgoDir = _options.CsgoDir ?? Cs2Locator.FindCsgoDir();
            return Path.Combine(csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");
        }
    }

    public ApplyRecord Apply(HumanPreset preset, string steamId64, string? presetPath = null)
    {
        if (Cs2Running)
            throw new ApplyException("cs2.exe is running. Close CS2, then re-run.");
        // Mutation boundary: the caller may hold an in-memory preset that never went
        // through JSON parsing (C4 UI), so re-validate domain + catalog membership here.
        HumanPresetValidator.EnsureValid(preset, _options.Catalog);

        var installedPath = InstalledFixturePath;
        if (!File.Exists(installedPath))
            throw new ApplyException($"Installed fixture not found at {installedPath} - the C1/C1.1 runtime install is missing.");

        var installedShaBefore = Sha256File(installedPath);

        // Project to staging (pure projection; validation already ran above).
        var projected = _projector.Project(preset, steamId64);
        var staging = Path.Combine(Path.GetTempPath(), $"cs2localkit-apply-{Guid.NewGuid():N}.json");
        var tempTarget = installedPath + ".new";
        try
        {
            File.WriteAllText(staging, projected);
            var stagedSha = Sha256File(staging);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
            var backupsRoot = _options.BackupsRoot ?? CorePaths.FixtureBackupRoot;
            var backupDir = Path.Combine(backupsRoot, $"{stamp}-{Guid.NewGuid().ToString("N")[..8]}-preset-apply");
            Directory.CreateDirectory(backupDir);
            var backupPath = Path.Combine(backupDir, "inventories.json");
            File.Copy(installedPath, backupPath, overwrite: true);
            if (Sha256File(backupPath) != installedShaBefore)
                throw new ApplyException("Backup verification failed - aborting before any change.");

            // Atomic same-volume replace: copy to "<target>.new", verify, then move.
            File.Copy(staging, tempTarget, overwrite: true);
            if (Sha256File(tempTarget) != stagedSha)
                throw new ApplyException("Staging copy to target volume failed.");
            File.Move(tempTarget, installedPath, overwrite: true);
            ReplaceVerified(staging, stagedSha, installedPath, backupPath, installedShaBefore);

            var record = new ApplyRecord
            {
                Kind = "cosmetics-lab-preset-apply-record",
                CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                PresetPath = presetPath ?? "<in-memory>",
                ProjectedSha256 = stagedSha,
                InstalledPath = installedPath,
                NewSha256 = stagedSha, // ReplaceVerified guarantees installed == staged
                PreviousSha256 = installedShaBefore,
                BackupPath = backupPath,
                BackupSha256 = installedShaBefore,
                Rollback = $"Copy-Item -LiteralPath '{backupPath}' -Destination '{installedPath}' -Force",
            };

            // The rollback record is part of the apply transaction: if it cannot be
            // written, the caller must not be left with "failed but new fixture still
            // installed" - restore the backup first, then report the failure.
            try
            {
                WriteApplyRecord(backupDir, record);
            }
            catch (Exception ex)
            {
                File.Copy(backupPath, installedPath, overwrite: true);
                if (Sha256File(installedPath) != installedShaBefore)
                    throw new ApplyException(
                        $"apply-record.json could not be written ({ex.Message}) AND the automatic rollback failed. " +
                        $"Manual restore required from: {backupPath}", ex);
                throw new ApplyException(
                    $"apply-record.json could not be written ({ex.Message}); installed fixture was rolled back to the previous state.", ex);
            }

            return record;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }
    }

    /// <summary>Persists the apply record. Internal seam so tests can simulate a metadata-write failure.</summary>
    internal virtual void WriteApplyRecord(string backupDir, ApplyRecord record)
    {
        File.WriteAllText(Path.Combine(backupDir, "apply-record.json"), record.ToJson());
    }

    /// <summary>
    /// Post-replace verification + auto-rollback. Internal seam so tests can drive the
    /// rollback path with a deliberately wrong expected hash.
    /// </summary>
    internal virtual void ReplaceVerified(string stagingPath, string stagedSha, string installedPath, string backupPath, string installedShaBefore)
    {
        var installedShaAfter = Sha256File(installedPath);
        if (installedShaAfter != stagedSha)
        {
            // Auto-rollback: never leave a half-applied state.
            File.Copy(backupPath, installedPath, overwrite: true);
            if (Sha256File(installedPath) != installedShaBefore)
                throw new ApplyException("Auto-rollback failed - manual restore required from backup dir.");
            throw new ApplyException("Post-replace verification failed; installed fixture auto-rolled back to the previous state.");
        }
    }

    /// <summary>
    /// Restores the most recent apply backup for the current installed path. Read-only
    /// discovery + explicit restore; never automatic. Same runtime-safety policy as
    /// Apply: refuses while CS2 runs, and refuses (without touching the file) when the
    /// installed fixture no longer matches the selected record's outcome - i.e. when
    /// something else changed it since that apply.
    /// </summary>
    public ApplyRecord RestoreLatest()
    {
        if (Cs2Running)
            throw new ApplyException("cs2.exe is running. Close CS2, then re-run.");

        var installedPath = InstalledFixturePath;
        var backupsRoot = _options.BackupsRoot ?? CorePaths.FixtureBackupRoot;
        if (!Directory.Exists(backupsRoot))
            throw new ApplyException($"No backup root found: {backupsRoot}");

        var candidates = Directory.EnumerateFiles(backupsRoot, "apply-record.json", SearchOption.AllDirectories)
            .Select(path => (Path: path, Record: ApplyRecord.FromJson(File.ReadAllText(path))))
            .Where(x => x.Record.InstalledPath.Equals(installedPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Record.CreatedAt, StringComparer.Ordinal)
            // Backup dir names start with the timestamp; ties within one system-clock
            // tick (DateTime.Now granularity) resolve deterministically to the later dir.
            .ThenByDescending(x => x.Path, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0)
            throw new ApplyException($"No apply records found for {installedPath}.");

        var (recordPath, record) = candidates[0];
        if (!File.Exists(record.BackupPath))
            throw new ApplyException($"Latest apply backup is missing: {record.BackupPath}");

        // Fail closed on drift: normal rollback is only valid while the installed fixture
        // is still exactly the state this apply produced. Report both hashes; never
        // overwrite the current file.
        var currentSha = File.Exists(installedPath) ? Sha256File(installedPath) : null;
        if (currentSha != record.NewSha256)
            throw new ApplyException(
                $"Current installed fixture no longer matches the latest apply record - refusing to restore. " +
                $"current sha256: {currentSha ?? "(file missing)"}, expected (record newSha256): {record.NewSha256}. " +
                $"Record: {recordPath}");

        var tempTarget = installedPath + ".new";
        try
        {
            File.Copy(record.BackupPath, tempTarget, overwrite: true);
            if (Sha256File(tempTarget) != record.PreviousSha256)
                throw new ApplyException("Backup content does not match the recorded previous hash - refusing to restore.");
            File.Move(tempTarget, installedPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempTarget)) File.Delete(tempTarget);
        }

        var restored = Sha256File(installedPath);
        if (restored != record.PreviousSha256)
            throw new ApplyException("Post-restore verification failed.");
        return record;
    }

    internal static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}

/// <summary>Rollback metadata, JSON-compatible with the C2-accepted PowerShell apply record.</summary>
public sealed class ApplyRecord
{
    public required string Kind { get; init; }
    public required string CreatedAt { get; init; }
    public required string PresetPath { get; init; }
    public required string ProjectedSha256 { get; init; }
    public required string InstalledPath { get; init; }
    public required string PreviousSha256 { get; init; }
    public required string NewSha256 { get; init; }
    public required string BackupPath { get; init; }
    public required string BackupSha256 { get; init; }
    public required string Rollback { get; init; }

    public string ToJson() => JsonSerializer.Serialize(new
    {
        kind = Kind,
        createdAt = CreatedAt,
        presetPath = PresetPath,
        projectedSha256 = ProjectedSha256,
        installedPath = InstalledPath,
        installed = new { previousSha256 = PreviousSha256, newSha256 = NewSha256 },
        backupPath = BackupPath,
        backupSha256 = BackupSha256,
        rollback = Rollback,
    }, new JsonSerializerOptions { WriteIndented = true });

    public static ApplyRecord FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new ApplyRecord
        {
            Kind = r.GetProperty("kind").GetString() ?? "",
            CreatedAt = r.GetProperty("createdAt").GetString() ?? "",
            PresetPath = r.TryGetProperty("presetPath", out var pp) ? pp.GetString() ?? "" : "",
            ProjectedSha256 = r.GetProperty("projectedSha256").GetString() ?? "",
            InstalledPath = r.GetProperty("installedPath").GetString() ?? "",
            PreviousSha256 = r.GetProperty("installed").GetProperty("previousSha256").GetString() ?? "",
            NewSha256 = r.GetProperty("installed").GetProperty("newSha256").GetString() ?? "",
            BackupPath = r.GetProperty("backupPath").GetString() ?? "",
            BackupSha256 = r.GetProperty("backupSha256").GetString() ?? "",
            Rollback = r.GetProperty("rollback").GetString() ?? "",
        };
    }
}

public sealed class ApplyException : Exception
{
    public ApplyException(string message) : base(message) { }
    public ApplyException(string message, Exception innerException) : base(message, innerException) { }
}
