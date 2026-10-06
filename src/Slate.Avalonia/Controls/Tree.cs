using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Slate.Collections;

namespace Slate.Avalonia.Controls;

/// <summary>A visible row of a <see cref="TreeList"/> (data for the row container).</summary>
public sealed class TreeListRowData(TreeList owner, TreeRow row, object item)
{
    public TreeList Owner { get; } = owner;
    public TreeRow Row { get; } = row;
    public object Item { get; } = item;
    public string Id => Row.Id;
    public int Depth => Row.Depth;
}

/// <summary>
/// Hierarchical list (docs: TreeView; Avalonia spelling TreeList because Avalonia.Controls.TreeView exists).
/// Flattened and virtualised; keyboard per the tree pattern (Up/Down, Home/End, Right expands/enters, Left
/// collapses/leaves, * expands siblings, typeahead, Space selects/checks, Enter activates); single, multi or
/// checkbox (tri-state) selection; <see cref="Filter"/> keeps ancestors of matches; lazy <see cref="LoadChildren"/>.
/// All tree logic comes from Slate.Core's <see cref="TreeModel{T}"/>.
/// </summary>
[TemplatePart("PART_Items", typeof(ItemsControl))]
[PseudoClasses(":dense")]
public class TreeList : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<TreeList, IEnumerable?>(nameof(Items));
    public static readonly StyledProperty<Func<object, IEnumerable?>?> ChildrenSelectorProperty = AvaloniaProperty.Register<TreeList, Func<object, IEnumerable?>?>(nameof(ChildrenSelector));
    public static readonly StyledProperty<Func<object, bool>?> HasChildrenProperty = AvaloniaProperty.Register<TreeList, Func<object, bool>?>(nameof(HasChildren));
    public static readonly StyledProperty<Func<object, Task<IEnumerable>>?> LoadChildrenProperty = AvaloniaProperty.Register<TreeList, Func<object, Task<IEnumerable>>?>(nameof(LoadChildren));
    public static readonly StyledProperty<Func<object, string>?> ItemTextProperty = AvaloniaProperty.Register<TreeList, Func<object, string>?>(nameof(ItemText));
    public static readonly StyledProperty<Func<object, string?>?> ItemIconProperty = AvaloniaProperty.Register<TreeList, Func<object, string?>?>(nameof(ItemIcon));
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty = AvaloniaProperty.Register<TreeList, IDataTemplate?>(nameof(ItemTemplate));
    public static readonly StyledProperty<TreeSelectionMode> SelectionModeProperty = AvaloniaProperty.Register<TreeList, TreeSelectionMode>(nameof(SelectionMode), TreeSelectionMode.Single);
    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<TreeList, IList?>(nameof(SelectedItems), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<IList?> ExpandedProperty =
        AvaloniaProperty.Register<TreeList, IList?>(nameof(Expanded), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> FilterProperty = AvaloniaProperty.Register<TreeList, string?>(nameof(Filter));
    public static readonly StyledProperty<bool> DenseProperty = AvaloniaProperty.Register<TreeList, bool>(nameof(Dense));

    public static readonly RoutedEvent<RoutedEventArgs> SelectionChangedEvent = RoutedEvent.Register<TreeList, RoutedEventArgs>(nameof(SelectionChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> ItemActivatedEvent = RoutedEvent.Register<TreeList, RoutedEventArgs>(nameof(ItemActivated), RoutingStrategies.Bubble);

    private static readonly ConditionalWeakTable<object, string> Ids = new();
    private static long _nextId;

    private readonly ObservableCollection<TreeListRowData> _rows = [];
    private readonly HashSet<string> _expanded = [];
    private readonly HashSet<string> _selected = [];
    private readonly Typeahead _typeahead = new();
    private TreeModel<object>? _model;
    private IReadOnlySet<string>? _visible;
    private ItemsControl? _itemsControl;
    private string? _focus;
    private string? _anchor;
    private bool _syncing;

    static TreeList()
    {
        FocusableProperty.OverrideDefaultValue<TreeList>(true);
        foreach (var p in new AvaloniaProperty[] { ItemsProperty, ChildrenSelectorProperty, HasChildrenProperty })
            p.Changed.AddClassHandler<TreeList>((t, _) => t.Rebuild());
        FilterProperty.Changed.AddClassHandler<TreeList>((t, _) => t.ApplyFilter());
        ExpandedProperty.Changed.AddClassHandler<TreeList>((t, _) => t.ReadExpanded());
        SelectedItemsProperty.Changed.AddClassHandler<TreeList>((t, _) => t.ReadSelected());
        DenseProperty.Changed.AddClassHandler<TreeList>((t, e) => t.PseudoClasses.Set(":dense", e.GetNewValue<bool>()));
        SelectionModeProperty.Changed.AddClassHandler<TreeList>((t, _) => t.Render());
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public Func<object, IEnumerable?>? ChildrenSelector { get => GetValue(ChildrenSelectorProperty); set => SetValue(ChildrenSelectorProperty, value); }
    public Func<object, bool>? HasChildren { get => GetValue(HasChildrenProperty); set => SetValue(HasChildrenProperty, value); }
    public Func<object, Task<IEnumerable>>? LoadChildren { get => GetValue(LoadChildrenProperty); set => SetValue(LoadChildrenProperty, value); }
    public Func<object, string>? ItemText { get => GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }
    public Func<object, string?>? ItemIcon { get => GetValue(ItemIconProperty); set => SetValue(ItemIconProperty, value); }
    public IDataTemplate? ItemTemplate { get => GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public TreeSelectionMode SelectionMode { get => GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }
    public IList? SelectedItems { get => GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }
    public IList? Expanded { get => GetValue(ExpandedProperty); set => SetValue(ExpandedProperty, value); }
    public string? Filter { get => GetValue(FilterProperty); set => SetValue(FilterProperty, value); }
    public bool Dense { get => GetValue(DenseProperty); set => SetValue(DenseProperty, value); }

    public event EventHandler<RoutedEventArgs>? SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? ItemActivated { add => AddHandler(ItemActivatedEvent, value); remove => RemoveHandler(ItemActivatedEvent, value); }

    /// <summary>Visible rows in display order.</summary>
    public IReadOnlyList<TreeListRowData> Rows => _rows;

    /// <summary>The keyboard-focused item (the active row).</summary>
    public object? FocusedItem => _focus is { } id && _model is not null ? _model.Node(id) : null;

    /// <summary>The most recently activated item (Enter / double-click).</summary>
    public object? ActivatedItem { get; private set; }

    public string TextOf(object item) => ItemText?.Invoke(item) ?? item.ToString() ?? "";

    internal static string IdOf(object item) => Ids.GetValue(item, _ => Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture));

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _itemsControl = e.NameScope.Find<ItemsControl>("PART_Items");
        if (_itemsControl is not null)
        {
            _itemsControl.ItemsSource = _rows;
            _itemsControl.ItemTemplate = new FuncDataTemplate<TreeListRowData>((row, _) => new TreeListRow(), supportsRecycling: true);
        }
        Rebuild();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TreePeer(this);

    // ---- model -------------------------------------------------------------------------------------------

    private void Rebuild()
    {
        var roots = Items?.Cast<object>().ToList() ?? [];
        var children = ChildrenSelector;
        var hasChildren = HasChildren;
        _model = new TreeModel<object>(roots, IdOf, n => children?.Invoke(n)?.Cast<object>(), hasChildren);
        ReadExpanded();
        ReadSelected();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_model is null) return;
        if (string.IsNullOrWhiteSpace(Filter))
        {
            _visible = null;
        }
        else
        {
            var folded = TextFolding.Fold(Filter.Trim());
            var (visible, expand) = _model.Filter(n => TextFolding.Fold(TextOf(n)).Contains(folded, StringComparison.Ordinal));
            _visible = visible;
            _expanded.UnionWith(expand);
        }
        Render();
    }

    private void ReadExpanded()
    {
        if (_syncing) return;
        _expanded.Clear();
        if (Expanded is not null)
            foreach (var item in Expanded) if (item is not null) _expanded.Add(IdOf(item));
        Render();
    }

    private void ReadSelected()
    {
        if (_syncing) return;
        _selected.Clear();
        if (SelectedItems is not null)
            foreach (var item in SelectedItems) if (item is not null) _selected.Add(IdOf(item));
        Render();
    }

    private void Render()
    {
        if (_model is null) return;
        var rows = _model.Flatten(_expanded, _visible);
        _rows.Clear();
        foreach (var r in rows) _rows.Add(new TreeListRowData(this, r, _model.Node(r.Id)));
        if (_focus is null || rows.All(r => r.Id != _focus)) _focus = rows.FirstOrDefault()?.Id;
        RefreshRows();
    }

    internal void RefreshRows()
    {
        if (_itemsControl is null) return;
        foreach (var row in _itemsControl.GetRealizedContainers().Select(c => c is ContentPresenter p ? p.Child : c).OfType<TreeListRow>())
            row.Refresh();
    }

    internal bool IsSelected(string id) => _selected.Contains(id);
    internal bool IsFocusedRow(string id) => id == _focus && IsKeyboardFocusWithin;
    internal CheckState CheckStateOf(string id) => _model?.GetCheckState(id, _selected) ?? CheckState.Unchecked;
    internal TreeLoadState LoadStateOf(string id) => _model?.LoadState(id) ?? TreeLoadState.Loaded;

    // ---- interaction ---------------------------------------------------------------------------------------

    internal void RowPressed(TreeListRowData row, KeyModifiers modifiers, int clickCount)
    {
        Focus();
        _focus = row.Id;
        if (clickCount >= 2)
        {
            Activate(row.Id);
            return;
        }
        switch (SelectionMode)
        {
            case TreeSelectionMode.Checkbox:
                ToggleCheck(row.Id);
                break;
            case TreeSelectionMode.Multi when modifiers.HasFlag(KeyModifiers.Shift) && _anchor is not null:
                SelectRange(_anchor, row.Id);
                break;
            case TreeSelectionMode.Multi when modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta):
                if (!_selected.Remove(row.Id)) _selected.Add(row.Id);
                _anchor = row.Id;
                PublishSelection();
                break;
            case TreeSelectionMode.None:
                break;
            default:
                _selected.Clear();
                _selected.Add(row.Id);
                _anchor = row.Id;
                PublishSelection();
                break;
        }
        RefreshRows();
    }

    internal async void ToggleExpand(string id)
    {
        if (_model is null) return;
        if (!_expanded.Remove(id))
        {
            _expanded.Add(id);
            if (_model.LoadState(id) is TreeLoadState.NotLoaded or TreeLoadState.Failed && LoadChildren is { } load && _model.BeginLoad(id))
            {
                Render();
                try
                {
                    var children = await load(_model.Node(id));
                    _model.CompleteLoad(id, children.Cast<object>());
                }
                catch
                {
                    _model.FailLoad(id);
                }
            }
        }
        PublishExpanded();
        Render();
    }

    internal void ToggleCheck(string id)
    {
        if (_model is null) return;
        var next = _model.ToggleCheck(id, _selected);
        _selected.Clear();
        _selected.UnionWith(next);
        PublishSelection();
        RefreshRows();
    }

    private void SelectRange(string from, string to)
    {
        var ids = _rows.Select(r => r.Id).ToList();
        int a = ids.IndexOf(from), b = ids.IndexOf(to);
        if (a < 0 || b < 0) return;
        _selected.Clear();
        foreach (var id in ids.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)) _selected.Add(id);
        PublishSelection();
    }

    private void Activate(string id)
    {
        if (_model is null) return;
        ActivatedItem = _model.Node(id);
        RaiseEvent(new RoutedEventArgs(ItemActivatedEvent));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _model is null || _rows.Count == 0) return;

        TreeKey? key = e.Key switch
        {
            Key.Up => TreeKey.Up,
            Key.Down => TreeKey.Down,
            Key.Home => TreeKey.Home,
            Key.End => TreeKey.End,
            Key.Right => TreeKey.Right,
            Key.Left => TreeKey.Left,
            Key.Multiply => TreeKey.ExpandSiblings,
            _ => null,
        };

        if (key is { } k)
        {
            var before = new HashSet<string>(_expanded);
            // Right on an unloaded lazy node starts loading through ToggleExpand.
            if (k == TreeKey.Right && _focus is { } f && _model.LoadState(f) is TreeLoadState.NotLoaded && !_expanded.Contains(f))
            {
                ToggleExpand(f);
                e.Handled = true;
                return;
            }
            var nav = _model.Navigate(_focus, k, _expanded, _visible);
            _focus = nav.FocusId;
            _expanded.Clear();
            _expanded.UnionWith(nav.Expanded);
            if (!before.SetEquals(_expanded)) { PublishExpanded(); Render(); }

            if (SelectionMode == TreeSelectionMode.Single && k is TreeKey.Up or TreeKey.Down or TreeKey.Home or TreeKey.End && _focus is { } id)
            {
                _selected.Clear();
                _selected.Add(id);
                _anchor = id;
                PublishSelection();
            }
            else if (SelectionMode == TreeSelectionMode.Multi && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _anchor is not null && _focus is not null)
            {
                SelectRange(_anchor, _focus);
            }
            ScrollToFocus();
            RefreshRows();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && _focus is { } focus)
        {
            if (SelectionMode == TreeSelectionMode.Checkbox) ToggleCheck(focus);
            else if (SelectionMode != TreeSelectionMode.None)
            {
                if (SelectionMode == TreeSelectionMode.Multi) { if (!_selected.Remove(focus)) _selected.Add(focus); }
                else { _selected.Clear(); _selected.Add(focus); }
                _anchor = focus;
                PublishSelection();
                RefreshRows();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _focus is { } active)
        {
            Activate(active);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || e.Text is " " or "*") return;
        var labels = _rows.Select(r => TextOf(r.Item)).ToList();
        var current = _rows.ToList().FindIndex(r => r.Id == _focus);
        var found = _typeahead.Search(e.Text, Environment.TickCount64, labels, current);
        if (found < 0) return;
        _focus = _rows[found].Id;
        ScrollToFocus();
        RefreshRows();
        e.Handled = true;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RefreshRows();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        RefreshRows();
    }

    private void ScrollToFocus()
    {
        var index = _rows.ToList().FindIndex(r => r.Id == _focus);
        if (index >= 0) _itemsControl?.ScrollIntoView(index);
    }

    private void PublishSelection()
    {
        if (_model is null) return;
        _syncing = true;
        try { SelectedItems = _selected.Select(id => _model.Node(id)).ToList(); }
        finally { _syncing = false; }
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent));
    }

    private void PublishExpanded()
    {
        if (_model is null) return;
        _syncing = true;
        try { Expanded = _expanded.Select(id => _model.Node(id)).ToList(); }
        finally { _syncing = false; }
    }

    private sealed class TreePeer(TreeList owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
    }
}

/// <summary>Row container of a <see cref="TreeList"/>: indent, expander, optional checkbox, icon and label.</summary>
[TemplatePart("PART_Expander", typeof(Button))]
[TemplatePart("PART_Check", typeof(CheckBox))]
[PseudoClasses(":selected", ":focused-row", ":expanded", ":leaf", ":loading", ":checkbox")]
public class TreeListRow : TemplatedControl
{
    public static readonly StyledProperty<double> IndentProperty = AvaloniaProperty.Register<TreeListRow, double>(nameof(Indent));
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<TreeListRow, string?>(nameof(Icon));
    public static readonly StyledProperty<object?> LabelProperty = AvaloniaProperty.Register<TreeListRow, object?>(nameof(Label));
    public static readonly StyledProperty<bool?> CheckedProperty = AvaloniaProperty.Register<TreeListRow, bool?>(nameof(Checked));

    private Button? _expander;
    private CheckBox? _check;

    public double Indent { get => GetValue(IndentProperty); set => SetValue(IndentProperty, value); }
    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool? Checked { get => GetValue(CheckedProperty); set => SetValue(CheckedProperty, value); }

    private TreeListRowData? Data => DataContext as TreeListRowData;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Refresh();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _expander = e.NameScope.Find<Button>("PART_Expander");
        _check = e.NameScope.Find<CheckBox>("PART_Check");
        if (_expander is not null) _expander.Click += (_, ev) => { if (Data is { } d) d.Owner.ToggleExpand(d.Id); ev.Handled = true; };
        if (_check is not null) _check.Click += (_, ev) => { if (Data is { } d) d.Owner.ToggleCheck(d.Id); ev.Handled = true; };
        Refresh();
    }

    internal void Refresh()
    {
        if (Data is not { } d) return;
        var owner = d.Owner;
        var indent = owner.TryFindResource("Sl.Component.Tree.Indent", ActualThemeVariant, out var v) && v is double px ? px : 20;
        Indent = d.Depth * indent;
        Icon = owner.ItemIcon?.Invoke(d.Item);
        Label = owner.ItemTemplate is { } t ? t.Build(d.Item) : owner.TextOf(d.Item);
        if (Label is Control c) c.DataContext = d.Item;

        var checkbox = owner.SelectionMode == TreeSelectionMode.Checkbox;
        var state = checkbox ? owner.CheckStateOf(d.Id) : CheckState.Unchecked;
        Checked = state switch { CheckState.Checked => true, CheckState.Indeterminate => null, _ => false };
        var load = owner.LoadStateOf(d.Id);

        PseudoClasses.Set(":checkbox", checkbox);
        PseudoClasses.Set(":selected", !checkbox && owner.IsSelected(d.Id));
        PseudoClasses.Set(":focused-row", owner.IsFocusedRow(d.Id));
        PseudoClasses.Set(":expanded", d.Row.Expanded);
        PseudoClasses.Set(":leaf", !d.Row.HasChildren);
        PseudoClasses.Set(":loading", load == TreeLoadState.Loading);

        AutomationProperties.SetName(this, owner.TextOf(d.Item));
        AutomationProperties.SetPositionInSet(this, d.Row.PositionInSet);
        AutomationProperties.SetSizeOfSet(this, d.Row.SetSize);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Data is not { } d || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        d.Owner.RowPressed(d, e.KeyModifiers, e.ClickCount);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer(TreeListRow owner) : ControlAutomationPeer(owner), IExpandCollapseProvider, ISelectionItemProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;

        public ExpandCollapseState ExpandCollapseState => owner.Data is not { } d || !d.Row.HasChildren
            ? ExpandCollapseState.LeafNode
            : d.Row.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => false;
        public void Expand() { if (owner.Data is { Row.Expanded: false } d) d.Owner.ToggleExpand(d.Id); }
        public void Collapse() { if (owner.Data is { Row.Expanded: true } d) d.Owner.ToggleExpand(d.Id); }

        public bool IsSelected => owner.Data is { } d && d.Owner.IsSelected(d.Id);
        public ISelectionProvider? SelectionContainer => null;
        public void AddToSelection() => Select();
        public void RemoveFromSelection() => Select();
        public void Select() { if (owner.Data is { } d) d.Owner.RowPressed(d, KeyModifiers.Control, 1); }
    }
}
