using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Slate.Dialogs;
using Slate.Theming;

namespace Slate.Wpf.Tests;

/// <summary>Configurability on a live WPF tree: variant × tone, radius, field variants, themes, defaults, scoped density.</summary>
public class ConfigurabilityTests
{
    private static Color Resource(string key, FrameworkElement? scope = null) =>
        ((SolidColorBrush)(scope?.FindResource(key) ?? Application.Current.FindResource(key))).Color;

    private static Color Of(Brush? brush) => Assert.IsAssignableFrom<SolidColorBrush>(brush).Color;

    private static Button RealizeButton(ButtonVariant? variant = null, Tone? tone = null, Action<Button>? configure = null)
    {
        var b = new Button { Content = "Go" };
        if (variant is { } v) Sl.SetVariant(b, v);
        if (tone is { } t) Sl.SetTone(b, t);
        configure?.Invoke(b);
        return Wpf.Realize(b);
    }

    [Theory]
    [InlineData(ButtonVariant.Solid, Tone.Accent, "Sl.Brush.Accent.Default", "Sl.Brush.Text.OnAccent")]
    [InlineData(ButtonVariant.Solid, Tone.Danger, "Sl.Brush.Status.Danger.Solid", "Sl.Brush.Status.Danger.OnSolid")]
    [InlineData(ButtonVariant.Solid, Tone.Neutral, "Sl.Brush.Inverse.Background", "Sl.Brush.Inverse.Text")]
    [InlineData(ButtonVariant.Soft, Tone.Success, "Sl.Brush.Status.Success.Bg", "Sl.Brush.Status.Success.Fg")]
    [InlineData(ButtonVariant.Outlined, Tone.Neutral, "Sl.Brush.Background.Surface", "Sl.Brush.Text.Primary")]
    [InlineData(ButtonVariant.Outlined, Tone.Danger, "Sl.Brush.Background.Surface", "Sl.Brush.Status.Danger.Fg")]
    public void Variant_and_tone_pick_the_right_brushes(ButtonVariant variant, Tone tone, string background, string foreground) => Wpf.Run(() =>
    {
        var b = RealizeButton(variant, tone);
        Assert.Equal(Resource(background), Of(b.Background));
        Assert.Equal(Resource(foreground), Of(b.Foreground));
        Assert.Equal(variant, Sl.GetActualVariant(b));
    });

    [Fact]
    public void Ghost_is_transparent_until_hovered_and_link_has_no_chrome() => Wpf.Run(() =>
    {
        var ghost = RealizeButton(ButtonVariant.Ghost);
        Assert.Equal(Colors.Transparent, Of(ghost.Background));
        Assert.Equal(Resource("Sl.Brush.Background.Muted"), Of(Sl.GetHoverBackground(ghost)));

        var link = RealizeButton(ButtonVariant.Link);
        Assert.Equal(0, link.BorderThickness.Left);
        Assert.Equal(Resource("Sl.Brush.Text.Link"), Of(link.Foreground));
    });

    [Fact]
    public void Local_brushes_win_over_variant_brushes() => Wpf.Run(() =>
    {
        var b = RealizeButton(ButtonVariant.Solid, Tone.Accent, x => x.Background = Brushes.Orange);
        Assert.Equal(Colors.Orange, Of(b.Background));
    });

    [Theory]
    [InlineData(Radius.None, 0)]
    [InlineData(Radius.Small, 4)]
    [InlineData(Radius.Large, 8)]
    public void Radius_override_sets_corner_radius(Radius radius, double px) => Wpf.Run(() =>
    {
        var b = RealizeButton(configure: x => Sl.SetRadius(x, radius));
        Assert.Equal(px, Sl.GetActualCornerRadius(b).TopLeft);
    });

    [Fact]
    public void Default_radius_follows_the_component_token() => Wpf.Run(() =>
    {
        var b = RealizeButton();
        Assert.Equal((double)Application.Current.FindResource("Sl.Component.Button.Radius"), Sl.GetActualCornerRadius(b).TopLeft);
    });

    [Fact]
    public void Defaults_apply_to_unset_options_and_explicit_options_win() => Wpf.Run(() =>
    {
        SlateTheme.Defaults.Button.Variant = ButtonVariant.Soft;
        SlateTheme.Defaults.Button.Size = ControlSize.Large;

        var unset = RealizeButton();
        Assert.Equal(ButtonVariant.Soft, Sl.GetActualVariant(unset));
        Assert.Equal(40, unset.ActualHeight);

        var explicitButton = RealizeButton(ButtonVariant.Outlined, configure: x => Sl.SetSize(x, ControlSize.Small));
        Assert.Equal(ButtonVariant.Outlined, Sl.GetActualVariant(explicitButton));
        Assert.Equal(26, explicitButton.ActualHeight);
    });

    [Fact]
    public void Field_variants_and_defaults() => Wpf.Run(() =>
    {
        var underlined = Wpf.Realize(new TextField { Label = "Name", Variant = FieldVariant.Underlined });
        Assert.Equal(FieldVariant.Underlined, underlined.ActualVariant);
        Assert.Equal(0, Sl.GetActualCornerRadius(underlined).TopLeft);

        SlateTheme.Defaults.Field.Variant = FieldVariant.Filled;
        var filled = Wpf.Realize(new TextField { Label = "Name" });
        Assert.Equal(FieldVariant.Filled, filled.ActualVariant);
        Assert.Equal(Resource("Sl.Component.Field.FilledBackground.Brush"), Of(Sl.GetChromeBackground(filled)));
    });

    [Fact]
    public void Text_field_counter_clear_and_password() => Wpf.Run(() =>
    {
        var field = Wpf.Realize(new TextField { Label = "Code", Counter = true, MaxLength = 10, Clearable = true, Value = "abc" });
        Assert.Equal("3 / 10", field.CounterText);
        Assert.True(field.ShowClear);
        field.Clear();
        Assert.Equal("", field.Value);
        Assert.False(field.ShowClear);

        var password = Wpf.Realize(new TextField { Label = "Password", InputType = InputType.Password });
        password.Value = "hunter2";
        var box = Assert.IsType<PasswordBox>(password.Template.FindName("PART_PasswordBox", password));
        Assert.Equal("hunter2", box.Password);
    });

    [Fact]
    public void Selection_tone_drives_the_checked_fill() => Wpf.Run(() =>
    {
        var accent = Wpf.Realize(new CheckBox { IsChecked = true });
        Assert.Equal(Resource("Sl.Component.Selection.Checked.Brush"), Of(Sl.GetFill(accent)));

        var danger = new CheckBox { IsChecked = true };
        Sl.SetTone(danger, Tone.Danger);
        Wpf.Realize(danger);
        Assert.Equal(Resource("Sl.Brush.Status.Danger.Solid"), Of(Sl.GetFill(danger)));
    });

    [Fact]
    public void Label_placement_defaults_to_end_and_can_move_to_start() => Wpf.Run(() =>
    {
        var sw = Wpf.Realize(new Switch { Content = "Wi-Fi" });
        Assert.Equal(Placement.End, Sl.GetActualLabelPlacement(sw));
        sw.LabelPlacement = Placement.Start;
        Assert.Equal(Placement.Start, Sl.GetActualLabelPlacement(sw));

        SlateTheme.Defaults.Selection.LabelPlacement = Placement.Start;
        var check = Wpf.Realize(new CheckBox { Content = "x" });
        Assert.Equal(Placement.Start, Sl.GetActualLabelPlacement(check));
    });

    [Fact]
    public void Badge_and_alert_variants() => Wpf.Run(() =>
    {
        var solid = Wpf.Realize(new Badge { Tone = Tone.Info, Variant = BadgeVariant.Solid, Content = "x" });
        Assert.Equal(Resource("Sl.Brush.Status.Info.Solid"), Of(solid.Background));

        var alert = Wpf.Realize(new Alert { Severity = Severity.Error, Variant = AlertVariant.Solid, Title = "Failed" });
        Assert.Equal(Resource("Sl.Brush.Status.Danger.Solid"), Of(alert.Background));
        Assert.Equal(Resource("Sl.Brush.Status.Danger.OnSolid"), Of(Sl.GetStrongForeground(alert)));
        alert.Icon = "none";
        Assert.Null(alert.ActualIcon);
    });

    [Fact]
    public void Card_variant_defaults_and_click() => Wpf.Run(() =>
    {
        SlateTheme.Defaults.Card.Variant = CardVariant.Outlined;
        var card = Wpf.Realize(new Card { Title = "x", Interactive = true });
        Assert.Equal(CardVariant.Outlined, card.ActualVariant);
        Assert.True(card.Focusable);

        var clicks = 0;
        card.Click += (_, _) => clicks++;
        card.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(card)!, 0, System.Windows.Input.Key.Enter)
        { RoutedEvent = UIElement.KeyDownEvent, Source = card });
        Assert.Equal(1, clicks);
    });

    [Fact]
    public void Custom_theme_overrides_accent_live_and_reset_restores_alloy() => Wpf.Run(() =>
    {
        var b = RealizeButton(ButtonVariant.Solid, Tone.Accent);
        var alloy = Of(b.Background);

        Wpf.Theme.Options = new SlateThemeOptions { Accent = "#5B3DF5" };
        Wpf.Pump();
        var expected = SlateColor.Parse(Wpf.Theme.AppliedDefinition!.Values["color.accent.default"]);
        Assert.Equal(Color.FromRgb(expected.R, expected.G, expected.B), Of(b.Background));

        Wpf.Theme.ResetTheme();
        Wpf.Pump();
        Assert.Equal(alloy, Of(b.Background));
    });

    [Fact]
    public void Custom_theme_is_rebuilt_for_dark_mode() => Wpf.Run(() =>
    {
        Wpf.Theme.Options = new SlateThemeOptions { Accent = "#FF5A1F" };
        var light = Wpf.Theme.AppliedDefinition!;
        Wpf.Theme.Mode = ThemeMode.Dark;
        var dark = Wpf.Theme.AppliedDefinition!;
        Assert.Equal("light", light.BaseTheme);
        Assert.Equal("dark", dark.BaseTheme);
        var expected = SlateColor.Parse(dark.Values["color.accent.default"]);
        Assert.Equal(Color.FromRgb(expected.R, expected.G, expected.B), Resource("Sl.Brush.Accent.Default"));
    });

    [Fact]
    public void Radius_scale_and_component_overrides_reach_controls() => Wpf.Run(() =>
    {
        Wpf.Theme.Options = new SlateThemeOptions
        {
            RadiusScale = 2,
            Overrides = new Dictionary<string, string> { ["component.card.radius"] = "20px" },
        };
        Assert.Equal(new CornerRadius(12), Application.Current.FindResource("Sl.Component.Button.Radius.Corner"));
        var b = RealizeButton();
        Assert.Equal(12, Sl.GetActualCornerRadius(b).TopLeft);
        Assert.Equal(new CornerRadius(20), Application.Current.FindResource("Sl.Component.Card.Radius.Corner"));
    });

    [Fact]
    public void Theme_resources_follow_the_naming_rules()
    {
        var dict = new ResourceDictionary();
        ThemeResources.Add(dict, "color.accent.default", "#123456");
        ThemeResources.Add(dict, "component.field.background", "#FFFFFF");
        ThemeResources.Add(dict, "radius.md", "9px");
        ThemeResources.Add(dict, "component.button.paddingMd", "14px");
        ThemeResources.Add(dict, "font.family.ui", "\"Inter\", sans-serif");
        ThemeResources.Add(dict, "component.card.shadow", "0px 1px 2px 0px #12161C0F, 0px 0px 0px 1px #E1E5EA");

        Assert.IsType<Color>(dict["Sl.Color.Accent.Default"]);
        Assert.IsType<SolidColorBrush>(dict["Sl.Brush.Accent.Default"]);
        Assert.IsType<SolidColorBrush>(dict["Sl.Component.Field.Background.Brush"]);
        Assert.Equal(new CornerRadius(9), dict["Sl.CornerRadius.Md"]);
        Assert.Equal(new Thickness(14), dict["Sl.Component.Button.PaddingMd.Thickness"]);
        Assert.Equal("Inter", ((FontFamily)dict["Sl.Font.Family.Ui"]).Source);
        Assert.IsType<System.Windows.Media.Effects.DropShadowEffect>(dict["Sl.Component.Card.Shadow.Effect"]);
    }

    [Fact]
    public void Scoped_density_changes_controls_inside_only() => Wpf.Run(() =>
    {
        var inside = new Button { Content = "in" };
        var outside = new Button { Content = "out" };
        var scope = new StackPanel { Children = { inside } };
        Sl.SetDensity(scope, Density.Comfortable);
        Wpf.Realize(new StackPanel { Children = { scope, outside } });

        Assert.Equal(40, inside.ActualHeight);
        Assert.Equal(32, outside.ActualHeight);
        Assert.Equal(Density.Comfortable, Sl.GetDensity(inside)); // inherited
    });

    [Fact]
    public void Dialog_content_options_override_dialog_options() => Wpf.Run(() =>
    {
        var service = new DialogService();
        var host = Wpf.Realize(new DialogHost { Service = service });
        service.Show(new DialogContent { Title = "From content", Tone = Tone.Danger, Icon = "trash", CloseOnEscape = false },
            new DialogOptions { Title = "From options" });
        Wpf.Pump();
        var frame = (DialogFrame)host.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.Equal("From content", frame.EffectiveOptions.Title);
        Assert.Equal(Tone.Danger, frame.EffectiveOptions.Tone);
        Assert.False(frame.EffectiveOptions.CloseOnEscape);
        Assert.Equal(Resource("Sl.Brush.Status.Danger.Bg"), Of(Sl.GetFill(frame)));
        service.Stack.CloseAll();
    });

    [Fact]
    public void Snackbar_host_position_overrides_the_service() => Wpf.Run(() =>
    {
        var host = Wpf.Realize(new SnackbarHost { Position = Slate.Snackbars.SnackbarPosition.TopCenter });
        Assert.Equal(HorizontalAlignment.Center, host.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Top, host.VerticalAlignment);
    });
}
