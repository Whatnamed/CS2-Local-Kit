using CS2LocalKit.Core.Store;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Apply target resolution: explicit --preset wins; otherwise the active pointer;
/// never a silent fallback to a fixed default file. An explicit --preset never
/// changes the active pointer.
/// </summary>
public sealed class ApplyPresetResolverTests : IDisposable
{
    private readonly string _root;
    private readonly ActivePresetState _active;

    public ApplyPresetResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cs2localkit-resolvertests-" + Guid.NewGuid().ToString("N"));
        _active = new ActivePresetState(Path.Combine(_root, "active-preset.json"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ExplicitName_Wins_WithoutTouchingActivePointer()
    {
        _active.SetActive("a.v1.json");
        Assert.Equal("explicit.v1.json", ApplyPresetResolver.Resolve("explicit.v1.json", _active));
        Assert.Equal("a.v1.json", _active.GetActive()); // unchanged
    }

    [Fact]
    public void ActivePointer_IsUsed_WhenNoExplicitName()
    {
        _active.SetActive("active.v1.json");
        Assert.Equal("active.v1.json", ApplyPresetResolver.Resolve(null, _active));
        Assert.Equal("active.v1.json", ApplyPresetResolver.Resolve("", _active));
        Assert.Equal("active.v1.json", ApplyPresetResolver.Resolve("  ", _active));
    }

    [Fact]
    public void NoExplicitName_AndNoActivePointer_IsAnError()
    {
        var ex = Assert.Throws<PresetStoreException>(() => ApplyPresetResolver.Resolve(null, _active));
        Assert.Contains("set-active", ex.Message);
        Assert.Contains("--preset", ex.Message);
    }
}
