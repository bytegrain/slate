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
