using System.Windows;
using System.Windows.Controls;

namespace Slate.Wpf.Demo.Pages;

/// <summary>Builds a live editor for each component: every canonical option gets a control.</summary>
public partial class PlaygroundPage : UserControl
{
    private readonly Dictionary<string, Action> _components;

    public PlaygroundPage()
    {
        InitializeComponent();
        _components = new()
        {
            ["Button"] = ButtonPlayground,
            ["TextField"] = TextFieldPlayground,
            ["Card"] = CardPlayground,
            ["Alert"] = AlertPlayground,
            ["Badge"] = BadgePlayground,
        };

        foreach (var name in _components.Keys)
        {
            var item = new RadioButton { Content = name, GroupName = "Playground", Style = (Style)FindResource("Sl.SegmentedItem") };
            item.Checked += (_, _) => Show(name);
            Picker.Children.Add(item);
        }
        ((RadioButton)Picker.Children[0]).IsChecked = true;
    }

    private void Show(string name)
    {
        Options.Children.Clear();
        _components[name]();
    }

    // ---- option editors ----

    private void Enum<T>(string label, T value, Action<T> set) where T : struct, Enum
    {
        var box = new ComboBox { ItemsSource = System.Enum.GetValues<T>(), SelectedItem = value };
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is T v) set(v); };
        AutomationLabel(box, label);
        Options.Children.Add(Row(label, box));
    }

    private void Flag(string label, bool value, Action<bool> set)
    {
        var sw = new Switch { Content = label, IsChecked = value, Spread = true };
        sw.Checked += (_, _) => set(true);
        sw.Unchecked += (_, _) => set(false);
        Options.Children.Add(sw);
    }

    private void Text(string label, string? value, Action<string?> set)
    {
        var field = new TextField { Label = label, Value = value ?? "", Size = ControlSize.Small, Clearable = true };
        field.ValueChanged += (_, e) => set(string.IsNullOrEmpty(e.NewValue) ? null : e.NewValue);
        Options.Children.Add(field);
    }

    private static FrameworkElement Row(string label, FrameworkElement editor) => new DockPanel
    {
        Children =
        {
            new Label { Content = label, Width = 120, VerticalAlignment = VerticalAlignment.Center },
            editor,
        },
    };

    private static void AutomationLabel(UIElement e, string label) => System.Windows.Automation.AutomationProperties.SetName(e, label);

    // ---- components ----

    private void ButtonPlayground()
    {
        var b = new Button { Content = "Deploy" };
        Sl.SetVariant(b, ButtonVariant.Solid);
        Sl.SetTone(b, Tone.Accent);
        Sl.SetStartIcon(b, "upload");
        Stage.Content = b;

        Enum("Variant", ButtonVariant.Solid, v => Sl.SetVariant(b, v));
        Enum("Tone", Tone.Accent, v => Sl.SetTone(b, v));
        Enum("Size", ControlSize.Medium, v => Sl.SetSize(b, v));
        Enum("Radius", Slate.Radius.Default, v => Sl.SetRadius(b, v));
        Text("Label", "Deploy", v => b.Content = v);
        Text("StartIcon", "upload", v => Sl.SetStartIcon(b, v));
        Text("EndIcon", null, v => Sl.SetEndIcon(b, v));
        Text("Shortcut", null, v => Sl.SetShortcut(b, v));
        Flag("IconOnly", false, v => { Sl.SetIconOnly(b, v); Sl.SetLabel(b, v ? "Deploy" : null); });
        Flag("Loading", false, v => Sl.SetLoading(b, v));
        Flag("FullWidth", false, v => { Sl.SetFullWidth(b, v); Stage.HorizontalAlignment = v ? HorizontalAlignment.Stretch : HorizontalAlignment.Center; });
        Flag("Pressed", false, v => Sl.SetPressed(b, v ? true : null));
        Flag("Disabled", false, v => b.IsEnabled = !v);
    }

    private void TextFieldPlayground()
    {
        var f = new TextField { Label = "Project name", Placeholder = "my-project", HelperText = "Lowercase letters, numbers and dashes.", Width = 320 };
        Stage.Content = f;

        Enum("Variant", FieldVariant.Outlined, v => f.Variant = v);
        Enum("Size", ControlSize.Medium, v => f.Size = v);
        Enum("Radius", Slate.Radius.Default, v => f.Radius = v);
        Enum("InputType", InputType.Text, v => f.InputType = v);
        Text("Label", f.Label, v => f.Label = v);
        Text("Placeholder", f.Placeholder, v => f.Placeholder = v);
        Text("HelperText", f.HelperText, v => f.HelperText = v);
        Text("Error", null, v => f.Error = v);
        Text("Prefix", null, v => f.Prefix = v);
        Text("Suffix", null, v => f.Suffix = v);
        Text("StartIcon", null, v => f.StartIcon = v);
        Text("EndIcon", null, v => f.EndIcon = v);
        Flag("Required", false, v => f.Required = v);
        Flag("ReadOnly", false, v => f.ReadOnly = v);
        Flag("Multiline", false, v => f.Multiline = v);
        Flag("Counter", false, v => { f.Counter = v; f.MaxLength = v ? 40 : null; });
        Flag("Clearable", false, v => f.Clearable = v);
        Flag("Disabled", false, v => f.IsEnabled = !v);
    }

    private void CardPlayground()
    {
        var c = new Card
        {
            Title = "Deployments",
            Subtitle = "Last 24 hours",
            Width = 380,
            Content = new TextBlock { Text = "42 successful, 1 failed. Median build 48s.", TextWrapping = TextWrapping.Wrap },
        };
        var actions = new Button { Content = "View all" };
        Sl.SetVariant(actions, ButtonVariant.Ghost);
        Sl.SetSize(actions, ControlSize.Small);
        Stage.Content = c;

        Enum("Variant", CardVariant.Elevated, v => c.Variant = v);
        Enum("Radius", Slate.Radius.Default, v => c.Radius = v);
        Text("Title", c.Title, v => c.Title = v);
        Text("Subtitle", c.Subtitle, v => c.Subtitle = v);
        Flag("HeaderActions", false, v => c.HeaderActions = v ? actions : null);
        Flag("Footer", false, v => c.Footer = v ? new Badge { Tone = Tone.Success, Dot = true, Content = "Healthy" } : null);
        Flag("Flush", false, v => c.Flush = v);
        Flag("Interactive", false, v => c.Interactive = v);
    }

    private void AlertPlayground()
    {
        var a = new Alert { Severity = Severity.Warning, Title = "Usage at 80%", Content = "Build minutes reset on the 1st.", Width = 420 };
        var upgrade = new Button { Content = "Upgrade" };
        Sl.SetSize(upgrade, ControlSize.Small);
        Stage.Content = a;

        Enum("Severity", Severity.Warning, v => a.Severity = v);
        Enum("Variant", AlertVariant.Soft, v => a.Variant = v);
        Text("Title", a.Title, v => a.Title = v);
        Text("Icon", null, v => a.Icon = v);
        Flag("Actions", false, v => a.Actions = v ? upgrade : null);
        Flag("Dense", false, v => a.Dense = v);
        Flag("Dismissible", false, v => { a.Dismissible = v; a.Visibility = Visibility.Visible; });
        Flag("Live", false, v => a.Live = v);
    }

    private void BadgePlayground()
    {
        var b = new Badge { Tone = Tone.Success, Dot = true, Content = "Ready" };
        Stage.Content = b;

        Enum("Tone", Tone.Success, v => b.Tone = v);
        Enum("Variant", BadgeVariant.Soft, v => b.Variant = v);
        Enum("Size", ControlSize.Medium, v => b.Size = v);
        Enum("Radius", Slate.Radius.Default, v => b.Radius = v);
        Text("Content", "Ready", v => b.Content = v);
        Text("Icon", null, v => b.Icon = v);
        Flag("Dot", true, v => b.Dot = v);
    }
}
