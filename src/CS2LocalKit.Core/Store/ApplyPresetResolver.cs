using CS2LocalKit.Core.HumanPresets;

namespace CS2LocalKit.Core.Store;

/// <summary>
/// Resolves which preset an apply operation targets. Explicit name wins; otherwise the
/// active-preset pointer is used. There is deliberately no default fallback to a fixed
/// file name (e.g. personal-default.v1.json): an apply without an explicit target and
/// without an active pointer is an error, so the operator decides - never the code.
/// </summary>
public static class ApplyPresetResolver
{
    public static string Resolve(string? explicitName, ActivePresetState active)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return explicitName;

        var activeName = active.GetActive();
        if (string.IsNullOrWhiteSpace(activeName))
            throw new PresetStoreException(
                "no active preset set - use 'cs2localkit presets set-active <name>' or pass an explicit preset (apply --preset <name>)");
        return activeName;
    }
}
