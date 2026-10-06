using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Slate.Data;

// Contract enums (GridSelectionMode, GridColumnType, SortDirection, GridPin, GridAggregate, GridPagination)
// live in the root Slate namespace (Theming/Configuration.cs). Engine-only enums are defined here.

/// <summary>How cells are edited: not at all, committed per cell, or collected and committed as a batch.</summary>
public enum GridEditMode { None, Cell, Batch }

/// <summary>Column filter operators. Text operators are case-insensitive.</summary>
public enum FilterOperator
{
    Contains,
    Equals,
    NotEquals,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    /// <summary>Inclusive range between <c>Value</c> and <c>Value2</c> (either order).</summary>
    Between,
    /// <summary>Display text equals any of <c>Values</c>.</summary>
    AnyOf,
}

/// <summary>Horizontal alignment of a column's cells.</summary>
public enum GridAlign { Start, Center, End }

/// <summary>
/// A column definition. Only <see cref="Field"/> is required; <see cref="Accessor"/> defaults to a public
/// property named <see cref="Field"/> (case-insensitive) on <typeparamref name="T"/>.
/// </summary>
public sealed class GridColumn<T>
{
    private static readonly ConcurrentDictionary<string, PropertyInfo?> Properties = new(StringComparer.OrdinalIgnoreCase);

    public required string Field { get; init; }
    public string? Title { get; init; }
    public GridColumnType Type { get; init; } = GridColumnType.Text;

    /// <summary>Reads the cell value. Defaults to the property named <see cref="Field"/>.</summary>
    public Func<T, object?>? Accessor { get; init; }

    /// <summary>Writes an edited value back (required for editing).</summary>
    public Action<T, object?>? Setter { get; init; }

    /// <summary>Fixed width in px. Null with <see cref="Flex"/> &gt; 0 = flexible; null otherwise = 150.</summary>
    public double? Width { get; init; }
    public double MinWidth { get; init; } = 48;
    public double MaxWidth { get; init; } = 2000;

    /// <summary>Share of leftover width when the column has no explicit width.</summary>
    public double Flex { get; init; }

    public bool Sortable { get; init; } = true;
    public bool Filterable { get; init; } = true;
    public bool Resizable { get; init; } = true;
    public bool Reorderable { get; init; } = true;
    public bool Hideable { get; init; } = true;

    /// <summary>Included in the quick filter.</summary>
    public bool Searchable { get; init; } = true;

    public GridPin Pinned { get; init; } = GridPin.None;
    public bool Hidden { get; init; }

    /// <summary>Null = end for numbers/progress, start otherwise.</summary>
    public GridAlign? Align { get; init; }

    /// <summary>Portable format pattern (see <see cref="GridValues.Format"/>): e.g. "#,##0.00", "0%", "yyyy-MM-dd HH:mm".</summary>
    public string? Format { get; init; }

    /// <summary>Custom display text; overrides <see cref="Format"/>.</summary>
    public Func<object?, string>? Formatter { get; init; }

    public GridAggregate Aggregate { get; init; } = GridAggregate.None;
    public bool Editable { get; init; }

    /// <summary>Returns an error message for an invalid value, or null when valid.</summary>
    public Func<object?, string?>? Validate { get; init; }

    /// <summary>Parses editor text into a value; throw <see cref="FormatException"/> with a user-facing message to reject.</summary>
    public Func<string, object?>? Parser { get; init; }

    /// <summary>Custom ordering for non-null values (sorting and grouping).</summary>
    public IComparer<object?>? Comparer { get; init; }

    /// <summary>Enum columns: value order for sorting/grouping (values not listed sort after, by text).</summary>
    public IReadOnlyList<string>? EnumOrder { get; init; }

    /// <summary>Enum columns: badge tone per value.</summary>
    public IReadOnlyDictionary<string, Tone>? EnumTones { get; init; }

    public string DisplayTitle => Title ?? Field;

    public GridAlign EffectiveAlign => Align ?? (Type is GridColumnType.Number or GridColumnType.Progress ? GridAlign.End : GridAlign.Start);

    public object? GetValue(T item)
    {
        if (Accessor is not null) return Accessor(item);
        if (item is null) return null;
        if (item is IReadOnlyDictionary<string, object?> dict) return dict.TryGetValue(Field, out var v) ? v : null;
        var prop = Properties.GetOrAdd($"{typeof(T).FullName}.{Field}", _ =>
            typeof(T).GetProperty(Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
        return prop?.GetValue(item);
    }

    /// <summary>The text shown in a cell (also used by the quick filter, text filters, grouping and export).</summary>
    public string DisplayText(object? value) => Formatter is not null ? Formatter(value) : GridValues.Format(value, Type, Format);

    /// <summary>Parses editor text with <see cref="Parser"/> or the type's default parser.</summary>
    public object? Parse(string text) => Parser is not null ? Parser(text) : GridValues.Parse(text, Type, EnumOrder);

    internal int CompareNonNull(object a, object b)
    {
        if (Comparer is not null) return Comparer.Compare(a, b);
        if (Type == GridColumnType.Enum && EnumOrder is { Count: > 0 } order)
        {
            int ia = IndexOf(order, DisplayText(a)), ib = IndexOf(order, DisplayText(b));
            if (ia != ib) return ia.CompareTo(ib);
        }
        return GridValues.Compare(GridValues.Normalize(a, Type), GridValues.Normalize(b, Type));
    }

    private static int IndexOf(IReadOnlyList<string> order, string text)
    {
        for (var i = 0; i < order.Count; i++)
            if (string.Equals(order[i], text, StringComparison.OrdinalIgnoreCase)) return i;
        return int.MaxValue;
    }
}

/// <summary>
/// Value semantics shared by every Slate platform (the TypeScript port mirrors these exactly):
/// normalisation, ordering, portable formatting and parsing.
/// </summary>
public static class GridValues
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>
    /// A comparable primitive: numbers/progress → double, dates → UTC epoch milliseconds (double), booleans → bool,
    /// everything else → string. Null stays null.
    /// </summary>
    public static object? Normalize(object? value, GridColumnType type)
    {
        if (value is null) return null;
        switch (type)
        {
            case GridColumnType.Number or GridColumnType.Progress:
                return ToDouble(value);
            case GridColumnType.Date:
                return ToEpochMs(value);
            case GridColumnType.Boolean:
                return ToBool(value);
            default:
                return value is string s ? s : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }

    /// <summary>Orders two normalised non-null values: numbers numerically, false &lt; true, text case-insensitively then ordinally.</summary>
    public static int Compare(object? a, object? b)
    {
        switch (a, b)
        {
            case (double x, double y): return x.CompareTo(y);
            case (bool x, bool y): return x.CompareTo(y);
            case (string x, string y):
                var c = string.CompareOrdinal(x.ToLowerInvariant(), y.ToLowerInvariant());
                return c != 0 ? Math.Sign(c) : Math.Sign(string.CompareOrdinal(x, y));
            case (null, null): return 0;
            case (null, _): return 1;
            case (_, null): return -1;
            default:
                return Math.Sign(string.CompareOrdinal(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture)));
        }
    }

    public static double? ToDouble(object? value) => value switch
    {
        null => null,
        double d => d,
        float f => f,
        decimal m => (double)m,
        int or long or short or byte or uint or ulong or ushort or sbyte => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
        bool b => b ? 1 : 0,
        _ => null,
    };

    public static double? ToEpochMs(object? value) => value switch
    {
        null => null,
        DateTimeOffset o => o.ToUnixTimeMilliseconds(),
        DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        DateOnly d => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds(),
        string s => ParseIsoDate(s) is { } p ? p.ToUnixTimeMilliseconds() : null,
        double d => d,
        long l => l,
        int i => i,
        _ => null,
    };

    public static bool? ToBool(object? value) => value switch
    {
        null => null,
        bool b => b,
        string s => s.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => null,
        },
        _ => ToDouble(value) is { } d ? d != 0 : null,
    };

    /// <summary>Parses "yyyy-MM-dd" or an ISO date-time; values without an offset are UTC.</summary>
    public static DateTimeOffset? ParseIsoDate(string text) =>
        DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    /// <summary>
    /// Portable display formatting (identical in C# and TypeScript).
    /// Numbers: default shortest round-trip; patterns "0", "0.00", "#,##0", "#,##0.0", "0%", "0.0%".
    /// Dates (UTC): default "yyyy-MM-dd", or "yyyy-MM-dd HH:mm" when the time isn't midnight; tokens yyyy MMM MM dd HH mm ss.
    /// Booleans: "true"/"false". Progress: like numbers. Null: "".
    /// </summary>
    public static string Format(object? value, GridColumnType type, string? pattern = null)
    {
        if (value is null) return "";
        switch (type)
        {
            case GridColumnType.Number or GridColumnType.Progress:
                return ToDouble(value) is { } d ? FormatNumber(d, pattern) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            case GridColumnType.Date:
                return ToEpochMs(value) is { } ms ? FormatDate(ms, pattern) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            case GridColumnType.Boolean:
                return ToBool(value) is { } b ? (b ? "true" : "false") : "";
            default:
                return value is string s ? s : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }
    }

    public static string FormatNumber(double d, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return d.ToString("R", CultureInfo.InvariantCulture);
        var percent = pattern.EndsWith('%');
        var core = percent ? pattern[..^1] : pattern;
        var dot = core.IndexOf('.');
        var decimals = dot < 0 ? 0 : core.Length - dot - 1;
        var grouping = core.Contains(',');
        var x = percent ? d * 100 : d;
        var fixedText = x.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        if (grouping) fixedText = Group(fixedText);
        return percent ? fixedText + "%" : fixedText;
    }

    private static string Group(string fixedText)
    {
        var negative = fixedText.StartsWith('-');
        var body = negative ? fixedText[1..] : fixedText;
        var dot = body.IndexOf('.');
        var intPart = dot < 0 ? body : body[..dot];
        var frac = dot < 0 ? "" : body[dot..];
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0) sb.Append(',');
            sb.Append(intPart[i]);
        }
        return (negative ? "-" : "") + sb + frac;
    }

    public static string FormatDate(double epochMs, string? pattern)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(epochMs)).UtcDateTime;
        pattern ??= d.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm";
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            if (Starts("yyyy")) { sb.Append(d.Year.ToString("0000", CultureInfo.InvariantCulture)); i += 4; }
            else if (Starts("MMM")) { sb.Append(Months[d.Month - 1]); i += 3; }
            else if (Starts("MM")) { sb.Append(d.Month.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (Starts("dd")) { sb.Append(d.Day.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (Starts("HH")) { sb.Append(d.Hour.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (Starts("mm")) { sb.Append(d.Minute.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else if (Starts("ss")) { sb.Append(d.Second.ToString("00", CultureInfo.InvariantCulture)); i += 2; }
            else { sb.Append(pattern[i]); i++; }
        }
        return sb.ToString();

        bool Starts(string token) => string.CompareOrdinal(pattern, i, token, 0, token.Length) == 0;
    }

    /// <summary>Default editor parsing per type. Throws <see cref="FormatException"/> with a user-facing message.</summary>
    public static object? Parse(string text, GridColumnType type, IReadOnlyList<string>? enumOrder = null)
    {
        var t = text.Trim();
        if (t.Length == 0) return null;
        switch (type)
        {
            case GridColumnType.Number or GridColumnType.Progress:
                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                    ? d : throw new FormatException("Enter a number.");
            case GridColumnType.Date:
                return ParseIsoDate(t) ?? throw new FormatException("Enter a date (YYYY-MM-DD).");
            case GridColumnType.Boolean:
                return ToBool(t) ?? throw new FormatException("Enter true or false.");
            case GridColumnType.Enum when enumOrder is { Count: > 0 }:
                return enumOrder.FirstOrDefault(v => string.Equals(v, t, StringComparison.OrdinalIgnoreCase))
                       ?? throw new FormatException($"Choose one of: {string.Join(", ", enumOrder)}.");
            default:
                return text;
        }
    }

    internal static string KeyText(object? key) => key switch
    {
        null => "",
        string s => s,
        _ => Convert.ToString(key, CultureInfo.InvariantCulture) ?? "",
    };
}
