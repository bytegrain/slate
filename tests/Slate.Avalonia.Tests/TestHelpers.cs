using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Slate.Avalonia.Tests;

internal static class TestHelpers
{
    /// <summary>Shows <paramref name="content"/> in a window of the given size and lays it out.</summary>
    public static Window Show(Control content, double width = 800, double height = 600)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Pump(window);
        return window;
    }

    /// <summary>Runs queued dispatcher work and a layout pass.</summary>
    public static void Pump(TopLevel? top = null)
    {
        Dispatcher.UIThread.RunJobs();
        top?.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    public static T Part<T>(this Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"Template part {name} ({typeof(T).Name}) not found in {control.GetType().Name}.");

    public static T? FindPart<T>(this Control control, string name) where T : Control =>
        control.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    public static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    /// <summary>The token colour for a theme, from the generated constants (#RRGGBB / #RRGGBBAA → Color).</summary>
    public static Color Token(string cssHex)
    {
        var h = cssHex.TrimStart('#');
        var a = h.Length == 8 ? Convert.ToByte(h[6..8], 16) : (byte)255;
        return Color.FromArgb(a, Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    public static object Resource(string key, ThemeVariant? variant = null)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant ?? ThemeVariant.Light, out var value), $"Resource {key} not found");
        return value!;
    }

    public static void Press(this TopLevel top, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        global::Avalonia.Headless.HeadlessWindowExtensions.KeyPress(top, key, modifiers, PhysicalKey.None, null);
        global::Avalonia.Headless.HeadlessWindowExtensions.KeyRelease(top, key, modifiers, PhysicalKey.None, null);
        Pump(top);
    }

    public static void Click(this TopLevel top, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top) ?? default;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, center, MouseButton.Left, default);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, center, MouseButton.Left, default);
        Pump(top);
    }

    public static void ClickAt(this TopLevel top, Point p)
    {
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseDown(top, p, MouseButton.Left, default);
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseUp(top, p, MouseButton.Left, default);
        Pump(top);
    }

    public static void Hover(this TopLevel top, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top) ?? default;
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, center, default);
        Pump(top);
    }

    public static void MoveTo(this TopLevel top, Point p)
    {
        global::Avalonia.Headless.HeadlessWindowExtensions.MouseMove(top, p, default);
        Pump(top);
    }

    public static IInputElement? Focused(this TopLevel top) => top.FocusManager?.GetFocusedElement();

    /// <summary>Runs <paramref name="body"/> with a theme setting changed, restoring it afterwards (the app is shared between tests).</summary>
    public static void WithTheme(Action<SlateTheme> change, Action<SlateTheme> restore, Action body)
    {
        var theme = SlateTheme.Current!;
        change(theme);
        try { body(); }
        finally { restore(theme); }
    }
}

