using System.Net.Http;
using System.Text.Json;
using CS2LocalKit.App.Catalog;
using CS2LocalKit.Core.Catalog;
using CS2LocalKit.Core.HumanPresets;
using Xunit;

namespace CS2LocalKit.Core.Tests;

/// <summary>
/// Localization is an attachment to an identity that already exists. These tests pin that order:
/// the English snapshot decides what is real, Chinese only decorates known numeric keys, and
/// neither search nor validation ever depends on a display name.
/// </summary>
public sealed class CatalogLocalizationTests : IDisposable
{
    private readonly string _work;

    public CatalogLocalizationTests()
    {
        _work = Path.Combine(Path.GetTempPath(), $"cs2localkit-i18n-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, recursive: true); } catch { /* temp */ }
    }

    private static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true };

    /// <summary>Build a locale cache directory from the English fixture, translating display names.</summary>
    private string CreateLocaleCache(bool withChinese = true, bool withMusic = true)
    {
        var root = _work;
        var en = CatalogSnapshot.LocaleDir(root, CatalogSnapshot.IdentityLocale);
        Directory.CreateDirectory(en);
        File.Copy(TestFixtures.CatalogPath("skins.json"), Path.Combine(en, "skins.json"), overwrite: true);
        if (withMusic)
            File.Copy(TestFixtures.CatalogPath("music_kits.json"), Path.Combine(en, "music_kits.json"), overwrite: true);

        if (!withChinese) return root;

        var zh = CatalogSnapshot.LocaleDir(root, CatalogSnapshot.ChineseLocale);
        Directory.CreateDirectory(zh);
        File.WriteAllText(Path.Combine(zh, "skins.json"), Localize(File.ReadAllText(Path.Combine(en, "skins.json"))));
        if (withMusic && File.Exists(Path.Combine(en, "music_kits.json")))
            File.WriteAllText(Path.Combine(zh, "music_kits.json"), Localize(File.ReadAllText(Path.Combine(en, "music_kits.json"))));
        return root;
    }

    /// <summary>Rename every display string with a marker, leaving all numeric identity untouched.</summary>
    private static string Localize(string json)
    {
        using var doc = JsonDocument.Parse(json, Options);
        var rewritten = Rewrite(doc.RootElement);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            rewritten.WriteTo(writer);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());

        static JsonElement Rewrite(JsonElement root)
        {
            var list = new List<JsonElement>();
            foreach (var item in root.EnumerateArray())
                list.Add(JsonDocument.Parse(RewriteObject(item.ToString()), Options).RootElement.Clone());
            var serialized = "[" + string.Join(",", list.Select(e => e.GetRawText())) + "]";
            return JsonDocument.Parse(serialized, Options).RootElement.Clone();
        }

        static string RewriteObject(string objJson)
        {
            using var doc = JsonDocument.Parse(objJson, Options);
            var map = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value;
                if (prop.Name == "name" && value.ValueKind == JsonValueKind.String)
                    map[prop.Name] = "中文·" + value.GetString();
                else if (value.ValueKind == JsonValueKind.Object && prop.Name is "weapon" or "pattern" or "rarity")
                    map[prop.Name] = JsonDocument.Parse(RewriteObject(value.ToString()), Options).RootElement.Clone();
                else
                    map[prop.Name] = value.Clone();
            }
            return JsonSerializer.Serialize(map, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never });
        }
    }

    [Fact]
    public void Load_MergesChineseByStableNumericIdentity()
    {
        var index = CatalogIndex.Load(CreateLocaleCache());

        Assert.True(index.HasChinese);
        Assert.Contains(CatalogSnapshot.ChineseLocale, index.LocalesLoaded);

        var weapon = index.GetOrdinaryWeapons().First(w => w.Paints.Count > 0);
        Assert.StartsWith("中文·", weapon.ChineseName);
        Assert.DoesNotContain("中文·", weapon.Name);

        var paint = weapon.Paints.Values.First(p => p.ChineseName is not null);
        Assert.StartsWith("中文·", paint.ChineseName);
        Assert.StartsWith("中文·", paint.PatternChineseName);
        Assert.Equal("中文·" + paint.Name.Replace("中文·", ""), paint.ChineseName);
        Assert.True(index.LocalizedPaintCount > 0);
    }

    [Fact]
    public void Load_IdentityKeysSurviveLocalization()
    {
        var enOnly = CatalogIndex.Load(CreateLocaleCache(withChinese: false));
        var bilingual = CatalogIndex.Load(CreateLocaleCache());

        Assert.Equal(enOnly.WeaponCount, bilingual.WeaponCount);
        Assert.Equal(enOnly.PaintCount, bilingual.PaintCount);
        Assert.Equal(enOnly.GetOrdinaryWeapons().Select(w => w.DefIndex), bilingual.GetOrdinaryWeapons().Select(w => w.DefIndex));

        foreach (var weapon in enOnly.GetOrdinaryWeapons().Take(8))
        {
            Assert.True(bilingual.TryGetWeapon(weapon.DefIndex, out var same));
            Assert.Equal(weapon.Name, same.Name);
            Assert.Equal(weapon.Paints.Keys.OrderBy(k => k), same.Paints.Keys.OrderBy(k => k));
            foreach (var key in weapon.Paints.Keys)
                Assert.Equal(weapon.Paints[key].PaintIndex, same.Paints[key].PaintIndex);
        }
    }

    [Fact]
    public void Load_ChineseRecordForUnknownKey_IsIgnored()
    {
        var root = CreateLocaleCache();
        var zhPath = Path.Combine(CatalogSnapshot.LocaleDir(root, CatalogSnapshot.ChineseLocale), "skins.json");
        var extra = File.ReadAllText(zhPath);
        var weapon = CatalogIndex.Load(root).GetOrdinaryWeapons().First();
        var unknown = extra.TrimEnd().TrimEnd(']') +
            ",{\"id\":\"ghost\",\"name\":\"中文·幽灵皮肤\",\"weapon\":{\"weapon_id\":" + weapon.DefIndex +
            ",\"name\":\"中文·幽灵\"},\"paint_index\":\"999999\",\"pattern\":{\"name\":\"中文·幽灵\"}}]";
        File.WriteAllText(zhPath, unknown);

        var index = CatalogIndex.Load(root);
        Assert.True(index.TryGetWeapon(weapon.DefIndex, out var def));
        Assert.Null(def.Paints.Values.SingleOrDefault(p => p.PaintIndex == 999999));
    }

    [Fact]
    public void Search_MatchesChineseEnglishAndNumericIds()
    {
        var index = CatalogIndex.Load(CreateLocaleCache());
        var weapon = index.GetOrdinaryWeapons().First(w => w.Paints.Count > 0);

        Assert.Contains(index.SearchOrdinaryWeapons("中文·"), w => w.DefIndex == weapon.DefIndex);
        Assert.Contains(index.SearchOrdinaryWeapons(weapon.Name[..4]), w => w.DefIndex == weapon.DefIndex);
        Assert.Contains(index.SearchOrdinaryWeapons(weapon.DefIndex.ToString()), w => w.DefIndex == weapon.DefIndex);
        Assert.Empty(index.SearchOrdinaryWeapons("不存在的东西xyz"));

        var localized = weapon.Paints.Values.First(p => p.ChineseName is not null);
        Assert.Contains(index.SearchPaints(weapon.DefIndex, localized.ChineseName), p => p.PaintIndex == localized.PaintIndex);
        Assert.Contains(index.SearchPaints(weapon.DefIndex, localized.PaintIndex.ToString()), p => p.PaintIndex == localized.PaintIndex);
        Assert.Contains(index.SearchPaints(weapon.DefIndex, localized.Name[..4]), p => p.PaintIndex == localized.PaintIndex);
    }

    [Fact]
    public void LegacyEnglishOnlyLayout_StillLoads()
    {
        var index = CatalogIndex.Load(TestFixtures.CatalogDir);

        Assert.False(index.HasChinese);
        Assert.Equal(new[] { CatalogSnapshot.IdentityLocale }, index.LocalesLoaded.ToList());
        Assert.True(index.WeaponCount > 0);
        Assert.NotNull(CatalogSnapshot.ResolveLocaleDir(TestFixtures.CatalogDir, CatalogSnapshot.IdentityLocale));
        Assert.Null(CatalogSnapshot.ResolveLocaleDir(TestFixtures.CatalogDir, CatalogSnapshot.ChineseLocale));
    }

    [Fact]
    public void Validation_IsIdenticalWithAndWithoutChinese()
    {
        var preset = TestFixtures.ExamplePreset();
        var en = CatalogIndex.Load(CreateLocaleCache(withChinese: false));
        var zh = CatalogIndex.Load(CreateLocaleCache());

        Assert.Equal(HumanPresetValidator.Validate(preset, en).Count, HumanPresetValidator.Validate(preset, zh).Count);
    }

    [Fact]
    public void Presentation_FallsBackToEnglishWithoutEmptyStrings()
    {
        Assert.Equal("AK-47", CatalogDisplay.Primary(null, "AK-47"));
        Assert.Equal("AK-47", CatalogDisplay.Primary("", "AK-47"));
        Assert.Equal("卡拉什尼科夫", CatalogDisplay.Primary("卡拉什尼科夫", "AK-47"));

        // The secondary line exists only to carry the other language.
        Assert.Equal("", CatalogDisplay.Secondary("AK-47", "AK-47"));
        Assert.Equal("", CatalogDisplay.Secondary(null, "AK-47"));
        Assert.Equal("AK-47", CatalogDisplay.Secondary("红线", "AK-47"));

        Assert.Equal("崭新出厂", CatalogDisplay.WearBucket(0.01));
        Assert.Equal("战痕累累", CatalogDisplay.WearBucket(0.99));
    }

    [Fact]
    public async Task Sync_WritesBothLocalesFromPinnedCommit_AndIndexLoads()
    {
        var root = Path.Combine(_work, "sync");
        var fetcher = new FixtureFetcher();

        var report = await new CatalogSyncService(root, fetcher).SyncAsync();

        Assert.True(report.Ok(CatalogSnapshot.IdentityLocale), report.FailedMessages);
        Assert.True(report.Ok(CatalogSnapshot.ChineseLocale), report.FailedMessages);
        Assert.Contains(CatalogSnapshot.PinnedCommit, fetcher.Urls.First());
        Assert.All(fetcher.Urls, url => Assert.Contains(CatalogSnapshot.PinnedCommit, url));
        Assert.Contains(fetcher.Urls, u => u.Contains("/" + CatalogSnapshot.IdentityLocale + "/"));
        Assert.Contains(fetcher.Urls, u => u.Contains("/" + CatalogSnapshot.ChineseLocale + "/"));
        Assert.True(File.Exists(CatalogSnapshot.CachePath(root, CatalogSnapshot.ChineseLocale, "skins.json")));

        var index = CatalogIndex.Load(root);
        Assert.True(index.HasChinese);
    }

    [Fact]
    public async Task Sync_FailureIsReportedNotThrown()
    {
        var root = Path.Combine(_work, "sync-fail");

        var report = await new CatalogSyncService(root, new ThrowingFetcher()).SyncAsync();

        Assert.False(report.Ok(CatalogSnapshot.IdentityLocale));
        Assert.NotEmpty(report.Messages);
        Assert.False(report.IdentityReady);
        Assert.Throws<CatalogCacheException>(() => CatalogSnapshot.LoadCachedIndex(root));
    }

    [Fact]
    public void Inspect_ReportsMissingLocales()
    {
        var root = CreateLocaleCache(withChinese: false);
        var status = CatalogSyncService.Inspect(root);

        Assert.Contains(CatalogSnapshot.IdentityLocale, status.LocalesPresent);
        Assert.Contains(CatalogSnapshot.ChineseLocale, status.LocalesMissing);
    }

    // --- Music kits: a kit and its StatTrak™ variant are two upstream records over one def_index. ---

    [Fact]
    public void MusicKits_LocalizedStatTrakVariant_CannotRelabelThePlainKit()
    {
        var index = CatalogIndex.Load(CreateMusicVariantCache(plainFirst: true));

        Assert.True(index.TryGetMusic(78, out var kit));
        Assert.Equal("Music Kit | Test Kit", kit.Name);
        Assert.Equal("音乐盒 | 测试包", kit.ChineseName);

        // Image and rarity follow the same record the canonical name came from.
        Assert.Equal("https://example.invalid/plain.png", kit.ImageUrl);
        Assert.Equal("rarity_common", kit.RarityId);
        Assert.Equal("普通", kit.RarityChineseName);
    }

    [Fact]
    public void MusicKits_CanonicalVariant_IsIndependentOfSnapshotOrder()
    {
        var forward = CatalogIndex.Load(CreateMusicVariantCache(plainFirst: true));
        var reversed = CatalogIndex.Load(CreateMusicVariantCache(plainFirst: false));

        Assert.True(forward.TryGetMusic(78, out var plainFirstKit));
        Assert.True(reversed.TryGetMusic(78, out var statFirstKit));
        Assert.Equal(plainFirstKit, statFirstKit);
        Assert.Equal(forward.MusicKitCount, reversed.MusicKitCount);
        Assert.Equal(1, reversed.MusicKitCount);
    }

    [Fact]
    public void MusicKits_DuplicateDefIndex_NeverBecomesASecondKit()
    {
        var enOnly = CatalogIndex.Load(CreateMusicVariantCache(plainFirst: true, withChinese: false));
        var bilingual = CatalogIndex.Load(CreateMusicVariantCache(plainFirst: true));

        Assert.Equal(enOnly.MusicKitCount, bilingual.MusicKitCount);
        Assert.Single(bilingual.GetMusicKits(), m => m.Id == 78);
        Assert.Single(bilingual.SearchMusicKits("Test Kit"));
        Assert.Single(bilingual.SearchMusicKits("音乐盒"));
        Assert.Single(bilingual.SearchMusicKits("78"));
        Assert.True(bilingual.LocalizedMusicKitCount > 0);
    }

    [Fact]
    public void MusicKits_PinnedFixtureShape_StaysOnThePlainVariant()
    {
        // The checked-in snapshot carries music_kit-78 and music_kit-78_st over def_index 78.
        var index = CatalogIndex.Load(CreateLocaleCache());

        Assert.True(index.TryGetMusic(78, out var kit));
        Assert.Equal("Music Kit | Austin Wintory, The Devil Went Clubbing In Georgia", kit.Name);
        Assert.DoesNotContain("StatTrak", kit.Name);
        Assert.Equal("中文·" + kit.Name, kit.ChineseName);
        Assert.DoesNotContain("StatTrak", kit.ChineseName);
    }

    /// <summary>
    /// A cache whose music file holds both variants of one def_index, each carrying metadata that
    /// identifies which record won. Upstream stable ids stay English in both locales, as upstream.
    /// </summary>
    private string CreateMusicVariantCache(bool plainFirst, bool withChinese = true)
    {
        var root = CreateLocaleCache(withChinese: withChinese, withMusic: false);

        File.WriteAllText(
            CatalogSnapshot.CachePath(root, CatalogSnapshot.IdentityLocale, "music_kits.json"),
            MusicKitPairJson(plainFirst, english: true));

        if (withChinese)
            File.WriteAllText(
                CatalogSnapshot.CachePath(root, CatalogSnapshot.ChineseLocale, "music_kits.json"),
                MusicKitPairJson(plainFirst, english: false));

        return root;
    }

    private static string MusicKitPairJson(bool plainFirst, bool english)
    {
        static string Kit(string id, string name, string image, string rarityId, string rarityName) =>
            "{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"def_index\":\"78\"" +
            ",\"image\":\"" + image + "\"" +
            ",\"rarity\":{\"id\":\"" + rarityId + "\",\"name\":\"" + rarityName + "\",\"color\":\"#4b69ff\"}}";

        var plain = english
            ? Kit("music_kit-78", "Music Kit | Test Kit", "https://example.invalid/plain.png", "rarity_common", "Common")
            : Kit("music_kit-78", "音乐盒 | 测试包", "https://example.invalid/plain.png", "rarity_common", "普通");
        var statTrak = english
            ? Kit("music_kit-78_st", "StatTrak™ Music Kit | Test Kit", "https://example.invalid/stat.png", "rarity_mythical", "Mythical")
            : Kit("music_kit-78_st", "StatTrak™ 音乐盒 | 测试包", "https://example.invalid/stat.png", "rarity_mythical", "隐秘");

        return "[" + (plainFirst ? plain + "," + statTrak : statTrak + "," + plain) + "]";
    }

    private sealed class FixtureFetcher : ICatalogFetcher
    {
        public List<string> Urls { get; } = new();

        public Task<byte[]> FetchAsync(string url, CancellationToken ct)
        {
            Urls.Add(url);
            var file = url.Contains("music_kits") ? "music_kits.json" : "skins.json";
            return Task.FromResult(File.ReadAllBytes(TestFixtures.CatalogPath(file)));
        }
    }

    private sealed class ThrowingFetcher : ICatalogFetcher
    {
        public Task<byte[]> FetchAsync(string url, CancellationToken ct)
            => Task.FromException<byte[]>(new HttpRequestException("simulated offline"));
    }
}
