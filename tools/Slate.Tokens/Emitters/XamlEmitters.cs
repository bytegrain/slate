using System.Security;
using Slate.Tokens.Loading;
using Slate.Tokens.Model;

namespace Slate.Tokens.Emitters;

/// <summary>Shared XAML conventions for WPF and Avalonia so both expose identical resource keys.</summary>
internal static class Xaml
{
    public static string Key(TokenBuild b, string path) => Naming.XamlKey(b.Prefix, path);

    /// <summary>Semantic colours get a Color (Sl.Color.*) and a brush (Sl.Brush.*); palette colours keep their own path.</summary>
    public static string BrushKey(TokenBuild b, string path) =>
        path.StartsWith("color.", StringComparison.Ordinal)
            ? Naming.XamlKey(b.Prefix, path, "brush")
            : Naming.XamlKey(b.Prefix, path) + ".Brush";

    public static string Attr(string value) => SecurityElement.Escape(value)!;

    // "--" is illegal inside XML comments.
    public static string Comment(string text) => $"<!-- {text.Replace("--", "- -")} -->";

    public static string FontWeightName(double w) => w switch
    {
        <= 100 => "Thin", <= 200 => "ExtraLight", <= 300 => "Light", <= 400 => "Normal",
        <= 500 => "Medium", <= 600 => "SemiBold", <= 700 => "Bold", <= 800 => "ExtraBold", _ => "Black",
    };

    public static string FontFamily(FontFamilyValue f) => string.Join(", ", f.NativeFamilies);

    public static bool IsRadius(Token t) => t.Path.StartsWith("radius.", StringComparison.Ordinal);
    public static bool IsSpace(Token t) => t.Path.StartsWith("space.", StringComparison.Ordinal);

    /// <summary>Writes the theme-independent resources that are spelled the same in WPF and Avalonia.</summary>
    public static void WriteShared(Writer w, TokenBuild b, string doubleTag, Action<Token> typography, Action<Token> duration, Action<Token> easing)
    {
        string? section = null;
        foreach (var t in b.Shared)
        {
            var group = t.Segments[0];
            if (group != section)
            {
                w.Line().Line(Comment(group));
                section = group;
            }

            var key = Key(b, t.Path);
            switch (t.Value)
            {
                case ColorValue c:
                    w.Line($"<Color x:Key=\"{key}\">{c.ToXamlHex()}</Color>");
                    break;
                case DimensionValue { IsPixels: true } d:
                    w.Line($"<{doubleTag} x:Key=\"{key}\">{Format.Number(d.Value)}</{doubleTag}>");
                    if (IsRadius(t))
                        w.Line($"<CornerRadius x:Key=\"{Naming.XamlKey(b.Prefix, t.Path, "cornerRadius")}\">{Format.Number(d.Value)}</CornerRadius>");
                    if (IsSpace(t))
                        w.Line($"<Thickness x:Key=\"{Naming.XamlKey(b.Prefix, t.Path, "thickness")}\">{Format.Number(d.Value)}</Thickness>");
                    break;
                case DimensionValue:
                    break; // em/rem/% have no XAML equivalent; typography converts letter-spacing itself.
                case double n when t.Type == TokenType.FontWeight:
                    w.Line($"<FontWeight x:Key=\"{key}\">{FontWeightName(n)}</FontWeight>");
                    break;
                case double n:
                    w.Line($"<{doubleTag} x:Key=\"{key}\">{Format.Number(n)}</{doubleTag}>");
                    break;
                case FontFamilyValue f:
                    w.Line($"<FontFamily x:Key=\"{key}\">{Attr(FontFamily(f))}</FontFamily>");
                    break;
                case DurationValue:
                    duration(t);
                    break;
                case CubicBezierValue:
                    easing(t);
                    break;
                case TypographyValue:
                    typography(t);
                    break;
                default:
                    throw new TokenException($"No XAML mapping for {t.Type}.", t.Path);
            }
        }
    }

    public static void WriteDensity(Writer w, TokenBuild b, string mode, string doubleTag)
    {
        if (b.Config.Density is not { } d)
            return;
        foreach (var (key, token) in b.DensityTokens(mode))
        {
            var value = token.As<DimensionValue>().Value;
            w.Line($"<{doubleTag} x:Key=\"{Key(b, $"{d.Group}.{key}")}\">{Format.Number(value)}</{doubleTag}>");
        }
    }

    public static string TimeSpan(DurationValue d) => d.ToTimeSpan().ToString("c", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// WPF resource dictionaries:
/// Tokens.xaml (shared), Theme.{Name}.xaml (swap to change theme), Density.{Mode}.xaml (swap to change density).
/// Consumers use DynamicResource for anything themed.
/// </summary>
public sealed class WpfXamlEmitter : IEmitter
{
    public string Name => "wpf";

    private const string Root = "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n" +
                                "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                                "                    xmlns:sys=\"clr-namespace:System;assembly=mscorlib\">";

    public IReadOnlyList<GeneratedFile> Emit(TokenBuild b)
    {
        var files = new List<GeneratedFile> { new("Tokens.xaml", Shared(b)) };
        files.AddRange(b.Themes.Select(t => new GeneratedFile($"Theme.{Naming.Pascal(t.Name)}.xaml", Theme(b, t))));
        if (b.Config.Density is { } d)
            files.AddRange(d.Modes.Select(m => new GeneratedFile($"Density.{Naming.Pascal(m)}.xaml", Density(b, m))));
        return files;
    }

    private static Writer Open() => new Writer().Line(Xaml.Comment(EmitterRegistry.Banner)).Line(Root).Indent();

    private static string Close(Writer w) => w.Outdent().Line("</ResourceDictionary>").ToString();

    private static string Shared(TokenBuild b)
    {
        var w = Open();
        Xaml.WriteShared(w, b, "sys:Double",
            typography: t =>
            {
                var v = t.As<TypographyValue>();
                var family = v.FontFamilyRef is { } r ? $"{{DynamicResource {Xaml.Key(b, r)}}}" : Xaml.Attr(Xaml.FontFamily(v.FontFamily));
                w.Line($"<Style x:Key=\"{Xaml.Key(b, t.Path)}\" TargetType=\"TextBlock\">").Indent()
                 .Line($"<Setter Property=\"FontFamily\" Value=\"{family}\" />")
                 .Line($"<Setter Property=\"FontSize\" Value=\"{Format.Number(v.FontSize.Value)}\" />")
                 .Line($"<Setter Property=\"FontWeight\" Value=\"{Xaml.FontWeightName(v.FontWeight)}\" />")
                 .Line($"<Setter Property=\"LineHeight\" Value=\"{Format.Number(v.LineHeightPixels)}\" />")
                 .Line("<Setter Property=\"LineStackingStrategy\" Value=\"BlockLineHeight\" />")
                 .Outdent().Line("</Style>");
            },
            duration: t => w.Line($"<Duration x:Key=\"{Xaml.Key(b, t.Path)}\">{Xaml.TimeSpan(t.As<DurationValue>())}</Duration>"),
            easing: t =>
            {
                var e = t.As<CubicBezierValue>();
                w.Line($"<KeySpline x:Key=\"{Xaml.Key(b, t.Path)}\" ControlPoint1=\"{Format.Number(e.X1)},{Format.Number(e.Y1)}\" ControlPoint2=\"{Format.Number(e.X2)},{Format.Number(e.Y2)}\" />");
            });

        if (b.Config.Density is { } d)
        {
            w.Line().Line(Xaml.Comment($"density: {d.Default} (default; merge Density.*.xaml after this file to change)"));
            Xaml.WriteDensity(w, b, d.Default, "sys:Double");
        }
        return Close(w);
    }

    private static string Theme(TokenBuild b, ThemeTokens theme)
    {
        var w = Open();
        foreach (var t in theme.Tokens)
        {
            switch (t.Value)
            {
                case ColorValue c:
                    w.Line($"<Color x:Key=\"{Xaml.Key(b, t.Path)}\">{c.ToXamlHex()}</Color>");
                    w.Line($"<SolidColorBrush x:Key=\"{Xaml.BrushKey(b, t.Path)}\" Color=\"{c.ToXamlHex()}\" />");
                    break;
                case ShadowValue s:
                    // WPF only has single-layer outer drop shadows: use the softest outer layer.
                    // Inset layers (the milled highlight) are drawn by control templates instead.
                    var layer = s.Layers.Where(l => !l.Inset && l.Blur.Value > 0).MaxBy(l => l.Blur.Value);
                    if (layer is null)
                        break;
                    w.Line($"<DropShadowEffect x:Key=\"{Naming.XamlKey(b.Prefix, t.Path, "effect")}\" Direction=\"270\" " +
                           $"ShadowDepth=\"{Format.Number(Math.Abs(layer.OffsetY.Value))}\" BlurRadius=\"{Format.Number(layer.Blur.Value)}\" " +
                           $"Color=\"{new ColorValue(layer.Color.R, layer.Color.G, layer.Color.B).ToXamlHex()}\" " +
                           $"Opacity=\"{Format.Number(Math.Round(layer.Color.A / 255.0, 3))}\" RenderingBias=\"Performance\" />");
                    break;
                default:
                    throw new TokenException($"No WPF theme mapping for {t.Type}.", t.Path);
            }
        }
        return Close(w);
    }

    private static string Density(TokenBuild b, string mode)
    {
        var w = Open();
        Xaml.WriteDensity(w, b, mode, "sys:Double");
        return Close(w);
    }
}

/// <summary>
/// Avalonia: one Tokens.axaml using ThemeDictionaries (Light/Dark follow RequestedThemeVariant),
/// plus Density.{Mode}.axaml. Shadows map to native multi-layer BoxShadows.
/// </summary>
public sealed class AvaloniaXamlEmitter : IEmitter
{
    public string Name => "avalonia";

    private const string Root = "<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n" +
                                "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                                "                    xmlns:sys=\"using:System\">";

    public IReadOnlyList<GeneratedFile> Emit(TokenBuild b)
    {
        var files = new List<GeneratedFile> { new("Tokens.axaml", Tokens(b)) };
        if (b.Config.Density is { } d)
            files.AddRange(d.Modes.Select(m => new GeneratedFile($"Density.{Naming.Pascal(m)}.axaml", Density(b, m))));
        return files;
    }

    private static Writer Open() => new Writer().Line(Xaml.Comment(EmitterRegistry.Banner)).Line(Root).Indent();

    private static string Close(Writer w) => w.Outdent().Line("</ResourceDictionary>").ToString();

    private static string Tokens(TokenBuild b)
    {
        var w = Open();
        w.Line("<ResourceDictionary.ThemeDictionaries>").Indent();
        foreach (var theme in b.Themes)
        {
            w.Line($"<ResourceDictionary x:Key=\"{Naming.Pascal(theme.Name)}\">").Indent();
            foreach (var t in theme.Tokens)
            {
                switch (t.Value)
                {
                    case ColorValue c:
                        w.Line($"<Color x:Key=\"{Xaml.Key(b, t.Path)}\">{c.ToXamlHex()}</Color>");
                        w.Line($"<SolidColorBrush x:Key=\"{Xaml.BrushKey(b, t.Path)}\" Color=\"{c.ToXamlHex()}\" />");
                        break;
                    case ShadowValue s:
                        w.Line($"<BoxShadows x:Key=\"{Xaml.Key(b, t.Path)}\">{BoxShadows(s)}</BoxShadows>");
                        break;
                    default:
                        throw new TokenException($"No Avalonia theme mapping for {t.Type}.", t.Path);
                }
            }
            w.Outdent().Line("</ResourceDictionary>");
        }
        w.Outdent().Line("</ResourceDictionary.ThemeDictionaries>");

        Xaml.WriteShared(w, b, "x:Double",
            typography: t =>
            {
                var v = t.As<TypographyValue>();
                var family = v.FontFamilyRef is { } r ? $"{{DynamicResource {Xaml.Key(b, r)}}}" : Xaml.Attr(Xaml.FontFamily(v.FontFamily));
                var tracking = v.LetterSpacing.Unit == "em" ? v.LetterSpacing.Value * v.FontSize.Value : v.LetterSpacing.Value;
                w.Line($"<ControlTheme x:Key=\"{Xaml.Key(b, t.Path)}\" TargetType=\"TextBlock\">").Indent()
                 .Line($"<Setter Property=\"FontFamily\" Value=\"{family}\" />")
                 .Line($"<Setter Property=\"FontSize\" Value=\"{Format.Number(v.FontSize.Value)}\" />")
                 .Line($"<Setter Property=\"FontWeight\" Value=\"{Xaml.FontWeightName(v.FontWeight)}\" />")
                 .Line($"<Setter Property=\"LineHeight\" Value=\"{Format.Number(v.LineHeightPixels)}\" />")
                 .Line($"<Setter Property=\"LetterSpacing\" Value=\"{Format.Number(Math.Round(tracking, 3))}\" />")
                 .Outdent().Line("</ControlTheme>");
            },
            duration: t => w.Line($"<sys:TimeSpan x:Key=\"{Xaml.Key(b, t.Path)}\">{Xaml.TimeSpan(t.As<DurationValue>())}</sys:TimeSpan>"),
            easing: t =>
            {
                var e = t.As<CubicBezierValue>();
                w.Line($"<SplineEasing x:Key=\"{Xaml.Key(b, t.Path)}\" X1=\"{Format.Number(e.X1)}\" Y1=\"{Format.Number(e.Y1)}\" X2=\"{Format.Number(e.X2)}\" Y2=\"{Format.Number(e.Y2)}\" />");
            });

        if (b.Config.Density is { } d)
        {
            w.Line().Line(Xaml.Comment($"density: {d.Default} (default; merge Density.*.axaml after this file to change)"));
            Xaml.WriteDensity(w, b, d.Default, "x:Double");
        }
        return Close(w);
    }

    private static string Density(TokenBuild b, string mode)
    {
        var w = Open();
        Xaml.WriteDensity(w, b, mode, "x:Double");
        return Close(w);
    }

    /// <summary>Avalonia syntax: [inset] x y blur spread #AARRGGBB, comma separated.</summary>
    public static string BoxShadows(ShadowValue s) => string.Join(", ", s.Layers.Select(l =>
        $"{(l.Inset ? "inset " : "")}{Format.Number(l.OffsetX.Value)} {Format.Number(l.OffsetY.Value)} " +
        $"{Format.Number(l.Blur.Value)} {Format.Number(l.Spread.Value)} {l.Color.ToXamlHex()}"));
}
