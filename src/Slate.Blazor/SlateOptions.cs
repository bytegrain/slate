using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slate.Blazor.Internal;
using Slate.Blazor.Services;
using Slate.Snackbars;

namespace Slate.Blazor;

/// <summary>App-wide Slate settings, configured with <see cref="SlateServiceCollectionExtensions.AddSlate"/>.</summary>
public sealed class SlateOptions
{
    /// <summary>Snackbar behaviour (position, limits, durations). Defaults come from the design tokens.</summary>
    public SnackbarConfiguration Snackbars { get; set; } = new();

    /// <summary>Clock used by snackbar timers. Replace with a fake in tests.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

public static class SlateServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISnackbarService"/> and <see cref="IDialogService"/> (scoped: one per user circuit / browser tab).
    /// Then place <c>&lt;SlateProvider&gt;</c> around your app (it hosts the snackbar and dialog layers).
    /// </summary>
    public static IServiceCollection AddSlate(this IServiceCollection services, Action<SlateOptions>? configure = null)
    {
        var options = new SlateOptions();
        configure?.Invoke(options);
        services.TryAddSingleton(options);
        services.TryAddScoped<ISnackbarService, SnackbarService>();
        services.TryAddScoped<IDialogService, DialogService>();
        services.TryAddScoped<SlateJs>();
        return services;
    }
}
