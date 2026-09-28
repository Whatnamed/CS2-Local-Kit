using System.Diagnostics;
using System.Text.RegularExpressions;

namespace CS2LocalKit.Core.Runtime;

/// <summary>
/// Locates the real CS2 install. Detection is based on appmanifest_730.acf inside a
/// library's steamapps folder (path probing alone hits stale library skeletons).
/// </summary>
public static partial class Cs2Locator
{
    private static readonly string[] SteamRoots =
    [
        @"D:\Steam",
        @"E:\SteamLibrary",
        @"C:\Program Files (x86)\Steam",
    ];

    [GeneratedRegex(@"""installdir""\s+""([^""]+)""")]
    private static partial Regex InstallDirRegex();

    public static bool IsCs2Running() => Process.GetProcessesByName("cs2").Length > 0;

    /// <summary>Returns the CS2 root (e.g. E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive).</summary>
    public static string FindCs2Root()
    {
        foreach (var steamRoot in SteamRoots)
        {
            var lib = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(lib)) continue;
            foreach (var library in ReadLibraryPaths(lib))
            {
                var manifest = Path.Combine(library, "steamapps", "appmanifest_730.acf");
                if (!File.Exists(manifest)) continue;
                var installdir = InstallDirRegex().Match(File.ReadAllText(manifest)).Groups[1].Value;
                if (string.IsNullOrEmpty(installdir)) continue;
                var root = Path.Combine(library, "steamapps", "common", installdir);
                if (File.Exists(Path.Combine(root, "game", "bin", "win64", "cs2.exe"))) return root;
            }
        }
        throw new InvalidOperationException("No library with a valid CS2 appmanifest + cs2.exe was found.");
    }

    public static string FindCsgoDir() => Path.Combine(FindCs2Root(), "game", "csgo");

    private static IEnumerable<string> ReadLibraryPaths(string libraryFoldersVdf)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(File.ReadAllText(libraryFoldersVdf), @"""path""\s+""([^""]+)"""))
        {
            var p = m.Groups[1].Value.Replace("\\\\", "\\");
            if (seen.Add(p)) yield return p;
        }
    }
}
