using System.Text.Json;
using CS2LocalKit.Core.HumanPresets;

namespace CS2LocalKit.Core.Store;

/// <summary>
/// Stable access to the personal preset store (E:\CS2MOD\presets\human\).
/// All name-based operations are path-safe: names must be plain file names inside the
/// store root - traversal, absolute paths and escaping the root are rejected.
/// </summary>
public sealed class PresetStore
{
    public string Root { get; }

    public PresetStore(string? root = null)
    {
        Root = Path.GetFullPath(root ?? CorePaths.PresetsHumanRoot);
    }

    public IReadOnlyList<string> ListNames()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.EnumerateFiles(Root, "*.json")
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Resolves a preset name to a full path inside the root. Throws on traversal.</summary>
    public string ResolvePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new PresetStoreException("preset name is empty");
        if (name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            throw new PresetStoreException($"preset name must be a plain file name inside the store: '{name}'");
        if (Path.IsPathRooted(name))
            throw new PresetStoreException($"preset name must not be an absolute path: '{name}'");

        var fileName = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        var full = Path.GetFullPath(Path.Combine(Root, fileName));
        if (!full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, Root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new PresetStoreException($"preset path escapes the store root: '{name}'");
        return full;
    }

    public bool Exists(string name) => File.Exists(ResolvePath(name));

    /// <summary>Parses and validates the stored preset. Throws HumanPresetFormatException on invalid content.</summary>
    public HumanPreset Load(string name) => HumanPresetJson.Parse(File.ReadAllText(ResolvePath(name)));

    /// <summary>
    /// Writes the preset in canonical form. Atomic (temp file + move within the root).
    /// Mutation boundary: re-validates the in-memory object (domain rules) because C4 UI
    /// constructs HumanPreset instances that never went through JSON parsing. Catalog
    /// membership is checked at apply time, not here - see FixtureApplier.
    /// </summary>
    public void Save(string name, HumanPreset preset)
    {
        HumanPresets.HumanPresetValidator.EnsureDomainValid(preset);
        var path = ResolvePath(name);
        Directory.CreateDirectory(Root);
        var temp = path + ".tmp";
        File.WriteAllText(temp, HumanPresetJson.Write(preset));
        File.Move(temp, path, overwrite: true);
    }

    public void Duplicate(string sourceName, string destName)
    {
        var preset = Load(sourceName); // validates source
        Save(destName, preset);
    }

    public void Delete(string name)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path)) throw new PresetStoreException($"preset not found: {name}");
        File.Delete(path);
    }

    /// <summary>Imports an external canonical HumanPreset file into the store (validated before copy).</summary>
    public string Import(string sourcePath, string? destName = null)
    {
        if (!File.Exists(sourcePath)) throw new PresetStoreException($"import source not found: {sourcePath}");
        var preset = HumanPresetJson.Parse(File.ReadAllText(sourcePath));
        var name = destName ?? Path.GetFileName(sourcePath);
        Save(name, preset);
        return name;
    }

    /// <summary>Exports a stored preset to an explicit destination path.</summary>
    public void Export(string name, string destPath, bool overwrite = false)
    {
        var source = ResolvePath(name);
        if (!File.Exists(source)) throw new PresetStoreException($"preset not found: {name}");
        var dest = Path.GetFullPath(destPath);
        if (File.Exists(dest) && !overwrite)
            throw new PresetStoreException($"export destination already exists: {destPath} (pass overwrite)");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }
}

public sealed class PresetStoreException : Exception
{
    public PresetStoreException(string message) : base(message) { }
}

/// <summary>Active preset pointer (private app-data state, not a per-preset property).</summary>
public sealed class ActivePresetState
{
    private readonly string _path;

    public ActivePresetState(string? path = null)
    {
        _path = path ?? CorePaths.ActivePresetPath;
    }

    public string? GetActive()
    {
        if (!File.Exists(_path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        return doc.RootElement.TryGetProperty("activePreset", out var p) ? p.GetString() : null;
    }

    public void SetActive(string name)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var state = new
        {
            kind = "cosmetics-lab-active-preset",
            schemaVersion = 1,
            activePreset = name,
            updatedAt = DateTimeOffset.Now,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
