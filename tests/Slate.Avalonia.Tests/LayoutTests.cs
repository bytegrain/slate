using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Slate.Avalonia.Controls;
using Slate.Layout;
using static Slate.Avalonia.Tests.TestHelpers;

namespace Slate.Avalonia.Tests;

public class StackTests
{
    private static Border Box(double w, double h) => new() { Width = w, Height = h };

    [AvaloniaFact]
    public void Vertical_stack_uses_spacing_in_token_steps()
    {
        Border a = Box(50, 20), b = Box(50, 30);
        var stack = new Stack { Spacing = 3, Children = { a, b } }; // 3 steps = 12px
        var w = Show(stack);
        Assert.Equal(0, a.Bounds.Y);
        Assert.Equal(32, b.Bounds.Y);
        Assert.Equal(62, stack.DesiredSize.Height);
        w.Close();
    }

    [AvaloniaFact]
    public void Spacer_pushes_siblings_to_the_ends()
    {
        Border a = Box(40, 20), b = Box(60, 20);
        var stack = new Stack { Direction = Direction.Row, Width = 400, Spacing = 0, Children = { a, new Spacer(), b } };
        var w = Show(stack);
        Assert.Equal(0, a.Bounds.X);
        Assert.Equal(340, b.Bounds.X);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(StackJustify.Start, 0)]
    [InlineData(StackJustify.Center, 150)]
    [InlineData(StackJustify.End, 300)]
    public void Justify_positions_children_on_the_main_axis(StackJustify justify, double firstX)
    {
        Border a = Box(50, 20), b = Box(50, 20);
        var stack = new Stack { Direction = Direction.Row, Width = 400, Spacing = 0, Justify = justify, Children = { a, b } };
        var w = Show(stack);
        Assert.Equal(firstX, a.Bounds.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Space_between_spreads_the_free_space()
    {
        Border a = Box(50, 20), b = Box(50, 20), c = Box(50, 20);
        var stack = new Stack { Direction = Direction.Row, Width = 350, Spacing = 0, Justify = StackJustify.Between, Children = { a, b, c } };
        var w = Show(stack);
        Assert.Equal([0, 150, 300], new[] { a, b, c }.Select(x => x.Bounds.X));
        w.Close();
    }

    [AvaloniaFact]
    public void Align_items_centers_on_the_cross_axis()
    {
        Border small = Box(50, 10), tall = Box(50, 40);
        var stack = new Stack { Direction = Direction.Row, Align = StackAlign.Center, VerticalAlignment = VerticalAlignment.Top, Children = { small, tall } };
        var w = Show(stack);
        Assert.Equal(15, small.Bounds.Y);
        w.Close();
    }

    [AvaloniaFact]
    public void Wrap_flows_onto_new_lines()
    {
        var items = Enumerable.Range(0, 5).Select(_ => Box(100, 20)).ToList();
        var stack = new Stack { Direction = Direction.Row, Wrap = true, Width = 330, Spacing = 2 };
        foreach (var i in items) stack.Children.Add(i);
        var w = Show(stack);
        // 3 per line at 100 + 8 gap = 316 ≤ 330
        Assert.Equal(0, items[2].Bounds.Y);
        Assert.Equal(28, items[3].Bounds.Y);
        Assert.Equal(0, items[3].Bounds.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Collapsed_children_take_no_space_or_gap()
    {
        Border a = Box(50, 20), hidden = Box(50, 20), b = Box(50, 20);
        hidden.IsVisible = false;
        var stack = new Stack { Spacing = 2, Children = { a, hidden, b } };
        var w = Show(stack);
        Assert.Equal(28, b.Bounds.Y);
        w.Close();
    }
}

public class ResponsiveGridTests
{
    private static Border Cell(int? xs = null, int? md = null, int? lg = null)
    {
        var b = new Border { Height = 20 };
        ResponsiveGrid.SetXs(b, xs);
        ResponsiveGrid.SetMd(b, md);
        ResponsiveGrid.SetLg(b, lg);
        return b;
    }

    [AvaloniaFact]
    public void Items_stack_full_width_below_md_and_pair_up_from_md()
    {
        Border a = Cell(md: 6), b = Cell(md: 6);
        var grid = new ResponsiveGrid { Spacing = 4, Children = { a, b } };

        var narrow = Show(grid, width: 500);
        Assert.Equal(Breakpoint.Xs, grid.CurrentBreakpoint);
        Assert.Equal(500, a.Bounds.Width);
        Assert.Equal(36, b.Bounds.Y); // second row: 20 + 16 gap
        narrow.Content = null;
        narrow.Close();

        var wide = Show(grid, width: 1000);
        Assert.Equal(Breakpoint.Md, grid.CurrentBreakpoint);
        Assert.Equal(0, b.Bounds.Y);
        Assert.Equal(492, a.Bounds.Width); // (1000 - 16) / 2
        Assert.Equal(508, b.Bounds.X);
        wide.Close();
    }

    [AvaloniaFact]
    public void Spans_overflowing_a_row_wrap_and_larger_breakpoints_inherit()
    {
        Border a = Cell(md: 8), b = Cell(md: 8), c = Cell(lg: 4);
        var grid = new ResponsiveGrid { Spacing = 0, Children = { a, b, c } };
        var w = Show(grid, width: 1300); // Lg
        var cells = grid.Place(1300);
        Assert.Equal([0, 1, 1], cells.Select(x => x.Row));
        Assert.Equal(4, cells[2].Span);
        Assert.Equal(8, cells[0].Span); // Lg inherits Md
        w.Close();
    }

    [AvaloniaFact]
    public void Breakpoint_follows_the_grids_own_width_not_the_window()
    {
        Border a = Cell(md: 6), b = Cell(md: 6);
        var grid = new ResponsiveGrid { Width = 400, Children = { a, b } };
        var w = Show(grid, width: 1400);
        Assert.Equal(Breakpoint.Xs, grid.CurrentBreakpoint);
        Assert.True(b.Bounds.Y > 0);
        w.Close();
    }
}

public class ContainerTests
{
    [AvaloniaTheory]
    [InlineData(1600, ContainerWidth.Lg, 1200)]
    [InlineData(1000, ContainerWidth.Lg, 952)] // 1000 - 2 × 24
    [InlineData(500, ContainerWidth.Lg, 468)]  // phone gutter 16
    [InlineData(1600, ContainerWidth.Sm, 600)]
    [InlineData(2000, ContainerWidth.Fluid, 1952)]
    public void Max_width_and_gutters_follow_tokens(double windowWidth, ContainerWidth size, double expected)
    {
        var child = new Border { Height = 10 };
        var container = new Container { ContainerMaxWidth = size, Child = child };
        var w = Show(container, width: windowWidth);
        Assert.Equal(expected, child.Bounds.Width);
        Assert.Equal((windowWidth - expected) / 2, child.Bounds.X);
        w.Close();
    }
}

public class AppShellTests
{
    private static (AppShell Shell, Drawer Drawer, Border Content, AppBar Bar) Build(DrawerVariant variant = DrawerVariant.Responsive)
    {
        var drawer = new Drawer { Content = new Stack { Children = { new NavItem { Label = "Overview", Icon = "home", Active = true }, new NavItem { Label = "Files", Icon = "folder" } } } };
        var content = new Border();
        var bar = new AppBar { Title = "Slate" };
        return (new AppShell { AppBar = bar, Drawer = drawer, Content = content, DrawerVariant = variant }, drawer, content, bar);
    }

    [AvaloniaFact]
    public void Wide_responsive_shell_docks_a_full_drawer_beside_content()
    {
        var (shell, drawer, content, bar) = Build();
        var w = Show(shell, width: 1200);
        Assert.Equal(DrawerMode.Persistent, shell.DrawerMode);
        Assert.Equal(SlateTokens.Size.Drawer.Full, drawer.Bounds.Width);
        Assert.Equal(SlateTokens.Size.Drawer.Full, content.TranslatePoint(default, shell)!.Value.X);
        Assert.Equal(SlateTokens.Size.Appbar, bar.Bounds.Height);
        Assert.False(shell.Part<Border>("PART_Scrim").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Nav_item_tooltips_only_appear_in_a_mini_drawer()
    {
        var (shell, drawer, _, _) = Build();
        var w = Show(shell, width: 1200);
        var item = Assert.IsType<Stack>(drawer.Content).Children.OfType<NavItem>().First();
        Assert.Null(ToolTip.GetTip(item)); // the label is visible: no redundant tooltip

        shell.DrawerVariant = DrawerVariant.Mini;
        Pump(w);
        Assert.Equal("Overview", ToolTip.GetTip(item));
        w.Close();
    }

    [AvaloniaFact]
    public void Full_drawer_shows_nav_item_labels_and_mini_hides_them()
    {
        var (shell, drawer, _, _) = Build();
        var w = Show(shell, width: 1200);
        var item = Assert.IsType<Stack>(drawer.Content).Children.OfType<NavItem>().First();
        var label = item.Part<TextBlock>("PART_Label");
        Assert.False(drawer.IsMini);
        Assert.True(label.IsEffectivelyVisible, "label hidden in a full drawer");
        Assert.True(label.Bounds.Width > 20, $"label width {label.Bounds.Width}");

        shell.DrawerVariant = DrawerVariant.Mini;
        Pump(w);
        Assert.False(label.IsEffectivelyVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Narrow_responsive_shell_is_temporary_and_starts_closed()
    {
        var (shell, drawer, content, _) = Build();
        var w = Show(shell, width: 700);
        Assert.Equal(DrawerMode.Temporary, shell.DrawerMode);
        Assert.False(shell.DrawerOpen);
        Assert.False(drawer.IsEffectivelyVisible);
        Assert.Equal(0, content.TranslatePoint(default, shell)!.Value.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Temporary_drawer_opens_over_content_and_closes_on_scrim_and_escape()
    {
        var (shell, drawer, content, bar) = Build(DrawerVariant.Temporary);
        var w = Show(shell, width: 1200);
        var scrim = shell.Part<Border>("PART_Scrim");

        var menu = bar.Part<Button>("PART_MenuButton");
        w.Click(menu);
        Assert.True(shell.DrawerOpen);
        Assert.True(scrim.IsVisible);
        Assert.True(drawer.IsEffectivelyVisible);
        Assert.Equal(0, content.TranslatePoint(default, shell)!.Value.X); // overlay, no push
        Assert.True(drawer.IsKeyboardFocusWithin); // focus moved into the drawer

        w.Press(Key.Escape);
        Assert.False(shell.DrawerOpen);
        Assert.Same(menu, w.Focused()); // focus returns to the toggle

        w.Click(menu);
        Assert.True(shell.DrawerOpen);
        w.MoveTo(new Point(1000, 300));
        w.ClickAt(new Point(1000, 300));
        Assert.False(shell.DrawerOpen);
        w.Close();
    }

    [AvaloniaFact]
    public void Mini_drawer_is_an_icon_rail()
    {
        var (shell, drawer, content, _) = Build(DrawerVariant.Mini);
        var w = Show(shell, width: 1200);
        Assert.Equal(SlateTokens.Size.Drawer.Mini, drawer.Bounds.Width);
        Assert.True(drawer.IsMini);
        Assert.Equal(SlateTokens.Size.Drawer.Mini, content.TranslatePoint(default, shell)!.Value.X);
        var label = ((Stack)drawer.Content!).Children.OfType<NavItem>().First().Part<TextBlock>("PART_Label");
        Assert.False(label.IsVisible);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(DrawerVariant.Responsive, 899, DrawerMode.Temporary)]
    [InlineData(DrawerVariant.Responsive, 900, DrawerMode.Persistent)]
    [InlineData(DrawerVariant.Persistent, 300, DrawerMode.Persistent)]
    [InlineData(DrawerVariant.Mini, 2000, DrawerMode.Mini)]
    public void Mode_resolution(DrawerVariant variant, double width, DrawerMode expected) =>
        Assert.Equal(expected, AppShell.ResolveMode(variant, width, Breakpoint.Md));

    [AvaloniaFact]
    public void Nav_item_label_is_its_accessible_name()
    {
        var item = new NavItem { Label = "Deployments", Icon = "layers" };
        Assert.Equal("Deployments", global::Avalonia.Automation.AutomationProperties.GetName(item));
        Assert.Null(ToolTip.GetTip(item)); // tooltip only in a mini drawer (see Nav_item_tooltips_only_appear_in_a_mini_drawer)
    }
}
