using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Slate.Collections;

namespace Slate.Wpf;

/// <summary>
/// Tab strip + panels (design/api/components.json "Tabs"). Items are <see cref="Tab"/>s (or anything, wrapped in one).
/// <see cref="Value"/> is the active tab's <see cref="Tab.Key"/>. Arrow keys move between tabs (TabControl behaviour);
/// Delete closes a closable tab.
/// </summary>
[TemplatePart(Name = PartKeepAliveHost, Type = typeof(Grid))]
[TemplatePart(Name = PartSelectedContentHost, Type = typeof(ContentPresenter))]
public class Tabs : TabControl
{
    public const string PartKeepAliveHost = "PART_KeepAliveHost";
    public const string PartSelectedContentHost = "PART_SelectedContentHost";

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(Tabs), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(TabsVariant), typeof(Tabs), new FrameworkPropertyMetadata(TabsVariant.Line, (d, _) => ((Tabs)d).SyncTabs()));

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Tabs),
        new FrameworkPropertyMetadata(ControlSize.Medium, (d, _) => ((Tabs)d).SyncTabs()));

    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(Direction), typeof(Tabs), new FrameworkPropertyMetadata(Direction.Row, OnDirectionChanged));

    public static readonly DependencyProperty KeepAliveProperty = DependencyProperty.Register(
        nameof(KeepAlive), typeof(bool), typeof(Tabs), new FrameworkPropertyMetadata(false, (d, _) => ((Tabs)d).SyncKeepAlive()));

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<string?>), typeof(Tabs));

    private Grid? _keepAliveHost;
    private ContentPresenter? _selectedHost;
    private readonly Dictionary<object, ContentPresenter> _alive = new();
    private bool _syncing;

    static Tabs()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Tabs), new FrameworkPropertyMetadata(typeof(Tabs)));
        EventManager.RegisterClassHandler(typeof(Tabs), Tab.ClosedEvent, new RoutedEventHandler(OnTabClosed));
    }

    /// <summary>Key of the active tab (two-way).</summary>
    public string? Value { get => (string?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Line (underline indicator, default), Pills or Enclosed.</summary>
    public TabsVariant Variant { get => (TabsVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Row = tabs above the panel; Column = tabs down the left side.</summary>
    public Direction Direction { get => (Direction)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }

    /// <summary>Keep visited panels alive (state, scroll position) instead of recreating them.</summary>
    public bool KeepAlive { get => (bool)GetValue(KeepAliveProperty); set => SetValue(KeepAliveProperty, value); }

    public event RoutedPropertyChangedEventHandler<string?> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    protected override DependencyObject GetContainerForItemOverride() => new Tab();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is TabItem;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is Tab tab)
            Apply(tab);
    }

    private void Apply(Tab tab)
    {
        tab.SetActualVariant(Variant);
        Sl.SetActualSize(tab, Size);
    }

    private void SyncTabs()
    {
        foreach (var item in Items)
        {
            if ((item as Tab ?? ItemContainerGenerator.ContainerFromItem(item)) is Tab tab)
                Apply(tab);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _keepAliveHost = GetTemplateChild(PartKeepAliveHost) as Grid;
        _selectedHost = GetTemplateChild(PartSelectedContentHost) as ContentPresenter;
        _alive.Clear();
        SyncKeepAlive();
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        SyncKeepAlive();
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            SetCurrentValue(ValueProperty, KeyOf(SelectedItem));
        }
        finally
        {
            _syncing = false;
        }
    }

    private string? KeyOf(object? item)
    {
        if (item is null)
            return null;
        var tab = item as Tab ?? ItemContainerGenerator.ContainerFromItem(item) as Tab;
        return tab?.Key ?? Items.IndexOf(item).ToString(CultureInfo.InvariantCulture);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tabs = (Tabs)d;
        if (!tabs._syncing)
        {
            tabs._syncing = true;
            try
            {
                foreach (var item in tabs.Items)
                {
                    if (tabs.KeyOf(item) == (string?)e.NewValue)
                    {
                        tabs.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                tabs._syncing = false;
            }
        }
        tabs.RaiseEvent(new RoutedPropertyChangedEventArgs<string?>((string?)e.OldValue, (string?)e.NewValue, ValueChangedEvent));
    }

    private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Tabs)d).TabStripPlacement = (Direction)e.NewValue == Direction.Column ? Dock.Left : Dock.Top;

    private void SyncKeepAlive()
    {
        if (_keepAliveHost is null || _selectedHost is null)
            return;

        if (!KeepAlive)
        {
            _keepAliveHost.Children.Clear();
            _alive.Clear();
            _selectedHost.ClearValue(ContentPresenter.ContentProperty);
            _selectedHost.Visibility = Visibility.Visible;
            return;
        }

        _selectedHost.Content = null;
        _selectedHost.Visibility = Visibility.Collapsed;
        var selected = SelectedItem;
        if (selected is not null && !_alive.ContainsKey(selected))
        {
            var content = selected is TabItem t ? t.Content : selected;
            var presenter = new ContentPresenter { Content = content, ContentTemplate = SelectedContentTemplate };
            _alive[selected] = presenter;
            _keepAliveHost.Children.Add(presenter);
        }
        foreach (var (item, presenter) in _alive)
            presenter.Visibility = ReferenceEquals(item, selected) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnTabClosed(object sender, RoutedEventArgs e)
    {
        // Unhandled Closed removes the tab (when the items are not data-bound).
        if (e.Handled || sender is not Tabs tabs || e.OriginalSource is not Tab tab || tabs.ItemsSource is not null)
            return;
        var index = tabs.Items.IndexOf(tab);
        if (index < 0)
            return;
        var wasSelected = tab.IsSelected;
        tabs._alive.Remove(tab);
        tabs.Items.RemoveAt(index);
        if (wasSelected && tabs.Items.Count > 0)
            tabs.SelectedIndex = Math.Min(index, tabs.Items.Count - 1);
        e.Handled = true;
    }
}

/// <summary>A tab in <see cref="Tabs"/>: <see cref="Label"/> (or Header), optional <see cref="Icon"/>, <see cref="Badge"/>, close button.</summary>
public class Tab : TabItem
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(nameof(Key), typeof(string), typeof(Tab), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Tab), new FrameworkPropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(Tab), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(string), typeof(Tab), new FrameworkPropertyMetadata(null));
    public static readonly DependencyProperty ClosableProperty = DependencyProperty.Register(nameof(Closable), typeof(bool), typeof(Tab), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey ActualVariantPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualVariant), typeof(TabsVariant), typeof(Tab), new FrameworkPropertyMetadata(TabsVariant.Line));

    public static readonly DependencyProperty ActualVariantProperty = ActualVariantPropertyKey.DependencyProperty;

    public static readonly RoutedEvent ClosedEvent = EventManager.RegisterRoutedEvent(
        nameof(Closed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Tab));

    static Tab() => DefaultStyleKeyProperty.OverrideMetadata(typeof(Tab), new FrameworkPropertyMetadata(typeof(Tab)));

    public Tab() => AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCloseClick));

    /// <summary>Identity used by <see cref="Tabs.Value"/>.</summary>
    public string? Key { get => (string?)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }

    /// <summary>Tab text (Header is used when no label is set).</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>Short count or status shown after the label.</summary>
    public string? Badge { get => (string?)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

    public bool Closable { get => (bool)GetValue(ClosableProperty); set => SetValue(ClosableProperty, value); }

    /// <summary>The parent's variant (set by <see cref="Tabs"/>).</summary>
    public TabsVariant ActualVariant => (TabsVariant)GetValue(ActualVariantProperty);

    internal void SetActualVariant(TabsVariant v) => SetValue(ActualVariantPropertyKey, v);

    /// <summary>Raised when the close button or Delete is pressed; unhandled, the parent Tabs removes the tab.</summary>
    public event RoutedEventHandler Closed { add => AddHandler(ClosedEvent, value); remove => RemoveHandler(ClosedEvent, value); }

    public void Close() => RaiseEvent(new RoutedEventArgs(ClosedEvent, this));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Closable && e.Key == System.Windows.Input.Key.Delete)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { Name: "PART_Close" })
        {
            Close();
            e.Handled = true;
        }
    }
}

/// <summary>An entry in <see cref="Breadcrumbs.Items"/>.</summary>
public class BreadcrumbItem
{
    public BreadcrumbItem() { }

    public BreadcrumbItem(string label, string? icon = null)
    {
        Label = label;
        Icon = icon;
    }

    public string Label { get; set; } = "";
    public string? Icon { get; set; }
    public object? Tag { get; set; }
    public override string ToString() => Label;
}

/// <summary>What <see cref="Breadcrumbs"/> renders: a crumb, the current page, or the "…" that lists collapsed crumbs.</summary>
public sealed record BreadcrumbEntry(BreadcrumbItem? Item, string Label, string? Icon, bool IsCurrent, bool IsEllipsis, bool ShowSeparator, IReadOnlyList<BreadcrumbItem> Hidden);

public class BreadcrumbClickEventArgs(RoutedEvent e, object source, BreadcrumbItem item) : RoutedEventArgs(e, source)
{
    public BreadcrumbItem Item { get; } = item;
}

/// <summary>Path navigation; with <see cref="MaxItems"/> the middle collapses into a "…" menu. The last item is the current page.</summary>
public class Breadcrumbs : Control
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(Breadcrumbs), new FrameworkPropertyMetadata(null, (d, _) => ((Breadcrumbs)d).Rebuild()));

    public static readonly DependencyProperty MaxItemsProperty = DependencyProperty.Register(
        nameof(MaxItems), typeof(int?), typeof(Breadcrumbs), new FrameworkPropertyMetadata(null, (d, _) => ((Breadcrumbs)d).Rebuild()));

    public static readonly DependencyProperty SeparatorProperty = DependencyProperty.Register(
        nameof(Separator), typeof(string), typeof(Breadcrumbs), new FrameworkPropertyMetadata("chevron-right"));

    private static readonly DependencyPropertyKey EntriesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Entries), typeof(IReadOnlyList<BreadcrumbEntry>), typeof(Breadcrumbs), new FrameworkPropertyMetadata(Array.Empty<BreadcrumbEntry>()));

    public static readonly DependencyProperty EntriesProperty = EntriesPropertyKey.DependencyProperty;

    public static readonly RoutedEvent ItemClickEvent = EventManager.RegisterRoutedEvent(
        nameof(ItemClick), RoutingStrategy.Bubble, typeof(EventHandler<BreadcrumbClickEventArgs>), typeof(Breadcrumbs));

    static Breadcrumbs()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Breadcrumbs), new FrameworkPropertyMetadata(typeof(Breadcrumbs)));
        FocusableProperty.OverrideMetadata(typeof(Breadcrumbs), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Breadcrumbs), new FrameworkPropertyMetadata(false));
    }

    public Breadcrumbs()
    {
        AutomationProperties.SetName(this, "Breadcrumb");
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
    }

    /// <summary><see cref="BreadcrumbItem"/>s (or strings), root first.</summary>
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    /// <summary>Show at most this many crumbs: the first, "…", and the last ones.</summary>
    public int? MaxItems { get => (int?)GetValue(MaxItemsProperty); set => SetValue(MaxItemsProperty, value); }

    /// <summary>Icon between crumbs.</summary>
    public string Separator { get => (string)GetValue(SeparatorProperty); set => SetValue(SeparatorProperty, value); }

    public IReadOnlyList<BreadcrumbEntry> Entries => (IReadOnlyList<BreadcrumbEntry>)GetValue(EntriesProperty);

    public event EventHandler<BreadcrumbClickEventArgs> ItemClick { add => AddHandler(ItemClickEvent, value); remove => RemoveHandler(ItemClickEvent, value); }

    internal static IReadOnlyList<BreadcrumbEntry> Layout(IReadOnlyList<BreadcrumbItem> items, int? maxItems)
    {
        var result = new List<BreadcrumbEntry>();
        if (items.Count == 0)
            return result;

        var max = maxItems is { } m ? Math.Max(2, m) : int.MaxValue;
        BreadcrumbEntry Crumb(BreadcrumbItem item, bool separator) =>
            new(item, item.Label, item.Icon, ReferenceEquals(item, items[^1]), false, separator, []);

        if (items.Count <= max)
        {
            for (var i = 0; i < items.Count; i++)
                result.Add(Crumb(items[i], i > 0));
            return result;
        }

        var tail = max - 1; // first + "…" + (max - 1) last items
        result.Add(Crumb(items[0], false));
        result.Add(new BreadcrumbEntry(null, "…", null, false, true, true, items.Skip(1).Take(items.Count - 1 - tail).ToList()));
        foreach (var item in items.Skip(items.Count - tail))
            result.Add(Crumb(item, true));
        return result;
    }

    private void Rebuild()
    {
        var items = (Items?.Cast<object>() ?? []).Select(o => o as BreadcrumbItem ?? new BreadcrumbItem(o?.ToString() ?? "")).ToList();
        SetValue(EntriesPropertyKey, Layout(items, MaxItems));
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement { DataContext: BreadcrumbEntry entry } button)
            return;
        e.Handled = true;

        if (entry.IsEllipsis)
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var hidden in entry.Hidden)
            {
                var mi = new MenuItem { Header = hidden.Label };
                if (hidden.Icon is { } icon)
                    Sl.SetStartIcon(mi, icon);
                var captured = hidden;
                mi.Click += (_, _) => RaiseEvent(new BreadcrumbClickEventArgs(ItemClickEvent, this, captured));
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
            return;
        }

        if (entry.Item is { } item && !entry.IsCurrent)
            RaiseEvent(new BreadcrumbClickEventArgs(ItemClickEvent, this, item));
    }
}

/// <summary>A page button, ellipsis, or the current page in <see cref="Pagination"/>.</summary>
public sealed record PageEntry(int Page, bool IsEllipsis, bool IsCurrent)
{
    public string Label => IsEllipsis ? "…" : Page.ToString(CultureInfo.CurrentCulture);
    public string AccessibleName => IsEllipsis ? "More pages" : $"Page {Page}";
}

/// <summary>
/// Page navigation (design/api/components.json "Pagination"): previous/next, page buttons with ellipses (Slate.Core
/// <see cref="PaginationRange"/>), optional page-size picker and "1–50 of 1,234" summary.
/// </summary>
[TemplatePart(Name = PartPrevious, Type = typeof(ButtonBase))]
[TemplatePart(Name = PartNext, Type = typeof(ButtonBase))]
public class Pagination : Control
{
    public const string PartPrevious = "PART_Previous";
    public const string PartNext = "PART_Next";

    private static readonly PropertyChangedCallback Recompute = (d, _) => ((Pagination)d).Rebuild();

    public static readonly DependencyProperty PageProperty = DependencyProperty.Register(
        nameof(Page), typeof(int), typeof(Pagination), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPageChanged, CoercePage));

    public static readonly DependencyProperty PageCountProperty = DependencyProperty.Register(
        nameof(PageCount), typeof(int), typeof(Pagination), new FrameworkPropertyMetadata(0, Recompute));

    public static readonly DependencyProperty SiblingsProperty = DependencyProperty.Register(
        nameof(Siblings), typeof(int), typeof(Pagination), new FrameworkPropertyMetadata(1, Recompute));

    public static readonly DependencyProperty PageSizeProperty = DependencyProperty.Register(
        nameof(PageSize), typeof(int?), typeof(Pagination), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPageSizeChanged));

    public static readonly DependencyProperty PageSizesProperty = DependencyProperty.Register(
        nameof(PageSizes), typeof(int[]), typeof(Pagination), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TotalCountProperty = DependencyProperty.Register(
        nameof(TotalCount), typeof(int?), typeof(Pagination), new FrameworkPropertyMetadata(null, Recompute));

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Pagination));

    private static readonly DependencyPropertyKey PagesPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Pages), typeof(IReadOnlyList<PageEntry>), typeof(Pagination), new FrameworkPropertyMetadata(Array.Empty<PageEntry>()));

    public static readonly DependencyProperty PagesProperty = PagesPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ActualPageCountPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualPageCount), typeof(int), typeof(Pagination), new FrameworkPropertyMetadata(1));

    public static readonly DependencyProperty ActualPageCountProperty = ActualPageCountPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey RangeTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(RangeText), typeof(string), typeof(Pagination), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty RangeTextProperty = RangeTextPropertyKey.DependencyProperty;

    public static readonly RoutedEvent PageChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(PageChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int>), typeof(Pagination));

    public static readonly RoutedEvent PageSizeChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(PageSizeChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<int?>), typeof(Pagination));

    static Pagination()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Pagination), new FrameworkPropertyMetadata(typeof(Pagination)));
        FocusableProperty.OverrideMetadata(typeof(Pagination), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Pagination), new FrameworkPropertyMetadata(false));
    }

    public Pagination()
    {
        AutomationProperties.SetName(this, "Pagination");
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
        Rebuild();
    }

    /// <summary>1-based, two-way.</summary>
    public int Page { get => (int)GetValue(PageProperty); set => SetValue(PageProperty, value); }

    /// <summary>Total pages; 0 = derive from <see cref="TotalCount"/> / <see cref="PageSize"/>.</summary>
    public int PageCount { get => (int)GetValue(PageCountProperty); set => SetValue(PageCountProperty, value); }

    /// <summary>Pages shown either side of the current one.</summary>
    public int Siblings { get => (int)GetValue(SiblingsProperty); set => SetValue(SiblingsProperty, value); }

    public int? PageSize { get => (int?)GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }

    /// <summary>Choices for the page-size picker (hidden when null).</summary>
    public int[]? PageSizes { get => (int[]?)GetValue(PageSizesProperty); set => SetValue(PageSizesProperty, value); }

    /// <summary>Total items, for the "1–50 of 1,234" summary.</summary>
    public int? TotalCount { get => (int?)GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public IReadOnlyList<PageEntry> Pages => (IReadOnlyList<PageEntry>)GetValue(PagesProperty);
    public int ActualPageCount => (int)GetValue(ActualPageCountProperty);
    public string? RangeText => (string?)GetValue(RangeTextProperty);

    public event RoutedPropertyChangedEventHandler<int> PageChanged { add => AddHandler(PageChangedEvent, value); remove => RemoveHandler(PageChangedEvent, value); }
    public event RoutedPropertyChangedEventHandler<int?> PageSizeChanged { add => AddHandler(PageSizeChangedEvent, value); remove => RemoveHandler(PageSizeChangedEvent, value); }

    private int ComputePageCount()
    {
        if (PageCount > 0)
            return PageCount;
        if (TotalCount is { } total && PageSize is { } size && size > 0)
            return Math.Max(1, PaginationRange.PageCount(total, size));
        return 1;
    }

    private static object CoercePage(DependencyObject d, object value)
    {
        var p = (Pagination)d;
        return Math.Clamp((int)value, 1, p.ComputePageCount());
    }

    private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (Pagination)d;
        p.Rebuild();
        p.RaiseEvent(new RoutedPropertyChangedEventArgs<int>((int)e.OldValue, (int)e.NewValue, PageChangedEvent));
    }

    private static void OnPageSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (Pagination)d;
        // Keep the first visible item on screen when the page size changes.
        if (e.OldValue is int oldSize && e.NewValue is int newSize && oldSize > 0 && newSize > 0)
            p.SetCurrentValue(PageProperty, PaginationRange.PageForFirstItem(p.Page, oldSize, newSize));
        p.Rebuild();
        p.RaiseEvent(new RoutedPropertyChangedEventArgs<int?>((int?)e.OldValue, (int?)e.NewValue, PageSizeChangedEvent));
    }

    private static readonly DependencyPropertyKey CanGoPreviousPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanGoPrevious), typeof(bool), typeof(Pagination), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey CanGoNextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(CanGoNext), typeof(bool), typeof(Pagination), new PropertyMetadata(false));
    public static readonly DependencyProperty CanGoPreviousProperty = CanGoPreviousPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanGoNextProperty = CanGoNextPropertyKey.DependencyProperty;

    /// <summary>True when there is a page before <see cref="Page"/>.</summary>
    public bool CanGoPrevious => (bool)GetValue(CanGoPreviousProperty);

    /// <summary>True when there is a page after <see cref="Page"/>.</summary>
    public bool CanGoNext => (bool)GetValue(CanGoNextProperty);

    private void Rebuild()
    {
        var count = ComputePageCount();
        SetValue(ActualPageCountPropertyKey, count);
        CoerceValue(PageProperty);
        var page = Page;
        SetValue(CanGoPreviousPropertyKey, page > 1);
        SetValue(CanGoNextPropertyKey, page < count);
        SetValue(PagesPropertyKey, PaginationRange.Compute(page, count, Math.Max(0, Siblings))
            .Select(i => new PageEntry(i.Page, i.IsEllipsis, !i.IsEllipsis && i.Page == page)).ToList());

        if (TotalCount is { } total && PageSize is { } size && size > 0)
        {
            var first = total == 0 ? 0 : (page - 1) * size + 1;
            var last = Math.Min(total, page * size);
            SetValue(RangeTextPropertyKey, string.Format(CultureInfo.CurrentCulture, "{0:N0}–{1:N0} of {2:N0}", first, last, total));
        }
        else
        {
            SetValue(RangeTextPropertyKey, null);
        }
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        switch (e.OriginalSource)
        {
            case FrameworkElement { Name: PartPrevious }:
                SetCurrentValue(PageProperty, Page - 1);
                break;
            case FrameworkElement { Name: PartNext }:
                SetCurrentValue(PageProperty, Page + 1);
                break;
            case FrameworkElement { DataContext: PageEntry { IsEllipsis: false } entry }:
                SetCurrentValue(PageProperty, entry.Page);
                break;
            default:
                return;
        }
        e.Handled = true;
    }
}

/// <summary>One segment of a <see cref="SegmentedControl"/>.</summary>
public sealed record Segment(object Item, string Text, string? Icon);

/// <summary>
/// Exclusive choice between a few options shown as a joined control (design/api/components.json "SegmentedControl").
/// Arrow keys move the selection (Slate.Core list navigation).
/// </summary>
[TemplatePart(Name = PartList, Type = typeof(ListBox))]
public class SegmentedControl : Control
{
    public const string PartList = "PART_List";

    private static readonly PropertyChangedCallback Rebuild = (d, _) => ((SegmentedControl)d).BuildSegments();

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(SegmentedControl), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(SegmentedControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty ItemTextProperty = DependencyProperty.Register(
        nameof(ItemText), typeof(Func<object, string>), typeof(SegmentedControl), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty ItemIconProperty = DependencyProperty.Register(
        nameof(ItemIcon), typeof(Func<object, string?>), typeof(SegmentedControl), new FrameworkPropertyMetadata(null, Rebuild));

    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(SegmentedControl));

    public static readonly DependencyProperty FullWidthProperty = DependencyProperty.Register(
        nameof(FullWidth), typeof(bool), typeof(SegmentedControl), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey SegmentsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Segments), typeof(IReadOnlyList<Segment>), typeof(SegmentedControl), new FrameworkPropertyMetadata(Array.Empty<Segment>()));

    public static readonly DependencyProperty SegmentsProperty = SegmentsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey SelectedSegmentPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(SelectedSegment), typeof(Segment), typeof(SegmentedControl), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SelectedSegmentProperty = SelectedSegmentPropertyKey.DependencyProperty;

    public static readonly RoutedEvent ValueChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(ValueChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<object?>), typeof(SegmentedControl));

    private ListBox? _list;

    static SegmentedControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(typeof(SegmentedControl)));
        FocusableProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(SegmentedControl), new FrameworkPropertyMetadata(false));
    }

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }

    /// <summary>The selected item (two-way).</summary>
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Label for an item (default: ToString).</summary>
    public Func<object, string>? ItemText { get => (Func<object, string>?)GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }

    /// <summary>Icon name for an item.</summary>
    public Func<object, string?>? ItemIcon { get => (Func<object, string?>?)GetValue(ItemIconProperty); set => SetValue(ItemIconProperty, value); }

    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Segments share the full width equally.</summary>
    public bool FullWidth { get => (bool)GetValue(FullWidthProperty); set => SetValue(FullWidthProperty, value); }

    public IReadOnlyList<Segment> Segments => (IReadOnlyList<Segment>)GetValue(SegmentsProperty);
    public Segment? SelectedSegment => (Segment?)GetValue(SelectedSegmentProperty);

    public event RoutedPropertyChangedEventHandler<object?> ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    public override void OnApplyTemplate()
    {
        if (_list is not null)
        {
            _list.SelectionChanged -= OnListSelectionChanged;
            _list.PreviewKeyDown -= OnListKeyDown;
        }
        base.OnApplyTemplate();
        _list = GetTemplateChild(PartList) as ListBox;
        if (_list is not null)
        {
            _list.SelectionChanged += OnListSelectionChanged;
            _list.PreviewKeyDown += OnListKeyDown;
        }
        SyncSelected();
    }

    private void BuildSegments()
    {
        var text = ItemText ?? (o => o?.ToString() ?? "");
        var segments = (Items?.Cast<object>() ?? []).Select(o => new Segment(o, text(o), ItemIcon?.Invoke(o))).ToList();
        SetValue(SegmentsPropertyKey, segments);
        SyncSelected();
    }

    private void SyncSelected()
    {
        var selected = Segments.FirstOrDefault(s => Equals(s.Item, Value));
        SetValue(SelectedSegmentPropertyKey, selected);
        if (_list is not null && !ReferenceEquals(_list.SelectedItem, selected))
            _list.SelectedItem = selected;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var s = (SegmentedControl)d;
        s.SyncSelected();
        s.RaiseEvent(new RoutedPropertyChangedEventArgs<object?>(e.OldValue, e.NewValue, ValueChangedEvent));
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_list?.SelectedItem is Segment segment && !Equals(segment.Item, Value))
            SetCurrentValue(ValueProperty, segment.Item);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.Right or Key.Down => ListKey.Next,
            Key.Left or Key.Up => ListKey.Previous,
            Key.Home => ListKey.First,
            Key.End => ListKey.Last,
            _ => (ListKey?)null,
        };
        if (key is null || _list is null || Segments.Count == 0)
            return;

        var current = SelectedSegment is { } sel ? Segments.ToList().IndexOf(sel) : -1;
        var next = ListNavigator.Move(current, key.Value, Segments.Select(_ => false).ToList(), new ListNavigationOptions { Wrap = true });
        if (next >= 0)
        {
            SetCurrentValue(ValueProperty, Segments[next].Item);
            if (_list.ItemContainerGenerator.ContainerFromIndex(next) is ListBoxItem item)
                item.Focus();
        }
        e.Handled = true;
    }
}
