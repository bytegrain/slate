using Avalonia.Controls.Templates;

using Slate.Data;

namespace Slate.Avalonia.Controls;

/// <summary>
/// A <see cref="DataGrid"/> column, declared in XAML (<c>&lt;sl:GridColumn Field="Status" Type="Enum" /&gt;</c>) or code.
/// Everything maps 1:1 onto the Slate.Core engine's <see cref="GridColumn{T}"/>; the grid builds the engine column
/// when its columns change. Values are read from the row by <see cref="Accessor"/>, else by property name
/// (<see cref="Field"/>), else from dictionaries.
/// </summary>
public class GridColumn
{
    /// <summary>Property name (or dictionary key) the column reads; also its identity in <see cref="GridState"/>.</summary>
    public string Field { get; set; } = "";
    public string? Title { get; set; }
    public GridColumnType Type { get; set; } = GridColumnType.Text;

    /// <summary>Fixed width in px; null = flex or the default width.</summary>
    public double? Width { get; set; }
    public double MinWidth { get; set; } = 48;
    public double MaxWidth { get; set; } = 2000;

    /// <summary>Share of the remaining width (like CSS flex-grow); 0 = fixed.</summary>
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

    /// <summary>Portable number/date pattern (see GridValues): numbers <c>0</c>, <c>0.00</c>, <c>#,##0.0</c>, <c>0%</c>; dates <c>yyyy-MM-dd HH:mm</c>.</summary>
    public string? Format { get; set; }

    public GridAggregate Aggregate { get; set; } = GridAggregate.None;
    public bool Editable { get; set; }

    /// <summary>Enum columns: value order (for sorting) as a comma-separated list, e.g. "Queued,Building,Ready,Failed".</summary>
    public string? EnumOrder { get; set; }

    /// <summary>Enum columns: badge tone per value, e.g. "Ready:Success, Failed:Danger, Building:Info".</summary>
    public string? EnumTones { get; set; }

    /// <summary>Custom cell content (Type=Custom, or any type). Its DataContext is the row item.</summary>
    public IDataTemplate? Template { get; set; }

    public Func<object, object?>? Accessor { get; set; }
    public Action<object, object?>? Setter { get; set; }
    public Func<object?, string>? Formatter { get; set; }
    public Func<string, object?>? Parser { get; set; }

    /// <summary>Returns an error message for an invalid edit, or null.</summary>
    public Func<object?, string?>? Validate { get; set; }

    /// <summary>Per-cell tone (tints the cell), e.g. red for failed builds.</summary>
    public Func<object, Tone?>? CellTone { get; set; }

    /// <summary>Actions columns: menu items built for a row when its "more" button opens.</summary>
    public Func<object, IEnumerable<GridRowAction>>? Actions { get; set; }

    public string DisplayTitle => Title ?? Field;

    internal GridColumn<object> ToEngine() => new()
    {
        Field = Field,
        Title = Title,
        Type = Type,
        // The engine column is GridColumn<object>, so its own reflection fallback would look at System.Object.
        Accessor = Accessor ?? (item => GridColumnAccess.Read(item, Field)),
        Setter = Setter ?? (Editable ? (item, value) => GridColumnAccess.Write(item, Field, value) : null),
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
        Aggregate = Aggregate,
        Editable = Editable,
        Validate = Validate,
        Parser = Parser,
        EnumOrder = SplitList(EnumOrder),
        EnumTones = ParseTones(EnumTones),
    };

    internal static IReadOnlyList<string>? SplitList(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static IReadOnlyDictionary<string, Tone>? ParseTones(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var map = new Dictionary<string, Tone>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && Enum.TryParse<Tone>(parts[1], ignoreCase: true, out var tone))
                map[parts[0]] = tone;
        }
        return map;
    }
}

/// <summary>A row action shown in an Actions column's menu.</summary>
public sealed record GridRowAction(string Label, Action Invoke, string? Icon = null, Tone Tone = Tone.Neutral, string? Shortcut = null);

/// <summary>Raised when a row is activated (Enter or double-click).</summary>
public sealed class GridRowEventArgs(global::Avalonia.Interactivity.RoutedEvent routedEvent, object item, string key)
    : global::Avalonia.Interactivity.RoutedEventArgs(routedEvent)
{
    public object Item { get; } = item;
    public string Key { get; } = key;
}

/// <summary>Raised when an edit is committed (cell mode: immediately; batch mode: per change on Commit).</summary>
public sealed class GridCellEditEventArgs(global::Avalonia.Interactivity.RoutedEvent routedEvent, GridCellChange<object> change)
    : global::Avalonia.Interactivity.RoutedEventArgs(routedEvent)
{
    public GridCellChange<object> Change { get; } = change;
}
