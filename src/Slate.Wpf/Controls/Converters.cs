using System.Globalization;
using System.Windows.Data;
using Slate.Dialogs;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf;

/// <summary>True when the value is not null (and not an empty string).</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public static IsNotNullConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && value is not "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True for dialog content that lays out its own padding (DialogContent).</summary>
public sealed class IsDialogContentConverter : IValueConverter
{
    public static IsDialogContentConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is DialogContent or MessageBoxOptions;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>px → Thickness(px, 0) — horizontal padding from a padding token. A numeric parameter is added to px.</summary>
public sealed class HorizontalThicknessConverter : IValueConverter
{
    public static HorizontalThicknessConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double px) return new System.Windows.Thickness(0);
        if (parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            px = Math.Max(0, px + offset);
        return new System.Windows.Thickness(px, 0, px, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Keeps only the top (parameter "Top") or bottom ("Bottom", default) corners of a CornerRadius, minus 1px for the border.</summary>
public sealed class CornerPartConverter : IValueConverter
{
    public static CornerPartConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not System.Windows.CornerRadius r) return new System.Windows.CornerRadius(0);
        static double In(double v) => Math.Max(0, v - 1);
        return parameter as string == "Top"
            ? new System.Windows.CornerRadius(In(r.TopLeft), In(r.TopRight), 0, 0)
            : new System.Windows.CornerRadius(0, 0, In(r.BottomRight), In(r.BottomLeft));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Multiplies all numeric inputs (e.g. switch thumb position × travel).</summary>
public sealed class MultiplyConverter : IMultiValueConverter
{
    public static MultiplyConverter Instance { get; } = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Aggregate(1.0, (acc, v) => acc * (v is double d && !double.IsNaN(d) ? d : 0));

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>(value, maximum) → "64%".</summary>
public sealed class PercentConverter : IMultiValueConverter
{
    public static PercentConverter Instance { get; } = new();

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [double value, double max, ..] && max > 0 ? $"{Math.Round(value / max * 100)}%" : "";

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Grows every corner of a CornerRadius by the numeric parameter (focus rings drawn outside a control).</summary>
public sealed class CornerInflateConverter : IValueConverter
{
    public static CornerInflateConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var by = parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var r = value is System.Windows.CornerRadius c ? c : new System.Windows.CornerRadius(0);
        static double Grow(double x, double d) => x <= 0 ? 0 : x + d;
        return new System.Windows.CornerRadius(Grow(r.TopLeft, by), Grow(r.TopRight, by), Grow(r.BottomRight, by), Grow(r.BottomLeft, by));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A number → Thickness(n, 0, 0, 0) (tree indentation).</summary>
public sealed class LeftThicknessConverter : IValueConverter
{
    public static LeftThicknessConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new System.Windows.Thickness(value is double d && !double.IsNaN(d) ? d : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
