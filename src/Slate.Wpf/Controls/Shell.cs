using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Slate.Layout;

namespace Slate.Wpf;

public enum DrawerMode
{
    /// <summary>Persistent from md (900px) up, temporary below. The default.</summary>
    Responsive,
    /// <summary>Pushes content; toggled in place.</summary>
    Persistent,
    /// <summary>Overlays content with a scrim; closes on scrim click or Escape.</summary>
    Temporary,
    /// <summary>56px icon rail; labels become tooltips.</summary>
    Mini,
}

/// <summary>
/// App frame: app bar on top, drawer on the left, scrolling main content (docs/design/layout.md#app-shell).
/// </summary>
[TemplatePart(Name = "PART_Scrim", Type = typeof(FrameworkElement))]
public class AppShell : ContentControl
{
    public static readonly RoutedCommand ToggleDrawerCommand = new(nameof(ToggleDrawerCommand), typeof(AppShell));

    public static readonly DependencyProperty AppBarProperty = DependencyProperty.Register(
        nameof(AppBar), typeof(object), typeof(AppShell), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DrawerProperty = DependencyProperty.Register(
        nameof(Drawer), typeof(object), typeof(AppShell), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DrawerModeProperty = DependencyProperty.Register(
        nameof(DrawerMode), typeof(DrawerMode), typeof(AppShell), new FrameworkPropertyMetadata(DrawerMode.Responsive, (d, _) => ((AppShell)d).UpdateState()));

    public static readonly DependencyProperty IsDrawerOpenProperty = DependencyProperty.Register(
        nameof(IsDrawerOpen), typeof(bool), typeof(AppShell),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((AppShell)d).OnDrawerOpenChanged((bool)e.NewValue)));

    private static readonly DependencyPropertyKey IsOverlayPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsOverlay), typeof(bool), typeof(AppShell), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsOverlayProperty = IsOverlayPropertyKey.DependencyProperty;

    /// <summary>Inherited flag telling drawer content (NavItem) to render as an icon rail.</summary>
    public static readonly DependencyProperty IsMiniProperty = DependencyProperty.RegisterAttached(
        "IsMini", typeof(bool), typeof(AppShell), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsMini(DependencyObject o) => (bool)o.GetValue(IsMiniProperty);
    public static void SetIsMini(DependencyObject o, bool v) => o.SetValue(IsMiniProperty, v);

    private IInputElement? _focusBeforeOverlay;

    static AppShell()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppShell), new FrameworkPropertyMetadata(typeof(AppShell)));
        FocusableProperty.OverrideMetadata(typeof(AppShell), new FrameworkPropertyMetadata(false));
    }

    public AppShell()
    {
        CommandBindings.Add(new CommandBinding(ToggleDrawerCommand, (_, _) => IsDrawerOpen = !IsDrawerOpen));
        SizeChanged += (_, _) => UpdateState();
    }

    public object? AppBar { get => GetValue(AppBarProperty); set => SetValue(AppBarProperty, value); }
    public object? Drawer { get => GetValue(DrawerProperty); set => SetValue(DrawerProperty, value); }
    public DrawerMode DrawerMode { get => (DrawerMode)GetValue(DrawerModeProperty); set => SetValue(DrawerModeProperty, value); }
    public bool IsDrawerOpen { get => (bool)GetValue(IsDrawerOpenProperty); set => SetValue(IsDrawerOpenProperty, value); }

    /// <summary>True when the drawer currently overlays content (temporary, or responsive below md).</summary>
    public bool IsOverlay => (bool)GetValue(IsOverlayProperty);

    /// <summary>The mode actually in effect at the current width.</summary>
    public static DrawerMode Effective(DrawerMode mode, double width) =>
        mode == DrawerMode.Responsive
            ? (Breakpoints.FromWidth(Math.Max(0, width)) >= Breakpoint.Md ? DrawerMode.Persistent : DrawerMode.Temporary)
            : mode;

    private void UpdateState()
    {
        var effective = Effective(DrawerMode, ActualWidth);
        var wasOverlay = IsOverlay;
        SetValue(IsOverlayPropertyKey, effective == DrawerMode.Temporary);
        SetIsMini(this, effective == DrawerMode.Mini);

        // Entering overlay mode (window got narrow) closes the drawer so it doesn't cover content unasked.
        if (!wasOverlay && IsOverlay && IsDrawerOpen && IsLoaded)
            IsDrawerOpen = false;
        else if (wasOverlay && !IsOverlay && !IsDrawerOpen)
            IsDrawerOpen = true;
    }

    private void OnDrawerOpenChanged(bool open)
    {
        if (!IsOverlay)
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
            scrim.MouseLeftButtonDown += (_, _) => IsDrawerOpen = false;
        UpdateState();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && IsOverlay && IsDrawerOpen)
        {
            IsDrawerOpen = false;
            e.Handled = true;
        }
    }
}

/// <summary>56px top bar: navigation toggle, title, centre content (Content) and trailing Actions.</summary>
public class AppBar : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(object), typeof(AppBar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(AppBar), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ShowNavigationButtonProperty = DependencyProperty.Register(
        nameof(ShowNavigationButton), typeof(bool), typeof(AppBar), new FrameworkPropertyMetadata(true));

    static AppBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AppBar), new FrameworkPropertyMetadata(typeof(AppBar)));
        FocusableProperty.OverrideMetadata(typeof(AppBar), new FrameworkPropertyMetadata(false));
    }

    public object? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public bool ShowNavigationButton { get => (bool)GetValue(ShowNavigationButtonProperty); set => SetValue(ShowNavigationButtonProperty, value); }
}

/// <summary>Drawer navigation entry: icon + label (+ optional trailing Badge). Collapses to the icon in mini mode.</summary>
public class NavItem : ListBoxItem
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(NavItem), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TrailingProperty = DependencyProperty.Register(
        nameof(Trailing), typeof(object), typeof(NavItem), new FrameworkPropertyMetadata(null));

    static NavItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavItem), new FrameworkPropertyMetadata(typeof(NavItem)));
    }

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Trailing { get => GetValue(TrailingProperty); set => SetValue(TrailingProperty, value); }
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
