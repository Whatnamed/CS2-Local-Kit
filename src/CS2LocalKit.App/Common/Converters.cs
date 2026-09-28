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
