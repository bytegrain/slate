namespace Slate.Blazor.Tests;

public class AvatarSkeletonTests : SlateTestContext
{
    [Fact]
    public void Avatar_shows_initials_tone_and_status()
    {
        var cut = Render<SlAvatar>(p => p.Add(x => x.Name, "Aaron Griffin").Add(x => x.Status, AvatarStatus.Online).Add(x => x.Size, ControlSize.Large));
        var root = cut.Find(".sl-avatar");
        Assert.Equal("img", root.GetAttribute("role"));
        Assert.Equal("Aaron Griffin, online", root.GetAttribute("aria-label"));
        Assert.Equal("online", root.GetAttribute("data-status"));
        Assert.Contains("sl-avatar--large", root.ClassList);
        Assert.Contains(Internal.Names.Tone(Slate.Collections.AvatarText.ToneFor("Aaron Griffin")), root.ClassList);
        Assert.Equal("AG", cut.Find(".sl-avatar__initials").TextContent);
        Assert.Contains("sl-avatar__status--online", cut.Find(".sl-avatar__status").ClassList);
    }

    [Fact]
    public void Avatar_image_falls_back_to_initials_on_error()
    {
        var cut = Render<SlAvatar>(p => p.Add(x => x.Name, "Ada Lovelace").Add(x => x.Image, "/missing.png").Add(x => x.Tone, Tone.Success));
        Assert.Contains("sl-tone-success", cut.Find(".sl-avatar").ClassList);
        Assert.Equal("", cut.Find(".sl-avatar__image").GetAttribute("alt"));
        cut.Find("img").TriggerEvent("onerror", EventArgs.Empty);
        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("AL", cut.Find(".sl-avatar__initials").TextContent);
    }

    [Fact]
    public void Avatar_group_wraps_avatars()
    {
        var cut = Render<SlAvatarGroup>(p => p.AddChildContent<SlAvatar>(a => a.Add(x => x.Name, "A B")));
        Assert.NotNull(cut.Find(".sl-avatar-group > .sl-avatar"));
    }

    [Fact]
    public void Skeleton_shapes_and_lines()
    {
        var circle = Render<SlSkeleton>(p => p.Add(x => x.Shape, SkeletonShape.Circle).Add(x => x.Width, "40px").Add(x => x.Height, "40px"));
        circle.MarkupMatches("""<span class="sl-skeleton sl-skeleton--circle is-animated" style="width:40px;height:40px" aria-hidden="true"></span>""");

        var lines = Render<SlSkeleton>(p => p.Add(x => x.Lines, 3).Add(x => x.Animated, false));
        var group = lines.Find(".sl-skeleton-group");
        Assert.Equal("true", group.GetAttribute("aria-hidden"));
        var blocks = lines.FindAll(".sl-skeleton--text");
        Assert.Equal(3, blocks.Count);
        Assert.Equal("width:60%", blocks[2].GetAttribute("style"));
        Assert.DoesNotContain("is-animated", blocks[0].ClassList);
    }
}
