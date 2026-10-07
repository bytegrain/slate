using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Metadata;
using Avalonia.VisualTree;
using Slate.Overlays;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Places Avalonia popups with Slate's shared positioning (<see cref="PopoverPositioner"/>): flip when the preferred
/// side doesn't fit, shift along the edge to stay inside the window, and a max height when neither side fits.
/// </summary>
public static class SlatePlacement
{
    /// <summary>Creates a custom placement callback for a popup anchored to <paramref name="anchor"/>.</summary>
    /// <param name="placement">Read on every placement, so it can change while the popup is closed.</param>
    /// <param name="placed">Receives the result (actual placement, max height) after each placement.</param>
    public static CustomPopupPlacementCallback Callback(Control anchor, Func<PopoverPlacement> placement, Func<double> offset, Action<PositionResult>? placed = null) =>
        p =>
        {
            var target = p.Target as Visual ?? anchor;
            var top = TopLevel.GetTopLevel(target);
            var viewport = new OverlayRect(0, 0, top?.ClientSize.Width ?? 4096, top?.ClientSize.Height ?? 4096);
            var origin = top is not null ? target.TranslatePoint(default, top) ?? default : default;

            var a = p.AnchorRectangle;
            var request = new PositionRequest
            {
                Anchor = new OverlayRect(a.X + origin.X, a.Y + origin.Y, a.Width, a.Height),
                PopupWidth = p.PopupSize.Width,
                PopupHeight = p.PopupSize.Height,
                Viewport = viewport,
                Placement = placement(),
                Offset = offset(),
            };
            var result = PopoverPositioner.Position(request);
            placed?.Invoke(result);

            // Express the computed top-left relative to the target, then pin the popup there exactly.
            p.AnchorRectangle = new Rect(result.Rect.X - origin.X, result.Rect.Y - origin.Y, 1, 1);
            p.Anchor = PopupAnchor.TopLeft;
            p.Gravity = PopupGravity.BottomRight;
            p.Offset = default;
            p.ConstraintAdjustment = PopupPositionerConstraintAdjustment.None;
        };

    /// <summary>Configures a popup to use Slate positioning relative to <paramref name="anchor"/>.</summary>
    public static void Apply(Popup popup, Control anchor, Func<PopoverPlacement> placement, Func<double> offset, Action<PositionResult>? placed = null)
    {
        popup.PlacementTarget = anchor;
        popup.Placement = PlacementMode.Custom;
        popup.CustomPopupPlacementCallback = Callback(anchor, placement, offset, placed);
    }

    /// <summary>Configures a context menu (or a menu opened from a trigger) to use Slate positioning.</summary>
    public static void Apply(ContextMenu menu, Control anchor, Func<PopoverPlacement> placement, Func<double> offset)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = Callback(anchor, placement, offset);
    }
}

/// <summary>
/// Floating panel attached to an <see cref="Anchor"/> (docs: Popover). Opens when a Button anchor is clicked (or via
/// <see cref="Open"/>), closes on Escape or an outside click, and returns focus to the anchor. <see cref="Modal"/>
/// traps Tab inside the panel. Styled from <c>Sl.Component.Popover.*</c>.
/// </summary>
[TemplatePart("PART_Anchor", typeof(ContentPresenter))]
[TemplatePart("PART_Popup", typeof(Popup))]
[TemplatePart("PART_Panel", typeof(Border))]
[PseudoClasses(":open")]
public class Popover : TemplatedControl
{
    public static readonly StyledProperty<object?> AnchorProperty = AvaloniaProperty.Register<Popover, object?>(nameof(Anchor));

    public static readonly StyledProperty<object?> ContentProperty = AvaloniaProperty.Register<Popover, object?>(nameof(Content));

    public static readonly StyledProperty<bool> OpenProperty =
        AvaloniaProperty.Register<Popover, bool>(nameof(Open), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<PopoverPlacement> PlacementProperty =
        AvaloniaProperty.Register<Popover, PopoverPlacement>(nameof(Placement), PopoverPlacement.Bottom);

    public static readonly StyledProperty<double> OffsetProperty = AvaloniaProperty.Register<Popover, double>(nameof(Offset), 6);
    public static readonly StyledProperty<bool> ModalProperty = AvaloniaProperty.Register<Popover, bool>(nameof(Modal));
    public static readonly StyledProperty<bool> CloseOnOutsideClickProperty = AvaloniaProperty.Register<Popover, bool>(nameof(CloseOnOutsideClick), true);
    public static readonly StyledProperty<bool> MatchAnchorWidthProperty = AvaloniaProperty.Register<Popover, bool>(nameof(MatchAnchorWidth));

    /// <summary>The side actually used after flipping (read-only; useful for arrows and tests).</summary>
    public static readonly DirectProperty<Popover, PopoverPlacement> ActualPlacementProperty =
        AvaloniaProperty.RegisterDirect<Popover, PopoverPlacement>(nameof(ActualPlacement), p => p.ActualPlacement);

    public static readonly RoutedEvent<RoutedEventArgs> OpenChangedEvent =
        RoutedEvent.Register<Popover, RoutedEventArgs>(nameof(OpenChanged), RoutingStrategies.Bubble);

    private ContentPresenter? _anchor;
    private Popup? _popup;
    private Border? _panel;
    private PopoverPlacement _actual = PopoverPlacement.Bottom;

    static Popover()
    {
        OpenProperty.Changed.AddClassHandler<Popover>((p, e) => p.OnOpenChanged(e.GetNewValue<bool>()));
        FocusableProperty.OverrideDefaultValue<Popover>(false);
    }

    public object? Anchor { get => GetValue(AnchorProperty); set => SetValue(AnchorProperty, value); }
    [Content]
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public bool Open { get => GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public PopoverPlacement Placement { get => GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }
    public double Offset { get => GetValue(OffsetProperty); set => SetValue(OffsetProperty, value); }
    public bool Modal { get => GetValue(ModalProperty); set => SetValue(ModalProperty, value); }
    public bool CloseOnOutsideClick { get => GetValue(CloseOnOutsideClickProperty); set => SetValue(CloseOnOutsideClickProperty, value); }
    public bool MatchAnchorWidth { get => GetValue(MatchAnchorWidthProperty); set => SetValue(MatchAnchorWidthProperty, value); }
    public PopoverPlacement ActualPlacement { get => _actual; private set => SetAndRaise(ActualPlacementProperty, ref _actual, value); }

    public event EventHandler<RoutedEventArgs>? OpenChanged
    {
        add => AddHandler(OpenChangedEvent, value);
        remove => RemoveHandler(OpenChangedEvent, value);
    }

    /// <summary>The panel hosting <see cref="Content"/> (null until the template is applied).</summary>
    public Border? Panel => _panel;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_anchor is not null) _anchor.RemoveHandler(Button.ClickEvent, OnAnchorClick);
        if (_popup is not null) _popup.Closed -= OnPopupClosed;
        if (_panel is not null) _panel.KeyDown -= OnPanelKeyDown;

        _anchor = e.NameScope.Find<ContentPresenter>("PART_Anchor");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _panel = e.NameScope.Find<Border>("PART_Panel");

        _anchor?.AddHandler(Button.ClickEvent, OnAnchorClick);
        if (_popup is not null && _anchor is not null)
        {
            SlatePlacement.Apply(_popup, _anchor, () => Placement, () => Offset, r =>
            {
                ActualPlacement = r.Placement;
                if (_panel is not null)
                    _panel.MaxHeight = r.MaxHeight ?? double.PositiveInfinity;
            });
            _popup.Closed += OnPopupClosed;
        }
        if (_panel is not null) _panel.KeyDown += OnPanelKeyDown;
        if (Open) OnOpenChanged(true);
    }

    private void OnAnchorClick(object? sender, RoutedEventArgs e)
    {
        Open = !Open;
        e.Handled = true;
    }

    private void OnOpenChanged(bool open)
    {
        PseudoClasses.Set(":open", open);
        if (_popup is null) return;

        if (open)
        {
            if (MatchAnchorWidth && _anchor is not null && _panel is not null)
                _panel.MinWidth = _anchor.Bounds.Width;
            _popup.IsLightDismissEnabled = CloseOnOutsideClick;
            KeyboardNavigation.SetTabNavigation(_panel!, Modal ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.Continue);
            _popup.IsOpen = true;
            if (Modal)
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => FirstFocusable(_panel)?.Focus(NavigationMethod.Tab));
        }
        else if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
        }
        RaiseEvent(new RoutedEventArgs(OpenChangedEvent));
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (Open) Open = false; // light dismiss
        FirstFocusable(_anchor)?.Focus(NavigationMethod.Tab);
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Open)
        {
            Open = false;
            e.Handled = true;
        }
    }

    internal static InputElement? FirstFocusable(Visual? root) =>
        root?.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(x => x.Focusable && x.IsEffectivelyVisible && x.IsEffectivelyEnabled);
}

/// <summary>
/// A menu opened from a <see cref="Trigger"/> (<c>sl:Menu</c>; distinct from <see cref="global::Avalonia.Controls.Menu"/>). Items are native <see cref="MenuItem"/>s and
/// <see cref="Separator"/>s (styled by Slate; use <c>sl:Sl.StartIcon</c>, <c>sl:Sl.Shortcut</c>, <c>sl:Sl.Tone</c>,
/// <c>ToggleType</c>/<c>IsChecked</c> and nested items for submenus). With <see cref="ContextMenu"/> the menu opens on
/// right-click / Shift+F10 at the pointer instead. Placed with Slate positioning.
/// </summary>
[TemplatePart("PART_Trigger", typeof(ContentPresenter))]
[PseudoClasses(":open")]
public class Menu : TemplatedControl
{
    public static readonly StyledProperty<object?> TriggerProperty = AvaloniaProperty.Register<Menu, object?>(nameof(Trigger));

    public static readonly StyledProperty<PopoverPlacement> PlacementProperty =
        AvaloniaProperty.Register<Menu, PopoverPlacement>(nameof(Placement), PopoverPlacement.BottomStart);

    public static readonly StyledProperty<bool> OpenProperty =
        AvaloniaProperty.Register<Menu, bool>(nameof(Open), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Open on right-click (and Shift+F10 / the context-menu key) at the pointer instead of on activation.
    /// Canonical option <c>ContextMenu</c>; spelled AsContextMenu because Control.ContextMenu already exists.
    /// </summary>
    public static readonly StyledProperty<bool> AsContextMenuProperty = AvaloniaProperty.Register<Menu, bool>(nameof(AsContextMenu));

    public static readonly RoutedEvent<RoutedEventArgs> OpenChangedEvent =
        RoutedEvent.Register<Menu, RoutedEventArgs>(nameof(OpenChanged), RoutingStrategies.Bubble);

    private readonly global::Avalonia.Controls.ContextMenu _menu = new();
    private ContentPresenter? _trigger;
    private bool _syncing;

    static Menu()
    {
        FocusableProperty.OverrideDefaultValue<Menu>(false);
        OpenProperty.Changed.AddClassHandler<Menu>((m, e) => m.SetOpen(e.GetNewValue<bool>()));
        AsContextMenuProperty.Changed.AddClassHandler<Menu>((m, _) => m.AttachMode());
    }

    public Menu()
    {
        _menu.Opened += (_, _) => Sync(true);
        _menu.Closed += (_, _) => Sync(false);
    }

    public object? Trigger { get => GetValue(TriggerProperty); set => SetValue(TriggerProperty, value); }
    public PopoverPlacement Placement { get => GetValue(PlacementProperty); set => SetValue(PlacementProperty, value); }
    public bool Open { get => GetValue(OpenProperty); set => SetValue(OpenProperty, value); }
    public bool AsContextMenu { get => GetValue(AsContextMenuProperty); set => SetValue(AsContextMenuProperty, value); }

    /// <summary>The menu items (MenuItem, Separator). Content property in XAML.</summary>
    [Content]
    public ItemCollection Items => _menu.Items;

    /// <summary>The native menu that hosts <see cref="Items"/>.</summary>
    public global::Avalonia.Controls.ContextMenu MenuPopup => _menu;

    public event EventHandler<RoutedEventArgs>? OpenChanged
    {
        add => AddHandler(OpenChangedEvent, value);
        remove => RemoveHandler(OpenChangedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _trigger?.RemoveHandler(Button.ClickEvent, OnTriggerClick);
        _trigger?.RemoveHandler(KeyDownEvent, OnTriggerKeyDown);
        _trigger = e.NameScope.Find<ContentPresenter>("PART_Trigger");
        _trigger?.AddHandler(Button.ClickEvent, OnTriggerClick);
        _trigger?.AddHandler(KeyDownEvent, OnTriggerKeyDown, RoutingStrategies.Tunnel);
        AttachMode();
    }

    private void AttachMode()
    {
        if (_trigger is null) return;
        if (AsContextMenu)
        {
            // Native context-menu behaviour: right-click / Shift+F10 at the pointer, keyboard fallback at the element.
            _trigger.ContextMenu = _menu;
            _menu.Placement = PlacementMode.Pointer;
            _menu.CustomPopupPlacementCallback = null;
        }
        else
        {
            _trigger.ContextMenu = null;
            SlatePlacement.Apply(_menu, _trigger, () => Placement, () => 4);
        }
    }

    private void OnTriggerClick(object? sender, RoutedEventArgs e)
    {
        if (AsContextMenu) return;
        Open = !Open;
        e.Handled = true;
    }

    private void OnTriggerKeyDown(object? sender, KeyEventArgs e)
    {
        if (!AsContextMenu && e.Key == Key.Down && !Open)
        {
            Open = true;
            e.Handled = true;
        }
    }

    private void SetOpen(bool open)
    {
        if (_syncing) return;
        if (open && _trigger is not null && !AsContextMenu)
            _menu.Open(_trigger);
        else if (!open && _menu.IsOpen)
            _menu.Close();
    }

    private void Sync(bool open)
    {
        PseudoClasses.Set(":open", open);
        _syncing = true;
        try { Open = open; }
        finally { _syncing = false; }
        RaiseEvent(new RoutedEventArgs(OpenChangedEvent));
        if (!open && !AsContextMenu)
            Popover.FirstFocusable(_trigger)?.Focus(NavigationMethod.Tab);
    }
}

/// <summary>
/// Wraps a control and gives it a Slate tooltip (docs: Tooltip): <see cref="Text"/> or rich <see cref="Content"/>,
/// an optional keyboard <see cref="Shortcut"/>, Slate placement and a show <see cref="Delay"/>. Moving between
/// tooltipped controls shows the next one immediately (native BetweenShowDelay).
/// </summary>
public class Tooltip : Decorator
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Tooltip, string?>(nameof(Text));
    public static readonly StyledProperty<object?> ContentProperty = AvaloniaProperty.Register<Tooltip, object?>(nameof(Content));
    public static readonly StyledProperty<string?> ShortcutProperty = AvaloniaProperty.Register<Tooltip, string?>(nameof(Shortcut));
    /// <summary>Canonical <c>Placement</c>; spelled TooltipPlacement on both XAML platforms (WPF's ToolTip.Placement exists).</summary>
    public static readonly StyledProperty<PopoverPlacement> TooltipPlacementProperty =
        AvaloniaProperty.Register<Tooltip, PopoverPlacement>(nameof(TooltipPlacement), PopoverPlacement.Top);
    public static readonly StyledProperty<int> DelayProperty = AvaloniaProperty.Register<Tooltip, int>(nameof(Delay), 500);
    public static readonly StyledProperty<bool> DisabledProperty = AvaloniaProperty.Register<Tooltip, bool>(nameof(Disabled));

    static Tooltip()
    {
        ChildProperty.Changed.AddClassHandler<Tooltip>((t, e) =>
        {
            if (e.OldValue is Control old) ToolTip.SetTip(old, null);
            t.Apply();
        });
        TextProperty.Changed.AddClassHandler<Tooltip>((t, _) => t.Apply());
        ContentProperty.Changed.AddClassHandler<Tooltip>((t, _) => t.Apply());
        ShortcutProperty.Changed.AddClassHandler<Tooltip>((t, _) => t.Apply());
        DelayProperty.Changed.AddClassHandler<Tooltip>((t, _) => t.Apply());
        DisabledProperty.Changed.AddClassHandler<Tooltip>((t, _) => t.Apply());
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public object? Content { get => GetValue(ContentProperty); set => SetValue(ContentProperty, value); }
    public string? Shortcut { get => GetValue(ShortcutProperty); set => SetValue(ShortcutProperty, value); }
    public PopoverPlacement TooltipPlacement { get => GetValue(TooltipPlacementProperty); set => SetValue(TooltipPlacementProperty, value); }
    public int Delay { get => GetValue(DelayProperty); set => SetValue(DelayProperty, value); }
    public bool Disabled { get => GetValue(DisabledProperty); set => SetValue(DisabledProperty, value); }

    /// <summary>The tip object assigned to the child (a <see cref="ToolTip"/> carrying the shortcut).</summary>
    public ToolTip? Tip { get; private set; }

    private void Apply()
    {
        if (Child is not Control child) return;
        if (Disabled || (Text is null && Content is null))
        {
            ToolTip.SetTip(child, null);
            Tip = null;
            return;
        }

        Tip ??= new ToolTip();
        Tip.Content = Content ?? Text;
        Slate.Avalonia.Sl.SetShortcut(Tip, Shortcut);
        ToolTip.SetTip(child, Tip);
        ToolTip.SetShowDelay(child, Delay);
        ToolTip.SetPlacement(child, PlacementMode.Custom);
        ToolTip.SetCustomPopupPlacementCallback(child, SlatePlacement.Callback(child, () => TooltipPlacement, () => 6));
        if (Text is not null && AutomationProperties.GetHelpText(child) is null)
            AutomationProperties.SetHelpText(child, Text);
    }
}
