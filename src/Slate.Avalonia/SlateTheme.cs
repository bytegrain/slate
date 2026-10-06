using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Slate.Avalonia;

/// <summary>
/// The Alloy theme for Avalonia. Add it to <c>Application.Styles</c>:
/// <code>&lt;Application.Styles&gt;&lt;sl:SlateTheme Density="Compact" /&gt;&lt;/Application.Styles&gt;</code>
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

    private ThemeMode _mode = ThemeMode.System;
    private Density _density = Density.Compact;
    private bool _reduceMotion;
    private ResourceDictionary? _densityOverrides;
    private Styles? _reducedMotionStyles;

    public SlateTheme(IServiceProvider? serviceProvider = null)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
        ConfigureFluentPalettes();
        AddThemeDerivedResources();
    }

    /// <summary>Raised after <see cref="Density"/> or <see cref="ReduceMotion"/> change.</summary>
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

    public static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>The SlateTheme in the current application's styles, if any.</summary>
    public static SlateTheme? Current => Application.Current?.Styles.OfType<SlateTheme>().FirstOrDefault();

    private ResourceDictionary Root => (ResourceDictionary)Resources;

    private void ApplyDensity()
    {
        if (_densityOverrides is not null)
            Root.MergedDictionaries.Remove(_densityOverrides);
        _densityOverrides = null;

        // Compact values are already in Tokens.axaml; other densities merge their overrides on top.
        if (_density == Density.Compact)
            return;

        if (!DensityCache.TryGetValue(_density, out var dict))
        {
            dict = new ResourceDictionary();
            dict.MergedDictionaries.Add((ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"{Base}Generated/Density.{_density}.axaml")));
            foreach (var (k, v) in SwitchSizes(_density))
                dict[k] = v;
            DensityCache[_density] = dict;
        }
        _densityOverrides = dict;
        Root.MergedDictionaries.Add(dict);
    }

    /// <summary>Switch geometry per density (docs: 36×20 compact, 44×24 comfortable).</summary>
    internal static IEnumerable<(string Key, object Value)> SwitchSizes(Density d)
    {
        var (w, h, knob) = d == Density.Comfortable ? (44.0, 24.0, 20.0) : (36.0, 20.0, 16.0);
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

        AddVariant(ThemeVariant.Light, SlateTokens.Themes.Light.Color.Background.Canvas, SlateTokens.Themes.Light.Color.Focus.Ring,
            SlateTokens.Themes.Light.Color.Status.Danger.Fg);
        AddVariant(ThemeVariant.Dark, SlateTokens.Themes.Dark.Color.Background.Canvas, SlateTokens.Themes.Dark.Color.Focus.Ring,
            SlateTokens.Themes.Dark.Color.Status.Danger.Fg);

        void AddVariant(ThemeVariant variant, string canvas, string ring, string danger)
        {
            // The focus ring: a 2px gap in the canvas colour, then a 2px ring (docs/design/components.md).
            var dict = new ResourceDictionary
            {
                ["Sl.Shadow.FocusRing"] = BoxShadows.Parse($"0 0 0 2 {canvas}, 0 0 0 4 {ring}"),
                ["Sl.Shadow.FocusRingDanger"] = BoxShadows.Parse($"0 0 0 2 {canvas}, 0 0 0 4 {danger}"),
            };
            Root.ThemeDictionaries[variant] = dict;
        }
    }

    private void ConfigureFluentPalettes()
    {
        if (this.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
            return;

        fluent.Palettes[ThemeVariant.Light] = new ColorPaletteResources
        {
            Accent = Color.Parse(SlateTokens.Themes.Light.Color.Accent.Default),
            RegionColor = Color.Parse(SlateTokens.Themes.Light.Color.Background.Canvas),
            BaseHigh = Color.Parse(SlateTokens.Themes.Light.Color.Text.Primary),
            ErrorText = Color.Parse(SlateTokens.Themes.Light.Color.Status.Danger.Fg),
            ChromeMediumLow = Color.Parse(SlateTokens.Themes.Light.Color.Background.Surface),
        };
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = Color.Parse(SlateTokens.Themes.Dark.Color.Accent.Default),
            RegionColor = Color.Parse(SlateTokens.Themes.Dark.Color.Background.Canvas),
            BaseHigh = Color.Parse(SlateTokens.Themes.Dark.Color.Text.Primary),
            ErrorText = Color.Parse(SlateTokens.Themes.Dark.Color.Status.Danger.Fg),
            ChromeMediumLow = Color.Parse(SlateTokens.Themes.Dark.Color.Background.Surface),
        };
    }
}
