using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Slate.Theming;

namespace Slate.Wpf;

/// <summary>
/// Writes token values (path → CSS value, as produced by <see cref="ThemeBuilder"/>) into a ResourceDictionary using
/// the same keys as the generated dictionaries (docs/design/configurability.md#1-themes):
/// <c>color.x.y</c> → Color <c>Sl.Color.X.Y</c> + brush <c>Sl.Brush.X.Y</c>; component colours → Color + <c>.Brush</c>;
/// px → double, plus <c>Sl.CornerRadius.*</c>/<c>Sl.Thickness.*</c> for radius/space and <c>.Corner</c>/<c>.Thickness</c>
/// for component radii/paddings; shadows → <c>Sl.Effect.*</c> / <c>.Effect</c>; font families and weights.
/// </summary>
public static class ThemeResources
{
    /// <summary>"color.text.onAccent" → "Sl.Color.Text.OnAccent".</summary>
    public static string Key(string path) =>
        "Sl." + string.Join('.', path.Split('.').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));

    private static string Replace(string path, string firstSegment) => Key(firstSegment + path[path.IndexOf('.')..]);

    public static ResourceDictionary Build(SlateThemeDefinition theme, IEnumerable<string> paths)
    {
        var dict = new ResourceDictionary();
        foreach (var path in paths)
        {
            if (theme.Values.TryGetValue(path, out var value))
                Add(dict, path, value);
        }
        return dict;
    }

    /// <summary>Adds the resources for one token. Unknown value shapes are skipped.</summary>
    public static void Add(ResourceDictionary dict, string path, string value)
    {
        var key = Key(path);
        var leaf = path[(path.LastIndexOf('.') + 1)..].ToLowerInvariant();
        var component = path.StartsWith("component.", StringComparison.Ordinal);

        if (path.StartsWith("font.family.", StringComparison.Ordinal))
        {
            dict[key] = new FontFamily(NativeFamilies(value));
            return;
        }

        if (SlateColor.TryParse(value, out var c) && !value.Contains(' '))
        {
            var color = Color.FromArgb(c.A, c.R, c.G, c.B);
            dict[key] = color;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            dict[path.StartsWith("color.", StringComparison.Ordinal) ? Replace(path, "brush") : key + ".Brush"] = brush;
            return;
        }

        if (TryPixels(value, out var px))
        {
            dict[key] = px;
            if (path.StartsWith("radius.", StringComparison.Ordinal))
                dict[Replace(path, "cornerRadius")] = new CornerRadius(px);
            if (path.StartsWith("space.", StringComparison.Ordinal))
                dict[Replace(path, "thickness")] = new Thickness(px);
            if (component && leaf.Contains("radius"))
                dict[key + ".Corner"] = new CornerRadius(px);
            if (component && leaf.Contains("padding"))
                dict[key + ".Thickness"] = new Thickness(px);
            return;
        }

        if (component && leaf.Contains("weight") && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
        {
            dict[key] = FontWeight.FromOpenTypeWeight(Math.Clamp(weight, 1, 999));
            return;
        }

        if ((path.StartsWith("shadow.", StringComparison.Ordinal) || component) && ParseShadow(value) is { } effect)
            dict[component ? key + ".Effect" : Replace(path, "effect")] = effect;
    }

    private static bool TryPixels(string value, out double px)
    {
        px = 0;
        return value.EndsWith("px", StringComparison.Ordinal) && !value.Contains(' ')
            && double.TryParse(value[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out px);
    }

    /// <summary>CSS font list → WPF family list (quotes removed, CSS-only generic families dropped).</summary>
    internal static string NativeFamilies(string css)
    {
        string[] generics = ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "emoji", "math"];
        var names = css.Split(',').Select(f => f.Trim().Trim('"', '\'')).Where(f => f.Length > 0 && !generics.Contains(f, StringComparer.OrdinalIgnoreCase));
        return string.Join(", ", names);
    }

    /// <summary>
    /// CSS box-shadow → a single DropShadowEffect (WPF only has one outer layer): the softest outer layer wins,
    /// as in the generated dictionaries. Null when there is no outer layer with blur.
    /// </summary>
    internal static DropShadowEffect? ParseShadow(string css)
    {
        DropShadowEffect? best = null;
        double bestBlur = 0;
        foreach (var layer in css.Split(','))
        {
            var parts = layer.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] == "inset") continue;
            var numbers = parts.Where(p => p.EndsWith("px", StringComparison.Ordinal)).Select(p => double.Parse(p[..^2], CultureInfo.InvariantCulture)).ToArray();
            var colorText = parts.FirstOrDefault(p => p.StartsWith('#'));
            if (numbers.Length < 3 || colorText is null || !SlateColor.TryParse(colorText, out var c)) continue;
            var blur = numbers[2];
            if (blur <= bestBlur) continue;
            bestBlur = blur;
            best = new DropShadowEffect
            {
                Direction = 270,
                ShadowDepth = Math.Abs(numbers[1]),
                BlurRadius = blur,
                Color = Color.FromRgb(c.R, c.G, c.B),
                Opacity = Math.Round(c.A / 255.0, 3),
                RenderingBias = RenderingBias.Performance,
            };
        }
        best?.Freeze();
        return best;
    }
}
