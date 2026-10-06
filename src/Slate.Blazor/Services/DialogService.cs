using Microsoft.AspNetCore.Components;
using Slate.Dialogs;

namespace Slate.Blazor.Services;

/// <summary>Parameters passed to a dialog component (matched to its [Parameter] properties by name).</summary>
public sealed class DialogParameters : Dictionary<string, object?>
{
    public DialogParameters() : base(StringComparer.Ordinal) { }

    /// <summary>Fluent add: <c>new DialogParameters().Set(nameof(RenameDialog.Name), "x")</c>.</summary>
    public DialogParameters Set(string name, object? value)
    {
        this[name] = value;
        return this;
    }
}

/// <summary>What a dialog shows. One of: a component type, an inline render fragment, or a message box.</summary>
public sealed record DialogContent
{
    public Type? ComponentType { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();
    public RenderFragment? Fragment { get; init; }
    public MessageBoxOptions? MessageBox { get; init; }

    /// <summary>Optional supporting line under the title (used as the accessible description).</summary>
    public string? Description { get; init; }
}

/// <summary>Opens dialogs. Inject it anywhere; <c>SlateProvider</c> renders them.</summary>
public interface IDialogService
{
    /// <summary>Open dialogs, bottom to top.</summary>
    IReadOnlyList<DialogReference> Open { get; }

    event EventHandler? Changed;

    /// <summary>Shows <typeparamref name="TComponent"/> in a dialog. It receives a cascading <see cref="SlDialogInstance"/>.</summary>
    DialogReference Show<TComponent>(string? title = null, IReadOnlyDictionary<string, object?>? parameters = null, DialogOptions? options = null)
        where TComponent : IComponent;

    Task<DialogResult> ShowAsync<TComponent>(string? title = null, IReadOnlyDictionary<string, object?>? parameters = null, DialogOptions? options = null)
        where TComponent : IComponent;

    /// <summary>Shows arbitrary content (used by the declarative <c>SlDialog</c>).</summary>
    DialogReference Show(DialogContent content, DialogOptions? options = null);

    /// <summary>A message box. True when confirmed; false when cancelled (cancel button, Escape, scrim, close).</summary>
    Task<bool> ConfirmAsync(MessageBoxOptions options);

    /// <summary>An acknowledgement box with a single button.</summary>
    Task AlertAsync(string title, string message, Severity severity = Severity.Normal);

    bool Close(DialogReference dialog, DialogResult? result = null);

    /// <summary>Escape pressed on the top dialog.</summary>
    bool HandleEscape();

    /// <summary>Scrim clicked behind a dialog.</summary>
    bool HandleBackdropClick(DialogReference dialog);

    void CloseAll();

    /// <summary>Re-render open dialogs (used by declarative dialogs when their parent re-renders).</summary>
    void Refresh();
}

/// <summary>Blazor adapter over Slate.Core's <see cref="DialogStack"/>.</summary>
internal sealed class DialogService : IDialogService
{
    private readonly DialogStack _stack = new();

    public DialogService() => _stack.Changed += (_, e) => Changed?.Invoke(this, e);

    public IReadOnlyList<DialogReference> Open => _stack.Open;
    public event EventHandler? Changed;

    public DialogReference Show<TComponent>(string? title = null, IReadOnlyDictionary<string, object?>? parameters = null, DialogOptions? options = null)
        where TComponent : IComponent
    {
        options ??= new DialogOptions();
        if (title is not null) options = options with { Title = title };
        return _stack.Push(new DialogContent
        {
            ComponentType = typeof(TComponent),
            Parameters = parameters ?? new Dictionary<string, object?>(),
        }, options);
    }

    public Task<DialogResult> ShowAsync<TComponent>(string? title = null, IReadOnlyDictionary<string, object?>? parameters = null, DialogOptions? options = null)
        where TComponent : IComponent => Show<TComponent>(title, parameters, options).Result;

    public DialogReference Show(DialogContent content, DialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.ComponentType is null && content.Fragment is null && content.MessageBox is null)
            throw new ArgumentException("Dialog content needs a component type, a fragment or message box options.", nameof(content));
        return _stack.Push(content, options);
    }

    public async Task<bool> ConfirmAsync(MessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var dialog = _stack.Push(new DialogContent { MessageBox = options, Description = options.Message }, new DialogOptions
        {
            Title = options.Title,
            MaxWidth = DialogWidth.Xs,
            // A destructive confirmation should only end through an explicit choice or Escape.
            CloseOnBackdropClick = !options.Destructive,
        });
        var result = await dialog.Result;
        return !result.Canceled;
    }

    public Task AlertAsync(string title, string message, Severity severity = Severity.Normal) =>
        ConfirmAsync(new MessageBoxOptions { Title = title, Message = message, CancelText = null, Severity = severity });

    public bool Close(DialogReference dialog, DialogResult? result = null) => _stack.Close(dialog, result);
    public bool HandleEscape() => _stack.HandleEscape();
    public bool HandleBackdropClick(DialogReference dialog) => _stack.HandleBackdropClick(dialog);
    public void CloseAll() => _stack.CloseAll();
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
}
