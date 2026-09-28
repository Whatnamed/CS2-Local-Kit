namespace CS2LocalKit.Core;

/// <summary>
/// Machine layout defaults (Windows-only product). Tests override paths explicitly;
/// the CS2LOCALKIT_CS2MOD_ROOT environment variable overrides the data root.
/// </summary>
public static class CorePaths
{
    public static string Cs2ModRoot =>
        Environment.GetEnvironmentVariable("CS2LOCALKIT_CS2MOD_ROOT") ?? @"E:\CS2MOD";

    public static string PresetsHumanRoot => Path.Combine(Cs2ModRoot, "presets", "human");
    public static string AppDataRoot => Path.Combine(Cs2ModRoot, "app-data", "cosmetics-lab");
    public static string PlayerStatePath => Path.Combine(AppDataRoot, "player-state.json");
    public static string ActivePresetPath => Path.Combine(AppDataRoot, "active-preset.json");
    public static string FixtureBackupRoot => Path.Combine(Cs2ModRoot, "backups", "cosmetics-lab");
    public static string LastProjectedFixturePath => Path.Combine(AppDataRoot, "inventory-simulator", "inventories.json");
}
