namespace Slate.Blazor.Tests;

public class LayoutTests : SlateTestContext
{
    [Theory]
    [InlineData(ContainerWidth.Lg, "sl-container")]
    [InlineData(ContainerWidth.Sm, "sl-container sl-container--sm")]
    [InlineData(ContainerWidth.Fluid, "sl-container sl-container--fluid")]
    public void Container_widths(ContainerWidth width, string cls) =>
        Assert.Equal(cls, Render<SlContainer>(p => p.Add(x => x.Width, width)).Find("div").ClassName);

    [Fact]
    public void Grid_spacing_class_and_validation()
    {
        Assert.Equal("sl-grid sl-grid--spacing-4", Render<SlGrid>().Find("div").ClassName);
        Assert.Equal("sl-grid sl-grid--spacing-0-5", Render<SlGrid>(p => p.Add(x => x.Spacing, 0.5)).Find("div").ClassName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Render<SlGrid>(p => p.Add(x => x.Spacing, 7)));
    }

    [Fact]
    public void Item_emits_only_breakpoints_that_change()
    {
        var item = Render<SlItem>(p => p.Add(x => x.Xs, 12).Add(x => x.Md, 6).Add(x => x.Lg, 6)).Find("div");
        Assert.Equal("sl-grid__item sl-col-xs-12 sl-col-md-6", item.ClassName);
    }

    [Fact]
    public void Item_defaults_to_full_width_and_validates_spans()
    {
        Assert.Equal("sl-grid__item sl-col-xs-12", Render<SlItem>().Find("div").ClassName);
        Assert.Equal("sl-grid__item sl-col-xs-12 sl-col-lg-4", Render<SlItem>(p => p.Add(x => x.Lg, 4)).Find("div").ClassName);
        Assert.Throws<ArgumentOutOfRangeException>(() => Render<SlItem>(p => p.Add(x => x.Md, 13)));
    }

    [Fact]
    public void Stack_modifiers()
    {
        var stack = Render<SlStack>(p => p.Add(x => x.Row, true).Add(x => x.Spacing, 1.5).Add(x => x.Align, StackAlign.Center)
            .Add(x => x.Justify, StackJustify.Between).Add(x => x.Wrap, true).Add(x => x.Tag, "section")).Find("section");
        Assert.Equal("sl-stack sl-stack--row sl-stack--spacing-1-5 sl-stack--align-center sl-stack--justify-between sl-stack--wrap", stack.ClassName);
        Assert.Equal("sl-stack sl-stack--spacing-2", Render<SlStack>().Find("div").ClassName);
    }

    [Fact]
    public void Spacer_and_dividers()
    {
        Render<SlSpacer>().MarkupMatches("""<span class="sl-spacer"></span>""");
        Render<SlDivider>().MarkupMatches("""<hr class="sl-divider" />""");
        Render<SlDivider>(p => p.Add(x => x.Vertical, true))
            .MarkupMatches("""<div class="sl-divider sl-divider--vertical" role="separator" aria-orientation="vertical"></div>""");
    }

    [Fact]
    public void Card_with_title_actions_body_and_footer()
    {
        var cut = Render<SlCard>(p => p.Add(x => x.Title, "Deployments").Add(x => x.Subtitle, "Last 7 days")
            .Add(x => x.Actions, "<button>x</button>").AddChildContent("<p>Body</p>").Add(x => x.Footer, "<button>Done</button>"));
        cut.MarkupMatches("""
            <article class="sl-card">
              <header class="sl-card__header">
                <div class="sl-card__titles"><h3 class="sl-card__title">Deployments</h3><p class="sl-card__subtitle">Last 7 days</p></div>
                <div class="sl-card__actions"><button>x</button></div>
              </header>
              <div class="sl-card__body"><p>Body</p></div>
              <footer class="sl-card__footer"><button>Done</button></footer>
            </article>
            """);
    }

    [Fact]
    public void Card_variants_and_interactive_keyboard()
    {
        var clicks = 0;
        var cut = Render<SlCard>(p => p.Add(x => x.Outlined, true).Add(x => x.Interactive, true).Add(x => x.FlushBody, true)
            .Add(x => x.OnClick, () => clicks++).AddChildContent("x"));
        var card = cut.Find("article");
        Assert.Equal("sl-card sl-card--outlined sl-card--interactive", card.ClassName);
        Assert.Equal("0", card.GetAttribute("tabindex"));
        Assert.Contains("sl-card__body--flush", cut.Find(".sl-card__body").ClassList);
        card.KeyDown("Enter");
        card.Click();
        Assert.Equal(2, clicks);
    }

    [Fact]
    public void Card_omits_empty_regions() =>
        Render<SlCard>(p => p.AddChildContent("only body")).MarkupMatches("""<article class="sl-card"><div class="sl-card__body">only body</div></article>""");

    [Fact]
    public void Toolbar_has_role_and_label() =>
        Render<SlToolbar>(p => p.Add(x => x.Label, "Formatting").Add(x => x.Flat, true))
            .MarkupMatches("""<div class="sl-toolbar sl-toolbar--flat" role="toolbar" aria-label="Formatting"></div>""");

    [Fact]
    public void Main_flush() =>
        Render<SlMain>(p => p.Add(x => x.Flush, true)).MarkupMatches("""<main class="sl-main sl-main--flush"></main>""");
}
