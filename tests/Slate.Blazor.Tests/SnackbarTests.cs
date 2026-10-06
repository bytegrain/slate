using Slate.Snackbars;
using Slate.Blazor.Services;

namespace Slate.Blazor.Tests;

public class SnackbarTests : SlateTestContext
{
    protected override SnackbarConfiguration ConfigureSnackbars(SnackbarConfiguration c) => c with { MaxVisible = 2 };

    private IRenderedComponent<SlSnackbarHost> Host() => Render<SlSnackbarHost>();

    private static readonly TimeSpan Leave = TimeSpan.FromMilliseconds(SlateTokens.Motion.Duration.Fast);

    [Fact]
    public void Host_is_a_labelled_region_at_the_configured_position()
    {
        var host = Host().Find("section");
        Assert.Equal("sl-snackbar-host sl-snackbar-host--bottom-right", host.ClassName);
        Assert.Equal("Notifications", host.GetAttribute("aria-label"));
    }

    [Fact]
    public void Position_override() =>
        Assert.Contains("sl-snackbar-host--top-center",
            Render<SlSnackbarHost>(p => p.Add(x => x.PositionOverride, SnackbarPosition.TopCenter)).Find("section").ClassList);

    [Fact]
    public void Renders_contract_markup_for_a_titled_success_with_action()
    {
        var cut = Host();
        Snackbars.Add(new SnackbarOptions
        {
            Title = "Deployed", Message = "slate-web is live.", Severity = Severity.Success, Action = new("View"),
        });

        var s = cut.Find(".sl-snackbar");
        Assert.Equal("sl-snackbar sl-snackbar--success", s.ClassName);
        Assert.Equal("status", s.GetAttribute("role"));
        Assert.Equal("polite", s.GetAttribute("aria-live"));
        Assert.Equal("--_duration: 8000ms", s.GetAttribute("style")); // action ⇒ withAction minimum
        Assert.Equal(SlateIcons.Check, cut.Find(".sl-snackbar__icon path").GetAttribute("d"));
        Assert.Equal("Deployed", cut.Find(".sl-snackbar__title").TextContent);
        Assert.Equal("slate-web is live.", cut.Find(".sl-snackbar__message").TextContent);
        Assert.Equal("View", cut.Find(".sl-snackbar__action").TextContent);
        Assert.Equal("Dismiss", cut.Find(".sl-snackbar__close").GetAttribute("aria-label"));
        Assert.NotNull(cut.Find(".sl-snackbar__timer"));
    }

    [Theory]
    [InlineData(Severity.Warning, "alert", "assertive")]
    [InlineData(Severity.Error, "alert", "assertive")]
    [InlineData(Severity.Info, "status", "polite")]
    [InlineData(Severity.Normal, "status", "polite")]
    public void Urgency_follows_severity(Severity severity, string role, string live)
    {
        var cut = Host();
        Snackbars.Add("Message", severity);
        var s = cut.Find(".sl-snackbar");
        Assert.Equal(role, s.GetAttribute("role"));
        Assert.Equal(live, s.GetAttribute("aria-live"));
        Assert.Equal(severity == Severity.Normal, cut.FindAll(".sl-snackbar__icon").Count == 0);
    }

    [Fact]
    public void Sticky_snackbars_have_no_timer()
    {
        var cut = Host();
        Snackbars.Add(new SnackbarOptions { Message = "Offline", RequireInteraction = true });
        Assert.Empty(cut.FindAll(".sl-snackbar__timer"));
        Assert.Null(cut.Find(".sl-snackbar").GetAttribute("style"));
    }

    [Fact]
    public void Times_out_then_plays_exit_then_is_removed()
    {
        var cut = Host();
        Snackbars.Add("Saved");

        Time.Advance(TimeSpan.FromMilliseconds(SlateTokens.Snackbar.Duration.Default));
        cut.WaitForAssertion(() => Assert.Contains("is-leaving", cut.Find(".sl-snackbar").ClassList));

        Time.Advance(Leave);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".sl-snackbar")));
    }

    [Fact]
    public void Queues_beyond_max_visible_and_promotes()
    {
        var cut = Host();
        Snackbars.Add("a"); Snackbars.Add("b"); Snackbars.Add("c");
        Assert.Equal(["a", "b"], cut.FindAll(".sl-snackbar__message").Select(m => m.TextContent));

        cut.FindAll(".sl-snackbar__close")[0].Click();
        Time.Advance(Leave);
        cut.WaitForAssertion(() => Assert.Equal(["b", "c"], cut.FindAll(".sl-snackbar__message").Select(m => m.TextContent)));
    }

    [Fact]
    public void Hover_and_focus_pause_and_resume()
    {
        var cut = Host();
        Snackbar s = null!;
        s = Snackbars.Add(new SnackbarOptions { Message = "x", Duration = TimeSpan.FromSeconds(5) });

        cut.Find(".sl-snackbar").MouseEnter();
        Assert.True(s.IsPaused);
        cut.WaitForAssertion(() => Assert.Contains("is-paused", cut.Find(".sl-snackbar").ClassList));

        cut.Find(".sl-snackbar").FocusIn();
        cut.Find(".sl-snackbar").MouseLeave();
        Assert.True(s.IsPaused); // still focused

        Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SnackbarState.Visible, s.State);

        cut.Find(".sl-snackbar").FocusOut();
        Assert.False(s.IsPaused);
        Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(SnackbarState.Closed, s.State);
    }

    [Fact]
    public void Escape_dismisses_with_user_reason()
    {
        var cut = Host();
        Snackbar s = null!;
        s = Snackbars.Add("x");
        cut.Find(".sl-snackbar").KeyDown("Escape");
        Assert.Equal(SnackbarCloseReason.User, s.CloseReason);
    }

    [Fact]
    public void Action_runs_once_and_closes()
    {
        var cut = Host();
        var runs = 0;
        Snackbar s = null!;
        s = Snackbars.Add(new SnackbarOptions { Message = "Deleted", Action = SnackbarAction.Create("Undo", () => runs++) });

        cut.Find(".sl-snackbar__action").Click();
        Assert.Equal(1, runs);
        Assert.Equal(SnackbarCloseReason.Action, s.CloseReason);
        cut.WaitForAssertion(() => Assert.True(cut.Find(".sl-snackbar__action").HasAttribute("disabled"))); // leaving
    }

    [Fact]
    public void Extension_helpers_set_severity_and_title()
    {
        var s = Snackbars.Error("Build failed", "Slate.Wpf");
        Assert.Equal(Severity.Error, s.Severity);
        Assert.Equal("Slate.Wpf", s.Options.Title);
        Assert.Equal(Severity.Success, Snackbars.Success("ok").Severity);
    }

    [Fact]
    public void Queued_snackbars_that_are_cleared_do_not_animate()
    {
        var cut = Host();
        Snackbars.Add("a"); Snackbars.Add("b"); Snackbars.Add("queued"); Snackbars.Clear();
        cut.WaitForAssertion(() => Assert.Equal(["a", "b"], cut.FindAll(".sl-snackbar.is-leaving .sl-snackbar__message").Select(m => m.TextContent)));
    }
}
