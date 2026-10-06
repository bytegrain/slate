using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Slate.Blazor;

/// <summary>
/// Base for Slate components: extra classes, inline style and unmatched attributes pass through to the root element,
/// and app-wide <see cref="SlateDefaults"/> are available for unset parameters.
/// </summary>
public abstract class SlComponentBase : ComponentBase
{
    private static readonly SlateDefaults FallbackDefaults = new();

    /// <summary>Extra CSS classes appended to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline style for the root element.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Any other attributes (id, data-*, aria-*, event handlers) go on the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>The app's defaults (<c>AddSlate(o => o.Defaults…)</c>), or Alloy's when Slate isn't registered.</summary>
    protected SlateDefaults Defaults => Services?.GetService<SlateOptions>()?.Defaults ?? FallbackDefaults;

    private static int _ids;

    /// <summary>A stable, unique id prefix for ARIA relationships.</summary>
    protected string UniqueId { get; } = $"sl{Interlocked.Increment(ref _ids)}";

    /// <summary>The user's id attribute if supplied, otherwise <see cref="UniqueId"/>.</summary>
    protected string ElementId =>
        AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("id", out var id) && id is not null
            ? id.ToString()!
            : UniqueId;
}
