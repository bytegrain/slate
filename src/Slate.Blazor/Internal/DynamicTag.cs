using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Slate.Blazor.Internal;

/// <summary>Renders an element whose tag name is chosen at runtime.</summary>
public sealed class DynamicTag : ComponentBase
{
    [Parameter, EditorRequired] public string Tag { get; set; } = "div";
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }
    [Parameter] public IReadOnlyDictionary<string, object>? Attributes { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Tag);
        builder.AddMultipleAttributes(1, Attributes);
        if (!string.IsNullOrEmpty(Class)) builder.AddAttribute(2, "class", Class);
        if (!string.IsNullOrEmpty(Style)) builder.AddAttribute(3, "style", Style);
        builder.AddContent(4, ChildContent);
        builder.CloseElement();
    }
}
