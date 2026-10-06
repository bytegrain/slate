using Microsoft.AspNetCore.Components;

namespace Slate.Blazor;

/// <summary>Base for Slate components: extra classes, inline style and unmatched attributes pass through to the root element.</summary>
public abstract class SlComponentBase : ComponentBase
{
    /// <summary>Extra CSS classes appended to the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline style for the root element.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Any other attributes (id, data-*, aria-*, event handlers) go on the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private static int _ids;

    /// <summary>A stable, unique id prefix for ARIA relationships.</summary>
    protected string UniqueId { get; } = $"sl{Interlocked.Increment(ref _ids)}";

    /// <summary>The user's id attribute if supplied, otherwise <see cref="UniqueId"/>.</summary>
    protected string ElementId =>
        AdditionalAttributes is not null && AdditionalAttributes.TryGetValue("id", out var id) && id is not null
            ? id.ToString()!
            : UniqueId;
}
