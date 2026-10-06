using System.Globalization;
using Avalonia.Data.Converters;

namespace Slate.Avalonia;

/// <summary>Small converters used by Slate templates.</summary>
public static class SlateConverters
{
    /// <summary>Multiplies all numeric inputs (e.g. a 0–1 fraction × a width).</summary>
    public static readonly IMultiValueConverter Multiply = new FuncMultiValueConverter<object?, double>(values =>
        values.Aggregate(1.0, (acc, v) => v is IConvertible c ? acc * c.ToDouble(CultureInfo.InvariantCulture) : 0));
}
