using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>
/// Observation-only runtime/compatibility status for Controller/UI consumption. Reads
/// files and process state; never repairs, updates or writes anything.
/// </summary>
public sealed partial class RuntimeStatusService
{
    public sealed class Options
    {
        public string? Cs2Root { get; init; }
        public string? LockPath { get; init; }
        public string? BackupsRoot { get; init; }
        public string? PresetsRoot { get; init; }
        public string? ActivePresetPath { get; init; }
    }

    private readonly Options _options;

    public RuntimeStatusService(Options? options = null)
    {
        _options = options ?? new Options();
    }

    [GeneratedRegex(@"^(PatchVersion|ClientVersion)=(.*)$", RegexOptions.Multiline)]
    private static partial Regex SteamInfLine();

    public RuntimeStatus GetStatus(InventorySimulatorLock? lockSnapshot = null)
    {
        var status = new RuntimeStatus();

        string? cs2Root = _options.Cs2Root;
        if (cs2Root is null)
        {
            try { cs2Root = Cs2Locator.FindCs2Root(); }
            catch { cs2Root = null; }
        }
        status.Cs2Detected = cs2Root is not null;
        status.Cs2Root = cs2Root;
        status.Cs2Running = Cs2Locator.IsCs2Running();

        // Lock / expected baseline
        var lockPath = _options.LockPath;
        InventorySimulatorLock? pin = lockSnapshot;
        if (pin is null && lockPath is not null && File.Exists(lockPath))
            pin = InventorySimulatorLock.Load(lockPath);
        status.Lock = pin;

        if (cs2Root is not null)
        {
            var csgoDir = Path.Combine(cs2Root, "game", "csgo");

            var steamInf = Path.Combine(csgoDir, "steam.inf");
            if (File.Exists(steamInf))
            {
                foreach (Match m in SteamInfLine().Matches(File.ReadAllText(steamInf)))
                {
                    if (m.Groups[1].Value == "PatchVersion") status.PatchVersion = m.Groups[2].Value.Trim();
                    else status.ClientVersion = m.Groups[2].Value.Trim();
                }
            }
            var acf = Path.Combine(cs2Root, "..", "..", "appmanifest_730.acf");
            if (File.Exists(acf))
            {
                var m2 = Regex.Match(File.ReadAllText(acf), @"""buildid""\s+""(\d+)""");
                if (m2.Success) status.BuildId = m2.Groups[1].Value;
            }

            if (pin?.PatchVersion is { } expPatch)
                status.TestedBuildMatch =
                    status.PatchVersion == expPatch
                    && (pin.ClientVersion is null || status.ClientVersion == pin.ClientVersion)
                    && (pin.BuildId is null || status.BuildId == pin.BuildId)
                        ? "match"
                        : "changed";
            else
                status.TestedBuildMatch = "unknown";

            status.GameinfoHasMetamod = File.Exists(Path.Combine(csgoDir, "gameinfo.gi"))
                && File.ReadAllText(Path.Combine(csgoDir, "gameinfo.gi")).Contains("csgo/addons/metamod");
            status.MetaModNativePresent = File.Exists(Path.Combine(csgoDir, "addons", "metamod", "bin", "win64", "metamod.2.cs2.dll"));
            status.CounterStrikeSharpNativePresent = Directory.Exists(Path.Combine(csgoDir, "addons", "counterstrikesharp", "bin", "win64"));

            var isDll = Path.Combine(csgoDir, "addons", "counterstrikesharp", "plugins", "InventorySimulator", "InventorySimulator.dll");
            status.InventorySimulatorPluginPresent = File.Exists(isDll);
            if (status.InventorySimulatorPluginPresent)
            {
                using var fs = File.OpenRead(isDll);
                status.InventorySimulatorDllSha256 = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                status.PatchedDllMatch = pin?.PatchedDllSha256 is { } expected
                    ? (status.InventorySimulatorDllSha256 == expected ? "match" : "mismatch")
                    : "unknown";
            }

            var fixture = Path.Combine(csgoDir, "addons", "counterstrikesharp", "configs", "plugins", "InventorySimulator", "inventories.json");
            status.FixtureInstalled = File.Exists(fixture);
            if (status.FixtureInstalled)
            {
                using var fs = File.OpenRead(fixture);
                status.FixtureSha256 = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
        }

        var presetsRoot = _options.PresetsRoot ?? CorePaths.PresetsHumanRoot;
        var active = new Store.ActivePresetState(_options.ActivePresetPath).GetActive();
        status.ActivePreset = active;
        status.ActivePresetExists = active is not null && File.Exists(Path.Combine(presetsRoot, active));

        status.LatestApply = FindLatestApply(_options.BackupsRoot ?? CorePaths.FixtureBackupRoot);
        return status;
    }

    private static LatestApplyInfo? FindLatestApply(string backupsRoot)
    {
        if (!Directory.Exists(backupsRoot)) return null;
        LatestApplyInfo? latest = null;
        foreach (var recordPath in Directory.EnumerateFiles(backupsRoot, "apply-record.json", SearchOption.AllDirectories))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(recordPath));
                var r = doc.RootElement;
                var info = new LatestApplyInfo
                {
                    RecordPath = recordPath,
                    CreatedAt = r.TryGetProperty("createdAt", out var c) ? c.GetString() ?? "" : "",
                    PresetPath = r.TryGetProperty("presetPath", out var p) ? p.GetString() ?? "" : "",
                    ProjectedSha256 = r.TryGetProperty("projectedSha256", out var s) ? s.GetString() ?? "" : "",
                    InstalledPath = r.TryGetProperty("installedPath", out var i) ? i.GetString() ?? "" : "",
                    RollbackAvailable = r.TryGetProperty("backupPath", out var b) && File.Exists(b.GetString() ?? ""),
                };
                if (latest is null || string.CompareOrdinal(info.CreatedAt, latest.CreatedAt) > 0) latest = info;
            }
            catch (JsonException) { /* skip unreadable records */ }
        }
        return latest;
    }
}

public sealed class RuntimeStatus
{
    public bool Cs2Detected { get; set; }
    public string? Cs2Root { get; set; }
    public bool Cs2Running { get; set; }
    public string? PatchVersion { get; set; }
    public string? ClientVersion { get; set; }
    public string? BuildId { get; set; }
    /// <summary>"match" | "changed" | "unknown" against the lock's tested build.</summary>
    public string TestedBuildMatch { get; set; } = "unknown";
    public bool GameinfoHasMetamod { get; set; }
    public bool MetaModNativePresent { get; set; }
    public bool CounterStrikeSharpNativePresent { get; set; }
    public bool InventorySimulatorPluginPresent { get; set; }
    public string? InventorySimulatorDllSha256 { get; set; }
    /// <summary>"match" | "mismatch" | "unknown" against the lock's patched DLL hash.</summary>
    public string PatchedDllMatch { get; set; } = "unknown";
    public bool FixtureInstalled { get; set; }
    public string? FixtureSha256 { get; set; }
    public string? ActivePreset { get; set; }
    public bool ActivePresetExists { get; set; }
    public LatestApplyInfo? LatestApply { get; set; }
    public InventorySimulatorLock? Lock { get; set; }
}

public sealed class LatestApplyInfo
{
    public required string RecordPath { get; init; }
    public required string CreatedAt { get; init; }
    public required string PresetPath { get; init; }
    public required string ProjectedSha256 { get; init; }
    public required string InstalledPath { get; init; }
    public required bool RollbackAvailable { get; init; }
}
