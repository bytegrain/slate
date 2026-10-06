using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace Slate.Wpf.Tests;

/// <summary>
/// Runs test bodies on one long-lived STA thread that owns a WPF <see cref="Application"/> with Slate merged.
/// (WPF allows one Application per process, and pack:// URIs need it.)
/// </summary>
internal static class Wpf
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start);

    public static SlateTheme Theme { get; private set; } = null!;

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Theme = new SlateTheme { ReducedMotion = true };
            app.Resources = Theme;
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "Slate WPF test UI thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    public static void Run(Action body) => Run(() => { body(); return 0; });

    public static T Run<T>(Func<T> body)
    {
        ExceptionDispatchInfo? error = null;
        var result = UiDispatcher.Value.Invoke(() =>
        {
            try
            {
                Theme.ResetTheme();
                Theme.Mode = ThemeMode.Light;
                Theme.Density = Density.Compact;
                SlateTheme.Defaults = new SlateDefaults();
                return body();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
                return default!;
            }
        });
        error?.Throw();
        return result;
    }

    /// <summary>Hosts an element in an off-screen window and runs layout so templates and bindings are live.</summary>
    public static T Realize<T>(T element, double width = 800, double height = 600) where T : FrameworkElement
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            Content = element,
        };
        window.Show();
        Pump();
        return element;
    }

    /// <summary>Processes queued dispatcher work (layout, bindings, BeginInvoke).</summary>
    public static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>First visual descendant of type T (depth-first), or throws.</summary>
    public static T Find<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && (match?.Invoke(t) ?? true)) return t;
            try { return Find(child, match); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"No {typeof(T).Name} under {root}");
    }
}
