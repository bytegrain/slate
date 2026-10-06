using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Slate.Overlays;

namespace Slate.Blazor.Internal;

/// <summary>Why an overlay is being dismissed by the shared rules (docs/design/css-classes.md#overlays).</summary>
public enum OverlayDismissReason
{
    /// <summary>Escape pressed while it was the topmost overlay.</summary>
    Escape,
    /// <summary>Pointer pressed outside it (and outside every overlay above it).</summary>
    Outside,
}

/// <summary>
/// The stack of open overlays for one user (scoped). Escape dismisses only the topmost overlay; a pointer-down
/// outside dismisses from the top down until it reaches an overlay that contains the target, or one that keeps
/// itself open on outside clicks. The browser side (slate.js) reports raw events; the rules live here so they
/// are the same in every render mode and testable without a browser.
/// </summary>
public sealed class OverlayManager
{
    private readonly List<OverlayHandle> _stack = [];
    private readonly Lock _gate = new();

    /// <summary>Open overlays, bottom to top.</summary>
    public IReadOnlyList<OverlayHandle> Open
    {
        get { lock (_gate) return _stack.ToList(); }
    }

    public OverlayHandle? Top
    {
        get { lock (_gate) return _stack.Count > 0 ? _stack[^1] : null; }
    }

    /// <summary>When a tooltip last hid (tooltip "warm-up": the next one shows without delay).</summary>
    public DateTimeOffset LastTooltipHidden { get; set; } = DateTimeOffset.MinValue;

    internal void Push(OverlayHandle handle)
    {
        lock (_gate)
        {
            _stack.Remove(handle);
            _stack.Add(handle);
        }
    }

    internal void Remove(OverlayHandle handle)
    {
        lock (_gate) _stack.Remove(handle);
    }

    /// <summary>Escape: dismisses the topmost overlay. Returns false when none is open.</summary>
    [JSInvokable]
    public async Task<bool> OnEscape()
    {
        var top = Top;
        if (top is null) return false;
        await top.DismissAsync(OverlayDismissReason.Escape);
        return true;
    }

    /// <summary>A pointer went down; <paramref name="containing"/> are the ids of the overlays whose anchor/panel contain the target.</summary>
    [JSInvokable]
    public async Task OnPointerDown(string[] containing)
    {
        var inside = new HashSet<string>(containing ?? [], StringComparer.Ordinal);
        foreach (var overlay in Open.AsEnumerable().Reverse())
        {
            if (inside.Contains(overlay.Id) || !overlay.DismissOnOutside()) break;
            await overlay.DismissAsync(OverlayDismissReason.Outside);
        }
    }
}

/// <summary>Where slate.js should put a panel (computed by Slate.Core's <see cref="PopoverPositioner"/>).</summary>
public sealed record OverlayPlacement(double Left, double Top, double? MaxHeight, string Side, string Placement, double Arrow);

/// <summary>A rectangle reported by the browser.</summary>
public sealed record BrowserRect(double X, double Y, double Width, double Height)
{
    internal OverlayRect ToOverlay() => new(X, Y, Width, Height);
}

/// <summary>
/// One floating surface (select listbox, menu, popover, tooltip, calendar). Owns its place in the
/// <see cref="OverlayManager"/> stack, asks slate.js to show/position/hide the panel, and answers the browser's
/// positioning requests with Slate.Core's positioning rules.
/// </summary>
public sealed class OverlayHandle : IAsyncDisposable
{
    private static int _ids;
    private readonly OverlayManager _manager;
    private readonly SlateJs _js;
    private readonly Func<OverlayDismissReason, Task> _onDismiss;
    private DotNetObjectReference<OverlayHandle>? _self;
    private DotNetObjectReference<OverlayManager>? _managerRef;

    public OverlayHandle(OverlayManager manager, SlateJs js, Func<OverlayDismissReason, Task> onDismiss)
    {
        _manager = manager;
        _js = js;
        _onDismiss = onDismiss;
        Id = $"slo{Interlocked.Increment(ref _ids)}";
    }

    public string Id { get; }

    public bool IsOpen { get; private set; }

    public PopoverPlacement Placement { get; set; } = PopoverPlacement.Bottom;
    public double Offset { get; set; } = 6;
    public bool MatchAnchorWidth { get; set; }
    public Func<bool> DismissOnOutside { get; set; } = () => true;

    /// <summary>Opens at a viewport point instead of the anchor (context menus).</summary>
    public (double X, double Y)? Point { get; set; }

    /// <summary>Registers as open (top of the stack). Call before rendering the panel visible.</summary>
    public void MarkOpen()
    {
        IsOpen = true;
        _manager.Push(this);
    }

    /// <summary>Removes from the stack. Call when the owner closes.</summary>
    public void MarkClosed()
    {
        IsOpen = false;
        Point = null;
        _manager.Remove(this);
    }

    /// <summary>Show and position the rendered panel (from OnAfterRenderAsync).</summary>
    public async ValueTask ShowAsync(ElementReference anchor, ElementReference panel)
    {
        _self ??= DotNetObjectReference.Create(this);
        _managerRef ??= DotNetObjectReference.Create(_manager);
        _shown = true;
        await JsSafe.Run(() => _js.OverlayOpenAsync(Id, _self, _managerRef, anchor, panel, MatchAnchorWidth));
    }

    // Set once slate.js has been told about the panel: only then is there anything to hide (never during prerender).
    private bool _shown;

    /// <summary>Recompute the position (content changed size).</summary>
    public ValueTask RepositionAsync() => JsSafe.Run(() => _js.OverlayRepositionAsync(Id));

    public ValueTask HideAsync()
    {
        if (!_shown) return ValueTask.CompletedTask;
        _shown = false;
        return JsSafe.Run(() => _js.OverlayCloseAsync(Id));
    }

    internal Task DismissAsync(OverlayDismissReason reason) => _onDismiss(reason);

    /// <summary>Called by slate.js with fresh measurements; returns where the panel goes.</summary>
    [JSInvokable]
    public OverlayPlacement Compute(BrowserRect anchor, double panelWidth, double panelHeight, BrowserRect viewport) =>
        Compute(anchor.ToOverlay(), panelWidth, panelHeight, viewport.ToOverlay());

    /// <summary>The placement logic itself (Slate.Core), exposed for tests.</summary>
    public OverlayPlacement Compute(OverlayRect anchor, double panelWidth, double panelHeight, OverlayRect viewport)
    {
        var result = Point is { } p
            ? PopoverPositioner.PositionAtPoint(p.X, p.Y, panelWidth, panelHeight, viewport)
            : PopoverPositioner.Position(new PositionRequest
            {
                Anchor = anchor,
                PopupWidth = panelWidth,
                PopupHeight = panelHeight,
                Viewport = viewport,
                Placement = Placement,
                Offset = Offset,
            });
        var placement = Names.Kebab(result.Placement.ToString());
        return new OverlayPlacement(Math.Round(result.Rect.X), Math.Round(result.Rect.Y),
            result.MaxHeight is { } h ? Math.Floor(h) : null, placement.Split('-')[0], placement, Math.Round(result.ArrowOffset));
    }

    public async ValueTask DisposeAsync()
    {
        _manager.Remove(this);
        await HideAsync();
        IsOpen = false;
        _self?.Dispose();
        _managerRef?.Dispose();
    }
}
