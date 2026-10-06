using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Services;
using Slate.Dialogs;
using Slate.Theming;
using static Slate.Avalonia.Tests.TestHelpers;
using L = Slate.SlateTokens.Themes.Light.Color;

namespace Slate.Avalonia.Tests;

/// <summary>Per-instance options: Variant × Tone, radius, field variants, selection options.</summary>
public class ParameterTests
{
    private static Button Btn(ButtonVariant variant, Tone tone)
    {
        var b = new Button { Content = "x" };
        Sl.SetVariant(b, variant);
        Sl.SetTone(b, tone);
        return b;
    }

    [AvaloniaTheory]
    [InlineData(Tone.Accent, L.Accent.Default, L.Text.OnAccent)]
    [InlineData(Tone.Danger, L.Status.Danger.Solid, L.Status.Danger.OnSolid)]
    [InlineData(Tone.Success, L.Status.Success.Solid, L.Status.Success.OnSolid)]
    [InlineData(Tone.Info, L.Status.Info.Solid, L.Status.Info.OnSolid)]
    [InlineData(Tone.Neutral, L.Text.Primary, L.Background.Surface)]
    public void Solid_buttons_fill_with_their_tone(Tone tone, string fill, string on)
    {
        var b = Btn(ButtonVariant.Solid, tone);
        var w = Show(b);
        Assert.Equal(Token(fill), ColorOf(b.Background));
        Assert.Equal(Token(on), ColorOf(b.Foreground));
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(Tone.Accent, L.Accent.Subtle, L.Accent.Text)]
    [InlineData(Tone.Warning, L.Status.Warning.Bg, L.Status.Warning.Fg)]
    public void Soft_buttons_use_the_tone_tint(Tone tone, string bg, string fg)
    {
        var b = Btn(ButtonVariant.Soft, tone);
        var w = Show(b);
        Assert.Equal(Token(bg), ColorOf(b.Background));
        Assert.Equal(Token(fg), ColorOf(b.Foreground));
        w.Close();
    }

    [AvaloniaFact]
    public void Outlined_and_ghost_keep_neutral_chrome_with_tone_text()
    {
        var outlined = Btn(ButtonVariant.Outlined, Tone.Danger);
        var ghost = Btn(ButtonVariant.Ghost, Tone.Accent);
        var w = Show(new StackPanel { Children = { outlined, ghost } });
        Assert.Equal(Token(L.Background.Surface), ColorOf(outlined.Background));
        Assert.Equal(Token(L.Status.Danger.Fg), ColorOf(outlined.Foreground));
        Assert.Equal(Colors.Transparent, ColorOf(ghost.Background));
        Assert.Equal(Token(L.Accent.Text), ColorOf(ghost.Foreground));
        w.Close();
    }

    [AvaloniaFact]
    public void Plain_classes_still_work_without_attached_properties()
    {
        var b = new Button { Content = "x", Classes = { "solid", "tone-accent" } };
        var w = Show(b);
        Assert.Equal(Token(L.Accent.Default), ColorOf(b.Background));
        w.Close();
    }

    [AvaloniaFact]
    public void End_icon_icon_only_label_and_pressed()
    {
        var b = new Button();
        Sl.SetStartIcon(b, "plus");
        Sl.SetIconOnly(b, true);
        Sl.SetLabel(b, "Add");
        var toggle = new Button { Content = "Bold" };
        Sl.SetPressed(toggle, true);
        var end = new Button { Content = "Next" };
        Sl.SetEndIcon(end, "arrow-right");
        var w = Show(new StackPanel { Children = { b, toggle, end } });

        Assert.Contains("icon-only", b.Classes);
        Assert.Equal(b.Bounds.Height, b.Bounds.Width);
        Assert.Equal("Add", global::Avalonia.Automation.AutomationProperties.GetName(b));
        Assert.Contains("pressed", toggle.Classes);
        Assert.Equal(Token(L.Accent.Subtle), ColorOf(toggle.Background));
        Assert.True(end.Part<Controls.Icon>("PART_EndIcon").IsVisible);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(Radius.None, 0)]
    [InlineData(Radius.Small, 4)]
    [InlineData(Radius.Large, 8)]
    [InlineData(Radius.Full, 999)]
    public void Radius_overrides_beat_component_tokens(Radius radius, double px)
    {
        var b = new Button { Content = "x" };
        Sl.SetRadius(b, radius);
        var card = new Card { Radius = radius, Content = "c" };
        var field = new TextField { Radius = radius };
        var w = Show(new StackPanel { Children = { b, card, field } });
        Assert.Equal(new CornerRadius(px), b.CornerRadius);
        Assert.Equal(new CornerRadius(px), card.CornerRadius);
        Assert.Equal(new CornerRadius(px), field.Input!.CornerRadius);
        w.Close();
    }

    [AvaloniaFact]
    public void Full_width_button_stretches()
    {
        var b = new Button { Content = "Wide" };
        Sl.SetFullWidth(b, true);
        var w = Show(new StackPanel { Width = 300, Children = { b } });
        Assert.Equal(300, b.Bounds.Width);
        w.Close();
    }

    [AvaloniaFact]
    public void Field_variants_restyle_the_input()
    {
        var filled = new TextField { Variant = FieldVariant.Filled };
        var underlined = new TextField { Variant = FieldVariant.Underlined };
        var small = new TextField { Size = ControlSize.Small };
        var w = Show(new StackPanel { Children = { filled, underlined, small } });
        Assert.Equal(Token(L.Background.Muted), ColorOf(filled.Input!.Background));
        Assert.Equal(new Thickness(0, 0, 0, 1), underlined.Input!.BorderThickness);
        Assert.Equal(new CornerRadius(0), underlined.Input.CornerRadius);
        Assert.Equal(26, small.Input!.Bounds.Height);
        w.Close();
    }

    [AvaloniaFact]
    public void Text_field_counter_clear_max_length_and_password()
    {
        var f = new TextField { Counter = true, MaxLength = 10, Clearable = true, Value = "hello", EndIcon = "search" };
        var pw = new TextField { InputType = "password", Value = "secret" };
        var w = Show(new StackPanel { Children = { f, pw } });

        Assert.Equal("5 / 10", f.CounterText);
        Assert.True(f.Part<TextBlock>("PART_Counter").IsVisible);
        Assert.Equal(10, f.Input!.MaxLength);
        Assert.Equal('•', pw.Input!.PasswordChar);

        var changes = 0;
        f.ValueChanged += (_, _) => changes++;
        var clear = f.Part<Button>("PART_Clear");
        Assert.True(clear.IsVisible);
        w.Click(clear);
        Assert.Equal("", f.Value);
        Assert.Equal(1, changes);
        Assert.Equal("0 / 10", f.CounterText);
        w.Close();
    }

    [AvaloniaFact]
    public void Custom_adornments_replace_icons()
    {
        var start = new TextBlock { Text = "$" };
        var f = new TextField { StartIcon = "search", StartContent = start };
        var w = Show(f);
        Assert.True(start.IsEffectivelyVisible);
        Assert.False(f.Part<Controls.Icon>("PART_StartIcon").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Checkbox_tone_size_description_and_label_placement()
    {
        var cb = new CheckBox { Content = "Notify", IsChecked = true };
        Sl.SetTone(cb, Tone.Danger);
        Sl.SetSize(cb, ControlSize.Large);
        Sl.SetDescription(cb, "Email on failure");
        Sl.SetLabelPlacement(cb, Placement.Start);
        var w = Show(cb);
        var box = cb.Part<Border>("PART_Box");
        Assert.Equal(Token(L.Status.Danger.Solid), ColorOf(box.Background));
        Assert.Equal(20, box.Bounds.Width);
        Assert.True(cb.Part<TextBlock>("PART_Description").IsVisible);
        Assert.Contains("label-start", cb.Classes);
        Assert.True(box.TranslatePoint(default, cb)!.Value.X > cb.Part<StackPanel>("PART_Text").TranslatePoint(default, cb)!.Value.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Switch_maps_canonical_options_and_label_follows_the_track_by_default()
    {
        var s = new Switch { Label = "Wi-Fi", Description = "Join known networks", Tone = Tone.Success, IsChecked = true };
        var w = Show(s);
        var track = s.Part<Grid>("PART_TrackArea");
        var text = s.Part<StackPanel>("PART_Text");
        Assert.Equal("Wi-Fi", s.Content);
        Assert.True(track.TranslatePoint(default, s)!.Value.X < text.TranslatePoint(default, s)!.Value.X);
        Assert.Equal(Token(L.Status.Success.Solid), ColorOf(s.Part<Border>("PART_Track").Background));
        Assert.True(s.Part<TextBlock>("PART_Description").IsVisible);

        s.Spread = true;
        s.LabelPlacement = Placement.Start;
        Pump(w);
        Assert.Contains("spread", s.Classes);
        Assert.True(track.TranslatePoint(default, s)!.Value.X > text.TranslatePoint(default, s)!.Value.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Radio_group_binds_value_both_ways_and_propagates_tone()
    {
        var a = new RadioButton { Content = "Team", Tag = "team" };
        var b = new RadioButton { Content = "Solo", Tag = "solo" };
        var group = new RadioGroup { Label = "Plan", Value = "solo", Tone = Tone.Info, Items = { a, b } };
        var changes = 0;
        group.ValueChanged += (_, _) => changes++;
        var w = Show(group);

        Assert.True(b.IsChecked);
        Assert.False(a.IsChecked);
        Assert.Equal(Tone.Info, Sl.GetTone(a));
        Assert.Equal(a.GroupName, b.GroupName);

        w.Click(a);
        Assert.Equal("team", group.Value);
        Assert.Equal(1, changes);
        Assert.False(b.IsChecked);

        group.Direction = Direction.Row;
        Pump(w);
        Assert.True(b.TranslatePoint(default, group)!.Value.X > a.TranslatePoint(default, group)!.Value.X);
        w.Close();
    }

    [AvaloniaFact]
    public void Alert_variants_icon_override_and_dense()
    {
        var solid = new Alert { Severity = Severity.Error, Variant = AlertVariant.Solid, Title = "Down" };
        var noIcon = new Alert { Severity = Severity.Info, Icon = "none", Content = "x" };
        var custom = new Alert { Severity = Severity.Info, Icon = "bell", Dense = true, Content = "x" };
        var w = Show(new StackPanel { Children = { solid, noIcon, custom } });
        Assert.Equal(Token(L.Status.Danger.Solid), ColorOf(solid.Background));
        Assert.Equal(Token(L.Status.Danger.OnSolid), ColorOf(solid.Part<TextBlock>("PART_Title").Foreground));
        Assert.Null(noIcon.IconKind);
        Assert.Equal("bell", custom.Part<Controls.Icon>("PART_Icon").Kind);
        Assert.True(custom.Bounds.Height < noIcon.Bounds.Height + 1);
        w.Close();
    }

    [AvaloniaFact]
    public void Badge_outlined_and_small()
    {
        var badge = new Badge { Content = "Beta", Tone = Tone.Accent, Variant = BadgeVariant.Outlined, Size = ControlSize.Small, Icon = "check" };
        var w = Show(badge);
        Assert.Equal(Colors.Transparent, ColorOf(badge.Background));
        Assert.Equal(Token(L.Accent.Default), ColorOf(badge.BorderBrush));
        Assert.Equal(18, badge.Bounds.Height);
        Assert.True(badge.Part<Controls.Icon>("PART_Icon").HasIcon);
        w.Close();
    }

    [AvaloniaFact]
    public void Progress_and_spinner_take_tone_and_size()
    {
        var bar = new ProgressBar { Value = 40 };
        Sl.SetTone(bar, Tone.Danger);
        Sl.SetSize(bar, ControlSize.Large);
        var spinner = new LoadingSpinner { Size = ControlSize.Large, Tone = Tone.Accent, Label = "Syncing" };
        var w = Show(new StackPanel { Width = 200, Children = { bar, spinner } });
        Assert.Equal(Token(L.Status.Danger.Solid), ColorOf(bar.Foreground));
        Assert.Equal(10, bar.Bounds.Height);
        Assert.Equal(24, spinner.ActualDiameter);
        Assert.Contains("tone-accent", spinner.Classes);
        Assert.Equal("Syncing", global::Avalonia.Automation.AutomationProperties.GetName(spinner));
        w.Close();
    }

    [AvaloniaFact]
    public void Card_variants_title_custom_header_and_click()
    {
        var flat = new Card { Variant = CardVariant.Flat, Title = "Flat", Content = "x" };
        var header = new TextBlock { Text = "Custom" };
        var custom = new Card { Title = "Ignored", Header = header, Flush = true, Interactive = true, Content = "y" };
        var clicks = 0;
        custom.Click += (_, _) => clicks++;
        var w = Show(new StackPanel { Children = { flat, custom } });
        Assert.Equal(Token(L.Background.Subtle), ColorOf(flat.Background));
        Assert.Equal(0, flat.Part<Border>("PART_Root").BoxShadow.Count);
        Assert.True(flat.Part<TextBlock>("PART_Title").IsEffectivelyVisible);
        Assert.False(custom.Part<StackPanel>("PART_TitleBlock").IsVisible);
        Assert.True(header.IsEffectivelyVisible);
        Assert.Equal(new Thickness(0), custom.Padding);
        w.Click(custom);
        Assert.Equal(1, clicks);
        w.Close();
    }

    [AvaloniaFact]
    public void App_bar_leading_and_center_slots_render()
    {
        var leading = new TextBlock { Text = "logo" };
        var center = new TextBlock { Text = "search" };
        var bar = new AppBar { Title = "Slate", Leading = leading, Center = center, ShowMenuButton = false };
        var w = Show(bar, width: 900);
        Assert.True(leading.IsEffectivelyVisible);
        Assert.True(center.IsEffectivelyVisible);
        Assert.True(leading.TranslatePoint(default, bar)!.Value.X < center.TranslatePoint(default, bar)!.Value.X);
        w.Close();
    }

    [AvaloniaFact]
    public void App_shell_raises_drawer_open_changed()
    {
        var shell = new AppShell { Drawer = new Drawer(), Content = new Border(), DrawerVariant = DrawerVariant.Temporary };
        var changes = 0;
        shell.DrawerOpenChanged += (_, _) => changes++;
        var w = Show(shell, width: 1200);
        shell.ToggleDrawer();
        Assert.True(changes >= 1);
        w.Close();
    }

    [AvaloniaFact]
    public void Container_gutters_can_be_disabled()
    {
        var child = new Border { Height = 10 };
        var w = Show(new Controls.Container { ContainerMaxWidth = ContainerWidth.Fluid, Gutters = false, Child = child }, width: 800);
        Assert.Equal(800, child.Bounds.Width);
        w.Close();
    }
}

/// <summary>Level 2: overriding a component token in any resource scope restyles that component there.</summary>
public class ComponentTokenTests
{
    [AvaloniaFact]
    public void Overriding_a_component_resource_in_a_scope_restyles_only_that_scope()
    {
        var inside = new Button { Content = "in" };
        var outside = new Button { Content = "out" };
        var scope = new Border { Child = inside };
        scope.Resources["Sl.Component.Button.Radius.Corner"] = new CornerRadius(14);
        scope.Resources["Sl.Component.Card.Background.Brush"] = new SolidColorBrush(Colors.Bisque);
        var card = new Card { Content = "c" };
        var w = Show(new StackPanel { Children = { scope, outside, new Border { Resources = { ["Sl.Component.Card.Background.Brush"] = new SolidColorBrush(Colors.Bisque) }, Child = card } } });
        Assert.Equal(new CornerRadius(14), inside.CornerRadius);
        Assert.Equal(new CornerRadius(6), outside.CornerRadius);
        Assert.Equal(Colors.Bisque, ColorOf(card.Background));
        w.Close();
    }

    [AvaloniaFact]
    public void Component_tokens_exist_for_both_themes()
    {
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Assert.IsType<CornerRadius>(Resource("Sl.Component.Button.Radius.Corner", variant));
            Assert.IsAssignableFrom<IBrush>(Resource("Sl.Component.Field.Background.Brush", variant));
            Assert.IsType<Thickness>(Resource("Sl.Component.Button.PaddingMd.Inline", variant));
        }
    }
}

/// <summary>Levels 1 and 3: runtime themes, scoped themes, app-wide defaults and scoped density.</summary>
public class ThemeAndDefaultsTests
{
    [AvaloniaFact]
    public void Applying_a_custom_theme_rewrites_tokens_live_and_reset_restores_them()
    {
        var theme = SlateTheme.Current!;
        var b = new Button { Content = "x" };
        Sl.SetVariant(b, ButtonVariant.Solid);
        Sl.SetTone(b, Tone.Accent);
        var cb = new CheckBox { IsChecked = true };
        var w = Show(new StackPanel { Children = { b, cb } });
        try
        {
            var custom = ThemeBuilder.Build(new SlateThemeOptions { Accent = "#5B3DF5", RadiusScale = 2 });
            theme.Apply(custom);
            Pump(w);

            var accent = custom.Color("color.accent.default");
            var expected = Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
            Assert.Equal(expected, ColorOf(b.Background));
            Assert.Equal(expected, ColorOf((IBrush)Resource("Sl.Brush.Accent.Default")));
            Assert.Equal(expected, ColorOf((IBrush)Resource("Sl.Component.Selection.Checked.Brush")));
            Assert.Equal(new CornerRadius(12), b.CornerRadius); // component.button.radius follows radius.md × 2
            Assert.Same(custom, theme.AppliedTheme(ThemeVariant.Light));

            theme.ResetTheme();
            Pump(w);
            Assert.Equal(Token(L.Accent.Default), ColorOf(b.Background));
            Assert.Equal(new CornerRadius(6), b.CornerRadius);
            Assert.Null(theme.AppliedTheme(ThemeVariant.Light));
        }
        finally
        {
            theme.ResetTheme();
            theme.Mode = ThemeMode.System;
            w.Close();
        }
    }

    [AvaloniaFact]
    public void Options_property_builds_and_applies_and_null_resets()
    {
        var theme = SlateTheme.Current!;
        try
        {
            theme.Options = new SlateThemeOptions { Accent = "#E8590C", FontFamily = "Inter, sans-serif" };
            Assert.Equal("Inter", ((FontFamily)Resource("Sl.Font.Family.Ui")).Name);
            theme.Options = null;
            Assert.Null(theme.AppliedTheme(ThemeVariant.Light));
        }
        finally
        {
            theme.ResetTheme();
            theme.Mode = ThemeMode.System;
        }
    }

    [AvaloniaFact]
    public void A_theme_can_be_scoped_to_a_subtree()
    {
        var inside = new Button { Content = "in" };
        var outside = new Button { Content = "out" };
        foreach (var b in new[] { inside, outside })
        {
            Sl.SetVariant(b, ButtonVariant.Solid);
            Sl.SetTone(b, Tone.Accent);
        }
        var scope = new ThemeVariantScope { Child = inside };
        SlateTheme.ApplyTo(scope, ThemeBuilder.Build(new SlateThemeOptions { Accent = "#D6336C" }));
        var w = Show(new StackPanel { Children = { scope, outside } });

        Assert.NotEqual(ColorOf(outside.Background), ColorOf(inside.Background));
        Assert.Equal(Token(L.Accent.Default), ColorOf(outside.Background));

        SlateTheme.ClearFrom(scope);
        Pump(w);
        Assert.Equal(ColorOf(outside.Background), ColorOf(inside.Background));
        w.Close();
    }

    [AvaloniaFact]
    public void Defaults_apply_to_controls_without_local_values()
    {
        var defaults = SlateTheme.Defaults;
        try
        {
            SlateTheme.Defaults = new SlateDefaults();
            SlateTheme.Defaults.Button.Variant = ButtonVariant.Soft;
            SlateTheme.Defaults.Button.Tone = Tone.Accent;
            SlateTheme.Defaults.Button.Size = ControlSize.Small;
            SlateTheme.Defaults.Field.Variant = FieldVariant.Filled;
            SlateTheme.Defaults.Card.Variant = CardVariant.Outlined;
            SlateTheme.Defaults.Selection.LabelPlacement = Placement.Start;

            var plain = new Button { Content = "x" };
            var local = new Button { Content = "y" };
            Sl.SetVariant(local, ButtonVariant.Ghost);
            var field = new TextField();
            var card = new Card { Content = "c" };
            var cb = new CheckBox { Content = "c" };
            var w = Show(new StackPanel { Children = { plain, local, field, card, cb } });

            Assert.Contains("soft", plain.Classes);
            Assert.Contains("tone-accent", plain.Classes);
            Assert.Equal(26, plain.Bounds.Height);
            Assert.Contains("ghost", local.Classes);
            Assert.Contains("filled", field.Input!.Classes);
            Assert.Equal(CardVariant.Outlined, card.EffectiveVariant);
            Assert.Contains("label-start", cb.Classes);
            w.Close();
        }
        finally
        {
            SlateTheme.Defaults = defaults;
        }
    }

    [AvaloniaFact]
    public async Task Dialog_defaults_and_dialog_content_options_reach_the_container()
    {
        var defaults = SlateTheme.Defaults;
        var service = new DialogService();
        var host = new DialogHost { Service = service, Content = new Border { Width = 900, Height = 700 } };
        var w = Show(host, 1000, 800);
        try
        {
            SlateTheme.Defaults = new SlateDefaults { Dialog = new DialogOptions { MaxWidth = DialogWidth.Lg } };
            var task = service.ShowAsync(new DialogContent
            {
                Title = "Delete?",
                Description = "Can't be undone.",
                Icon = "trash",
                Tone = Tone.Danger,
                CloseOnEscape = false,
                Content = "body",
            });
            Pump(w);
            var dialog = Assert.Single(host.Dialogs);
            Assert.Equal("Delete?", dialog.Title);
            Assert.Equal("Can't be undone.", dialog.Description);
            Assert.Equal("trash", dialog.IconKind);
            Assert.Contains("tone-danger", dialog.Classes);
            Assert.Equal(DialogOptions.WidthPixels(DialogWidth.Lg), dialog.PanelMaxWidth);

            Assert.False(service.Stack.HandleEscape()); // the content's CloseOnEscape=false reached the stack
            service.Stack.CloseAll();
            await task;
        }
        finally
        {
            SlateTheme.Defaults = defaults;
            w.Close();
        }
    }

    [AvaloniaFact]
    public void Scoped_density_resizes_only_its_subtree()
    {
        var inside = new Button { Content = "in" };
        var outside = new Button { Content = "out" };
        var scope = new Border { Child = inside };
        Sl.SetDensity(scope, Density.Comfortable);
        var w = Show(new StackPanel { Children = { scope, outside } });
        Assert.Equal(40, inside.Bounds.Height);
        Assert.Equal(32, outside.Bounds.Height);
        Assert.Equal(Density.Comfortable, Sl.GetDensity(inside)); // inherited

        Sl.SetDensity(scope, null);
        Pump(w);
        Assert.Equal(32, inside.Bounds.Height);
        w.Close();
    }

    [AvaloniaFact]
    public void Add_slate_configures_defaults_and_snackbar_services()
    {
        var defaults = SlateTheme.Defaults;
        try
        {
            SlateTheme.Defaults = new SlateDefaults();
            var provider = new ServiceCollection().AddSlate(o =>
            {
                o.Defaults.Button.Size = ControlSize.Large;
                o.Defaults.Snackbar = new Slate.Snackbars.SnackbarConfiguration { MaxVisible = 1 };
            }).BuildServiceProvider();

            Assert.Equal(ControlSize.Large, SlateTheme.Defaults.Button.Size);
            Assert.Equal(1, provider.GetRequiredService<ISnackbarService>().Configuration.MaxVisible);
            Assert.Same(provider.GetRequiredService<ISnackbarService>(), SlateServices.Snackbars);
        }
        finally
        {
            SlateTheme.Defaults = defaults;
            SlateServices.Snackbars = new SnackbarService();
            SlateServices.Dialogs = new DialogService();
        }
    }
}
