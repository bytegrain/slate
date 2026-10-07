using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Slate.Data;

namespace Slate.Wpf;

/// <summary>A row action for <see cref="GridColumnType.Actions"/> columns.</summary>
public sealed record GridRowAction(string Label, Action Invoke, string? Icon = null, Tone Tone = Tone.Neutral, string? Shortcut = null);

/// <summary>
/// A DataGrid column, declared in XAML (<c>&lt;sl:GridColumn Field="Status" Type="Enum" EnumTones="Ready=Success, Failed=Danger" /&gt;</c>)
/// or code. Mirrors the engine's <see cref="GridColumn{T}"/>; <see cref="Field"/> is a property path on the row item
/// (dots allowed) unless <see cref="Accessor"/> is supplied.
/// </summary>
public class GridColumn
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Props = new();
    private GridColumn<object>? _engine;

    public string Field { get; set; } = "";
    public string? Title { get; set; }
    public GridColumnType Type { get; set; } = GridColumnType.Text;

    /// <summary>Fixed width (px). Leave unset and set <see cref="Flex"/> for a flexible column.</summary>
    public double? Width { get; set; }
    public double MinWidth { get; set; } = 48;
    public double MaxWidth { get; set; } = 2000;
    public double Flex { get; set; }

    public bool Sortable { get; set; } = true;
    public bool Filterable { get; set; } = true;
    public bool Resizable { get; set; } = true;
    public bool Reorderable { get; set; } = true;
    public bool Hideable { get; set; } = true;
    public bool Searchable { get; set; } = true;
    public GridPin Pinned { get; set; } = GridPin.None;
    public bool Hidden { get; set; }
    public GridAlign? Align { get; set; }

    /// <summary>Portable format pattern: "#,##0.00", "0%", "yyyy-MM-dd HH:mm"…</summary>
    public string? Format { get; set; }

    public GridAggregate Aggregate { get; set; } = GridAggregate.None;

    /// <summary>Cells can be edited (requires the grid's EditMode and a writable property or <see cref="Setter"/>).</summary>
    public bool Editable { get; set; }

    /// <summary>Enum columns: value order, comma separated ("Queued, Building, Ready, Failed").</summary>
    public string? EnumOrder { get; set; }

    /// <summary>Enum columns: badge tone per value, e.g. "Ready=Success, Failed=Danger, Building=Warning".</summary>
    public string? EnumTones { get; set; }

    /// <summary>Custom cell content (<see cref="GridColumnType.Custom"/>, or overrides the type's default rendering). DataContext = row item.</summary>
    public DataTemplate? Template { get; set; }

    /// <summary>Row actions for <see cref="GridColumnType.Actions"/> columns (code), shown in the row's actions menu.</summary>
    public Func<object, IEnumerable<GridRowAction>>? Actions { get; set; }

    /// <summary>XAML alternative to <see cref="Actions"/>: a menu whose DataContext is the row item when opened.</summary>
    public ContextMenu? ActionsMenu { get; set; }

    // ---- code-only hooks ----

    public Func<object, object?>? Accessor { get; set; }
    public Action<object, object?>? Setter { get; set; }
    public Func<object?, string>? Formatter { get; set; }
    public Func<string, object?>? Parser { get; set; }

    /// <summary>Returns an error message for an invalid value (or null).</summary>
    public Func<object?, string?>? Validate { get; set; }

    public IComparer<object?>? Comparer { get; set; }

    /// <summary>Per-row tone for this column's cells (status colouring).</summary>
    public Func<object, Tone?>? CellTone { get; set; }

    public string DisplayTitle => Title ?? Field;

    /// <summary>The engine column (rebuilt by <see cref="Invalidate"/>).</summary>
    public GridColumn<object> Engine => _engine ??= Build();

    public void Invalidate() => _engine = null;

    public IReadOnlyDictionary<string, Tone>? ParsedTones => ParseTones(EnumTones);

    private GridColumn<object> Build() => new()
    {
        Field = Field,
        Title = Title,
        Type = Type,
        Accessor = Accessor ?? (item => GetPath(item, Field)),
        Setter = Setter ?? (CanWrite() ? (item, value) => SetPath(item, Field, value) : null),
        Width = Width,
        MinWidth = MinWidth,
        MaxWidth = MaxWidth,
        Flex = Flex,
        Sortable = Sortable,
        Filterable = Filterable,
        Resizable = Resizable,
        Reorderable = Reorderable,
        Hideable = Hideable,
        Searchable = Searchable,
        Pinned = Pinned,
        Hidden = Hidden,
        Align = Align,
        Format = Format,
        Formatter = Formatter,
        Parser = Parser,
        Aggregate = Aggregate,
        Editable = Editable,
        Validate = Validate,
        Comparer = Comparer,
        EnumOrder = EnumOrder?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        EnumTones = ParsedTones,
    };

    private bool CanWrite() => Editable && !Field.Contains('.');

    internal static IReadOnlyDictionary<string, Tone>? ParseTones(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var map = new Dictionary<string, Tone>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && Enum.TryParse<Tone>(pair[(eq + 1)..].Trim(), true, out var tone))
                map[pair[..eq].Trim()] = tone;
        }
        return map;
    }

    /// <summary>Reads a dotted property path (dictionaries and DataRowView-style indexers supported).</summary>
    public static object? GetPath(object? item, string path)
    {
        foreach (var part in path.Split('.'))
        {
            if (item is null) return null;
            item = item switch
            {
                IDictionary<string, object?> d => d.TryGetValue(part, out var v) ? v : null,
                IDictionary d => d.Contains(part) ? d[part] : null,
                ICustomTypeDescriptor td => td.GetProperties().Find(part, true)?.GetValue(item),
                _ => Prop(item.GetType(), part)?.GetValue(item),
            };
        }
        return item;
    }

    public static void SetPath(object item, string path, object? value)
    {
        if (item is IDictionary<string, object?> d)
        {
            d[path] = value;
            return;
        }
        var prop = Prop(item.GetType(), path);
        if (prop is null || !prop.CanWrite) return;
        prop.SetValue(item, Coerce(value, prop.PropertyType));
    }

    private static PropertyInfo? Prop(Type type, string name) =>
        Props.GetOrAdd((type, name), k => k.Item1.GetProperty(k.Item2, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

    private static object? Coerce(object? value, Type target)
    {
        if (value is null) return null;
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t.IsInstanceOfType(value)) return value;
        if (t.IsEnum) return value is string s ? Enum.Parse(t, s, true) : Enum.ToObject(t, value);
        if (t == typeof(DateTime) && value is DateTimeOffset dto) return dto.UtcDateTime;
        if (t == typeof(DateTimeOffset) && value is DateTime dt) return new DateTimeOffset(dt);
        if (value is IConvertible) return Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
        return value;
    }
}
