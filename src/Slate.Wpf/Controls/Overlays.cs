using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using Slate.Overlays;

namespace Slate.Wpf;

/// <summary>
/// Popup placement for every Slate overlay, computed by Slate.Core's <see cref="PopoverPositioner"/> so WPF matches the
/// other platforms. WPF knows the screen, so it receives the preferred placement plus its flipped opposite and keeps
/// whichever fits (sliding along the primary axis to stay on screen).
/// </summary>
internal static class PopupPositions
{
    public static PopoverPlacement Opposite(PopoverPlacement p) => p switch
    {
        PopoverPlacement.Top => PopoverPlacement.Bottom,
        PopoverPlacement.TopStart => PopoverPlacement.BottomStart,
        PopoverPlacement.TopEnd => PopoverPlacement.BottomEnd,
        PopoverPlacement.Bottom => PopoverPlacement.Top,
        PopoverPlacement.BottomStart => PopoverPlacement.TopStart,
        PopoverPlacement.BottomEnd => PopoverPlacement.TopEnd,
        PopoverPlacement.Left => PopoverPlacement.Right,
        PopoverPlacement.LeftStart => PopoverPlacement.RightStart,
        PopoverPlacement.LeftEnd => PopoverPlacement.RightEnd,
        PopoverPlacement.Right => PopoverPlacement.Left,
        PopoverPlacement.RightStart => PopoverPlacement.LeftStart,
        _ => PopoverPlacement.LeftEnd,
    };

    public static bool IsVertical(PopoverPlacement p) => p is PopoverPlacement.Top or PopoverPlacement.TopStart or PopoverPlacement.TopEnd
        or PopoverPlacement.Bottom or PopoverPlacement.BottomStart or PopoverPlacement.BottomEnd;

    /// <summary>Popup-relative point for <paramref name="placement"/> (anchor at the origin).</summary>
    public static Point Locate(PopoverPlacement placement, Size popup, Size target, double offset)
    {
        var result = PopoverPositioner.Position(new PositionRequest
        {
            Anchor = new OverlayRect(0, 0, target.Width, target.Height),
            PopupWidth = popup.Width,
            PopupHeight = popup.Height,
            Viewport = new OverlayRect(-1e6, -1e6, 2e6, 2e6),
            Placement = placement,
            Offset = offset,
            Flip = false,
            Shift = false,
        });
        return new Point(result.Rect.X, result.Rect.Y);
    }

    public static CustomPopupPlacementCallback Callback(Func<PopoverPlacement> placement, Func<double> offset) => (popup, target, _) =>
    {
        var preferred = placement();
        var axis = IsVertical(preferred) ? PopupPrimaryAxis.Horizontal : PopupPrimaryAxis.Vertical;
        return
        [
            new CustomPopupPlacement(Locate(preferred, popup, target, offset()), axis),
            new CustomPopupPlacement(Locate(Opposite(preferred), popup, target, offset()), axis),
        ];
    };

    /// <summary>Moves keyboard focus to the first focusable element inside <paramref name="root"/>.</summary>
    public static void FocusFirst(UIElement root) =>
        root.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!root.IsKeyboardFocusWithin)
                root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });
}

/// <summary>
/// Floating panel attached to an anchor (docs/design/data-grid.md uses it for column menus and filter editors).
/// <see cref="Anchor"/> is the visible trigger; <see cref="ContentControl.Content"/> is shown in the panel.
/// An anchor that is a button toggles the popover. Escape and (by default) outside clicks close it; focus returns to the anchor.
/// </summary>
[TemplatePart(Name = PartPopup, Type = typeof(Popup))]
[TemplatePart(Name = PartAnchor, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartPanel, Type = typeof(FrameworkElement))]
public class Popover : ContentControl
{
    public const string PartPopup = "PART_Popup";
    public const string PartAnchor = "PART_Anchor";
    public const string PartPanel = "PART_Panel";

    public static readonly DependencyProperty AnchorProperty = DependencyProperty.Register(
        nameof(Anchor), typeof(object), typeof(Popover), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty OpenProperty = DependencyProperty.Register(
        nameof(Open), typeof(bool), typeof(Popover), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnOpenChanged));

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(PopoverPlacement), typeof(Popover), new FrameworkPropertyMetadata(PopoverPlacement.Bottom));

    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(Popover), new FrameworkPropertyMetadata(6.0));

    public static readonly DependencyProperty ModalProperty = DependencyProperty.Register(
        nameof(Modal), typeof(bool), typeof(Popover), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CloseOnOutsideClickProperty = DependencyProperty.Register(
        nameof(CloseOnOutsideClick), typeof(bool), typeof(Popover), new FrameworkPropertyMetadata(true, (d, _) => ((Popover)d).SyncPopup()));

    public static readonly DependencyProperty MatchAnchorWidthProperty = DependencyProperty.Register(
        nameof(MatchAnchorWidth), typeof(bool), typeof(Popover), new FrameworkPropertyMetadata(false));

    public static readonly RoutedEvent OpenChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(OpenChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<bool>), typeof(Popover));

    private Popup? _popup;
    private FrameworkElement? _anchor;
    private IInputElement? _returnFocus;

    static Popover()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Popover), new FrameworkPropertyMetadata(typeof(Popover)));
        FocusableProperty.OverrideMetadata(typeof(Popover), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Popover), new FrameworkPropertyMetadata(false));
    }

    public Popover() => AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnAnchorClick));

    /// <summary>The element that opens the popover and that it is positioned against.</summary>
    public object? Anchor { get => GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }

    /// <summary>Two-way.</summary>
    public bool Open { get => (bool)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }

    public PopoverPlacement Placement { get => (PopoverPlacement)GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }

    /// <summary>Gap between anchor and panel (px).</summary>
    public double Offset { get => (double)GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }

    /// <summary>Traps Tab focus inside the panel while open.</summary>
    public bool Modal { get => (bool)GetValue(ModalProperty); set => SetValue(ModalProperty, value); }

    public bool CloseOnOutsideClick { get => (bool)GetValue(CloseOnOutsideClickProperty); set => SetValue(CloseOnOutsideClickProperty, value); }

    /// <summary>Panel is at least as wide as the anchor (selects, menus under fields).</summary>
    public bool MatchAnchorWidth { get => (bool)GetValue(MatchAnchorWidthProperty); set => SetValue(MatchAnchorWidthProperty, value); }

    public event RoutedPropertyChangedEventHandler<bool> OpenChanged { add => AddHandler(OpenChangedEvent, value); remove => RemoveHandler(OpenChangedEvent, value); }

    public override void OnApplyTemplate()
    {
        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
            _popup.PreviewKeyDown -= OnPopupKeyDown;
        }

        base.OnApplyTemplate();
        _anchor = GetTemplateChild(PartAnchor) as FrameworkElement;
        _popup = GetTemplateChild(PartPopup) as Popup;
        if (_popup is not null)
        {
            _popup.Placement = PlacementMode.Custom;
            _popup.PlacementTarget = _anchor;
            _popup.CustomPopupPlacementCallback = PopupPositions.Callback(() => Placement, () => Offset);
            _popup.Closed += OnPopupClosed;
            _popup.PreviewKeyDown += OnPopupKeyDown;
        }
        SyncPopup();
    }

    private void SyncPopup()
    {
        if (_popup is null)
            return;
        _popup.StaysOpen = !CloseOnOutsideClick;
        if (_popup.Child is FrameworkElement panel)
        {
            KeyboardNavigation.SetTabNavigation(panel, Modal ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            panel.MinWidth = MatchAnchorWidth && _anchor is not null ? _anchor.ActualWidth : 0;
        }
        _popup.IsOpen = Open;
    }

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var p = (Popover)d;
        var open = (bool)e.NewValue;
        if (open)
            p._returnFocus = Keyboard.FocusedElement;

        p.SyncPopup();
        if (open && p._popup?.Child is UIElement child)
            PopupPositions.FocusFirst(child);
        else if (!open && p._popup?.Child is UIElement panel && (panel.IsKeyboardFocusWithin || Keyboard.FocusedElement is null))
            p.RestoreFocus();

        AutomationProperties.SetItemStatus(p, open ? "expanded" : "collapsed");
        p.RaiseEvent(new RoutedPropertyChangedEventArgs<bool>((bool)e.OldValue, open, OpenChangedEvent));
    }

    private void RestoreFocus()
    {
        var target = _returnFocus ?? (_anchor as IInputElement);
        _returnFocus = null;
        if (target is UIElement u && u.IsVisible)
            u.Focus();
        else
            _anchor?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnPopupClosed(object? sender, EventArgs e) => SetCurrentValue(OpenProperty, false);

    private void OnPopupKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Open)
        {
            SetCurrentValue(OpenProperty, false);
            RestoreFocus();
            e.Handled = true;
        }
    }

    private void OnAnchorClick(object sender, RoutedEventArgs e)
    {
        // Only clicks from the anchor toggle; buttons inside the panel are the app's.
        if (_anchor is not null && e.OriginalSource is DependencyObject source && IsWithin(source, _anchor))
            SetCurrentValue(OpenProperty, !Open);
    }

    internal static bool IsWithin(DependencyObject? node, DependencyObject ancestor)
    {
        for (; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
                return true;
        }
        return false;
    }
}

/// <summary>
/// Alloy tooltip (docs/design/components.md): inverse bubble with optional <see cref="Shortcut"/>, positioned by Slate's
/// placement rules. Assign as an element's ToolTip: <c>&lt;Button.ToolTip&gt;&lt;sl:Tooltip Text="Copy" Shortcut="⌘C" /&gt;&lt;/Button.ToolTip&gt;</c>.
/// Plain-string ToolTips get the same look from the implicit ToolTip style (and <c>sl:Sl.Shortcut</c>).
/// </summary>
/// <remarks>The canonical <c>Placement</c> option is spelled <see cref="TooltipPlacement"/>: ToolTip already has a Placement of another type.</remarks>
public class Tooltip : ToolTip
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Tooltip), new FrameworkPropertyMetadata(null, (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(string), typeof(Tooltip), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TooltipPlacementProperty = DependencyProperty.Register(
        nameof(TooltipPlacement), typeof(PopoverPlacement), typeof(Tooltip), new FrameworkPropertyMetadata(PopoverPlacement.Top));

    public static readonly DependencyProperty DelayProperty = DependencyProperty.Register(
        nameof(Delay), typeof(int), typeof(Tooltip), new FrameworkPropertyMetadata(500));

    static Tooltip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Tooltip), new FrameworkPropertyMetadata(typeof(Tooltip)));
        // The owner's ToolTipService settings decide the delay; apply ours before its timer starts.
        EventManager.RegisterClassHandler(typeof(FrameworkElement), UIElement.MouseEnterEvent, new MouseEventHandler(OnOwnerMouseEnter), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnOwnerFocus), true);
    }

    public Tooltip()
    {
        Placement = PlacementMode.Custom;
        CustomPopupPlacementCallback = PopupPositions.Callback(() => TooltipPlacement, () => 6);
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Shortcut { get => (string?)GetValue(ShortcutProperty); set => SetValue(ShortcutProperty, value); }
    public PopoverPlacement TooltipPlacement { get => (PopoverPlacement)GetValue(TooltipPlacementProperty); set => SetValue(TooltipPlacementProperty, value); }

    /// <summary>Milliseconds before showing; moving between tooltips shows immediately (WPF's BetweenShowDelay).</summary>
    public int Delay { get => (int)GetValue(DelayProperty); set => SetValue(DelayProperty, value); }

    private static void OnOwnerMouseEnter(object sender, MouseEventArgs e) => Configure(sender);

    private static void OnOwnerFocus(object sender, KeyboardFocusChangedEventArgs e) => Configure(sender);

    private static void Configure(object sender)
    {
        if (sender is FrameworkElement owner && owner.ToolTip is Tooltip t)
        {
            ToolTipService.SetInitialShowDelay(owner, Math.Max(0, t.Delay));
            ToolTipService.SetIsEnabled(owner, t.IsEnabled);
        }
    }
}

/// <summary>
/// Dropdown or context menu (design/api/components.json "Menu"): <see cref="Trigger"/> is the visible element; the
/// items are native <see cref="MenuItem"/>s (Header, sl:Sl.StartIcon, InputGestureText or sl:Sl.Shortcut,
/// sl:Sl.Tone="Danger", IsCheckable/IsChecked, nested items for submenus) and <see cref="Separator"/>s.
/// Opens on click, Enter/Space/ArrowDown on the trigger; with <see cref="AsContextMenu"/> on right-click / Shift+F10 at the pointer.
/// </summary>
/// <remarks>The canonical <c>ContextMenu</c> flag is spelled <see cref="AsContextMenu"/> (FrameworkElement.ContextMenu exists).</remarks>
[ContentProperty(nameof(Items))]
[TemplatePart(Name = PartTrigger, Type = typeof(FrameworkElement))]
public class Menu : Control
{
    public const string PartTrigger = "PART_Trigger";

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger), typeof(object), typeof(Menu), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(PopoverPlacement), typeof(Menu), new FrameworkPropertyMetadata(PopoverPlacement.BottomStart));

    public static readonly DependencyProperty OpenProperty = DependencyProperty.Register(
        nameof(Open), typeof(bool), typeof(Menu), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnOpenChanged));

    public static readonly DependencyProperty AsContextMenuProperty = DependencyProperty.Register(
        nameof(AsContextMenu), typeof(bool), typeof(Menu), new FrameworkPropertyMetadata(false, (d, _) => ((Menu)d).SyncMode()));

    public static readonly RoutedEvent OpenChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(OpenChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<bool>), typeof(Menu));

    private readonly ContextMenu _menu = new();
    private FrameworkElement? _trigger;

    static Menu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Menu), new FrameworkPropertyMetadata(typeof(Menu)));
        FocusableProperty.OverrideMetadata(typeof(Menu), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(Menu), new FrameworkPropertyMetadata(false));
    }

    public Menu()
    {
        _menu.Opened += (_, _) => SetCurrentValue(OpenProperty, true);
        _menu.Closed += (_, _) => SetCurrentValue(OpenProperty, false);
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnTriggerClick));
        SyncMode();
    }

    /// <summary>The menu's items (MenuItem / Separator), hosted by an Alloy-styled ContextMenu.</summary>
    public ItemCollection Items => _menu.Items;

    /// <summary>The element that opens the menu (typically a Button with sl:Sl.EndIcon="chevron-down").</summary>
    public object? Trigger { get => GetValue(TriggerProperty); set => SetValue(TriggerProperty, value); }

    public PopoverPlacement Placement { get => (PopoverPlacement)GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }

    /// <summary>Two-way.</summary>
    public bool Open { get => (bool)GetValue(OpenProperty); set => SetValue(OpenProperty, value); }

    /// <summary>Open on right-click / Shift+F10 at the pointer instead of on click.</summary>
    public bool AsContextMenu { get => (bool)GetValue(AsContextMenuProperty); set => SetValue(AsContextMenuProperty, value); }

    public event RoutedPropertyChangedEventHandler<bool> OpenChanged { add => AddHandler(OpenChangedEvent, value); remove => RemoveHandler(OpenChangedEvent, value); }

    /// <summary>The ContextMenu that renders the items (exposed for styling and tests).</summary>
    public ContextMenu Popup => _menu;

    public override void OnApplyTemplate()
    {
        if (_trigger is not null)
            _trigger.PreviewKeyDown -= OnTriggerKeyDown;
        base.OnApplyTemplate();
        _trigger = GetTemplateChild(PartTrigger) as FrameworkElement;
        if (_trigger is not null)
            _trigger.PreviewKeyDown += OnTriggerKeyDown;
        SyncMode();
    }

    private void SyncMode()
    {
        if (AsContextMenu)
        {
            _menu.Placement = PlacementMode.MousePoint;
            _menu.CustomPopupPlacementCallback = null;
            if (_trigger is not null)
                _trigger.ContextMenu = _menu;
        }
        else
        {
            if (_trigger is not null && ReferenceEquals(_trigger.ContextMenu, _menu))
                _trigger.ClearValue(ContextMenuProperty);
            _menu.Placement = PlacementMode.Custom;
            _menu.CustomPopupPlacementCallback = PopupPositions.Callback(() => Placement, () => 4);
        }
        _menu.PlacementTarget = _trigger;
    }

    private static void OnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (Menu)d;
        var open = (bool)e.NewValue;
        if (m._menu.IsOpen != open)
        {
            m._menu.PlacementTarget = m._trigger;
            m._menu.IsOpen = open;
        }
        if (m._trigger is not null)
            AutomationProperties.SetItemStatus(m._trigger, open ? "expanded" : "collapsed");
        m.RaiseEvent(new RoutedPropertyChangedEventArgs<bool>((bool)e.OldValue, open, OpenChangedEvent));
    }

    private void OnTriggerClick(object sender, RoutedEventArgs e)
    {
        if (AsContextMenu || _trigger is null || e.OriginalSource is not DependencyObject source || !Popover.IsWithin(source, _trigger))
            return;
        SetCurrentValue(OpenProperty, !Open);
    }

    private void OnTriggerKeyDown(object sender, KeyEventArgs e)
    {
        if (AsContextMenu)
            return;
        // Buttons turn Enter/Space into Click; ArrowDown (and Enter/Space on non-buttons) open too.
        var isButton = e.OriginalSource is ButtonBase;
        if (e.Key == Key.Down || (!isButton && e.Key is Key.Enter or Key.Space))
        {
            SetCurrentValue(OpenProperty, true);
            _menu.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (_menu.ItemContainerGenerator.ContainerFromIndex(0) is MenuItem first)
                    first.Focus();
            });
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Non-button triggers (avatars, chips…) open on click too.
        if (!AsContextMenu && !e.Handled && _trigger is not null && e.OriginalSource is DependencyObject source
            && Popover.IsWithin(source, _trigger) && !IsInButton(source))
        {
            SetCurrentValue(OpenProperty, !Open);
            e.Handled = true;
        }
    }

    private bool IsInButton(DependencyObject source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, _trigger); node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
                return true;
        }
        return _trigger is ButtonBase;
    }
}
