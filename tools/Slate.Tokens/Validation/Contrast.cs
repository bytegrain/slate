using Slate.Tokens.Model;

namespace Slate.Tokens.Validation;

/// <summary>WCAG 2.x contrast. Translucent foregrounds are composited over the background first.</summary>
public static class Contrast
{
    /// <summary>Normal text (WCAG 1.4.3 AA).</summary>
    public const double Text = 4.5;

    /// <summary>Large text and non-text UI: icons, control boundaries, focus indicators (WCAG 1.4.11).</summary>
    public const double NonText = 3.0;

    public static double Ratio(ColorValue foreground, ColorValue background)
    {
        if (!background.IsOpaque)
            throw new ArgumentException("Background must be opaque; composite it over its own backdrop first.", nameof(background));

        var fg = foreground.IsOpaque ? foreground : foreground.Over(background);
        var a = fg.RelativeLuminance();
        var b = background.RelativeLuminance();
        var (hi, lo) = a > b ? (a, b) : (b, a);
        return (hi + 0.05) / (lo + 0.05);
    }
}
