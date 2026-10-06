using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Slate.Dialogs;
using Slate.Snackbars;

namespace Slate.Avalonia.Services;

/// <summary>App-wide snackbars (docs/design/systems.md). Rendered by a <see cref="Controls.SnackbarHost"/>.</summary>
public interface ISnackbarService
{
    SnackbarConfiguration Configuration { get; }

    /// <summary>Snackbars to render, in display order.</summary>
    IReadOnlyList<Snackbar> Visible { get; }

    int QueuedCount { get; }

    /// <summary>Raised on the UI thread whenever <see cref="Visible"/> or the queue changes.</summary>
    event EventHandler? Changed;

    Snackbar Add(string message, Severity severity = Severity.Normal);
    Snackbar Add(SnackbarOptions options);
    bool Dismiss(Snackbar snackbar, SnackbarCloseReason reason = SnackbarCloseReason.Programmatic);
    Task InvokeActionAsync(Snackbar snackbar);
    void Pause(Snackbar snackbar);
    void Resume(Snackbar snackbar);
    void Clear();
}

/// <summary>Avalonia wrapper over <see cref="SnackbarQueue"/> that marshals change notifications to the UI thread.</summary>
public sealed class SnackbarService : ISnackbarService, IDisposable
{
    private readonly SnackbarQueue _queue;

    public SnackbarService(SnackbarConfiguration? configuration = null, TimeProvider? timeProvider = null)
    {
        _queue = new SnackbarQueue(configuration, timeProvider);
        _queue.Changed += (_, _) => OnUiThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    public SnackbarConfiguration Configuration => _queue.Configuration;
    public IReadOnlyList<Snackbar> Visible => _queue.Visible;
    public int QueuedCount => _queue.QueuedCount;
    public event EventHandler? Changed;

    /// <summary>The underlying engine (for advanced scenarios and tests).</summary>
    public SnackbarQueue Queue => _queue;

    public Snackbar Add(string message, Severity severity = Severity.Normal) => _queue.Add(message, severity);
    public Snackbar Add(SnackbarOptions options) => _queue.Add(options);
    public bool Dismiss(Snackbar snackbar, SnackbarCloseReason reason = SnackbarCloseReason.Programmatic) => _queue.Dismiss(snackbar, reason);
    public Task InvokeActionAsync(Snackbar snackbar) => _queue.InvokeActionAsync(snackbar);
    public void Pause(Snackbar snackbar) => _queue.Pause(snackbar);
    public void Resume(Snackbar snackbar) => _queue.Resume(snackbar);
    public void Clear() => _queue.Clear();
    public void Dispose() => _queue.Dispose();

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}

/// <summary>App-wide modal dialogs (docs/design/systems.md). Rendered by a <see cref="Controls.DialogHost"/>.</summary>
public interface IDialogService
{
    DialogStack Stack { get; }

    /// <summary>
    /// Shows any content: a control, or a view model resolved through DataTemplates. Content that is a
    /// <see cref="Controls.DialogContent"/> gets a footer area for its actions.
    /// </summary>
    Task<DialogResult> ShowAsync(object content, DialogOptions? options = null);

    /// <summary>Creates <typeparamref name="TView"/>, sets its DataContext and shows it.</summary>
    Task<DialogResult> ShowAsync<TView>(object? dataContext = null, DialogOptions? options = null) where TView : Control, new();

    /// <summary>A message box. True when confirmed.</summary>
    Task<bool> ConfirmAsync(MessageBoxOptions options);

    /// <summary>An acknowledgement box with a single button.</summary>
    Task AlertAsync(string message, string? title = null);

    bool Close(DialogReference dialog, DialogResult? result = null);
}

public sealed class DialogService : IDialogService
{
    public DialogStack Stack { get; } = new();

    public async Task<DialogResult> ShowAsync(object content, DialogOptions? options = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        return await Stack.Push(content, options).Result;
    }

    public Task<DialogResult> ShowAsync<TView>(object? dataContext = null, DialogOptions? options = null) where TView : Control, new()
    {
        var view = new TView();
        if (dataContext is not null)
            view.DataContext = dataContext;
        return ShowAsync(view, options);
    }

    public async Task<bool> ConfirmAsync(MessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = await ShowAsync(options, new DialogOptions
        {
            Title = options.Title,
            MaxWidth = DialogWidth.Xs,
            // A destructive confirmation must be an explicit choice.
            CloseOnBackdropClick = !options.Destructive,
            ShowCloseButton = false,
        });
        return !result.Canceled;
    }

    public Task AlertAsync(string message, string? title = null) =>
        ConfirmAsync(new MessageBoxOptions { Message = message, Title = title, CancelText = null });

    public bool Close(DialogReference dialog, DialogResult? result = null) => Stack.Close(dialog, result);
}

/// <summary>
/// Default service instances, used by <see cref="Controls.SlateWindow"/> and the hosts when none are assigned.
/// Replace them at startup (or use <see cref="SlateServiceCollectionExtensions.AddSlate"/>) to customise.
/// </summary>
public static class SlateServices
{
    public static ISnackbarService Snackbars { get; set; } = new SnackbarService();
    public static IDialogService Dialogs { get; set; } = new DialogService();
}

public static class SlateServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISnackbarService"/> and <see cref="IDialogService"/> as singletons and makes them the
    /// <see cref="SlateServices"/> defaults, so SlateWindow and the hosts pick them up automatically.
    /// </summary>
    public static IServiceCollection AddSlate(this IServiceCollection services, SnackbarConfiguration? snackbars = null)
    {
        var snackbarService = new SnackbarService(snackbars);
        var dialogService = new DialogService();
        SlateServices.Snackbars = snackbarService;
        SlateServices.Dialogs = dialogService;
        services.AddSingleton<ISnackbarService>(snackbarService);
        services.AddSingleton<IDialogService>(dialogService);
        return services;
    }
}
