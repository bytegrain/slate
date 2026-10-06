using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Slate.Avalonia.Services;
using Slate.Layout;

namespace Slate.Avalonia.Controls;

/// <summary>The drawer mode in effect after resolving <see cref="DrawerVariant.Responsive"/>.</summary>
public enum DrawerMode
{
    Persistent,
    Temporary,
    Mini,
}

/// <summary>
/// Application frame (docs/design/layout.md#app-shell): an <see cref="AppBar"/> on top, a <see cref="Drawer"/>
/// on the left and scrolling main content.
/// </summary>
[TemplatePart("PART_Content", typeof(ContentPresenter))]
[TemplatePart("PART_DrawerPresenter", typeof(ContentPresenter))]
[TemplatePart("PART_Scrim", typeof(Border))]
[PseudoClasses(":persistent", ":temporary", ":mini", ":open")]
public class AppShell : ContentControl
{
    public static readonly StyledProperty<object?> AppBarProperty = AvaloniaProperty.Register<AppShell, object?>(nameof(AppBar));
    public static readonly StyledProperty<object?> DrawerProperty = AvaloniaProperty.Register<AppShell, object?>(nameof(Drawer));
    public static readonly StyledProperty<DrawerVariant> DrawerVariantProperty = AvaloniaProperty.Register<AppShell, DrawerVariant>(nameof(DrawerVariant));
    public static readonly StyledProperty<bool> DrawerOpenProperty =
        AvaloniaProperty.Register<AppShell, bool>(nameof(DrawerOpen), true, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Stretch to fill the hosting window/area (the default for desktop shells).</summary>
    public static readonly StyledProperty<bool> FillViewportProperty =
        AvaloniaProperty.Register<AppShell, bool>(nameof(FillViewport), true);
    public static readonly StyledProperty<Breakpoint> ResponsiveBreakpointProperty =
        AvaloniaProperty.Register<AppShell, Breakpoint>(nameof(ResponsiveBreakpoint), Breakpoint.Md);

    public static readonly DirectProperty<AppShell, DrawerMode> DrawerModeProperty =
        AvaloniaProperty.RegisterDirect<AppShell, DrawerMode>(nameof(DrawerMode), s => s.DrawerMode);

    /// <summary>Raised by an <see cref="AppBar"/> menu button (or anything else) to toggle the nearest shell's drawer.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> ToggleDrawerRequestedEvent =
        RoutedEvent.Register<AppShell, RoutedEventArgs>("ToggleDrawerRequested", RoutingStrategies.Bubble);

    private ContentPresenter? _content;
    private ContentPresenter? _drawer;
    private Border? _scrim;
    private DrawerMode _mode = DrawerMode.Persistent;
    private IInputElement? _focusBeforeOpen;
    private bool _wasTemporaryOpen;

    static AppShell()
    {
        DrawerVariantProperty.Changed.AddClassHandler<AppShell>((s, _) => s.UpdateMode());
        ResponsiveBreakpointProperty.Changed.AddClassHandler<AppShell>((s, _) => s.UpdateMode());
        DrawerOpenProperty.Changed.AddClassHandler<AppShell>((s, _) =>
        {
            s.UpdateState();
            s.DrawerOpenChanged?.Invoke(s, EventArgs.Empty);
        });
        FillViewportProperty.Changed.AddClassHandler<AppShell>((s, e) => s.UpdateFill(e.GetNewValue<bool>()));
        DrawerProperty.Changed.AddClassHandler<AppShell>((s, _) => s.UpdateState());
        ToggleDrawerRequestedEvent.AddClassHandler<AppShell>((s, e) => { s.ToggleDrawer(e.Source as IInputElement); e.Handled = true; });
    }

    public object? AppBar { get => GetValue(AppBarProperty); set => SetValue(AppBarProperty, value); }
    public object? Drawer { get => GetValue(DrawerProperty); set => SetValue(DrawerProperty, value); }
    public DrawerVariant DrawerVariant { get => GetValue(DrawerVariantProperty); set => SetValue(DrawerVariantProperty, value); }
    public bool DrawerOpen { get => GetValue(DrawerOpenProperty); set => SetValue(DrawerOpenProperty, value); }
    public bool FillViewport { get => GetValue(FillViewportProperty); set => SetValue(FillViewportProperty, value); }

    /// <summary>Raised when <see cref="DrawerOpen"/> changes (toggle button, scrim click, Escape or width change).</summary>
    public event EventHandler? DrawerOpenChanged;

    public AppShell() => UpdateFill(true);

    private void UpdateFill(bool fill)
    {
        HorizontalAlignment = fill ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        VerticalAlignment = fill ? VerticalAlignment.Stretch : VerticalAlignment.Top;
    }
    public Breakpoint ResponsiveBreakpoint { get => GetValue(ResponsiveBreakpointProperty); set => SetValue(ResponsiveBreakpointProperty, value); }

    public DrawerMode DrawerMode
    {
        get => _mode;
        private set => SetAndRaise(DrawerModeProperty, ref _mode, value);
    }

    /// <summary>Opens or closes the drawer. When a temporary drawer closes, focus returns to <paramref name="toggle"/>.</summary>
    public void ToggleDrawer(IInputElement? toggle = null)
    {
        if (!DrawerOpen)
            _focusBeforeOpen = toggle ?? TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        DrawerOpen = !DrawerOpen;
    }

    /// <summary>Resolves the drawer mode for a given width — the rule documented for Responsive drawers.</summary>
    public static DrawerMode ResolveMode(DrawerVariant variant, double width, Breakpoint responsiveBreakpoint) => variant switch
    {
        DrawerVariant.Persistent => DrawerMode.Persistent,
        DrawerVariant.Temporary => DrawerMode.Temporary,
        DrawerVariant.Mini => DrawerMode.Mini,
        _ => width >= Breakpoints.MinWidth(responsiveBreakpoint) ? DrawerMode.Persistent : DrawerMode.Temporary,
    };

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_scrim is not null)
            _scrim.PointerPressed -= OnScrimPressed;
        _content = e.NameScope.Find<ContentPresenter>("PART_Content");
        _drawer = e.NameScope.Find<ContentPresenter>("PART_DrawerPresenter");
        _scrim = e.NameScope.Find<Border>("PART_Scrim");
        if (_scrim is not null)
            _scrim.PointerPressed += OnScrimPressed;
        UpdateState();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateMode();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && DrawerMode == DrawerMode.Temporary && DrawerOpen)
        {
            DrawerOpen = false;
            e.Handled = true;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DrawerMode == DrawerMode.Temporary)
        {
            DrawerOpen = false;
            e.Handled = true;
        }
    }

    private void UpdateMode()
    {
        var previous = DrawerMode;
        DrawerMode = ResolveMode(DrawerVariant, Bounds.Width, ResponsiveBreakpoint);
        // Switching to temporary (window narrowed) should not suddenly cover the content.
        if (previous != DrawerMode && DrawerMode == DrawerMode.Temporary)
            DrawerOpen = false;
        else if (previous == DrawerMode.Temporary && DrawerMode == DrawerMode.Persistent)
            DrawerOpen = true;
        UpdateState();
    }

    private void UpdateState()
    {
        var mode = DrawerMode;
        var open = DrawerOpen || mode == DrawerMode.Mini;
        PseudoClasses.Set(":persistent", mode == DrawerMode.Persistent);
        PseudoClasses.Set(":temporary", mode == DrawerMode.Temporary);
        PseudoClasses.Set(":mini", mode == DrawerMode.Mini);
        PseudoClasses.Set(":open", open);

        var width = mode == DrawerMode.Mini ? SlateTokens.Size.Drawer.Mini : SlateTokens.Size.Drawer.Full;
        if (Drawer is Drawer d)
            d.IsMini = mode == DrawerMode.Mini;

        if (_drawer is not null)
        {
            _drawer.Width = width;
            _drawer.IsVisible = Drawer is not null && open;
        }

        if (_content is not null)
            _content.Margin = new Thickness(Drawer is not null && open && mode != DrawerMode.Temporary ? width : 0, 0, 0, 0);

        var overlay = mode == DrawerMode.Temporary && open && Drawer is not null;
        if (_scrim is not null)
            _scrim.IsVisible = overlay;

        if (overlay && !_wasTemporaryOpen)
            Post(() => (_drawer?.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(x => x.Focusable && x.IsEffectivelyVisible))?.Focus(NavigationMethod.Tab));
        else if (!overlay && _wasTemporaryOpen && mode == DrawerMode.Temporary)
        {
            var target = _focusBeforeOpen;
            _focusBeforeOpen = null;
            Post(() => target?.Focus());
        }
        _wasTemporaryOpen = overlay;
    }

    private static void Post(Action a) => global::Avalonia.Threading.Dispatcher.UIThread.Post(a);
}

/// <summary>56px top bar: optional menu (drawer toggle) button, title, centre content and trailing actions.</summary>
public class AppBar : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<AppBar, string?>(nameof(Title));
    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<AppBar, object?>(nameof(Actions));
    public static readonly StyledProperty<bool> ShowMenuButtonProperty = AvaloniaProperty.Register<AppBar, bool>(nameof(ShowMenuButton), true);

    /// <summary>Content before the title (e.g. a logo or back button).</summary>
    public static readonly StyledProperty<object?> LeadingProperty = AvaloniaProperty.Register<AppBar, object?>(nameof(Leading));

    /// <summary>Content in the middle (e.g. search). <see cref="ContentControl.Content"/> is shown here too.</summary>
    public static readonly StyledProperty<object?> CenterProperty = AvaloniaProperty.Register<AppBar, object?>(nameof(Center));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public object? Leading { get => GetValue(LeadingProperty); set => SetValue(LeadingProperty, value); }
    public object? Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }
    public bool ShowMenuButton { get => GetValue(ShowMenuButtonProperty); set => SetValue(ShowMenuButtonProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_MenuButton") is { } menu)
            menu.Click += (s, _) => menu.RaiseEvent(new RoutedEventArgs(AppShell.ToggleDrawerRequestedEvent, s));
    }
}

/// <summary>Navigation panel for an <see cref="AppShell"/>: header, scrolling items (Content) and footer.</summary>
[PseudoClasses(":mini")]
public class Drawer : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<Drawer, object?>(nameof(Header));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<Drawer, object?>(nameof(Footer));
    /// <summary>Inherited, so items inside the drawer (e.g. <see cref="NavItem"/>) can switch to icon-only themselves.</summary>
    public static readonly AttachedProperty<bool> IsMiniProperty =
        AvaloniaProperty.RegisterAttached<Drawer, Control, bool>(nameof(IsMini), inherits: true);

    static Drawer()
    {
        IsMiniProperty.Changed.AddClassHandler<Drawer>((d, e) => d.PseudoClasses.Set(":mini", e.GetNewValue<bool>()));
    }

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>Set by the AppShell in Mini mode: items show icons only.</summary>
    public bool IsMini { get => GetValue(IsMiniProperty); set => SetValue(IsMiniProperty, value); }

    public static bool GetIsMini(Control control) => control.GetValue(IsMiniProperty);
    public static void SetIsMini(Control control, bool value) => control.SetValue(IsMiniProperty, value);
}

/// <summary>A drawer navigation entry: icon, label, optional trailing count/status and an active state.</summary>
[PseudoClasses(":active", ":mini")]
public class NavItem : Button
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<NavItem, string?>(nameof(Icon));
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<NavItem, string?>(nameof(Label));
    public static readonly StyledProperty<string?> TrailingProperty = AvaloniaProperty.Register<NavItem, string?>(nameof(Trailing));
    public static readonly StyledProperty<bool> ActiveProperty = AvaloniaProperty.Register<NavItem, bool>(nameof(Active));

    static NavItem()
    {
        ActiveProperty.Changed.AddClassHandler<NavItem>((n, e) =>
        {
            n.PseudoClasses.Set(":active", e.GetNewValue<bool>());
            AutomationProperties.SetItemStatus(n, e.GetNewValue<bool>() ? "Current" : null);
        });
        // Driven by the inherited Drawer.IsMini rather than an ancestor selector: nested ancestor selectors with
        // comma lists inside ControlThemes don't scope reliably in Avalonia 12 (labels were hidden in full drawers).
        Drawer.IsMiniProperty.Changed.AddClassHandler<NavItem>((n, e) =>
        {
            n.PseudoClasses.Set(":mini", e.GetNewValue<bool>());
            n.UpdateTip();
        });
        LabelProperty.Changed.AddClassHandler<NavItem>((n, e) =>
        {
            AutomationProperties.SetName(n, e.GetNewValue<string?>());
            n.UpdateTip();
        });
    }

    protected override Type StyleKeyOverride => typeof(NavItem);

    /// <summary>The label is only repeated as a tooltip in a mini (icon-only) drawer, where it isn't visible.</summary>
    private void UpdateTip() => ToolTip.SetTip(this, GetValue(Drawer.IsMiniProperty) ? Label : null);

    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
    public bool Active { get => GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
}

/// <summary>
/// A window with Alloy's 36px custom title bar (icon, title, centre content, actions and neutral
/// minimise/maximise/close buttons). Hosts a <see cref="DialogHost"/> and <see cref="SnackbarHost"/> around its content,
/// bound to <see cref="SlateServices"/> unless other services are assigned.
/// </summary>
[PseudoClasses(":maximized")]
public class SlateWindow : Window
{
    public static readonly StyledProperty<object?> TitleBarContentProperty = AvaloniaProperty.Register<SlateWindow, object?>(nameof(TitleBarContent));
    public static readonly StyledProperty<object?> TitleBarActionsProperty = AvaloniaProperty.Register<SlateWindow, object?>(nameof(TitleBarActions));
    public static readonly StyledProperty<string?> AppIconProperty = AvaloniaProperty.Register<SlateWindow, string?>(nameof(AppIcon), "layers");
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<SlateWindow, string?>(nameof(Subtitle));
    public static readonly StyledProperty<Services.ISnackbarService?> SnackbarServiceProperty =
        AvaloniaProperty.Register<SlateWindow, Services.ISnackbarService?>(nameof(SnackbarService));
    public static readonly StyledProperty<Services.IDialogService?> DialogServiceProperty =
        AvaloniaProperty.Register<SlateWindow, Services.IDialogService?>(nameof(DialogService));

    public SlateWindow()
    {
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = SlateTokens.Size.Titlebar;
        // The system keeps the resize border; Slate draws the title bar and caption buttons.
        WindowDecorations = global::Avalonia.Controls.WindowDecorations.BorderOnly;
        SnackbarService = SlateServices.Snackbars;
        DialogService = SlateServices.Dialogs;
    }

    protected override Type StyleKeyOverride => typeof(SlateWindow);

    public object? TitleBarContent { get => GetValue(TitleBarContentProperty); set => SetValue(TitleBarContentProperty, value); }
    public object? TitleBarActions { get => GetValue(TitleBarActionsProperty); set => SetValue(TitleBarActionsProperty, value); }
    public string? AppIcon { get => GetValue(AppIconProperty); set => SetValue(AppIconProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Services.ISnackbarService? SnackbarService { get => GetValue(SnackbarServiceProperty); set => SetValue(SnackbarServiceProperty, value); }
    public Services.IDialogService? DialogService { get => GetValue(DialogServiceProperty); set => SetValue(DialogServiceProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Minimize") is { } min)
            min.Click += (_, _) => WindowState = WindowState.Minimized;
        if (e.NameScope.Find<Button>("PART_Maximize") is { } max)
            max.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Close();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty)
            PseudoClasses.Set(":maximized", WindowState == WindowState.Maximized);
    }
}
