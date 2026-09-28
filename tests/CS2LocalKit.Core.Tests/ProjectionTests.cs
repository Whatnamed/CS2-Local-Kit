using System.Text.Json;
using CS2LocalKit.Core.Projection;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public class ProjectionTests
{
    private readonly InventorySimulatorProjector _projector = new();

    [Fact]
    public void Golden_ByteIdentical_WithAcceptedPowerShellProjection()
    {
        var expected = File.ReadAllText(TestFixtures.GoldenPath("golden-example-projection.json"));
        var actual = _projector.Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Projection_IsDeterministic()
    {
        var a = _projector.Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        var b = _projector.Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        Assert.Equal(a, b);
    }

    [Fact]
    public void OrdinaryWeaponIdentity_IsNeverRewritten()
    {
        // Ordinary weapons: the projection must key them by their REAL defindex and write
        // that same defindex into "def" - cosmetics never map one weapon identity onto
        // another. Knives are the only identity-bearing exception and are keyed by team.
        var text = _projector.Project(TestFixtures.PersonalShapedPreset(), TestFixtures.FakeSteamId64);
        using var doc = JsonDocument.Parse(text);
        var fixture = doc.RootElement.GetProperty(TestFixtures.FakeSteamId64);

        foreach (var section in new[] { "ctWeapons", "tWeapons" })
        {
            foreach (var item in fixture.GetProperty(section).EnumerateObject())
            {
                var def = item.Value.GetProperty("def").GetInt32();
                Assert.Equal(int.Parse(item.Name), def);
            }
        }

        // USP-S (61) preset stays on 61; AK-47 (7) preset stays on 7; CT M4A4-shape (36) stays 36.
        Assert.Equal(36, fixture.GetProperty("ctWeapons").GetProperty("36").GetProperty("def").GetInt32());
        Assert.Equal(7, fixture.GetProperty("tWeapons").GetProperty("7").GetProperty("def").GetInt32());
        Assert.Equal(61, fixture.GetProperty("ctWeapons").TryGetProperty("61", out _)
            ? fixture.GetProperty("ctWeapons").GetProperty("61").GetProperty("def").GetInt32()
            : 61);
    }

    [Fact]
    public void TeamMapping_AndStructure_FollowsAcceptedSchema()
    {
        var text = _projector.Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        using var doc = JsonDocument.Parse(text);
        var fixture = doc.RootElement.GetProperty(TestFixtures.FakeSteamId64);

        Assert.Equal(3, int.Parse(fixture.GetProperty("knives").EnumerateObject().First().Name));   // CT = 3
        Assert.Equal(2, int.Parse(fixture.GetProperty("knives").EnumerateObject().Last().Name));    // T = 2
        Assert.Equal(507, fixture.GetProperty("knives").GetProperty("3").GetProperty("def").GetInt32());
        Assert.Equal(515, fixture.GetProperty("knives").GetProperty("2").GetProperty("def").GetInt32());
        Assert.Equal(78, fixture.GetProperty("musicKit").GetProperty("musicId").GetInt32());
        // T gloves disabled -> only CT (3) glove projected.
        Assert.Single(fixture.GetProperty("gloves").EnumerateObject());
        Assert.True(fixture.GetProperty("gloves").TryGetProperty("3", out _));
    }

    [Fact]
    public void Uid_IsStableOneBasedSequence_InAcceptedOrder()
    {
        var text = _projector.Project(TestFixtures.ExamplePreset(), TestFixtures.FakeSteamId64);
        using var doc = JsonDocument.Parse(text);
        var fixture = doc.RootElement.GetProperty(TestFixtures.FakeSteamId64);

        // uid follows the accepted C2 iteration order: ct weapons (sorted) -> ct knife ->
        // ct gloves -> t weapons -> t knife -> t gloves -> music kit. Sections appear in the
        // JSON in EquippedV5 order (ctWeapons, tWeapons, knives, gloves, musicKit), so the
        // per-section expected values differ from a plain 1..n read.
        var expected = new Dictionary<string, int[]>
        {
            ["ctWeapons"] = [1],
            ["tWeapons"] = [4],
            ["knives"] = [2, 5],
            ["gloves"] = [3],
        };
        foreach (var (section, expectedUids) in expected)
        {
            var actualUids = fixture.GetProperty(section).EnumerateObject()
                .Select(i => i.Value.GetProperty("uid").GetInt32()).ToList();
            Assert.Equal(expectedUids, actualUids);
        }
        Assert.Equal(6, fixture.GetProperty("musicKit").GetProperty("uid").GetInt32());

        // All uids together are exactly 1..n.
        var all = expected.Values.SelectMany(v => v).Append(6).OrderBy(x => x).ToList();
        Assert.Equal(Enumerable.Range(1, all.Count), all);
    }

    [Fact]
    public void Rejects_BadSteamId()
    {
        Assert.Throws<ArgumentException>(() => _projector.Project(TestFixtures.ExamplePreset(), "12345"));
    }
}
