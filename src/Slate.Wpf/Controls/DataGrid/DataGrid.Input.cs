using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Slate.Data;

namespace Slate.Wpf;

public partial class DataGrid
{
    private bool _keyboardMode;
    private GridCellView? _editingCell;
    private FrameworkElement? _editor;
    private Popup? _popup;

    /// <summary>Field whose column menu is open (keeps its header's menu button visible).</summary>
    internal string? ColumnMenuField { get; private set; }

    internal bool IsEditingCell => _editingCell is not null;

    // ---- mouse -----------------------------------------------------------------------------------------------

    internal void OnRowCheckboxClick(int rowIndex)
    {
        Controller.Toggle(rowIndex);
        Controller.Active = Controller.Active with { Row = rowIndex };
    }

    internal void OnRowMouseDown(GridRowView row, MouseButtonEventArgs e)
    {
        if (row.Row is not { } viewRow) return;
        _keyboardMode = false;
        if (!IsKeyboardFocusWithin || _editingCell is not null) Focus();
        var column = row.ColumnAt(e.GetPosition(row).X);
        Controller.Active = new GridCell(row.Index, column < 0 ? Controller.Active.Column : column);

        switch (viewRow.Kind)
        {
            case GridRowKind.Group:
                Controller.ToggleExpand(row.Index);
                break;
            case GridRowKind.Data when viewRow.Item is { } item:
                var mods = Keyboard.Modifiers;
                if (e.ClickCount == 2)
                {
                    if (column >= 0 && CurrentLayout.Columns[column].Column is var col && Controller.CanEdit(row.Index, col))
                        BeginEditAt(row.Index, column, null);
                    else
                        RaiseEvent(new GridRowEventArgs(RowActivatedEvent, item, Controller.KeyOf(item) ?? ""));
                }
                else if (SelectionMode != GridSelectionMode.None)
                {
                    Controller.Click(row.Index, ctrl: (mods & ModifierKeys.Control) != 0, shift: (mods & ModifierKeys.Shift) != 0);
                }
                break;
        }
        RefreshActive();
        e.Handled = true;
    }

    /// <summary>Moves a column so it lands at <paramref name="visibleIndex"/> among visible columns (header drag).</summary>
    internal void MoveColumnToVisibleIndex(string field, int visibleIndex)
    {
        var layout = Controller.Layout();
        var visible = layout.Columns.Select(c => c.Field).ToList();
        var from = visible.IndexOf(field);
        if (from < 0) return;
        if (visibleIndex > from) visibleIndex--;
        if (visibleIndex == from) return;
        visibleIndex = Math.Clamp(visibleIndex, 0, visible.Count - 1);
        var order = layout.Order.ToList();
        Controller.Move(field, order.IndexOf(visible[visibleIndex]));
    }

    // ---- keyboard --------------------------------------------------------------------------------------------

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this) && e.KeyboardDevice.IsKeyDown(Key.Tab))
            _keyboardMode = true;
        RefreshActive();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        RefreshActive();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !ReferenceEquals(e.OriginalSource, this)) return;
        e.Handled = HandleKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers);
    }

    /// <summary>The grid keyboard model (WAI-ARIA grid pattern). Returns true when the key was handled.</summary>
    internal bool HandleKey(Key key, ModifierKeys mods)
    {
        var ctrl = (mods & ModifierKeys.Control) != 0;
        var shift = (mods & ModifierKeys.Shift) != 0;
        var alt = (mods & ModifierKeys.Alt) != 0;
        var c = Controller;
        var columns = CurrentLayout.Columns.Count;

        if (alt && key is Key.Left or Key.Right)
        {
            if (Controller.Active.Column < columns)
            {
                var field = CurrentLayout.Columns[c.Active.Column].Field;
                var delta = key == Key.Left ? -1 : 1;
                if (c.MoveBy(field, delta))
                {
                    c.Active = c.Active with { Column = Math.Clamp(c.Active.Column + delta, 0, columns - 1) };
                    Announce($"{c.Column(field)?.DisplayTitle} moved {(delta < 0 ? "left" : "right")}");
                }
            }
            _keyboardMode = true;
            RefreshActive();
            return true;
        }

        GridKey? nav = key switch
        {
            Key.Up => GridKey.Up,
            Key.Down => GridKey.Down,
            Key.Left => GridKey.Left,
            Key.Right => GridKey.Right,
            Key.Home => ctrl ? GridKey.CtrlHome : GridKey.Home,
            Key.End => ctrl ? GridKey.CtrlEnd : GridKey.End,
            Key.PageUp => GridKey.PageUp,
            Key.PageDown => GridKey.PageDown,
            Key.Add or Key.OemPlus => GridKey.Plus,
            Key.Subtract or Key.OemMinus => GridKey.Minus,
            _ => null,
        };

        if (nav is { } gridKey)
        {
            _keyboardMode = true;
            c.Navigate(gridKey, _surface?.ViewportHeight ?? 400, Math.Max(1, columns));
            if (shift && gridKey is GridKey.Up or GridKey.Down or GridKey.PageUp or GridKey.PageDown && SelectionMode == GridSelectionMode.Multi)
                c.ExtendTo(c.Active.Row);
            RevealActive();
            return true;
        }

        switch (key)
        {
            case Key.Space when SelectionMode != GridSelectionMode.None:
                _keyboardMode = true;
                if (c.RowAt(c.Active.Row) is { Kind: GridRowKind.Data, Item: not null })
                {
                    if (SelectionMode == GridSelectionMode.Single) c.Click(c.Active.Row);
                    else if (shift) c.ExtendTo(c.Active.Row);
                    else c.Toggle(c.Active.Row);
                }
                else if (c.RowAt(c.Active.Row) is { Kind: GridRowKind.Group })
                {
                    c.ToggleExpand(c.Active.Row);
                }
                return true;
            case Key.A when ctrl && SelectionMode == GridSelectionMode.Multi:
                if (c.HeaderState != SelectAllState.All) c.ToggleAll();
                return true;
            case Key.C when ctrl:
                CopySelection(includeHeader: shift);
                Announce("Copied");
                return true;
            case Key.Enter or Key.F2:
            {
                _keyboardMode = true;
                var row = c.RowAt(c.Active.Row);
                if (row is { Kind: GridRowKind.Group }) { c.ToggleExpand(c.Active.Row); return true; }
                if (c.Active.Column < columns && c.CanEdit(c.Active.Row, CurrentLayout.Columns[c.Active.Column].Column))
                {
                    BeginEditAt(c.Active.Row, c.Active.Column, null);
                    return true;
                }
                if (key == Key.Enter && row is { Kind: GridRowKind.Data, Item: { } item })
                    RaiseEvent(new GridRowEventArgs(RowActivatedEvent, item, Controller.KeyOf(item) ?? ""));
                return true;
            }
            case Key.Escape:
                if (SelectionMode != GridSelectionMode.None && c.Selection.Count > 0) { c.ClearSelection(); return true; }
                return false;
            case Key.Apps:
            case Key.F10 when shift:
                if (c.Active.Column < columns && _header?.Cell(CurrentLayout.Columns[c.Active.Column].Field) is { } headerCell)
                    OpenColumnMenu(headerCell);
                return true;
        }
        return false;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || !ReferenceEquals(e.OriginalSource, this) || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (e.Text is "+" or "-" or " ") return;
        var c = Controller;
        if (c.Active.Column < CurrentLayout.Columns.Count && CurrentLayout.Columns[c.Active.Column].Column is var col
            && col.Type is not (GridColumnType.Boolean or GridColumnType.Enum) && c.CanEdit(c.Active.Row, col))
        {
            BeginEditAt(c.Active.Row, c.Active.Column, e.Text);
            e.Handled = true;
        }
    }

    private void RevealActive()
    {
        RevealRow(Controller.Active.Row);
        RevealColumn(Controller.Active.Column);
        RefreshActive();
        if (Controller.RowAt(Controller.Active.Row) is { } row)
        {
            var colTitle = Controller.Active.Column < CurrentLayout.Columns.Count ? CurrentLayout.Columns[Controller.Active.Column].Column.DisplayTitle : "";
            var text = row is { Kind: GridRowKind.Data, Item: { } item } && Controller.Active.Column < CurrentLayout.Columns.Count
                ? Controller.Text(item, row.Key, CurrentLayout.Columns[Controller.Active.Column].Column)
                : row.Kind == GridRowKind.Group ? $"{row.GroupKeyText}, {row.RowCount} rows, {(row.Expanded ? "expanded" : "collapsed")}" : "";
            AutomationProperties.SetItemStatus(this, $"Row {Controller.Active.Row + 1}, {colTitle}");
            Announce(text.Length > 0 ? $"{colTitle} {text}" : colTitle);
        }
    }

    private void RefreshActive()
    {
        if (_surface is null) return;
        foreach (var row in _surface.RealizedRows) row.UpdateActive();
    }

    // ---- editing ---------------------------------------------------------------------------------------------

    /// <summary>Opens the inline editor for a cell (Enter / F2 / double-click / typing).</summary>
    internal bool BeginEditAt(int rowIndex, int columnIndex, string? initialText)
    {
        if (columnIndex < 0 || columnIndex >= CurrentLayout.Columns.Count) return false;
        var column = CurrentLayout.Columns[columnIndex].Column;
        var c = Controller;
        if (!c.CanEdit(rowIndex, column)) return false;
        CloseEditor();
        c.Active = new GridCell(rowIndex, columnIndex);

        if (column.Type == GridColumnType.Boolean)
        {
            // Booleans toggle in place.
            var row = c.RowAt(rowIndex)!;
            if (!c.BeginEdit(rowIndex, column)) return false;
            c.Edit.SetDraft(!(GridValues.ToBool(c.Value(row.Item!, row.Key, column)) ?? false));
            c.CommitEdit();
            return true;
        }

        if (!c.BeginEdit(rowIndex, column, initialText)) return false;
        RevealRow(rowIndex);
        RevealColumn(columnIndex);
        _surface?.UpdateLayout();
        if (_surface?.RowView(rowIndex)?.Cell(column.Field) is not { } cell)
        {
            c.CancelEdit();
            return false;
        }

        _editingCell = cell;
        _editor = CreateEditor(column, initialText);
        cell.ShowEditor(_editor);
        cell.SetError(c.Edit.Current?.Error);
        _editor.Loaded += (_, _) => FocusEditor();
        FocusEditor();
        RefreshActive();
        return true;
    }

    private void FocusEditor()
    {
        if (_editor is TextBox tb)
        {
            tb.Focus();
            tb.CaretIndex = tb.Text.Length;
        }
        else _editor?.Focus();
    }

    private FrameworkElement CreateEditor(GridColumn<object> column, string? initialText)
    {
        var c = Controller;
        var edit = c.Edit.Current!;
        FrameworkElement editor;
        if (column.Type == GridColumnType.Enum)
        {
            var combo = new ComboBox { ItemsSource = c.DistinctValues(column.Field), IsEditable = false };
            combo.SelectedItem = column.DisplayText(edit.Draft);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string text && c.Edit.IsEditing) c.Edit.SetDraftText(text);
                _editingCell?.SetError(c.Edit.Current?.Error);
            };
            combo.DropDownClosed += (_, _) => FinishEdit(null, false);
            combo.Loaded += (_, _) => combo.IsDropDownOpen = true;
            editor = combo;
        }
        else
        {
            var text = initialText ?? edit.DraftText ?? column.DisplayText(edit.Draft);
            var box = new TextBox { Text = text, VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0) };
            box.SetResourceReference(StyleProperty, "Sl.TextBox.Bare");
            if (column.Type is GridColumnType.Number or GridColumnType.Date or GridColumnType.Progress)
                box.SetResourceReference(FontFamilyProperty, GridKeys.MonoFont);
            box.TextAlignment = column.EffectiveAlign == GridAlign.End ? TextAlignment.Right : TextAlignment.Left;
            box.TextChanged += (_, _) =>
            {
                if (!c.Edit.IsEditing) return;
                c.Edit.SetDraftText(box.Text);
                _editingCell?.SetError(c.Edit.Current?.Error);
            };
            editor = box;
        }

        AutomationProperties.SetName(editor, $"Edit {column.DisplayTitle}");
        editor.PreviewKeyDown += (_, e) =>
        {
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            switch (e.Key)
            {
                case Key.Enter when !(editor is ComboBox { IsDropDownOpen: true }):
                    e.Handled = true;
                    FinishEdit(GridEditCommitKey.Enter, shift);
                    break;
                case Key.Tab:
                    e.Handled = true;
                    FinishEdit(GridEditCommitKey.Tab, shift);
                    break;
                case Key.Escape:
                    e.Handled = true;
                    CancelEditor();
                    break;
            }
        };
        editor.LostKeyboardFocus += (_, e) =>
        {
            if (_editor != editor || editor.IsKeyboardFocusWithin) return;
            if (editor is ComboBox { IsDropDownOpen: true }) return;
            if (!Controller.Edit.IsEditing) return;
            if (Controller.Edit.Current is { IsValid: true }) FinishEdit(null, false);
            else CancelEditor(refocus: false);
        };
        return editor;
    }

    /// <summary>Commits the open edit (blocked while invalid) and moves like a spreadsheet.</summary>
    internal bool FinishEdit(GridEditCommitKey? key, bool shift)
    {
        var c = Controller;
        if (!c.Edit.IsEditing) { CloseEditor(); return true; }
        if (c.Edit.Current is { IsValid: false } invalid)
        {
            _editingCell?.SetError(invalid.Error);
            Announce(invalid.Error ?? "Invalid value");
            return false;
        }
        CloseEditor();
        var ok = c.CommitEdit(key, shift, CurrentLayout.Columns.Count, _surface?.ViewportHeight ?? 400);
        _keyboardMode = key is not null || _keyboardMode;
        Focus();
        if (key is not null) RevealActive();
        InvalidateRows();
        return ok;
    }

    internal void CancelEditor(bool refocus = true)
    {
        Controller.CancelEdit();
        CloseEditor();
        if (refocus) Focus();
        InvalidateRows();
    }

    /// <summary>The editing row scrolled out of the window: commit when valid, otherwise cancel.</summary>
    internal void OnEditorRecycled()
    {
        var cell = _editingCell;
        _editingCell = null;
        _editor = null;
        cell?.HideEditor();
        Dispatcher.BeginInvoke(() =>
        {
            if (Controller.Edit.Current is { IsValid: true }) Controller.CommitEdit();
            else Controller.CancelEdit();
        });
    }

    private void CloseEditor()
    {
        var cell = _editingCell;
        _editingCell = null;
        _editor = null;
        cell?.HideEditor();
        RefreshActive();
    }

    // ---- column menu, filter popover, chooser, export ---------------------------------------------------------

    private static MenuItem Item(string header, string? icon, Action action, bool enabled = true, bool? check = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is not null) item.Icon = new Icon { Kind = icon, Size = 16 };
        if (check is { } isChecked) { item.IsCheckable = true; item.IsChecked = isChecked; }
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Column menu: sort, filter, group, pin, autosize, hide, columns.</summary>
    internal void OpenColumnMenu(GridHeaderCell header)
    {
        var c = Controller;
        var column = header.Column;
        var field = column.Field;
        var sort = c.State.SortOf(field);
        var pin = CurrentLayout.Find(field)?.Pin ?? GridPin.None;
        var menu = new ContextMenu { PlacementTarget = header, Placement = PlacementMode.Bottom };

        if (column.Sortable)
        {
            menu.Items.Add(Item("Sort ascending", "arrow-up", () => c.SortBy(field, SortDirection.Ascending), check: sort == SortDirection.Ascending));
            menu.Items.Add(Item("Sort descending", "arrow-down", () => c.SortBy(field, SortDirection.Descending), check: sort == SortDirection.Descending));
            if (sort is not null) menu.Items.Add(Item("Clear sort", null, () => c.SortBy(field, null)));
            menu.Items.Add(new Separator());
        }
        if (column.Filterable)
        {
            menu.Items.Add(Item("Filter…", "filter", () => OpenFilterPopover(header)));
            if (c.State.Filters.Any(f => f.Field == field)) menu.Items.Add(Item("Clear filter", null, () => c.ClearFilter(field)));
        }
        if (Groupable && !c.IsServer && ChildrenSelector is null)
        {
            menu.Items.Add(c.State.GroupBy.Contains(field)
                ? Item("Ungroup", "group", () => c.Ungroup(field))
                : Item("Group by " + column.DisplayTitle, "group", () => c.GroupBy(field)));
        }
        menu.Items.Add(new Separator());
        var pinMenu = new MenuItem { Header = "Pin", Icon = new Icon { Kind = "pin", Size = 16 } };
        pinMenu.Items.Add(Item("Pin to start", null, () => c.Pin(field, GridPin.Start), check: pin == GridPin.Start));
        pinMenu.Items.Add(Item("Pin to end", null, () => c.Pin(field, GridPin.End), check: pin == GridPin.End));
        pinMenu.Items.Add(Item("Unpin", null, () => c.Pin(field, GridPin.None), enabled: pin != GridPin.None));
        menu.Items.Add(pinMenu);
        if (column.Resizable) menu.Items.Add(Item("Autosize", null, () => c.AutoFit(field)));
        if (column.Reorderable)
        {
            menu.Items.Add(Item("Move left", "arrow-left", () => c.MoveBy(field, -1)));
            menu.Items.Add(Item("Move right", "arrow-right", () => c.MoveBy(field, 1)));
        }
        if (column.Hideable) menu.Items.Add(Item("Hide column", "eye-off", () => c.Hide(field)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Columns…", "columns", () => OpenColumnChooser(header)));

        ColumnMenuField = field;
        menu.Closed += (_, _) =>
        {
            ColumnMenuField = null;
            header.Refresh();
        };
        menu.IsOpen = true;
    }

    internal void OpenFilterFor(string field)
    {
        if (_header?.Cell(field) is { } header) OpenFilterPopover(header);
    }

    private Popup ShowPopup(FrameworkElement anchor, FrameworkElement content, double width)
    {
        if (_popup is not null) _popup.IsOpen = false;
        var panel = new Border
        {
            Child = content,
            Width = width,
            Padding = new Thickness(12),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(8),
        };
        panel.SetResourceReference(Border.BackgroundProperty, "Sl.Component.Popover.Background.Brush");
        panel.SetResourceReference(Border.BorderBrushProperty, "Sl.Brush.Border.Default");
        panel.SetResourceReference(Border.CornerRadiusProperty, GridKeys.PopoverRadius);
        panel.SetResourceReference(EffectProperty, GridKeys.PopoverShadow);
        panel.SetResourceReference(TextElement.ForegroundProperty, GridKeys.Text);
        var popup = new Popup
        {
            Child = panel,
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false,
        };
        popup.Closed += (_, _) =>
        {
            if (_popup == popup) _popup = null;
            if (!IsKeyboardFocusWithin && anchor.IsVisible) Focus();
        };
        popup.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                popup.IsOpen = false;
                Focus();
            }
        };
        _popup = popup;
        popup.IsOpen = true;
        return popup;
    }

    /// <summary>Type-aware filter editor: operator select plus value editors (TextField, DatePicker, Select).</summary>
    internal void OpenFilterPopover(GridHeaderCell header)
    {
        var c = Controller;
        var column = header.Column;
        var field = column.Field;
        var existing = c.State.Filters.FirstOrDefault(f => f.Field == field);
        var ops = GridController.OperatorsFor(column.Type);

        var stack = new StackPanel();
        var title = new TextBlock { Text = "Filter " + column.DisplayTitle, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(title);

        var opSelect = new Select
        {
            Items = ops.Cast<object>().ToList(),
            ItemText = o => GridController.OperatorText((FilterOperator)o),
            Value = existing?.Operator ?? ops[0],
            Size = ControlSize.Small,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AutomationProperties.SetName(opSelect, "Operator");
        stack.Children.Add(opSelect);

        var values = new StackPanel();
        stack.Children.Add(values);
        Func<GridFilter?> read = () => null;

        void BuildValues()
        {
            values.Children.Clear();
            var op = (FilterOperator)(opSelect.Value ?? ops[0]);
            if (op is FilterOperator.IsEmpty or FilterOperator.IsNotEmpty)
            {
                read = () => new GridFilter(field, op);
                return;
            }
            switch (column.Type)
            {
                case GridColumnType.Enum:
                {
                    var choices = c.DistinctValues(field);
                    var select = new Select { Items = choices.Cast<object>().ToList(), Multiple = true, Searchable = choices.Count > 8, Placeholder = "Any value", Size = ControlSize.Small };
                    var initial = existing?.Operator == FilterOperator.AnyOf ? (existing.Values ?? []).Select(v => (object?)Convert.ToString(v, CultureInfo.InvariantCulture)).ToList() : [];
                    select.Values = new System.Collections.ArrayList(initial);
                    AutomationProperties.SetName(select, "Values");
                    values.Children.Add(select);
                    read = () => select.Values is { Count: > 0 } picked ? new GridFilter(field, FilterOperator.AnyOf, Values: picked.Cast<object?>().ToList()) : null;
                    break;
                }
                case GridColumnType.Boolean:
                {
                    var select = new Select { Items = new List<object> { "Yes", "No" }, Value = existing?.Value is bool b ? (b ? "Yes" : "No") : "Yes", Size = ControlSize.Small };
                    values.Children.Add(select);
                    read = () => new GridFilter(field, FilterOperator.Equals, Equals(select.Value, "Yes"));
                    break;
                }
                case GridColumnType.Date:
                {
                    DatePicker Picker(object? initial) => new()
                    {
                        Value = initial is string s && GridValues.ParseIsoDate(s) is { } d ? DateOnly.FromDateTime(d.Date) : null,
                        Size = ControlSize.Small,
                        Margin = new Thickness(0, 0, 0, 6),
                    };
                    var from = Picker(existing?.Value);
                    values.Children.Add(from);
                    DatePicker? to = null;
                    if (op == FilterOperator.Between)
                    {
                        to = Picker(existing?.Value2);
                        values.Children.Add(to);
                    }
                    static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    read = () => from.Value is null ? null : new GridFilter(field, op, Iso(from.Value), Iso(to?.Value));
                    break;
                }
                default:
                {
                    var numeric = column.Type is GridColumnType.Number or GridColumnType.Progress;
                    TextField Field(object? initial, string placeholder) => new()
                    {
                        Value = Convert.ToString(initial, CultureInfo.CurrentCulture) ?? "",
                        Placeholder = placeholder,
                        Size = ControlSize.Small,
                        Margin = new Thickness(0, 0, 0, 6),
                    };
                    var first = Field(existing?.Operator == op ? existing.Value : null, numeric ? "Value" : "Text");
                    values.Children.Add(first);
                    TextField? second = null;
                    if (op == FilterOperator.Between)
                    {
                        second = Field(existing?.Value2, "and");
                        values.Children.Add(second);
                    }
                    object? Parse(string text) => numeric
                        ? double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) ? d : null
                        : text;
                    read = () =>
                    {
                        if (string.IsNullOrWhiteSpace(first.Value)) return null;
                        var v1 = Parse(first.Value);
                        if (v1 is null) { first.Error = "Enter a number"; return null; }
                        object? v2 = null;
                        if (second is not null)
                        {
                            v2 = Parse(second.Value);
                            if (v2 is null) { second.Error = "Enter a number"; return null; }
                        }
                        return new GridFilter(field, op, v1, v2);
                    };
                    break;
                }
            }
        }

        BuildValues();
        opSelect.ValueChanged += (_, _) => BuildValues();

        Popup? popup = null;
        void Apply()
        {
            if (read() is { } filter) c.SetFilter(filter);
            else c.ClearFilter(field);
            if (popup is not null) popup.IsOpen = false;
            Focus();
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ChromeButton("Clear", null, ButtonVariant.Ghost, (_, _) =>
        {
            c.ClearFilter(field);
            if (popup is not null) popup.IsOpen = false;
            Focus();
        }));
        buttons.Children.Add(ChromeButton("Apply", null, ButtonVariant.Solid, (_, _) => Apply()));
        stack.Children.Add(buttons);
        stack.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is not Button)
            {
                e.Handled = true;
                Apply();
            }
        };

        popup = ShowPopup(header, stack, 280);
        popup.Opened += (_, _) => opSelect.Focus();
    }

    /// <summary>Column chooser: show/hide and pin state for every column.</summary>
    internal void OpenColumnChooser(FrameworkElement anchor)
    {
        var c = Controller;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Columns", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var layoutOrder = c.Layout().Order;
        foreach (var field in layoutOrder)
        {
            if (c.Column(field) is not { } column) continue;
            var hidden = c.State.Column(field)?.Hidden ?? column.Hidden;
            var check = new CheckBox { Content = column.DisplayTitle, IsChecked = !hidden, IsEnabled = column.Hideable, Margin = new Thickness(0, 2, 0, 2) };
            var f = field;
            check.Click += (_, _) => c.Hide(f, check.IsChecked != true);
            stack.Children.Add(check);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ChromeButton("Show all", null, ButtonVariant.Ghost, (_, _) =>
        {
            foreach (var field in layoutOrder) c.Hide(field, false);
            foreach (var check in stack.Children.OfType<CheckBox>()) check.IsChecked = true;
        }));
        stack.Children.Add(buttons);
        var scroll = new ScrollViewer { Content = stack, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var popup = ShowPopup(anchor, scroll, 240);
        popup.Opened += (_, _) => stack.Children.OfType<CheckBox>().FirstOrDefault(cb => cb.IsEnabled)?.Focus();
    }

    /// <summary>Export menu: copy CSV/TSV to the clipboard or save to a file.</summary>
    internal void OpenExportMenu(FrameworkElement anchor)
    {
        var hasSelection = Controller.Selection.Count > 0;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        menu.Items.Add(Item("Save visible rows as CSV…", "download", () => SaveExport(GridExportScope.Visible, csv: true)));
        menu.Items.Add(Item("Save all rows as CSV…", "download", () => SaveExport(GridExportScope.All, csv: true)));
        menu.Items.Add(Item("Save selected rows as CSV…", "download", () => SaveExport(GridExportScope.Selected, csv: true), hasSelection));
        menu.Items.Add(Item("Save visible rows as TSV…", "download", () => SaveExport(GridExportScope.Visible, csv: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy visible rows as CSV", "copy", () => TrySetClipboard(ExportCsv(GridExportScope.Visible))));
        menu.Items.Add(Item("Copy visible rows as TSV", "copy", () => TrySetClipboard(ExportTsv(GridExportScope.Visible))));
        menu.Items.Add(Item("Copy selected rows as TSV", "copy", () => CopySelection(includeHeader: true), hasSelection));
        menu.IsOpen = true;
    }

    private void SaveExport(GridExportScope scope, bool csv)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "export." + (csv ? "csv" : "tsv"),
            Filter = csv ? "CSV (*.csv)|*.csv" : "TSV (*.tsv)|*.tsv",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var text = csv ? ExportCsv(scope) : ExportTsv(scope);
        System.IO.File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
