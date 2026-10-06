using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Slate.Avalonia;

/// <summary>
/// Attached properties that apply Slate options to native controls:
/// <c>sl:Sl.Variant="Primary"</c>, <c>sl:Sl.Size="Small"</c>, <c>sl:Sl.Icon="plus"</c>, <c>sl:Sl.IsLoading="True"</c>.
/// Each maps to style classes (primary, small, loading, icon-only…) so plain <c>Classes="primary"</c> works too.
/// </summary>
public static class Sl
{
    public static readonly AttachedProperty<ButtonVariant> VariantProperty =
        AvaloniaProperty.RegisterAttached<Control, ButtonVariant>("Variant", typeof(Sl));

    public static readonly AttachedProperty<ControlSize> SizeProperty =
        AvaloniaProperty.RegisterAttached<Control, ControlSize>("Size", typeof(Sl), ControlSize.Medium);

    /// <summary>Name of a built-in icon (see SlateIcons) shown before the content.</summary>
    public static readonly AttachedProperty<string?> IconProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Icon", typeof(Sl));

    /// <summary>Shows a spinner, blocks activation and reports busy to assistive tech.</summary>
    public static readonly AttachedProperty<bool> IsLoadingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsLoading", typeof(Sl));

    /// <summary>Keyboard shortcut hint rendered as a Kbd after the content (e.g. "⌘↵").</summary>
    public static readonly AttachedProperty<string?> ShortcutProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Shortcut", typeof(Sl));

    internal static readonly string[] VariantClasses = ["primary", "ghost", "danger", "danger-solid", "link"];

    static Sl()
    {
        VariantProperty.Changed.AddClassHandler<Control>((c, _) => UpdateVariant(c));
        SizeProperty.Changed.AddClassHandler<Control>((c, _) => UpdateSize(c));
        IconProperty.Changed.AddClassHandler<Control>((c, _) => UpdateIconOnly(c));
        ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c, _) => UpdateIconOnly(c));
        IsLoadingProperty.Changed.AddClassHandler<Control>((c, e) => UpdateLoading(c, e.GetNewValue<bool>()));
        // Loading buttons swallow Click (class handlers run first), so neither handlers nor Command run twice.
        Button.ClickEvent.AddClassHandler<Button>((b, e) => { if (GetIsLoading(b)) e.Handled = true; });
        ShortcutProperty.Changed.AddClassHandler<Control>((c, e) => c.Classes.Set("has-shortcut", e.GetNewValue<string?>() is { Length: > 0 }));
    }

    public static ButtonVariant GetVariant(Control c) => c.GetValue(VariantProperty);
    public static void SetVariant(Control c, ButtonVariant v) => c.SetValue(VariantProperty, v);
    public static ControlSize GetSize(Control c) => c.GetValue(SizeProperty);
    public static void SetSize(Control c, ControlSize v) => c.SetValue(SizeProperty, v);
    public static string? GetIcon(Control c) => c.GetValue(IconProperty);
    public static void SetIcon(Control c, string? v) => c.SetValue(IconProperty, v);
    public static bool GetIsLoading(Control c) => c.GetValue(IsLoadingProperty);
    public static void SetIsLoading(Control c, bool v) => c.SetValue(IsLoadingProperty, v);
    public static string? GetShortcut(Control c) => c.GetValue(ShortcutProperty);
    public static void SetShortcut(Control c, string? v) => c.SetValue(ShortcutProperty, v);

    /// <summary>Style class used for a variant ("" for the default secondary variant).</summary>
    public static string ClassFor(ButtonVariant v) => v switch
    {
        ButtonVariant.Primary => "primary",
        ButtonVariant.Ghost => "ghost",
        ButtonVariant.Danger => "danger",
        ButtonVariant.DangerSolid => "danger-solid",
        ButtonVariant.Link => "link",
        _ => "",
    };

    private static void UpdateVariant(Control c)
    {
        var cls = ClassFor(GetVariant(c));
        foreach (var v in VariantClasses)
            c.Classes.Set(v, v == cls);
    }

    private static void UpdateSize(Control c)
    {
        var size = GetSize(c);
        c.Classes.Set("small", size == ControlSize.Small);
        c.Classes.Set("large", size == ControlSize.Large);
    }

    private static void UpdateIconOnly(Control c)
    {
        var hasIcon = GetIcon(c) is { Length: > 0 };
        c.Classes.Set("has-icon", hasIcon);
        var noContent = c is not ContentControl cc || cc.Content is null || cc.Content is string { Length: 0 };
        c.Classes.Set("icon-only", hasIcon && noContent);
    }

    private static void UpdateLoading(Control c, bool loading)
    {
        c.Classes.Set("loading", loading);
        AutomationProperties.SetItemStatus(c, loading ? "Busy" : null);
    }
}
