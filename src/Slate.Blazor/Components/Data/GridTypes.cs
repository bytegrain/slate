using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Slate.Data;

namespace Slate.Blazor;

/// <summary>
/// A column of <see cref="SlDataGrid{T}"/>: the engine's <see cref="GridColumn{T}"/> plus renderer-only options
/// (the web grid's <c>DataGridColumn</c>). Declare columns with <see cref="SlGridColumn{T}"/> children, or pass a list
/// to <see cref="SlDataGrid{T}.ColumnDefinitions"/> (a plain <see cref="GridColumn{T}"/> converts implicitly).
/// </summary>
public sealed class DataGridColumn<T>(GridColumn<T> column)
{
    public GridColumn<T> Column { get; } = column;

    public string Field => Column.Field;

    /// <summary>Label of a header group spanning consecutive columns with the same value (multi-row headers).</summary>
    public string? HeaderGroup { get; init; }

    /// <summary>Longer description shown as the header's tooltip.</summary>
    public string? Description { get; init; }

    /// <summary>Row actions for <see cref="GridColumnType.Actions"/> columns (rendered as an SlMenu).</summary>
    public IReadOnlyList<GridAction<T>>? Actions { get; set; }

    /// <summary>Values offered by the enum filter (default: EnumOrder, else distinct values from the data).</summary>
    public IReadOnlyList<string>? FilterOptions { get; init; }

    /// <summary>Editor override (defaults by type: text, number, date, checkbox, select for enums).</summary>
    public GridEditorKind? Editor { get; init; }

    /// <summary>Custom cell content (overrides the type's default rendering).</summary>
    public RenderFragment<GridCellContext<T>>? Template { get; set; }

    public static implicit operator DataGridColumn<T>(GridColumn<T> column) => new(column);
}

/// <summary>Everything a cell template receives.</summary>
public sealed record GridCellContext<T>(T Item, object? Value, string Text, GridColumn<T> Column, string RowKey, int RowIndex);

/// <summary>A row action of an actions column.</summary>
public sealed record GridAction<T>(string Label)
{
    public string? Icon { get; init; }
    public Tone? Tone { get; init; }
    public string? Shortcut { get; init; }
    public bool Separator { get; init; }
    public Func<T, bool>? Disabled { get; init; }
    public Func<T, Task>? Run { get; init; }

    public static GridAction<T> SeparatorItem { get; } = new("") { Separator = true };
}

public enum GridEditorKind { Text, Number, Date, Select, Checkbox }

public enum GridExportFormat { Csv, Tsv }

public enum GridExportScope { Visible, Selected, All }

/// <summary>Selection change: engine keys (RowKey text), the known items, the count (including "all matching").</summary>
public sealed record GridSelectionChangedEventArgs<T>(IReadOnlyList<string> Keys, IReadOnlyList<T> Items, int Count, bool AllMatching);

/// <summary>Raised after Ctrl+C with the TSV that was written to the clipboard.</summary>
public sealed record GridCopyEventArgs(string Text, int Rows);

/// <summary>Raised before an export downloads; set <see cref="Cancel"/> to handle the text yourself.</summary>
public sealed class GridExportEventArgs(GridExportFormat format, GridExportScope scope, string text)
{
    public GridExportFormat Format { get; } = format;
    public GridExportScope Scope { get; } = scope;
    public string Text { get; } = text;
    public bool Cancel { get; set; }
}

/// <summary>Presentation helpers shared by the grid's cells, editors and filter popovers (ports of data-grid-cells.ts).</summary>
internal static class GridCells
{
    public enum FilterKind { Text, Number, Date, Boolean, Enum }

    public sealed class FilterDraft
    {
        public required string Field { get; init; }
        public FilterOperator Operator { get; set; }
        public string Value { get; set; } = "";
        public string Value2 { get; set; } = "";
        public List<string> Values { get; set; } = [];
    }

    public static FilterKind KindOf(GridColumnType type) => type switch
    {
        GridColumnType.Number or GridColumnType.Progress => FilterKind.Number,
        GridColumnType.Date => FilterKind.Date,
        GridColumnType.Boolean => FilterKind.Boolean,
        GridColumnType.Enum => FilterKind.Enum,
        _ => FilterKind.Text,
    };

    public static readonly IReadOnlyDictionary<FilterKind, (FilterOperator Value, string Label)[]> Operators = new Dictionary<FilterKind, (FilterOperator, string)[]>
    {
        [FilterKind.Text] =
        [
            (FilterOperator.Contains, "Contains"), (FilterOperator.Equals, "Equals"), (FilterOperator.NotEquals, "Does not equal"),
            (FilterOperator.StartsWith, "Starts with"), (FilterOperator.EndsWith, "Ends with"), (FilterOperator.IsEmpty, "Is empty"),
            (FilterOperator.IsNotEmpty, "Is not empty"),
        ],
        [FilterKind.Number] =
        [
            (FilterOperator.Equals, "="), (FilterOperator.NotEquals, "≠"), (FilterOperator.LessThan, "<"), (FilterOperator.LessThanOrEqual, "≤"),
            (FilterOperator.GreaterThan, ">"), (FilterOperator.GreaterThanOrEqual, "≥"), (FilterOperator.Between, "Between"), (FilterOperator.IsEmpty, "Is empty"),
        ],
        [FilterKind.Date] =
        [
            (FilterOperator.Equals, "On"), (FilterOperator.LessThan, "Before"), (FilterOperator.GreaterThan, "After"),
            (FilterOperator.Between, "Between"), (FilterOperator.IsEmpty, "Is empty"),
        ],
        [FilterKind.Boolean] = [(FilterOperator.Equals, "Is")],
        [FilterKind.Enum] = [(FilterOperator.AnyOf, "Is any of"), (FilterOperator.IsEmpty, "Is empty")],
    };

    public static string OperatorLabel(FilterOperator op)
    {
        foreach (var list in Operators.Values)
            foreach (var (value, label) in list)
                if (value == op) return label;
        return op.ToString();
    }

    public static bool NeedsValue(FilterOperator op) => op is not FilterOperator.IsEmpty and not FilterOperator.IsNotEmpty;

    public static string FilterText(object? v) => DataPipeline<object>.FilterText(v);

    public static FilterDraft DraftFrom(string field, GridColumnType type, GridFilter? active) => new()
    {
        Field = field,
        Operator = active?.Operator ?? Operators[KindOf(type)][0].Value,
        Value = FilterText(active?.Value),
        Value2 = FilterText(active?.Value2),
        Values = (active?.Values ?? []).Select(FilterText).ToList(),
    };

    /// <summary>The engine filter for a draft, or null when it filters nothing (empty value).</summary>
    public static GridFilter? FilterFromDraft(GridColumnType type, FilterDraft d)
    {
        var kind = KindOf(type);
        if (!NeedsValue(d.Operator)) return new GridFilter(d.Field, d.Operator);
        if (d.Operator == FilterOperator.AnyOf)
            return d.Values.Count > 0 ? new GridFilter(d.Field, FilterOperator.AnyOf, Values: d.Values.Cast<object?>().ToList()) : null;
        object? Coerce(string s) => kind switch
        {
            FilterKind.Number => GridValues.ToDouble(s.Length == 0 ? null : s) is { } n ? n : null,
            FilterKind.Boolean => s == "true" ? true : s == "false" ? false : null,
            _ => s,
        };
        var value = Coerce(d.Value.Trim());
        if (value is null || value is "") return null;
        var filter = new GridFilter(d.Field, d.Operator, value);
        if (d.Operator == FilterOperator.Between)
        {
            var v2 = Coerce(d.Value2.Trim());
            return v2 is null || v2 is "" ? filter : filter with { Value2 = v2 };
        }
        return filter;
    }

    /// <summary>Short human text for a filter chip: "Status: Ready, Failed".</summary>
    public static string Describe(string title, GridColumnType type, GridFilter f)
    {
        if (f.Operator is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty) return $"{title} {OperatorLabel(f.Operator).ToLowerInvariant()}";
        if (f.Operator == FilterOperator.AnyOf) return $"{title}: {string.Join(", ", (f.Values ?? []).Select(FilterText))}";
        if (f.Operator == FilterOperator.Between) return $"{title}: {FilterText(f.Value)} – {FilterText(f.Value2)}";
        if (type == GridColumnType.Boolean) return $"{title} is {(f.Value is true ? "Yes" : "No")}";
        var op = OperatorLabel(f.Operator);
        var value = FilterText(f.Value);
        return char.IsAsciiLetter(op[0]) ? $"{title} {op.ToLowerInvariant()} {value}" : $"{title} {op} {value}";
    }

    public static string AggregateLabel(GridAggregate a) => a switch
    {
        GridAggregate.Sum => "Σ",
        GridAggregate.Avg => "avg",
        GridAggregate.Min => "min",
        GridAggregate.Max => "max",
        GridAggregate.Count => "count",
        _ => "",
    };

    public static string AggregateText<T>(GridColumn<T> column, GridAggregate aggregate, object? value)
    {
        if (value is null) return "—";
        if (aggregate == GridAggregate.Count) return GridValues.Format(value, GridColumnType.Number, "#,##0");
        if (aggregate == GridAggregate.Avg && value is double d && column.Format is null) return column.DisplayText(Math.Round(d * 100, MidpointRounding.AwayFromZero) / 100);
        return column.DisplayText(value);
    }

    public static GridEditorKind EditorKind(GridColumnType type, GridEditorKind? overrideKind) => overrideKind ?? type switch
    {
        GridColumnType.Number or GridColumnType.Progress => GridEditorKind.Number,
        GridColumnType.Date => GridEditorKind.Date,
        GridColumnType.Boolean => GridEditorKind.Checkbox,
        GridColumnType.Enum => GridEditorKind.Select,
        _ => GridEditorKind.Text,
    };

    /// <summary>Editor text for a value (what the user edits; the engine parses it back).</summary>
    public static string EditorText<T>(GridColumn<T> column, GridEditorKind kind, object? value)
    {
        if (value is null) return "";
        if (kind == GridEditorKind.Date)
        {
            var text = GridValues.ToEpochMs(value) is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return text.Length > 10 ? text[..10] : text;
        }
        if (kind == GridEditorKind.Number) return GridValues.ToDouble(value) is { } d ? d.ToString("R", CultureInfo.InvariantCulture) : "";
        return column.DisplayText(value);
    }

    public static string Px(double value) => value.ToString(CultureInfo.InvariantCulture) + "px";

    public static string Number(int value) => value.ToString("#,##0", CultureInfo.InvariantCulture);

    /// <summary>Values of a sparkline cell.</summary>
    public static IReadOnlyList<double> Series(object? value) => value switch
    {
        string => [],
        IEnumerable e => e.Cast<object?>().Select(v => GridValues.ToDouble(v) ?? 0).ToList(),
        _ => [],
    };

    // ---- auto setter: editable plain-field columns write back to the same property ----

    public static Action<T, object?>? AutoSetter<T>(string field)
    {
        if (typeof(IDictionary<string, object?>).IsAssignableFrom(typeof(T)))
            return (item, value) => { if (item is IDictionary<string, object?> d) d[field] = value; };
        var prop = typeof(T).GetProperty(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop is null || !prop.CanWrite) return null;
        return (item, value) => prop.SetValue(item, ConvertTo(value, prop.PropertyType));
    }

    public static object? ConvertTo(object? value, Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var target = underlying ?? type;
        if (value is null) return type.IsValueType && underlying is null ? Activator.CreateInstance(type) : null;
        if (target.IsInstanceOfType(value)) return value;
        if (value is DateTimeOffset dto)
        {
            if (target == typeof(DateTime)) return dto.UtcDateTime;
            if (target == typeof(DateOnly)) return DateOnly.FromDateTime(dto.UtcDateTime);
        }
        if (target.IsEnum) return value is string s ? Enum.Parse(target, s, ignoreCase: true) : Enum.ToObject(target, Convert.ToInt64(value, CultureInfo.InvariantCulture));
        if (target == typeof(string)) return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (value is double d && (target == typeof(int) || target == typeof(long) || target == typeof(short)))
            return Convert.ChangeType(Math.Round(d, MidpointRounding.AwayFromZero), target, CultureInfo.InvariantCulture);
        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    /// <summary>A copy of an engine column with a setter (GridColumn is init-only).</summary>
    public static GridColumn<T> WithSetter<T>(GridColumn<T> c, Action<T, object?> setter) => new()
    {
        Field = c.Field, Title = c.Title, Type = c.Type, Accessor = c.Accessor, Setter = setter, Width = c.Width, MinWidth = c.MinWidth,
        MaxWidth = c.MaxWidth, Flex = c.Flex, Sortable = c.Sortable, Filterable = c.Filterable, Resizable = c.Resizable,
        Reorderable = c.Reorderable, Hideable = c.Hideable, Searchable = c.Searchable, Pinned = c.Pinned, Hidden = c.Hidden,
        Align = c.Align, Format = c.Format, Formatter = c.Formatter, Aggregate = c.Aggregate, Editable = c.Editable,
        Validate = c.Validate, Parser = c.Parser, Comparer = c.Comparer, EnumOrder = c.EnumOrder, EnumTones = c.EnumTones,
    };

    public static GridKey? KeyFrom(string key, bool mod) => key switch
    {
        "ArrowUp" => GridKey.Up,
        "ArrowDown" => GridKey.Down,
        "ArrowLeft" => GridKey.Left,
        "ArrowRight" => GridKey.Right,
        "Home" => mod ? GridKey.CtrlHome : GridKey.Home,
        "End" => mod ? GridKey.CtrlEnd : GridKey.End,
        "PageUp" => GridKey.PageUp,
        "PageDown" => GridKey.PageDown,
        "+" => GridKey.Plus,
        "-" => GridKey.Minus,
        _ => null,
    };
}
