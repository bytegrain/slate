namespace Slate.Theming;

/// <summary>
/// What an app may customise in a theme. Everything is optional: an empty options object reproduces
/// the built-in Alloy theme exactly.
/// </summary>
public sealed record SlateThemeOptions
{
    /// <summary>Theme name, e.g. "acme-dark". Defaults to the base theme's name.</summary>
    public string? Name { get; init; }

    /// <summary>Which built-in theme to start from: <see cref="ThemeNames.Light"/> or <see cref="ThemeNames.Dark"/>.</summary>
    public string Base { get; init; } = ThemeNames.Light;

    /// <summary>
    /// Brand colour (#RRGGBB). Slate derives default/hover/pressed/subtle/text/link and the on-accent text colour
    /// from it, adjusting lightness only as far as needed to keep every contrast rule.
    /// </summary>
    public string? Accent { get; init; }

    /// <summary>Multiplies every radius token except <c>radius.full</c>: 0 = square, 1 = Alloy, 1.5 = softer.</summary>
    public double RadiusScale { get; init; } = 1;

    /// <summary>Replaces the UI font family list (comma separated, most preferred first).</summary>
    public string? FontFamily { get; init; }

    /// <summary>Replaces the monospace font family list.</summary>
    public string? MonoFontFamily { get; init; }

    /// <summary>
    /// Raw token overrides applied last, by token path (e.g. <c>["color.background.canvas"] = "#FAFAFA"</c>).
    /// Values use CSS syntax. Unknown paths are rejected so typos fail loudly.
    /// </summary>
    public IReadOnlyDictionary<string, string> Overrides { get; init; } = new Dictionary<string, string>();
}

/// <summary>A fully resolved theme: every themed token plus any customised shared tokens, as CSS values.</summary>
public sealed class SlateThemeDefinition
{
    internal SlateThemeDefinition(string name, string baseTheme, IReadOnlyDictionary<string, string> values, IReadOnlyCollection<string> changed)
    {
        Name = name;
        BaseTheme = baseTheme;
        Values = values;
        ChangedPaths = changed;
    }

    public string Name { get; }

    /// <summary>"light" or "dark" — tells platforms which colour scheme / theme variant to use underneath.</summary>
    public string BaseTheme { get; }

    public bool IsDark => BaseTheme == ThemeNames.Dark;

    /// <summary>Token path → CSS value for every token platforms need to apply this theme.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>Paths whose value differs from the base theme (what a platform must actually override).</summary>
    public IReadOnlyCollection<string> ChangedPaths { get; }

    public SlateColor Color(string path) =>
        Values.TryGetValue(path, out var v) && SlateColor.TryParse(v, out var c)
            ? c
            : throw new KeyNotFoundException($"'{path}' is not a colour token in theme '{Name}'.");

    public double Pixels(string path) =>
        Values.TryGetValue(path, out var v) && v.EndsWith("px", StringComparison.Ordinal)
            ? double.Parse(v[..^2], System.Globalization.CultureInfo.InvariantCulture)
            : throw new KeyNotFoundException($"'{path}' is not a pixel token in theme '{Name}'.");
}

/// <summary>
/// Builds custom themes on top of Alloy. Deterministic and platform-independent, so every Slate package derives
/// the same colours from the same options. Contrast guarantees (tested for arbitrary brand colours):
/// on-accent text ≥ 4.5:1 on default/hover/pressed; the accent fill ≥ 3:1 against every surface (checked
/// controls); accent text and links ≥ 4.5:1 on every reading surface.
/// </summary>
public static class ThemeBuilder
{
    private const double TextContrast = 4.5;
    private const double NonTextContrast = 3.0;

    private static readonly string[] ReadingSurfaces =
        ["color.background.canvas", "color.background.surface", "color.background.sunken", "color.background.raised", "color.background.subtle"];

    private static readonly SlateColor DarkInk = SlateColor.Parse("#05181C");

    public static SlateThemeDefinition Build(SlateThemeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var baseValues = options.Base switch
        {
            ThemeNames.Light => SlateTokens.Themes.Light.Values,
            ThemeNames.Dark => SlateTokens.Themes.Dark.Values,
            _ => throw new ArgumentException($"Base theme must be '{ThemeNames.Light}' or '{ThemeNames.Dark}', not '{options.Base}'.", nameof(options)),
        };
        if (options.RadiusScale is < 0 or > 4 || double.IsNaN(options.RadiusScale))
            throw new ArgumentOutOfRangeException(nameof(options), options.RadiusScale, "RadiusScale must be between 0 and 4.");

        var values = new Dictionary<string, string>(baseValues, StringComparer.Ordinal);
        var dark = options.Base == ThemeNames.Dark;

        if (options.Accent is { } accent)
        {
            if (!SlateColor.TryParse(accent, out var brand) || !brand.IsOpaque)
                throw new ArgumentException($"Accent '{accent}' must be an opaque #RRGGBB colour.", nameof(options));
            foreach (var (path, color) in DeriveAccent(brand, dark, values))
                values[path] = color.ToHex();
        }

        if (options.RadiusScale != 1)
        {
            foreach (var (path, raw) in SlateTokens.Values.Where(kv => kv.Key.StartsWith("radius.", StringComparison.Ordinal) && kv.Key != "radius.full"))
            {
                var px = double.Parse(raw[..^2], System.Globalization.CultureInfo.InvariantCulture);
                values[path] = Px(Math.Round(px * options.RadiusScale, 1));
            }
        }

        if (options.FontFamily is { } ui) values["font.family.ui"] = ui;
        if (options.MonoFontFamily is { } mono) values["font.family.mono"] = mono;

        foreach (var (path, value) in options.Overrides)
        {
            var known = baseValues.ContainsKey(path) || SlateTokens.Values.ContainsKey(path);
            if (!known)
                throw new ArgumentException($"Unknown token path '{path}' in overrides.", nameof(options));
            if (path.StartsWith("color.", StringComparison.Ordinal) && !SlateColor.TryParse(value, out _))
                throw new ArgumentException($"Override '{path}' must be a hex colour, got '{value}'.", nameof(options));
            values[path] = value;
        }

        var changed = values
            .Where(kv => !(baseValues.TryGetValue(kv.Key, out var b) ? b == kv.Value : SlateTokens.Values.TryGetValue(kv.Key, out var s) && s == kv.Value))
            .Select(kv => kv.Key)
            .ToList();

        return new SlateThemeDefinition(options.Name ?? options.Base, options.Base, values, changed);
    }

    /// <summary>The accent token set derived from a brand colour against the given theme's surfaces.</summary>
    internal static IEnumerable<(string Path, SlateColor Color)> DeriveAccent(SlateColor brand, bool dark, IReadOnlyDictionary<string, string> theme)
    {
        var surfaces = ReadingSurfaces.Select(p => SlateColor.Parse(theme[p])).ToArray();

        // 1. Fill + text-on-fill: the lightness closest to the brand that satisfies both rules.
        //    Candidates: white or dark ink text; ties prefer the theme's natural pairing.
        var preferred = dark ? DarkInk : SlateColor.White;
        var other = dark ? SlateColor.White : DarkInk;
        var best = new[] { preferred, other }
            .Select(text => (Text: text, Fill: Nearest(brand, c => c.ContrastWith(text) >= TextContrast && surfaces.All(s => c.ContrastWith(s) >= NonTextContrast))))
            .Where(x => x.Fill is not null)
            .OrderBy(x => Distance(brand, x.Fill!.Value))
            .Select(x => (x.Text, Fill: x.Fill!.Value))
            .FirstOrDefault();

        if (best == default)
        {
            // Unreachable for real colours (a mid-tone always satisfies both); kept as a safe fallback.
            best = (preferred, SlateColor.Parse(theme["color.accent.default"]));
        }

        var (onAccent, fill) = best;
        bool Readable(SlateColor c) => c.ContrastWith(onAccent) >= TextContrast;

        // 2. Hover and pressed: small, visible lightness steps that keep the text readable.
        //    Light themes lighten on hover and darken on press (as Alloy's teal does); dark themes the reverse for press.
        var hover = Step(fill, dark ? [0.06, -0.06] : [0.05, -0.05], Readable);
        var pressed = Step(fill, dark ? [-0.06, 0.06] : [-0.07, 0.07], Readable);

        // 3. Subtle tint: an opaque wash in light themes, translucent in dark ones (as the built-in tokens do).
        var subtle = dark ? fill.WithAlpha(0x1F) : fill.Mix(SlateColor.White, 0.9);

        // 4. Accent-coloured text and links must be readable on every reading surface.
        var text = Nearest(brand, c => surfaces.All(s => c.ContrastWith(s) >= TextContrast)) ?? SlateColor.Parse(theme["color.accent.text"]);
        var linkHover = Step(text, dark ? [0.08, -0.08] : [-0.08, 0.08], c => surfaces.All(s => c.ContrastWith(s) >= TextContrast));

        yield return ("color.accent.default", fill);
        yield return ("color.accent.hover", hover);
        yield return ("color.accent.pressed", pressed);
        yield return ("color.accent.subtle", subtle);
        yield return ("color.accent.text", text);
        yield return ("color.text.onAccent", onAccent);
        yield return ("color.text.link", text);
        yield return ("color.text.linkHover", linkHover);

    }

    /// <summary>The colour with the brand's hue/saturation and the nearest lightness satisfying <paramref name="ok"/>.</summary>
    private static SlateColor? Nearest(SlateColor brand, Func<SlateColor, bool> ok)
    {
        if (ok(brand)) return brand;
        for (var step = 0.005; step <= 1; step += 0.005)
        {
            var down = brand.AdjustLightness(-step);
            if (ok(down)) return down;
            var up = brand.AdjustLightness(step);
            if (ok(up)) return up;
        }
        return null;
    }

    private static SlateColor Step(SlateColor from, double[] deltas, Func<SlateColor, bool> ok)
    {
        foreach (var d in deltas)
        {
            var c = from.AdjustLightness(d);
            if (ok(c) && c != from) return c;
        }
        // Halve the step until something passes; worst case keep the colour (state then relies on shadow/inset).
        foreach (var d in deltas.Select(d => d / 2))
        {
            var c = from.AdjustLightness(d);
            if (ok(c)) return c;
        }
        return from;
    }

    private static double Distance(SlateColor a, SlateColor b) => Math.Abs(a.ToHsl().L - b.ToHsl().L);

    private static string Px(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "px";
}
