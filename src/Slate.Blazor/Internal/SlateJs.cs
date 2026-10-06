using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Slate.Blazor.Internal;

/// <summary>Lazily imports wwwroot/slate.js. Only call from OnAfterRender or event handlers (never during prerender).</summary>
public sealed class SlateJs(IJSRuntime js) : IAsyncDisposable, IDisposable
{
    public const string ModulePath = "./_content/Slate.Blazor/slate.js";

    private Task<IJSObjectReference>? _module;

    private Task<IJSObjectReference> Module => _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async ValueTask<IJSObjectReference> TrapFocusAsync(ElementReference panel) =>
        await (await Module).InvokeAsync<IJSObjectReference>("trapFocus", panel);

    public async ValueTask<bool> MediaMatchesAsync(string query) =>
        await (await Module).InvokeAsync<bool>("mediaMatches", query);

    public async ValueTask<IJSObjectReference> WatchMediaAsync<T>(string query, DotNetObjectReference<T> callback) where T : class =>
        await (await Module).InvokeAsync<IJSObjectReference>("watchMedia", query, callback);

    public async ValueTask FocusAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("focusElement", element);

    public async ValueTask PreventEnterAsync(ElementReference element) =>
        await (await Module).InvokeVoidAsync("preventEnter", element);

    public async ValueTask SetIndeterminateAsync(ElementReference input, bool value) =>
        await (await Module).InvokeVoidAsync("setIndeterminate", input, value);

    /// <summary>Synchronous scopes (some hosts and tests) can't await JS; the module is released with the page.</summary>
    public void Dispose() => _module = null;

    public async ValueTask DisposeAsync()
    {
        if (_module is { IsCompletedSuccessfully: true })
        {
            try { await _module.Result.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}

/// <summary>Swallows the exceptions JS interop throws when the circuit/page is going away.</summary>
internal static class JsSafe
{
    public static async ValueTask Run(Func<ValueTask> action)
    {
        try { await action(); }
        catch (JSDisconnectedException) { }
        catch (TaskCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public static async ValueTask DisposeQuietly(IJSObjectReference? reference, string? releaseMethod = null, params object?[] args)
    {
        if (reference is null) return;
        await Run(async () =>
        {
            if (releaseMethod is not null) await reference.InvokeVoidAsync(releaseMethod, args);
            await reference.DisposeAsync();
        });
    }
}
