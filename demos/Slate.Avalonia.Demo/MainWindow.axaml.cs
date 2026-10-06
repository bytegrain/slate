using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Demo.Pages;

namespace Slate.Avalonia.Demo;

public partial class MainWindow : SlateWindow
{
    private readonly Dictionary<string, Func<Control>> _pages = new()
    {
        ["components"] = () => new ComponentsPage(),
        ["layout"] = () => new LayoutPage(),
        ["feedback"] = () => new FeedbackPage(),
        ["dialogs"] = () => new DialogsPage(),
        ["sample"] = () => new MigrationPage(),
    };

    public MainWindow()
    {
        InitializeComponent();

        foreach (var item in Nav.Children.OfType<NavItem>())
            item.Click += (_, _) => Navigate(item);
        Navigate(Nav.Children.OfType<NavItem>().First());

        LightButton.Click += (_, _) => SetMode(ThemeMode.Light);
        DarkButton.Click += (_, _) => SetMode(ThemeMode.Dark);
        SystemButton.Click += (_, _) => SetMode(ThemeMode.System);
        SetMode(ThemeMode.System);

        DensitySwitch.IsCheckedChanged += (_, _) =>
        {
            if (SlateTheme.Current is { } theme)
                theme.Density = DensitySwitch.IsChecked == true ? Density.Comfortable : Density.Compact;
        };
    }

    private void SetMode(ThemeMode mode)
    {
        if (SlateTheme.Current is { } theme)
            theme.Mode = mode;
        LightButton.IsChecked = mode == ThemeMode.Light;
        DarkButton.IsChecked = mode == ThemeMode.Dark;
        SystemButton.IsChecked = mode == ThemeMode.System;
    }

    private void Navigate(NavItem item)
    {
        foreach (var n in Nav.Children.OfType<NavItem>())
            n.IsActive = n == item;
        Bar.Title = item.Label;
        Page.Content = new ScrollViewer { Content = _pages[(string)item.Tag!]() };
        if (Shell.DrawerMode == DrawerMode.Temporary)
            Shell.IsDrawerOpen = false;
    }

    /// <summary>Renders each page in each theme to {dir}/{page}-{theme}.png (used by `--screenshot`).</summary>
    public async Task CaptureAllAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        {
            SetMode(mode);
            foreach (var item in Nav.Children.OfType<NavItem>())
            {
                Navigate(item);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                await Task.Delay(1500); // let enter transitions finish

                var file = Path.Combine(dir, $"{item.Tag}-{mode.ToString().ToLowerInvariant()}.png");
                if (OperatingSystem.IsMacOS() && MacWindowNumber() is { } windowId)
                {
                    // Real on-screen pixels of this window only (includes OS chrome; needs Screen Recording permission).
                    Activate();
                    using var p = System.Diagnostics.Process.Start("screencapture", ["-x", "-o", $"-l{windowId}", file]);
                    await p!.WaitForExitAsync();
                }
                else
                {
                    var scale = RenderScaling;
                    var size = new PixelSize((int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                    bitmap.Render(this);
                    bitmap.Save(file, PngBitmapEncoderOptions.Default);
                }
            }
        }
    }

    /// <summary>The Quartz window number of this window on macOS (NSWindow.windowNumber).</summary>
    private long? MacWindowNumber()
    {
        if (TryGetPlatformHandle() is not { } handle || handle.Handle == IntPtr.Zero)
            return null;
        var target = handle.Handle;
        if (handle.HandleDescriptor == "NSView")
            target = ObjC.Send(target, ObjC.Sel("window"));
        return target == IntPtr.Zero ? null : ObjC.SendLong(target, ObjC.Sel("windowNumber"));
    }

    private static class ObjC
    {
        private const string Lib = "/usr/lib/libobjc.dylib";
        [System.Runtime.InteropServices.DllImport(Lib, EntryPoint = "sel_registerName")]
        public static extern IntPtr Sel(string name);
        [System.Runtime.InteropServices.DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [System.Runtime.InteropServices.DllImport(Lib, EntryPoint = "objc_msgSend")]
        public static extern long SendLong(IntPtr receiver, IntPtr selector);
    }
}
