using CS2LocalKit.Core.Store;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public sealed class PresetStoreTests : IDisposable
{
    private readonly string _root;
    private readonly PresetStore _store;

    public PresetStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cs2localkit-tests-" + Guid.NewGuid().ToString("N"));
        _store = new PresetStore(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SaveLoad_Duplicate_List_Delete()
    {
        _store.Save("alpha.v1.json", TestFixtures.ExamplePreset());
        Assert.True(_store.Exists("alpha.v1.json"));
        _store.Duplicate("alpha.v1.json", "beta.v1.json");
        Assert.Equal(["alpha.v1.json", "beta.v1.json"], _store.ListNames());

        var loaded = _store.Load("beta.v1.json");
        Assert.Equal(78, loaded.MusicKitId);

        _store.Delete("beta.v1.json");
        Assert.Equal(["alpha.v1.json"], _store.ListNames());
        Assert.Throws<PresetStoreException>(() => _store.Delete("beta.v1.json"));
    }

    [Theory]
    [InlineData("../evil.json")]
    [InlineData("..\\evil.json")]
    [InlineData("sub/dir/x.json")]
    [InlineData("a..b")] // harmless dots in the middle of a file NAME are fine; traversal is not
    public void PathTraversal_IsRejected(string badName)
    {
        if (badName == "a..b")
        {
            // contains ".." but as part of a file name without separators - policy: still rejected
            Assert.Throws<PresetStoreException>(() => _store.ResolvePath(badName));
            return;
        }
        Assert.Throws<PresetStoreException>(() => _store.ResolvePath(badName));
    }

    [Fact]
    public void AbsolutePath_IsRejected()
    {
        Assert.Throws<PresetStoreException>(() => _store.ResolvePath(Path.Combine(Path.GetTempPath(), "x.json")));
    }

    [Fact]
    public void Delete_CannotReachOutsideRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), "cs2localkit-outside-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outside, "{}");
        try
        {
            Assert.Throws<PresetStoreException>(() => _store.Delete(Path.Combine(Path.GetTempPath(), "..", Path.GetFileName(outside))));
            Assert.True(File.Exists(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void Import_Validates_AndExports()
    {
        var exportPath = Path.Combine(_root, "export-out.json");
        _store.Save("alpha.v1.json", TestFixtures.ExamplePreset());

        _store.Export("alpha.v1.json", exportPath);
        Assert.True(File.Exists(exportPath));
        Assert.Throws<PresetStoreException>(() => _store.Export("alpha.v1.json", exportPath)); // no overwrite by default
        _store.Export("alpha.v1.json", exportPath, overwrite: true);

        var importedName = _store.Import(exportPath, "imported.v1.json");
        Assert.Equal("imported.v1.json", importedName);
        Assert.Equal(78, _store.Load(importedName).MusicKitId);

        var badImport = Path.Combine(_root, "bad-import.json");
        File.WriteAllText(badImport, "{ not a preset }");
        Assert.ThrowsAny<Exception>(() => _store.Import(badImport, "should-not-exist.json"));
        Assert.False(_store.Exists("should-not-exist.json"));
    }

    [Fact]
    public void ActivePreset_PointerRoundTrip()
    {
        var statePath = Path.Combine(_root, "active-preset.json");
        var state = new ActivePresetState(statePath);
        Assert.Null(state.GetActive());
        state.SetActive("personal-default.v1.json");
        Assert.Equal("personal-default.v1.json", state.GetActive());
    }
}
