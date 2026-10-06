namespace Slate.Snackbars;

/// <summary>
/// The platform-independent snackbar engine: ordering, queueing, duplicate suppression, auto-close timers
/// and pause-on-hover. Each platform owns one queue per host and renders <see cref="Visible"/> whenever
/// <see cref="Changed"/> fires.
/// </summary>
/// <remarks>
/// Thread-safe. Timers fire on thread-pool threads, so <see cref="Changed"/> and <see cref="Closed"/> may be
/// raised off the UI thread — platform hosts must marshal to their dispatcher. Events are never raised
/// while the internal lock is held.
/// </remarks>
public sealed class SnackbarQueue : IDisposable
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly List<Snackbar> _visible = [];
    private readonly LinkedList<Snackbar> _queued = new();
    private long _nextId;
    private bool _disposed;

    public SnackbarQueue(SnackbarConfiguration? configuration = null, TimeProvider? timeProvider = null)
    {
        Configuration = configuration ?? new SnackbarConfiguration();
        Configuration.Validate();
        _time = timeProvider ?? TimeProvider.System;
    }

    public SnackbarConfiguration Configuration { get; }

    /// <summary>Raised after any change to visible or queued snackbars.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised once per snackbar when it closes, with the reason.</summary>
    public event EventHandler<SnackbarClosedEventArgs>? Closed;

    /// <summary>Snackbars to render, in display order.</summary>
    public IReadOnlyList<Snackbar> Visible
    {
        get
        {
            lock (_gate)
            {
                return Configuration.NewestOnTop ? _visible.AsEnumerable().Reverse().ToList() : _visible.ToList();
            }
        }
    }

    public int QueuedCount
    {
        get { lock (_gate) return _queued.Count; }
    }

    public Snackbar Add(string message, Severity severity = Severity.Normal) =>
        Add(new SnackbarOptions { Message = message, Severity = severity });

    public Snackbar Add(SnackbarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Message))
            throw new ArgumentException("A snackbar needs a message.", nameof(options));
        if (options.Duration is { } d && d <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Duration must be positive; use RequireInteraction for sticky snackbars.");

        var closed = new List<(Snackbar, SnackbarCloseReason)>();
        Snackbar snackbar;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (Configuration.PreventDuplicates)
            {
                var key = options.EffectiveKey;
                var existing = _visible.Concat(_queued).FirstOrDefault(s => s.Options.EffectiveKey == key);
                if (existing is not null)
                    return existing;
            }

            snackbar = new Snackbar(++_nextId, options, EffectiveDuration(options), _time.GetUtcNow());

            if (_visible.Count < Configuration.MaxVisible)
            {
                Show(snackbar);
            }
            else
            {
                _queued.AddLast(snackbar);
                while (_queued.Count > Configuration.MaxQueued)
                {
                    var dropped = _queued.First!.Value;
                    _queued.RemoveFirst();
                    MarkClosed(dropped, SnackbarCloseReason.Cleared);
                    closed.Add((dropped, SnackbarCloseReason.Cleared));
                }
            }
        }

        Raise(closed);
        return snackbar;
    }

    /// <summary>Closes a visible snackbar (promoting the next queued one) or removes a queued one.</summary>
    /// <returns>False if it was already closed.</returns>
    public bool Dismiss(Snackbar snackbar, SnackbarCloseReason reason = SnackbarCloseReason.Programmatic)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        lock (_gate)
        {
            if (!CloseLocked(snackbar, reason))
                return false;
        }

        Raise([(snackbar, reason)]);
        return true;
    }

    /// <summary>Runs the snackbar's action, then closes it with <see cref="SnackbarCloseReason.Action"/>.</summary>
    public async Task InvokeActionAsync(Snackbar snackbar)
    {
        ArgumentNullException.ThrowIfNull(snackbar);
        if (snackbar.Options.Action is not { } action || snackbar.State != SnackbarState.Visible)
            return;

        // Close first so a slow or failing action can't leave the snackbar on screen or run twice.
        if (!Dismiss(snackbar, SnackbarCloseReason.Action))
            return;

        if (action.OnInvoke is { } run)
            await run().ConfigureAwait(false);
    }

    /// <summary>Freezes the auto-close countdown (pointer hover or keyboard focus).</summary>
    public void Pause(Snackbar snackbar)
    {
        lock (_gate)
        {
            if (snackbar.State != SnackbarState.Visible || snackbar.IsPaused)
                return;
            snackbar.IsPaused = true;
            if (snackbar.Timer is null)
                return;

            snackbar.Remaining = RemainingNow(snackbar);
            StopTimer(snackbar);
        }
        RaiseChanged();
    }

    /// <summary>Restarts the countdown with the time that was left when paused.</summary>
    public void Resume(Snackbar snackbar)
    {
        lock (_gate)
        {
            if (snackbar.State != SnackbarState.Visible || !snackbar.IsPaused)
                return;
            snackbar.IsPaused = false;
            if (snackbar.Remaining is { } left)
                StartTimer(snackbar, left);
        }
        RaiseChanged();
    }

    /// <summary>Pause or resume everything — e.g. while the window is inactive.</summary>
    public void PauseAll() { foreach (var s in Visible) Pause(s); }

    public void ResumeAll() { foreach (var s in Visible) Resume(s); }

    /// <summary>Closes every visible and queued snackbar.</summary>
    public void Clear()
    {
        List<(Snackbar, SnackbarCloseReason)> closed;
        lock (_gate)
        {
            closed = _queued.Concat(_visible).Select(s => (s, SnackbarCloseReason.Cleared)).ToList();
            _queued.Clear();
            foreach (var s in _visible) StopTimer(s);
            _visible.Clear();
            foreach (var (s, r) in closed) MarkClosed(s, r);
        }
        if (closed.Count > 0)
            Raise(closed);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _visible) StopTimer(s);
        }
    }

    internal TimeSpan? EffectiveDuration(SnackbarOptions o)
    {
        if (o.RequireInteraction)
            return null;
        var duration = o.Duration ?? (o.Severity == Severity.Error ? Configuration.ErrorDuration : Configuration.DefaultDuration);
        if (o.Action is not null && duration < Configuration.MinimumDurationWithAction)
            duration = Configuration.MinimumDurationWithAction;
        return duration;
    }

    // ---- everything below runs under _gate ----

    private void Show(Snackbar s)
    {
        s.State = SnackbarState.Visible;
        _visible.Add(s);
        if (s.Duration is { } d)
        {
            s.Remaining = d;
            StartTimer(s, d);
        }
    }

    private bool CloseLocked(Snackbar s, SnackbarCloseReason reason)
    {
        if (s.State == SnackbarState.Closed)
            return false;

        if (s.State == SnackbarState.Queued)
        {
            _queued.Remove(s);
        }
        else
        {
            StopTimer(s);
            _visible.Remove(s);
            if (_queued.First is { } next && !_disposed)
            {
                _queued.RemoveFirst();
                Show(next.Value);
            }
        }

        MarkClosed(s, reason);
        return true;
    }

    private static void MarkClosed(Snackbar s, SnackbarCloseReason reason)
    {
        s.State = SnackbarState.Closed;
        s.CloseReason = reason;
        s.Remaining = null;
    }

    private void StartTimer(Snackbar s, TimeSpan due)
    {
        s.TimerStartedAt = _time.GetUtcNow();
        s.Remaining = due;
        s.Timer = _time.CreateTimer(static state =>
        {
            var (queue, snackbar) = ((SnackbarQueue, Snackbar))state!;
            queue.Dismiss(snackbar, SnackbarCloseReason.Timeout);
        }, (this, s), due, Timeout.InfiniteTimeSpan);
    }

    private static void StopTimer(Snackbar s)
    {
        s.Timer?.Dispose();
        s.Timer = null;
        s.TimerStartedAt = null;
    }

    private TimeSpan RemainingNow(Snackbar s)
    {
        if (s.Remaining is not { } remaining || s.TimerStartedAt is not { } started)
            return TimeSpan.Zero;
        var left = remaining - (_time.GetUtcNow() - started);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void Raise(IReadOnlyList<(Snackbar Snackbar, SnackbarCloseReason Reason)> closed)
    {
        foreach (var (s, r) in closed)
            Closed?.Invoke(this, new SnackbarClosedEventArgs(s, r));
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
