using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Slate.Wpf;

/// <summary>
/// Attached properties that let native WPF controls opt into Slate variants:
/// <c>&lt;Button sl:Ui.Variant="Primary" sl:Ui.Icon="plus" Content="New" /&gt;</c>.
/// </summary>
public static class Ui
{
    // ---- Variant / size ----

    public static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "Variant", typeof(ButtonVariant), typeof(Ui), new FrameworkPropertyMetadata(ButtonVariant.Secondary));

    public static ButtonVariant GetVariant(DependencyObject o) => (ButtonVariant)o.GetValue(VariantProperty);
    public static void SetVariant(DependencyObject o, ButtonVariant v) => o.SetValue(VariantProperty, v);

    public static readonly DependencyProperty SizeProperty = DependencyProperty.RegisterAttached(
        "Size", typeof(ControlSize), typeof(Ui), new FrameworkPropertyMetadata(ControlSize.Medium));

    public static ControlSize GetSize(DependencyObject o) => (ControlSize)o.GetValue(SizeProperty);
    public static void SetSize(DependencyObject o, ControlSize v) => o.SetValue(SizeProperty, v);

    // ---- Icons ----

    /// <summary>Leading icon: a name from the Slate icon set (e.g. "plus").</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    /// <summary>Trailing icon (e.g. "chevron-down" on a menu button).</summary>
    public static readonly DependencyProperty IconEndProperty = DependencyProperty.RegisterAttached(
        "IconEnd", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetIconEnd(DependencyObject o) => (string?)o.GetValue(IconEndProperty);
    public static void SetIconEnd(DependencyObject o, string? v) => o.SetValue(IconEndProperty, v);

    // ---- Keyboard shortcut hint ----

    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.RegisterAttached(
        "Shortcut", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static string? GetShortcut(DependencyObject o) => (string?)o.GetValue(ShortcutProperty);
    public static void SetShortcut(DependencyObject o, string? v) => o.SetValue(ShortcutProperty, v);

    // ---- Loading ----

    /// <summary>Shows a spinner, keeps the label, and blocks repeated activation (mouse and keyboard).</summary>
    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.RegisterAttached(
        "IsLoading", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, OnIsLoadingChanged));

    public static bool GetIsLoading(DependencyObject o) => (bool)o.GetValue(IsLoadingProperty);
    public static void SetIsLoading(DependencyObject o, bool v) => o.SetValue(IsLoadingProperty, v);

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.PreviewMouseLeftButtonDown += SwallowWhileLoading;
            element.PreviewKeyDown += SwallowKeysWhileLoading;
            AutomationProperties.SetItemStatus(element, "Busy");
        }
        else
        {
            element.PreviewMouseLeftButtonDown -= SwallowWhileLoading;
            element.PreviewKeyDown -= SwallowKeysWhileLoading;
            element.ClearValue(AutomationProperties.ItemStatusProperty);
        }
    }

    private static void SwallowWhileLoading(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static void SwallowKeysWhileLoading(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
            e.Handled = true;
    }

    // ---- Text inputs ----

    /// <summary>Placeholder for TextBox / PasswordBox. Never a substitute for a label.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null, OnPlaceholderChanged));

    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    /// <summary>True while a PasswordBox/TextBox is empty (maintained automatically; drives the placeholder).</summary>
    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached(
        "IsEmpty", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(true));

    public static bool GetIsEmpty(DependencyObject o) => (bool)o.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject o, bool v) => o.SetValue(IsEmptyProperty, v);

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case PasswordBox pb:
                pb.PasswordChanged -= UpdatePasswordEmpty;
                pb.PasswordChanged += UpdatePasswordEmpty;
                SetIsEmpty(pb, pb.SecurePassword.Length == 0);
                break;
            case TextBox tb:
                tb.TextChanged -= UpdateTextEmpty;
                tb.TextChanged += UpdateTextEmpty;
                SetIsEmpty(tb, string.IsNullOrEmpty(tb.Text));
                break;
        }
    }

    private static void UpdatePasswordEmpty(object sender, RoutedEventArgs e) =>
        SetIsEmpty((PasswordBox)sender, ((PasswordBox)sender).SecurePassword.Length == 0);

    private static void UpdateTextEmpty(object sender, TextChangedEventArgs e) =>
        SetIsEmpty((TextBox)sender, string.IsNullOrEmpty(((TextBox)sender).Text));

    /// <summary>Marks an input invalid (danger border). TextField sets this from its Error.</summary>
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));

    public static bool GetHasError(DependencyObject o) => (bool)o.GetValue(HasErrorProperty);
    public static void SetHasError(DependencyObject o, bool v) => o.SetValue(HasErrorProperty, v);

    /// <summary>Clicking anywhere in a <see cref="ToggleButton"/>'s row toggles it — already true for CheckBox; helper for custom rows.</summary>
    internal static bool IsToggle(object? o) => o is ToggleButton;
}
