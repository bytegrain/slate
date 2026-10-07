using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Slate.Blazor.Internal;

/// <summary>Renders its content only when <see cref="Version"/> changes (skips re-diffing static subtrees on hot paths).</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class Memo : ComponentBase
{
    private int? _rendered;

    [Parameter] public int Version { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override bool ShouldRender() => _rendered != Version;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _rendered = Version;
        builder.AddContent(0, ChildContent);
    }
}
