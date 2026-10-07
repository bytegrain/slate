using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Slate.Data;

namespace Slate.Wpf;

public partial class DataGrid
{
    internal const double CheckColumnWidth = 40;
    internal const double DetailColumnWidth = 32;

    private Decorator? _root;
    private GridSurface? _surface;
    private ScrollViewer? _scroller;
    private GridHeaderView? _header;
    private GridFooterView? _footer;
    private StackPanel? _chromeTop;
    private Border? _toolbar;
    private Border? _selectionBar;
    private WrapPanel? _chips;
    private Border? _groupBar;
    private Border? _batchBar;
    private Pagination? _pager;
    private ContentPresenter? _empty;
    private Border? _loadingOverlay;
    private TextField? _quickFilterField;
    private TextBlock? _liveRegion;

    private string _layoutSignature = "";
    private double _bodyWidth;
    private double _hOffset;

    // ---- column geometry -------------------------------------------------------------------------------------

    /// <summary>The resolved column layout for the current body width.</summary>
    internal GridColumnLayout<object> CurrentLayout { get; private set; } = GridLayout.Resolve<object>([], GridState.Empty, 0);

    /// <summary>Bumped when the visible column set, order or pinning changes (row views rebuild their cells).</summary>
    internal int LayoutVersion { get; private set; }

    internal double HorizontalOffset => _hOffset;

    internal double BodyWidth => _bodyWidth > 0 ? _bodyWidth : Math.Max(0, ActualWidth);

    internal bool ShowSelectionColumn => SelectionMode == GridSelectionMode.Multi;

    /// <summary>Width of the fixed chrome columns before the data columns (checkbox, detail toggle).</summary>
    internal double Prefix => (ShowSelectionColumn ? CheckColumnWidth : 0) + (RowDetail is not null ? DetailColumnWidth : 0);

    internal double ContentWidth => Prefix + CurrentLayout.TotalWidth;

    internal double StartPinWidth { get; private set; }

    internal double EndPinWidth { get; private set; }

    /// <summary>The active-cell outline shows for keyboard interaction only.</summary>
    internal bool ShowActiveCell => _keyboardMode && IsKeyboardFocusWithin && !Controller.Edit.IsEditing;

    internal void ResolveLayout(double width)
    {
        _bodyWidth = width;
        var layout = Controller.Layout(Math.Max(0, width - Prefix));
        var signature = string.Join("|", layout.Columns.Select(c => c.Field + ":" + c.Pin)) + "#" + Prefix.ToString(CultureInfo.InvariantCulture)
                        + "#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Controller.Columns);
        var widthsChanged = CurrentLayout.TotalWidth != layout.TotalWidth
                            || CurrentLayout.Columns.Count != layout.Columns.Count
                            || CurrentLayout.Columns.Zip(layout.Columns).Any(p => p.First.Width != p.Second.Width);
        CurrentLayout = layout;
        StartPinWidth = layout.Columns.Where(c => c.Pin == GridPin.Start).Sum(c => c.Width);
        EndPinWidth = layout.Columns.Where(c => c.Pin == GridPin.End).Sum(c => c.Width);
        if (signature != _layoutSignature)
        {
            _layoutSignature = signature;
            LayoutVersion++;
            widthsChanged = true;
        }
        if (widthsChanged)
        {
            _header?.InvalidateVisuals();
            _footer?.InvalidateVisuals();
        }
    }

    /// <summary>X of a column in a row of width <paramref name="rowWidth"/>: CSS-sticky semantics for pinned columns.</summary>
    internal double ColumnX(ResolvedColumn<object> col, double rowWidth)
    {
        var natural = Prefix + col.Left - _hOffset;
        return col.Pin switch
        {
            GridPin.Start => Prefix + col.Left,
            GridPin.End => Math.Min(natural, rowWidth - col.StickyOffset - col.Width),
            _ => natural,
        };
    }

    /// <summary>The horizontal band in which scrolling cells are visible (between start and end pins).</summary>
    internal (double Left, double Right, double Width) LaneBounds(double rowWidth)
    {
        var left = Prefix + StartPinWidth;
        var right = rowWidth;
        var firstEnd = CurrentLayout.Columns.FirstOrDefault(c => c.Pin == GridPin.End);
        if (firstEnd is not null) right = Math.Min(rowWidth, ColumnX(firstEnd, rowWidth));
        right = Math.Max(left, right);
        return (left, right, right - left);
    }

    /// <summary>Whether the start / end pin edges currently cast a scroll shadow.</summary>
    internal (bool Start, bool End) ShadowEdges(double rowWidth)
    {
        var start = _hOffset > 0.5 && Prefix + StartPinWidth > 0;
        var firstEnd = CurrentLayout.Columns.FirstOrDefault(c => c.Pin == GridPin.End);
        var end = firstEnd is not null && Prefix + firstEnd.Left - _hOffset > ColumnX(firstEnd, rowWidth) + 0.5;
        return (start, end);
    }

    internal void OnHorizontalOffset(double offset)
    {
        if (Math.Abs(offset - _hOffset) < 0.01) return;
        _hOffset = offset;
        _header?.InvalidateMeasure();
        _header?.InvalidateArrange();
        _footer?.InvalidateArrange();
        if (_surface is not null)
            foreach (var row in _surface.RealizedRows)
            {
                row.InvalidateMeasure();
                row.InvalidateArrange();
                row.InvalidateVisual();
            }
    }

    // ---- template --------------------------------------------------------------------------------------------

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (_root is not null) _root.Child = null;
        _root = GetTemplateChild(PartRoot) as Decorator;
        if (_root is null) return;
        _root.Child = BuildTree();
        _root.SizeChanged += (_, _) => UpdateClip();
        UpdateChrome();
    }

    /// <summary>Clips the content to the frame's rounded corners (header and rows are square).</summary>
    private void UpdateClip()
    {
        if (_root is null) return;
        var radius = TryFindResource(GridKeys.Radius) is CornerRadius r ? Math.Max(0, r.TopLeft - 1) : 0;
        _root.Clip = new RectangleGeometry(new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), radius, radius);
    }

    private FrameworkElement BuildTree()
    {
        var dock = new DockPanel { LastChildFill = true };

        _chromeTop = new StackPanel();
        DockPanel.SetDock(_chromeTop, Dock.Top);
        dock.Children.Add(_chromeTop);

        _toolbar = BuildToolbar();
        _selectionBar = BuildSelectionBar();
        _chips = new WrapPanel { Margin = new Thickness(12, 0, 12, 8) };
        _groupBar = BuildGroupBar();
        _batchBar = BuildBatchBar();
        _chromeTop.Children.Add(_toolbar);
        _chromeTop.Children.Add(_selectionBar);
        _chromeTop.Children.Add(_chips);
        _chromeTop.Children.Add(_groupBar);
        _chromeTop.Children.Add(_batchBar);

        _pager = new Pagination { Margin = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Right, PageSizes = [25, 50, 100, 250] };
        _pager.PageChanged += (_, e) => { if (!_syncing) Controller.SetPage(e.NewValue - 1); };
        _pager.PageSizeChanged += (_, e) => { if (!_syncing && e.NewValue is { } size) SetCurrentValue(PageSizeProperty, size); };
        var pagerHost = new Border { Child = _pager, BorderThickness = new Thickness(0, 1, 0, 0) };
        pagerHost.SetResourceReference(Border.BorderBrushProperty, GridKeys.Border);
        DockPanel.SetDock(pagerHost, Dock.Bottom);
        dock.Children.Add(pagerHost);

        _footer = new GridFooterView(this);
        DockPanel.SetDock(_footer, Dock.Bottom);
        dock.Children.Add(_footer);

        _header = new GridHeaderView(this);
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);

        _surface = new GridSurface(this);
        _scroller = new ScrollViewer
        {
            CanContentScroll = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _surface,
            Focusable = false,
            Padding = new Thickness(0),
        };
        _scroller.SizeChanged += (_, _) => _header?.InvalidateMeasure();

        _empty = new ContentPresenter { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        _empty.SetResourceReference(TextElement.ForegroundProperty, GridKeys.TextSecondary);

        var spinner = new Spinner { Size = ControlSize.Large, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _loadingOverlay = new Border { Child = spinner, Opacity = 0.9 };
        _loadingOverlay.SetResourceReference(Border.BackgroundProperty, GridKeys.Surface);

        _liveRegion = new TextBlock { Width = 0, Height = 0, Opacity = 0, IsHitTestVisible = false };
        AutomationProperties.SetLiveSetting(_liveRegion, AutomationLiveSetting.Polite);

        var body = new Grid();
        body.Children.Add(_scroller);
        body.Children.Add(_empty);
        body.Children.Add(_loadingOverlay);
        body.Children.Add(_liveRegion);
        dock.Children.Add(body);
        return dock;
    }

    // ---- chrome ----------------------------------------------------------------------------------------------

    private static Button ChromeButton(string text, string? icon, ButtonVariant variant = ButtonVariant.Ghost, RoutedEventHandler? click = null)
    {
        var b = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Focusable = true };
        Sl.SetVariant(b, variant);
        Sl.SetSize(b, ControlSize.Small);
        if (icon is not null) Sl.SetStartIcon(b, icon);
        if (click is not null) b.Click += click;
        return b;
    }

    private static Button IconButton(string icon, string label, RoutedEventHandler click)
    {
        var b = new Button { Margin = new Thickness(4, 0, 0, 0) };
        Sl.SetVariant(b, ButtonVariant.Ghost);
        Sl.SetSize(b, ControlSize.Small);
        Sl.SetIconOnly(b, true);
        Sl.SetStartIcon(b, icon);
        Sl.SetLabel(b, label);
        b.ToolTip = label;
        b.Click += click;
        return b;
    }

    private Border Bar(UIElement child)
    {
        var border = new Border { Child = child, Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, GridKeys.Border);
        return border;
    }

    private Border BuildToolbar()
    {
        var dock = new DockPanel { LastChildFill = false };
        _quickFilterField = new TextField { Width = 260, Placeholder = "Search…", StartIcon = "search", Clearable = true, Size = ControlSize.Small };
        AutomationProperties.SetName(_quickFilterField, "Quick filter");
        _quickFilterField.ValueChanged += (_, e) => { if (!_syncing) SetCurrentValue(QuickFilterProperty, e.NewValue ?? ""); };
        DockPanel.SetDock(_quickFilterField, Dock.Left);
        dock.Children.Add(_quickFilterField);

        var custom = new ContentPresenter { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        custom.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(ToolbarContent)) { Source = this });
        DockPanel.SetDock(custom, Dock.Left);
        dock.Children.Add(custom);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(IconButton("layers", "Density", (_, _) => ToggleDensity()));
        right.Children.Add(ChromeButton("Columns", "columns", click: (s, _) => OpenColumnChooser((FrameworkElement)s)));
        right.Children.Add(ChromeButton("Export", "download", click: (s, _) => OpenExportMenu((FrameworkElement)s)));
        dock.Children.Add(right);
        return Bar(dock);
    }

    private TextBlock? _selectionCount;
    private Button? _selectAllMatching;

    private Border BuildSelectionBar()
    {
        var dock = new DockPanel { LastChildFill = false };
        _selectionCount = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        _selectionCount.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.Text);
        DockPanel.SetDock(_selectionCount, Dock.Left);
        dock.Children.Add(_selectionCount);

        _selectAllMatching = ChromeButton("", null, ButtonVariant.Link, (_, _) => Controller.SelectAllMatching());
        _selectAllMatching.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_selectAllMatching, Dock.Left);
        dock.Children.Add(_selectAllMatching);

        var actions = new ContentPresenter { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        actions.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(SelectionActions)) { Source = this });
        DockPanel.SetDock(actions, Dock.Left);
        dock.Children.Add(actions);

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(ChromeButton("Copy", "copy", click: (_, _) => CopySelection(includeHeader: true)));
        right.Children.Add(ChromeButton("Clear", "x", click: (_, _) => Controller.ClearSelection()));
        dock.Children.Add(right);

        var bar = Bar(dock);
        bar.SetResourceReference(Border.BackgroundProperty, GridKeys.RowSelected);
        return bar;
    }

    private WrapPanel? _groupChips;

    private Border BuildGroupBar()
    {
        var panel = new DockPanel { LastChildFill = true };
        var icon = new Icon { Kind = "group", Size = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Icon.ForegroundProperty, GridKeys.TextTertiary);
        DockPanel.SetDock(icon, Dock.Left);
        panel.Children.Add(icon);
        _groupChips = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(_groupChips);
        var bar = Bar(panel);
        bar.MinHeight = 44;
        bar.SetResourceReference(Border.BackgroundProperty, GridKeys.HeaderBackground);
        AutomationProperties.SetName(bar, "Group by");
        return bar;
    }

    private TextBlock? _batchText;

    private Border BuildBatchBar()
    {
        var dock = new DockPanel { LastChildFill = false };
        var icon = new Icon { Kind = "pencil", Size = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(Icon.ForegroundProperty, GridKeys.Warning);
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        _batchText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        _batchText.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.Text);
        DockPanel.SetDock(_batchText, Dock.Left);
        dock.Children.Add(_batchText);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(right, Dock.Right);
        right.Children.Add(ChromeButton("Discard", null, ButtonVariant.Ghost, (_, _) => DiscardAll()));
        right.Children.Add(ChromeButton("Commit", "check", ButtonVariant.Solid, (_, _) => CommitAll()));
        dock.Children.Add(right);
        var bar = Bar(dock);
        bar.SetResourceReference(Border.BackgroundProperty, "Sl.Brush.Status.Warning.Bg");
        return bar;
    }

    private static UIElement Chip(string text, string removeLabel, Action remove, Action? click = null)
    {
        var badge = new Button { Margin = new Thickness(0, 0, 6, 4), Content = text };
        Sl.SetVariant(badge, ButtonVariant.Soft);
        Sl.SetSize(badge, ControlSize.Small);
        Sl.SetEndIcon(badge, "x");
        AutomationProperties.SetHelpText(badge, removeLabel);
        badge.Click += (_, _) => (click ?? remove)();
        badge.PreviewMouseUp += (_, e) =>
        {
            // Clicking the trailing × removes, the rest opens (when there's an open action).
            if (click is not null && e.GetPosition(badge).X > badge.ActualWidth - 24)
            {
                e.Handled = true;
                remove();
            }
        };
        badge.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Delete or Key.Back)
            {
                e.Handled = true;
                remove();
            }
        };
        return badge;
    }

    /// <summary>Syncs toolbar, chips, group bar, selection/batch bars, pager and overlays with the controller.</summary>
    internal void UpdateChrome()
    {
        if (_chromeTop is null) return;
        var c = Controller;
        var selected = c.Selection.Count;
        var showSelectionBar = SelectionMode == GridSelectionMode.Multi && selected > 0;

        _toolbar!.Visibility = ShowToolbar && !showSelectionBar ? Visibility.Visible : Visibility.Collapsed;
        _selectionBar!.Visibility = showSelectionBar ? Visibility.Visible : Visibility.Collapsed;
        if (showSelectionBar)
        {
            _selectionCount!.Text = selected.ToString("N0", CultureInfo.CurrentCulture) + " selected";
            var matching = c.MatchingCount;
            _selectAllMatching!.Visibility = !c.Selection.AllMatching && c.HeaderState == SelectAllState.All && matching > selected
                ? Visibility.Visible : Visibility.Collapsed;
            _selectAllMatching.Content = $"Select all {matching.ToString("N0", CultureInfo.CurrentCulture)} matching";
        }

        _syncing = true;
        try
        {
            if (_quickFilterField is not null && _quickFilterField.Value != c.State.QuickFilter) _quickFilterField.Value = c.State.QuickFilter;
            if (_pager is not null)
            {
                _pager.TotalCount = c.MatchingCount;
                _pager.PageSize = c.State.PageSize;
                _pager.Page = c.State.PageIndex + 1;
            }
        }
        finally
        {
            _syncing = false;
        }

        _chips!.Children.Clear();
        foreach (var chip in c.FilterChips)
        {
            var field = chip.Field;
            _chips.Children.Add(Chip(chip.Text, "Remove filter", () => c.ClearFilter(field), () => OpenFilterFor(field)));
        }
        if (c.State.Filters.Count > 1)
            _chips.Children.Add(ChromeButton("Clear all", null, ButtonVariant.Link, (_, _) => c.ClearFilters()));
        _chips.Visibility = c.State.Filters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!ShowToolbar && c.State.Filters.Count > 0) _chips.Margin = new Thickness(12, 8, 12, 8);
        else _chips.Margin = new Thickness(12, 0, 12, 8);

        _groupBar!.Visibility = Groupable && !c.IsServer ? Visibility.Visible : Visibility.Collapsed;
        _groupChips!.Children.Clear();
        if (c.State.GroupBy.Count == 0)
        {
            var hint = new TextBlock { Text = "Drag a column header here to group", VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.TextTertiary);
            _groupChips.Children.Add(hint);
        }
        foreach (var field in c.State.GroupBy)
        {
            var f = field;
            _groupChips.Children.Add(Chip(c.Column(f)?.DisplayTitle ?? f, "Remove grouping", () => c.Ungroup(f)));
        }
        if (c.State.GroupBy.Count > 0)
        {
            _groupChips.Children.Add(ChromeButton("Expand all", null, ButtonVariant.Link, (_, _) => c.ExpandAll()));
            _groupChips.Children.Add(ChromeButton("Collapse all", null, ButtonVariant.Link, (_, _) => c.CollapseAll()));
        }

        var pending = c.PendingCount;
        _batchBar!.Visibility = EditMode == GridEditMode.Batch && pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        _batchText!.Text = pending == 1 ? "1 unsaved change" : $"{pending.ToString("N0", CultureInfo.CurrentCulture)} unsaved changes";

        ((FrameworkElement)_pager!.Parent).Visibility = Pagination == GridPagination.Pages ? Visibility.Visible : Visibility.Collapsed;
        _footer!.Visibility = ShowFooter ? Visibility.Visible : Visibility.Collapsed;

        _loadingOverlay!.Visibility = Loading ? Visibility.Visible : Visibility.Collapsed;
        var isEmpty = !Loading && c.RowCount == 0;
        _empty!.Content = c.State.HasFilters && c.TotalCount > 0 ? "No rows match the current filters" : EmptyContent;
        _empty.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        _header?.InvalidateVisuals();
    }

    private void ToggleDensity()
    {
        // The effective density shows in the row height (Sl.Size.Control.Md: 32 compact, 40 comfortable).
        Density = TokenRowHeight >= 36 ? Slate.Density.Compact : Slate.Density.Comfortable;
    }

    internal void Announce(string text)
    {
        if (_liveRegion is null) return;
        _liveRegion.Text = text;
        if (System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(_liveRegion) is { } peer || (peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(_liveRegion)) is not null)
            peer.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    private void AnnounceSelection()
    {
        var n = Controller.Selection.Count;
        Announce(n == 0 ? "Selection cleared" : n == 1 ? "1 row selected" : $"{n.ToString("N0", CultureInfo.CurrentCulture)} rows selected");
    }

    /// <summary>Scrolls so the row at a view index is fully visible.</summary>
    internal void RevealRow(int index)
    {
        if (_surface is null) return;
        var target = Controller.ScrollToReveal(index, _surface.VerticalOffset, _surface.ViewportHeight);
        _surface.SetVerticalOffset(target);
    }

    /// <summary>Scrolls horizontally so a (scrolling) column is visible between the pins.</summary>
    internal void RevealColumn(int columnIndex)
    {
        if (_surface is null || columnIndex < 0 || columnIndex >= CurrentLayout.Columns.Count) return;
        var col = CurrentLayout.Columns[columnIndex];
        if (col.Pin != GridPin.None) return;
        var laneWidth = BodyWidth - Prefix - StartPinWidth - EndPinWidth;
        var visibleLeft = _hOffset + StartPinWidth;
        if (col.Left < visibleLeft) _surface.SetHorizontalOffset(col.Left - StartPinWidth);
        else if (col.Left + col.Width > visibleLeft + laneWidth) _surface.SetHorizontalOffset(col.Left + col.Width - StartPinWidth - laneWidth);
    }

    internal GridSurface? Surface => _surface;

    internal GridHeaderView? Header => _header;

    /// <summary>The group bar's bounds relative to the grid (drop target for header drags).</summary>
    internal bool IsOverGroupBar(Point gridPoint)
    {
        if (_groupBar is not { Visibility: Visibility.Visible }) return false;
        var p = TranslatePoint(gridPoint, _groupBar);
        return p.X >= 0 && p.Y >= 0 && p.X <= _groupBar.ActualWidth && p.Y <= _groupBar.ActualHeight;
    }
}

/// <summary>Edge gradients cast by pinned columns onto scrolled content (a panel's top-most children).</summary>
internal sealed class GridPinShadows
{
    private const double Size = 8;
    private readonly Border _start;
    private readonly Border _end;

    public GridPinShadows(UIElementCollection children)
    {
        _start = Make(true);
        _end = Make(false);
        children.Add(_start);
        children.Add(_end);
    }

    private static Border Make(bool start)
    {
        var shade = Color.FromArgb(0x30, 0, 0, 0);
        var brush = new LinearGradientBrush(start ? shade : Colors.Transparent, start ? Colors.Transparent : shade, 0);
        brush.Freeze();
        var b = new Border { Background = brush, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        Panel.SetZIndex(b, 100);
        return b;
    }

    public void Measure()
    {
        _start.Measure(new Size(Size, double.PositiveInfinity));
        _end.Measure(new Size(Size, double.PositiveInfinity));
    }

    public void Arrange(DataGrid owner, Size size)
    {
        var (start, end) = owner.ShadowEdges(size.Width);
        var lane = owner.LaneBounds(size.Width);
        _start.Visibility = start ? Visibility.Visible : Visibility.Collapsed;
        _end.Visibility = end ? Visibility.Visible : Visibility.Collapsed;
        _start.Arrange(new Rect(lane.Left, 0, Size, size.Height));
        _end.Arrange(new Rect(Math.Max(0, lane.Right - Size), 0, Size, size.Height));
    }
}

/// <summary>The sticky header row: chrome (select-all checkbox), header cells for pinned columns and a clipped lane of
/// scrolling header cells. Drag a header to reorder or onto the group bar to group.</summary>
internal sealed class GridHeaderView : Panel
{
    private readonly DataGrid _owner;
    private readonly Dictionary<string, GridHeaderCell> _cells = new(StringComparer.Ordinal);
    private readonly GridLane _lane = new();
    private readonly GridPinShadows _shadows;
    private readonly CheckBox _all;
    private readonly Border _dropIndicator;
    private int _version = -1;

    public GridHeaderView(DataGrid owner)
    {
        _owner = owner;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        _lane.Place = child => child is GridHeaderCell cell && _owner.CurrentLayout.Find(cell.Field) is { } col
            ? new Rect(_owner.ColumnX(col, _owner.BodyWidth), 0, col.Width, 0)
            : Rect.Empty;
        Children.Add(_lane);
        _all = new CheckBox { Focusable = false, IsThreeState = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(_all, "Select all rows");
        _all.Click += (_, _) => _owner.Controller.ToggleAll();
        Children.Add(_all);
        _dropIndicator = new Border { Width = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _dropIndicator.SetResourceReference(Border.BackgroundProperty, GridKeys.Accent);
        Panel.SetZIndex(_dropIndicator, 200);
        Children.Add(_dropIndicator);
        _shadows = new GridPinShadows(Children);
    }

    public IEnumerable<GridHeaderCell> HeaderCells => _cells.Values;

    public GridHeaderCell? Cell(string field) => _cells.GetValueOrDefault(field);

    public void InvalidateVisuals()
    {
        if (_version != _owner.LayoutVersion) Rebuild();
        foreach (var cell in _cells.Values) cell.Refresh();
        _all.Visibility = _owner.ShowSelectionColumn ? Visibility.Visible : Visibility.Collapsed;
        _all.IsChecked = _owner.Controller.HeaderState switch
        {
            SelectAllState.All => true,
            SelectAllState.Some => null,
            _ => false,
        };
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void Rebuild()
    {
        _version = _owner.LayoutVersion;
        foreach (var cell in _cells.Values)
            if (cell.Parent is Panel p) p.Children.Remove(cell);
        _cells.Clear();
        foreach (var col in _owner.CurrentLayout.Columns)
        {
            var cell = new GridHeaderCell(_owner, col.Column);
            _cells[col.Field] = cell;
            if (col.Pin == GridPin.None) _lane.Children.Add(cell);
            else Children.Insert(Children.IndexOf(_all), cell);
        }
    }

    /// <summary>Shows the reorder drop indicator at the boundary nearest to <paramref name="x"/>; returns the target index.</summary>
    public int ShowDropAt(double x)
    {
        var layout = _owner.CurrentLayout;
        var best = 0;
        var bestX = 0.0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i <= layout.Columns.Count; i++)
        {
            double edge;
            if (i < layout.Columns.Count) edge = _owner.ColumnX(layout.Columns[i], _owner.BodyWidth);
            else if (layout.Columns.Count > 0) edge = _owner.ColumnX(layout.Columns[^1], _owner.BodyWidth) + layout.Columns[^1].Width;
            else edge = _owner.Prefix;
            var d = Math.Abs(edge - x);
            if (d < bestDistance) { bestDistance = d; best = i; bestX = edge; }
        }
        _dropIndicator.Visibility = Visibility.Visible;
        _dropIndicator.Tag = bestX;
        InvalidateArrange();
        return best;
    }

    public void HideDrop()
    {
        _dropIndicator.Visibility = Visibility.Collapsed;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size available)
    {
        if (_version != _owner.LayoutVersion) Rebuild();
        var h = _owner.TokenHeaderHeight;
        foreach (UIElement child in Children) child.Measure(new Size(_owner.BodyWidth, h));
        _shadows.Measure();
        return new Size(double.IsInfinity(available.Width) ? _owner.BodyWidth : available.Width, h);
    }

    protected override Size ArrangeOverride(Size size)
    {
        var w = _owner.BodyWidth;
        var h = size.Height;
        _all.Arrange(_owner.ShowSelectionColumn ? new Rect(0, 0, DataGrid.CheckColumnWidth, h) : new Rect());
        var lane = _owner.LaneBounds(w);
        _lane.Origin = lane.Left;
        _lane.InvalidateArrange();
        _lane.Arrange(new Rect(lane.Left, 0, lane.Width, h));
        foreach (var (field, cell) in _cells)
        {
            if (cell.Parent == _lane) continue;
            if (_owner.CurrentLayout.Find(field) is { } col) cell.Arrange(new Rect(_owner.ColumnX(col, w), 0, col.Width, h));
            else cell.Arrange(new Rect());
        }
        _dropIndicator.Arrange(_dropIndicator.Tag is double x ? new Rect(x - 1, 4, 2, Math.Max(0, h - 8)) : new Rect());
        _shadows.Arrange(_owner, new Size(w, h));
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        dc.DrawRectangle(_owner.TokenHeader, null, rect);
        dc.DrawLine(new Pen(_owner.TokenBorder, 1), new Point(0, rect.Height - 0.5), new Point(rect.Width, rect.Height - 0.5));
    }
}

/// <summary>A column header: title, sort direction with multi-sort order badge, filter dot, column-menu button and a
/// resize grip (drag; double-click to auto-fit).</summary>
internal sealed class GridHeaderCell : Grid
{
    private static readonly ControlTemplate GripTemplate = CreateGripTemplate();
    private readonly DataGrid _owner;
    private readonly TextBlock _title;
    private readonly Icon _sortIcon;
    private readonly TextBlock _sortOrder;
    private readonly Border _filterDot;
    private readonly Button _menuButton;
    private readonly Thumb _grip;
    private Point? _pressAt;
    private bool _dragging;
    private double _resizeStart;

    public GridHeaderCell(DataGrid owner, GridColumn<object> column)
    {
        _owner = owner;
        Column = column;
        Background = Brushes.Transparent;
        ClipToBounds = true;
        AutomationProperties.SetName(this, column.DisplayTitle);

        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(owner.TokenPadding, 0, 4, 0) };
        _menuButton = new Button { Focusable = false, Opacity = 0, Width = 24, Height = 24 };
        Sl.SetVariant(_menuButton, ButtonVariant.Ghost);
        Sl.SetSize(_menuButton, ControlSize.Small);
        Sl.SetIconOnly(_menuButton, true);
        Sl.SetStartIcon(_menuButton, "more-horizontal");
        Sl.SetLabel(_menuButton, column.DisplayTitle + " column menu");
        _menuButton.Click += (_, e) =>
        {
            e.Handled = true;
            _owner.OpenColumnMenu(this);
        };
        DockPanel.SetDock(_menuButton, Dock.Right);
        dock.Children.Add(_menuButton);

        _filterDot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _filterDot.SetResourceReference(Border.BackgroundProperty, GridKeys.Accent);
        DockPanel.SetDock(_filterDot, Dock.Right);
        dock.Children.Add(_filterDot);

        _sortOrder = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
        _sortOrder.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.AccentText);
        _sortOrder.SetResourceReference(TextBlock.FontFamilyProperty, GridKeys.MonoFont);
        _sortIcon = new Icon { Size = 14, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        _sortIcon.SetResourceReference(Icon.ForegroundProperty, GridKeys.AccentText);

        var end = column.EffectiveAlign == GridAlign.End;
        _title = new TextBlock
        {
            Text = column.DisplayTitle,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = end ? TextAlignment.Right : TextAlignment.Left,
        };
        _title.SetResourceReference(TextBlock.ForegroundProperty, GridKeys.HeaderText);
        _title.SetResourceReference(TextBlock.FontSizeProperty, GridKeys.FontSize);

        DockPanel.SetDock(_sortOrder, Dock.Right);
        DockPanel.SetDock(_sortIcon, Dock.Right);
        dock.Children.Add(_sortOrder);
        dock.Children.Add(_sortIcon);
        dock.Children.Add(_title);
        Children.Add(dock);

        _grip = new Thumb { Width = 8, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE, Template = GripTemplate, Focusable = false, Visibility = column.Resizable ? Visibility.Visible : Visibility.Collapsed };
        _grip.DragStarted += (_, _) => _resizeStart = _owner.CurrentLayout.Find(Field)?.Width ?? 100;
        _grip.DragDelta += (_, e) =>
        {
            _resizeStart += e.HorizontalChange;
            _owner.Controller.Resize(Field, _resizeStart);
        };
        _grip.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            _owner.Controller.AutoFit(Field);
        };
        Children.Add(_grip);
        Refresh();
    }

    public GridColumn<object> Column { get; }

    public string Field => Column.Field;

    public SortDirection? Sort { get; private set; }

    private static ControlTemplate CreateGripTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var template = new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
        template.Seal();
        return template;
    }

    public void Refresh()
    {
        var state = _owner.Controller.State;
        var sorts = state.Sorts;
        var at = -1;
        for (var i = 0; i < sorts.Count; i++) if (sorts[i].Field == Field) at = i;
        Sort = at >= 0 ? sorts[at].Direction : null;
        _sortIcon.Visibility = Sort is null ? Visibility.Collapsed : Visibility.Visible;
        _sortIcon.Kind = Sort == SortDirection.Descending ? "arrow-down" : "arrow-up";
        _sortOrder.Visibility = at >= 0 && sorts.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _sortOrder.Text = (at + 1).ToString(CultureInfo.InvariantCulture);
        _filterDot.Visibility = state.Filters.Any(f => f.Field == Field) ? Visibility.Visible : Visibility.Collapsed;
        _menuButton.Opacity = IsMouseOver || _owner.ColumnMenuField == Field ? 1 : 0;
        Cursor = Column.Sortable ? Cursors.Hand : null;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _menuButton.Opacity = 1;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Refresh();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rect = new Rect(RenderSize);
        if (VisualTreeHelper.GetParent(this) is not GridLane)
            dc.DrawRectangle(_owner.TokenHeader, null, rect); // pinned header cells stay opaque over the lane
        if (IsMouseOver && Column.Sortable) dc.DrawRectangle(_owner.TokenHover, null, rect);
        var pen = new Pen(_owner.TokenBorder, 1);
        dc.DrawLine(pen, new Point(rect.Width - 0.5, rect.Height * 0.25), new Point(rect.Width - 0.5, rect.Height * 0.75));
        if (VisualTreeHelper.GetParent(this) is not GridLane)
            dc.DrawLine(pen, new Point(0, rect.Height - 0.5), new Point(rect.Width, rect.Height - 0.5));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        _pressAt = e.GetPosition(_owner);
        _dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pressAt is not { } start || !IsMouseCaptured) return;
        var p = e.GetPosition(_owner);
        if (!_dragging && (Math.Abs(p.X - start.X) > SystemParameters.MinimumHorizontalDragDistance * 2 || Math.Abs(p.Y - start.Y) > SystemParameters.MinimumVerticalDragDistance * 2))
            _dragging = Column.Reorderable || (_owner.Groupable && !_owner.Controller.IsServer);
        if (!_dragging) return;
        Opacity = 0.6;
        if (_owner.Header is { } header)
        {
            if (_owner.IsOverGroupBar(p)) header.HideDrop();
            else header.ShowDropAt(e.GetPosition(header).X);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressAt is null) return;
        _pressAt = null;
        ReleaseMouseCapture();
        Opacity = 1;
        e.Handled = true;
        if (_dragging)
        {
            _dragging = false;
            var header = _owner.Header;
            header?.HideDrop();
            var p = e.GetPosition(_owner);
            if (_owner.IsOverGroupBar(p))
            {
                _owner.Controller.GroupBy(Field);
                return;
            }
            if (header is not null && Column.Reorderable)
            {
                var target = header.ShowDropAt(e.GetPosition(header).X);
                header.HideDrop();
                _owner.MoveColumnToVisibleIndex(Field, target);
            }
            return;
        }
        if (Column.Sortable)
            _owner.Controller.ToggleSort(Field, additive: (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pressAt = null;
        _dragging = false;
        Opacity = 1;
        _owner.Header?.HideDrop();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        _owner.OpenColumnMenu(this);
    }

    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new GridHeaderCellAutomationPeer(this);
}

/// <summary>Footer row: aggregates of all matching rows per column (mono, like group rows).</summary>
internal sealed class GridFooterView : FrameworkElement
{
    private readonly DataGrid _owner;

    public GridFooterView(DataGrid owner)
    {
        _owner = owner;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public void InvalidateVisuals()
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Footer text per field (for automation and tests).</summary>
    public IReadOnlyDictionary<string, string> Texts
    {
        get
        {
            var totals = _owner.Controller.Totals;
            var map = new Dictionary<string, string>();
            foreach (var col in _owner.CurrentLayout.Columns)
                if (col.Column.Aggregate != GridAggregate.None && totals.TryGetValue(col.Field, out var v))
                    map[col.Field] = GridCellFactory.AggregateText(col.Column, v);
            return map;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, _owner.Controller.RowHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        dc.DrawRectangle(_owner.TokenHeader, null, rect);
        dc.DrawLine(new Pen(_owner.TokenBorder, 1), new Point(0, 0.5), new Point(rect.Width, 0.5));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var w = _owner.BodyWidth;
        var lane = _owner.LaneBounds(w);
        var pad = _owner.TokenPadding;
        var texts = Texts;
        var mono = new Typeface(_owner.TokenMono, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        if (_owner.CurrentLayout.Columns.Count > 0 && !texts.ContainsKey(_owner.CurrentLayout.Columns[0].Field))
        {
            var label = new FormattedText($"{_owner.Controller.MatchingCount.ToString("N0", CultureInfo.CurrentCulture)} rows", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, mono, 12, _owner.TokenTertiary, dpi);
            dc.DrawText(label, new Point(_owner.ColumnX(_owner.CurrentLayout.Columns[0], w) + pad, (rect.Height - label.Height) / 2));
        }

        foreach (var col in _owner.CurrentLayout.Columns)
        {
            if (!texts.TryGetValue(col.Field, out var text)) continue;
            var x = _owner.ColumnX(col, w);
            if (col.Pin == GridPin.None && (x + col.Width < lane.Left || x > lane.Right)) continue;
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, mono, 12, _owner.TokenText, dpi)
            {
                MaxTextWidth = Math.Max(1, col.Width - 2 * pad),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
                TextAlignment = col.Column.EffectiveAlign == GridAlign.End ? TextAlignment.Right : TextAlignment.Left,
            };
            if (col.Pin == GridPin.None) dc.PushClip(new RectangleGeometry(new Rect(lane.Left, 0, lane.Width, rect.Height)));
            dc.DrawText(ft, new Point(x + pad, (rect.Height - ft.Height) / 2));
            if (col.Pin == GridPin.None) dc.Pop();
        }
    }
}
