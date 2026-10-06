using System.Windows;
using Microsoft.Win32;
using Slate.Theming;

namespace Slate.Wpf;

/// <summary>
/// The Slate resource dictionary. Merge it once in App.xaml:
/// <code>&lt;sl:SlateTheme Mode="System" Density="Compact" /&gt;</code>
/// Changing <see cref="Mode"/> or <see cref="Density"/> swaps the generated token dictionaries in place;
/// everything in Slate uses DynamicResource, so the whole UI updates live. Custom themes
/// (<see cref="Options"/> / <see cref="Apply(SlateThemeDefinition)"/>) are written into an overrides dictionary
/// merged after the tokens, so they follow the same mechanism.
/// </summary>
public class SlateTheme : ResourceDictionary
{
    private const string Assembly = "Slate.Wpf";

    // Fixed positions in MergedDictionaries: generated tokens, density, theme, fonts, custom-theme overrides, control styles.
    private const int DensityIndex = 1;
    private const int ThemeIndex = 2;
    private const int OverridesIndex = 4;

    private ThemeMode _mode = ThemeMode.Light;
    private Density _density = Slate.Density.Compact;
    private bool? _reducedMotion;
    private string? _loadedTheme;
    private Density? _loadedDensity;
    private SlateThemeOptions? _options;
    private SlateThemeDefinition? _fixedDefinition;
    private SlateThemeDefinition? _applied;

    public SlateTheme()
    {
        MergedDictionaries.Add(Load("Themes/Generated/Tokens.xaml"));
        MergedDictionaries.Add(new ResourceDictionary()); // density, filled by Apply
        MergedDictionaries.Add(new ResourceDictionary()); // theme, filled by Apply
        MergedDictionaries.Add(Load("Themes/Fonts.xaml"));
        MergedDictionaries.Add(new ResourceDictionary()); // custom-theme overrides, filled by Apply
        MergedDictionaries.Add(Load("Themes/Controls.xaml"));
        _options = PendingOptions;
        PendingOptions = null;
        Apply(force: _options is not null);

        Current = this;
        SystemTheme.Changed += OnSystemThemeChanged;
    }

    /// <summary>The most recently created SlateTheme (normally the one in App.xaml).</summary>
    public static SlateTheme? Current { get; private set; }

    /// <summary>
    /// App-wide component defaults (docs/design/configurability.md#3-defaults). Options set on a control always win;
    /// unset ones read these when the control loads. <c>services.AddSlate(o => o.Defaults…)</c> configures the same object.
    /// </summary>
    public static SlateDefaults Defaults { get; set; } = new();

    /// <summary>
    /// Custom theme on top of Alloy (accent, radius scale, fonts, token overrides). Rebuilt for light or dark whenever
    /// the actual theme changes, so one brand colour works in both. Null restores the built-in theme.
    /// </summary>
    public SlateThemeOptions? Options
    {
        get => _options;
        set
        {
            _options = value;
            _fixedDefinition = null;
            Apply(force: true);
        }
    }

    /// <summary>The custom theme currently applied, if any.</summary>
    public SlateThemeDefinition? AppliedDefinition => _applied;

    /// <summary>Theme options registered by AddSlate before any SlateTheme existed; consumed by the next one created.</summary>
    internal static SlateThemeOptions? PendingOptions { get; set; }

    /// <summary>Raised after a custom theme is applied or reset.</summary>
    public event EventHandler? ThemeApplied;

    /// <summary>Raised after <see cref="ActualTheme"/> changes (including when the OS theme changes under System).</summary>
    public event EventHandler? ActualThemeChanged;

    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Apply();
        }
    }

    public Density Density
    {
        get => _density;
        set
        {
            if (_density == value) return;
            _density = value;
            Apply();
        }
    }

    /// <summary>Force reduced motion on/off; null (default) follows the OS animation setting.</summary>
    public bool? ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            SlateMotion.Override = value;
        }
    }

    /// <summary>"light" or "dark" — <see cref="Mode"/> with System resolved.</summary>
    public string ActualTheme { get; private set; } = ThemeNames.Light;

    public static Uri PackUri(string path) => new($"pack://application:,,,/{Assembly};component/{path}", UriKind.Absolute);

    internal static string ThemePath(string theme) => $"Themes/Generated/Theme.{(theme == ThemeNames.Dark ? "Dark" : "Light")}.xaml";

    internal static string DensityPath(Density density) => $"Themes/Generated/Density.{density}.xaml";

    private static ResourceDictionary Load(string path) => new() { Source = PackUri(path) };

    /// <summary>Re-evaluates System mode against the current OS setting.</summary>
    public void Refresh() => Apply();

    /// <summary>
    /// Applies a prebuilt theme definition as-is (e.g. from <see cref="ThemeBuilder.Build"/>) and switches
    /// <see cref="Mode"/> to its base theme. Prefer <see cref="Options"/> to follow light/dark automatically.
    /// </summary>
    public void Apply(SlateThemeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _options = null;
        _fixedDefinition = definition;
        _mode = definition.IsDark ? ThemeMode.Dark : ThemeMode.Light;
        Apply(force: true);
    }

    /// <summary>Removes any custom theme and returns to built-in Alloy.</summary>
    public void ResetTheme()
    {
        _options = null;
        _fixedDefinition = null;
        Apply(force: true);
    }

    private void Apply(bool force = false)
    {
        // Replacing a merged entry (rather than mutating it) makes WPF raise one resource
        // invalidation, which every DynamicResource consumer picks up.
        if (_loadedDensity != _density)
        {
            MergedDictionaries[DensityIndex] = Load(DensityPath(_density));
            _loadedDensity = _density;
        }

        var actual = ThemeNames.Resolve(_mode, SystemTheme.IsDark);
        var themeChanged = _loadedTheme != actual;
        if (themeChanged)
        {
            MergedDictionaries[ThemeIndex] = Load(ThemePath(actual));
            _loadedTheme = actual;
            ActualTheme = actual;
        }

        if (themeChanged || force)
        {
            _applied = _fixedDefinition ?? (_options is { } o ? ThemeBuilder.Build(o with { Base = actual }) : null);
            MergedDictionaries[OverridesIndex] = _applied is null ? new ResourceDictionary() : ThemeResources.Build(_applied, _applied.ChangedPaths);
            ThemeApplied?.Invoke(this, EventArgs.Empty);
        }

        if (themeChanged)
            ActualThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (_mode == ThemeMode.System)
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply()));
    }
}

/// <summary>Reads and watches the Windows "app mode" (light/dark) preference.</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static bool _hooked;
    private static EventHandler? _changed;

    /// <summary>True when Windows apps are set to dark mode.</summary>
    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Raised (on a system thread) when the user changes personalisation settings.</summary>
    public static event EventHandler? Changed
    {
        add
        {
            _changed += value;
            if (_hooked) return;
            _hooked = true;
            try { SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category == UserPreferenceCategory.General) _changed?.Invoke(null, EventArgs.Empty); }; }
            catch { /* SystemEvents unavailable (e.g. non-interactive session) */ }
        }
        remove => _changed -= value;
    }
}

/// <summary>Reduced-motion switch used by every Slate animation.</summary>
public static class SlateMotion
{
    /// <summary>Explicit override; null follows <see cref="SystemParameters.ClientAreaAnimation"/>.</summary>
    public static bool? Override { get; set; }

    public static bool IsReduced => Override ?? !SystemParameters.ClientAreaAnimation;
}
