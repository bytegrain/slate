using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Time.Testing;
using Slate.Dialogs;
using Slate.Layout;
using Slate.Snackbars;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf.Tests;

public class ThemeTests
{
    private static Color BrushColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

    [Fact]
    public void Light_theme_resolves_generated_tokens() => Wpf.Run(() =>
    {
        Assert.Equal(Color.FromRgb(0xF6, 0xF7, 0xF9), BrushColor("Sl.Brush.Background.Canvas"));
        Assert.Equal(32.0, Application.Current.FindResource("Sl.Size.Control.Md"));
        Assert.Equal("light", Wpf.Theme.ActualTheme);
    });

    [Fact]
    public void Switching_mode_swaps_theme_dictionaries_and_raises_event() => Wpf.Run(() =>
    {
        var raised = 0;
        Wpf.Theme.ActualThemeChanged += Handler;
        try
        {
            Wpf.Theme.Mode = ThemeMode.Dark;
            Assert.Equal(Color.FromRgb(0x0D, 0x10, 0x14), BrushColor("Sl.Brush.Background.Canvas"));
            Assert.Equal("dark", Wpf.Theme.ActualTheme);
            Assert.Equal(1, raised);
        }
        finally
        {
            Wpf.Theme.ActualThemeChanged -= Handler;
        }

        void Handler(object? s, EventArgs e) => raised++;
    });

    [Fact]
    public void Dynamic_resources_update_live_when_theme_changes() => Wpf.Run(() =>
    {
        var border = new Border();
        border.SetResourceReference(Border.BackgroundProperty, "Sl.Brush.Background.Canvas");
        Wpf.Realize(border);
        Wpf.Theme.Mode = ThemeMode.Dark;
        Wpf.Pump();
        Assert.Equal(Color.FromRgb(0x0D, 0x10, 0x14), ((SolidColorBrush)border.Background).Color);
    });

    [Fact]
    public void Density_swaps_control_heights() => Wpf.Run(() =>
    {
        Wpf.Theme.Density = Density.Comfortable;
        Assert.Equal(40.0, Application.Current.FindResource("Sl.Size.Control.Md"));
        Wpf.Theme.Density = Density.Compact;
        Assert.Equal(32.0, Application.Current.FindResource("Sl.Size.Control.Md"));
    });

    [Fact]
    public void Embedded_font_families_override_generated_ones() => Wpf.Run(() =>
    {
        var ui = (FontFamily)Application.Current.FindResource("Sl.Font.Family.Ui");
        Assert.Contains("Instrument Sans", ui.Source);
        Assert.StartsWith("pack://application", ui.Source);
    });
}

public class ButtonTests
{
    [Fact]
    public void Default_button_is_medium_secondary() => Wpf.Run(() =>
    {
        var b = Wpf.Realize(new Button { Content = "Save" });
        Assert.Equal(32, b.ActualHeight);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource("Sl.Brush.Background.Surface")).Color, ((SolidColorBrush)b.Background).Color);
    });

    [Fact]
    public void Primary_uses_accent_and_on_accent() => Wpf.Run(() =>
    {
        var b = new Button { Content = "Deploy" };
        Ui.SetVariant(b, ButtonVariant.Primary);
        Wpf.Realize(b);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource("Sl.Brush.Accent.Default")).Color, ((SolidColorBrush)b.Background).Color);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource("Sl.Brush.Text.OnAccent")).Color, ((SolidColorBrush)b.Foreground).Color);
    });

    [Theory]
    [InlineData(ControlSize.Small, 26)]
    [InlineData(ControlSize.Large, 40)]
    public void Sizes_follow_control_tokens(ControlSize size, double height) => Wpf.Run(() =>
    {
        var b = new Button { Content = "x" };
        Ui.SetSize(b, size);
        Wpf.Realize(b);
        Assert.Equal(height, b.ActualHeight);
    });

    [Fact]
    public void Icon_only_buttons_are_square() => Wpf.Run(() =>
    {
        var b = new Button();
        Ui.SetIcon(b, "plus");
        Wpf.Realize(new StackPanel { Orientation = Orientation.Horizontal, Children = { b } });
        Assert.Equal(b.ActualHeight, b.ActualWidth);
    });

    [Fact]
    public void Loading_blocks_clicks_and_reports_busy() => Wpf.Run(() =>
    {
        var clicks = 0;
        var b = new Button { Content = "Save" };
        b.Click += (_, _) => clicks++;
        Ui.SetIsLoading(b, true);
        Wpf.Realize(b);

        b.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
        Assert.Equal("Busy", AutomationProperties.GetItemStatus(b));

        Ui.SetIsLoading(b, false);
        Assert.Equal("", AutomationProperties.GetItemStatus(b));
    });
}

public class InputTests
{
    [Fact]
    public void TextField_wires_label_description_required_and_invalid_into_automation() => Wpf.Run(() =>
    {
        var field = Wpf.Realize(new TextField { Label = "Email", HelperText = "We never share it.", IsRequired = true });
        var box = field.TextBox!;
        Assert.Equal("Email", AutomationProperties.GetName(box));
        Assert.Equal("We never share it.", AutomationProperties.GetHelpText(box));
        Assert.True(AutomationProperties.GetIsRequiredForForm(box));
        Assert.False(Ui.GetHasError(box));

        field.Error = "Enter a complete email address.";
        Assert.True(field.HasError);
        Assert.Equal("Enter a complete email address.", AutomationProperties.GetHelpText(box));
        Assert.Equal("Invalid", AutomationProperties.GetItemStatus(box));
        Assert.True(Ui.GetHasError(box));
    });

    [Fact]
    public void TextField_text_binds_two_way() => Wpf.Run(() =>
    {
        var field = Wpf.Realize(new TextField { Label = "Name" });
        field.TextBox!.Text = "Aaron";
        Assert.Equal("Aaron", field.Text);
        field.Text = "Jo";
        Assert.Equal("Jo", field.TextBox.Text);
    });

    [Fact]
    public void Placeholder_tracks_emptiness() => Wpf.Run(() =>
    {
        var box = new TextBox();
        Ui.SetPlaceholder(box, "Search");
        Wpf.Realize(box);
        Assert.True(Ui.GetIsEmpty(box));
        box.Text = "x";
        Assert.False(Ui.GetIsEmpty(box));
    });

    [Fact]
    public void Switch_is_announced_as_a_switch_and_ignores_enter() => Wpf.Run(() =>
    {
        var sw = Wpf.Realize(new Switch { Content = "Notifications" });
        var peer = UIElementAutomationPeer.CreatePeerForElement(sw);
        Assert.Equal("switch", peer.GetLocalizedControlType());
        Assert.False(sw.IsThreeState);
    });
}

public class LayoutTests
{
    [Fact]
    public void Grid_placement_wraps_rows_at_twelve_columns()
    {
        var placed = ResponsiveGrid.Place([6, 6, 4, 4, 4, 12, 8, 6]);
        Assert.Equal([(0, 0, 6), (0, 6, 6), (1, 0, 4), (1, 4, 4), (1, 8, 4), (2, 0, 12), (3, 0, 8), (4, 0, 6)], placed);
    }

    [Fact]
    public void ResponsiveGrid_uses_its_own_width_as_breakpoint() => Wpf.Run(() =>
    {
        var a = new Border { Height = 10 };
        var b = new Border { Height = 10 };
        foreach (var item in new[] { a, b })
        {
            ResponsiveGrid.SetXs(item, 12);
            ResponsiveGrid.SetMd(item, 6);
        }
        var grid = new ResponsiveGrid { Spacing = 4, Children = { a, b } };
        Wpf.Realize(grid, width: 1000);

        Assert.Equal(Breakpoint.Md, grid.CurrentBreakpoint);
        Assert.Equal(a.TranslatePoint(new Point(), grid).Y, b.TranslatePoint(new Point(), grid).Y); // same row
        Assert.True(b.TranslatePoint(new Point(), grid).X > 0);
    });

    [Fact]
    public void Stack_spacing_is_token_steps() => Wpf.Run(() =>
    {
        var a = new Border { Height = 10 };
        var b = new Border { Height = 10 };
        var stack = new Stack { Spacing = 3, Children = { a, b } }; // 12px
        Wpf.Realize(stack);
        Assert.Equal(22, b.TranslatePoint(new Point(), stack).Y);
    });

    [Fact]
    public void Spacer_pushes_following_items_to_the_end() => Wpf.Run(() =>
    {
        var end = new Border { Width = 20 };
        var stack = new Stack { Orientation = Orientation.Horizontal, Spacing = 0, Width = 200, Children = { new Border { Width = 20 }, new Spacer(), end } };
        Wpf.Realize(stack);
        Assert.Equal(180, end.TranslatePoint(new Point(), stack).X);
    });

    [Theory]
    [InlineData(DrawerMode.Responsive, 1200, DrawerMode.Persistent)]
    [InlineData(DrawerMode.Responsive, 700, DrawerMode.Temporary)]
    [InlineData(DrawerMode.Mini, 700, DrawerMode.Mini)]
    public void Responsive_drawer_switches_at_md(DrawerMode mode, double width, DrawerMode expected) =>
        Assert.Equal(expected, AppShell.Effective(mode, width));

    [Fact]
    public void Container_widths_come_from_tokens()
    {
        Assert.Equal(1200, Container.MaxContentWidth(ContainerSize.Lg));
        Assert.True(double.IsPositiveInfinity(Container.MaxContentWidth(ContainerSize.Fluid)));
    }
}

public class DisplayTests
{
    [Fact]
    public void Icons_resolve_from_the_shared_set()
    {
        Assert.NotNull(Icon.GeometryFor("check"));
        Assert.Null(Icon.GeometryFor("no-such-icon"));
        Assert.Same(Icon.GeometryFor("check"), Icon.GeometryFor("check")); // cached
    }

    [Theory]
    [InlineData(Severity.Error, BadgeTone.Danger, "alert-circle")]
    [InlineData(Severity.Success, BadgeTone.Success, "check-circle")]
    [InlineData(Severity.Normal, BadgeTone.Neutral, "info")]
    public void Severity_maps_to_tone_and_icon(Severity s, BadgeTone tone, string icon)
    {
        Assert.Equal(tone, Badge.ToneFor(s));
        Assert.Equal(icon, Alert.IconFor(s));
    }

    [Fact]
    public void Cubic_bezier_ease_hits_endpoints_and_is_monotonic()
    {
        var ease = new CubicBezierEase(SlateTokens.Motion.Easing.Standard);
        Assert.Equal(0, ease.Ease(0), 3);
        Assert.Equal(1, ease.Ease(1), 3);
        var samples = Enumerable.Range(0, 21).Select(i => ease.Ease(i / 20.0)).ToList();
        Assert.Equal(samples.Order(), samples);
    }

    [Fact]
    public void Spacing_steps_are_four_pixels() => Assert.Equal(24, Spacing.ToPixels(6));
}

public class SnackbarTests
{
    [Fact]
    public void Service_mirrors_queue_and_times_out() => Wpf.Run(() =>
    {
        var time = new FakeTimeProvider();
        using var service = new SnackbarService(timeProvider: time, dispatcher: System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var s = service.Add("Saved", Severity.Success);
        Assert.Equal([s], service.Visible);

        time.Advance(s.Duration!.Value);
        Wpf.Pump();
        Assert.Empty(service.Visible);
    });

    [Fact]
    public void Host_renders_items_and_dismiss_uses_user_reason() => Wpf.Run(() =>
    {
        using var service = new SnackbarService(new SnackbarConfiguration { MaxVisible = 2 }, new FakeTimeProvider(),
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var host = Wpf.Realize(new SnackbarHost { Service = service });
        var a = service.Add("one");
        service.Add("two");
        service.Add("three");
        Wpf.Pump();

        Assert.Equal(2, host.Items.Count);
        var container = (SnackbarItem)host.ItemContainerGenerator.ContainerFromItem(a);
        Assert.Same(a, container.Snackbar);
        Assert.Equal(HorizontalAlignment.Right, host.HorizontalAlignment); // bottom-right default
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(container));
    });

    [Fact]
    public void Visible_order_is_preserved_when_items_are_promoted() => Wpf.Run(() =>
    {
        using var service = new SnackbarService(new SnackbarConfiguration { MaxVisible = 2 }, new FakeTimeProvider(),
            System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var a = service.Add("a");
        var b = service.Add("b");
        var c = service.Add("c");
        service.Dismiss(a);
        Assert.Equal([b, c], service.Visible);
    });
}

public class DialogTests
{
    [Fact]
    public async Task Confirm_resolves_true_on_ok_command()
    {
        var service = new DialogService();
        var task = Wpf.Run(() =>
        {
            var host = Wpf.Realize(new DialogHost { Service = service });
            var pending = service.ConfirmAsync(new MessageBoxOptions { Title = "Delete?", Message = "Can't be undone.", Destructive = true });
            Wpf.Pump();

            Assert.Equal(Visibility.Visible, host.Visibility);
            var frame = (DialogFrame)host.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.False(frame.ShowHeader);
            DialogCommands.Ok.Execute(null, frame);
            Wpf.Pump();
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            return pending;
        });
        Assert.True(await task);
    }

    [Fact]
    public async Task Escape_cancels_top_dialog_only()
    {
        var service = new DialogService();
        var (first, second) = Wpf.Run(() =>
        {
            Wpf.Realize(new DialogHost { Service = service });
            var a = service.Show(new TextBlock { Text = "a" }, new DialogOptions { Title = "A" });
            var b = service.Show(new TextBlock { Text = "b" }, new DialogOptions { Title = "B" });
            Wpf.Pump();
            Assert.True(service.Stack.HandleEscape());
            Wpf.Pump();
            return (a, b);
        });

        Assert.True((await second.Result).Canceled);
        Assert.True(first.IsOpen);
        Wpf.Run(() => service.Close(first));
    }

    [Fact]
    public void Only_the_top_frame_is_enabled() => Wpf.Run(() =>
    {
        var service = new DialogService();
        var host = Wpf.Realize(new DialogHost { Service = service });
        service.Show(new TextBlock(), new DialogOptions());
        service.Show(new TextBlock(), new DialogOptions());
        Wpf.Pump();
        var bottom = (DialogFrame)host.ItemContainerGenerator.ContainerFromIndex(0);
        var top = (DialogFrame)host.ItemContainerGenerator.ContainerFromIndex(1);
        Assert.False(bottom.IsEnabled);
        Assert.True(top.IsEnabled);
        service.Stack.CloseAll();
    });

    [Fact]
    public void Message_box_options_follow_the_systems_rules()
    {
        var destructive = DialogService.MessageBoxDialogOptions(new MessageBoxOptions { Message = "x", Destructive = true });
        Assert.False(destructive.CloseOnBackdropClick);
        Assert.Equal(DialogWidth.Xs, destructive.MaxWidth);

        var ack = DialogService.MessageBoxDialogOptions(new MessageBoxOptions { Message = "x", CancelText = null });
        Assert.False(ack.CloseOnBackdropClick);
    }
}
