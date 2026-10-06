using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Slate.Avalonia;

/// <summary>
/// Slate options for native controls (docs/design/configurability.md), e.g.
/// <c>&lt;Button sl:Sl.Variant="Solid" sl:Sl.Tone="Accent" sl:Sl.StartIcon="plus" /&gt;</c>.
/// Each option maps to style classes (<c>solid</c>, <c>tone-accent</c>, <c>small</c>, <c>radius-full</c>, …) so
/// plain <c>Classes</c> work too. Options that are not set locally fall back to <see cref="SlateTheme.Defaults"/>.
/// </summary>
public static class Sl
{
    // ---- Button / ToggleButton ----------------------------------------------------------------------------

    public static readonly AttachedProperty<ButtonVariant> VariantProperty =
        AvaloniaProperty.RegisterAttached<Control, ButtonVariant>("Variant", typeof(Sl));

    public static readonly AttachedProperty<Tone> ToneProperty =
        AvaloniaProperty.RegisterAttached<Control, Tone>("Tone", typeof(Sl));

    public static readonly AttachedProperty<ControlSize> SizeProperty =
        AvaloniaProperty.RegisterAttached<Control, ControlSize>("Size", typeof(Sl), ControlSize.Medium);

    public static readonly AttachedProperty<Radius> RadiusProperty =
        AvaloniaProperty.RegisterAttached<Control, Radius>("Radius", typeof(Sl));

    /// <summary>Name of a built-in icon (SlateIcons) shown before the content.</summary>
    public static readonly AttachedProperty<string?> StartIconProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("StartIcon", typeof(Sl));

    /// <summary>Name of a built-in icon shown after the content.</summary>
    public static readonly AttachedProperty<string?> EndIconProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("EndIcon", typeof(Sl));

    /// <summary>Square, icon-only button. Requires <see cref="LabelProperty"/> for its accessible name.</summary>
    public static readonly AttachedProperty<bool> IconOnlyProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IconOnly", typeof(Sl));

    /// <summary>Accessible name (sets AutomationProperties.Name; shown as a tooltip on icon-only buttons).</summary>
    public static readonly AttachedProperty<string?> LabelProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Label", typeof(Sl));

    /// <summary>Shows a spinner, blocks activation and reports busy to assistive tech.</summary>
    public static readonly AttachedProperty<bool> LoadingProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Loading", typeof(Sl));

    public static readonly AttachedProperty<bool> FullWidthProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("FullWidth", typeof(Sl));

    /// <summary>Toggle state for a plain Button (aria-pressed). Null = not a toggle.</summary>
    public static readonly AttachedProperty<bool?> PressedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool?>("Pressed", typeof(Sl));

    /// <summary>Keyboard shortcut hint rendered as a Kbd after the content (e.g. "⌘↵").</summary>
    public static readonly AttachedProperty<string?> ShortcutProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Shortcut", typeof(Sl));

    // ---- Selection controls (CheckBox, RadioButton) and ProgressBar --------------------------------------

    public static readonly AttachedProperty<string?> DescriptionProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Description", typeof(Sl));

    public static readonly AttachedProperty<Placement> LabelPlacementProperty =
        AvaloniaProperty.RegisterAttached<Control, Placement>("LabelPlacement", typeof(Sl), Placement.End);

    // ---- Scoping -------------------------------------------------------------------------------------------

    /// <summary>
    /// Scoped density: set on any element to resize every Slate control inside it
    /// (merges that density's sizes into the element's resources). Inherited, so descendants can read it.
    /// </summary>
    public static readonly AttachedProperty<Density?> DensityProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, Density?>("Density", typeof(Sl), inherits: true);

    // ---- Tone brushes (set by the tone-* classes; templates and variant styles read them) -----------------

    public static readonly AttachedProperty<IBrush?> ToneFillProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneFill", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneFillHoverProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneFillHover", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneFillPressedProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneFillPressed", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneOnProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneOn", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneTextProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneText", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneSubtleProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneSubtle", typeof(Sl));
    public static readonly AttachedProperty<IBrush?> ToneBorderProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("ToneBorder", typeof(Sl));

    internal static readonly string[] VariantClasses = ["outlined", "solid", "soft", "ghost", "link"];
    internal static readonly string[] ToneClasses = ["tone-neutral", "tone-accent", "tone-success", "tone-warning", "tone-danger", "tone-info"];
    internal static readonly string[] RadiusClasses = ["radius-none", "radius-small", "radius-medium", "radius-large", "radius-full"];

    static Sl()
    {
        VariantProperty.Changed.AddClassHandler<Control>((c, _) => Refresh(c));
        ToneProperty.Changed.AddClassHandler<Control>((c, _) => Refresh(c));
        SizeProperty.Changed.AddClassHandler<Control>((c, _) => Refresh(c));
        RadiusProperty.Changed.AddClassHandler<Control>((c, _) => Refresh(c));
        LabelPlacementProperty.Changed.AddClassHandler<Control>((c, _) => Refresh(c));
        StartIconProperty.Changed.AddClassHandler<Control>((c, _) => UpdateIcons(c));
        EndIconProperty.Changed.AddClassHandler<Control>((c, _) => UpdateIcons(c));
        IconOnlyProperty.Changed.AddClassHandler<Control>((c, _) => UpdateIcons(c));
        ContentControl.ContentProperty.Changed.AddClassHandler<ContentControl>((c, _) => UpdateIcons(c));
        LabelProperty.Changed.AddClassHandler<Control>((c, e) => UpdateLabel(c, e.GetNewValue<string?>()));
        LoadingProperty.Changed.AddClassHandler<Control>((c, e) => UpdateLoading(c, e.GetNewValue<bool>()));
        FullWidthProperty.Changed.AddClassHandler<Control>((c, e) => UpdateFullWidth(c, e.GetNewValue<bool>()));
        PressedProperty.Changed.AddClassHandler<Control>((c, e) => UpdatePressed(c, e.GetNewValue<bool?>()));
        ShortcutProperty.Changed.AddClassHandler<Control>((c, e) => c.Classes.Set("has-shortcut", e.GetNewValue<string?>() is { Length: > 0 }));
        DescriptionProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.Classes.Set("has-description", e.GetNewValue<string?>() is { Length: > 0 });
            AutomationProperties.SetHelpText(c, e.GetNewValue<string?>());
        });
        DensityProperty.Changed.AddClassHandler<StyledElement>((e, args) =>
        {
            // Only the element the value was set on gets the resources; descendants just inherit the value.
            if (e.IsSet(DensityProperty))
                SlateTheme.ApplyDensityScope(e, args.GetNewValue<Density?>());
        });

        // Loading buttons swallow Click (class handlers run first), so neither handlers nor Command run twice.
        Button.ClickEvent.AddClassHandler<Button>((b, e) => { if (GetLoading(b)) e.Handled = true; });

        // Apply defaults and computed classes once the control is in a tree (defaults may change at startup).
        Control.LoadedEvent.AddClassHandler<Button>((c, _) => Refresh(c));
        Control.LoadedEvent.AddClassHandler<CheckBox>((c, _) => Refresh(c));
        Control.LoadedEvent.AddClassHandler<RadioButton>((c, _) => Refresh(c));
        Control.LoadedEvent.AddClassHandler<ToggleSwitch>((c, _) => Refresh(c));
        Control.LoadedEvent.AddClassHandler<ProgressBar>((c, _) => Refresh(c));
    }

    public static ButtonVariant GetVariant(Control c) => c.GetValue(VariantProperty);
    public static void SetVariant(Control c, ButtonVariant v) => c.SetValue(VariantProperty, v);
    public static Tone GetTone(Control c) => c.GetValue(ToneProperty);
    public static void SetTone(Control c, Tone v) => c.SetValue(ToneProperty, v);
    public static ControlSize GetSize(Control c) => c.GetValue(SizeProperty);
    public static void SetSize(Control c, ControlSize v) => c.SetValue(SizeProperty, v);
    public static Radius GetRadius(Control c) => c.GetValue(RadiusProperty);
    public static void SetRadius(Control c, Radius v) => c.SetValue(RadiusProperty, v);
    public static string? GetStartIcon(Control c) => c.GetValue(StartIconProperty);
    public static void SetStartIcon(Control c, string? v) => c.SetValue(StartIconProperty, v);
    public static string? GetEndIcon(Control c) => c.GetValue(EndIconProperty);
    public static void SetEndIcon(Control c, string? v) => c.SetValue(EndIconProperty, v);
    public static bool GetIconOnly(Control c) => c.GetValue(IconOnlyProperty);
    public static void SetIconOnly(Control c, bool v) => c.SetValue(IconOnlyProperty, v);
    public static string? GetLabel(Control c) => c.GetValue(LabelProperty);
    public static void SetLabel(Control c, string? v) => c.SetValue(LabelProperty, v);
    public static bool GetLoading(Control c) => c.GetValue(LoadingProperty);
    public static void SetLoading(Control c, bool v) => c.SetValue(LoadingProperty, v);
    public static bool GetFullWidth(Control c) => c.GetValue(FullWidthProperty);
    public static void SetFullWidth(Control c, bool v) => c.SetValue(FullWidthProperty, v);
    public static bool? GetPressed(Control c) => c.GetValue(PressedProperty);
    public static void SetPressed(Control c, bool? v) => c.SetValue(PressedProperty, v);
    public static string? GetShortcut(Control c) => c.GetValue(ShortcutProperty);
    public static void SetShortcut(Control c, string? v) => c.SetValue(ShortcutProperty, v);
    public static string? GetDescription(Control c) => c.GetValue(DescriptionProperty);
    public static void SetDescription(Control c, string? v) => c.SetValue(DescriptionProperty, v);
    public static Placement GetLabelPlacement(Control c) => c.GetValue(LabelPlacementProperty);
    public static void SetLabelPlacement(Control c, Placement v) => c.SetValue(LabelPlacementProperty, v);
    public static Density? GetDensity(StyledElement e) => e.GetValue(DensityProperty);
    public static void SetDensity(StyledElement e, Density? v) => e.SetValue(DensityProperty, v);

    public static IBrush? GetToneFill(Control c) => c.GetValue(ToneFillProperty);
    public static void SetToneFill(Control c, IBrush? v) => c.SetValue(ToneFillProperty, v);
    public static IBrush? GetToneFillHover(Control c) => c.GetValue(ToneFillHoverProperty);
    public static void SetToneFillHover(Control c, IBrush? v) => c.SetValue(ToneFillHoverProperty, v);
    public static IBrush? GetToneFillPressed(Control c) => c.GetValue(ToneFillPressedProperty);
    public static void SetToneFillPressed(Control c, IBrush? v) => c.SetValue(ToneFillPressedProperty, v);
    public static IBrush? GetToneOn(Control c) => c.GetValue(ToneOnProperty);
    public static void SetToneOn(Control c, IBrush? v) => c.SetValue(ToneOnProperty, v);
    public static IBrush? GetToneText(Control c) => c.GetValue(ToneTextProperty);
    public static void SetToneText(Control c, IBrush? v) => c.SetValue(ToneTextProperty, v);
    public static IBrush? GetToneSubtle(Control c) => c.GetValue(ToneSubtleProperty);
    public static void SetToneSubtle(Control c, IBrush? v) => c.SetValue(ToneSubtleProperty, v);
    public static IBrush? GetToneBorder(Control c) => c.GetValue(ToneBorderProperty);
    public static void SetToneBorder(Control c, IBrush? v) => c.SetValue(ToneBorderProperty, v);

    // ---- effective values (local value, else SlateTheme.Defaults) ---------------------------------------

    /// <summary>Push buttons (Button/ToggleButton) — not CheckBox, RadioButton or ToggleSwitch, which derive from Button in Avalonia.</summary>
    internal static bool IsPushButton(Control c) => c is Button && c is not (CheckBox or RadioButton or ToggleSwitch);

    public static ButtonVariant EffectiveVariant(Control c) =>
        c.IsSet(VariantProperty) ? GetVariant(c) : SlateTheme.Defaults.Button.Variant;

    public static Tone EffectiveTone(Control c) =>
        c.IsSet(ToneProperty) ? GetTone(c) : IsPushButton(c) ? SlateTheme.Defaults.Button.Tone : Tone.Accent;

    public static ControlSize EffectiveSize(Control c) =>
        c.IsSet(SizeProperty) ? GetSize(c) : IsPushButton(c) ? SlateTheme.Defaults.Button.Size : ControlSize.Medium;

    public static Radius EffectiveRadius(Control c) =>
        c.IsSet(RadiusProperty) ? GetRadius(c) : IsPushButton(c) ? SlateTheme.Defaults.Button.Radius : Radius.Default;

    public static Placement EffectiveLabelPlacement(Control c) =>
        c.IsSet(LabelPlacementProperty) ? GetLabelPlacement(c) : SlateTheme.Defaults.Selection.LabelPlacement;

    // ---- class mapping ---------------------------------------------------------------------------------

    /// <summary>Style class for a variant.</summary>
    public static string ClassFor(ButtonVariant v) => v.ToString().ToLowerInvariant();

    public static string ClassFor(Tone t) => "tone-" + t.ToString().ToLowerInvariant();

    public static string? ClassFor(Radius r) => r == Radius.Default ? null : "radius-" + r.ToString().ToLowerInvariant();

    /// <summary>Re-evaluates every computed class (variant, tone, size, radius, placement) for a control.</summary>
    public static void Refresh(Control c)
    {
        // A class set directly (Classes="solid tone-accent") wins over the default; the attached property wins over both.
        if (IsPushButton(c) && (c.IsSet(VariantProperty) || !VariantClasses.Any(c.Classes.Contains)))
            SetOne(c, VariantClasses, ClassFor(EffectiveVariant(c)));
        if (c.IsSet(ToneProperty) || !ToneClasses.Any(c.Classes.Contains))
            SetOne(c, ToneClasses, ClassFor(EffectiveTone(c)));

        var size = EffectiveSize(c);
        c.Classes.Set("small", size == ControlSize.Small);
        c.Classes.Set("large", size == ControlSize.Large);

        SetOne(c, RadiusClasses, ClassFor(EffectiveRadius(c)));

        if (c is CheckBox or RadioButton or ToggleSwitch)
            c.Classes.Set("label-start", EffectiveLabelPlacement(c) == Placement.Start);
    }

    internal static void SetOne(StyledElement c, string[] all, string? active)
    {
        foreach (var cls in all)
            c.Classes.Set(cls, cls == active);
    }

    private static void UpdateIcons(Control c)
    {
        c.Classes.Set("has-icon", GetStartIcon(c) is { Length: > 0 });
        c.Classes.Set("has-end-icon", GetEndIcon(c) is { Length: > 0 });
        var noContent = c is not ContentControl cc || cc.Content is null || cc.Content is string { Length: 0 };
        c.Classes.Set("icon-only", GetIconOnly(c) || (GetStartIcon(c) is { Length: > 0 } && noContent && GetEndIcon(c) is null));
    }

    private static void UpdateLabel(Control c, string? label)
    {
        AutomationProperties.SetName(c, label);
        if (GetIconOnly(c) || c.Classes.Contains("icon-only"))
            ToolTip.SetTip(c, label);
    }

    private static void UpdateLoading(Control c, bool loading)
    {
        c.Classes.Set("loading", loading);
        AutomationProperties.SetItemStatus(c, loading ? "Busy" : null);
    }

    private static void UpdateFullWidth(Control c, bool full)
    {
        c.Classes.Set("full-width", full);
        if (full)
            c.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch;
        else
            c.ClearValue(Layoutable.HorizontalAlignmentProperty);
    }

    private static void UpdatePressed(Control c, bool? pressed)
    {
        c.Classes.Set("pressed", pressed == true);
        c.Classes.Set("toggle", pressed is not null);
    }
}
