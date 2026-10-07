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

    // ---- Overlays and wave-2 helpers (see wwwroot/slate.js) ----

    public async ValueTask OverlayOpenAsync<THandle, TManager>(string id, DotNetObjectReference<THandle> handle, DotNetObjectReference<TManager> manager,
        ElementReference anchor, ElementReference panel, bool matchWidth) where THandle : class where TManager : class =>
        await (await Module).InvokeVoidAsync("overlayOpen", id, handle, manager, anchor, panel, matchWidth);

    public async ValueTask OverlayRepositionAsync(string id) =>
        await (await Module).InvokeVoidAsync("overlayReposition", id);

    public async ValueTask OverlayCloseAsync(string id) =>
        await (await Module).InvokeVoidAsync("overlayClose", id);

    /// <summary>Sets ARIA attributes on an anchor wrapper's first element (null removes).</summary>
    public async ValueTask SetAnchorAriaAsync(ElementReference wrapper, IDictionary<string, string?> attributes) =>
        await (await Module).InvokeVoidAsync("setAnchorAria", wrapper, attributes);

    public async ValueTask FocusFirstChildAsync(ElementReference wrapper) =>
        await (await Module).InvokeVoidAsync("focusFirstChild", wrapper);

    public async ValueTask FocusSelectorAsync(ElementReference root, string selector) =>
        await (await Module).InvokeVoidAsync("focusSelector", root, selector);

    public async ValueTask FocusIdAsync(string id) =>
        await (await Module).InvokeVoidAsync("focusId", id);

    /// <summary>Prevents default browser behaviour for these keys (names as KeyboardEvent.key; " " is "Space").</summary>
    public async ValueTask PreventKeysAsync(ElementReference element, params string[] keys) =>
        await (await Module).InvokeVoidAsync("preventKeys", element, keys);

    public async ValueTask TabsMeasureAsync(ElementReference root) =>
        await (await Module).InvokeVoidAsync("tabsMeasure", root);

    public async ValueTask TabsScrollAsync(ElementReference root, int direction) =>
        await (await Module).InvokeVoidAsync("tabsScroll", root, direction);

    public async ValueTask ScrollActiveIntoViewAsync(ElementReference root, string selector) =>
        await (await Module).InvokeVoidAsync("scrollActiveIntoView", root, selector);

    public async ValueTask SliderTrackAsync<T>(ElementReference track, DotNetObjectReference<T> callback) where T : class =>
        await (await Module).InvokeVoidAsync("sliderTrack", track, callback);

    public async ValueTask SliderReleaseAsync(ElementReference track) =>
        await (await Module).InvokeVoidAsync("sliderRelease", track);

    // ---- Data grid ----

    public async ValueTask GridInitAsync<T>(ElementReference viewport, DotNetObjectReference<T> callback) where T : class =>
        await (await Module).InvokeVoidAsync("gridInit", viewport, callback);

    public async ValueTask GridSyncAsync(ElementReference viewport, object options) =>
        await (await Module).InvokeVoidAsync("gridSync", viewport, options);

    public async ValueTask GridDisposeAsync(ElementReference viewport) =>
        await (await Module).InvokeVoidAsync("gridDispose", viewport);

    public async ValueTask<bool> CopyTextAsync(string text) =>
        await (await Module).InvokeAsync<bool>("copyText", text);

    public async ValueTask DownloadTextAsync(string fileName, string mimeType, string text) =>
        await (await Module).InvokeVoidAsync("downloadText", fileName, mimeType, text);

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
