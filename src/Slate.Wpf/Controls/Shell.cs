using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Layout;

namespace Slate.Wpf;

/// <summary>
/// App frame: app bar on top, drawer on the left, scrolling main content (docs/design/layout.md#app-shell).
/// Options follow design/api/components.json (AppShell).
/// </summary>
[TemplatePart(Name = "PART_Scrim", Type = typeof(FrameworkElement))]
public class AppShell : ContentControl
{
    public static readonly RoutedCommand ToggleDrawerCommand = new(nameof(ToggleDrawerCommand), typeof(AppShell));

    public static readonly DependencyProperty AppBarProperty = DependencyProperty.Register(
        nameof(AppBar), typeof(object), typeof(AppShell), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DrawerProperty = DependencyProperty.Register(
        nameof(Drawer), typeof(object), typeof(AppShell), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DrawerVariantProperty = DependencyProperty.Register(
        nameof(DrawerVariant), typeof(DrawerVariant), typeof(AppShell), new FrameworkPropertyMetadata(DrawerVariant.Responsive, (d, _) => ((AppShell)d).UpdateState()));

    public static readonly DependencyProperty DrawerOpenProperty = DependencyProperty.Register(
        nameof(DrawerOpen), typeof(bool), typeof(AppShell),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((AppShell)d).OnDrawerOpenChanged((bool)e.OldValue, (bool)e.NewValue)));

    public static readonly RoutedEvent DrawerOpenChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(DrawerOpenChanged), RoutingStrategy.Bubble, typeof(RoutedPropertyChangedEventHandler<bool>), typeof(AppShell));

    public static readonly DependencyProperty ResponsiveBreakpointProperty = DependencyProperty.Register(
        nameof(ResponsiveBreakpoint), typeof(Breakpoint), typeof(AppShell), new FrameworkPropertyMetadata(Breakpoint.Md, (d, _) => ((AppShell)d).UpdateState()));

    public static readonly DependencyProperty FillViewportProperty = DependencyProperty.Register(
        nameof(FillViewport), typeof(bool), typeof(AppShell), new FrameworkPropertyMetadata(false));

    private static readonly DependencyPropertyKey OverlayPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Overlay), typeof(bool), typeof(AppShell), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty OverlayProperty = OverlayPropertyKey.DependencyProperty;

    /// <summary>Inherited flag telling drawer content (NavItem) to render as an icon rail.</summary>
    public static readonly DependencyProperty MiniProperty = DependencyProperty.RegisterAttached(
        "Mini", typeof(bool), typeof(AppShell), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetMini(DependencyObject o) => (bool)o.GetValue(MiniProperty);
    public static void SetMini(DependencyObject o, bool v) => o.SetValue(MiniProperty, v);

    private IInputElement? _focusBeforeOverlay;

    static AppShell()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppShell), new FrameworkPropertyMetadata(typeof(AppShell)));
        FocusableProperty.OverrideMetadata(typeof(AppShell), new FrameworkPropertyMetadata(false));
    }

    public AppShell()
    {
        CommandBindings.Add(new CommandBinding(ToggleDrawerCommand, (_, _) => DrawerOpen = !DrawerOpen));
        SizeChanged += (_, _) => UpdateState();
    }

    public object? AppBar { get => GetValue(AppBarProperty); set => SetValue(AppBarProperty, value); }
    public object? Drawer { get => GetValue(DrawerProperty); set => SetValue(DrawerProperty, value); }

    /// <summary>Responsive (default), Persistent, Temporary or Mini.</summary>
    public DrawerVariant DrawerVariant { get => (DrawerVariant)GetValue(DrawerVariantProperty); set => SetValue(DrawerVariantProperty, value); }

    /// <summary>Whether the drawer is shown. Two-way bindable.</summary>
    public bool DrawerOpen { get => (bool)GetValue(DrawerOpenProperty); set => SetValue(DrawerOpenProperty, value); }

    public event RoutedPropertyChangedEventHandler<bool> DrawerOpenChanged { add => AddHandler(DrawerOpenChangedEvent, value); remove => RemoveHandler(DrawerOpenChangedEvent, value); }

    /// <summary>Width at which a Responsive drawer switches from temporary to persistent (default Md).</summary>
    public Breakpoint ResponsiveBreakpoint { get => (Breakpoint)GetValue(ResponsiveBreakpointProperty); set => SetValue(ResponsiveBreakpointProperty, value); }

    /// <summary>
    /// On the web this makes the shell fill the viewport. A WPF shell already fills its container; here it also
    /// removes the window frame padding around the shell (use it as a window's root content).
    /// </summary>
    public bool FillViewport { get => (bool)GetValue(FillViewportProperty); set => SetValue(FillViewportProperty, value); }

    /// <summary>True when the drawer currently overlays content (temporary, or responsive below the breakpoint).</summary>
    public bool Overlay => (bool)GetValue(OverlayProperty);

    /// <summary>The variant actually in effect at a width (never Responsive).</summary>
    public static DrawerVariant Effective(DrawerVariant variant, double width, Breakpoint breakpoint = Breakpoint.Md) =>
        variant == DrawerVariant.Responsive
            ? (Breakpoints.FromWidth(Math.Max(0, width)) >= breakpoint ? DrawerVariant.Persistent : DrawerVariant.Temporary)
            : variant;

    private void UpdateState()
    {
        var effective = Effective(DrawerVariant, ActualWidth, ResponsiveBreakpoint);
        var wasOverlay = Overlay;
        SetValue(OverlayPropertyKey, effective == DrawerVariant.Temporary);
        SetMini(this, effective == DrawerVariant.Mini);

        // Entering overlay mode (window got narrow) closes the drawer so it doesn't cover content unasked.
        if (!wasOverlay && Overlay && DrawerOpen && IsLoaded)
            DrawerOpen = false;
        else if (wasOverlay && !Overlay && !DrawerOpen)
            DrawerOpen = true;
    }

    private void OnDrawerOpenChanged(bool oldValue, bool open)
    {
        RaiseEvent(new RoutedPropertyChangedEventArgs<bool>(oldValue, open, DrawerOpenChangedEvent));
        if (!Overlay)
            return;
        if (open)
            _focusBeforeOverlay = Keyboard.FocusedElement;
        else if (_focusBeforeOverlay is UIElement { IsVisible: true } previous)
            previous.Focus();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Scrim") is FrameworkElement scrim)
            scrim.MouseLeftButtonDown += (_, _) => DrawerOpen = false;
        UpdateState();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && Overlay && DrawerOpen)
        {
            DrawerOpen = false;
            e.Handled = true;
        }
    }
}

/// <summary>56px top bar: menu button, Leading content, Title, Center content and trailing Actions.</summary>
public class AppBar : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(AppBar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ShowMenuButtonProperty = DependencyProperty.Register(
        nameof(ShowMenuButton), typeof(bool), typeof(AppBar), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty LeadingProperty = DependencyProperty.Register(
        nameof(Leading), typeof(object), typeof(AppBar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty CenterProperty = DependencyProperty.Register(
        nameof(Center), typeof(object), typeof(AppBar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(AppBar), new FrameworkPropertyMetadata(null));

    static AppBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppBar), new FrameworkPropertyMetadata(typeof(AppBar)));
        FocusableProperty.OverrideMetadata(typeof(AppBar), new FrameworkPropertyMetadata(false));
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Shows the drawer toggle (default true).</summary>
    public bool ShowMenuButton { get => (bool)GetValue(ShowMenuButtonProperty); set => SetValue(ShowMenuButtonProperty, value); }

    /// <summary>Content between the menu button and the title (e.g. a logo or back button).</summary>
    public object? Leading { get => GetValue(LeadingProperty); set => SetValue(LeadingProperty, value); }

    /// <summary>Content in the middle (e.g. a search field).</summary>
    public object? Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }

    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
}

/// <summary>
/// Drawer navigation entry: icon + label (+ trailing count/status). Collapses to the icon in mini mode.
/// Place NavItems in a ListBox styled <c>Sl.NavList</c>; <see cref="Active"/> mirrors selection.
/// </summary>
public class NavItem : ListBoxItem
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(NavItem), new FrameworkPropertyMetadata(null, OnLabelChanged));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(NavItem), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(object), typeof(NavItem), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(NavItem),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((NavItem)d).IsSelected = (bool)e.NewValue));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NavItem));

    static NavItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavItem), new FrameworkPropertyMetadata(typeof(NavItem)));
    }

    /// <summary>Text label (sets Content when Content is empty, and the accessible name).</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>Count or short status at the end of the row.</summary>
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }

    /// <summary>The current page. Kept in sync with ListBoxItem.IsSelected.</summary>
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var item = (NavItem)d;
        if (item.Content is null || Equals(item.Content, e.OldValue))
            item.Content = e.NewValue;
        AutomationProperties.SetName(item, (string?)e.NewValue ?? "");
    }

    protected override void OnSelected(RoutedEventArgs e)
    {
        base.OnSelected(e);
        Active = true;
    }

    protected override void OnUnselected(RoutedEventArgs e)
    {
        base.OnUnselected(e);
        Active = false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsEnabled)
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key is Key.Enter or Key.Space)
        {
            IsSelected = true;
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
            e.Handled = true;
        }
    }
}

/// <summary>Small caps heading inside drawers and menus (typography.overline).</summary>
public class SectionHeader : ContentControl
{
    static SectionHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SectionHeader), new FrameworkPropertyMetadata(typeof(SectionHeader)));
        FocusableProperty.OverrideMetadata(typeof(SectionHeader), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(SectionHeader), new FrameworkPropertyMetadata(false));
    }
}
