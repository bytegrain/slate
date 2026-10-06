using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Slate.Wpf;

/// <summary>
/// Window with the Alloy 36px custom title bar: app icon, title, optional centre content (e.g. search),
/// trailing actions and neutral minimise / maximise / close. Hosts the snackbar and dialog layers.
/// Supports Windows 11 snap layouts on the maximise button and pads correctly when maximised.
/// </summary>
[TemplatePart(Name = PartMaximize, Type = typeof(Button))]
public class SlateWindow : Window
{
    public const string PartMaximize = "PART_Maximize";

    public static readonly DependencyProperty TitleBarContentProperty = DependencyProperty.Register(
        nameof(TitleBarContent), typeof(object), typeof(SlateWindow), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TitleBarActionsProperty = DependencyProperty.Register(
        nameof(TitleBarActions), typeof(object), typeof(SlateWindow), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty AppIconProperty = DependencyProperty.Register(
        nameof(AppIcon), typeof(string), typeof(SlateWindow), new FrameworkPropertyMetadata("layers"));

    public static readonly DependencyProperty SnackbarServiceProperty = DependencyProperty.Register(
        nameof(SnackbarService), typeof(ISnackbarService), typeof(SlateWindow), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DialogServiceProperty = DependencyProperty.Register(
        nameof(DialogService), typeof(IDialogService), typeof(SlateWindow), new FrameworkPropertyMetadata(null));

    private static readonly DependencyPropertyKey IsMaximizeHoveredPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsMaximizeHovered), typeof(bool), typeof(SlateWindow), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsMaximizeHoveredProperty = IsMaximizeHoveredPropertyKey.DependencyProperty;

    private Button? _maximize;

    static SlateWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SlateWindow), new FrameworkPropertyMetadata(typeof(SlateWindow)));
    }

    public SlateWindow()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = SlateTokens.Size.Titlebar,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0, 0, 0, 1), // keeps the native shadow + snap behaviour
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) => SystemCommands.MaximizeWindow(this)));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.RestoreWindowCommand, (_, _) => SystemCommands.RestoreWindow(this)));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));
    }

    /// <summary>Centre of the title bar (search, tabs, a connection switcher…). Hit-testable.</summary>
    public object? TitleBarContent { get => GetValue(TitleBarContentProperty); set => SetValue(TitleBarContentProperty, value); }

    /// <summary>Right side of the title bar, before the window buttons (theme toggle, avatar…).</summary>
    public object? TitleBarActions { get => GetValue(TitleBarActionsProperty); set => SetValue(TitleBarActionsProperty, value); }

    /// <summary>Slate icon name shown in the title bar.</summary>
    public string? AppIcon { get => (string?)GetValue(AppIconProperty); set => SetValue(AppIconProperty, value); }

    /// <summary>Snackbar service rendered by this window; defaults to <see cref="SlateServices.Snackbar"/>.</summary>
    public ISnackbarService? SnackbarService { get => (ISnackbarService?)GetValue(SnackbarServiceProperty); set => SetValue(SnackbarServiceProperty, value); }

    /// <summary>Dialog service rendered by this window; defaults to <see cref="SlateServices.Dialogs"/>.</summary>
    public IDialogService? DialogService { get => (IDialogService?)GetValue(DialogServiceProperty); set => SetValue(DialogServiceProperty, value); }

    /// <summary>True while the pointer is over the maximise button (driven by the snap-layout hit test).</summary>
    public bool IsMaximizeHovered => (bool)GetValue(IsMaximizeHoveredProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _maximize = GetTemplateChild(PartMaximize) as Button;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    // Windows 11 shows snap layouts only when WM_NCHITTEST reports HTMAXBUTTON over the maximise button.
    private const int WM_NCHITTEST = 0x0084, WM_NCMOUSELEAVE = 0x02A2, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2;
    private const int HTMAXBUTTON = 9;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST when IsOverMaximize(lParam):
                SetValue(IsMaximizeHoveredPropertyKey, true);
                handled = true;
                return HTMAXBUTTON;
            case WM_NCHITTEST:
            case WM_NCMOUSELEAVE:
                SetValue(IsMaximizeHoveredPropertyKey, false);
                break;
            case WM_NCLBUTTONDOWN when wParam.ToInt32() == HTMAXBUTTON:
                handled = true;
                break;
            case WM_NCLBUTTONUP when wParam.ToInt32() == HTMAXBUTTON:
                handled = true;
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
        }
        return IntPtr.Zero;
    }

    private bool IsOverMaximize(IntPtr lParam)
    {
        if (_maximize is not { IsVisible: true } button || ResizeMode is ResizeMode.NoResize or ResizeMode.CanMinimize)
            return false;

        var screen = new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));
        try
        {
            var local = button.PointFromScreen(screen);
            return local.X >= 0 && local.Y >= 0 && local.X < button.ActualWidth && local.Y < button.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // not connected to a presentation source yet
        }
    }

    /// <summary>Extra padding needed when maximised so content isn't clipped by the off-screen resize border.</summary>
    public static Thickness MaximizedPadding
    {
        get
        {
            var frame = SystemParameters.WindowResizeBorderThickness;
            var padded = GetSystemMetrics(SM_CXPADDEDBORDER) / GetDpiScale();
            return new Thickness(frame.Left + padded, frame.Top + padded, frame.Right + padded, frame.Bottom + padded);
        }
    }

    private const int SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private static double GetDpiScale() =>
        Application.Current?.MainWindow is { } w ? VisualTreeHelperDpi(w) : 1.0;

    private static double VisualTreeHelperDpi(Visual v)
    {
        try { return System.Windows.Media.VisualTreeHelper.GetDpi(v).DpiScaleX; }
        catch { return 1.0; }
    }
}
