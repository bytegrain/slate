using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Slate.Collections;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Tabs (docs: Tabs): a TabControl with Slate <see cref="Variant"/>s (line, pills, enclosed), sizes, horizontal or
/// vertical <see cref="Direction"/>, a two-way <see cref="Value"/> (the active <see cref="Tab.Key"/>) and
/// <see cref="KeepAlive"/> to keep visited panels mounted. Items are <see cref="Tab"/>s.
/// </summary>
[TemplatePart("PART_KeepAliveHost", typeof(Panel))]
[PseudoClasses(":line", ":pills", ":enclosed", ":vertical", ":keep-alive")]
public class Tabs : TabControl
{
    public static readonly StyledProperty<TabsVariant> VariantProperty = AvaloniaProperty.Register<Tabs, TabsVariant>(nameof(Variant));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Tabs, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<Direction> DirectionProperty = AvaloniaProperty.Register<Tabs, Direction>(nameof(Direction));
    public static readonly StyledProperty<bool> KeepAliveProperty = AvaloniaProperty.Register<Tabs, bool>(nameof(KeepAlive));
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<Tabs, string?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent = RoutedEvent.Register<Tabs, RoutedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    private readonly Dictionary<Tab, ContentPresenter> _alive = [];
    private Panel? _keepAliveHost;
    private bool _syncing;

    static Tabs()
    {
        VariantProperty.Changed.AddClassHandler<Tabs>((t, _) => t.UpdateLook());
        SizeProperty.Changed.AddClassHandler<Tabs>((t, _) => t.UpdateLook());
        DirectionProperty.Changed.AddClassHandler<Tabs>((t, _) => t.UpdateLook());
        KeepAliveProperty.Changed.AddClassHandler<Tabs>((t, _) => { t.UpdateLook(); t.UpdateKeepAlive(); });
        ValueProperty.Changed.AddClassHandler<Tabs>((t, e) => t.SelectByKey(e.GetNewValue<string?>()));
        SelectionChangedEvent.AddClassHandler<Tabs>((t, e) => { if (e.Source == t) t.OnTabSelected(); });
    }

    public Tabs() => UpdateLook();

    protected override Type StyleKeyOverride => typeof(Tabs);

    public TabsVariant Variant { get => GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Direction Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public bool KeepAlive { get => GetValue(KeepAliveProperty); set => SetValue(KeepAliveProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public event EventHandler<RoutedEventArgs>? ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _keepAliveHost = e.NameScope.Find<Panel>("PART_KeepAliveHost");
        _alive.Clear();
        if (Value is not null) SelectByKey(Value);
        UpdateKeepAlive();
    }

    private void SelectByKey(string? key)
    {
        if (_syncing || key is null) return;
        var tab = Items.OfType<Tab>().FirstOrDefault(t => t.Key == key);
        if (tab is not null) SelectedItem = tab;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (Value is not null) SelectByKey(Value);
    }

    private void OnTabSelected()
    {
        // While XAML is still adding tabs, auto-selection of the first tab must not overwrite a declared Value.
        if (!IsInitialized && Value is not null) return;
        var key = (SelectedItem as Tab)?.Key;
        if (key != Value)
        {
            _syncing = true;
            try { Value = key; }
            finally { _syncing = false; }
            RaiseEvent(new RoutedEventArgs(ValueChangedEvent));
        }
        UpdateKeepAlive();
    }

    private void UpdateKeepAlive()
    {
        if (_keepAliveHost is null || !KeepAlive) return;
        if (SelectedItem is Tab selected && !_alive.ContainsKey(selected))
        {
            var presenter = new ContentPresenter { Content = selected.Content, ContentTemplate = selected.ContentTemplate };
            _alive[selected] = presenter;
            _keepAliveHost.Children.Add(presenter);
        }
        foreach (var (tab, presenter) in _alive)
            presenter.IsVisible = tab == SelectedItem;
    }

    private void UpdateLook()
    {
        PseudoClasses.Set(":line", Variant == TabsVariant.Line);
        PseudoClasses.Set(":pills", Variant == TabsVariant.Pills);
        PseudoClasses.Set(":enclosed", Variant == TabsVariant.Enclosed);
        PseudoClasses.Set(":vertical", Direction == Direction.Column);
        PseudoClasses.Set(":keep-alive", KeepAlive);
        Classes.Set("small", Size == ControlSize.Small);
        Classes.Set("large", Size == ControlSize.Large);
        TabStripPlacement = Direction == Direction.Column ? Dock.Left : Dock.Top;
    }
}

/// <summary>A tab inside <see cref="Tabs"/>: key, label, icon, badge, optional close button (raises <see cref="Closed"/>).</summary>
[PseudoClasses(":closable", ":has-badge", ":has-icon")]
public class Tab : TabItem
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<Tab, string?>(nameof(Key));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Tab, string?>(nameof(Label));
    public static readonly StyledProperty<string?> BadgeProperty = AvaloniaProperty.Register<Tab, string?>(nameof(Badge));
    public static readonly StyledProperty<bool> ClosableProperty = AvaloniaProperty.Register<Tab, bool>(nameof(Closable));
    public static readonly RoutedEvent<RoutedEventArgs> ClosedEvent = RoutedEvent.Register<Tab, RoutedEventArgs>(nameof(Closed), RoutingStrategies.Bubble);

    static Tab()
    {
        LabelProperty.Changed.AddClassHandler<Tab>((t, e) => { t.Header ??= e.GetNewValue<string?>(); AutomationProperties.SetName(t, e.GetNewValue<string?>()); });
        ClosableProperty.Changed.AddClassHandler<Tab>((t, e) => t.PseudoClasses.Set(":closable", e.GetNewValue<bool>()));
        BadgeProperty.Changed.AddClassHandler<Tab>((t, e) => t.PseudoClasses.Set(":has-badge", !string.IsNullOrEmpty(e.GetNewValue<string?>())));
        // Icon accepts an icon name (string) as well as any content; names become a Slate Icon.
        IconProperty.Changed.AddClassHandler<Tab>((t, e) =>
        {
            if (e.GetNewValue<object?>() is string name && name.Length > 0)
                t.Icon = new Icon { Kind = name, Size = 16 };
            t.PseudoClasses.Set(":has-icon", t.Icon is not null);
        });
    }

    protected override Type StyleKeyOverride => typeof(Tab);

    public string? Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public bool Closable { get => GetValue(ClosableProperty); set => SetValue(ClosableProperty, value); }

    public event EventHandler<RoutedEventArgs>? Closed { add => AddHandler(ClosedEvent, value); remove => RemoveHandler(ClosedEvent, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, ev) => { RequestClose(); ev.Handled = true; };
    }

    /// <summary>Raises <see cref="Closed"/> (the app removes the tab).</summary>
    public void RequestClose() => RaiseEvent(new RoutedEventArgs(ClosedEvent));

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && Closable && e.Key == global::Avalonia.Input.Key.Delete)
        {
            RequestClose();
            e.Handled = true;
        }
    }
}

/// <summary>
/// Segmented control (docs: SegmentedControl): a single-choice row of segments with radio semantics. Arrow keys
/// move and select (wrapping), Home/End jump. Built on Slate.Core's ListNavigator.
/// </summary>
[TemplatePart("PART_Segments", typeof(Panel))]
[PseudoClasses(":full-width")]
public class SegmentedControl : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty = AvaloniaProperty.Register<SegmentedControl, IEnumerable?>(nameof(Items));
    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<SegmentedControl, object?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<Func<object?, string>?> ItemTextProperty = AvaloniaProperty.Register<SegmentedControl, Func<object?, string>?>(nameof(ItemText));
    public static readonly StyledProperty<Func<object?, string?>?> ItemIconProperty = AvaloniaProperty.Register<SegmentedControl, Func<object?, string?>?>(nameof(ItemIcon));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<SegmentedControl, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<bool> FullWidthProperty = AvaloniaProperty.Register<SegmentedControl, bool>(nameof(FullWidth));
    public static readonly RoutedEvent<RoutedEventArgs> ValueChangedEvent = RoutedEvent.Register<SegmentedControl, RoutedEventArgs>(nameof(ValueChanged), RoutingStrategies.Bubble);

    private readonly List<ToggleButton> _segments = [];
    private Panel? _host;

    static SegmentedControl()
    {
        ItemsProperty.Changed.AddClassHandler<SegmentedControl>((s, _) => s.Build());
        ItemTextProperty.Changed.AddClassHandler<SegmentedControl>((s, _) => s.Build());
        ItemIconProperty.Changed.AddClassHandler<SegmentedControl>((s, _) => s.Build());
        ValueProperty.Changed.AddClassHandler<SegmentedControl>((s, _) => { s.Sync(); s.RaiseEvent(new RoutedEventArgs(ValueChangedEvent)); });
        SizeProperty.Changed.AddClassHandler<SegmentedControl>((s, e) => { s.Classes.Set("small", e.GetNewValue<ControlSize>() == ControlSize.Small); s.Classes.Set("large", e.GetNewValue<ControlSize>() == ControlSize.Large); });
        FullWidthProperty.Changed.AddClassHandler<SegmentedControl>((s, e) => { s.PseudoClasses.Set(":full-width", e.GetNewValue<bool>()); s.Build(); });
    }

    public IEnumerable? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Func<object?, string>? ItemText { get => GetValue(ItemTextProperty); set => SetValue(ItemTextProperty, value); }
    public Func<object?, string?>? ItemIcon { get => GetValue(ItemIconProperty); set => SetValue(ItemIconProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool FullWidth { get => GetValue(FullWidthProperty); set => SetValue(FullWidthProperty, value); }
    public event EventHandler<RoutedEventArgs>? ValueChanged { add => AddHandler(ValueChangedEvent, value); remove => RemoveHandler(ValueChangedEvent, value); }

    public IReadOnlyList<ToggleButton> Segments => _segments;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _host = e.NameScope.Find<Panel>("PART_Segments");
        Build();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    private void Build()
    {
        if (_host is null) return;
        _host.Children.Clear();
        _segments.Clear();
        var items = Items?.Cast<object?>().ToList() ?? [];
        if (_host is Grid grid)
        {
            grid.ColumnDefinitions = new ColumnDefinitions(string.Join(",", Enumerable.Repeat(FullWidth ? "*" : "Auto", Math.Max(1, items.Count))));
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var b = new ToggleButton { Classes = { "segment" }, Content = ItemText?.Invoke(item) ?? item?.ToString(), Tag = item };
            if (ItemIcon?.Invoke(item) is { Length: > 0 } icon) Slate.Avalonia.Sl.SetStartIcon(b, icon);
            AutomationProperties.SetControlTypeOverride(b, AutomationControlType.RadioButton);
            b.Click += (s, _) => { Value = ((ToggleButton)s!).Tag; Sync(); };
            Grid.SetColumn(b, i);
            _segments.Add(b);
            _host.Children.Add(b);
        }
        Sync();
    }

    private void Sync()
    {
        foreach (var s in _segments)
        {
            var on = Equals(s.Tag, Value);
            s.IsChecked = on;
            KeyboardNavigation.SetIsTabStop(s, on);
        }
        if (_segments.Count > 0 && _segments.All(s => s.IsChecked != true))
            KeyboardNavigation.SetIsTabStop(_segments[0], true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _segments.Count == 0) return;
        ListKey? key = e.Key switch
        {
            Key.Right or Key.Down => ListKey.Next,
            Key.Left or Key.Up => ListKey.Previous,
            Key.Home => ListKey.First,
            Key.End => ListKey.Last,
            _ => null,
        };
        if (key is not { } k) return;
        var current = _segments.FindIndex(s => s.IsChecked == true);
        var next = ListNavigator.Move(current, k, _segments.Select(s => !s.IsEnabled).ToList(), new ListNavigationOptions { Wrap = true });
        if (next < 0) return;
        Value = _segments[next].Tag;
        _segments[next].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private sealed class GroupPeer(SegmentedControl owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}

/// <summary>An entry in <see cref="Breadcrumbs"/>.</summary>
public sealed record BreadcrumbItem(string Label, string? Href = null, string? Icon = null, object? Tag = null);

public sealed class BreadcrumbClickEventArgs(RoutedEvent routedEvent, BreadcrumbItem item) : RoutedEventArgs(routedEvent)
{
    public BreadcrumbItem Item { get; } = item;
}

/// <summary>
/// Breadcrumb trail (docs: Breadcrumbs): links separated by an icon; the last item is the current page. With
/// <see cref="MaxItems"/> the middle collapses into a "…" menu.
/// </summary>
[TemplatePart("PART_Items", typeof(StackPanel))]
public class Breadcrumbs : TemplatedControl
{
    public static readonly StyledProperty<IEnumerable<BreadcrumbItem>?> ItemsProperty = AvaloniaProperty.Register<Breadcrumbs, IEnumerable<BreadcrumbItem>?>(nameof(Items));
    public static readonly StyledProperty<int?> MaxItemsProperty = AvaloniaProperty.Register<Breadcrumbs, int?>(nameof(MaxItems));
    public static readonly StyledProperty<string> SeparatorProperty = AvaloniaProperty.Register<Breadcrumbs, string>(nameof(Separator), "chevron-right");
    public static readonly RoutedEvent<BreadcrumbClickEventArgs> ItemClickEvent = RoutedEvent.Register<Breadcrumbs, BreadcrumbClickEventArgs>(nameof(ItemClick), RoutingStrategies.Bubble);

    private StackPanel? _host;

    static Breadcrumbs()
    {
        ItemsProperty.Changed.AddClassHandler<Breadcrumbs>((b, _) => b.Build());
        MaxItemsProperty.Changed.AddClassHandler<Breadcrumbs>((b, _) => b.Build());
        SeparatorProperty.Changed.AddClassHandler<Breadcrumbs>((b, _) => b.Build());
        FocusableProperty.OverrideDefaultValue<Breadcrumbs>(false);
    }

    public IEnumerable<BreadcrumbItem>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public int? MaxItems { get => GetValue(MaxItemsProperty); set => SetValue(MaxItemsProperty, value); }
    public string Separator { get => GetValue(SeparatorProperty); set => SetValue(SeparatorProperty, value); }
    public event EventHandler<BreadcrumbClickEventArgs>? ItemClick { add => AddHandler(ItemClickEvent, value); remove => RemoveHandler(ItemClickEvent, value); }

    /// <summary>Items hidden in the overflow menu (empty when nothing is collapsed).</summary>
    public IReadOnlyList<BreadcrumbItem> Collapsed { get; private set; } = [];

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _host = e.NameScope.Find<StackPanel>("PART_Items");
        AutomationProperties.SetName(this, "Breadcrumb");
        Build();
    }

    private void Build()
    {
        if (_host is null) return;
        _host.Children.Clear();
        var items = Items?.ToList() ?? [];
        var visible = items.Cast<BreadcrumbItem?>().ToList();
        Collapsed = [];

        // Keep the first and the last (MaxItems - 1); collapse the middle into a menu.
        if (MaxItems is { } max && max >= 2 && items.Count > max)
        {
            var tail = max - 1;
            Collapsed = items.Skip(1).Take(items.Count - 1 - tail).ToList();
            visible = [items[0], null, .. items.Skip(items.Count - tail)];
        }

        for (var i = 0; i < visible.Count; i++)
        {
            if (i > 0)
                _host.Children.Add(new Icon { Kind = Separator, Size = 14, Classes = { "breadcrumb-separator" }, VerticalAlignment = VerticalAlignment.Center });

            if (visible[i] is not { } item)
            {
                var more = new Menu { Trigger = MoreButton() };
                foreach (var hidden in Collapsed)
                {
                    var mi = new MenuItem { Header = hidden.Label };
                    var captured = hidden;
                    mi.Click += (_, _) => RaiseEvent(new BreadcrumbClickEventArgs(ItemClickEvent, captured));
                    more.Items.Add(mi);
                }
                _host.Children.Add(more);
                continue;
            }

            var isCurrent = i == visible.Count - 1;
            if (isCurrent)
            {
                var current = new TextBlock { Text = item.Label, Classes = { "breadcrumb-current" }, VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetItemStatus(current, "current page");
                _host.Children.Add(current);
            }
            else
            {
                var link = new Button { Content = item.Label, Classes = { "ghost", "breadcrumb-link" } };
                Slate.Avalonia.Sl.SetSize(link, ControlSize.Small);
                if (item.Icon is { } icon) Slate.Avalonia.Sl.SetStartIcon(link, icon);
                link.Click += (_, _) => RaiseEvent(new BreadcrumbClickEventArgs(ItemClickEvent, item));
                _host.Children.Add(link);
            }
        }
    }

    private static Button MoreButton()
    {
        var b = new Button { Classes = { "ghost", "icon-only" } };
        Slate.Avalonia.Sl.SetSize(b, ControlSize.Small);
        Slate.Avalonia.Sl.SetStartIcon(b, "more-horizontal");
        Slate.Avalonia.Sl.SetLabel(b, "Show hidden breadcrumbs");
        return b;
    }
}

/// <summary>
/// Page navigation (docs: Pagination): previous/next, page numbers with sibling/boundary pages and ellipses
/// (Slate.Core PaginationRange), optional page-size choice that keeps the first visible item in view, and a
/// "1–50 of 1,240" summary when <see cref="TotalCount"/> is known. <see cref="Page"/> is 1-based.
/// </summary>
[TemplatePart("PART_Pages", typeof(StackPanel))]
[TemplatePart("PART_PageSize", typeof(ComboBox))]
[TemplatePart("PART_Summary", typeof(TextBlock))]
[PseudoClasses(":has-page-size", ":has-summary")]
public class Pagination : TemplatedControl
{
    public static readonly StyledProperty<int> PageProperty =
        AvaloniaProperty.Register<Pagination, int>(nameof(Page), 1, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay, coerce: (o, v) => Math.Max(1, v));
    public static readonly StyledProperty<int> PageCountProperty = AvaloniaProperty.Register<Pagination, int>(nameof(PageCount), 1);
    public static readonly StyledProperty<int> SiblingsProperty = AvaloniaProperty.Register<Pagination, int>(nameof(Siblings), 1);
    public static readonly StyledProperty<int?> PageSizeProperty =
        AvaloniaProperty.Register<Pagination, int?>(nameof(PageSize), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<int[]?> PageSizesProperty = AvaloniaProperty.Register<Pagination, int[]?>(nameof(PageSizes));
    public static readonly StyledProperty<int?> TotalCountProperty = AvaloniaProperty.Register<Pagination, int?>(nameof(TotalCount));
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Pagination, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly RoutedEvent<RoutedEventArgs> PageChangedEvent = RoutedEvent.Register<Pagination, RoutedEventArgs>(nameof(PageChanged), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<RoutedEventArgs> PageSizeChangedEvent = RoutedEvent.Register<Pagination, RoutedEventArgs>(nameof(PageSizeChanged), RoutingStrategies.Bubble);

    private StackPanel? _pages;
    private ComboBox? _pageSize;
    private TextBlock? _summary;
    private bool _syncing;

    static Pagination()
    {
        FocusableProperty.OverrideDefaultValue<Pagination>(false);
        PageProperty.Changed.AddClassHandler<Pagination>((p, _) => { p.Build(); p.RaiseEvent(new RoutedEventArgs(PageChangedEvent)); });
        foreach (var prop in new AvaloniaProperty[] { PageCountProperty, SiblingsProperty, TotalCountProperty, PageSizesProperty, SizeProperty })
            prop.Changed.AddClassHandler<Pagination>((p, _) => p.Build());
        PageSizeProperty.Changed.AddClassHandler<Pagination>((p, e) => p.OnPageSizeChanged(e.GetOldValue<int?>(), e.GetNewValue<int?>()));
    }

    public int Page { get => GetValue(PageProperty); set => SetValue(PageProperty, value); }
    public int PageCount { get => GetValue(PageCountProperty); set => SetValue(PageCountProperty, value); }
    public int Siblings { get => GetValue(SiblingsProperty); set => SetValue(SiblingsProperty, value); }
    public int? PageSize { get => GetValue(PageSizeProperty); set => SetValue(PageSizeProperty, value); }
    public int[]? PageSizes { get => GetValue(PageSizesProperty); set => SetValue(PageSizesProperty, value); }
    public int? TotalCount { get => GetValue(TotalCountProperty); set => SetValue(TotalCountProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public event EventHandler<RoutedEventArgs>? PageChanged { add => AddHandler(PageChangedEvent, value); remove => RemoveHandler(PageChangedEvent, value); }
    public event EventHandler<RoutedEventArgs>? PageSizeChanged { add => AddHandler(PageSizeChangedEvent, value); remove => RemoveHandler(PageSizeChangedEvent, value); }

    /// <summary>Page count from <see cref="TotalCount"/> and <see cref="PageSize"/> when both are known, else <see cref="PageCount"/>.</summary>
    public int EffectivePageCount => TotalCount is { } total && PageSize is { } size and > 0
        ? Math.Max(1, PaginationRange.PageCount(total, size))
        : Math.Max(1, PageCount);

    /// <summary>The items currently rendered (pages and ellipses), for tests and automation.</summary>
    public IReadOnlyList<PaginationItem> Items { get; private set; } = [];

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _pages = e.NameScope.Find<StackPanel>("PART_Pages");
        _pageSize = e.NameScope.Find<ComboBox>("PART_PageSize");
        _summary = e.NameScope.Find<TextBlock>("PART_Summary");
        if (_pageSize is not null)
            _pageSize.SelectionChanged += (_, _) => { if (!_syncing && _pageSize.SelectedItem is int n) PageSize = n; };
        AutomationProperties.SetName(this, "Pagination");
        Build();
    }

    public void GoTo(int page) => Page = Math.Clamp(page, 1, EffectivePageCount);

    private void OnPageSizeChanged(int? old, int? size)
    {
        if (old is { } o && size is { } n && n > 0 && !_syncing)
        {
            var target = PaginationRange.PageForFirstItem(Page, o, n);
            _syncing = true;
            try { Page = target; }
            finally { _syncing = false; }
        }
        Build();
        RaiseEvent(new RoutedEventArgs(PageSizeChangedEvent));
    }

    private void Build()
    {
        if (_pages is null) return;
        var count = EffectivePageCount;
        if (Page > count && !_syncing) { Page = count; return; }

        Items = PaginationRange.Compute(Page, count, Siblings);
        _pages.Children.Clear();
        var small = Size == ControlSize.Small;

        _pages.Children.Add(NavButton("chevron-left", "Previous page", Page > 1, () => GoTo(Page - 1), small));
        foreach (var item in Items)
        {
            if (item.IsEllipsis)
            {
                _pages.Children.Add(new TextBlock { Text = "…", Classes = { "pagination-ellipsis" }, VerticalAlignment = VerticalAlignment.Center });
                continue;
            }
            var page = item.Page;
            var b = new Button { Content = page.ToString(CultureInfo.CurrentCulture), Classes = { "ghost", "pagination-page" } };
            Slate.Avalonia.Sl.SetSize(b, small ? ControlSize.Small : ControlSize.Medium);
            b.Classes.Set("current", page == Page);
            AutomationProperties.SetName(b, $"Page {page}");
            if (page == Page) AutomationProperties.SetItemStatus(b, "current page");
            b.Click += (_, _) => GoTo(page);
            _pages.Children.Add(b);
        }
        _pages.Children.Add(NavButton("chevron-right", "Next page", Page < count, () => GoTo(Page + 1), small));

        PseudoClasses.Set(":has-page-size", PageSizes is { Length: > 0 });
        if (_pageSize is not null && PageSizes is { Length: > 0 } sizes)
        {
            _syncing = true;
            try
            {
                if (!sizes.SequenceEqual(_pageSize.Items.OfType<int>()))
                {
                    _pageSize.Items.Clear();
                    foreach (var s in sizes) _pageSize.Items.Add(s);
                }
                _pageSize.SelectedItem = PageSize;
            }
            finally { _syncing = false; }
        }

        var hasSummary = TotalCount is { } total && PageSize is { } size;
        PseudoClasses.Set(":has-summary", hasSummary);
        if (_summary is not null && hasSummary)
        {
            var first = Math.Min(TotalCount!.Value, (Page - 1) * PageSize!.Value + 1);
            var last = Math.Min(TotalCount.Value, Page * PageSize.Value);
            _summary.Text = string.Format(CultureInfo.CurrentCulture, "{0:N0}–{1:N0} of {2:N0}", first, last, TotalCount.Value);
        }
    }

    private static Button NavButton(string icon, string label, bool enabled, Action go, bool small)
    {
        var b = new Button { Classes = { "ghost", "icon-only" }, IsEnabled = enabled };
        Slate.Avalonia.Sl.SetSize(b, small ? ControlSize.Small : ControlSize.Medium);
        Slate.Avalonia.Sl.SetStartIcon(b, icon);
        Slate.Avalonia.Sl.SetLabel(b, label);
        b.Click += (_, _) => go();
        return b;
    }
}
