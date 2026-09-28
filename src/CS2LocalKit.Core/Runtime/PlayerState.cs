using System.Text.Json;

namespace CS2LocalKit.Core.Runtime;

/// <summary>
/// Private player/runtime state (E:\CS2MOD\app-data\cosmetics-lab\player-state.json).
/// Carries the machine-verified human SteamID64. Never part of presets, never committed.
/// </summary>
public static class PlayerState
{
    public static string GetSteamId64(string? path = null)
    {
        var p = path ?? CorePaths.PlayerStatePath;
        if (!File.Exists(p))
            throw new InvalidOperationException(
                $"No player state found at {p} - pass an explicit SteamID64 instead.");
        using var doc = JsonDocument.Parse(File.ReadAllText(p));
        var id = doc.RootElement.TryGetProperty("steamId64", out var v) ? v.GetString() : null;
        if (id is null || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^\d{17}$"))
            throw new InvalidOperationException($"player-state.json does not contain a valid 17-digit steamId64.");
        return id;
    }
}
