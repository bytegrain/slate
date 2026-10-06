using Slate.Dialogs;
using Slate.Layout;

namespace Slate.Core.Tests;

public class DialogStackTests
{
    [Fact]
    public void Push_adds_to_top_and_raises_changed()
    {
        var stack = new DialogStack();
        var changes = 0;
        stack.Changed += (_, _) => changes++;

        var a = stack.Push("a");
        var b = stack.Push("b");

        Assert.Equal([a, b], stack.Open);
        Assert.Same(b, stack.Top);
        Assert.Equal(2, changes);
        Assert.True(a.IsOpen);
    }

    [Fact]
    public async Task Close_completes_the_result()
    {
        var stack = new DialogStack();
        var d = stack.Push("content");

        Assert.True(stack.Close(d, DialogResult.Ok(42)));
        var result = await d.Result;

        Assert.False(result.Canceled);
        Assert.Equal(42, result.GetData<int>());
        Assert.False(d.IsOpen);
        Assert.Null(stack.Top);
    }

    [Fact]
    public async Task Close_without_result_cancels_and_second_close_is_ignored()
    {
        var stack = new DialogStack();
        var d = stack.Push("x");
        stack.Close(d);
        Assert.False(stack.Close(d, DialogResult.Ok()));
        Assert.True((await d.Result).Canceled);
    }

    [Fact]
    public void Escape_only_closes_the_top_dialog_when_allowed()
    {
        var stack = new DialogStack();
        var bottom = stack.Push("a");
        var locked = stack.Push("b", new DialogOptions { CloseOnEscape = false });

        Assert.False(stack.HandleEscape());
        Assert.Same(locked, stack.Top);

        stack.Close(locked);
        Assert.True(stack.HandleEscape());
        Assert.False(bottom.IsOpen);
        Assert.False(stack.HandleEscape()); // nothing open
    }

    [Fact]
    public void Backdrop_click_only_affects_the_top_dialog_that_allows_it()
    {
        var stack = new DialogStack();
        var a = stack.Push("a");
        var b = stack.Push("b", new DialogOptions { CloseOnBackdropClick = false });

        Assert.False(stack.HandleBackdropClick(a)); // not on top
        Assert.False(stack.HandleBackdropClick(b)); // disallowed
        stack.Close(b);
        Assert.True(stack.HandleBackdropClick(a));
    }

    [Fact]
    public async Task Close_all_cancels_everything_top_down()
    {
        var stack = new DialogStack();
        var a = stack.Push("a");
        var b = stack.Push("b");
        stack.CloseAll();

        Assert.Empty(stack.Open);
        Assert.True((await a.Result).Canceled);
        Assert.True((await b.Result).Canceled);
    }

    [Fact]
    public void Result_continuations_do_not_run_inline_with_close()
    {
        // Continuations of Result run asynchronously, so a dialog closing can't re-enter the stack mid-update.
        var stack = new DialogStack();
        var d = stack.Push("x");
        var ranInline = false;
        var closing = true;
        d.Result.ContinueWith(_ => ranInline = closing, TaskContinuationOptions.ExecuteSynchronously);
        stack.Close(d);
        closing = false;
        Assert.False(ranInline);
    }

    [Theory]
    [InlineData(DialogWidth.Xs, 360)]
    [InlineData(DialogWidth.Sm, 480)]
    [InlineData(DialogWidth.Xl, 1120)]
    public void Dialog_widths_come_from_tokens(DialogWidth width, double px) =>
        Assert.Equal(px, DialogOptions.WidthPixels(width));

    [Fact]
    public void Result_get_data_returns_default_for_wrong_type() =>
        Assert.Null(DialogResult.Ok("text").GetData<int?>());
}

public class BreakpointTests
{
    [Theory]
    [InlineData(0, Breakpoint.Xs)]
    [InlineData(599.9, Breakpoint.Xs)]
    [InlineData(600, Breakpoint.Sm)]
    [InlineData(899, Breakpoint.Sm)]
    [InlineData(900, Breakpoint.Md)]
    [InlineData(1200, Breakpoint.Lg)]
    [InlineData(1536, Breakpoint.Xl)]
    [InlineData(4000, Breakpoint.Xl)]
    public void Width_maps_to_breakpoint(double width, Breakpoint expected) =>
        Assert.Equal(expected, Breakpoints.FromWidth(width));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Invalid_widths_throw(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Breakpoints.FromWidth(width));

    [Fact]
    public void Container_is_fluid_on_phones()
    {
        Assert.Null(Breakpoints.ContainerMaxWidth(Breakpoint.Xs));
        Assert.Equal(1440, Breakpoints.ContainerMaxWidth(Breakpoint.Xl));
    }

    [Fact]
    public void Grid_spans_inherit_upwards_mobile_first()
    {
        var span = new GridSpan(Xs: 12, Md: 6);
        Assert.Equal(12, span.Resolve(Breakpoint.Xs));
        Assert.Equal(12, span.Resolve(Breakpoint.Sm));
        Assert.Equal(6, span.Resolve(Breakpoint.Md));
        Assert.Equal(6, span.Resolve(Breakpoint.Xl));
    }

    [Fact]
    public void Unset_grid_span_is_full_width()
    {
        Assert.Equal(12, new GridSpan().Resolve(Breakpoint.Lg));
        Assert.Equal(4, new GridSpan(Lg: 4).Resolve(Breakpoint.Xl));
        Assert.Equal(12, new GridSpan(Lg: 4).Resolve(Breakpoint.Md));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Grid_span_must_be_1_to_12(int span) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridSpan(Xs: span).Resolve(Breakpoint.Xs));
}

public class ThemingTests
{
    [Theory]
    [InlineData(ThemeMode.Light, true, "light")]
    [InlineData(ThemeMode.Dark, false, "dark")]
    [InlineData(ThemeMode.System, true, "dark")]
    [InlineData(ThemeMode.System, false, "light")]
    public void Theme_mode_resolution(ThemeMode mode, bool systemDark, string expected) =>
        Assert.Equal(expected, ThemeNames.Resolve(mode, systemDark));

    [Fact]
    public void Generated_tokens_expose_both_themes() =>
        Assert.Equal(["light", "dark"], SlateTokens.ThemeNames);

    [Fact]
    public void Text_style_derives_pixel_metrics()
    {
        var h1 = SlateTokens.Typography.H1;
        Assert.Equal(36, h1.FontSize);
        Assert.Equal(41.4, h1.LineHeightPixels);
        Assert.Equal(-1.08, h1.LetterSpacingPixels);
    }
}
