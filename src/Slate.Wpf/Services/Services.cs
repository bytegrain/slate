using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Slate.Dialogs;
using Slate.Snackbars;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf;

/// <summary>Shows snackbars (docs/design/systems.md#snackbars). Behaviour lives in Slate.Core's <see cref="SnackbarQueue"/>.</summary>
public interface ISnackbarService
{
    SnackbarQueue Queue { get; }
    SnackbarConfiguration Configuration { get; }

    /// <summary>Visible snackbars in display order, updated on the UI thread (bind hosts to this).</summary>
    ReadOnlyObservableCollection<Snackbar> Visible { get; }

    Snackbar Add(string message, Severity severity = Severity.Normal);
    Snackbar Add(SnackbarOptions options);
    void Dismiss(Snackbar snackbar);
    void Clear();
}

/// <summary>Default <see cref="ISnackbarService"/>: wraps a queue and mirrors it onto the dispatcher.</summary>
public sealed class SnackbarService : ISnackbarService, IDisposable
{
    private readonly ObservableCollection<Snackbar> _visible = [];
    private readonly Dispatcher _dispatcher;

    public SnackbarService(SnackbarConfiguration? configuration = null, TimeProvider? timeProvider = null, Dispatcher? dispatcher = null)
    {
        Queue = new SnackbarQueue(configuration, timeProvider);
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Visible = new ReadOnlyObservableCollection<Snackbar>(_visible);
        Queue.Changed += (_, _) => Post(Sync);
    }

    public SnackbarQueue Queue { get; }
    public SnackbarConfiguration Configuration => Queue.Configuration;
    public ReadOnlyObservableCollection<Snackbar> Visible { get; }

    public Snackbar Add(string message, Severity severity = Severity.Normal) => Queue.Add(message, severity);
    public Snackbar Add(SnackbarOptions options) => Queue.Add(options);
    public void Dismiss(Snackbar snackbar) => Queue.Dismiss(snackbar, SnackbarCloseReason.Programmatic);
    public void Clear() => Queue.Clear();

    public void Dispose() => Queue.Dispose();

    private void Post(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.BeginInvoke(action);
    }

    /// <summary>Applies the queue's current visible list with minimal collection changes (keeps item containers alive).</summary>
    internal void Sync()
    {
        var target = Queue.Visible;
        for (var i = _visible.Count - 1; i >= 0; i--)
        {
            if (!target.Contains(_visible[i]))
                _visible.RemoveAt(i);
        }

        for (var i = 0; i < target.Count; i++)
        {
            var current = _visible.IndexOf(target[i]);
            if (current < 0)
                _visible.Insert(i, target[i]);
            else if (current != i)
                _visible.Move(current, i);
        }
    }
}

/// <summary>Shows modal dialogs inside the window (docs/design/systems.md#dialogs), backed by Slate.Core's <see cref="DialogStack"/>.</summary>
public interface IDialogService
{
    DialogStack Stack { get; }

    /// <summary>Opens a dialog without waiting; await <see cref="DialogReference.Result"/> later.</summary>
    DialogReference Show(object content, DialogOptions? options = null);

    /// <summary>Shows any content: a UIElement, or a view model rendered through a DataTemplate.</summary>
    Task<DialogResult> ShowAsync(object content, DialogOptions? options = null);

    Task<DialogResult> ShowAsync(object content, string title);

    /// <summary>Confirmation / message box. True when confirmed.</summary>
    Task<bool> ConfirmAsync(MessageBoxOptions options);

    /// <summary>Acknowledgement box with a single OK button.</summary>
    Task AlertAsync(string title, string message, Severity severity = Severity.Normal);

    bool Close(DialogReference dialog, DialogResult? result = null);
}

public sealed class DialogService : IDialogService
{
    public DialogStack Stack { get; } = new();

    /// <summary>Unset options fall back to <see cref="SlateTheme.Defaults"/>.Dialog.</summary>
    public DialogReference Show(object content, DialogOptions? options = null) => Stack.Push(content, options ?? SlateTheme.Defaults.Dialog);

    public Task<DialogResult> ShowAsync(object content, DialogOptions? options = null) => Show(content, options).Result;

    public Task<DialogResult> ShowAsync(object content, string title) => ShowAsync(content, SlateTheme.Defaults.Dialog with { Title = title });

    public async Task<bool> ConfirmAsync(MessageBoxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = await ShowAsync(options, MessageBoxDialogOptions(options)).ConfigureAwait(true);
        return !result.Canceled;
    }

    public Task AlertAsync(string title, string message, Severity severity = Severity.Normal) =>
        ConfirmAsync(new MessageBoxOptions { Title = title, Message = message, CancelText = null, Severity = severity });

    public bool Close(DialogReference dialog, DialogResult? result = null) => Stack.Close(dialog, result);

    /// <summary>Message boxes are narrow and only close on Escape when they can be cancelled.</summary>
    public static DialogOptions MessageBoxDialogOptions(MessageBoxOptions o) => new()
    {
        Title = o.Title,
        Tone = o.Destructive ? Tone.Danger : o.Severity.ToTone(),
        MaxWidth = DialogWidth.Xs,
        ShowCloseButton = false,
        CloseOnBackdropClick = o.CancelText is not null && !o.Destructive,
        CloseOnEscape = true,
    };
}

/// <summary>
/// App-wide defaults for apps that don't use dependency injection. <see cref="SlateWindow"/>, <see cref="SnackbarHost"/>
/// and <see cref="DialogHost"/> use these unless given a service explicitly. <c>AddSlate()</c> points them at the DI singletons.
/// </summary>
public static class SlateServices
{
    private static ISnackbarService? _snackbar;
    private static IDialogService? _dialogs;

    public static ISnackbarService Snackbar
    {
        get => _snackbar ??= new SnackbarService(SlateTheme.Defaults.Snackbar);
        set => _snackbar = value;
    }

    public static IDialogService Dialogs
    {
        get => _dialogs ??= new DialogService();
        set => _dialogs = value;
    }
}

public static class SlateServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ISnackbarService"/> and <see cref="IDialogService"/> as singletons shared with <see cref="SlateServices"/>,
    /// and applies <see cref="SlateOptions.Defaults"/> (to <see cref="SlateTheme.Defaults"/>) and <see cref="SlateOptions.Theme"/>.
    /// </summary>
    public static IServiceCollection AddSlate(this IServiceCollection services, Action<SlateOptions>? configure = null)
    {
        var options = new SlateOptions();
        configure?.Invoke(options);

        SlateTheme.Defaults = options.Defaults;
        if (options.Theme is { } theme)
        {
            if (SlateTheme.Current is { } current)
                current.Options = theme;
            else
                SlateTheme.PendingOptions = theme;
        }

        var snackbar = new SnackbarService(options.Defaults.Snackbar, options.TimeProvider);
        var dialogs = new DialogService();
        SlateServices.Snackbar = snackbar;
        SlateServices.Dialogs = dialogs;

        services.TryAddSingleton<ISnackbarService>(snackbar);
        services.TryAddSingleton<IDialogService>(dialogs);
        return services;
    }
}

public sealed class SlateOptions
{
    /// <summary>App-wide component defaults (also used for the snackbar host and dialogs).</summary>
    public SlateDefaults Defaults { get; } = new();

    /// <summary>Custom theme applied to <see cref="SlateTheme.Current"/> (or to the next SlateTheme created).</summary>
    public Theming.SlateThemeOptions? Theme { get; set; }

    public TimeProvider? TimeProvider { get; set; }
}
