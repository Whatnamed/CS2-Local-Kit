using System.Windows.Media;
using CS2LocalKit.App.Common;
using CS2LocalKit.Core.Catalog;

namespace CS2LocalKit.App.Catalog;

/// <summary>
/// Catalog display rules for a Chinese-first UI: Chinese name when the localized snapshot has
/// one, English otherwise, never an empty string. All of this is presentation only - presets and
/// validation keep using defIndex / paintIndex / musicKitId.
/// </summary>
public static class CatalogDisplay
{
    public const string ThumbnailSize = "232x176";
    public const string DetailSize = "512x384";

    /// <summary>Primary display name: Chinese when available, English as fallback.</summary>
    public static string Primary(string? chinese, string english)
        => string.IsNullOrWhiteSpace(chinese) ? english : chinese;

    /// <summary>Secondary line: the other language, or empty when it would repeat the primary.</summary>
    public static string Secondary(string? chinese, string english)
    {
        if (string.IsNullOrWhiteSpace(chinese)) return "";
        if (string.Equals(chinese, english, StringComparison.Ordinal)) return "";
        return english;
    }

    /// <summary>
    /// The finish name without the weapon prefix: "红线" for "AK-47 | 红线". Uses the upstream
    /// pattern name when present, otherwise the text after the last separator.
    /// </summary>
    public static string FinishPrimary(CatalogPaint paint)
    {
        var pattern = paint.PatternChineseName;
        var full = StripPrefix(paint.ChineseName) ?? StripPrefix(paint.Name);
        return !string.IsNullOrWhiteSpace(pattern) ? pattern
            : !string.IsNullOrWhiteSpace(full) ? full
            : paint.Name;
    }

    public static string FinishSecondary(CatalogPaint paint)
    {
        var pattern = paint.PatternName;
        var full = StripPrefix(paint.Name);
        var primary = FinishPrimary(paint);
        var english = !string.IsNullOrWhiteSpace(pattern) ? pattern : full ?? paint.Name;
        return string.Equals(english, primary, StringComparison.Ordinal) ? "" : english;
    }

    private static string? StripPrefix(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var index = fullName.LastIndexOf('|');
        if (index < 0) return fullName.Trim();
        var tail = fullName[(index + 1)..].Trim();
        return tail.Length == 0 ? null : tail;
    }

    /// <summary>
    /// Steam serves economy art at a requested size when it is appended to the path, which keeps a
    /// thumbnail grid cheap. Non-economy URLs (for example the image-tracker PNGs) are used as-is.
    /// </summary>
    public static string? ThumbnailUrl(string? image) => Sized(image, ThumbnailSize);
    public static string? DetailUrl(string? image) => Sized(image, DetailSize);

    private static string? Sized(string? image, string size)
    {
        if (string.IsNullOrWhiteSpace(image)) return null;
        if (!image.Contains("/economy/image/", StringComparison.OrdinalIgnoreCase)) return image;
        return image.TrimEnd('/') + "/" + size;
    }

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var brush = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return brush;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Wear bucket label for a float value, using the game's own ranges.</summary>
    public static string WearBucket(double wear) => wear switch
    {
        < 0.07 => "崭新出厂",
        < 0.15 => "略有磨损",
        < 0.38 => "久经沙场",
        < 0.45 => "破损不堪",
        _ => "战痕累累",
    };

    public static string WearBucketRange(double wear) => wear switch
    {
        < 0.07 => "0.00 - 0.07",
        < 0.15 => "0.07 - 0.15",
        < 0.38 => "0.15 - 0.38",
        < 0.45 => "0.38 - 0.45",
        _ => "0.45 - 1.00",
    };

    private static readonly Dictionary<string, (string Label, int Order)> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csgo_inventory_weapon_category_rifles"] = ("步枪", 1),
        ["csgo_inventory_weapon_category_pistols"] = ("手枪", 2),
        ["csgo_inventory_weapon_category_smgs"] = ("微型冲锋枪", 3),
        ["csgo_inventory_weapon_category_heavy"] = ("重型武器", 4),
        ["sfui_invpanel_filter_melee"] = ("近战武器", 5),
        ["sfui_invpanel_filter_gloves"] = ("手套", 6),
    };

    public static string CategoryLabel(string categoryId)
        => Categories.TryGetValue(categoryId, out var hit) ? hit.Label : "其他";

    public static int CategoryOrder(string categoryId)
        => Categories.TryGetValue(categoryId, out var hit) ? hit.Order : 90;
}

/// <summary>A weapon / knife model / glove model row in the left browser column.</summary>
public sealed class CatalogItemViewModel : ViewModelBase
{
    private bool _isConfigured;
    private bool _isCurrent;
    private string? _summary;
    private string? _imageUrl;

    public int DefIndex { get; }
    public string ChineseName { get; }
    public string EnglishName { get; }
    public string CategoryId { get; }
    public string DisplayName { get; }
    public string SecondaryName { get; }
    public string CategoryLabel { get; }
    public int CategoryOrder { get; }
    public string SortKey { get; }

    public bool IsConfigured
    {
        get => _isConfigured;
        set => SetProperty(ref _isConfigured, value);
    }

    /// <summary>Knife only: this model is the team's currently selected knife.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>Chosen finish name shown under the weapon name once configured.</summary>
    public string? Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    /// <summary>Thumbnail of the configured finish, so the list reads visually.</summary>
    public string? ImageUrl
    {
        get => _imageUrl;
        set => SetProperty(ref _imageUrl, value);
    }

    public CatalogItemViewModel(WeaponDef def, string? paintSummary = null, string? paintImageUrl = null, bool configured = false)
    {
        DefIndex = def.DefIndex;
        EnglishName = def.Name;
        ChineseName = def.ChineseName ?? "";
        DisplayName = CatalogDisplay.Primary(def.ChineseName, def.Name);
        SecondaryName = CatalogDisplay.Secondary(def.ChineseName, def.Name);
        CategoryId = def.CategoryId;
        CategoryLabel = CatalogDisplay.CategoryLabel(def.CategoryId);
        CategoryOrder = CatalogDisplay.CategoryOrder(def.CategoryId);
        SortKey = DisplayName;
        _summary = paintSummary;
        _imageUrl = paintImageUrl;
        _isConfigured = configured;
    }
}

/// <summary>
/// One selectable finish in the visual grid. IsSelected is presentation state only: the card is
/// highlighted because it matches the draft, and a user click reaches the editor through a command.
/// </summary>
public sealed class SkinCardViewModel : ViewModelBase
{
    private bool _isSelected;

    public int PaintIndex { get; }
    public string DisplayName { get; }
    public string SecondaryName { get; }
    public string FullEnglishName { get; }
    public string? ThumbnailUrl { get; }
    public string? DetailUrl { get; }
    public string? RarityColor { get; }
    public string RarityLabel { get; }
    public string PaintIndexText { get; }
    public string FloatRangeText { get; }
    public CatalogPaint Paint { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SkinCardViewModel(CatalogPaint paint)
    {
        Paint = paint;
        PaintIndex = paint.PaintIndex;
        DisplayName = CatalogDisplay.FinishPrimary(paint);
        SecondaryName = CatalogDisplay.FinishSecondary(paint);
        FullEnglishName = paint.Name;
        ThumbnailUrl = CatalogDisplay.ThumbnailUrl(paint.ImageUrl);
        DetailUrl = CatalogDisplay.DetailUrl(paint.ImageUrl);
        RarityColor = paint.RarityColor;
        RarityLabel = CatalogDisplay.Primary(paint.RarityChineseName, paint.RarityName ?? "");
        PaintIndexText = "#" + paint.PaintIndex.ToString();
        FloatRangeText = paint.MinFloat is { } min && paint.MaxFloat is { } max
            ? $"出厂 {min:0.###} - 战痕 {max:0.###}"
            : "";
    }
}

/// <summary>One music kit card. Music kits are global, so the card carries no team state.</summary>
public sealed class MusicKitCardViewModel : ViewModelBase
{
    private bool _isSelected;

    public int Id { get; }
    public string DisplayName { get; }
    public string SecondaryName { get; }
    public string? ThumbnailUrl { get; }
    public string? DetailUrl { get; }
    public string? RarityColor { get; }
    public CatalogMusicKit Kit { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public MusicKitCardViewModel(CatalogMusicKit kit)
    {
        Kit = kit;
        Id = kit.Id;
        DisplayName = CatalogDisplay.Primary(kit.ChineseName, kit.Name);
        SecondaryName = CatalogDisplay.Secondary(kit.ChineseName, kit.Name);
        ThumbnailUrl = CatalogDisplay.ThumbnailUrl(kit.ImageUrl);
        DetailUrl = CatalogDisplay.DetailUrl(kit.ImageUrl);
        RarityColor = kit.RarityColor;
    }
}

/// <summary>
/// A row of cards. The grid virtualizes rows with the standard VirtualizingStackPanel, so only the
/// rows that scroll into view create card elements and request their art.
/// </summary>
public sealed class CardRowViewModel<T>
{
    public CardRowViewModel(IReadOnlyList<T> cards) => Cards = cards;

    public IReadOnlyList<T> Cards { get; }
}
