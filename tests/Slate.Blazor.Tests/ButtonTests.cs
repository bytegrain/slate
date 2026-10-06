using Microsoft.AspNetCore.Components.Web;

namespace Slate.Blazor.Tests;

public class ButtonTests : SlateTestContext
{
    [Fact]
    public void Default_is_secondary_medium_type_button() =>
        Render<SlButton>(p => p.AddChildContent("Save"))
            .MarkupMatches("""<button type="button" class="sl-button"><span class="sl-button__label">Save</span></button>""");

    [Theory]
    [InlineData(ButtonVariant.Primary, "sl-button--primary")]
    [InlineData(ButtonVariant.Ghost, "sl-button--ghost")]
    [InlineData(ButtonVariant.Danger, "sl-button--danger")]
    [InlineData(ButtonVariant.DangerSolid, "sl-button--danger-solid")]
    [InlineData(ButtonVariant.Link, "sl-button--link")]
    public void Variants_map_to_modifiers(ButtonVariant variant, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Variant, variant)).Find("button").ClassList);

    [Theory]
    [InlineData(ControlSize.Small, "sl-button--sm")]
    [InlineData(ControlSize.Large, "sl-button--lg")]
    public void Sizes_map_to_modifiers(ControlSize size, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Size, size)).Find("button").ClassList);

    [Fact]
    public void Icons_and_shortcut()
    {
        var cut = Render<SlButton>(p => p.Add(x => x.StartIcon, "download").Add(x => x.EndIcon, "chevron-down")
            .Add(x => x.Shortcut, "⌘↵").AddChildContent("Export"));
        var button = cut.Find("button");
        Assert.Equal("⌘↵", button.GetAttribute("aria-keyshortcuts"));
        var icons = cut.FindAll("svg.sl-icon.sl-button__icon");
        Assert.Equal(2, icons.Count);
        Assert.Equal("true", cut.Find("kbd.sl-kbd").GetAttribute("aria-hidden"));
        // order: start icon, label, end icon, kbd
        Assert.Equal(["svg", "span", "svg", "kbd"], button.Children.Select(c => c.LocalName));
    }

    [Fact]
    public void Icon_only_requires_a_label()
    {
        Assert.Throws<InvalidOperationException>(() => Render<SlButton>(p => p.Add(x => x.IconOnly, true).Add(x => x.StartIcon, "plus")));
        var button = Render<SlButton>(p => p.Add(x => x.IconOnly, true).Add(x => x.StartIcon, "plus").Add(x => x.AriaLabel, "Add")).Find("button");
        Assert.Contains("sl-button--icon", button.ClassList);
        Assert.Equal("Add", button.GetAttribute("aria-label"));
        Assert.Empty(button.QuerySelectorAll(".sl-button__label"));
    }

    [Fact]
    public void Loading_replaces_start_icon_with_spinner_and_blocks_clicks()
    {
        var clicks = 0;
        var cut = Render<SlButton>(p => p.Add(x => x.Loading, true).Add(x => x.StartIcon, "check").Add(x => x.OnClick, () => clicks++).AddChildContent("Deploying"));
        var button = cut.Find("button");
        Assert.Contains("is-loading", button.ClassList);
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.NotNull(cut.Find("span.sl-spinner.sl-button__icon"));
        Assert.Empty(cut.FindAll("svg"));
        button.Click();
        Assert.Equal(0, clicks);
    }

    [Fact]
    public async Task Loading_while_busy_shows_spinner_until_handler_completes()
    {
        var gate = new TaskCompletionSource();
        var cut = Render<SlButton>(p => p.Add(x => x.LoadingWhileBusy, true).Add(x => x.OnClick, (MouseEventArgs _) => gate.Task).AddChildContent("Save"));

        var click = cut.Find("button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Contains("is-loading", cut.Find("button").ClassList));

        gate.SetResult();
        await click;
        cut.WaitForAssertion(() => Assert.DoesNotContain("is-loading", cut.Find("button").ClassList));
    }

    [Fact]
    public void Disabled_and_full_width()
    {
        var button = Render<SlButton>(p => p.Add(x => x.Disabled, true).Add(x => x.FullWidth, true)).Find("button");
        Assert.True(button.HasAttribute("disabled"));
        Assert.Contains("sl-button--full", button.ClassList);
    }

    [Fact]
    public void Submit_type_and_pressed_state()
    {
        var button = Render<SlButton>(p => p.Add(x => x.Type, ButtonType.Submit).Add(x => x.Pressed, true)).Find("button");
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Href_renders_a_link_and_disabled_link_loses_href()
    {
        var link = Render<SlButton>(p => p.Add(x => x.Href, "/docs").Add(x => x.Variant, ButtonVariant.Primary)).Find("a");
        Assert.Equal("/docs", link.GetAttribute("href"));
        Assert.Contains("sl-button--primary", link.ClassList);

        var disabled = Render<SlButton>(p => p.Add(x => x.Href, "/docs").Add(x => x.Disabled, true)).Find("a");
        Assert.Null(disabled.GetAttribute("href"));
        Assert.Equal("true", disabled.GetAttribute("aria-disabled"));
        Assert.Equal("-1", disabled.GetAttribute("tabindex"));
    }

    [Fact]
    public void Click_invokes_handler()
    {
        var clicks = 0;
        Render<SlButton>(p => p.Add(x => x.OnClick, () => clicks++)).Find("button").Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Button_group_has_group_role_and_label() =>
        Render<SlButtonGroup>(p => p.Add(x => x.Label, "View"))
            .MarkupMatches("""<div class="sl-button-group" role="group" aria-label="View"></div>""");
}
