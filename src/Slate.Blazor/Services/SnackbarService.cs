using Slate.Snackbars;

namespace Slate.Blazor.Services;

/// <summary>Shows snackbars. Inject it anywhere; <c>SlateProvider</c> renders them.</summary>
public interface ISnackbarService
{
    SnackbarConfiguration Configuration { get; }

    /// <summary>Snackbars to render, in display order.</summary>
    IReadOnlyList<Snackbar> Visible { get; }

    int QueuedCount { get; }

    /// <summary>Raised after any change. May fire off the UI thread (timers); hosts must marshal.</summary>
    event EventHandler? Changed;

    /// <summary>Raised once per snackbar when it closes.</summary>
    event EventHandler<SnackbarClosedEventArgs>? Closed;

    Snackbar Add(string message, Severity severity = Severity.Normal);
    Snackbar Add(SnackbarOptions options);
    bool Dismiss(Snackbar snackbar, SnackbarCloseReason reason = SnackbarCloseReason.User);
    Task InvokeActionAsync(Snackbar snackbar);
    void Pause(Snackbar snackbar);
    void Resume(Snackbar snackbar);
    void Clear();
}

/// <summary>Convenience overloads.</summary>
public static class SnackbarServiceExtensions
{
    public static Snackbar Info(this ISnackbarService s, string message, string? title = null) => s.Add(new SnackbarOptions { Message = message, Title = title, Severity = Severity.Info });
    public static Snackbar Success(this ISnackbarService s, string message, string? title = null) => s.Add(new SnackbarOptions { Message = message, Title = title, Severity = Severity.Success });
    public static Snackbar Warning(this ISnackbarService s, string message, string? title = null) => s.Add(new SnackbarOptions { Message = message, Title = title, Severity = Severity.Warning });
    public static Snackbar Error(this ISnackbarService s, string message, string? title = null) => s.Add(new SnackbarOptions { Message = message, Title = title, Severity = Severity.Error });
}

/// <summary>Blazor adapter over Slate.Core's <see cref="SnackbarQueue"/> (one per circuit/tab).</summary>
internal sealed class SnackbarService : ISnackbarService, IDisposable
{
    private readonly SnackbarQueue _queue;

    public SnackbarService(SlateOptions options)
    {
        _queue = new SnackbarQueue(options.Snackbars, options.TimeProvider);
        _queue.Changed += (_, e) => Changed?.Invoke(this, e);
        _queue.Closed += (_, e) => Closed?.Invoke(this, e);
    }

    public SnackbarConfiguration Configuration => _queue.Configuration;
    public IReadOnlyList<Snackbar> Visible => _queue.Visible;
    public int QueuedCount => _queue.QueuedCount;
    public event EventHandler? Changed;
    public event EventHandler<SnackbarClosedEventArgs>? Closed;

    public Snackbar Add(string message, Severity severity = Severity.Normal) => _queue.Add(message, severity);
    public Snackbar Add(SnackbarOptions options) => _queue.Add(options);
    public bool Dismiss(Snackbar snackbar, SnackbarCloseReason reason = SnackbarCloseReason.User) => _queue.Dismiss(snackbar, reason);
    public Task InvokeActionAsync(Snackbar snackbar) => _queue.InvokeActionAsync(snackbar);
    public void Pause(Snackbar snackbar) => _queue.Pause(snackbar);
    public void Resume(Snackbar snackbar) => _queue.Resume(snackbar);
    public void Clear() => _queue.Clear();
    public void Dispose() => _queue.Dispose();
}
