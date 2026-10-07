using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Slate.Data;

namespace Slate.Avalonia.Controls;

public partial class DataGrid
{
    private Border? _toolbar;
    private Border? _groupBar;
    private DataGridHeaderRow? _header;
    private ScrollViewer? _scroll;
    private DataGridRowsPresenter? _rows;
    private DataGridFooterRow? _footer;
    private Border? _bottomBar;
    private ContentControl? _empty;
    private TextField? _quickFilter;
    private Vector _lastOffset;

    internal ScrollViewer? ScrollViewer => _scroll;

    /// <summary>The rows presenter (virtualised body).</summary>
    public DataGridRowsPresenter? RowsPresenter => _rows;

    /// <summary>The header row.</summary>
    public DataGridHeaderRow? HeaderRow => _header;

    public DataGridFooterRow? FooterRow => _footer;

    internal double ScrollX => _scroll?.Offset.X ?? 0;

    internal bool CanScrollRight => _scroll is { } s && s.Offset.X + s.Viewport.Width < s.Extent.Width - 0.5;

    /// <summary>Content width: all columns plus the lead columns, at least the viewport.</summary>
    internal double ContentWidth => Math.Max(_layout.TotalWidth + LeadWidth, ViewportWidth);

    /// <summary>Whether the keyboard (not the pointer) moved the active cell, so the focus ring should show.</summary>
    public bool KeyboardFocusVisible { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_scroll is not null) _scroll.ScrollChanged -= OnScrollChanged;

        _toolbar = e.NameScope.Find<Border>("PART_Toolbar");
        _groupBar = e.NameScope.Find<Border>("PART_GroupBar");
        _header = e.NameScope.Find<DataGridHeaderRow>("PART_Header");
        _scroll = e.NameScope.Find<ScrollViewer>("PART_Scroll");
        _rows = e.NameScope.Find<DataGridRowsPresenter>("PART_Rows");
        _footer = e.NameScope.Find<DataGridFooterRow>("PART_Footer");
        _bottomBar = e.NameScope.Find<Border>("PART_BottomBar");
        _empty = e.NameScope.Find<ContentControl>("PART_Empty");

        if (_header is not null) _header.Owner = this;
        if (_rows is not null) _rows.Owner = this;
        if (_footer is not null) _footer.Owner = this;
        if (_scroll is not null) _scroll.ScrollChanged += OnScrollChanged;

        if (_selection.Mode != SelectionMode) ResetSelection();
        ResetEditing();
        Refresh();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        QueueRefresh();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll is null) return;
        var offset = _scroll.Offset;
        var dx = Math.Abs(offset.X - _lastOffset.X) > 0.01;
        var dy = Math.Abs(offset.Y - _lastOffset.Y) > 0.01;
        var viewportChanged = e.ViewportDelta != default;
        _lastOffset = offset;

        if (viewportChanged && _engineColumns.Any(c => c.Flex > 0))
        {
            QueueRefresh();
            return;
        }
        if (dy || viewportChanged) _rows?.Realize(force: false);
        if (dx)
        {
            _rows?.OnHorizontalScroll();
            _rows?.Realize(force: false);
            _header?.InvalidateArrange();
            _footer?.InvalidateArrange();
        }
    }

    /// <summary>Scrolls by a number of rows (tests and the keyboard use this).</summary>
    public void ScrollToRow(int index)
    {
        if (_scroll is null || _rows is null) return;
        var top = _rows.TopOf(Math.Clamp(index, 0, Math.Max(0, ViewRowCount - 1)));
        var vh = _scroll.Viewport.Height;
        var y = _scroll.Offset.Y;
        if (top < y) y = top;
        else if (top + RowHeight > y + vh) y = top + RowHeight - vh;
        _scroll.Offset = new Vector(_scroll.Offset.X, y);
        _rows.Realize(force: false);
    }

    /// <summary>Content-space x of a column (before the lead columns), honouring sticky pinning.</summary>
    internal double CellX(ResolvedColumn<object> col, double scrollX, double viewportWidth) => col.Pin switch
    {
        GridPin.Start => Math.Max(col.Left, scrollX + col.StickyOffset),
        GridPin.End => Math.Min(col.Left, scrollX + Math.Max(0, viewportWidth - LeadWidth) - col.StickyOffset - col.Width),
        _ => col.Left,
    };

    internal bool IsLastStartPin(ResolvedColumn<object> col) =>
        _layout.Columns.LastOrDefault(c => c.Pin == GridPin.Start)?.Field == col.Field;

    internal bool IsFirstEndPin(ResolvedColumn<object> col) =>
        _layout.Columns.FirstOrDefault(c => c.Pin == GridPin.End)?.Field == col.Field;

    /// <summary>The first visible column carries the tree indentation and expander.</summary>
    internal bool IsTreeColumn(GridColumn<object> col) =>
        ChildrenSelector is not null && _cache is null && _layout.Columns.FirstOrDefault()?.Field == col.Field;

    internal object? Resource(string key) => this.TryFindResource(key, ActualThemeVariant, out var v) ? v : null;

    internal string AggregateText(string field, object? value, bool withName = true)
    {
        var col = _engineColumns.FirstOrDefault(c => c.Field == field);
        if (col is null || col.Aggregate == GridAggregate.None) return "";
        var text = col.Aggregate is GridAggregate.Count
            ? GridValues.Format(value, GridColumnType.Number, "#,##0")
            : col.Aggregate is GridAggregate.Avg or GridAggregate.Sum && col.Type is GridColumnType.Number or GridColumnType.Progress
                ? GridValues.Format(value, GridColumnType.Number, col.Format ?? (col.Aggregate == GridAggregate.Avg ? "#,##0.0" : "#,##0"))
                : col.DisplayText(value);
        var name = col.Aggregate.ToString().ToLowerInvariant();
        return withName ? $"{name} {col.DisplayTitle.ToLowerInvariant()} {text}" : $"{name} {text}";
    }

    private void ClampActiveCell()
    {
        if (ActiveCell is not { } a) return;
        if (ViewRowCount == 0 || _layout.Columns.Count == 0) ActiveCell = null;
        else ActiveCell = new GridCell(Math.Min(a.Row, ViewRowCount - 1), Math.Min(a.Column, _layout.Columns.Count - 1));
    }

    // ---- pointer & keyboard ----

    internal void OnRowPressed(DataGridRowInfo row, int column, KeyModifiers modifiers, bool rightButton)
    {
        if (_edit.IsEditing && !(_edit.Current!.RowKey == row.Key && column >= 0 && _layout.Columns[column].Field == _edit.Current.Column.Field))
        {
            if (!CommitEdit()) return;
        }
        KeyboardFocusVisible = false;
        ActiveCell = new GridCell(row.Index, Math.Max(0, column));
        var ctrl = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (!(rightButton && _selection.IsSelected(row.Key)))
            ClickRow(row.Key, ctrl, modifiers.HasFlag(KeyModifiers.Shift));
        Focus(NavigationMethod.Pointer);
        _rows?.Realize(force: false);
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        // Editing: Enter/Tab commit and move, Escape cancels; everything else goes to the editor.
        if (_edit.IsEditing)
        {
            if (e.Key is Key.Enter or Key.Tab)
            {
                if (CommitEdit())
                {
                    var key = EditSession<object>.MoveAfterCommit(e.Key == Key.Enter ? GridEditCommitKey.Enter : GridEditCommitKey.Tab, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                    MoveActive(key, extend: false);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelEdit();
                e.Handled = true;
            }
            return;
        }

        if (e.Source is DataGridHeaderCell or TextBox or Button or CheckBox) return;
        if (e.Source is Control c && c != this && global::Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<DataGridRowsPresenter>(c) is null) return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        GridKey? nav = e.Key switch
        {
            Key.Up => GridKey.Up,
            Key.Down => GridKey.Down,
            Key.Left => GridKey.Left,
            Key.Right => GridKey.Right,
            Key.Home => ctrl ? GridKey.CtrlHome : GridKey.Home,
            Key.End => ctrl ? GridKey.CtrlEnd : GridKey.End,
            Key.PageUp => GridKey.PageUp,
            Key.PageDown => GridKey.PageDown,
            Key.OemPlus or Key.Add => GridKey.Plus,
            Key.OemMinus or Key.Subtract => GridKey.Minus,
            _ => null,
        };

        if (nav is { } k)
        {
            MoveActive(k, extend: shift && k is GridKey.Up or GridKey.Down or GridKey.PageUp or GridKey.PageDown);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space when ActiveCell is { } a && SelectionMode != GridSelectionMode.None:
                var row = RowAt(a.Row);
                if (row.Kind == GridRowKind.Data && !row.IsSkeleton)
                {
                    if (SelectionMode == GridSelectionMode.Multi) _selection.Toggle(row.Key);
                    else _selection.Click(row.Key, VisibleKeys);
                }
                else if (row.Row?.GroupId is { } gid) ToggleGroup(gid);
                e.Handled = true;
                break;
            case Key.A when ctrl && SelectionMode == GridSelectionMode.Multi:
                if (_cache is not null) _selection.SelectAllMatching(_cache.TotalCount ?? 0);
                else _selection.SelectAll(_result?.ItemKeys ?? []);
                e.Handled = true;
                break;
            case Key.C when ctrl:
                CopySelection(includeHeader: shift);
                e.Handled = true;
                break;
            case Key.Enter or Key.F2 when ActiveCell is { } a2:
                var info = RowAt(a2.Row);
                if (info.Kind == GridRowKind.Group && info.Row?.GroupId is { } id) ToggleGroup(id);
                else if (!(EditMode != GridEditMode.None && BeginEdit())) if (e.Key == Key.Enter) Activate(info);
                e.Handled = true;
                break;
            case Key.Escape when _selection.Count > 0:
                _selection.Clear();
                e.Handled = true;
                break;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        // Typing on an editable cell starts editing with that text.
        if (!e.Handled && !_edit.IsEditing && EditMode != GridEditMode.None && e.Text is { Length: > 0 } text && !char.IsControl(text[0]) && e.Source == this)
        {
            if (BeginEdit(initialText: text)) e.Handled = true;
        }
    }

    /// <summary>Moves the active cell with a navigator key (keyboard model), extending the selection with Shift.</summary>
    public void MoveActive(GridKey key, bool extend = false)
    {
        if (ViewRowCount == 0 || _layout.Columns.Count == 0) return;
        KeyboardFocusVisible = true;
        var current = ActiveCell ?? new GridCell(0, 0);
        var ctx = new GridNavContext
        {
            RowCount = ViewRowCount,
            ColumnCount = _layout.Columns.Count,
            PageRows = GridViewport.PageRows(_scroll?.Viewport.Height ?? 400, RowHeight),
            Kind = i => RowAt(i).Kind,
            CanExpand = i => RowAt(i).Row is { } r && (r.Kind == GridRowKind.Group || r.HasChildren),
            IsExpanded = i => RowAt(i).Row?.Expanded ?? false,
        };
        var result = ActiveCell is null ? new GridNavResult(current, GridNavAction.None) : GridNavigator.Move(current, key, ctx);
        if (result.Action != GridNavAction.None && ExpandId(RowAt(current.Row).Row) is { } gid)
        {
            Apply(_state.SetGroupExpanded(gid, result.Action == GridNavAction.Expand));
            return;
        }
        ActiveCell = result.Cell;
        if (extend && SelectionMode == GridSelectionMode.Multi && RowAt(result.Cell.Row) is { Kind: GridRowKind.Data, IsSkeleton: false } r2)
            _selection.ExtendTo(r2.Key, VisibleKeys);
        ScrollToRow(result.Cell.Row);
        RevealColumn(result.Cell.Column);
        _rows?.Realize(force: false);
    }

    private void RevealColumn(int index)
    {
        if (_scroll is null || index >= _layout.Columns.Count) return;
        var col = _layout.Columns[index];
        if (col.Pin != GridPin.None) return;
        var vw = Math.Max(0, ViewportWidth - LeadWidth);
        var startPinned = _layout.Columns.Where(c => c.Pin == GridPin.Start).Sum(c => c.Width);
        var endPinned = _layout.Columns.Where(c => c.Pin == GridPin.End).Sum(c => c.Width);
        var x = _scroll.Offset.X;
        if (col.Left - startPinned < x) x = col.Left - startPinned;
        else if (col.Left + col.Width > x + vw - endPinned) x = col.Left + col.Width - vw + endPinned;
        _scroll.Offset = new Vector(Math.Max(0, x), _scroll.Offset.Y);
    }

    // ---- group bar drag target ----

    internal bool IsOverGroupBar(PointerEventArgs e) =>
        _groupBar is { IsVisible: true } bar && new Rect(bar.Bounds.Size).Contains(e.GetPosition(bar));

    internal void HighlightGroupBar(bool on) => _groupBar?.Classes.Set("drop-target", on);

    // ---- chrome ----

    private void RenderChrome()
    {
        RenderToolbar();
        RenderGroupBar();
        RenderBottomBar();
        if (_empty is not null)
        {
            _empty.IsVisible = ViewRowCount == 0 && !Loading && !(IsServerMode && _cache!.TotalCount is null);
            _empty.Content = EmptyContent ?? DefaultEmpty();
        }
    }

    private Control DefaultEmpty()
    {
        var filtered = _state.HasFilters;
        var panel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new Icon { Kind = filtered ? "search" : "file", Size = 20, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = filtered ? "No rows match the current filters" : "No rows", HorizontalAlignment = HorizontalAlignment.Center, Classes = { "sl-grid-empty-text" } });
        if (filtered)
        {
            var clear = new Button { Content = "Clear filters", HorizontalAlignment = HorizontalAlignment.Center };
            Sl.SetSize(clear, ControlSize.Small);
            clear.Click += (_, _) => ClearFilters();
            panel.Children.Add(clear);
        }
        return panel;
    }

    private void RenderToolbar()
    {
        if (_toolbar is null) return;
        _toolbar.IsVisible = ShowToolbar || _state.Filters.Count > 0;
        if (!_toolbar.IsVisible) return;

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };

        if (ShowToolbar)
        {
            if (_quickFilter is null)
            {
                _quickFilter = new TextField { Placeholder = "Search", StartIcon = "search", Clearable = true, Width = 240, Size = ControlSize.Small };
                AutomationProperties.SetName(_quickFilter, "Search rows");
                _quickFilter.PropertyChanged += (_, e) =>
                {
                    if (e.Property == TextField.ValueProperty) Apply(_state.SetQuickFilter(_quickFilter.Value ?? ""));
                };
            }
            if (_quickFilter.Value != _state.QuickFilter) _quickFilter.Value = _state.QuickFilter;
            (_quickFilter.Parent as Panel)?.Children.Remove(_quickFilter);
            bar.Children.Add(_quickFilter);
        }

        foreach (var f in _state.Filters)
        {
            var title = Definition(f.Field)?.DisplayTitle ?? f.Field;
            var chip = new Button { Classes = { "sl-grid-chip" } };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = $"{title} {DataGridFilterEditor.Describe(f)}", VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new Icon { Kind = "x", Size = 12, VerticalAlignment = VerticalAlignment.Center });
            chip.Content = content;
            AutomationProperties.SetName(chip, $"Remove filter {title} {DataGridFilterEditor.Describe(f)}");
            var field = f.Field;
            chip.Click += (_, _) => ClearFilter(field);
            bar.Children.Add(chip);
        }
        if (_state.Filters.Count > 1)
        {
            var clear = new Button { Content = "Clear all", Classes = { "ghost" } };
            Sl.SetSize(clear, ControlSize.Small);
            clear.Click += (_, _) => ClearFilters();
            bar.Children.Add(clear);
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (ToolbarContent is { } custom)
            right.Children.Add(Adopt(custom));
        if (ShowToolbar)
        {
            right.Children.Add(DensityButton());
            right.Children.Add(ColumnsChooser());
            right.Children.Add(ExportMenu());
        }

        var dock = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(bar);
        _toolbar.Child = dock;
    }

    /// <summary>Slot content (ToolbarContent, SelectionActions) moves from the previous render's panel into the new one.</summary>
    private static Control Adopt(object content)
    {
        if (content is not Control control) return new ContentControl { Content = content };
        if (control.Parent is Panel old) old.Children.Remove(control);
        return control;
    }

    private Button DensityButton()
    {
        var comfortable = (Sl.GetDensity(this) ?? SlateTheme.Current?.Density ?? Slate.Density.Compact) == Slate.Density.Comfortable;
        var b = new Button { Classes = { "ghost" } };
        Sl.SetStartIcon(b, "menu");
        Sl.SetIconOnly(b, true);
        Sl.SetSize(b, ControlSize.Small);
        AutomationProperties.SetName(b, comfortable ? "Compact rows" : "Comfortable rows");
        ToolTip.SetTip(b, comfortable ? "Compact rows" : "Comfortable rows");
        b.Click += (_, _) => { Density = comfortable ? Slate.Density.Compact : Slate.Density.Comfortable; QueueRefresh(); };
        return b;
    }

    private Control ColumnsChooser()
    {
        var anchor = new Button { Classes = { "ghost" } };
        Sl.SetStartIcon(anchor, "columns");
        Sl.SetIconOnly(anchor, true);
        Sl.SetSize(anchor, ControlSize.Small);
        AutomationProperties.SetName(anchor, "Columns");
        ToolTip.SetTip(anchor, "Columns");
        var popover = new Popover { Anchor = anchor, Placement = PopoverPlacement.BottomEnd };
        popover.OpenChanged += (_, _) =>
        {
            if (!popover.Open) return;
            var list = new StackPanel { Spacing = 2, MinWidth = 200 };
            list.Children.Add(new TextBlock { Text = "Columns", Classes = { "sl-grid-popover-title" } });
            var order = _layout.Order;
            foreach (var field in _engineColumns.Select(c => c.Field).OrderBy(f => order.ToList().IndexOf(f)))
            {
                var col = _engineColumns.First(c => c.Field == field);
                var cb = new CheckBox
                {
                    Content = col.DisplayTitle,
                    IsChecked = !(_state.Column(field)?.Hidden ?? col.Hidden),
                    IsEnabled = col.Hideable,
                };
                cb.IsCheckedChanged += (_, _) => SetColumnHidden(field, cb.IsChecked != true);
                list.Children.Add(cb);
            }
            popover.Content = list;
        };
        return popover;
    }

    private Control ExportMenu()
    {
        var trigger = new Button { Classes = { "ghost" } };
        Sl.SetStartIcon(trigger, "download");
        Sl.SetIconOnly(trigger, true);
        Sl.SetSize(trigger, ControlSize.Small);
        AutomationProperties.SetName(trigger, "Export");
        ToolTip.SetTip(trigger, "Export");
        var menu = new Menu { Trigger = trigger, Placement = PopoverPlacement.BottomEnd };
        void Add(string label, ExportScope scope, bool csv)
        {
            var mi = new MenuItem { Header = label };
            mi.Click += (_, _) => RunExport(scope, csv);
            menu.Items.Add(mi);
        }
        Add("CSV — visible rows", ExportScope.Visible, true);
        Add("CSV — selected rows", ExportScope.Selected, true);
        Add("CSV — all rows", ExportScope.All, true);
        menu.Items.Add(new Separator());
        Add("TSV — visible rows", ExportScope.Visible, false);
        return menu;
    }

    private void RenderGroupBar()
    {
        if (_groupBar is null) return;
        _groupBar.IsVisible = Groupable && !IsServerMode && ChildrenSelector is null;
        if (!_groupBar.IsVisible) return;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new Icon { Kind = "group", Size = 14, VerticalAlignment = VerticalAlignment.Center });
        if (_state.GroupBy.Count == 0)
            panel.Children.Add(new TextBlock { Text = "Drag a column header here to group by it", Classes = { "sl-grid-groupbar-hint" }, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < _state.GroupBy.Count; i++)
        {
            var field = _state.GroupBy[i];
            if (i > 0) panel.Children.Add(new Icon { Kind = "chevron-right", Size = 12, VerticalAlignment = VerticalAlignment.Center });
            var chip = new Button { Classes = { "sl-grid-chip" } };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = Definition(field)?.DisplayTitle ?? field, VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new Icon { Kind = "x", Size = 12, VerticalAlignment = VerticalAlignment.Center });
            chip.Content = content;
            AutomationProperties.SetName(chip, $"Remove grouping by {Definition(field)?.DisplayTitle ?? field}");
            chip.Click += (_, _) => RemoveGroupBy(field);
            panel.Children.Add(chip);
        }
        if (_state.GroupBy.Count > 0)
        {
            var expand = new Button { Content = "Expand all", Classes = { "ghost" } };
            var collapse = new Button { Content = "Collapse all", Classes = { "ghost" } };
            Sl.SetSize(expand, ControlSize.Small);
            Sl.SetSize(collapse, ControlSize.Small);
            expand.Click += (_, _) => Apply(_state.ExpandAll());
            collapse.Click += (_, _) => Apply(_state.CollapseAll());
            panel.Children.Add(expand);
            panel.Children.Add(collapse);
        }
        _groupBar.Child = panel;
    }

    private void RenderBottomBar()
    {
        if (_bottomBar is null) return;
        var stack = new StackPanel { Spacing = 0 };

        // Selection bar
        if (_selection.Count > 0 && SelectionMode != GridSelectionMode.None)
        {
            var sel = new DockPanel { Classes = { "sl-grid-selection-bar" } };
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            var count = _selection.Count;
            left.Children.Add(new TextBlock { Text = $"{count:N0} selected", Classes = { "sl-grid-bar-text" }, VerticalAlignment = VerticalAlignment.Center });
            if (_selection.AllMatching)
                left.Children.Add(new Badge { Content = "All matching", Tone = Tone.Accent, Size = ControlSize.Small, VerticalAlignment = VerticalAlignment.Center });
            else if (_cache is not null && SelectionMode == GridSelectionMode.Multi && (_cache.TotalCount ?? 0) > count)
            {
                var all = new Button { Content = $"Select all {_cache.TotalCount:N0}", Classes = { "ghost" } };
                Sl.SetSize(all, ControlSize.Small);
                all.Click += (_, _) => _selection.SelectAllMatching(_cache.TotalCount ?? 0);
                left.Children.Add(all);
            }
            var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (SelectionActions is { } actions)
                right.Children.Add(Adopt(actions));
            var clear = new Button { Content = "Clear", Classes = { "ghost" } };
            Sl.SetSize(clear, ControlSize.Small);
            clear.Click += (_, _) => _selection.Clear();
            right.Children.Add(clear);
            DockPanel.SetDock(right, Dock.Right);
            sel.Children.Add(right);
            sel.Children.Add(left);
            stack.Children.Add(new Border { Child = sel, Padding = new Thickness(12, 6), Background = Resource("Sl.Component.Grid.RowSelected.Brush") as IBrush });
        }

        // Batch edit bar
        if (_edit.Mode == GridEditMode.Batch && _edit.Pending.Count > 0)
        {
            var batch = new DockPanel { Classes = { "sl-grid-batch-bar" } };
            var n = _edit.Pending.Count;
            var text = new TextBlock { Text = $"{n} unsaved change{(n == 1 ? "" : "s")}", VerticalAlignment = VerticalAlignment.Center, Classes = { "sl-grid-bar-text" } };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var discard = new Button { Content = "Discard" };
            var commit = new Button { Content = "Commit" };
            Sl.SetSize(discard, ControlSize.Small);
            Sl.SetSize(commit, ControlSize.Small);
            Sl.SetVariant(commit, ButtonVariant.Solid);
            Sl.SetTone(commit, Tone.Accent);
            discard.Click += (_, _) => DiscardAll();
            commit.Click += (_, _) => CommitAll();
            buttons.Children.Add(discard);
            buttons.Children.Add(commit);
            DockPanel.SetDock(buttons, Dock.Right);
            batch.Children.Add(buttons);
            batch.Children.Add(text);
            stack.Children.Add(new Border { Child = batch, Padding = new Thickness(12, 6), Background = Resource("Sl.Brush.Status.Warning.Bg") as IBrush });
        }

        // Pager / status
        var status = new DockPanel { Classes = { "sl-grid-status" }, Margin = new Thickness(12, 6) };
        var info = new TextBlock { Classes = { "sl-grid-status-text" }, VerticalAlignment = VerticalAlignment.Center };
        if (_cache is not null)
            info.Text = _cache.TotalCount is { } total ? $"{total:N0} rows" : "Loading…";
        else if (_result is { } r)
            info.Text = r.FilteredCount == r.TotalCount ? $"{r.TotalCount:N0} rows" : $"{r.FilteredCount:N0} of {r.TotalCount:N0} rows";
        if (Pagination == GridPagination.Pages)
        {
            var pageCount = _cache is not null ? Math.Max(1, (int)Math.Ceiling((_cache.TotalCount ?? 0) / (double)Math.Max(1, _state.PageSize))) : _result?.PageCount ?? 1;
            var pager = new Controls.Pagination
            {
                Page = _state.PageIndex + 1,
                PageCount = pageCount,
                PageSize = _state.PageSize,
                PageSizes = [25, 50, 100, 250],
                TotalCount = _cache?.TotalCount ?? _result?.ViewRowCount,
                Size = ControlSize.Small,
            };
            pager.PropertyChanged += (_, e) =>
            {
                if (e.Property == Controls.Pagination.PageProperty) Apply(_state.SetPage(pager.Page - 1));
                else if (e.Property == Controls.Pagination.PageSizeProperty && pager.PageSize is { } size) Apply(_state.SetPageSize(size));
            };
            DockPanel.SetDock(pager, Dock.Right);
            status.Children.Add(pager);
        }
        status.Children.Add(info);
        status.IsVisible = Pagination == GridPagination.Pages || ShowToolbar;
        stack.Children.Add(status);

        _bottomBar.Child = stack;
        _bottomBar.IsVisible = stack.Children.Any(c => c.IsVisible);
    }
}
