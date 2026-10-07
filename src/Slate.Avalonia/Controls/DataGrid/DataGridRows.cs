using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Slate.Data;

namespace Slate.Avalonia.Controls;

/// <summary>
/// The scrolling body of a <see cref="DataGrid"/>: its desired size is the whole dataset (so the ScrollViewer gets the
/// right extent) but only the rows in the viewport (+ overscan) exist as children, recycled while scrolling.
/// </summary>
public class DataGridRowsPresenter : Panel
{
    private readonly Stack<DataGridRow> _pool = new();
    private readonly Dictionary<int, DataGridRow> _realized = new();
    private int _first = -1, _last = -1;
    private double[]? _tops; // prefix offsets when detail rows make heights uneven
    private int _rowCount;

    internal DataGrid? Owner { get; set; }

    /// <summary>Number of realised (live) row containers.</summary>
    public int RealizedCount => _realized.Count;

    /// <summary>View indices of the realised rows, in order.</summary>
    public IReadOnlyList<int> RealizedIndices => _realized.Keys.Order().ToList();

    internal double RowHeight => Owner?.RowHeight ?? 32;

    internal double HeightOf(DataGridRowInfo info) =>
        info.Kind == GridRowKind.Detail ? RowHeight * Math.Max(1, Owner!.DetailRowSpan) : RowHeight;

    internal double TopOf(int index) => _tops is { } t ? t[index] : index * RowHeight;

    internal double TotalHeight => _tops is { } t ? t[^1] : _rowCount * RowHeight;

    /// <summary>Creates, recycles and re-binds rows for the current scroll position.</summary>
    internal void Realize(bool force)
    {
        if (Owner is not { } grid) return;
        var scroll = grid.ScrollViewer;
        var count = grid.ViewRowCount;

        if (force || count != _rowCount)
        {
            _rowCount = count;
            _tops = null;
            if (!grid.IsServerMode && grid.Result is { } r && r.Rows.Any(x => x.Kind == GridRowKind.Detail))
            {
                var tops = new double[count + 1];
                for (var i = 0; i < count; i++)
                    tops[i + 1] = tops[i] + (r.Rows[i].Kind == GridRowKind.Detail ? RowHeight * Math.Max(1, grid.DetailRowSpan) : RowHeight);
                _tops = tops;
            }
        }

        var viewportHeight = scroll?.Viewport.Height is > 0 and var h ? h : Math.Max(Bounds.Height, 400);
        var scrollTop = scroll?.Offset.Y ?? 0;
        int first, last;
        if (_tops is { } tops2)
        {
            first = Math.Max(0, LowerBound(tops2, scrollTop) - 1 - 6);
            last = Math.Min(count, LowerBound(tops2, scrollTop + viewportHeight) + 6);
        }
        else
        {
            var range = GridViewport.Compute(scrollTop, viewportHeight, RowHeight, count);
            first = range.First;
            last = range.Last;
        }

        if (force || first != _first || last != _last)
        {
            foreach (var index in _realized.Keys.Where(i => i < first || i >= last).ToList())
            {
                var row = _realized[index];
                _realized.Remove(index);
                row.IsVisible = false;
                _pool.Push(row);
            }

            for (var i = first; i < last; i++)
            {
                if (_realized.ContainsKey(i)) continue;
                var row = _pool.Count > 0 ? _pool.Pop() : CreateRow();
                row.IsVisible = true;
                _realized[i] = row;
            }
            _first = first;
            _last = last;
            grid.EnsureLoaded(first, last - first);
        }

        foreach (var (index, row) in _realized)
            row.Bind(grid.RowAt(index), force);

        InvalidateMeasure();
        InvalidateArrange();
    }

    /// <summary>Horizontal scroll: pinned cells move, column virtualisation may add or drop cells.</summary>
    internal void OnHorizontalScroll()
    {
        foreach (var row in _realized.Values)
        {
            row.SyncCells();
            row.InvalidateArrange();
        }
    }

    internal DataGridRow? RowFor(int index) => _realized.GetValueOrDefault(index);

    internal void FocusEditor()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var row in _realized.Values)
                if (row.EditorControl is { } editor) { editor.Focus(); if (editor is TextBox tb) tb.SelectAll(); }
        }, DispatcherPriority.Input);
    }

    private DataGridRow CreateRow()
    {
        var row = new DataGridRow(Owner!);
        Children.Add(row);
        return row;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Max(Owner?.ContentWidth ?? 0, 0);
        foreach (var (index, row) in _realized)
            row.Measure(new Size(width, HeightOf(row.Info)));
        return new Size(width, TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, row) in _realized)
            row.Arrange(new Rect(0, TopOf(index), finalSize.Width, HeightOf(row.Info)));
        foreach (var row in _pool)
            row.Arrange(new Rect(0, -10000, 0, 0));
        return finalSize;
    }

    private static int LowerBound(double[] tops, double y)
    {
        int lo = 0, hi = tops.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (tops[mid] < y) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

/// <summary>One rendered grid row (data, group, detail or skeleton). Recycled by <see cref="DataGridRowsPresenter"/>.</summary>
public class DataGridRow : Panel
{
    private readonly DataGrid _grid;
    private readonly Dictionary<string, DataGridCell> _cells = new(StringComparer.Ordinal);
    private CheckBox? _select;
    private Button? _detailToggle;
    private DataGridGroupHeader? _group;
    private ContentControl? _detail;
    private Border? _indicator;
    private bool _hover;
    private IBrush? _tone;

    internal DataGridRow(DataGrid grid)
    {
        _grid = grid;
        ClipToBounds = true;
        Classes.Add("sl-grid-row");
        PointerPressed += OnPointerPressed;
        PointerEntered += (_, _) => { _hover = true; UpdateOverlays(); };
        PointerExited += (_, _) => { _hover = false; UpdateOverlays(); };
        DoubleTapped += (_, e) =>
        {
            if (Info.Kind != GridRowKind.Data || Info.Item is null) return;
            var col = ColumnAt(e.GetPosition(this).X);
            if (col >= 0 && _grid.EditMode != GridEditMode.None && _grid.BeginEdit(new GridCell(Info.Index, col))) return;
            _grid.Activate(Info);
        };
    }

    internal DataGridRowInfo Info { get; private set; }

    public bool IsSelected { get; private set; }

    /// <summary>View index of the row this container currently shows.</summary>
    public int ViewIndex => Info.Index;

    public object? Item => Info.Item;

    /// <summary>The cells currently realised for this row, by field (column virtualisation keeps only visible ones).</summary>
    public IReadOnlyDictionary<string, DataGridCell> Cells => _cells;

    internal Control? EditorControl => _cells.Values.Select(c => c.Editor).FirstOrDefault(e => e is not null);

    internal void Bind(DataGridRowInfo info, bool force)
    {
        var kindChanged = Info.Kind != info.Kind || Info.IsSkeleton != info.IsSkeleton;
        Info = info;
        IsSelected = info.Kind == GridRowKind.Data && !info.IsSkeleton && _grid.Selection.IsSelected(info.Key);

        Classes.Set("selected", IsSelected);
        Classes.Set("group", info.Kind == GridRowKind.Group);
        Classes.Set("detail", info.Kind == GridRowKind.Detail);
        Classes.Set("skeleton", info.IsSkeleton);
        Classes.Set("odd", info.Index % 2 == 1);
        Classes.Set("flash", info.Kind == GridRowKind.Data && _grid.IsFlashing(info.Key));

        var tone = info.Item is not null && info.Kind == GridRowKind.Data ? _grid.RowTone?.Invoke(info.Item) : null;
        // Local Background only for a tone; otherwise the theme's hover/selected states apply.
        _tone = IsSelected ? null : RowBackground(tone);
        if (_tone is not null) Background = _tone;
        else ClearValue(BackgroundProperty);

        AutomationProperties.SetName(this, info.Kind switch
        {
            GridRowKind.Group => $"{info.Row?.GroupKeyText}, {info.Row?.RowCount} rows",
            _ => $"Row {info.Index + 1}",
        });

        if (kindChanged || force)
        {
            Children.Clear();
            _cells.Clear();
            _select = null;
            _detailToggle = null;
            _group = null;
            _detail = null;
            _indicator = null;
        }

        switch (info.Kind)
        {
            case GridRowKind.Group:
                _group ??= Add(new DataGridGroupHeader(_grid));
                _group.Bind(info);
                break;
            case GridRowKind.Detail:
                _detail ??= Add(new ContentControl { Classes = { "sl-grid-detail" } });
                _detail.ContentTemplate = _grid.RowDetail;
                _detail.Content = info.Item;
                break;
            default:
                SyncLeadCells();
                SyncCells();
                foreach (var cell in _cells.Values)
                    cell.Bind(info);
                break;
        }

        _indicator ??= Add(new Border { Classes = { "sl-grid-selection-indicator" }, IsHitTestVisible = false });
        _indicator.IsVisible = IsSelected;
        _indicator.Background = _grid.Resource("Sl.Component.Grid.SelectionIndicator.Brush") as IBrush;
        UpdateOverlays();
        InvalidateArrange();
    }

    /// <summary>
    /// Pinned cells are opaque (scrolled cells must not show through), so they paint the row's state tint themselves.
    /// </summary>
    private void UpdateOverlays()
    {
        var key = Classes.Contains("flash") ? "Sl.Brush.Accent.Subtle"
            : IsSelected ? "Sl.Component.Grid.RowSelected.Brush"
            : _hover && Info.Kind == GridRowKind.Data ? "Sl.Component.Grid.RowHover.Brush"
            : null;
        var brush = key is null ? _tone : _grid.Resource(key) as IBrush;
        foreach (var cell in _cells.Values)
            cell.RowOverlay = cell.Classes.Contains("pinned") ? brush : null;
    }

    private IBrush? RowBackground(Tone? tone)
    {
        if (tone is { } t && t != Tone.Neutral && _grid.Resource(ToneKey(t)) is IBrush tint)
            return tint;
        return null;
    }

    private string ToneKey(Tone t) => t switch
    {
        Tone.Accent => "Sl.Brush.Accent.Subtle",
        Tone.Success => "Sl.Brush.Status.Success.Bg",
        Tone.Warning => "Sl.Brush.Status.Warning.Bg",
        Tone.Danger => "Sl.Brush.Status.Danger.Bg",
        _ => "Sl.Brush.Status.Info.Bg",
    };

    private void SyncLeadCells()
    {
        if (_grid.SelectionMode == GridSelectionMode.Multi && !Info.IsSkeleton)
        {
            _select ??= Add(new CheckBox { Classes = { "sl-grid-select" }, Focusable = false, MinWidth = 0, Padding = new Thickness(0) });
            _select.IsCheckedChanged -= OnSelectChanged;
            _select.IsChecked = IsSelected;
            AutomationProperties.SetName(_select, $"Select row {Info.Index + 1}");
            _select.IsCheckedChanged += OnSelectChanged;
        }

        if (_grid.RowDetail is not null && !Info.IsSkeleton)
        {
            _detailToggle ??= Add(new Button { Classes = { "ghost", "sl-grid-expander" }, Focusable = false });
            var expanded = _grid.State.ExpandedDetails.Contains(Info.Key);
            _detailToggle.Content = new Icon { Kind = expanded ? "chevron-down" : "chevron-right", Size = 14 };
            AutomationProperties.SetName(_detailToggle, expanded ? "Hide details" : "Show details");
            _detailToggle.Click -= OnDetailToggle;
            _detailToggle.Click += OnDetailToggle;
        }
    }

    private void OnSelectChanged(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => _grid.Selection.Toggle(Info.Key);

    private void OnDetailToggle(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _grid.ToggleDetail(Info.Key);
        e.Handled = true;
    }

    /// <summary>Column virtualisation: keeps cells for pinned columns and the visible scrolling range.</summary>
    internal void SyncCells()
    {
        if (Info.Kind != GridRowKind.Data) return;
        var layout = _grid.Layout;
        var (first, last) = layout.ScrollingRange(_grid.ScrollX, Math.Max(0, _grid.ViewportWidth - _grid.LeadWidth));
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layout.Columns.Count; i++)
        {
            var c = layout.Columns[i];
            if (c.Pin != GridPin.None || (i >= first && i <= last))
                wanted.Add(c.Field);
        }

        foreach (var field in _cells.Keys.Where(f => !wanted.Contains(f)).ToList())
        {
            Children.Remove(_cells[field]);
            _cells.Remove(field);
        }

        foreach (var field in wanted)
        {
            if (_cells.ContainsKey(field)) continue;
            var col = layout.Find(field)!;
            var cell = new DataGridCell(_grid, col.Column);
            _cells[field] = cell;
            Children.Insert(0, cell);
            cell.Bind(Info);
        }
    }

    private T Add<T>(T control) where T : Control
    {
        Children.Add(control);
        return control;
    }

    /// <summary>The navigator column under an x position (relative to this row), or −1.</summary>
    internal int ColumnAt(double x)
    {
        foreach (var cell in _cells.Values)
            if (x >= cell.Bounds.Left && x < cell.Bounds.Right)
                return _grid.Layout.Find(cell.Column.Field)?.Index ?? -1;
        return -1;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Info.IsSkeleton) return;
        var point = e.GetCurrentPoint(this);
        if (Info.Kind == GridRowKind.Group)
        {
            if (Info.Row?.GroupId is { } id) _grid.ToggleGroup(id);
            e.Handled = true;
            return;
        }
        if (Info.Kind != GridRowKind.Data) return;
        var col = ColumnAt(point.Position.X);
        _grid.OnRowPressed(Info, col, e.KeyModifiers, point.Properties.IsRightButtonPressed);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = availableSize.Height;
        foreach (var child in Children)
            child.Measure(new Size(double.PositiveInfinity, h));
        return new Size(_grid.ContentWidth, h);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var h = finalSize.Height;
        var scrollX = _grid.ScrollX;
        var vw = _grid.ViewportWidth;
        var lead = _grid.LeadWidth;
        var x = scrollX; // lead cells are sticky at the start

        if (_select is not null)
        {
            _select.Arrange(new Rect(x, 0, DataGrid.SelectionColumnWidth, h));
            x += DataGrid.SelectionColumnWidth;
        }
        if (_detailToggle is not null)
        {
            _detailToggle.Arrange(new Rect(x, 0, DataGrid.DetailColumnWidth, h));
        }

        foreach (var (field, cell) in _cells)
        {
            var col = _grid.Layout.Find(field);
            if (col is null) { cell.Arrange(default); continue; }
            cell.Arrange(new Rect(_grid.CellX(col, scrollX, vw) + lead, 0, col.Width, h));
            cell.ZIndex = col.Pin == GridPin.None ? 0 : 2;
        }

        _group?.Arrange(new Rect(scrollX, 0, Math.Max(0, Math.Min(vw, finalSize.Width)), h));
        _detail?.Arrange(new Rect(scrollX, 0, Math.Max(0, vw), h));
        _indicator?.Arrange(new Rect(scrollX, 0, 2, h));
        if (_indicator is not null) _indicator.ZIndex = 5;
        if (_select is not null) _select.ZIndex = 3;
        if (_detailToggle is not null) _detailToggle.ZIndex = 3;
        return finalSize;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer(DataGridRow owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IExpandCollapseProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            owner.Info.Kind == GridRowKind.Group ? AutomationControlType.Group : AutomationControlType.DataItem;

        public bool IsSelected => owner.IsSelected;
        public ISelectionProvider? SelectionContainer => null;
        public void Select() => owner._grid.Selection.Set([owner.Info.Key]);
        public void AddToSelection() { if (!owner.IsSelected) owner._grid.Selection.Toggle(owner.Info.Key); }
        public void RemoveFromSelection() { if (owner.IsSelected) owner._grid.Selection.Toggle(owner.Info.Key); }

        public ExpandCollapseState ExpandCollapseState =>
            owner.Info.Row is { } r && (r.Kind == GridRowKind.Group || r.HasChildren)
                ? (r.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed)
                : ExpandCollapseState.LeafNode;

        public bool ShowsMenu => false;
        public void Expand() { if (ExpandCollapseState == ExpandCollapseState.Collapsed && DataGrid.ExpandId(owner.Info.Row) is { } id) owner._grid.ToggleGroup(id); }
        public void Collapse() { if (ExpandCollapseState == ExpandCollapseState.Expanded && DataGrid.ExpandId(owner.Info.Row) is { } id) owner._grid.ToggleGroup(id); }
    }
}

/// <summary>A group row's label: expander, column, value, count and the group's aggregates.</summary>
public class DataGridGroupHeader : StackPanel
{
    private readonly DataGrid _grid;
    private readonly Icon _chevron = new() { Size = 14 };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Badge _count = new() { Size = ControlSize.Small, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _aggregates = new() { Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };

    internal DataGridGroupHeader(DataGrid grid)
    {
        _grid = grid;
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        Classes.Add("sl-grid-group");
        Children.Add(_chevron);
        Children.Add(_label);
        Children.Add(_count);
        Children.Add(_aggregates);
    }

    internal void Bind(DataGridRowInfo info)
    {
        var row = info.Row!;
        Margin = new Thickness(12 + row.Depth * 20, 0, 0, 0);
        _chevron.Kind = row.Expanded ? "chevron-down" : "chevron-right";
        var title = _grid.Definition(row.GroupField ?? "")?.DisplayTitle ?? row.GroupField;
        _label.Text = $"{title}: {(string.IsNullOrEmpty(row.GroupKeyText) ? "(empty)" : row.GroupKeyText)}";
        _count.Content = row.RowCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _aggregates.Text = row.Aggregates is { Count: > 0 } aggs
            ? string.Join("   ", aggs.Select(kv => _grid.AggregateText(kv.Key, kv.Value)).Where(t => t.Length > 0))
            : "";
    }
}

/// <summary>A data cell. Its child depends on the column type (text, badge, progress, sparkline, actions, template, editor).</summary>
public class DataGridCell : Border
{
    private readonly DataGrid _grid;
    private Control? _content;
    private string _contentKind = "";

    internal DataGridCell(DataGrid grid, GridColumn<object> column)
    {
        _grid = grid;
        Column = column;
        Classes.Add("sl-grid-cell");
        ClipToBounds = true;
    }

    public GridColumn<object> Column { get; }

    /// <summary>The live editor while this cell is being edited.</summary>
    public Control? Editor { get; private set; }

    /// <summary>The cell's display text (what export and copy use).</summary>
    public string Text { get; private set; } = "";

    /// <summary>
    /// Row state tint for an opaque pinned cell: composited over the grid background into a solid colour, so the
    /// cell still hides content scrolling beneath it.
    /// </summary>
    internal IBrush? RowOverlay
    {
        get => _overlay;
        set
        {
            if (ReferenceEquals(_overlay, value)) return;
            _overlay = value;
            if (value is ISolidColorBrush tint && _grid.Resource("Sl.Component.Grid.Background.Brush") is ISolidColorBrush bg)
            {
                var a = tint.Color.A / 255.0 * tint.Opacity;
                byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
                Background = new SolidColorBrush(Color.FromRgb(Mix(tint.Color.R, bg.Color.R), Mix(tint.Color.G, bg.Color.G), Mix(tint.Color.B, bg.Color.B)));
            }
            else if (!_hasTone)
                ClearValue(BackgroundProperty);
        }
    }

    private IBrush? _overlay;
    private bool _hasTone;

    internal void Bind(DataGridRowInfo info)
    {
        var col = Column;
        var resolved = _grid.Layout.Find(col.Field);
        var pad = _grid.Resource("Sl.Component.Grid.CellPaddingX", 12);
        Padding = col.Type == GridColumnType.Actions ? new Thickness(4, 0) : new Thickness(pad, 0);
        var isActive = _grid.ActiveCell is { } a && a.Row == info.Index && resolved is not null && a.Column == resolved.Index;
        Classes.Set("active", isActive && _grid.KeyboardFocusVisible);
        Classes.Set("pinned", resolved?.Pin is GridPin.Start or GridPin.End);
        Classes.Set("pin-edge-start", resolved is { Pin: GridPin.Start } && _grid.IsLastStartPin(resolved) && _grid.ScrollX > 0.5);
        Classes.Set("pin-edge-end", resolved is { Pin: GridPin.End } && _grid.IsFirstEndPin(resolved) && _grid.CanScrollRight);
        Classes.Set("numeric", col.Type is GridColumnType.Number or GridColumnType.Date);

        if (info.IsSkeleton || info.Item is null)
        {
            Text = "";
            SetContent("skeleton", () => new Skeleton { Shape = SkeletonShape.Text, Height = 10, Width = 40 + (col.Field.Length * 17 + info.Index * 29) % 80, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            Editor = null;
            return;
        }

        var item = info.Item;
        var edit = _grid.Editing.Current;
        var editingHere = edit is not null && edit.RowKey == info.Key && edit.Column.Field == col.Field;
        var value = _grid.Editing.GetValue(item, info.Key, col);
        Text = col.DisplayText(value);
        Classes.Set("pending", _grid.Editing.HasPending(info.Key, col.Field));
        Classes.Set("invalid", editingHere && edit!.Error is not null);
        ToolTip.SetTip(this, editingHere && edit!.Error is { } err ? err : null);
        AutomationProperties.SetName(this, $"{col.DisplayTitle}: {Text}");

        var def = _grid.Definition(col.Field);
        _hasTone = def?.CellTone?.Invoke(item) is { } tn && tn != Tone.Neutral;
        if (def?.CellTone?.Invoke(item) is { } tone and not Tone.Neutral)
            Background = _grid.Resource(tone switch
            {
                Tone.Success => "Sl.Brush.Status.Success.Bg",
                Tone.Warning => "Sl.Brush.Status.Warning.Bg",
                Tone.Danger => "Sl.Brush.Status.Danger.Bg",
                Tone.Info => "Sl.Brush.Status.Info.Bg",
                _ => "Sl.Brush.Accent.Subtle",
            }) as IBrush;
        else if (_overlay is null)
            ClearValue(BackgroundProperty);

        if (editingHere)
        {
            BindEditor(edit!, value);
            return;
        }
        Editor = null;

        if (def?.Template is { } template)
        {
            var cc = (ContentControl)SetContent("template", () => new ContentControl { VerticalAlignment = VerticalAlignment.Center });
            cc.ContentTemplate = template;
            cc.Content = item;
            return;
        }

        switch (col.Type)
        {
            case GridColumnType.Boolean:
                var icon = (Icon)SetContent("bool", () => new Icon { Size = 16, HorizontalAlignment = HorizontalAlignment.Center });
                icon.Kind = GridValues.ToBool(value) == true ? "check" : null;
                break;
            case GridColumnType.Enum:
                var badge = (Badge)SetContent("enum", () => new Badge { Size = ControlSize.Small, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
                badge.IsVisible = Text.Length > 0;
                badge.Content = Text;
                badge.Tone = col.EnumTones is { } tones && tones.TryGetValue(Text, out var t) ? t : Tone.Neutral;
                break;
            case GridColumnType.Progress:
                var bar = (DataGridProgress)SetContent("progress", () => new DataGridProgress());
                bar.Value = GridValues.ToDouble(value);
                bar.Text = Text;
                break;
            case GridColumnType.Sparkline:
                var spark = (DataGridSparkline)SetContent("spark", () => new DataGridSparkline());
                spark.Values = value is IEnumerable<double> d ? d.ToArray() : value is System.Collections.IEnumerable e ? e.Cast<object?>().Select(GridValues.ToDouble).Select(x => x ?? 0).ToArray() : [];
                break;
            case GridColumnType.Actions:
                var button = (Button)SetContent("actions", () => CreateActionsButton());
                button.Tag = item;
                break;
            default:
                var text = (TextBlock)SetContent("text", () => new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                text.Text = Text;
                text.TextAlignment = col.EffectiveAlign switch { GridAlign.End => TextAlignment.Right, GridAlign.Center => TextAlignment.Center, _ => TextAlignment.Left };
                text.Classes.Set("mono", col.Type is GridColumnType.Number or GridColumnType.Date);
                break;
        }

        if (_grid.IsTreeColumn(col) && info.Row is { } row)
            WrapTree(row);
    }

    private void WrapTree(GridViewRow<object> row)
    {
        // Indent + expander for tree data, in the first column.
        if (_content is null) return;
        var indent = row.Depth * 20;
        _content.Margin = new Thickness(indent + 26, 0, 0, 0);
        _treePanel ??= new Panel();
        if (_treeToggle is null)
        {
            _treeToggle = new Button { Classes = { "ghost", "sl-grid-tree-toggle" }, Focusable = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            _treeToggle.Click += OnTreeToggle;
        }
        UpdateToggle(_treeToggle, row, indent);
        if (!ReferenceEquals(_content.Parent, _treePanel))
        {
            Detach(_content);
            _treePanel.Children.Clear();
            _treePanel.Children.Add(_content);
            _treePanel.Children.Add(_treeToggle);
        }
        if (!ReferenceEquals(Child, _treePanel)) Child = _treePanel;
    }

    private Panel? _treePanel;
    private Button? _treeToggle;

    private static void Detach(Control? control)
    {
        switch (control?.Parent)
        {
            case Panel p: p.Children.Remove(control); break;
            case Decorator d: d.Child = null; break;
        }
    }

    private void UpdateToggle(Button toggle, GridViewRow<object> row, double indent)
    {
        toggle.Margin = new Thickness(indent, 0, 0, 0);
        toggle.IsVisible = row.HasChildren;
        toggle.Content = new Icon { Kind = row.Expanded ? "chevron-down" : "chevron-right", Size = 14 };
        AutomationProperties.SetName(toggle, row.Expanded ? "Collapse" : "Expand");
        toggle.Tag = DataGrid.ExpandId(row);
    }

    private void OnTreeToggle(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) _grid.ToggleGroup(id);
        e.Handled = true;
    }

    private Button CreateActionsButton()
    {
        var button = new Button { Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        Sl.SetStartIcon(button, "more-horizontal");
        Sl.SetIconOnly(button, true);
        Sl.SetSize(button, ControlSize.Small);
        AutomationProperties.SetName(button, "Row actions");
        button.Click += (_, e) =>
        {
            e.Handled = true;
            if (button.Tag is not { } item || _grid.Definition(Column.Field)?.Actions is not { } actions) return;
            var menu = new ContextMenu();
            foreach (var action in actions(item))
            {
                var mi = new MenuItem { Header = action.Label };
                if (action.Icon is { } ic) Sl.SetStartIcon(mi, ic);
                if (action.Shortcut is { } sc) Sl.SetShortcut(mi, sc);
                if (action.Tone == Tone.Danger) mi.Classes.Add("danger");
                mi.Click += (_, _) => action.Invoke();
                menu.Items.Add(mi);
            }
            menu.Open(button);
        };
        return button;
    }

    private Control SetContent(string kind, Func<Control> factory)
    {
        if (_contentKind != kind || _content is null)
        {
            _content = factory();
            _contentKind = kind;
        }
        _content.Margin = default;
        var wrapped = _treePanel is not null && ReferenceEquals(Child, _treePanel) && ReferenceEquals(_content.Parent, _treePanel);
        if (!ReferenceEquals(Child, _content) && !(wrapped && _grid.IsTreeColumn(Column)))
        {
            Detach(_content);
            Child = _content;
        }
        return _content;
    }

    private void BindEditor(GridCellEdit<object> edit, object? value)
    {
        Control editor;
        switch (Column.Type)
        {
            case GridColumnType.Boolean:
                var cb = Editor as CheckBox ?? new CheckBox { HorizontalAlignment = HorizontalAlignment.Center };
                cb.IsChecked = GridValues.ToBool(edit.Draft ?? value) == true;
                cb.IsCheckedChanged -= OnCheckEdit;
                cb.IsCheckedChanged += OnCheckEdit;
                editor = cb;
                break;
            case GridColumnType.Enum when Column.EnumOrder is { Count: > 0 } options:
                var combo = Editor as ComboBox ?? new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                combo.ItemsSource = options;
                combo.SelectedItem = edit.DraftText ?? Text;
                combo.SelectionChanged -= OnComboEdit;
                combo.SelectionChanged += OnComboEdit;
                editor = combo;
                break;
            default:
                var tb = Editor as TextBox ?? new TextBox { Classes = { "sl-grid-editor" }, VerticalContentAlignment = VerticalAlignment.Center };
                tb.TextChanged -= OnTextEdit;
                if (tb.Text != (edit.DraftText ?? Text)) tb.Text = edit.DraftText ?? Text;
                tb.TextChanged += OnTextEdit;
                tb.TextAlignment = Column.EffectiveAlign == GridAlign.End ? TextAlignment.Right : TextAlignment.Left;
                editor = tb;
                break;
        }
        AutomationProperties.SetName(editor, $"Edit {Column.DisplayTitle}");
        AutomationProperties.SetHelpText(editor, edit.Error ?? "");
        Editor = editor;
        _content = null;
        _contentKind = "editor";
        if (!ReferenceEquals(Child, editor)) Child = editor;
    }

    private void OnTextEdit(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && _grid.Editing.IsEditing)
        {
            _grid.Editing.SetDraftText(tb.Text ?? "");
            Classes.Set("invalid", _grid.Editing.Current?.Error is not null);
            ToolTip.SetTip(this, _grid.Editing.Current?.Error);
        }
    }

    private void OnCheckEdit(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is CheckBox cb && _grid.Editing.IsEditing)
            _grid.Editing.SetDraft(cb.IsChecked == true);
    }

    private void OnComboEdit(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string s } && _grid.Editing.IsEditing)
            _grid.Editing.SetDraftText(s);
    }
}

/// <summary>A compact progress bar with its value (progress columns).</summary>
public class DataGridProgress : Control
{
    public double? Value { get; set { field = value; InvalidateVisual(); } }
    public string Text { get; set { field = value; InvalidateVisual(); } } = "";

    public DataGridProgress() => VerticalAlignment = VerticalAlignment.Stretch;

    public override void Render(DrawingContext context)
    {
        var track = Brush("Sl.Component.Progress.Track.Brush", "Sl.Brush.Background.Muted");
        var fill = Brush("Sl.Component.Progress.Fill.Brush", "Sl.Brush.Accent.Default");
        var text = Brush("Sl.Brush.Text.Secondary", "Sl.Brush.Text.Primary");
        var labelWidth = 40.0;
        var barWidth = Math.Max(0, Bounds.Width - labelWidth - 8);
        var y = Bounds.Height / 2 - 3;
        var v = Math.Clamp((Value ?? 0) is var d && d > 1 ? d / 100 : d, 0, 1);
        context.DrawRectangle(track, null, new RoundedRect(new Rect(0, y, barWidth, 6), 3));
        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, y, barWidth * v, 6), 3));
        var ft = new FormattedText(Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(Font("Sl.Font.Family.Mono")), 12, text);
        context.DrawText(ft, new Point(Bounds.Width - ft.Width, (Bounds.Height - ft.Height) / 2));
    }

    private IBrush? Brush(params string[] keys)
    {
        foreach (var k in keys)
            if (this.TryFindResource(k, ActualThemeVariant, out var v) && v is IBrush b) return b;
        return Brushes.Gray;
    }

    private FontFamily Font(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) && v is FontFamily f ? f : FontFamily.Default;
}

/// <summary>Mini bar chart for sparkline columns; the last bar uses the accent.</summary>
public class DataGridSparkline : Control
{
    public double[] Values { get; set { field = value ?? []; InvalidateVisual(); } } = [];

    public override void Render(DrawingContext context)
    {
        if (Values.Length == 0) return;
        var muted = Find("Sl.Brush.Border.Strong");
        var accent = Find("Sl.Brush.Accent.Default");
        var h = Math.Min(20, Bounds.Height - 8);
        var top = (Bounds.Height - h) / 2;
        var max = Math.Max(1e-9, Values.Max());
        var gap = 2.0;
        var bw = Math.Max(1, (Bounds.Width - gap * (Values.Length - 1)) / Values.Length);
        for (var i = 0; i < Values.Length; i++)
        {
            var bh = Math.Max(2, h * Values[i] / max);
            context.DrawRectangle(i == Values.Length - 1 ? accent : muted, null,
                new RoundedRect(new Rect(i * (bw + gap), top + h - bh, bw, bh), 1));
        }
    }

    private IBrush Find(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;
}

/// <summary>A 2px indeterminate bar along the top of the body while <see cref="DataGrid.Loading"/> (static under reduced motion).</summary>
public class DataGridLoadingBar : Control
{
    private readonly global::Avalonia.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _phase;

    public DataGridLoadingBar()
    {
        _timer.Tick += (_, _) => { _phase = (_phase + 0.012) % 1.3; InvalidateVisual(); };
        AttachedToVisualTree += (_, _) => Sync();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Sync(); };
    }

    private void Sync()
    {
        if (IsVisible && IsAttachedToVisualTree() && SlateTheme.Current?.ReduceMotion != true) _timer.Start();
        else _timer.Stop();
    }

    private bool IsAttachedToVisualTree() => TopLevel.GetTopLevel(this) is not null;

    public override void Render(DrawingContext context)
    {
        var brush = this.TryFindResource("Sl.Brush.Accent.Default", ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Teal;
        var w = Bounds.Width;
        var start = (SlateTheme.Current?.ReduceMotion == true ? 0 : _phase - 0.3) * w;
        context.FillRectangle(brush, new Rect(Math.Max(0, start), 0, Math.Max(0, Math.Min(w, start + w * 0.3) - Math.Max(0, start)), Bounds.Height));
    }
}
