using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Slate.Avalonia.Controls;
using static Slate.Avalonia.Tests.TestHelpers;
using L = Slate.SlateTokens.Themes.Light.Color;

namespace Slate.Avalonia.Tests;

public class ButtonTests
{
    private static Border Chrome(Button b) => b.Part<Border>("PART_Border");

    [AvaloniaFact]
    public void Secondary_is_the_default_with_milled_chrome()
    {
        var b = new Button { Content = "Save" };
        var w = Show(b);
        Assert.Equal(32, b.Bounds.Height);
        Assert.Equal(new CornerRadius(6), b.CornerRadius);
        Assert.Equal(Token(L.Background.Surface), ColorOf(b.Background));
        Assert.Equal(Token(L.Border.Strong), ColorOf(b.BorderBrush));
        Assert.Equal(Token(L.Text.Primary), ColorOf(b.Foreground));
        Assert.Equal(Resource("Sl.Shadow.Control"), Chrome(b).BoxShadow);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(ButtonVariant.Primary, "primary")]
    [InlineData(ButtonVariant.Ghost, "ghost")]
    [InlineData(ButtonVariant.Danger, "danger")]
    [InlineData(ButtonVariant.DangerSolid, "danger-solid")]
    [InlineData(ButtonVariant.Link, "link")]
    public void Variant_attached_property_sets_exactly_one_class(ButtonVariant variant, string cls)
    {
        var b = new Button { Content = "x" };
        Sl.SetVariant(b, ButtonVariant.Ghost);
        Sl.SetVariant(b, variant);
        Assert.Contains(cls, b.Classes);
        Assert.Single(b.Classes, c => Sl.VariantClasses.Contains(c));

        Sl.SetVariant(b, ButtonVariant.Secondary);
        Assert.DoesNotContain(b.Classes, c => Sl.VariantClasses.Contains(c));
    }

    [AvaloniaFact]
    public void Primary_uses_accent_fill_and_on_accent_text()
    {
        var b = new Button { Content = "Deploy", Classes = { "primary" } };
        var w = Show(b);
        Assert.Equal(Token(L.Accent.Default), ColorOf(b.Background));
        Assert.Equal(Token(L.Text.OnAccent), ColorOf(b.Foreground));
        Assert.Equal(Resource("Sl.Shadow.Primary"), Chrome(b).BoxShadow);
        w.Close();
    }

    [AvaloniaFact]
    public void Ghost_has_no_chrome_and_danger_uses_danger_text()
    {
        var ghost = new Button { Content = "g", Classes = { "ghost" } };
        var danger = new Button { Content = "d", Classes = { "danger" } };
        var solid = new Button { Content = "s", Classes = { "danger-solid" } };
        var w = Show(new StackPanel { Children = { ghost, danger, solid } });
        Assert.Equal(Colors.Transparent, ColorOf(ghost.Background));
        Assert.Equal(0, Chrome(ghost).BoxShadow.Count);
        Assert.Equal(Token(L.Status.Danger.Fg), ColorOf(danger.Foreground));
        Assert.Equal(Token(L.Status.Danger.Solid), ColorOf(solid.Background));
        Assert.Equal(Token(L.Status.Danger.OnSolid), ColorOf(solid.Foreground));
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(ControlSize.Small, 26)]
    [InlineData(ControlSize.Medium, 32)]
    [InlineData(ControlSize.Large, 40)]
    public void Sizes_map_to_control_height_tokens(ControlSize size, double height)
    {
        var b = new Button { Content = "x" };
        Sl.SetSize(b, size);
        var w = Show(b);
        Assert.Equal(height, b.Bounds.Height);
        w.Close();
    }

    [AvaloniaFact]
    public void Icon_only_buttons_are_square()
    {
        var b = new Button();
        Sl.SetIcon(b, "plus");
        AutomationProperties.SetName(b, "Add");
        var w = Show(b);
        Assert.Contains("icon-only", b.Classes);
        Assert.Equal(b.Bounds.Height, b.Bounds.Width);

        b.Content = "Add";
        Assert.DoesNotContain("icon-only", b.Classes);
        Assert.Contains("has-icon", b.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public void Loading_shows_spinner_reports_busy_and_blocks_click()
    {
        var clicks = 0;
        var b = new Button { Content = "Save" };
        b.Click += (_, _) => clicks++;
        var w = Show(b);

        Sl.SetIsLoading(b, true);
        Pump(w);
        Assert.True(b.Part<LoadingSpinner>("PART_Spinner").IsVisible);
        Assert.Equal("Busy", AutomationProperties.GetItemStatus(b));
        b.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, clicks);

        Sl.SetIsLoading(b, false);
        Pump(w);
        Assert.False(b.Part<LoadingSpinner>("PART_Spinner").IsVisible);
        b.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
        w.Close();
    }

    [AvaloniaFact]
    public void Disabled_buttons_use_the_disabled_opacity()
    {
        var b = new Button { Content = "x", IsEnabled = false };
        var w = Show(b);
        Assert.Equal(SlateTokens.Opacity.Disabled, b.Opacity, 3);
        w.Close();
    }

    [AvaloniaFact]
    public void Shortcut_hint_renders_a_kbd()
    {
        var b = new Button { Content = "Commit" };
        Sl.SetShortcut(b, "⌘↵");
        var w = Show(b);
        var kbd = b.Part<Kbd>("PART_Shortcut");
        Assert.True(kbd.IsVisible);
        Assert.Equal("⌘↵", kbd.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Keyboard_focus_uses_the_slate_focus_adorner()
    {
        var b = new Button { Content = "x" };
        var w = Show(b);
        Assert.Same(Resource("Sl.FocusAdorner"), b.FocusAdorner);
        w.Close();
    }
}

public class SelectionControlTests
{
    [AvaloniaFact]
    public void Checkbox_states_fill_with_accent_and_show_the_right_glyph()
    {
        var cb = new CheckBox { Content = "Run tests", IsChecked = false };
        var w = Show(cb);
        var box = cb.Part<Border>("PART_Box");
        Assert.Equal(Token(L.Border.Control), ColorOf(box.BorderBrush));
        Assert.False(cb.Part<Controls.Icon>("PART_Check").IsVisible);

        cb.IsChecked = true;
        Pump(w);
        Assert.Equal(Token(L.Accent.Default), ColorOf(box.Background));
        Assert.True(cb.Part<Controls.Icon>("PART_Check").IsVisible);
        Assert.False(cb.Part<Controls.Icon>("PART_Indeterminate").IsVisible);

        cb.IsThreeState = true;
        cb.IsChecked = null;
        Pump(w);
        Assert.True(cb.Part<Controls.Icon>("PART_Indeterminate").IsVisible);
        Assert.False(cb.Part<Controls.Icon>("PART_Check").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Checkbox_toggles_with_space()
    {
        var cb = new CheckBox { Content = "x" };
        var w = Show(cb);
        cb.Focus(NavigationMethod.Tab);
        w.Press(Key.Space);
        Assert.True(cb.IsChecked);
        w.Close();
    }

    [AvaloniaFact]
    public void Radio_shows_dot_when_checked()
    {
        var a = new RadioButton { Content = "A", GroupName = "g", IsChecked = true };
        var b = new RadioButton { Content = "B", GroupName = "g" };
        var w = Show(new StackPanel { Children = { a, b } });
        Assert.True(a.Part<global::Avalonia.Controls.Shapes.Ellipse>("PART_Dot").IsVisible);
        Assert.False(b.Part<global::Avalonia.Controls.Shapes.Ellipse>("PART_Dot").IsVisible);
        Assert.Equal(Token(L.Accent.Default), ColorOf(a.Part<Border>("PART_Ring").Background));
        w.Close();
    }

    [AvaloniaFact]
    public void Switch_track_is_outlined_off_and_accent_on_with_token_geometry()
    {
        var s = new ToggleSwitch { Content = "Hot reload" };
        var w = Show(s);
        SlateTheme.Current!.ReduceMotion = true; // no colour transition mid-assert
        try
        {
        var track = s.Part<Border>("PART_Track");
        Assert.Equal(36, track.Bounds.Width);
        Assert.Equal(20, track.Bounds.Height);
        Assert.Equal(Token(L.Border.Control), ColorOf(track.BorderBrush));

        s.IsChecked = true;
        Pump(w);
        Assert.Equal(Token(L.Accent.Default), ColorOf(track.Background));
        Assert.Equal(16, s.Part<Panel>("PART_SwitchKnob").Bounds.Width); // knob travel
        }
        finally { SlateTheme.Current!.ReduceMotion = false; w.Close(); }
    }
}

public class TextFieldTests
{
    [AvaloniaFact]
    public void Label_names_the_input_and_text_binds_both_ways()
    {
        var f = new TextField { Label = "Project name", Text = "slate" };
        var w = Show(f);
        Assert.Equal("Project name", f.Part<TextBlock>("PART_Label").Text);
        Assert.Equal("Project name", AutomationProperties.GetName(f.Input!));
        Assert.Equal("slate", f.Input!.Text);

        f.Input.Text = "slate-web";
        Assert.Equal("slate-web", f.Text);
        w.Close();
    }

    [AvaloniaFact]
    public void Error_replaces_helper_marks_invalid_and_sets_help_text()
    {
        var f = new TextField { Label = "Email", HelperText = "We never share it." };
        var w = Show(f);
        Assert.True(f.Part<TextBlock>("PART_Helper").IsVisible);
        Assert.Equal("We never share it.", AutomationProperties.GetHelpText(f.Input!));

        f.Error = "Enter a complete email address.";
        Pump(w);
        Assert.True(f.IsInvalid);
        Assert.True(f.Part<TextBlock>("PART_Error").IsVisible);
        Assert.False(f.Part<TextBlock>("PART_Helper").IsVisible);
        Assert.True(f.Part<Controls.Icon>("PART_ErrorIcon").IsVisible);
        Assert.Contains("invalid", f.Input!.Classes);
        Assert.Equal("Enter a complete email address.", AutomationProperties.GetHelpText(f.Input));
        Assert.Equal(Token(L.Status.Danger.Fg), ColorOf(f.Input.Part<Border>("PART_BorderElement").BorderBrush));

        f.Error = null;
        Pump(w);
        Assert.False(f.IsInvalid);
        Assert.DoesNotContain("invalid", f.Input.Classes);
        w.Close();
    }

    [AvaloniaFact]
    public void Required_prefix_and_suffix_render()
    {
        var f = new TextField { Label = "Domain", IsRequired = true, Prefix = "https://", Suffix = ".dev" };
        var w = Show(f);
        Assert.True(f.Part<TextBlock>("PART_Required").IsVisible);
        Assert.True(f.Part<Border>("PART_Prefix").IsVisible);
        Assert.True(f.Part<Border>("PART_Suffix").IsVisible);
        Assert.True(AutomationProperties.GetIsRequiredForForm(f.Input!));
        w.Close();
    }

    [AvaloniaFact]
    public void Input_uses_sunken_field_tokens_and_control_height()
    {
        var f = new TextField { Label = "x" };
        var w = Show(f);
        Assert.Equal(Token(L.Background.Sunken), ColorOf(f.Input!.Background));
        Assert.Equal(Token(L.Border.Strong), ColorOf(f.Input.BorderBrush));
        Assert.Equal(32, f.Input.Bounds.Height);
        w.Close();
    }
}

public class DisplayControlTests
{
    [AvaloniaTheory]
    [InlineData(BadgeTone.Success, L.Status.Success.Bg, L.Status.Success.Fg)]
    [InlineData(BadgeTone.Danger, L.Status.Danger.Bg, L.Status.Danger.Fg)]
    [InlineData(BadgeTone.Accent, L.Accent.Default, L.Text.OnAccent)]
    public void Badge_tones_map_to_status_tokens(BadgeTone tone, string bg, string fg)
    {
        var badge = new Badge { Text = "Ready", Tone = tone, ShowDot = true };
        var w = Show(badge);
        Assert.Equal(Token(bg), ColorOf(badge.Background));
        Assert.Equal(Token(fg), ColorOf(badge.Foreground));
        Assert.Equal(20, badge.Bounds.Height);
        Assert.True(badge.Part<global::Avalonia.Controls.Shapes.Ellipse>("PART_Dot").IsVisible);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(Severity.Info, "info", AutomationLiveSetting.Polite)]
    [InlineData(Severity.Success, "check-circle", AutomationLiveSetting.Polite)]
    [InlineData(Severity.Warning, "alert-triangle", AutomationLiveSetting.Polite)]
    [InlineData(Severity.Error, "alert-circle", AutomationLiveSetting.Assertive)]
    public void Alert_pairs_severity_with_icon_and_announcement(Severity severity, string icon, AutomationLiveSetting live)
    {
        var alert = new Alert { Severity = severity, Title = "Build failed", Content = "3 tests failed." };
        var w = Show(alert);
        Assert.Equal(icon, alert.Part<Controls.Icon>("PART_Icon").Kind);
        Assert.Equal(live, AutomationProperties.GetLiveSetting(alert));
        Assert.True(alert.Part<TextBlock>("PART_Title").IsVisible);
        w.Close();
    }

    [AvaloniaFact]
    public void Dismissible_alert_closes_and_raises_dismissed()
    {
        var dismissed = 0;
        var alert = new Alert { Content = "x", IsDismissible = true };
        alert.Dismissed += (_, _) => dismissed++;
        var w = Show(alert);
        var close = alert.Part<Button>("PART_Close");
        Assert.True(close.IsVisible);
        w.Click(close);
        Assert.False(alert.IsVisible);
        Assert.Equal(1, dismissed);
        w.Close();
    }

    [AvaloniaFact]
    public void Card_shows_header_and_footer_only_when_set()
    {
        var card = new Card { Content = new TextBlock { Text = "Body" } };
        var w = Show(card);
        Assert.False(card.Part<Border>("PART_Header").IsVisible);
        Assert.False(card.Part<Border>("PART_Footer").IsVisible);
        Assert.Equal(Resource("Sl.Shadow.E1"), card.Part<Border>("PART_Root").BoxShadow);

        card.Header = "Deployments";
        card.Footer = new Button { Content = "View all" };
        card.IsOutlined = true;
        Pump(w);
        Assert.True(card.Part<Border>("PART_Header").IsVisible);
        Assert.True(card.Part<Border>("PART_Footer").IsVisible);
        Assert.Equal(0, card.Part<Border>("PART_Root").BoxShadow.Count);
        w.Close();
    }

    [AvaloniaFact]
    public void Icon_measures_to_its_size_and_zero_when_unknown()
    {
        var known = new Controls.Icon { Kind = "check", Size = 20 };
        var unknown = new Controls.Icon { Kind = "does-not-exist" };
        var w = Show(new StackPanel { Children = { known, unknown } });
        Assert.Equal(new Size(20, 20), known.Bounds.Size);
        Assert.Equal(0, unknown.Bounds.Height);
        Assert.True(known.HasIcon);
        Assert.False(unknown.HasIcon);
        w.Close();
    }

    [AvaloniaFact]
    public void Every_shared_icon_parses_as_geometry()
    {
        foreach (var (name, _) in SlateIcons.All)
            Assert.True(new Controls.Icon { Kind = name }.HasIcon, name);
    }

    [AvaloniaFact]
    public void Divider_is_a_one_pixel_hairline()
    {
        var d = new Divider();
        var w = Show(new StackPanel { Width = 200, Children = { d } });
        Assert.Equal(1, d.Bounds.Height);
        Assert.Equal(200, d.Bounds.Width);
        Assert.Equal(Token(L.Border.Default), ColorOf(d.Brush));
        w.Close();
    }
}
