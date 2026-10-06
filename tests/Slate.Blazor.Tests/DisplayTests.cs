using Slate.Dialogs;
using Microsoft.AspNetCore.Components;

namespace Slate.Blazor.Tests;

public class DisplayTests : SlateTestContext
{
    [Fact]
    public void Icon_renders_contract_svg_and_is_decorative_by_default()
    {
        var cut = Render<SlIcon>(p => p.Add(x => x.Name, "check"));
        cut.MarkupMatches($"""
            <svg class="sl-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"
                 stroke-linejoin="round" focusable="false" aria-hidden="true"><path d="{SlateIcons.Check}" /></svg>
            """);
    }

    [Fact]
    public void Icon_with_label_is_an_image_with_a_name()
    {
        var svg = Render<SlIcon>(p => p.Add(x => x.Name, "info").Add(x => x.Label, "Info").Add(x => x.Size, ControlSize.Large)).Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Info", svg.GetAttribute("aria-label"));
        Assert.Null(svg.GetAttribute("aria-hidden"));
        Assert.Contains("sl-icon--lg", svg.ClassList);
    }

    [Fact]
    public void Unknown_icon_names_fail_loudly() =>
        Assert.Throws<ArgumentException>(() => Render<SlIcon>(p => p.Add(x => x.Name, "nope")));

    [Theory]
    [InlineData(Typo.H2, "h2", "sl-text--h2")]
    [InlineData(Typo.BodyStrong, "p", "sl-text--body-strong")]
    [InlineData(Typo.Caption, "span", "sl-text--caption")]
    [InlineData(Typo.Mono, "code", "sl-text--mono")]
    public void Text_picks_tag_and_variant_class(Typo typo, string tag, string cls)
    {
        var el = Render<SlText>(p => p.Add(x => x.Typo, typo).AddChildContent("Hi")).Nodes[0] as AngleSharp.Dom.IElement;
        Assert.Equal(tag, el!.LocalName);
        Assert.Contains(cls, el.ClassList);
    }

    [Fact]
    public void Text_tone_truncate_and_custom_tag()
    {
        var cut = Render<SlText>(p => p.Add(x => x.Tone, TextTone.Secondary).Add(x => x.Truncate, true).Add(x => x.Tag, "div").AddChildContent("x"));
        cut.MarkupMatches("""<div class="sl-text sl-text--body sl-text--secondary sl-text--truncate">x</div>""");
    }

    [Fact]
    public void Kbd() => Render<SlKbd>(p => p.AddChildContent("⌘K")).MarkupMatches("""<kbd class="sl-kbd">⌘K</kbd>""");

    [Fact]
    public void Badge_tone_and_dot() =>
        Render<SlBadge>(p => p.Add(x => x.Tone, Tone.Success).Add(x => x.Dot, true).AddChildContent("Ready"))
            .MarkupMatches("""<span class="sl-badge sl-badge--soft sl-tone-success"><span class="sl-badge__dot" aria-hidden="true"></span>Ready</span>""");

    [Fact]
    public void Badge_defaults_to_soft_neutral() =>
        Render<SlBadge>(p => p.AddChildContent("Draft")).MarkupMatches("""<span class="sl-badge sl-badge--soft sl-tone-neutral">Draft</span>""");

    [Theory]
    [InlineData(BadgeVariant.Solid, "sl-badge--solid")]
    [InlineData(BadgeVariant.Outlined, "sl-badge--outlined")]
    public void Badge_variants_size_and_icon(BadgeVariant variant, string cls)
    {
        var cut = Render<SlBadge>(p => p.Add(x => x.Variant, variant).Add(x => x.Size, BadgeSize.Small).Add(x => x.Icon, "check").AddChildContent("Ok"));
        var badge = cut.Find(".sl-badge");
        Assert.Contains(cls, badge.ClassList);
        Assert.Contains("sl-badge--small", badge.ClassList);
        Assert.NotNull(cut.Find("svg.sl-icon.sl-badge__icon"));
    }

    [Theory]
    [InlineData(Severity.Info, "sl-alert--info", "info", null)]
    [InlineData(Severity.Success, "sl-alert--success", "check-circle", null)]
    [InlineData(Severity.Warning, "sl-alert--warning", "alert-triangle", null)]
    [InlineData(Severity.Error, "sl-alert--error", "alert-circle", "alert")]
    public void Alert_severity_icon_and_role(Severity severity, string cls, string icon, string? role)
    {
        var cut = Render<SlAlert>(p => p.Add(x => x.Severity, severity).Add(x => x.Title, "Title").AddChildContent("Message"));
        var root = cut.Find(".sl-alert");
        Assert.Contains(cls, root.ClassList);
        Assert.Contains("sl-alert--soft", root.ClassList);
        Assert.Contains($"sl-tone-{(severity == Severity.Error ? "danger" : severity.ToString().ToLowerInvariant())}", root.ClassList);
        Assert.Equal(role, root.GetAttribute("role"));
        Assert.Equal(SlateIcons.All[icon], cut.Find(".sl-alert__icon path").GetAttribute("d"));
        Assert.Equal("Title", cut.Find(".sl-alert__title").TextContent);
        Assert.Equal("Message", cut.Find(".sl-alert__message").TextContent);
    }

    [Fact]
    public void Alert_variant_dense_and_icon_override()
    {
        var cut = Render<SlAlert>(p => p.Add(x => x.Variant, AlertVariant.Solid).Add(x => x.Dense, true).Add(x => x.Icon, "bell"));
        var root = cut.Find(".sl-alert");
        Assert.Contains("sl-alert--solid", root.ClassList);
        Assert.Contains("sl-alert--dense", root.ClassList);
        Assert.Equal(SlateIcons.Bell, cut.Find(".sl-alert__icon path").GetAttribute("d"));

        var none = Render<SlAlert>(p => p.Add(x => x.Icon, "none"));
        Assert.Empty(none.FindAll(".sl-alert__icon"));
    }

    [Fact]
    public void Normal_severity_alert_is_neutral() =>
        Assert.Contains("sl-tone-neutral", Render<SlAlert>(p => p.Add(x => x.Severity, Severity.Normal)).Find(".sl-alert").ClassList);

    [Fact]
    public void Live_alert_is_a_status()
    {
        var cut = Render<SlAlert>(p => p.Add(x => x.Severity, Severity.Success).Add(x => x.Live, true));
        Assert.Equal("status", cut.Find(".sl-alert").GetAttribute("role"));
    }

    [Fact]
    public void Dismissible_alert_closes_and_raises_event()
    {
        var dismissed = false;
        var cut = Render<SlAlert>(p => p.Add(x => x.Dismissible, true).Add(x => x.Dismissed, () => dismissed = true).AddChildContent("x"));
        var close = cut.Find(".sl-alert__close");
        Assert.Equal("Dismiss", close.GetAttribute("aria-label"));
        close.Click();
        Assert.True(dismissed);
        Assert.Empty(cut.FindAll(".sl-alert"));
    }

    [Fact]
    public void Progress_determinate_clamps_and_sets_value() =>
        Render<SlProgress>(p => p.Add(x => x.Value, 140).Add(x => x.Label, "Uploading"))
            .MarkupMatches("""
                <div class="sl-progress sl-tone-accent" style="--_value: 100%;" role="progressbar" aria-label="Uploading" aria-valuemin="0" aria-valuemax="100" aria-valuenow="100">
                  <div class="sl-progress__bar"></div>
                </div>
                """);

    [Fact]
    public void Progress_indeterminate_is_busy_without_value()
    {
        var bar = Render<SlProgress>(p => p.Add(x => x.Indeterminate, true).Add(x => x.Label, "Loading")).Find(".sl-progress");
        Assert.Contains("sl-progress--indeterminate", bar.ClassList);
        Assert.Equal("true", bar.GetAttribute("aria-busy"));
        Assert.Null(bar.GetAttribute("aria-valuenow"));
    }

    [Fact]
    public void Progress_tone_size_max_and_show_value()
    {
        var cut = Render<SlProgress>(p => p.Add(x => x.Value, 30).Add(x => x.Max, 60).Add(x => x.Tone, Tone.Success)
            .Add(x => x.Size, ControlSize.Large).Add(x => x.ShowValue, true).Add(x => x.Label, "Sync"));
        var bar = cut.Find(".sl-progress-row > .sl-progress");
        Assert.Contains("sl-tone-success", bar.ClassList);
        Assert.Contains("sl-progress--large", bar.ClassList);
        Assert.Equal("60", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("30", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("50%", cut.Find(".sl-progress__value").TextContent);
    }

    [Fact]
    public void Spinner() =>
        Render<SlSpinner>(p => p.Add(x => x.Size, ControlSize.Small))
            .MarkupMatches("""<span class="sl-spinner sl-spinner--small" role="status" aria-label="Loading"></span>""");

    [Fact]
    public void Spinner_tone_adds_tone_class_except_neutral() =>
        Assert.Contains("sl-tone-accent", Render<SlSpinner>(p => p.Add(x => x.Tone, Tone.Accent)).Find("span").ClassList);

    [Fact]
    public void Class_style_and_extra_attributes_pass_through()
    {
        var el = Render<SlBadge>(p => p.Add(x => x.Class, "mine").Add(x => x.Style, "margin: 0").AddUnmatched("data-x", "1")).Find("span");
        Assert.Contains("mine", el.ClassList);
        Assert.Equal("margin: 0", el.GetAttribute("style"));
        Assert.Equal("1", el.GetAttribute("data-x"));
    }
}
