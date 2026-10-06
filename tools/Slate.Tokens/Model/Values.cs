using System.Globalization;

namespace Slate.Tokens.Model;

/// <summary>The DTCG token types Slate understands.</summary>
public enum TokenType
{
    Color,
    Dimension,
    FontFamily,
    FontWeight,
    Number,
    Duration,
    CubicBezier,
    Shadow,
    Typography,
}

public static class TokenTypes
{
    public static TokenType Parse(string value, string path) => value switch
    {
        "color" => TokenType.Color,
        "dimension" => TokenType.Dimension,
        "fontFamily" => TokenType.FontFamily,
        "fontWeight" => TokenType.FontWeight,
        "number" => TokenType.Number,
        "duration" => TokenType.Duration,
        "cubicBezier" => TokenType.CubicBezier,
        "shadow" => TokenType.Shadow,
        "typography" => TokenType.Typography,
        _ => throw new TokenException($"Unknown $type '{value}'.", path),
    };
}

/// <summary>An sRGB colour with 8-bit channels.</summary>
public readonly record struct ColorValue(byte R, byte G, byte B, byte A = 255)
{
    public static ColorValue Parse(string text, string path)
    {
        var s = text.Trim();
        if (!s.StartsWith('#'))
            throw new TokenException($"Colour '{text}' must be a hex value (#RGB, #RRGGBB or #RRGGBBAA).", path);

        var hex = s[1..];
        if (hex.Length == 3)
            hex = string.Concat(hex.Select(c => new string(c, 2)));

        if ((hex.Length != 6 && hex.Length != 8) || !hex.All(Uri.IsHexDigit))
            throw new TokenException($"Colour '{text}' is not a valid hex value.", path);

        byte Channel(int i) => byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new ColorValue(Channel(0), Channel(2), Channel(4), hex.Length == 8 ? Channel(6) : (byte)255);
    }

    public bool IsOpaque => A == 255;

    /// <summary>CSS order: #RRGGBB or #RRGGBBAA.</summary>
    public string ToCssHex() => IsOpaque ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>XAML order: #AARRGGBB.</summary>
    public string ToXamlHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    /// <summary>WCAG 2.x relative luminance (alpha ignored).</summary>
    public double RelativeLuminance()
    {
        static double Lin(byte c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
    }

    /// <summary>Composites this (possibly translucent) colour over an opaque backdrop.</summary>
    public ColorValue Over(ColorValue backdrop)
    {
        var a = A / 255.0;
        byte Mix(byte fg, byte bg) => (byte)Math.Round(fg * a + bg * (1 - a));
        return new ColorValue(Mix(R, backdrop.R), Mix(G, backdrop.G), Mix(B, backdrop.B));
    }

    public override string ToString() => ToCssHex();
}

public readonly record struct DimensionValue(double Value, string Unit)
{
    private static readonly string[] Units = ["px", "rem", "em", "%"]; // "rem" before "em": suffix match

    public static DimensionValue Parse(string text, string path)
    {
        var s = text.Trim();
        var unit = Units.FirstOrDefault(u => s.EndsWith(u, StringComparison.Ordinal));
        if (unit is null)
            throw new TokenException($"Dimension '{text}' needs a unit (px, em, rem or %).", path);

        if (!double.TryParse(s[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new TokenException($"Dimension '{text}' is not a number.", path);

        return new DimensionValue(v, unit);
    }

    public bool IsPixels => Unit == "px";

    public override string ToString() => Format.Number(Value) + Unit;
}

public readonly record struct DurationValue(double Milliseconds)
{
    public static DurationValue Parse(string text, string path)
    {
        var s = text.Trim();
        double factor;
        if (s.EndsWith("ms", StringComparison.Ordinal)) { factor = 1; s = s[..^2]; }
        else if (s.EndsWith('s')) { factor = 1000; s = s[..^1]; }
        else throw new TokenException($"Duration '{text}' needs a unit (ms or s).", path);

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < 0)
            throw new TokenException($"Duration '{text}' is not a non-negative number.", path);

        return new DurationValue(v * factor);
    }

    public TimeSpan ToTimeSpan() => TimeSpan.FromMilliseconds(Milliseconds);

    public override string ToString() => Format.Number(Milliseconds) + "ms";
}

public readonly record struct CubicBezierValue(double X1, double Y1, double X2, double Y2)
{
    public override string ToString() =>
        $"cubic-bezier({Format.Number(X1)}, {Format.Number(Y1)}, {Format.Number(X2)}, {Format.Number(Y2)})";
}

public sealed record FontFamilyValue(IReadOnlyList<string> Families)
{
    /// <summary>CSS generic families that only exist on the web.</summary>
    public static readonly IReadOnlySet<string> CssGenerics = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui",
        "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "emoji", "math",
    };

    public IEnumerable<string> NativeFamilies => Families.Where(f => !CssGenerics.Contains(f));

    public string ToCss() => string.Join(", ",
        Families.Select(f => CssGenerics.Contains(f) || !f.Contains(' ') ? f : $"\"{f}\""));

    public override string ToString() => string.Join(", ", Families);
}

public sealed record ShadowLayer(
    ColorValue Color,
    DimensionValue OffsetX,
    DimensionValue OffsetY,
    DimensionValue Blur,
    DimensionValue Spread,
    bool Inset);

public sealed record ShadowValue(IReadOnlyList<ShadowLayer> Layers)
{
    public string ToCss() => string.Join(", ", Layers.Select(l =>
        $"{(l.Inset ? "inset " : "")}{l.OffsetX} {l.OffsetY} {l.Blur} {l.Spread} {l.Color.ToCssHex()}"));
}

/// <summary>A composite text style. Font family keeps the token it came from so platforms can reference it.</summary>
public sealed record TypographyValue(
    FontFamilyValue FontFamily,
    string? FontFamilyRef,
    DimensionValue FontSize,
    double FontWeight,
    double LineHeight,
    DimensionValue LetterSpacing)
{
    /// <summary>Line height in px (font size × multiplier), for platforms that need absolute values.</summary>
    public double LineHeightPixels => Math.Round(FontSize.Value * LineHeight, 2);
}

internal static class Format
{
    public static string Number(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
