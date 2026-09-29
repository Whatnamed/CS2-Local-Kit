using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CS2LocalKit.Core.Runtime;

namespace CS2LocalKit.App.Common;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !(value is true);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !(value is true);
    }
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value != null;
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HealthLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(34, 197, 94));
    private static readonly SolidColorBrush AmberBrush = new(Color.FromRgb(245, 158, 11));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(239, 68, 68));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RuntimeHealthLevel level)
        {
            return level switch
            {
                RuntimeHealthLevel.Ready => GreenBrush,
                RuntimeHealthLevel.Attention => AmberBrush,
                RuntimeHealthLevel.Blocked => RedBrush,
                _ => AmberBrush,
            };
        }
        return AmberBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class HealthLevelToBadgeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RuntimeHealthLevel level)
        {
            return level switch
            {
                RuntimeHealthLevel.Ready => "环境正常",
                RuntimeHealthLevel.Attention => "注意",
                RuntimeHealthLevel.Blocked => "存在阻断",
                _ => "未知",
            };
        }
        return "未知";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DoubleFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return d.ToString("F4", CultureInfo.InvariantCulture);
        }
        return "0.0000";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return Math.Clamp(d, 0.0, 1.0);
        }
        return 0.0;
    }
}

/// <summary>Shows content only when the bound string has something in it.</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visibility when a value equals the converter parameter (used for section/tab state).</summary>
public sealed class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Boolean to a readable yes/no label, so facts stay sentences rather than raw values.</summary>
public sealed class TrueFalseTextConverter : IValueConverter
{
    public string TrueText { get; set; } = "是";
    public string FalseText { get; set; } = "否";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueText : FalseText;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Turns a catalog rarity hex color into a brush. Unparseable or missing colors fall back to the
/// neutral border brush so a presentation value can never break the layout.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Brush Fallback = new SolidColorBrush(Color.FromRgb(0x36, 0x3D, 0x49));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex)) return Fallback;
        try
        {
            var parsed = (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(parsed);
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            return Fallback;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
