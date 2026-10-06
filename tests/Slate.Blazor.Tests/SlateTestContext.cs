using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Slate.Blazor.Services;
using Slate.Snackbars;

namespace Slate.Blazor.Tests;

/// <summary>bUnit context with Slate services, a fake clock and loose JS interop (module calls return defaults).</summary>
public abstract class SlateTestContext : BunitContext
{
    protected FakeTimeProvider Time { get; } = new();

    protected SlateTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSlate(o =>
        {
            o.TimeProvider = Time;
            o.Snackbars = ConfigureSnackbars(new SnackbarConfiguration());
        });
    }

    protected virtual SnackbarConfiguration ConfigureSnackbars(SnackbarConfiguration c) => c;

    protected ISnackbarService Snackbars => Services.GetRequiredService<ISnackbarService>();
    protected IDialogService Dialogs => Services.GetRequiredService<IDialogService>();

    /// <summary>The JS module the library imports, for setting up and verifying calls.</summary>
    protected BunitJSModuleInterop Module => JSInterop.SetupModule(Internal.SlateJs.ModulePath);
}
