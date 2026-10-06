using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slate.Blazor.Internal;
using Slate.Blazor.Services;
using Slate.Snackbars;
using Slate.Theming;

namespace Slate.Blazor;

/// <summary>App-wide Slate settings, configured with <see cref="SlateServiceCollectionExtensions.AddSlate"/>.</summary>
public sealed class SlateOptions
{
    /// <summary>
    /// App-wide component defaults (Slate.Core <see cref="SlateDefaults"/>). A parameter set on a component always
    /// wins; unset parameters read these at render time.
    /// </summary>
    public SlateDefaults Defaults { get; } = new();

    /// <summary>
    /// The app's custom theme (accent, radius scale, fonts, token overrides), applied by every
    /// <c>SlateProvider</c> that doesn't set its own. Null = built-in Alloy.
    /// </summary>
    public SlateThemeOptions? Theme { get; set; }

    /// <summary>Snackbar behaviour. Shorthand for <c>Defaults.Snackbar</c>.</summary>
    public SnackbarConfiguration Snackbars
    {
        get => Defaults.Snackbar;
        set => Defaults.Snackbar = value;
    }

    /// <summary>Clock used by snackbar timers and debounces. Replace with a fake in tests.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

public static class SlateServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISnackbarService"/> and <see cref="IDialogService"/> (scoped: one per user circuit / browser tab)
    /// and the app-wide <see cref="SlateOptions"/>. Then place <c>&lt;SlateProvider&gt;</c> around your app.
    /// <code>services.AddSlate(o => { o.Defaults.Button.Size = ControlSize.Small; o.Theme = new() { Accent = "#5B3DF5" }; });</code>
    /// </summary>
    public static IServiceCollection AddSlate(this IServiceCollection services, Action<SlateOptions>? configure = null)
    {
        var options = new SlateOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddScoped<ISnackbarService, SnackbarService>();
        services.TryAddScoped<IDialogService, DialogService>();
        services.TryAddScoped<SlateJs>();
        services.TryAddScoped<OverlayManager>();
        return services;
    }
}
