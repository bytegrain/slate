using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Slate.Avalonia.Controls;
using Slate.Theming;

namespace Slate.Avalonia.Demo.Pages;

/// <summary>Live theme builder and component playground (docs/design/configurability.md).</summary>
public partial class ThemingPage : UserControl
{
    private static readonly (string Name, string Hex)[] Presets =
    [
        ("Alloy teal", "#0E5E6F"), ("Violet", "#5B3DF5"), ("Tangerine", "#E8590C"), ("Cobalt", "#2F4FD8"),
        ("Rose", "#D6336C"), ("Forest", "#1F3B2D"), ("Amber", "#FFB020"), ("Volt", "#D4FF3A"),
    ];

    private static readonly (string Name, string? Family)[] Fonts =
    [
        ("Instrument Sans (Alloy)", null), ("Inter", "Inter, sans-serif"), ("System UI", "Segoe UI, SF Pro Text, Helvetica Neue, sans-serif"),
        ("Georgia (serif)", "Georgia, serif"),
    ];

    private string? _accent;
    private bool _dark;

    public ThemingPage()
    {
        InitializeComponent();

        foreach (var (name, hex) in Presets)
        {
            var swatch = new Button { Width = 32, Height = 32, MinWidth = 32, Padding = new global::Avalonia.Thickness(0), Background = new SolidColorBrush(Color.Parse(hex)) };
            Sl.SetLabel(swatch, $"{name} {hex}");
            ToolTip.SetTip(swatch, $"{name} {hex}");
            swatch.Click += (_, _) => { _accent = hex == "#0E5E6F" ? null : hex; HexField.Value = hex.TrimStart('#'); ApplyTheme(); };
            Swatches.Children.Add(swatch);
        }
        HexField.Value = "5B3DF5";
        ApplyHex.Click += (_, _) =>
        {
            if (SlateColor.TryParse("#" + HexField.Value?.TrimStart('#'), out var c) && c.IsOpaque)
            {
                HexField.Error = null;
                _accent = c.ToHex();
                ApplyTheme();
            }
            else
            {
                HexField.Error = "Use six hex digits, e.g. 5B3DF5.";
            }
        };

        BaseLight.Click += (_, _) => { _dark = false; ApplyTheme(); };
        BaseDark.Click += (_, _) => { _dark = true; ApplyTheme(); };
        RadiusSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                RadiusValue.Text = $"{RadiusSlider.Value:0.00}×";
                ApplyTheme();
            }
        };
        FontBox.ItemsSource = Fonts.Select(f => f.Name).ToList();
        FontBox.SelectionChanged += (_, _) => ApplyTheme();
        Comfortable.IsCheckedChanged += (_, _) => Sl.SetDensity(Playground, Comfortable.IsChecked == true ? Density.Comfortable : null);
        ResetTheme.Click += (_, _) => Reset();

        BuildPlayground();
        UpdateBaseButtons();
    }

    private void ApplyTheme()
    {
        if (SlateTheme.Current is not { } theme)
            return;
        var font = FontBox.SelectedIndex is var i and >= 0 && i < Fonts.Length ? Fonts[i].Family : null;
        var options = new SlateThemeOptions
        {
            Name = "playground",
            Base = _dark ? ThemeNames.Dark : ThemeNames.Light,
            Accent = _accent,
            RadiusScale = RadiusSlider.Value,
            FontFamily = font,
        };
        theme.Options = options;
        var built = theme.AppliedTheme(_dark ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light);
        ThemeSummary.Text = built is null
            ? ""
            : $"{built.ChangedPaths.Count} tokens changed · accent {built.Values["color.accent.default"]} · on-accent {built.Values["color.text.onAccent"]}";
        UpdateBaseButtons();
    }

    private void Reset()
    {
        _accent = null;
        _dark = false;
        RadiusSlider.Value = 1;
        FontBox.SelectedIndex = 0;
        if (SlateTheme.Current is { } theme)
        {
            theme.Options = null;
            theme.Mode = ThemeMode.System;
        }
        ThemeSummary.Text = "";
        UpdateBaseButtons();
    }

    private void UpdateBaseButtons()
    {
        BaseLight.IsChecked = !_dark;
        BaseDark.IsChecked = _dark;
    }

    // ---- playground --------------------------------------------------------------------------------

    private void BuildPlayground()
    {
        Sl.SetVariant(PreviewButton, ButtonVariant.Solid);
        Sl.SetTone(PreviewButton, Tone.Accent);
        Sl.SetStartIcon(PreviewButton, "upload");
        Sl.SetLabel(PreviewButton, "Deploy");

        ButtonOptions.Children.Add(Choice("Variant", ButtonVariant.Solid, v => Sl.SetVariant(PreviewButton, v)));
        ButtonOptions.Children.Add(Choice("Tone", Tone.Accent, v => Sl.SetTone(PreviewButton, v)));
        ButtonOptions.Children.Add(Choice("Size", ControlSize.Medium, v => Sl.SetSize(PreviewButton, v)));
        ButtonOptions.Children.Add(Choice("Radius", Radius.Default, v => Sl.SetRadius(PreviewButton, v)));
        ButtonOptions.Children.Add(Choice("StartIcon", "upload", new[] { "none", "upload", "plus", "download", "check" }, v => Sl.SetStartIcon(PreviewButton, v == "none" ? null : v)));
        ButtonOptions.Children.Add(Flag("Loading", false, v => Sl.SetLoading(PreviewButton, v)));
        ButtonOptions.Children.Add(Flag("IconOnly", false, v => Sl.SetIconOnly(PreviewButton, v)));
        ButtonOptions.Children.Add(Flag("FullWidth", false, v => Sl.SetFullWidth(PreviewButton, v)));
        ButtonOptions.Children.Add(Flag("Shortcut", false, v => Sl.SetShortcut(PreviewButton, v ? "⌘↵" : null)));

        FieldOptions.Children.Add(Choice("Variant", FieldVariant.Outlined, v => PreviewField.Variant = v));
        FieldOptions.Children.Add(Choice("Size", ControlSize.Medium, v => PreviewField.Size = v));
        FieldOptions.Children.Add(Choice("Radius", Radius.Default, v => PreviewField.Radius = v));
        FieldOptions.Children.Add(Flag("Counter", false, v => PreviewField.Counter = v));
        FieldOptions.Children.Add(Flag("Clearable", false, v => PreviewField.Clearable = v));
        FieldOptions.Children.Add(Flag("Required", false, v => PreviewField.Required = v));
        FieldOptions.Children.Add(Flag("Error", false, v => PreviewField.Error = v ? "That ID is taken on NuGet." : null));
        FieldOptions.Children.Add(Flag("StartIcon", false, v => PreviewField.StartIcon = v ? "layers" : null));

        DisplayOptions.Children.Add(Choice("Severity", Severity.Success, v => PreviewAlert.Severity = v));
        DisplayOptions.Children.Add(Choice("Alert", AlertVariant.Soft, v => PreviewAlert.Variant = v));
        DisplayOptions.Children.Add(Flag("Dense", false, v => PreviewAlert.Dense = v));
        DisplayOptions.Children.Add(Choice("Badge tone", Tone.Success, v => PreviewBadge.Tone = v));
        DisplayOptions.Children.Add(Choice("Badge", BadgeVariant.Soft, v => PreviewBadge.Variant = v));
        DisplayOptions.Children.Add(Choice("Card", CardVariant.Elevated, v => PreviewCard.Variant = v));
        DisplayOptions.Children.Add(Choice("Card radius", Radius.Default, v => PreviewCard.Radius = v));
        PreviewAlert.Severity = Severity.Success;
        PreviewBadge.Tone = Tone.Success;
    }

    private static Control Choice<T>(string label, T initial, Action<T> apply) where T : struct, Enum =>
        Choice(label, initial, Enum.GetValues<T>(), apply);

    private static Control Choice<T>(string label, T initial, IReadOnlyList<T> values, Action<T> apply)
    {
        var box = new ComboBox { ItemsSource = values, SelectedItem = initial, MinWidth = 120, FontSize = 13 };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T v) apply(v); };
        var caption = new TextBlock { Text = label, FontSize = 11.5 };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("Sl.Brush.Text.Tertiary"));
        return new StackPanel { Spacing = 4, Children = { caption, box } };
    }

    private static Control Flag(string label, bool initial, Action<bool> apply)
    {
        var cb = new CheckBox { Content = label, IsChecked = initial, VerticalAlignment = VerticalAlignment.Bottom, Margin = new global::Avalonia.Thickness(0, 18, 0, 0) };
        cb.IsCheckedChanged += (_, _) => apply(cb.IsChecked == true);
        return cb;
    }
}
