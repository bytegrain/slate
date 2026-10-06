using System.Globalization;

namespace Slate.Theming;

/// <summary>
/// An sRGB colour with the maths theming needs: parsing, WCAG contrast, mixing and HSL lightness shifts.
/// Platform-neutral so web, Blazor, WPF and Avalonia derive identical palettes from the same input.
/// </summary>
public readonly record struct SlateColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly SlateColor White = new(255, 255, 255);
    public static readonly SlateColor Black = new(0, 0, 0);

    /// <summary>Parses #RGB, #RRGGBB or #RRGGBBAA (CSS order).</summary>
    public static SlateColor Parse(string hex)
    {
        if (!TryParse(hex, out var c))
            throw new FormatException($"'{hex}' is not a #RGB, #RRGGBB or #RRGGBBAA colour.");
        return c;
    }

    public static bool TryParse(string? hex, out SlateColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(ch => new string(ch, 2)));
        if ((s.Length != 6 && s.Length != 8) || !s.All(Uri.IsHexDigit)) return false;

        byte At(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = new SlateColor(At(0), At(2), At(4), s.Length == 8 ? At(6) : (byte)255);
        return true;
    }

    public bool IsOpaque => A == 255;

    /// <summary>#RRGGBB, or #RRGGBBAA when translucent (CSS order).</summary>
    public string ToHex() => IsOpaque ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <summary>#AARRGGBB (XAML order).</summary>
    public string ToArgbHex() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    public SlateColor WithAlpha(byte alpha) => this with { A = alpha };

    public double RelativeLuminance()
    {
        static double Lin(byte c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
    }

    /// <summary>WCAG 2.x contrast ratio. Translucent foregrounds are composited over <paramref name="background"/> first.</summary>
    public double ContrastWith(SlateColor background)
    {
        var bg = background.IsOpaque ? background : background.Over(White);
        var fg = IsOpaque ? this : Over(bg);
        var (a, b) = (fg.RelativeLuminance(), bg.RelativeLuminance());
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>Composites this colour over an opaque backdrop.</summary>
    public SlateColor Over(SlateColor backdrop)
    {
        var a = A / 255.0;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return new SlateColor(Mix(R, backdrop.R), Mix(G, backdrop.G), Mix(B, backdrop.B));
    }

    /// <summary>Linear RGB mix: amount 0 = this, 1 = other.</summary>
    public SlateColor Mix(SlateColor other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * amount);
        return new SlateColor(M(R, other.R), M(G, other.G), M(B, other.B), M(A, other.A));
    }

    /// <summary>Shifts HSL lightness by <paramref name="delta"/> (−1…1), keeping hue and saturation.</summary>
    public SlateColor AdjustLightness(double delta)
    {
        var (h, s, l) = ToHsl();
        return FromHsl(h, s, Math.Clamp(l + delta, 0, 1), A);
    }

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    public static SlateColor FromHsl(double h, double s, double l, byte alpha = 255)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            static double Hue(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
            }
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        static byte To(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return new SlateColor(To(r), To(g), To(b), alpha);
    }

    public override string ToString() => ToHex();
}
