using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Slate.Wpf;

/// <summary>
/// Slate options for native WPF controls, spelled as in design/api/components.json:
/// <c>&lt;Button sl:Sl.Variant="Solid" sl:Sl.Tone="Accent" sl:Sl.StartIcon="plus" Content="New" /&gt;</c>.
/// Slate's own controls expose the same options as regular properties (most are these properties via AddOwner).
/// Options left unset fall back to <see cref="SlateTheme.Defaults"/>.
/// </summary>
public static partial class Sl
{
    private static DependencyProperty Attached<T>(string name, T defaultValue, PropertyChangedCallback? changed = null, FrameworkPropertyMetadataOptions flags = FrameworkPropertyMetadataOptions.None) =>
        DependencyProperty.RegisterAttached(name, typeof(T), typeof(Sl), new FrameworkPropertyMetadata(defaultValue, flags, changed));

    private static readonly PropertyChangedCallback Restyle = (d, _) => Styling.Refresh(d);

    // ---- Appearance ----

    /// <summary>Button chrome (Outlined, Solid, Soft, Ghost, Link). Unset = <c>SlateTheme.Defaults.Button.Variant</c>.</summary>
    public static readonly DependencyProperty VariantProperty = Attached("Variant", ButtonVariant.Outlined, Restyle);
    public static ButtonVariant GetVariant(DependencyObject o) => (ButtonVariant)o.GetValue(VariantProperty);
    public static void SetVariant(DependencyObject o, ButtonVariant v) => o.SetValue(VariantProperty, v);

    /// <summary>Colour role. Unset = the component's default (buttons: Defaults.Button.Tone; selection/progress: Accent).</summary>
    public static readonly DependencyProperty ToneProperty = Attached("Tone", Tone.Neutral, Restyle);
    public static Tone GetTone(DependencyObject o) => (Tone)o.GetValue(ToneProperty);
    public static void SetTone(DependencyObject o, Tone v) => o.SetValue(ToneProperty, v);

    public static readonly DependencyProperty SizeProperty = Attached("Size", ControlSize.Medium, Restyle);
    public static ControlSize GetSize(DependencyObject o) => (ControlSize)o.GetValue(SizeProperty);
    public static void SetSize(DependencyObject o, ControlSize v) => o.SetValue(SizeProperty, v);

    /// <summary>Corner radius override. Default uses the component token (e.g. Sl.Component.Button.Radius).</summary>
    public static readonly DependencyProperty RadiusProperty = Attached("Radius", Slate.Radius.Default, Restyle);
    public static Radius GetRadius(DependencyObject o) => (Radius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, Radius v) => o.SetValue(RadiusProperty, v);

    // ---- Content ----

    /// <summary>Leading icon: a name from the Slate icon set (e.g. "plus").</summary>
    public static readonly DependencyProperty StartIconProperty = Attached<string?>("StartIcon", null);
    public static string? GetStartIcon(DependencyObject o) => (string?)o.GetValue(StartIconProperty);
    public static void SetStartIcon(DependencyObject o, string? v) => o.SetValue(StartIconProperty, v);

    /// <summary>Trailing icon (e.g. "chevron-down" on a menu button).</summary>
    public static readonly DependencyProperty EndIconProperty = Attached<string?>("EndIcon", null);
    public static string? GetEndIcon(DependencyObject o) => (string?)o.GetValue(EndIconProperty);
    public static void SetEndIcon(DependencyObject o, string? v) => o.SetValue(EndIconProperty, v);

    /// <summary>Square button showing only its icon. Give it a <see cref="LabelProperty"/>.</summary>
    public static readonly DependencyProperty IconOnlyProperty = Attached("IconOnly", false);
    public static bool GetIconOnly(DependencyObject o) => (bool)o.GetValue(IconOnlyProperty);
    public static void SetIconOnly(DependencyObject o, bool v) => o.SetValue(IconOnlyProperty, v);

    /// <summary>Accessible name (sets AutomationProperties.Name). Required for icon-only buttons.</summary>
    public static readonly DependencyProperty LabelProperty = Attached<string?>("Label", null,
        (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? ""));
    public static string? GetLabel(DependencyObject o) => (string?)o.GetValue(LabelProperty);
    public static void SetLabel(DependencyObject o, string? v) => o.SetValue(LabelProperty, v);

    /// <summary>Keyboard shortcut hint shown as a Kbd after the label.</summary>
    public static readonly DependencyProperty ShortcutProperty = Attached<string?>("Shortcut", null);
    public static string? GetShortcut(DependencyObject o) => (string?)o.GetValue(ShortcutProperty);
    public static void SetShortcut(DependencyObject o, string? v) => o.SetValue(ShortcutProperty, v);

    /// <summary>Supporting text under a checkbox/radio label.</summary>
    public static readonly DependencyProperty DescriptionProperty = Attached<string?>("Description", null);
    public static string? GetDescription(DependencyObject o) => (string?)o.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject o, string? v) => o.SetValue(DescriptionProperty, v);

    /// <summary>Side of the label for checkboxes, radios and switches. Unset = Defaults.Selection.LabelPlacement.</summary>
    public static readonly DependencyProperty LabelPlacementProperty = Attached("LabelPlacement", Placement.End, Restyle);
    public static Placement GetLabelPlacement(DependencyObject o) => (Placement)o.GetValue(LabelPlacementProperty);
    public static void SetLabelPlacement(DependencyObject o, Placement v) => o.SetValue(LabelPlacementProperty, v);

    // ---- State ----

    /// <summary>Shows a spinner, keeps the label, and blocks repeated activation (mouse and keyboard).</summary>
    public static readonly DependencyProperty LoadingProperty = Attached("Loading", false, OnLoadingChanged);
    public static bool GetLoading(DependencyObject o) => (bool)o.GetValue(LoadingProperty);
    public static void SetLoading(DependencyObject o, bool v) => o.SetValue(LoadingProperty, v);

    /// <summary>Stretches a button to the width of its container.</summary>
    public static readonly DependencyProperty FullWidthProperty = Attached("FullWidth", false);
    public static bool GetFullWidth(DependencyObject o) => (bool)o.GetValue(FullWidthProperty);
    public static void SetFullWidth(DependencyObject o, bool v) => o.SetValue(FullWidthProperty, v);

    /// <summary>Toggle state for a plain Button (null = not a toggle). Pressed buttons show the accent tint.</summary>
    public static readonly DependencyProperty PressedProperty = Attached<bool?>("Pressed", null,
        (d, e) => AutomationProperties.SetItemStatus(d, e.NewValue is true ? "pressed" : e.NewValue is false ? "not pressed" : ""));
    public static bool? GetPressed(DependencyObject o) => (bool?)o.GetValue(PressedProperty);
    public static void SetPressed(DependencyObject o, bool? v) => o.SetValue(PressedProperty, v);

    /// <summary>Shows the percentage beside a ProgressBar.</summary>
    public static readonly DependencyProperty ShowValueProperty = Attached("ShowValue", false);
    public static bool GetShowValue(DependencyObject o) => (bool)o.GetValue(ShowValueProperty);
    public static void SetShowValue(DependencyObject o, bool v) => o.SetValue(ShowValueProperty, v);

    /// <summary>
    /// Scoped density: merges the compact/comfortable control sizes into this element's resources, so every
    /// Slate control inside it uses them. Inherited, so descendants can read the density in effect.
    /// </summary>
    public static readonly DependencyProperty DensityProperty = Attached<Density?>("Density", null, OnDensityChanged, FrameworkPropertyMetadataOptions.Inherits);
    public static Density? GetDensity(DependencyObject o) => (Density?)o.GetValue(DensityProperty);
    public static void SetDensity(DependencyObject o, Density? v) => o.SetValue(DensityProperty, v);

    // ---- Text inputs ----

    /// <summary>Placeholder for TextBox / PasswordBox. Never a substitute for a label.</summary>
    public static readonly DependencyProperty PlaceholderProperty = Attached<string?>("Placeholder", null, OnPlaceholderChanged);
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    /// <summary>True while a TextBox/PasswordBox is empty (maintained automatically; drives the placeholder).</summary>
    public static readonly DependencyProperty EmptyProperty = Attached("Empty", true);
    public static bool GetEmpty(DependencyObject o) => (bool)o.GetValue(EmptyProperty);
    public static void SetEmpty(DependencyObject o, bool v) => o.SetValue(EmptyProperty, v);

    /// <summary>Marks an input invalid (danger border). TextField sets this from its Error.</summary>
    public static readonly DependencyProperty HasErrorProperty = Attached("HasError", false);
    public static bool GetHasError(DependencyObject o) => (bool)o.GetValue(HasErrorProperty);
    public static void SetHasError(DependencyObject o, bool v) => o.SetValue(HasErrorProperty, v);

    // ---- Infrastructure: which styling rules apply (set by Slate's styles / control constructors) ----

    public static readonly DependencyProperty KindProperty = Attached("Kind", SlKind.None, OnKindChanged);
    public static SlKind GetKind(DependencyObject o) => (SlKind)o.GetValue(KindProperty);
    public static void SetKind(DependencyObject o, SlKind v) => o.SetValue(KindProperty, v);

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe)
        {
            fe.Loaded -= RefreshOnLoaded;
            fe.Loaded += RefreshOnLoaded;
        }
        Styling.Refresh(d);
    }

    // Defaults can change after construction (AddSlate runs before windows open, but tests and demos change them live).
    private static void RefreshOnLoaded(object sender, RoutedEventArgs e) => Styling.Refresh((DependencyObject)sender);

    private static void OnLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        switch (d)
        {
            case PasswordBox pb:
                pb.PasswordChanged -= UpdatePasswordEmpty;
                pb.PasswordChanged += UpdatePasswordEmpty;
                SetEmpty(pb, pb.SecurePassword.Length == 0);
                break;
            case TextBox tb:
                tb.TextChanged -= UpdateTextEmpty;
                tb.TextChanged += UpdateTextEmpty;
                SetEmpty(tb, string.IsNullOrEmpty(tb.Text));
                break;
        }
    }

    private static void UpdatePasswordEmpty(object sender, RoutedEventArgs e) =>
        SetEmpty((PasswordBox)sender, ((PasswordBox)sender).SecurePassword.Length == 0);

    private static void UpdateTextEmpty(object sender, TextChangedEventArgs e) =>
        SetEmpty((TextBox)sender, string.IsNullOrEmpty(((TextBox)sender).Text));

    private static readonly DependencyProperty DensityDictionaryProperty = Attached<ResourceDictionary?>("DensityDictionary", null);

    private static void OnDensityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Only the element where density is set locally gets the dictionary; descendants just inherit the value.
        if (d is not FrameworkElement fe || DependencyPropertyHelper.GetValueSource(fe, DensityProperty).BaseValueSource != BaseValueSource.Local)
            return;

        if (fe.GetValue(DensityDictionaryProperty) is ResourceDictionary previous)
            fe.Resources.MergedDictionaries.Remove(previous);

        if (e.NewValue is Density density)
        {
            var dictionary = new ResourceDictionary { Source = SlateTheme.PackUri(SlateTheme.DensityPath(density)) };
            fe.Resources.MergedDictionaries.Add(dictionary);
            fe.SetValue(DensityDictionaryProperty, dictionary);
        }
        else
        {
            fe.ClearValue(DensityDictionaryProperty);
        }
    }

    /// <summary>Clicking anywhere in a <see cref="ToggleButton"/>'s row toggles it — already true for CheckBox; helper for custom rows.</summary>
    internal static bool IsToggle(object? o) => o is ToggleButton;
}

/// <summary>Which Slate styling rules an element follows (set by Slate's styles; rarely needed in app code).</summary>
public enum SlKind
{
    None,
    Button,
    Selection,
    Progress,
    Spinner,
    Badge,
    Alert,
    Card,
    Field,
}

/// <summary>
/// Values Slate computes from the options above plus <see cref="SlateTheme.Defaults"/>. Templates bind to these;
/// app code should read rather than set them.
/// </summary>
public static partial class Sl
{
    private static DependencyProperty Computed<T>(string name, T defaultValue) =>
        DependencyProperty.RegisterAttached(name, typeof(T), typeof(Sl), new FrameworkPropertyMetadata(defaultValue));

    public static readonly DependencyProperty ActualVariantProperty = Computed("ActualVariant", ButtonVariant.Outlined);
    public static ButtonVariant GetActualVariant(DependencyObject o) => (ButtonVariant)o.GetValue(ActualVariantProperty);
    public static void SetActualVariant(DependencyObject o, ButtonVariant v) => o.SetValue(ActualVariantProperty, v);

    public static readonly DependencyProperty ActualToneProperty = Computed("ActualTone", Tone.Neutral);
    public static Tone GetActualTone(DependencyObject o) => (Tone)o.GetValue(ActualToneProperty);
    public static void SetActualTone(DependencyObject o, Tone v) => o.SetValue(ActualToneProperty, v);

    public static readonly DependencyProperty ActualSizeProperty = Computed("ActualSize", ControlSize.Medium);
    public static ControlSize GetActualSize(DependencyObject o) => (ControlSize)o.GetValue(ActualSizeProperty);
    public static void SetActualSize(DependencyObject o, ControlSize v) => o.SetValue(ActualSizeProperty, v);

    public static readonly DependencyProperty ActualLabelPlacementProperty = Computed("ActualLabelPlacement", Placement.End);
    public static Placement GetActualLabelPlacement(DependencyObject o) => (Placement)o.GetValue(ActualLabelPlacementProperty);
    public static void SetActualLabelPlacement(DependencyObject o, Placement v) => o.SetValue(ActualLabelPlacementProperty, v);

    public static readonly DependencyProperty ActualCornerRadiusProperty = Computed("ActualCornerRadius", new CornerRadius(6));
    public static CornerRadius GetActualCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(ActualCornerRadiusProperty);
    public static void SetActualCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(ActualCornerRadiusProperty, v);

    /// <summary>Horizontal padding (px) from the component's padding token for the current size.</summary>
    public static readonly DependencyProperty PaddingXProperty = Computed("PaddingX", 12.0);
    public static double GetPaddingX(DependencyObject o) => (double)o.GetValue(PaddingXProperty);
    public static void SetPaddingX(DependencyObject o, double v) => o.SetValue(PaddingXProperty, v);

    public static readonly DependencyProperty ChromeBackgroundProperty = Computed<Brush?>("ChromeBackground", null);
    public static Brush? GetChromeBackground(DependencyObject o) => (Brush?)o.GetValue(ChromeBackgroundProperty);
    public static void SetChromeBackground(DependencyObject o, Brush? v) => o.SetValue(ChromeBackgroundProperty, v);

    public static readonly DependencyProperty ChromeBorderProperty = Computed<Brush?>("ChromeBorder", null);
    public static Brush? GetChromeBorder(DependencyObject o) => (Brush?)o.GetValue(ChromeBorderProperty);
    public static void SetChromeBorder(DependencyObject o, Brush? v) => o.SetValue(ChromeBorderProperty, v);

    public static readonly DependencyProperty ChromeForegroundProperty = Computed<Brush?>("ChromeForeground", null);
    public static Brush? GetChromeForeground(DependencyObject o) => (Brush?)o.GetValue(ChromeForegroundProperty);
    public static void SetChromeForeground(DependencyObject o, Brush? v) => o.SetValue(ChromeForegroundProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty = Computed<Brush?>("HoverBackground", null);
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty HoverBorderProperty = Computed<Brush?>("HoverBorder", null);
    public static Brush? GetHoverBorder(DependencyObject o) => (Brush?)o.GetValue(HoverBorderProperty);
    public static void SetHoverBorder(DependencyObject o, Brush? v) => o.SetValue(HoverBorderProperty, v);

    public static readonly DependencyProperty HoverForegroundProperty = Computed<Brush?>("HoverForeground", null);
    public static Brush? GetHoverForeground(DependencyObject o) => (Brush?)o.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject o, Brush? v) => o.SetValue(HoverForegroundProperty, v);

    public static readonly DependencyProperty PressedBackgroundProperty = Computed<Brush?>("PressedBackground", null);
    public static Brush? GetPressedBackground(DependencyObject o) => (Brush?)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush? v) => o.SetValue(PressedBackgroundProperty, v);

    /// <summary>Solid fill of the tone (checked boxes, progress fill, icon tiles).</summary>
    public static readonly DependencyProperty FillProperty = Computed<Brush?>("Fill", null);
    public static Brush? GetFill(DependencyObject o) => (Brush?)o.GetValue(FillProperty);
    public static void SetFill(DependencyObject o, Brush? v) => o.SetValue(FillProperty, v);

    /// <summary>Content drawn on <see cref="FillProperty"/> (check marks, on-solid text).</summary>
    public static readonly DependencyProperty OnFillProperty = Computed<Brush?>("OnFill", null);
    public static Brush? GetOnFill(DependencyObject o) => (Brush?)o.GetValue(OnFillProperty);
    public static void SetOnFill(DependencyObject o, Brush? v) => o.SetValue(OnFillProperty, v);

    /// <summary>Title text colour (alerts).</summary>
    public static readonly DependencyProperty StrongForegroundProperty = Computed<Brush?>("StrongForeground", null);
    public static Brush? GetStrongForeground(DependencyObject o) => (Brush?)o.GetValue(StrongForegroundProperty);
    public static void SetStrongForeground(DependencyObject o, Brush? v) => o.SetValue(StrongForegroundProperty, v);

    /// <summary>Body text colour (alerts).</summary>
    public static readonly DependencyProperty MutedForegroundProperty = Computed<Brush?>("MutedForeground", null);
    public static Brush? GetMutedForeground(DependencyObject o) => (Brush?)o.GetValue(MutedForegroundProperty);
    public static void SetMutedForeground(DependencyObject o, Brush? v) => o.SetValue(MutedForegroundProperty, v);

    /// <summary>True when hover/press are shown with a darkening overlay (solid/soft fills without their own state colours).</summary>
    public static readonly DependencyProperty HoverOverlayProperty = Computed("HoverOverlay", false);
    public static bool GetHoverOverlay(DependencyObject o) => (bool)o.GetValue(HoverOverlayProperty);
    public static void SetHoverOverlay(DependencyObject o, bool v) => o.SetValue(HoverOverlayProperty, v);
}
