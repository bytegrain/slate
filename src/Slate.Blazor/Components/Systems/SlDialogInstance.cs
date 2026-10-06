using Slate.Blazor.Services;
using Slate.Dialogs;

namespace Slate.Blazor;

/// <summary>
/// Cascaded to dialog content. Use it to finish the dialog:
/// <code>[CascadingParameter] SlDialogInstance Dialog { get; set; }  …  Dialog.Ok(name);</code>
/// </summary>
public sealed class SlDialogInstance
{
    private readonly IDialogService _service;

    internal SlDialogInstance(IDialogService service, DialogReference reference)
    {
        _service = service;
        Reference = reference;
    }

    public DialogReference Reference { get; }
    public DialogOptions Options => Reference.Options;

    /// <summary>Id of the dialog title element, for aria references inside custom content.</summary>
    public string TitleId => $"sl-dialog-{Reference.Id}-title";

    public bool Close(DialogResult result) => _service.Close(Reference, result);
    public bool Ok(object? data = null) => Close(DialogResult.Ok(data));
    public bool Cancel() => Close(DialogResult.Cancel());
}
