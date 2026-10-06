using Slate.Theming;

namespace Slate.Blazor.Demo;

/// <summary>Per-circuit demo state: theme mode, density and the custom theme edited on the Theming page.</summary>
public sealed class DemoTheme
{
    public ThemeMode Mode { get; set; } = ThemeMode.System;
    public Density Density { get; set; } = Density.Compact;

    /// <summary>Null = built-in Alloy.</summary>
    public SlateThemeOptions? Options { get; private set; }

    public event Action? Changed;

    public void Apply(SlateThemeOptions? options)
    {
        Options = options;
        Changed?.Invoke();
    }

    public void Notify() => Changed?.Invoke();
}
