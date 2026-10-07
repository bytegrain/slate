using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Slate.Theming;

namespace Slate.Avalonia;

/// <summary>
/// The Alloy theme for Avalonia. Add it to <c>Application.Styles</c>:
/// <code>&lt;Application.Styles&gt;&lt;sl:SlateTheme Density="Compact" /&gt;&lt;/Application.Styles&gt;</code>
/// Configure it at four levels (docs/design/configurability.md): a custom theme (<see cref="Options"/> /
/// <see cref="Apply"/>), component tokens (override any <c>Sl.Component.*</c> resource in any scope), app-wide
/// <see cref="Defaults"/>, and per-instance properties.
/// </summary>
/// <remarks>
/// SlateTheme layers on FluentTheme: Fluent supplies templates for the controls Slate does not restyle yet
/// (menus, ComboBox, DataGrid…), re-coloured from the Slate tokens so they blend in. Every control Slate
/// specifies (docs/design/components.md) gets a Slate ControlTheme or style that takes precedence, because a
/// Styles' own resources are searched before its children's.
/// </remarks>
public partial class SlateTheme : Styles
{
    private const string Base = "avares://Slate.Avalonia/Themes/";
    private static readonly Dictionary<Density, ResourceDictionary> DensityCache = new();
    private static readonly string[] CssGenericFamilies =
        ["serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded", "emoji", "math"];

    private ThemeMode _mode = ThemeMode.System;
    private Density _density = Density.Compact;
    private bool _reduceMotion;
    private ResourceDictionary? _densityOverrides;
    private Styles? _reducedMotionStyles;
    private SlateThemeOptions? _options;
    private readonly Dictionary<ThemeVariant, HashSet<string>> _appliedKeys = new();
    private readonly Dictionary<ThemeVariant, SlateThemeDefinition> _applied = new();

    public SlateTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
        ConfigureFluentPalettes();
        AddThemeDerivedResources();
        if (PendingOptions is { } pending)
            Options = pending;
    }

    /// <summary>
    /// App-wide component defaults (button variant/tone/size, field variant, card variant, label placement, dialog and
    /// snackbar options). Read whenever a control is loaded and has no local value, so set them at startup.
    /// </summary>
    public static SlateDefaults Defaults { get; set; } = new();

    /// <summary>Theme options registered through <c>AddSlate</c> before the theme was created.</summary>
    internal static SlateThemeOptions? PendingOptions { get; set; }

    /// <summary>Raised after <see cref="Density"/>, <see cref="ReduceMotion"/> or the custom theme change.</summary>
    public event EventHandler? Changed;

    /// <summary>Light, Dark, or System (follow the OS). Applied to <see cref="Application.RequestedThemeVariant"/>.</summary>
    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (Application.Current is { } app)
                app.RequestedThemeVariant = ToVariant(value);
        }
    }

    /// <summary>Compact (32px controls, default) or Comfortable (40px).</summary>
    public Density Density
    {
        get => _density;
        set
        {
            if (_density == value && (_densityOverrides is not null || value == Density.Compact))
                return;
            _density = value;
            ApplyDensity();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes transitions and looping animations (spinners, indeterminate progress).</summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (_reduceMotion == value)
                return;
            _reduceMotion = value;
            _reducedMotionStyles ??= (Styles)AvaloniaXamlLoader.Load(new Uri(Base + "ReducedMotion.axaml"));
            if (value)
                Add(_reducedMotionStyles);
            else
                Remove(_reducedMotionStyles);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// A custom theme built with <see cref="ThemeBuilder"/> (brand accent, radius scale, fonts, token overrides).
    /// Setting it applies the theme live and switches <see cref="Mode"/> to its base; null resets to Alloy.
    /// </summary>
    public SlateThemeOptions? Options
    {
        get => _options;
        set
        {
            _options = value;
            if (value is null)
                ResetTheme();
            else
                Apply(ThemeBuilder.Build(value));
        }
    }

    /// <summary>The custom theme currently applied to a variant, if any.</summary>
    public SlateThemeDefinition? AppliedTheme(ThemeVariant variant) => _applied.GetValueOrDefault(Normalize(variant));

    public static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>The SlateTheme in the current application's styles, if any.</summary>
    public static SlateTheme? Current => Application.Current?.Styles.OfType<SlateTheme>().FirstOrDefault();

    private ResourceDictionary Root => (ResourceDictionary)Resources;

    // ---- custom themes ---------------------------------------------------------------------------------

    /// <summary>
    /// Applies a custom theme to its base variant (light or dark) for the whole app. Only the tokens it changes
    /// are written; every Slate control picks them up immediately (DynamicResource).
    /// </summary>
    /// <param name="activate">Also switch <see cref="Mode"/> to the theme's base so it becomes visible.</param>
    public void Apply(SlateThemeDefinition theme, bool activate = true)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var variant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var dict = VariantDictionary(variant);

        if (_appliedKeys.TryGetValue(variant, out var previous))
            foreach (var key in previous)
                dict.Remove(key);

        var written = new HashSet<string>(StringComparer.Ordinal);
        WriteTokens(dict, theme.Values, theme.ChangedPaths, written);
        WriteDerived(dict, theme.Values);
        _appliedKeys[variant] = written;
        _applied[variant] = theme;
        UpdateFluentAccent(variant, theme.Values);

        if (activate)
            Mode = theme.IsDark ? ThemeMode.Dark : ThemeMode.Light;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes every custom theme, restoring the built-in Alloy light and dark themes.</summary>
    public void ResetTheme()
    {
        foreach (var (variant, keys) in _appliedKeys)
        {
            var dict = VariantDictionary(variant);
            foreach (var key in keys)
                dict.Remove(key);
            WriteDerived(dict, BaseValues(variant));
            UpdateFluentAccent(variant, BaseValues(variant));
        }
        _appliedKeys.Clear();
        _applied.Clear();
        _options = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Scopes a custom theme to one element and its descendants (e.g. a branded panel). The element's resources get
    /// the theme's tokens for its base variant; wrap the subtree in a <see cref="ThemeVariantScope"/> to force that
    /// variant there.
    /// </summary>
    public static void ApplyTo(StyledElement scope, SlateThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(theme);
        var variant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        var dict = new ResourceDictionary();
        WriteTokens(dict, theme.Values, theme.ChangedPaths, null);
        WriteDerived(dict, theme.Values);
        scope.Resources.ThemeDictionaries[variant] = dict;
        if (scope is ThemeVariantScope tvs)
            tvs.RequestedThemeVariant = variant;
    }

    /// <summary>Removes a theme applied with <see cref="ApplyTo"/>.</summary>
    public static void ClearFrom(StyledElement scope)
    {
        scope.Resources.ThemeDictionaries.Remove(ThemeVariant.Light);
        scope.Resources.ThemeDictionaries.Remove(ThemeVariant.Dark);
    }

    private ResourceDictionary VariantDictionary(ThemeVariant variant)
    {
        variant = Normalize(variant);
        if (Root.ThemeDictionaries.TryGetValue(variant, out var provider) && provider is ResourceDictionary rd)
            return rd;
        var dict = new ResourceDictionary();
        Root.ThemeDictionaries[variant] = dict;
        return dict;
    }

    private static ThemeVariant Normalize(ThemeVariant v) => v == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;

    private static IReadOnlyDictionary<string, string> BaseValues(ThemeVariant v) =>
        Normalize(v) == ThemeVariant.Dark ? SlateTokens.Themes.Dark.Values : SlateTokens.Themes.Light.Values;

    /// <summary>XAML key for a token path (same rule as the generator): color.text.primary → Sl.Color.Text.Primary.</summary>
    public static string KeyFor(string path) =>
        "Sl." + string.Join('.', path.Split('.').Select(s => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..]));

    /// <summary>Writes token values as Avalonia resources using the generator's naming rules.</summary>
    internal static void WriteTokens(IResourceDictionary dict, IReadOnlyDictionary<string, string> values, IEnumerable<string> paths, ISet<string>? written)
    {
        void Put(string key, object value)
        {
            dict[key] = value;
            written?.Add(key);
        }

        foreach (var path in paths)
        {
            if (!values.TryGetValue(path, out var raw))
                continue;
            var key = KeyFor(path);
            var leaf = path[(path.LastIndexOf('.') + 1)..].ToLowerInvariant();

            if (path.StartsWith("font.family.", StringComparison.Ordinal))
            {
                Put(key, ToFontFamily(raw));
            }
            else if (SlateColor.TryParse(raw, out var c) && raw.TrimStart().StartsWith('#'))
            {
                var color = Color.FromArgb(c.A, c.R, c.G, c.B);
                Put(key, color);
                var brushKey = path.StartsWith("color.", StringComparison.Ordinal) ? "Sl.Brush." + key["Sl.Color.".Length..] : key + ".Brush";
                Put(brushKey, new SolidColorBrush(color));
            }
            else if (raw.Contains("px ", StringComparison.Ordinal) || raw == "none")
            {
                Put(key, ToBoxShadows(raw));
            }
            else if (raw.EndsWith("px", StringComparison.Ordinal) && double.TryParse(raw[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            {
                Put(key, px);
                if (path.StartsWith("radius.", StringComparison.Ordinal))
                    Put("Sl.CornerRadius." + key["Sl.Radius.".Length..], new CornerRadius(px));
                if (path.StartsWith("component.", StringComparison.Ordinal) && leaf.Contains("radius"))
                    Put(key + ".Corner", new CornerRadius(px));
                if (path.StartsWith("component.", StringComparison.Ordinal) && leaf.Contains("padding"))
                    Put(key + ".Thickness", new Thickness(px));
            }
            else if (leaf.Contains("fontweight") && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
            {
                Put(key, (FontWeight)(int)w);
            }
            else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                Put(key, number);
            }
        }
    }

    /// <summary>Resources Slate computes from tokens (inline paddings, switch geometry, focus ring shadows).</summary>
    internal static void WriteDerived(IResourceDictionary dict, IReadOnlyDictionary<string, string> values)
    {
        double Px(string path, double fallback) =>
            values.TryGetValue(path, out var v) && v.EndsWith("px", StringComparison.Ordinal)
                && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

        dict["Sl.Component.Button.PaddingSm.Inline"] = new Thickness(Px("component.button.paddingSm", 8), 0);
        dict["Sl.Component.Button.PaddingMd.Inline"] = new Thickness(Px("component.button.paddingMd", 12), 0);
        dict["Sl.Component.Button.PaddingLg.Inline"] = new Thickness(Px("component.button.paddingLg", 16), 0);
        dict["Sl.Component.Field.PaddingX.Inline"] = new Thickness(Px("component.field.paddingX", 10), 0);

        if (values.TryGetValue("color.background.canvas", out var canvas) && values.TryGetValue("color.focus.ring", out var ring)
            && values.TryGetValue("color.status.danger.fg", out var danger))
        {
            dict["Sl.Shadow.FocusRing"] = BoxShadows.Parse($"0 0 0 2 {Argb(canvas)}, 0 0 0 4 {Argb(ring)}");
            dict["Sl.Shadow.FocusRingDanger"] = BoxShadows.Parse($"0 0 0 2 {Argb(canvas)}, 0 0 0 4 {Argb(danger)}");
        }
    }

    private static string Argb(string css) => SlateColor.Parse(css).ToArgbHex();

    /// <summary>Converts CSS box-shadow syntax (from token values) to Avalonia BoxShadows.</summary>
    internal static BoxShadows ToBoxShadows(string css)
    {
        if (css.Trim() == "none")
            return default;
        var layers = css.Split(", ").Select(layer =>
        {
            var parts = layer.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var inset = parts[0] == "inset";
            var nums = parts.Skip(inset ? 1 : 0).Where(p => p.EndsWith("px", StringComparison.Ordinal)).Select(p => p[..^2]);
            var color = Argb(parts[^1]);
            return $"{(inset ? "inset " : "")}{string.Join(' ', nums)} {color}";
        });
        return BoxShadows.Parse(string.Join(", ", layers));
    }

    private static FontFamily ToFontFamily(string css)
    {
        var families = css.Split(',').Select(f => f.Trim().Trim('"', '\'')).Where(f => f.Length > 0 && !CssGenericFamilies.Contains(f, StringComparer.OrdinalIgnoreCase));
        var list = string.Join(", ", families);
        return new FontFamily(list.Length == 0 ? "Inter" : list);
    }

    // ---- density -------------------------------------------------------------------------------------

    private void ApplyDensity()
    {
        if (_densityOverrides is not null)
            Root.MergedDictionaries.Remove(_densityOverrides);
        _densityOverrides = null;

        // Compact values are already in Tokens.axaml; other densities merge their overrides on top.
        if (_density == Density.Compact)
            return;

        _densityOverrides = DensityDictionary(_density);
        Root.MergedDictionaries.Add(_densityOverrides);
    }

    private static ResourceDictionary DensityDictionary(Density density)
    {
        if (DensityCache.TryGetValue(density, out var dict))
            return dict;
        dict = new ResourceDictionary();
        dict.MergedDictionaries.Add((ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"{Base}Generated/Density.{density}.axaml")));
        foreach (var (k, v) in SwitchSizes(density))
            dict[k] = v;
        DensityCache[density] = dict;
        return dict;
    }

    /// <summary>
    /// Scoped density (<c>sl:Sl.Density</c>): merges that density's control sizes into the element's resources,
    /// so every Slate control inside it resizes. Null removes the scope.
    /// </summary>
    internal static void ApplyDensityScope(StyledElement element, Density? density)
    {
        foreach (var d in element.Resources.MergedDictionaries.Where(m => m is ResourceDictionary rd && rd.ContainsKey(DensityScopeMarker)).ToList())
            element.Resources.MergedDictionaries.Remove(d);
        if (density is { } value)
        {
            // A dictionary has one owner, so each scope gets its own flat copy of the (cached) density values.
            var scope = new ResourceDictionary { [DensityScopeMarker] = value };
            static void Copy(ResourceDictionary from, ResourceDictionary to)
            {
                foreach (var merged in from.MergedDictionaries.OfType<ResourceDictionary>())
                    Copy(merged, to);
                foreach (var (k, v) in from)
                    to[k] = v;
            }
            Copy(DensityDictionary(value), scope);
            element.Resources.MergedDictionaries.Add(scope);
        }
    }

    private const string DensityScopeMarker = "Sl.DensityScope";

    /// <summary>Switch geometry per density, from the component tokens (compact) — comfortable is 8/4/4px larger.</summary>
    internal static IEnumerable<(string Key, object Value)> SwitchSizes(Density d)
    {
        var tokens = SlateTokens.Themes.Light.Values;
        double Px(string path, double fallback) =>
            tokens.TryGetValue(path, out var v) && double.TryParse(v.TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : fallback;
        var (w, h, knob) = (Px("component.selection.switchWidth", 36), Px("component.selection.switchHeight", 20), Px("component.selection.thumbSize", 16));
        if (d == Density.Comfortable)
            (w, h, knob) = (w + 8, h + 4, knob + 4);
        yield return ("Sl.Switch.TrackWidth", w);
        yield return ("Sl.Switch.TrackHeight", h);
        yield return ("Sl.Switch.KnobSize", knob);
        yield return ("Sl.Switch.KnobSlot", h);
        yield return ("Sl.Switch.KnobTravel", w - h);
    }

    private void AddThemeDerivedResources()
    {
        // Base (compact) sizes live in a merged dictionary so density overrides merged later can win.
        var sizes = new ResourceDictionary();
        foreach (var (k, v) in SwitchSizes(Density.Compact))
            sizes[k] = v;
        Root.MergedDictionaries.Add(sizes);

        WriteDerived(VariantDictionary(ThemeVariant.Light), SlateTokens.Themes.Light.Values);
        WriteDerived(VariantDictionary(ThemeVariant.Dark), SlateTokens.Themes.Dark.Values);
    }

    // ---- Fluent ------------------------------------------------------------------------------------------

    private void ConfigureFluentPalettes()
    {
        if (this.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
            return;
        fluent.Palettes[ThemeVariant.Light] = Palette(SlateTokens.Themes.Light.Values);
        fluent.Palettes[ThemeVariant.Dark] = Palette(SlateTokens.Themes.Dark.Values);
    }

    private void UpdateFluentAccent(ThemeVariant variant, IReadOnlyDictionary<string, string> values)
    {
        if (this.OfType<FluentTheme>().FirstOrDefault() is { } fluent)
            fluent.Palettes[Normalize(variant)] = Palette(values);
    }

    private static ColorPaletteResources Palette(IReadOnlyDictionary<string, string> v)
    {
        Color C(string path) => Color.Parse(SlateColor.Parse(v[path]).ToArgbHex());
        return new ColorPaletteResources
        {
            Accent = C("color.accent.default"),
            RegionColor = C("color.background.canvas"),
            BaseHigh = C("color.text.primary"),
            ErrorText = C("color.status.danger.fg"),
            ChromeMediumLow = C("color.background.surface"),
        };
    }
}
