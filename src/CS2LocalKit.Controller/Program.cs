using System.Text.Json;
using CS2LocalKit.Core.Application;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using CS2LocalKit.Core.Projection;
using CS2LocalKit.Core.Runtime;
using CS2LocalKit.Core.Store;

namespace CS2LocalKit.Controller;

/// <summary>
/// Thin developer CLI over CS2LocalKit.Core. This is the executable acceptance entry for
/// the Core API, not the final product UI. Commands:
///   presets list | validate &lt;name&gt; | show-active | set-active &lt;name&gt;
///              | export &lt;name&gt; &lt;dest&gt; [--overwrite] | import &lt;src&gt; [--as &lt;name&gt;]
///   apply [--preset &lt;name&gt;] [--steamid &lt;id&gt;]
///   restore-latest
///   status [--lock &lt;path&gt;]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) { PrintUsage(); return 2; }
            return args[0] switch
            {
                "presets" => RunPresets(args[1..]),
                "apply" => RunApply(args[1..]),
                "restore-latest" => RunRestoreLatest(),
                "status" => RunStatus(args[1..]),
                _ => Usage($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("usage: cs2localkit <command>");
        Console.WriteLine("  presets list | validate <name> | show-active | set-active <name>");
        Console.WriteLine("  presets export <name> <dest> [--overwrite] | import <src> [--as <name>]");
        Console.WriteLine("  apply [--preset <name>] [--steamid <id>]");
        Console.WriteLine("  restore-latest");
        Console.WriteLine("  status [--lock <path>]");
    }

    private static int RunPresets(string[] args)
    {
        if (args.Length == 0) return Usage("presets requires a subcommand");
        var store = new PresetStore();
        switch (args[0])
        {
            case "list":
                foreach (var name in store.ListNames()) Console.WriteLine(name);
                return 0;
            case "validate":
            {
                var preset = store.Load(GetArg(args, 1) ?? throw new ArgumentException("validate requires a preset name"));
                Console.WriteLine($"valid HumanPreset v1: weapons ct={preset.Ct.Weapons.Count} t={preset.T.Weapons.Count}, " +
                                  $"knife ct={preset.Ct.Knife.Selected} t={preset.T.Knife.Selected}, musicKit={preset.MusicKitId?.ToString() ?? "none"}");
                return 0;
            }
            case "show-active":
                Console.WriteLine(new ActivePresetState().GetActive() ?? "(none set)");
                return 0;
            case "set-active":
            {
                var name = GetArg(args, 1) ?? throw new ArgumentException("set-active requires a preset name");
                if (!store.Exists(name)) throw new ArgumentException($"preset not found: {name}");
                new ActivePresetState().SetActive(Path.GetFileName(store.ResolvePath(name)));
                Console.WriteLine($"active preset: {new ActivePresetState().GetActive()}");
                return 0;
            }
            case "export":
            {
                var name = GetArg(args, 1) ?? throw new ArgumentException("export requires a preset name");
                var dest = GetArg(args, 2) ?? throw new ArgumentException("export requires a destination path");
                var overwrite = args.Contains("--overwrite");
                store.Export(name, dest, overwrite);
                Console.WriteLine($"exported {name} -> {dest}");
                return 0;
            }
            case "import":
            {
                var src = GetArg(args, 1) ?? throw new ArgumentException("import requires a source path");
                var asIdx = Array.IndexOf(args, "--as");
                var destName = asIdx >= 0 && asIdx + 1 < args.Length ? args[asIdx + 1] : null;
                var name = store.Import(src, destName);
                Console.WriteLine($"imported as {name}");
                return 0;
            }
            default:
                return Usage($"unknown presets subcommand '{args[0]}'");
        }
    }

    private static int RunApply(string[] args)
    {
        var presetName = "personal-default.v1.json";
        string? steamId = null;
        var presetIdx = Array.IndexOf(args, "--preset");
        if (presetIdx >= 0 && presetIdx + 1 < args.Length) presetName = args[presetIdx + 1];
        var sidIdx = Array.IndexOf(args, "--steamid");
        if (sidIdx >= 0 && sidIdx + 1 < args.Length) steamId = args[sidIdx + 1];

        var store = new PresetStore();
        var preset = store.Load(presetName);
        steamId ??= PlayerState.GetSteamId64();
        var applier = new FixtureApplier();
        var record = applier.Apply(preset, steamId, Path.GetFileName(store.ResolvePath(presetName)));
        Console.WriteLine($"applied preset '{presetName}'");
        Console.WriteLine($"  projection sha256: {record.ProjectedSha256}");
        Console.WriteLine($"  previous         : {record.PreviousSha256}");
        Console.WriteLine($"  rollback         : {record.Rollback}");
        return 0;
    }

    private static int RunRestoreLatest()
    {
        var record = new FixtureApplier().RestoreLatest();
        Console.WriteLine("restored latest apply backup");
        Console.WriteLine($"  createdAt: {record.CreatedAt}");
        Console.WriteLine($"  restored sha256: {record.PreviousSha256}");
        return 0;
    }

    private static int RunStatus(string[] args)
    {
        string? lockPath = null;
        var lockIdx = Array.IndexOf(args, "--lock");
        if (lockIdx >= 0 && lockIdx + 1 < args.Length) lockPath = args[lockIdx + 1];
        lockPath ??= FindLockPath();

        var status = new RuntimeStatusService(new RuntimeStatusService.Options
        {
            LockPath = lockPath,
        }).GetStatus();

        Console.WriteLine($"cs2 detected      : {status.Cs2Detected}{(status.Cs2Root is null ? "" : $" ({status.Cs2Root})")}");
        Console.WriteLine($"cs2 running       : {status.Cs2Running}");
        Console.WriteLine($"build             : patch={status.PatchVersion ?? "?"} client={status.ClientVersion ?? "?"} buildid={status.BuildId ?? "?"}");
        Console.WriteLine($"tested build match: {status.TestedBuildMatch}");
        Console.WriteLine($"gameinfo metamod  : {status.GameinfoHasMetamod}");
        Console.WriteLine($"metamod native    : {status.MetaModNativePresent}{(status.Lock is null ? "" : $" (expected {status.Lock.MetaModVersion})")}");
        Console.WriteLine($"cssharp native    : {status.CounterStrikeSharpNativePresent}{(status.Lock is null ? "" : $" (expected {status.Lock.CounterStrikeSharpVersion})")}");
        Console.WriteLine($"invsim plugin     : {status.InventorySimulatorPluginPresent}, patched dll hash: {status.PatchedDllMatch}");
        Console.WriteLine($"fixture installed : {status.FixtureInstalled}, sha256: {status.FixtureSha256 ?? "-"}");
        Console.WriteLine($"active preset     : {status.ActivePreset ?? "(none set)"}{(status.ActivePreset is null || status.ActivePresetExists ? "" : " (MISSING FILE)")}");
        Console.WriteLine($"latest apply      : {(status.LatestApply is null ? "(none)" : $"{status.LatestApply.CreatedAt} projection={status.LatestApply.ProjectedSha256} rollbackAvailable={status.LatestApply.RollbackAvailable}")}");
        return 0;
    }

    private static string? FindLockPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var d = dir; d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "runtime", "inventory-simulator.lock.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? GetArg(string[] args, int index)
        => index < args.Length ? args[index] : null;
}
