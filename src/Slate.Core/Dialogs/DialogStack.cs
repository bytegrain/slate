namespace Slate.Dialogs;

/// <summary>Maximum dialog width, from the size.dialog tokens.</summary>
public enum DialogWidth
{
    Xs,
    Sm,
    Md,
    Lg,
    Xl,
}

public enum DialogPlacement
{
    Center,
    /// <summary>Near the top — for command palettes and long forms that grow.</summary>
    Top,
}

public sealed record DialogOptions
{
    public string? Title { get; init; }

    /// <summary>Supporting text under the title (the dialog's accessible description).</summary>
    public string? Description { get; init; }

    /// <summary>Icon shown in a tinted tile beside the title (an icon name from SlateIcons).</summary>
    public string? Icon { get; init; }

    /// <summary>Colours the icon tile; <see cref="Tone.Danger"/> for destructive confirmations.</summary>
    public Tone Tone { get; init; } = Tone.Neutral;
    public DialogWidth MaxWidth { get; init; } = DialogWidth.Sm;

    /// <summary>Use the full <see cref="MaxWidth"/> instead of sizing to content.</summary>
    public bool FullWidth { get; init; }

    public bool FullScreen { get; init; }
    public DialogPlacement Placement { get; init; } = DialogPlacement.Center;
    public bool CloseOnEscape { get; init; } = true;

    /// <summary>Clicking the scrim cancels. Turn off for forms that could lose input.</summary>
    public bool CloseOnBackdropClick { get; init; } = true;

    public bool ShowCloseButton { get; init; } = true;

    public static double WidthPixels(DialogWidth w) => w switch
    {
        DialogWidth.Xs => SlateTokens.Size.Dialog.Xs,
        DialogWidth.Sm => SlateTokens.Size.Dialog.Sm,
        DialogWidth.Md => SlateTokens.Size.Dialog.Md,
        DialogWidth.Lg => SlateTokens.Size.Dialog.Lg,
        DialogWidth.Xl => SlateTokens.Size.Dialog.Xl,
        _ => throw new ArgumentOutOfRangeException(nameof(w)),
    };
}

/// <summary>How a dialog ended. Cancel covers Escape, the close button and backdrop clicks.</summary>
public sealed record DialogResult(bool Canceled, object? Data)
{
    public static DialogResult Ok(object? data = null) => new(false, data);
    public static DialogResult Cancel() => new(true, null);

    public T? GetData<T>() => Data is T t ? t : default;
}

/// <summary>A message box / confirmation built from options rather than custom content.</summary>
public sealed record MessageBoxOptions
{
    public string? Title { get; init; }
    public required string Message { get; init; }
    public string ConfirmText { get; init; } = "OK";

    /// <summary>Null hides the cancel button (an acknowledgement box).</summary>
    public string? CancelText { get; init; } = "Cancel";

    /// <summary>Renders the confirm button as <c>Solid + Danger</c> and adds a warning icon.</summary>
    public bool Destructive { get; init; }

    public Severity Severity { get; init; } = Severity.Normal;
}

/// <summary>An open dialog. Await <see cref="Result"/> for the outcome.</summary>
public sealed class DialogReference
{
    private readonly TaskCompletionSource<DialogResult> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal DialogReference(long id, object content, DialogOptions options)
    {
        Id = id;
        Content = content;
        Options = options;
    }

    public long Id { get; }

    /// <summary>Platform-specific payload: a component type + parameters, a view model, a control, or <see cref="MessageBoxOptions"/>.</summary>
    public object Content { get; }

    public DialogOptions Options { get; }
    public Task<DialogResult> Result => _tcs.Task;
    public bool IsOpen => !_tcs.Task.IsCompleted;

    internal bool Complete(DialogResult result) => _tcs.TrySetResult(result);
}

/// <summary>
/// The platform-independent dialog manager: a stack of modal dialogs where only the top one is interactive.
/// Platform dialog services push content here and render <see cref="Open"/>.
/// </summary>
public sealed class DialogStack
{
    private readonly Lock _gate = new();
    private readonly List<DialogReference> _open = [];
    private long _nextId;

    public event EventHandler? Changed;

    /// <summary>Open dialogs, bottom to top.</summary>
    public IReadOnlyList<DialogReference> Open
    {
        get { lock (_gate) return _open.ToList(); }
    }

    public DialogReference? Top
    {
        get { lock (_gate) return _open.Count > 0 ? _open[^1] : null; }
    }

    public DialogReference Push(object content, DialogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        DialogReference dialog;
        lock (_gate)
        {
            dialog = new DialogReference(++_nextId, content, options ?? new DialogOptions());
            _open.Add(dialog);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return dialog;
    }

    /// <summary>Closes a dialog (not necessarily the top one). A null result means cancel.</summary>
    public bool Close(DialogReference dialog, DialogResult? result = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        lock (_gate)
        {
            if (!_open.Remove(dialog))
                return false;
        }
        dialog.Complete(result ?? DialogResult.Cancel());
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Escape pressed: cancels the top dialog if it allows it.</summary>
    public bool HandleEscape() =>
        Top is { Options.CloseOnEscape: true } top && Close(top, DialogResult.Cancel());

    /// <summary>Scrim clicked behind <paramref name="dialog"/>: cancels it only if it is on top and allows it.</summary>
    public bool HandleBackdropClick(DialogReference dialog) =>
        Top == dialog && dialog.Options.CloseOnBackdropClick && Close(dialog, DialogResult.Cancel());

    public void CloseAll()
    {
        List<DialogReference> all;
        lock (_gate)
        {
            all = _open.AsEnumerable().Reverse().ToList();
            _open.Clear();
        }
        foreach (var d in all) d.Complete(DialogResult.Cancel());
        if (all.Count > 0)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}
