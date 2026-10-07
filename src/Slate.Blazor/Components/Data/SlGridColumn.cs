using Microsoft.AspNetCore.Components;
using Slate.Data;

namespace Slate.Blazor;

/// <summary>
/// Declares a column of the enclosing <see cref="SlDataGrid{T}"/> (renders nothing):
/// <c>&lt;SlGridColumn T="Deployment" Field="Cost" Title="Cost ($)" Type="GridColumnType.Number" Format="#,##0.00" Aggregate="GridAggregate.Sum" /&gt;</c>.
/// Delegates and templates are read when used, so re-rendering the page with new lambda instances doesn't rebuild the
/// grid's pipeline; only changes to the plain options do.
/// </summary>
public sealed class SlGridColumn<T> : SlComponentBase, IDisposable
{
    [CascadingParameter] private SlDataGrid<T>? Grid { get; set; }

    [Parameter, EditorRequired] public string Field { get; set; } = "";
    [Parameter] public string? Title { get; set; }
    [Parameter] public GridColumnType Type { get; set; } = GridColumnType.Text;

    /// <summary>Reads the value (default: the property named <see cref="Field"/>).</summary>
    [Parameter] public Func<T, object?>? Accessor { get; set; }

    /// <summary>Writes an edited value (editable columns without an accessor write the property by default).</summary>
    [Parameter] public Action<T, object?>? Setter { get; set; }

    [Parameter] public double? Width { get; set; }
    [Parameter] public double MinWidth { get; set; } = 48;
    [Parameter] public double MaxWidth { get; set; } = 2000;
    [Parameter] public double Flex { get; set; }
    [Parameter] public bool Sortable { get; set; } = true;
    [Parameter] public bool Filterable { get; set; } = true;
    [Parameter] public bool Resizable { get; set; } = true;
    [Parameter] public bool Reorderable { get; set; } = true;
    [Parameter] public bool Hideable { get; set; } = true;
    [Parameter] public bool Searchable { get; set; } = true;
    [Parameter] public GridPin Pinned { get; set; }
    [Parameter] public bool Hidden { get; set; }
    [Parameter] public GridAlign? Align { get; set; }
    [Parameter] public string? Format { get; set; }
    [Parameter] public Func<object?, string>? Formatter { get; set; }
    [Parameter] public GridAggregate Aggregate { get; set; }
    [Parameter] public bool Editable { get; set; }

    /// <summary>Returns an error message for an invalid value, or null.</summary>
    [Parameter] public Func<object?, string?>? Validate { get; set; }

    [Parameter] public Func<string, object?>? Parser { get; set; }
    [Parameter] public IComparer<object?>? Comparer { get; set; }
    [Parameter] public IReadOnlyList<string>? EnumOrder { get; set; }
    [Parameter] public IReadOnlyDictionary<string, Tone>? EnumTones { get; set; }

    /// <summary>Header group label (consecutive columns with the same label share a header cell above).</summary>
    [Parameter] public string? HeaderGroup { get; set; }

    /// <summary>Header tooltip.</summary>
    [Parameter] public string? Description { get; set; }

    [Parameter] public IReadOnlyList<GridAction<T>>? Actions { get; set; }
    [Parameter] public IReadOnlyList<string>? FilterOptions { get; set; }
    [Parameter] public GridEditorKind? Editor { get; set; }

    /// <summary>Custom cell content.</summary>
    [Parameter] public RenderFragment<GridCellContext<T>>? Template { get; set; }

    /// <summary>The definition the grid uses; replaced only when a plain option changes.</summary>
    internal DataGridColumn<T>? Definition { get; private set; }

    private object[]? _signature;

    protected override void OnParametersSet()
    {
        if (Grid is null) throw new InvalidOperationException($"{nameof(SlGridColumn<T>)} must be placed inside an SlDataGrid's Columns.");
        object[] signature =
        [
            Field, Title ?? "\0", Type, Width ?? -1, MinWidth, MaxWidth, Flex, Sortable, Filterable, Resizable, Reorderable, Hideable, Searchable,
            Pinned, Hidden, Align?.ToString() ?? "", Format ?? "\0", Aggregate, Editable, HeaderGroup ?? "\0", Description ?? "\0", Editor?.ToString() ?? "",
            Accessor is null, Setter is null, Formatter is null, Validate is null, Parser is null, Template is null, (object?)Comparer ?? "",
            EnumOrder is null ? "\0" : string.Join('\u001f', EnumOrder), EnumTones is null ? "\0" : string.Join('\u001f', EnumTones.Select(p => $"{p.Key}={p.Value}")),
            FilterOptions is null ? "\0" : string.Join('\u001f', FilterOptions),
        ];
        if (Definition is not null && _signature is not null && signature.SequenceEqual(_signature))
        {
            Definition.Actions = Actions;
            return;
        }
        _signature = signature;
        Definition = new DataGridColumn<T>(new GridColumn<T>
        {
            Field = Field, Title = Title, Type = Type, Width = Width, MinWidth = MinWidth, MaxWidth = MaxWidth, Flex = Flex,
            Sortable = Sortable, Filterable = Filterable, Resizable = Resizable, Reorderable = Reorderable, Hideable = Hideable,
            Searchable = Searchable, Pinned = Pinned, Hidden = Hidden, Align = Align, Format = Format, Aggregate = Aggregate,
            Editable = Editable, Comparer = Comparer, EnumOrder = EnumOrder, EnumTones = EnumTones,
            // Forwarders: always call the latest delegate, so new lambda instances need no rebuild.
            Accessor = Accessor is null ? null : item => Accessor!(item),
            Setter = Setter is null ? null : (item, value) => Setter!(item, value),
            Formatter = Formatter is null ? null : value => Formatter!(value),
            Validate = Validate is null ? null : value => Validate!(value),
            Parser = Parser is null ? null : text => Parser!(text),
        })
        {
            HeaderGroup = HeaderGroup, Description = Description, FilterOptions = FilterOptions, Editor = Editor, Actions = Actions,
            Template = Template is null ? null : ctx => Template!(ctx),
        };
        Grid.RegisterColumn(this);
    }

    public void Dispose() => Grid?.UnregisterColumn(this);
}
