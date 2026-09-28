using CS2LocalKit.Core.Catalog;
using Xunit;

namespace CS2LocalKit.Core.Tests;

public class CatalogTests
{
    private static CatalogIndex LoadTrimmed() => CatalogIndex.Load(TestFixtures.CatalogDir);

    [Fact]
    public void PaintMembership_Weapon()
    {
        var index = LoadTrimmed();
        Assert.True(index.HasPaint(36, 258));  // P250 | Mehndi
        Assert.False(index.HasPaint(36, 999));
        Assert.True(index.HasPaint(7, 316));   // AK-47 | Jaguar
        Assert.False(index.HasPaint(7, 3));
    }

    [Fact]
    public void PaintMembership_KnifeAndGloves()
    {
        var index = LoadTrimmed();
        Assert.True(index.HasPaint(507, 38));      // Karambit | Fade
        Assert.False(index.HasPaint(507, 10037));  // a glove paint is not a Karambit finish
        Assert.True(index.HasPaint(515, 409));     // Butterfly | Tiger Tooth
        Assert.True(index.HasPaint(5030, 10037)); // Sport Gloves | Pandora's Box
        Assert.False(index.HasPaint(5030, 9999));
    }

    [Fact]
    public void MusicKitLookup()
    {
        var index = LoadTrimmed();
        Assert.True(index.TryGetMusicKit(78, out var name));
        Assert.Contains("Austin Wintory", name);
        Assert.False(index.TryGetMusicKit(999, out _));
    }

    [Fact]
    public void DisplayMetadata_Classification()
    {
        var index = LoadTrimmed();
        Assert.True(index.TryGetWeapon(507, out var karambit));
        Assert.True(karambit.IsKnife);
        Assert.True(index.TryGetWeapon(5030, out var sportGloves));
        Assert.True(sportGloves.IsGloves);
        Assert.True(index.TryGetWeapon(36, out var p250));
        Assert.Contains("P250", p250.Name);
    }
}
