using Microsoft.Extensions.Time.Testing;
using Slate.Snackbars;

namespace Slate.Core.Tests;

public class SnackbarQueueTests
{
    private readonly FakeTimeProvider _time = new();

    private SnackbarQueue Queue(SnackbarConfiguration? config = null) => new(config, _time);

    private static SnackbarOptions Msg(string text, Severity severity = Severity.Normal) => new() { Message = text, Severity = severity };

    [Fact]
    public void Shows_immediately_while_there_is_room()
    {
        using var q = Queue();
        var s = q.Add("Saved");

        Assert.Equal(SnackbarState.Visible, s.State);
        Assert.Equal([s], q.Visible);
        Assert.Equal(0, q.QueuedCount);
    }

    [Fact]
    public void Queues_beyond_max_visible_and_promotes_in_order()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 2 });
        var a = q.Add("a");
        var b = q.Add("b");
        var c = q.Add("c");
        var d = q.Add("d");

        Assert.Equal([a, b], q.Visible);
        Assert.Equal(SnackbarState.Queued, c.State);
        Assert.Equal(2, q.QueuedCount);

        q.Dismiss(a);
        Assert.Equal([b, c], q.Visible);
        Assert.Equal(SnackbarState.Visible, c.State);
        Assert.Equal(SnackbarState.Queued, d.State);
    }

    [Fact]
    public void Newest_on_top_reverses_display_order()
    {
        using var q = Queue(new SnackbarConfiguration { NewestOnTop = true });
        var a = q.Add("a");
        var b = q.Add("b");
        Assert.Equal([b, a], q.Visible);
    }

    [Fact]
    public void Auto_closes_after_the_default_duration()
    {
        using var q = Queue();
        var s = q.Add("Saved");

        _time.Advance(TimeSpan.FromMilliseconds(SlateTokens.Snackbar.Duration.Default - 1));
        Assert.Equal(SnackbarState.Visible, s.State);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(SnackbarState.Closed, s.State);
        Assert.Equal(SnackbarCloseReason.Timeout, s.CloseReason);
        Assert.Empty(q.Visible);
    }

    [Fact]
    public void Errors_linger_longer_than_other_severities()
    {
        using var q = Queue();
        var info = q.Add(Msg("i", Severity.Info));
        var error = q.Add(Msg("e", Severity.Error));

        Assert.True(error.Duration > info.Duration);
        _time.Advance(info.Duration!.Value);
        Assert.Equal(SnackbarState.Closed, info.State);
        Assert.Equal(SnackbarState.Visible, error.State);
    }

    [Fact]
    public void Snackbars_with_actions_get_at_least_the_minimum_action_duration()
    {
        using var q = Queue();
        var s = q.Add(new SnackbarOptions { Message = "Deleted", Duration = TimeSpan.FromSeconds(1), Action = new("Undo") });
        Assert.Equal(q.Configuration.MinimumDurationWithAction, s.Duration);
    }

    [Fact]
    public void Explicit_duration_wins_when_there_is_no_action()
    {
        using var q = Queue();
        var s = q.Add(new SnackbarOptions { Message = "Quick", Duration = TimeSpan.FromSeconds(2) });
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SnackbarState.Closed, s.State);
    }

    [Fact]
    public void Require_interaction_never_times_out()
    {
        using var q = Queue();
        var s = q.Add(new SnackbarOptions { Message = "Connection lost", RequireInteraction = true });

        Assert.Null(s.Duration);
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(SnackbarState.Visible, s.State);
    }

    [Fact]
    public void Timeout_promotes_the_next_queued_snackbar_and_starts_its_timer()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 1 });
        var a = q.Add("a");
        var b = q.Add("b");

        _time.Advance(a.Duration!.Value);
        Assert.Equal(SnackbarState.Visible, b.State);

        _time.Advance(b.Duration!.Value);
        Assert.Equal(SnackbarState.Closed, b.State);
    }

    [Fact]
    public void Queued_time_does_not_count_against_duration()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 1 });
        var a = q.Add(new SnackbarOptions { Message = "a", RequireInteraction = true });
        var b = q.Add("b");

        _time.Advance(TimeSpan.FromMinutes(5)); // b is waiting the whole time
        q.Dismiss(a);
        Assert.Equal(SnackbarState.Visible, b.State);
        Assert.Equal(b.Duration, b.Remaining);
    }

    [Fact]
    public void Pause_freezes_and_resume_continues_with_remaining_time()
    {
        using var q = Queue();
        var s = q.Add(new SnackbarOptions { Message = "x", Duration = TimeSpan.FromSeconds(5) });

        _time.Advance(TimeSpan.FromSeconds(3));
        q.Pause(s);
        Assert.True(s.IsPaused);
        Assert.Equal(TimeSpan.FromSeconds(2), s.Remaining);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(SnackbarState.Visible, s.State);

        q.Resume(s);
        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(SnackbarState.Visible, s.State);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(SnackbarState.Closed, s.State);
    }

    [Fact]
    public void Pause_and_resume_are_idempotent()
    {
        using var q = Queue();
        var s = q.Add(new SnackbarOptions { Message = "x", Duration = TimeSpan.FromSeconds(5) });
        q.Pause(s);
        q.Pause(s);
        q.Resume(s);
        q.Resume(s);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(SnackbarState.Closed, s.State);
    }

    [Fact]
    public void Pause_all_and_resume_all_cover_every_visible_snackbar()
    {
        using var q = Queue();
        var a = q.Add("a");
        var b = q.Add("b");
        q.PauseAll();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.All(new[] { a, b }, s => Assert.Equal(SnackbarState.Visible, s.State));
        q.ResumeAll();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.All(new[] { a, b }, s => Assert.Equal(SnackbarState.Closed, s.State));
    }

    [Fact]
    public void Duplicates_return_the_existing_snackbar()
    {
        using var q = Queue();
        var a = q.Add("Saved");
        var b = q.Add("Saved");
        Assert.Same(a, b);
        Assert.Single(q.Visible);
    }

    [Fact]
    public void Same_message_with_different_severity_is_not_a_duplicate()
    {
        using var q = Queue();
        q.Add(Msg("Sync", Severity.Info));
        q.Add(Msg("Sync", Severity.Error));
        Assert.Equal(2, q.Visible.Count);
    }

    [Fact]
    public void Duplicates_allowed_when_disabled_or_after_close()
    {
        using var q = Queue(new SnackbarConfiguration { PreventDuplicates = false });
        Assert.NotSame(q.Add("x"), q.Add("x"));

        using var q2 = Queue();
        var first = q2.Add("x");
        q2.Dismiss(first);
        Assert.NotSame(first, q2.Add("x"));
    }

    [Fact]
    public void Custom_key_controls_duplicate_detection()
    {
        using var q = Queue();
        var a = q.Add(new SnackbarOptions { Message = "Uploading 1 file", Key = "upload" });
        var b = q.Add(new SnackbarOptions { Message = "Uploading 2 files", Key = "upload" });
        Assert.Same(a, b);
    }

    [Fact]
    public void Queue_overflow_drops_the_oldest_queued()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 1, MaxQueued = 2 });
        var closed = new List<SnackbarClosedEventArgs>();
        q.Closed += (_, e) => closed.Add(e);

        q.Add("visible");
        var oldest = q.Add("q1");
        q.Add("q2");
        q.Add("q3");

        Assert.Equal(2, q.QueuedCount);
        Assert.Equal(SnackbarState.Closed, oldest.State);
        Assert.Equal(SnackbarCloseReason.Cleared, Assert.Single(closed).Reason);
    }

    [Fact]
    public void Dismissing_a_queued_snackbar_removes_it_without_showing_it()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 1 });
        q.Add("a");
        var b = q.Add("b");
        Assert.True(q.Dismiss(b, SnackbarCloseReason.User));
        Assert.Equal(0, q.QueuedCount);
        Assert.False(q.Dismiss(b));
    }

    [Fact]
    public void Closed_fires_once_with_the_reason_and_changed_fires_after()
    {
        using var q = Queue();
        var events = new List<string>();
        q.Closed += (_, e) => events.Add($"closed:{e.Reason}");
        q.Changed += (_, _) => events.Add("changed");

        var s = q.Add("x");
        events.Clear();
        q.Dismiss(s, SnackbarCloseReason.User);
        q.Dismiss(s, SnackbarCloseReason.User);

        Assert.Equal(["closed:User", "changed"], events);
    }

    [Fact]
    public async Task Invoking_the_action_closes_first_then_runs_it_once()
    {
        using var q = Queue();
        var runs = 0;
        SnackbarState? stateDuringRun = null;
        Snackbar? s = null;
        s = q.Add(new SnackbarOptions
        {
            Message = "Deleted",
            Action = new("Undo", () => { runs++; stateDuringRun = s!.State; return Task.CompletedTask; }),
        });

        await q.InvokeActionAsync(s);
        await q.InvokeActionAsync(s);

        Assert.Equal(1, runs);
        Assert.Equal(SnackbarState.Closed, stateDuringRun);
        Assert.Equal(SnackbarCloseReason.Action, s.CloseReason);
    }

    [Fact]
    public void Clear_closes_visible_and_queued()
    {
        using var q = Queue(new SnackbarConfiguration { MaxVisible = 1 });
        var a = q.Add("a");
        var b = q.Add("b");
        q.Clear();

        Assert.Empty(q.Visible);
        Assert.Equal(0, q.QueuedCount);
        Assert.All(new[] { a, b }, s => Assert.Equal(SnackbarCloseReason.Cleared, s.CloseReason));

        _time.Advance(TimeSpan.FromMinutes(1)); // cancelled timers must not fire
        Assert.Equal(SnackbarCloseReason.Cleared, a.CloseReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_messages(string message)
    {
        using var q = Queue();
        Assert.Throws<ArgumentException>(() => q.Add(message));
    }

    [Fact]
    public void Rejects_non_positive_durations()
    {
        using var q = Queue();
        Assert.Throws<ArgumentOutOfRangeException>(() => q.Add(new SnackbarOptions { Message = "x", Duration = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SnackbarQueue(new SnackbarConfiguration { MaxVisible = 0 }));
    }

    [Fact]
    public void Disposed_queue_rejects_new_snackbars_and_stops_timers()
    {
        var q = Queue();
        var s = q.Add("x");
        q.Dispose();
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SnackbarState.Visible, s.State);
        Assert.Throws<ObjectDisposedException>(() => q.Add("y"));
    }

    [Fact]
    public async Task Concurrent_adds_and_dismissals_keep_invariants()
    {
        using var q = new SnackbarQueue(new SnackbarConfiguration { MaxVisible = 3, MaxQueued = 1000, PreventDuplicates = false });
        var all = new System.Collections.Concurrent.ConcurrentBag<Snackbar>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                var s = q.Add($"{t}-{i}");
                all.Add(s);
                if (i % 2 == 0) q.Dismiss(s);
            }
        })));

        Assert.True(q.Visible.Count <= 3);
        var open = all.Count(s => s.State != SnackbarState.Closed);
        Assert.Equal(q.Visible.Count + q.QueuedCount, open);
    }
}
