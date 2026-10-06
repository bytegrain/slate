using Microsoft.AspNetCore.Components.Web;

namespace Slate.Blazor.Tests;

public class ButtonTests : SlateTestContext
{
    [Fact]
    public void Default_is_outlined_neutral_medium_type_button() =>
        Render<SlButton>(p => p.AddChildContent("Save"))
            .MarkupMatches("""<button type="button" class="sl-button sl-button--outlined sl-tone-neutral"><span class="sl-button__label">Save</span></button>""");

    [Theory]
    [InlineData(ButtonVariant.Outlined, "sl-button--outlined")]
    [InlineData(ButtonVariant.Solid, "sl-button--solid")]
    [InlineData(ButtonVariant.Soft, "sl-button--soft")]
    [InlineData(ButtonVariant.Ghost, "sl-button--ghost")]
    [InlineData(ButtonVariant.Link, "sl-button--link")]
    public void Variants_map_to_modifiers(ButtonVariant variant, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Variant, variant)).Find("button").ClassList);

    [Theory]
    [InlineData(Tone.Neutral, "sl-tone-neutral")]
    [InlineData(Tone.Accent, "sl-tone-accent")]
    [InlineData(Tone.Success, "sl-tone-success")]
    [InlineData(Tone.Warning, "sl-tone-warning")]
    [InlineData(Tone.Danger, "sl-tone-danger")]
    [InlineData(Tone.Info, "sl-tone-info")]
    public void Tones_map_to_tone_classes(Tone tone, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Tone, tone)).Find("button").ClassList);

    [Fact]
    public void Primary_action_is_solid_accent()
    {
        var classes = Render<SlButton>(p => p.Add(x => x.Variant, ButtonVariant.Solid).Add(x => x.Tone, Tone.Accent)).Find("button").ClassList;
        Assert.Contains("sl-button--solid", classes);
        Assert.Contains("sl-tone-accent", classes);
    }

    [Theory]
    [InlineData(ControlSize.Small, "sl-button--small")]
    [InlineData(ControlSize.Large, "sl-button--large")]
    public void Sizes_map_to_modifiers(ControlSize size, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Size, size)).Find("button").ClassList);

    [Fact]
    public void Medium_size_and_default_radius_emit_no_modifier()
    {
        var classes = Render<SlButton>(p => p.Add(x => x.Size, ControlSize.Medium).Add(x => x.Radius, Radius.Default)).Find("button").ClassList;
        Assert.DoesNotContain(classes, c => c.StartsWith("sl-radius-") || c is "sl-button--medium");
    }

    [Theory]
    [InlineData(Radius.None, "sl-radius-none")]
    [InlineData(Radius.Small, "sl-radius-small")]
    [InlineData(Radius.Full, "sl-radius-full")]
    public void Radius_override_adds_radius_class(Radius radius, string cls) =>
        Assert.Contains(cls, Render<SlButton>(p => p.Add(x => x.Radius, radius)).Find("button").ClassList);

    [Fact]
    public void Icons_and_shortcut_follow_the_contract_order()
    {
        var cut = Render<SlButton>(p => p.Add(x => x.StartIcon, "download").Add(x => x.EndIcon, "chevron-down")
            .Add(x => x.Shortcut, "⌘↵").AddChildContent("Export"));
        var button = cut.Find("button");
        Assert.Equal("⌘↵", button.GetAttribute("aria-keyshortcuts"));
        Assert.Equal(2, cut.FindAll("svg.sl-icon.sl-button__icon").Count);
        Assert.Equal("true", cut.Find("kbd.sl-kbd").GetAttribute("aria-hidden"));
        Assert.Equal(["sl-button__start", "sl-button__label", "sl-button__end", "sl-kbd"], button.Children.Select(c => c.ClassName));
    }

    [Fact]
    public void Empty_start_and_end_regions_are_omitted()
    {
        var button = Render<SlButton>(p => p.AddChildContent("Save")).Find("button");
        Assert.Empty(button.QuerySelectorAll(".sl-button__start, .sl-button__end"));
    }

    [Fact]
    public void Start_and_end_content_replace_icons()
    {
        var cut = Render<SlButton>(p => p
            .Add(x => x.StartIcon, "plus").Add(x => x.StartContent, b => b.AddMarkupContent(0, "<b class=\"s\"></b>"))
            .Add(x => x.EndContent, b => b.AddMarkupContent(0, "<i class=\"e\"></i>")).AddChildContent("Go"));
        Assert.NotNull(cut.Find(".sl-button__start b.s"));
        Assert.NotNull(cut.Find(".sl-button__end i.e"));
        Assert.Empty(cut.FindAll("svg"));
    }

    [Fact]
    public void Icon_only_requires_a_label()
    {
        Assert.Throws<InvalidOperationException>(() => Render<SlButton>(p => p.Add(x => x.IconOnly, true).Add(x => x.StartIcon, "plus")));
        var button = Render<SlButton>(p => p.Add(x => x.IconOnly, true).Add(x => x.StartIcon, "plus").Add(x => x.Label, "Add")).Find("button");
        Assert.Contains("sl-button--icon-only", button.ClassList);
        Assert.Equal("Add", button.GetAttribute("aria-label"));
        Assert.Empty(button.QuerySelectorAll(".sl-button__label"));
    }

    [Fact]
    public void Loading_replaces_start_icon_with_spinner_and_blocks_clicks()
    {
        var clicks = 0;
        var cut = Render<SlButton>(p => p.Add(x => x.Loading, true).Add(x => x.StartIcon, "check").Add(x => x.Click, () => clicks++).AddChildContent("Deploying"));
        var button = cut.Find("button");
        Assert.Contains("is-loading", button.ClassList);
        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.NotNull(cut.Find(".sl-button__start > span.sl-spinner.sl-button__icon"));
        Assert.Empty(cut.FindAll("svg"));
        button.Click();
        Assert.Equal(0, clicks);
    }

    [Fact]
    public async Task Loading_while_busy_shows_spinner_until_handler_completes()
    {
        var gate = new TaskCompletionSource();
        var cut = Render<SlButton>(p => p.Add(x => x.LoadingWhileBusy, true).Add(x => x.Click, (MouseEventArgs _) => gate.Task).AddChildContent("Save"));

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
    public void Href_renders_a_link_and_a_disabled_link_becomes_a_disabled_button()
    {
        var link = Render<SlButton>(p => p.Add(x => x.Href, "/docs").Add(x => x.Variant, ButtonVariant.Solid).Add(x => x.Target, "_blank")).Find("a");
        Assert.Equal("/docs", link.GetAttribute("href"));
        Assert.Equal("noreferrer noopener", link.GetAttribute("rel"));
        Assert.Contains("sl-button--solid", link.ClassList);

        var disabled = Render<SlButton>(p => p.Add(x => x.Href, "/docs").Add(x => x.Disabled, true));
        Assert.Empty(disabled.FindAll("a"));
        Assert.True(disabled.Find("button").HasAttribute("disabled"));
    }

    [Fact]
    public void Click_invokes_handler()
    {
        var clicks = 0;
        Render<SlButton>(p => p.Add(x => x.Click, () => clicks++)).Find("button").Click();
        Assert.Equal(1, clicks);
    }

    [Fact]
    public void Button_group_has_group_role_and_label() =>
        Render<SlButtonGroup>(p => p.Add(x => x.Label, "View"))
            .MarkupMatches("""<div class="sl-button-group" role="group" aria-label="View"></div>""");
}
