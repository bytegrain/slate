using System.Windows;
using System.Windows.Controls.Primitives;

namespace Slate.Wpf;

/// <summary>
/// Turns Slate options (variant × tone × size × radius, falling back to <see cref="SlateTheme.Defaults"/>) into the
/// computed <see cref="Sl"/> properties templates bind to. Colours are resource <em>references</em>, so theme
/// switches, custom themes and component-token overrides restyle everything live.
/// </summary>
internal static class Styling
{
    private const string Transparent = "Sl.Brush.Transparent";

    /// <summary>Resource keys for one tone.</summary>
    internal sealed record ToneKeys(string Solid, string OnSolid, string Text, string Subtle, string Border, string? SolidHover = null, string? SolidPressed = null);

    internal static ToneKeys Keys(Tone tone) => tone switch
    {
        Tone.Accent => new("Sl.Brush.Accent.Default", "Sl.Brush.Text.OnAccent", "Sl.Brush.Accent.Text", "Sl.Brush.Accent.Subtle",
            "Sl.Brush.Accent.Default", "Sl.Brush.Accent.Hover", "Sl.Brush.Accent.Pressed"),
        Tone.Neutral => new("Sl.Brush.Inverse.Background", "Sl.Brush.Inverse.Text", "Sl.Brush.Text.Primary", "Sl.Brush.Background.Muted", "Sl.Brush.Border.Strong"),
        _ => Status(tone.ToString()),
    };

    private static ToneKeys Status(string name) =>
        new($"Sl.Brush.Status.{name}.Solid", $"Sl.Brush.Status.{name}.OnSolid", $"Sl.Brush.Status.{name}.Fg", $"Sl.Brush.Status.{name}.Bg", $"Sl.Brush.Status.{name}.Border");

    /// <summary>True when the property was set anywhere (local, style, template…), i.e. should not fall back to defaults.</summary>
    internal static bool IsSet(DependencyObject d, DependencyProperty p) =>
        DependencyPropertyHelper.GetValueSource(d, p).BaseValueSource != BaseValueSource.Default;

    internal static T Pick<T>(DependencyObject d, DependencyProperty p, T fallback) => IsSet(d, p) ? (T)d.GetValue(p) : fallback;

    private static SlateDefaults Defaults => SlateTheme.Defaults;

    public static void Refresh(DependencyObject d)
    {
        if (d is not FrameworkElement fe)
            return;

        switch (Sl.GetKind(fe))
        {
            case SlKind.Button: StyleButton(fe); break;
            case SlKind.Selection: StyleSelection(fe); break;
            case SlKind.Progress: StyleProgress(fe); break;
            case SlKind.Spinner: StyleSpinner(fe); break;
            case SlKind.Badge when fe is Badge b: StyleBadge(b); break;
            case SlKind.Alert when fe is Alert a: StyleAlert(a); break;
            case SlKind.Card when fe is Card c: StyleCard(c); break;
            case SlKind.Field when fe is TextField t: StyleField(t); break;
        }
    }

    private static void Ref(FrameworkElement fe, DependencyProperty p, string key) => fe.SetResourceReference(p, key);

    private static void ApplyRadius(FrameworkElement fe, Radius radius, string componentKey)
    {
        if (radius == Slate.Radius.Default)
            Ref(fe, Sl.ActualCornerRadiusProperty, componentKey);
        else
            fe.SetValue(Sl.ActualCornerRadiusProperty, new CornerRadius(radius.Pixels() ?? 0));
    }

    private static string SizeSuffix(ControlSize size) => size switch
    {
        ControlSize.Small => "Sm",
        ControlSize.Large => "Lg",
        _ => "Md",
    };

    private static void Chrome(FrameworkElement fe, string background, string border, string foreground)
    {
        Ref(fe, Sl.ChromeBackgroundProperty, background);
        Ref(fe, Sl.ChromeBorderProperty, border);
        Ref(fe, Sl.ChromeForegroundProperty, foreground);
    }

    private static void Hover(FrameworkElement fe, string background, string border, string foreground)
    {
        Ref(fe, Sl.HoverBackgroundProperty, background);
        Ref(fe, Sl.HoverBorderProperty, border);
        Ref(fe, Sl.HoverForegroundProperty, foreground);
    }

    // ---- Buttons: variant × tone ----

    internal sealed record ButtonPalette(
        string Background, string Border, string Foreground,
        string HoverBackground, string HoverBorder, string HoverForeground,
        string PressedBackground, bool Overlay);

    /// <summary>The brushes for every variant × tone (docs/design/components.md#button, extended to tones).</summary>
    internal static ButtonPalette Palette(ButtonVariant variant, Tone tone)
    {
        var t = Keys(tone);
        var neutral = tone == Tone.Neutral;
        return variant switch
        {
            ButtonVariant.Solid when t.SolidHover is not null =>
                new(t.Solid, Transparent, t.OnSolid, t.SolidHover, Transparent, t.OnSolid, t.SolidPressed!, false),
            ButtonVariant.Solid =>
                new(t.Solid, Transparent, t.OnSolid, t.Solid, Transparent, t.OnSolid, t.Solid, true),
            ButtonVariant.Soft =>
                new(t.Subtle, Transparent, neutral ? "Sl.Brush.Text.Primary" : t.Text,
                    t.Subtle, Transparent, neutral ? "Sl.Brush.Text.Primary" : t.Text, t.Subtle, true),
            ButtonVariant.Ghost => neutral
                ? new(Transparent, Transparent, "Sl.Brush.Text.Secondary", "Sl.Brush.Background.Muted", Transparent, "Sl.Brush.Text.Primary", "Sl.Brush.Background.Pressed", false)
                : new(Transparent, Transparent, t.Text, t.Subtle, Transparent, t.Text, t.Subtle, false),
            ButtonVariant.Link => tone is Tone.Neutral or Tone.Accent
                ? new(Transparent, Transparent, "Sl.Brush.Text.Link", Transparent, Transparent, "Sl.Brush.Text.LinkHover", Transparent, false)
                : new(Transparent, Transparent, t.Text, Transparent, Transparent, t.Text, Transparent, false),
            _ => neutral // Outlined
                ? new("Sl.Brush.Background.Surface", "Sl.Brush.Border.Strong", "Sl.Brush.Text.Primary",
                      "Sl.Brush.Background.Hover", "Sl.Brush.Border.ControlHover", "Sl.Brush.Text.Primary", "Sl.Brush.Background.Pressed", false)
                : new("Sl.Brush.Background.Surface", "Sl.Brush.Border.Strong", t.Text, t.Subtle, t.Border, t.Text, t.Subtle, false),
        };
    }

    private static void StyleButton(FrameworkElement fe)
    {
        var d = Defaults.Button;
        var variant = Pick(fe, Sl.VariantProperty, d.Variant);
        var tone = Pick(fe, Sl.ToneProperty, d.Tone);
        var size = Pick(fe, Sl.SizeProperty, d.Size);

        Sl.SetActualVariant(fe, variant);
        Sl.SetActualTone(fe, tone);
        Sl.SetActualSize(fe, size);
        Ref(fe, Sl.PaddingXProperty, $"Sl.Component.Button.Padding{SizeSuffix(size)}");
        ApplyRadius(fe, Pick(fe, Sl.RadiusProperty, d.Radius), "Sl.Component.Button.Radius.Corner");

        var p = Palette(variant, tone);
        Chrome(fe, p.Background, p.Border, p.Foreground);
        Hover(fe, p.HoverBackground, p.HoverBorder, p.HoverForeground);
        Ref(fe, Sl.PressedBackgroundProperty, p.PressedBackground);
        Sl.SetHoverOverlay(fe, p.Overlay);
    }

    // ---- Selection controls ----

    private static void StyleSelection(FrameworkElement fe)
    {
        var tone = Pick(fe, Sl.ToneProperty, Tone.Accent);
        Sl.SetActualTone(fe, tone);
        Sl.SetActualSize(fe, Pick(fe, Sl.SizeProperty, ControlSize.Medium));
        Sl.SetActualLabelPlacement(fe, Pick(fe, Sl.LabelPlacementProperty, Defaults.Selection.LabelPlacement));
        ApplyRadius(fe, Pick(fe, Sl.RadiusProperty, Slate.Radius.Default), "Sl.Component.Selection.BoxRadius.Corner");

        var t = Keys(tone);
        Ref(fe, Sl.FillProperty, tone == Tone.Accent ? "Sl.Component.Selection.Checked.Brush" : t.Solid);
        Ref(fe, Sl.OnFillProperty, t.OnSolid);
        Ref(fe, Sl.ChromeBorderProperty, "Sl.Component.Selection.Border.Brush");
    }

    // ---- Progress / spinner ----

    private static void StyleProgress(FrameworkElement fe)
    {
        var tone = Pick(fe, Sl.ToneProperty, Tone.Accent);
        Sl.SetActualTone(fe, tone);
        Sl.SetActualSize(fe, Pick(fe, Sl.SizeProperty, ControlSize.Medium));
        Ref(fe, Sl.FillProperty, tone == Tone.Accent ? "Sl.Component.Progress.Fill.Brush" : Keys(tone).Solid);
        Ref(fe, Sl.ChromeBackgroundProperty, "Sl.Component.Progress.Track.Brush");
    }

    private static void StyleSpinner(FrameworkElement fe)
    {
        var tone = Pick(fe, Sl.ToneProperty, Tone.Neutral);
        Sl.SetActualTone(fe, tone);
        Sl.SetActualSize(fe, Pick(fe, Sl.SizeProperty, ControlSize.Medium));
        if (tone == Tone.Neutral)
            fe.ClearValue(Sl.ChromeForegroundProperty); // inherit the surrounding text colour
        else
            Ref(fe, Sl.ChromeForegroundProperty, Keys(tone).Text);
    }

    // ---- Display ----

    private static void StyleBadge(Badge b)
    {
        var tone = Pick(b, Sl.ToneProperty, Tone.Neutral);
        Sl.SetActualTone(b, tone);
        Sl.SetActualSize(b, Pick(b, Sl.SizeProperty, ControlSize.Medium));
        ApplyRadius(b, Pick(b, Sl.RadiusProperty, Slate.Radius.Default), "Sl.Component.Badge.Radius.Corner");

        var t = Keys(tone);
        var neutral = tone == Tone.Neutral;
        switch (b.Variant)
        {
            case BadgeVariant.Solid:
                Chrome(b, t.Solid, t.Solid, t.OnSolid);
                break;
            case BadgeVariant.Outlined:
                Chrome(b, Transparent, neutral ? "Sl.Brush.Border.Strong" : t.Border, neutral ? "Sl.Brush.Text.Secondary" : t.Text);
                break;
            default:
                if (neutral)
                    Chrome(b, "Sl.Brush.Background.Subtle", "Sl.Brush.Border.Default", "Sl.Brush.Text.Secondary");
                else
                    Chrome(b, t.Subtle, t.Border, t.Text);
                break;
        }
    }

    private static void StyleAlert(Alert a)
    {
        var tone = a.Severity.ToTone();
        Sl.SetActualTone(a, tone);
        ApplyRadius(a, Pick(a, Sl.RadiusProperty, Slate.Radius.Default), "Sl.Component.Alert.Radius.Corner");

        var t = Keys(tone);
        var neutral = tone == Tone.Neutral;
        var icon = neutral ? "Sl.Brush.Text.Secondary" : t.Text;
        switch (a.Variant)
        {
            case AlertVariant.Solid:
                Chrome(a, t.Solid, t.Solid, t.OnSolid);
                Ref(a, Sl.StrongForegroundProperty, t.OnSolid);
                Ref(a, Sl.MutedForegroundProperty, t.OnSolid);
                return;
            case AlertVariant.Outlined:
                Chrome(a, "Sl.Brush.Background.Surface", neutral ? "Sl.Brush.Border.Strong" : t.Border, icon);
                break;
            default:
                if (neutral)
                    Chrome(a, "Sl.Brush.Background.Subtle", "Sl.Brush.Border.Default", icon);
                else
                    Chrome(a, t.Subtle, t.Border, icon);
                break;
        }
        Ref(a, Sl.StrongForegroundProperty, "Sl.Brush.Text.Primary");
        Ref(a, Sl.MutedForegroundProperty, "Sl.Brush.Text.Secondary");
    }

    private static void StyleCard(Card c)
    {
        c.SetActualVariant(Pick(c, global::Slate.Wpf.Card.VariantProperty, Defaults.Card.Variant));
        ApplyRadius(c, Pick(c, Sl.RadiusProperty, Defaults.Card.Radius), "Sl.Component.Card.Radius.Corner");
    }

    private static void StyleField(TextField t)
    {
        var d = Defaults.Field;
        var variant = Pick(t, TextField.VariantProperty, d.Variant);
        t.SetActualVariant(variant);
        Sl.SetActualSize(t, Pick(t, Sl.SizeProperty, d.Size));
        Ref(t, Sl.PaddingXProperty, "Sl.Component.Field.PaddingX");
        if (variant == FieldVariant.Underlined)
            t.SetValue(Sl.ActualCornerRadiusProperty, new CornerRadius(0));
        else
            ApplyRadius(t, Pick(t, Sl.RadiusProperty, d.Radius), "Sl.Component.Field.Radius.Corner");

        switch (variant)
        {
            case FieldVariant.Filled:
                Chrome(t, "Sl.Component.Field.FilledBackground.Brush", Transparent, "Sl.Brush.Text.Primary");
                break;
            case FieldVariant.Underlined:
                Chrome(t, Transparent, "Sl.Component.Field.Border.Brush", "Sl.Brush.Text.Primary");
                break;
            default:
                Chrome(t, "Sl.Component.Field.Background.Brush", "Sl.Component.Field.Border.Brush", "Sl.Brush.Text.Primary");
                break;
        }
        Ref(t, Sl.HoverBorderProperty, "Sl.Component.Field.BorderHover.Brush");
    }

    /// <summary>Whether a ToggleButton-like element is "on" (used by Pressed buttons and segmented items).</summary>
    internal static bool IsOn(DependencyObject d) => d is ToggleButton { IsChecked: true } || Sl.GetPressed(d) == true;
}
