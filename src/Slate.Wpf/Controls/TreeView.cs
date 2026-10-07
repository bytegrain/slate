using System.Collections;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Slate.Collections;

namespace Slate.Wpf;

/// <summary>A visible row of <see cref="TreeView"/>.</summary>
public sealed record TreeItemView(
    string Id,
    object Item,
    int Depth,
    double Indent,
    string Text,
    string? Icon,
    bool HasChildren,
    bool Expanded,
    bool Loading,
    bool Selected,
    CheckState Check,
    bool ShowCheckbox,
    int PositionInSet,
    int SetSize)
{
    public int Level => Depth + 1;
    public bool? IsChecked => Check switch { CheckState.Checked => true, CheckState.Unchecked => false, _ => null };
    public string Status => HasChildren ? Expanded ? "expanded" : "collapsed" : "";
}

public class TreeItemEventArgs(RoutedEvent e, object source, object item) : RoutedEventArgs(e, source)
{
    public object Item { get; } = item;
}

/// <summary>
/// Hierarchical list (design/api/components.json "TreeView"), rendered as a flat virtualised list over Slate.Core
/// <see cref="TreeModel{T}"/>: WAI-ARIA tree keyboard (Up/Down/Home/End, Right expands/enters, Left collapses/exits,
/// * expands siblings), single/multi/checkbox selection (tri-state propagation), filter that keeps ancestors, and lazy
/// loading through <see cref="LoadChildren"/>.
/// </summary>
/// <remarks>This is Slate's TreeView (Slate.Wpf.TreeView), not System.Windows.Controls.TreeView.</remarks>
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public class TreeView : Control
{
    public const string PartList = "PART_List";

    private static readonly PropertyChangedCallback Remodel = (d, _) => ((TreeView)d).BuildModel();
    private static readonly PropertyChangedCallback Reflatten = (d, _) => ((TreeView)d).Refresh();

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(TreeView), new FrameworkPropertyMetadata(null, Remodel));
    public static readonly DependencyProperty ChildrenSelectorProperty = DependencyProperty.Register(nameof(ChildrenSelector), typeof(Func<object, IEnumerable?>), typeof(TreeView), new FrameworkPropertyMetadata(null, Remodel));
    public static readonly DependencyProperty HasChildrenProperty = DependencyProperty.Register(nameof(HasChildren), typeof(Func<object, bool>), typeof(TreeView), new FrameworkPropertyMetadata(null, Remodel));
    public static readonly DependencyProperty LoadChildrenProperty = DependencyProperty.Register(nameof(LoadChildren), typeof(Func<object, Task<IEnumerable>>), typeof(TreeView), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ItemTextProperty = DependencyProperty.Register(nameof(ItemText), typeof(Func<object, string>), typeof(TreeView), new FrameworkPropertyMetadata(null, Reflatten));
    public static readonly DependencyProperty ItemIconProperty = DependencyProperty.Register(nameof(ItemIcon), typeof(Func<object, string?>), typeof(TreeView), new FrameworkPropertyMetadata(null, Reflatten));
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(TreeView), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty SelectionModeProperty = DependencyProperty.Register(nameof(SelectionMode), typeof(TreeSelectionMode), typeof(TreeView), new FrameworkPropertyMetadata(TreeSelectionMode.Single, Reflatten));

    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems), typeof(IList), typeof(TreeView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Reflatten));

    public static readonly DependencyProperty ExpandedProperty = DependencyProperty.Register(
        nameof(Expanded), typeof(IList), typeof(TreeView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnExpandedChanged));

    public static readonly DependencyProperty FilterProperty = DependencyProperty.Register(nameof(Filter), typeof(string), typeof(TreeView), new FrameworkPropertyMetadata(null, OnFilterChanged));
    public static readonly DependencyProperty DenseProperty = DependencyProperty.Register(nameof(Dense), typeof(bool), typeof(TreeView), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey RowsPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Rows), typeof(IReadOnlyList<TreeItemView>), typeof(TreeView), new FrameworkPropertyMetadata(Array.Empty<TreeItemView>()));
    public static readonly DependencyProperty RowsProperty = RowsPropertyKey.DependencyProperty;

    public static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TreeView));
    public static readonly RoutedEvent ItemActivatedEvent = EventManager.RegisterRoutedEvent(nameof(ItemActivated), RoutingStrategy.Bubble, typeof(EventHandler<TreeItemEventArgs>), typeof(TreeView));

    private readonly ConditionalWeakTable<object, string> _ids = new();
    private int _nextId;
    private TreeModel<object>? _model;
    private HashSet<string> _expanded = new(StringComparer.Ordinal);
    private IReadOnlySet<string>? _visible;
    private string? _focus;
    private string? _anchor;
    private ListBox? _list;
    private bool _syncingExpanded;

    static TreeView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TreeView), new FrameworkPropertyMetadata(typeof(TreeView)));
        FocusableProperty.OverrideMetadata(typeof(TreeView), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(TreeView), new FrameworkPropertyMetadata(false));
    }

    public TreeView() => AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnButtonClick));

    /// <summary>Root items.</summary>
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    public Func<object, IEnumerable?>? ChildrenSelector { get => (Func<object, IEnumerable?>?)GetValue(ChildrenSelectorProperty); set => SetValue(ChildrenSelectorProperty, value); }

    /// <summary>Lazy trees: whether an item has children not loaded yet.</summary>
    public Func<object, bool>? HasChildren { get => (Func<object, bool>?)GetValue(HasChildrenProperty); set => SetValue(HasChildrenProperty, value); }

    /// <summary>Lazy trees: loads an item's children when it is first expanded.</summary>
    public Func<object, Task<IEnumerable>>? LoadChildren { get => (Func<object, Task<IEnumerable>>?)GetValue(LoadChildrenProperty); set => SetValue(LoadChildrenProperty, value); }

    public Func<object, string>? ItemText { get => (Func<object, string>?)GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }
    public Func<object, string?>? ItemIcon { get => (Func<object, string?>?)GetValue(ItemIconProperty); set => SetValue(ItemIconProperty, value); }

    /// <summary>Custom row content (DataContext is the <see cref="TreeItemView"/>; the item is <c>Item</c>).</summary>
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }

    public TreeSelectionMode SelectionMode { get => (TreeSelectionMode)GetValue(SelectionModeProperty); set => SetValue(SelectionModeProperty, value); }

    /// <summary>Selected (or, in checkbox mode, checked) items. Two-way; Slate assigns a new list on every change.</summary>
    public IList? SelectedItems { get => (IList?)GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }

    /// <summary>Expanded items (two-way).</summary>
    public IList? Expanded { get => (IList?)GetValue(ExpandedProperty); set => SetValue(ExpandedProperty, value); }

    /// <summary>Shows matching items and their ancestors (case/diacritic-insensitive).</summary>
    public string? Filter { get => (string?)GetValue(FilterProperty); set => SetValue(FilterProperty, value); }

    public bool Dense { get => (bool)GetValue(DenseProperty); set => SetValue(DenseProperty, value); }

    public IReadOnlyList<TreeItemView> Rows => (IReadOnlyList<TreeItemView>)GetValue(RowsProperty);

    public event RoutedEventHandler SelectionChanged { add => AddHandler(SelectionChangedEvent, value); remove => RemoveHandler(SelectionChangedEvent, value); }

    /// <summary>Enter or double-click on an item.</summary>
    public event EventHandler<TreeItemEventArgs> ItemActivated { add => AddHandler(ItemActivatedEvent, value); remove => RemoveHandler(ItemActivatedEvent, value); }

    // ---- model ----

    private string IdOf(object item) => _ids.GetValue(item, _ => (++_nextId).ToString(System.Globalization.CultureInfo.InvariantCulture));

    private void BuildModel()
    {
        var children = ChildrenSelector;
        _model = new TreeModel<object>(Items?.Cast<object>() ?? [], IdOf, n => children?.Invoke(n)?.Cast<object>(), HasChildren);
        SyncExpandedFromProperty();
        ApplyFilter();
    }

    private void SyncExpandedFromProperty()
    {
        _expanded = new HashSet<string>((Expanded?.Cast<object>() ?? []).Select(IdOf), StringComparer.Ordinal);
    }

    private static void OnExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TreeView)d;
        if (t._syncingExpanded) return;
        t.SyncExpandedFromProperty();
        t.Refresh();
    }

    private static void OnFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TreeView)d).ApplyFilter();

    private string TextOf(object item) => ItemText?.Invoke(item) ?? item.ToString() ?? "";

    private void ApplyFilter()
    {
        if (_model is null) return;
        if (string.IsNullOrWhiteSpace(Filter))
        {
            _visible = null;
        }
        else
        {
            var query = TextFolding.Fold(Filter.Trim());
            var (visible, expand) = _model.Filter(n => TextFolding.Fold(TextOf(n)).Contains(query, StringComparison.Ordinal));
            _visible = visible;
            _expanded.UnionWith(expand);
        }
        Refresh();
    }

    private HashSet<string> SelectedIds() =>
        new((SelectedItems?.Cast<object>() ?? []).Select(IdOf), StringComparer.Ordinal);

    /// <summary>Re-flattens the tree into <see cref="Rows"/>.</summary>
    public void Refresh()
    {
        if (_model is null)
        {
            SetValue(RowsPropertyKey, Array.Empty<TreeItemView>());
            return;
        }

        var indent = TryFindResource("Sl.Component.Tree.Indent") is double i ? i : 20;
        var selected = SelectedIds();
        var checkbox = SelectionMode == TreeSelectionMode.Checkbox;
        var rows = _model.Flatten(_expanded, _visible).Select(r =>
        {
            var item = _model.Node(r.Id);
            return new TreeItemView(r.Id, item, r.Depth, r.Depth * indent, TextOf(item), ItemIcon?.Invoke(item), r.HasChildren, r.Expanded,
                _model.LoadState(r.Id) == TreeLoadState.Loading,
                !checkbox && selected.Contains(r.Id),
                checkbox ? _model.GetCheckState(r.Id, selected) : CheckState.Unchecked,
                checkbox, r.PositionInSet, r.SetSize);
        }).ToList();
        SetValue(RowsPropertyKey, rows);
        SyncActive();
    }

    private void SyncActive()
    {
        if (_list is null) return;
        var row = Rows.FirstOrDefault(r => r.Id == _focus);
        if (!ReferenceEquals(_list.SelectedItem, row))
            _list.SelectedItem = row;
    }

    private void PublishExpanded()
    {
        if (_model is null) return;
        _syncingExpanded = true;
        try
        {
            SetCurrentValue(ExpandedProperty, _expanded.Select(_model.Node).ToList());
        }
        finally
        {
            _syncingExpanded = false;
        }
    }

    private void SetSelection(IEnumerable<string> ids)
    {
        if (_model is null) return;
        SetCurrentValue(SelectedItemsProperty, ids.Distinct().Select(_model.Node).ToList());
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent, this));
    }

    // ---- expansion & lazy loading ----

    /// <summary>Expands or collapses an item (loading lazy children on first expand).</summary>
    public void Toggle(object item) => SetExpanded(IdOf(item), !_expanded.Contains(IdOf(item)));

    private void SetExpanded(string id, bool expanded)
    {
        if (expanded) _expanded.Add(id);
        else _expanded.Remove(id);
        PublishExpanded();
        if (expanded)
            _ = EnsureLoadedAsync(id);
        Refresh();
    }

    private async Task EnsureLoadedAsync(string id)
    {
        if (_model is null || LoadChildren is not { } load || !_model.BeginLoad(id))
            return;
        Refresh();
        try
        {
            var children = await load(_model.Node(id));
            _model.CompleteLoad(id, children.Cast<object>());
        }
        catch (Exception)
        {
            _model.FailLoad(id);
            _expanded.Remove(id);
            PublishExpanded();
        }
        Refresh();
    }

    // ---- input ----

    public override void OnApplyTemplate()
    {
        if (_list is not null)
        {
            _list.PreviewKeyDown -= OnListKeyDown;
            _list.PreviewMouseLeftButtonDown -= OnListMouseDown;
            _list.MouseDoubleClick -= OnListDoubleClick;
        }
        base.OnApplyTemplate();
        _list = GetTemplateChild(PartList) as ListBox;
        if (_list is not null)
        {
            _list.PreviewKeyDown += OnListKeyDown;
            _list.PreviewMouseLeftButtonDown += OnListMouseDown;
            _list.MouseDoubleClick += OnListDoubleClick;
        }
        if (_model is null)
            BuildModel();
        else
            Refresh();
    }

    private void Activate(TreeItemView row) => RaiseEvent(new TreeItemEventArgs(ItemActivatedEvent, this, row.Item));

    /// <summary>Selects a row as a click with the given modifiers would.</summary>
    public void Click(object item, ModifierKeys modifiers = ModifierKeys.None)
    {
        var id = IdOf(item);
        _focus = id;
        switch (SelectionMode)
        {
            case TreeSelectionMode.None:
                break;
            case TreeSelectionMode.Checkbox:
                ToggleCheck(id);
                break;
            case TreeSelectionMode.Multi when modifiers.HasFlag(ModifierKeys.Shift) && _anchor is not null:
                var ids = Rows.Select(r => r.Id).ToList();
                int a = ids.IndexOf(_anchor), b = ids.IndexOf(id);
                if (a >= 0 && b >= 0)
                    SetSelection(ids.GetRange(Math.Min(a, b), Math.Abs(a - b) + 1));
                break;
            case TreeSelectionMode.Multi when modifiers.HasFlag(ModifierKeys.Control):
                var set = SelectedIds();
                if (!set.Remove(id)) set.Add(id);
                _anchor = id;
                SetSelection(set);
                break;
            default:
                _anchor = id;
                SetSelection([id]);
                break;
        }
        Refresh();
    }

    private void ToggleCheck(string id)
    {
        if (_model is null) return;
        SetSelection(_model.ToggleCheck(id, SelectedIds()));
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "PART_Expander", DataContext: TreeItemView row })
        {
            _focus = row.Id;
            SetExpanded(row.Id, !row.Expanded);
            e.Handled = true;
        }
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source || source.DataContext is not TreeItemView row)
            return;
        // Expander buttons handle themselves; the checkbox and row toggle/select through Click.
        for (DependencyObject? n = source; n is not null && n is not ListBoxItem; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
        {
            if (n is FrameworkElement { Name: "PART_Expander" })
                return;
        }
        Click(row.Item, Keyboard.Modifiers);
        if (_list?.ItemContainerGenerator.ContainerFromItem(Rows.FirstOrDefault(r => r.Id == row.Id)) is ListBoxItem container)
            container.Focus();
        e.Handled = true;
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: TreeItemView row })
        {
            if (row.HasChildren)
                SetExpanded(row.Id, !row.Expanded);
            Activate(row);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null) return;
        TreeKey? key = e.Key switch
        {
            System.Windows.Input.Key.Up => TreeKey.Up,
            System.Windows.Input.Key.Down => TreeKey.Down,
            System.Windows.Input.Key.Home => TreeKey.Home,
            System.Windows.Input.Key.End => TreeKey.End,
            System.Windows.Input.Key.Right => TreeKey.Right,
            System.Windows.Input.Key.Left => TreeKey.Left,
            System.Windows.Input.Key.Multiply => TreeKey.ExpandSiblings,
            _ => null,
        };

        _focus ??= Rows.FirstOrDefault()?.Id;
        if (key is { } k)
        {
            var before = new HashSet<string>(_expanded, StringComparer.Ordinal);
            var result = _model.Navigate(_focus, k, _expanded, _visible);
            _expanded = new HashSet<string>(result.Expanded, StringComparer.Ordinal);
            _focus = result.FocusId;
            if (!_expanded.SetEquals(before))
            {
                PublishExpanded();
                foreach (var id in _expanded.Except(before))
                    _ = EnsureLoadedAsync(id);
            }
            // Single selection follows focus; multi extends with Shift.
            if (_focus is not null && k is TreeKey.Up or TreeKey.Down or TreeKey.Home or TreeKey.End)
            {
                if (SelectionMode == TreeSelectionMode.Single)
                    SetSelection([_focus]);
                else if (SelectionMode == TreeSelectionMode.Multi && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    Click(_model.Node(_focus), ModifierKeys.Shift);
            }
            Refresh();
            FocusRow();
            e.Handled = true;
            return;
        }

        var focused = Rows.FirstOrDefault(r => r.Id == _focus);
        if (focused is null) return;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Space:
                Click(focused.Item, SelectionMode == TreeSelectionMode.Multi ? ModifierKeys.Control : ModifierKeys.None);
                FocusRow();
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Enter:
                Activate(focused);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.A when SelectionMode == TreeSelectionMode.Multi && Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                SetSelection(Rows.Select(r => r.Id));
                Refresh();
                e.Handled = true;
                break;
        }
    }

    private void FocusRow() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
    {
        if (_list is null) return;
        var row = Rows.FirstOrDefault(r => r.Id == _focus);
        if (row is null) return;
        _list.ScrollIntoView(row);
        (_list.ItemContainerGenerator.ContainerFromItem(row) as ListBoxItem)?.Focus();
    });
}

/// <summary>The row host of <see cref="TreeView"/>: a list whose items present to UI Automation as tree items.</summary>
public class TreeViewRows : ListBox
{
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new TreeViewRowsAutomationPeer(this);
}

internal sealed class TreeViewRowsAutomationPeer(TreeViewRows owner) : System.Windows.Automation.Peers.ListBoxAutomationPeer(owner)
{
    protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
        System.Windows.Automation.Peers.AutomationControlType.Tree;

    protected override System.Windows.Automation.Peers.ItemAutomationPeer CreateItemAutomationPeer(object item) => new TreeRowAutomationPeer(item, this);
}

internal sealed class TreeRowAutomationPeer(object item, System.Windows.Automation.Peers.SelectorAutomationPeer parent)
    : System.Windows.Automation.Peers.ListBoxItemAutomationPeer(item, parent), System.Windows.Automation.Provider.IExpandCollapseProvider
{
    private TreeItemView? Row => Item as TreeItemView;
    private TreeView? Tree => (ItemsControlAutomationPeer.Owner as FrameworkElement)?.TemplatedParent as TreeView;

    protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
        System.Windows.Automation.Peers.AutomationControlType.TreeItem;

    protected override string GetClassNameCore() => "TreeViewItem";

    protected override int GetPositionInSetCore() => Row?.PositionInSet ?? base.GetPositionInSetCore();

    protected override int GetSizeOfSetCore() => Row?.SetSize ?? base.GetSizeOfSetCore();

    protected override string GetItemStatusCore() => Row is { } r ? $"level {r.Level}" + (r.Status.Length > 0 ? ", " + r.Status : "") : base.GetItemStatusCore();

    public override object GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface) =>
        patternInterface == System.Windows.Automation.Peers.PatternInterface.ExpandCollapse && Row is { HasChildren: true }
            ? this
            : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState => Row switch
    {
        { HasChildren: false } or null => ExpandCollapseState.LeafNode,
        { Expanded: true } => ExpandCollapseState.Expanded,
        _ => ExpandCollapseState.Collapsed,
    };

    public void Expand() { if (Row is { Expanded: false } r) Tree?.Toggle(r.Item); }

    public void Collapse() { if (Row is { Expanded: true } r) Tree?.Toggle(r.Item); }
}
