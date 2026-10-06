namespace Slate.Snackbars;

public enum SnackbarPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public enum SnackbarState
{
    /// <summary>Waiting for a free slot (more than <see cref="SnackbarConfiguration.MaxVisible"/> are active).</summary>
    Queued,
    Visible,
    Closed,
}

public enum SnackbarCloseReason
{
    /// <summary>Its duration elapsed.</summary>
    Timeout,
    /// <summary>The user pressed close (or Escape).</summary>
    User,
    /// <summary>The user invoked its action.</summary>
    Action,
    /// <summary>Closed from code via <see cref="SnackbarQueue.Dismiss"/>.</summary>
    Programmatic,
    /// <summary>Removed by <see cref="SnackbarQueue.Clear"/> or because the queue overflowed.</summary>
    Cleared,
}

/// <summary>A button shown on a snackbar, e.g. "Undo". Invoking it also closes the snackbar.</summary>
public sealed record SnackbarAction(string Label, Func<Task>? OnInvoke = null)
{
    public static SnackbarAction Create(string label, Action onInvoke) =>
        new(label, () => { onInvoke(); return Task.CompletedTask; });
}

/// <summary>What to show. Only <see cref="Message"/> is required.</summary>
public sealed record SnackbarOptions
{
    public required string Message { get; init; }

    /// <summary>Optional bold first line.</summary>
    public string? Title { get; init; }

    public Severity Severity { get; init; } = Severity.Normal;

    /// <summary>How long it stays. Null uses the configuration's default for its severity/action.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Stays until dismissed. Use for messages the user must not miss.</summary>
    public bool RequireInteraction { get; init; }

    public SnackbarAction? Action { get; init; }

    public bool ShowCloseButton { get; init; } = true;

    /// <summary>Identity for duplicate suppression. Defaults to severity + title + message.</summary>
    public string? Key { get; init; }

    internal string EffectiveKey => Key ?? $"{Severity}|{Title}|{Message}";
}

/// <summary>Behaviour of a snackbar host. Defaults come from the design tokens.</summary>
public sealed record SnackbarConfiguration
{
    public SnackbarPosition Position { get; init; } = SnackbarPosition.BottomRight;

    public int MaxVisible { get; init; } = (int)SlateTokens.Snackbar.MaxVisible;

    /// <summary>Queued snackbars beyond this are dropped, oldest first.</summary>
    public int MaxQueued { get; init; } = 20;

    public TimeSpan DefaultDuration { get; init; } = TimeSpan.FromMilliseconds(SlateTokens.Snackbar.Duration.Default);

    public TimeSpan ErrorDuration { get; init; } = TimeSpan.FromMilliseconds(SlateTokens.Snackbar.Duration.Error);

    /// <summary>Snackbars with an action never auto-close sooner than this.</summary>
    public TimeSpan MinimumDurationWithAction { get; init; } = TimeSpan.FromMilliseconds(SlateTokens.Snackbar.Duration.WithAction);

    /// <summary>When an identical snackbar is already active, return it instead of adding another.</summary>
    public bool PreventDuplicates { get; init; } = true;

    /// <summary>Newest first in <see cref="SnackbarQueue.Visible"/> (for top-anchored hosts).</summary>
    public bool NewestOnTop { get; init; }

    internal void Validate()
    {
        if (MaxVisible < 1) throw new ArgumentOutOfRangeException(nameof(MaxVisible), "At least one snackbar must be visible.");
        if (MaxQueued < 0) throw new ArgumentOutOfRangeException(nameof(MaxQueued));
        if (DefaultDuration <= TimeSpan.Zero || ErrorDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DefaultDuration), "Durations must be positive; use RequireInteraction for sticky snackbars.");
    }
}

/// <summary>A live snackbar. Platforms render <see cref="SnackbarQueue.Visible"/> and call back into the queue.</summary>
public sealed class Snackbar
{
    internal Snackbar(long id, SnackbarOptions options, TimeSpan? duration, DateTimeOffset createdAt)
    {
        Id = id;
        Options = options;
        Duration = duration;
        CreatedAt = createdAt;
    }

    public long Id { get; }
    public SnackbarOptions Options { get; }

    /// <summary>Effective auto-close time, or null if it waits for the user.</summary>
    public TimeSpan? Duration { get; }

    public DateTimeOffset CreatedAt { get; }
    public SnackbarState State { get; internal set; } = SnackbarState.Queued;
    public SnackbarCloseReason? CloseReason { get; internal set; }
    public bool IsPaused { get; internal set; }

    /// <summary>Time left before auto-close (frozen while paused). Null for sticky snackbars.</summary>
    public TimeSpan? Remaining { get; internal set; }

    internal DateTimeOffset? TimerStartedAt { get; set; }
    internal ITimer? Timer { get; set; }

    public string Message => Options.Message;
    public Severity Severity => Options.Severity;

    public override string ToString() => $"#{Id} {State} {Severity}: {Message}";
}

public sealed class SnackbarClosedEventArgs(Snackbar snackbar, SnackbarCloseReason reason) : EventArgs
{
    public Snackbar Snackbar { get; } = snackbar;
    public SnackbarCloseReason Reason { get; } = reason;
}
