using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Slate.Theming;

namespace Slate.Wpf.Demo.Pages;

public partial class ThemingPage : UserControl
{
    private static readonly (string Name, string Hex)[] Presets =
    [
        ("Alloy teal", "#0E5E6F"), ("Cobalt", "#2F4FD8"), ("Violet", "#5B3DF5"), ("Signal orange", "#FF5A1F"),
        ("Rose", "#D6336C"), ("Forest", "#1F7A4D"), ("Amber", "#FFB020"), ("Graphite", "#3A3F47"),
    ];

    private bool _ready;

    public ThemingPage()
    {
        InitializeComponent();

        foreach (var (name, hex) in Presets)
        {
            var swatch = new Button
            {
                Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 8), ToolTip = $"{name} {hex}", Tag = hex,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), BorderThickness = new Thickness(0),
            };
            Sl.SetLabel(swatch, $"Accent {name}");
            Sl.SetRadius(swatch, Slate.Radius.Full);
            swatch.Click += (_, _) => ApplyAccent(hex);
            Swatches.Children.Add(swatch);
        }

        DefaultVariant.ItemsSource = Enum.GetValues<ButtonVariant>();
        DefaultSize.ItemsSource = Enum.GetValues<ControlSize>();
        DefaultField.ItemsSource = Enum.GetValues<FieldVariant>();
        DefaultCard.ItemsSource = Enum.GetValues<CardVariant>();
        var d = SlateTheme.Defaults;
        DefaultVariant.SelectedItem = d.Button.Variant;
        DefaultSize.SelectedItem = d.Button.Size;
        DefaultField.SelectedItem = d.Field.Variant;
        DefaultCard.SelectedItem = d.Card.Variant;

        var theme = SlateTheme.Current!;
        (theme.Mode switch { Slate.ThemeMode.Dark => ModeDark, Slate.ThemeMode.Light => ModeLight, _ => ModeSystem }).IsChecked = true;
        if (theme.Options is { } options)
        {
            AccentHex.Value = (options.Accent ?? "#0E5E6F").TrimStart('#');
            Radius.Value = options.RadiusScale;
        }

        _ready = true;
        BuildPreview();
        Describe();
    }

    private static SlateThemeOptions Current => SlateTheme.Current!.Options ?? new SlateThemeOptions();

    private void Update(Func<SlateThemeOptions, SlateThemeOptions> change)
    {
        if (!_ready) return;
        SlateTheme.Current!.Options = change(Current);
        Describe();
    }

    private void ApplyAccent(string hex)
    {
        if (!SlateColor.TryParse(hex, out _))
        {
            AccentHex.Error = "Enter a #RRGGBB colour.";
            return;
        }
        AccentHex.Error = null;
        AccentHex.Value = hex.TrimStart('#');
        Update(o => o with { Accent = hex });
    }

    private void OnApplyHex(object sender, RoutedEventArgs e)
    {
        var text = AccentHex.Value.Trim();
        ApplyAccent(text.StartsWith('#') ? text : "#" + text);
    }

    private void OnRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RadiusValue is null) return;
        RadiusValue.Text = $"{e.NewValue:0.00}×";
        Update(o => o with { RadiusScale = e.NewValue });
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Font.SelectedItem is ComboBoxItem { Tag: string family })
            Update(o => o with { FontFamily = family.Length == 0 ? null : family });
    }

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string mode } && SlateTheme.Current is { } theme)
            theme.Mode = Enum.Parse<Slate.ThemeMode>(mode);
        Describe();
    }

    private void OnDefaultsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var d = SlateTheme.Defaults;
        if (DefaultVariant.SelectedItem is ButtonVariant v) d.Button.Variant = v;
        if (DefaultSize.SelectedItem is ControlSize s) d.Button.Size = s;
        if (DefaultField.SelectedItem is FieldVariant f) d.Field.Variant = f;
        if (DefaultCard.SelectedItem is CardVariant c) d.Card.Variant = c;
        BuildPreview(); // defaults apply to controls as they load
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        SlateTheme.Current!.ResetTheme();
        SlateTheme.Defaults = new SlateDefaults();
        _ready = false;
        AccentHex.Value = "0E5E6F";
        AccentHex.Error = null;
        Radius.Value = 1;
        Font.SelectedIndex = 0;
        DefaultVariant.SelectedItem = ButtonVariant.Outlined;
        DefaultSize.SelectedItem = ControlSize.Medium;
        DefaultField.SelectedItem = FieldVariant.Outlined;
        DefaultCard.SelectedItem = CardVariant.Elevated;
        _ready = true;
        BuildPreview();
        Describe();
    }

    private void Describe()
    {
        var applied = SlateTheme.Current!.AppliedDefinition;
        AccentResult.Text = applied is null
            ? "Built-in Alloy theme."
            : $"Accent {applied.Values["color.accent.default"]} with {(applied.Values["color.text.onAccent"] == "#FFFFFF" ? "white" : "dark")} text · {applied.ChangedPaths.Count} tokens changed ({applied.BaseTheme}).";
    }

    /// <summary>Rebuilt when defaults change, because defaults are read as controls load.</summary>
    private void BuildPreview()
    {
        var buttons = new Stack { Direction = Direction.Row, Spacing = 2, Wrap = true };
        var primary = new Button { Content = "Primary" };
        Sl.SetVariant(primary, ButtonVariant.Solid);
        Sl.SetTone(primary, Tone.Accent);
        buttons.Children.Add(primary);
        buttons.Children.Add(new Button { Content = "Default (from Defaults)" });
        var soft = new Button { Content = "Soft" };
        Sl.SetVariant(soft, ButtonVariant.Soft);
        Sl.SetTone(soft, Tone.Accent);
        buttons.Children.Add(soft);
        var danger = new Button { Content = "Delete" };
        Sl.SetTone(danger, Tone.Danger);
        buttons.Children.Add(danger);

        var checks = new Stack { Direction = Direction.Row, Spacing = 4, Wrap = true };
        checks.Children.Add(new CheckBox { Content = "Checked", IsChecked = true });
        checks.Children.Add(new RadioButton { Content = "Selected", IsChecked = true });
        checks.Children.Add(new Switch { Content = "On", IsChecked = true });

        var badges = new Stack { Direction = Direction.Row, Spacing = 2 };
        badges.Children.Add(new Badge { Tone = Tone.Accent, Content = "Accent" });
        badges.Children.Add(new Badge { Tone = Tone.Accent, Variant = BadgeVariant.Solid, Content = "Solid" });
        badges.Children.Add(new Badge { Tone = Tone.Success, Dot = true, Content = "Ready" });

        Preview.Content = new Card
        {
            Title = "Preview",
            Subtitle = "Defaults apply to the unconfigured controls here.",
            Content = new Stack
            {
                Spacing = 4,
                Children =
                {
                    buttons,
                    new TextField { Label = "Workspace", Value = "studio-tools", HelperText = "Uses Defaults.Field.Variant." },
                    checks,
                    new ProgressBar { Value = 62 },
                    badges,
                    new Alert { Severity = Severity.Info, Title = "Theme tokens", Content = "Links, focus and selection follow the accent too." },
                },
            },
        };
    }
}
