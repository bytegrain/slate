using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace Slate.Avalonia.Controls;

/// <summary>
/// On/off switch (docs/design/components.md#switch). A native <see cref="ToggleSwitch"/> with the canonical
/// Slate options; each maps to the <c>sl:Sl.*</c> attached property, so the native control supports them too.
/// The label sits after the track by default (<see cref="LabelPlacement"/>); <see cref="Spread"/> pushes label and
/// switch to opposite ends of the row.
/// </summary>
public class Switch : ToggleSwitch
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<Switch, string?>(nameof(Label));
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<Switch, string?>(nameof(Description));
    public static readonly StyledProperty<Tone> ToneProperty = AvaloniaProperty.Register<Switch, Tone>(nameof(Tone), Tone.Accent);
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<Switch, ControlSize>(nameof(Size), ControlSize.Medium);
    public static readonly StyledProperty<Placement> LabelPlacementProperty = AvaloniaProperty.Register<Switch, Placement>(nameof(LabelPlacement), Placement.End);
    public static readonly StyledProperty<bool> SpreadProperty = AvaloniaProperty.Register<Switch, bool>(nameof(Spread));

    static Switch()
    {
        LabelProperty.Changed.AddClassHandler<Switch>((s, e) =>
        {
            if (s.Content is null || s.Content is string)
                s.Content = e.GetNewValue<string?>();
            AutomationProperties.SetName(s, e.GetNewValue<string?>());
        });
        DescriptionProperty.Changed.AddClassHandler<Switch>((s, e) => Slate.Avalonia.Sl.SetDescription(s, e.GetNewValue<string?>()));
        ToneProperty.Changed.AddClassHandler<Switch>((s, e) => Slate.Avalonia.Sl.SetTone(s, e.GetNewValue<Tone>()));
        SizeProperty.Changed.AddClassHandler<Switch>((s, e) => Slate.Avalonia.Sl.SetSize(s, e.GetNewValue<ControlSize>()));
        LabelPlacementProperty.Changed.AddClassHandler<Switch>((s, e) => Slate.Avalonia.Sl.SetLabelPlacement(s, e.GetNewValue<Placement>()));
        SpreadProperty.Changed.AddClassHandler<Switch>((s, e) =>
        {
            s.Classes.Set("spread", e.GetNewValue<bool>());
            s.HorizontalAlignment = e.GetNewValue<bool>() ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        });
    }

    protected override Type StyleKeyOverride => typeof(ToggleSwitch);

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public Tone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Placement LabelPlacement { get => GetValue(LabelPlacementProperty); set => SetValue(LabelPlacementProperty, value); }
    public bool Spread { get => GetValue(SpreadProperty); set => SetValue(SpreadProperty, value); }
}

/// <summary>
/// A labelled group of <see cref="RadioButton"/>s (docs/design/components.md#radio-group). <see cref="Value"/> is the
/// <c>Tag</c> (or, if unset, the content) of the checked radio; setting it checks the matching radio.
/// Arrow keys move the selection within the group; <see cref="Tone"/> and <see cref="Size"/> apply to every radio.
/// </summary>
[PseudoClasses(":row")]
public class RadioGroup : ItemsControl
{
    public static readonly StyledProperty<object?> ValueProperty =
        AvaloniaProperty.Register<RadioGroup, object?>(nameof(Value), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<RadioGroup, string?>(nameof(Label));
    public static readonly StyledProperty<Direction> DirectionProperty = AvaloniaProperty.Register<RadioGroup, Direction>(nameof(Direction), Direction.Column);
    public static readonly StyledProperty<Tone> ToneProperty = AvaloniaProperty.Register<RadioGroup, Tone>(nameof(Tone), Tone.Accent);
    public static readonly StyledProperty<ControlSize> SizeProperty = AvaloniaProperty.Register<RadioGroup, ControlSize>(nameof(Size), ControlSize.Medium);

    private static int _groups;
    private readonly string _groupName = "sl-radio-group-" + Interlocked.Increment(ref _groups);
    private bool _syncing;

    static RadioGroup()
    {
        ValueProperty.Changed.AddClassHandler<RadioGroup>((g, e) =>
        {
            g.SyncChecked();
            g.ValueChanged?.Invoke(g, EventArgs.Empty);
        });
        DirectionProperty.Changed.AddClassHandler<RadioGroup>((g, e) => g.PseudoClasses.Set(":row", e.GetNewValue<Direction>() == Direction.Row));
        ToneProperty.Changed.AddClassHandler<RadioGroup>((g, _) => g.SyncRadios());
        SizeProperty.Changed.AddClassHandler<RadioGroup>((g, _) => g.SyncRadios());
        LabelProperty.Changed.AddClassHandler<RadioGroup>((g, e) => AutomationProperties.SetName(g, e.GetNewValue<string?>()));
    }

    public RadioGroup()
    {
        AddHandler(ToggleButton.IsCheckedChangedEvent, OnRadioChanged, RoutingStrategies.Bubble);
    }

    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Direction Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public Tone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public ControlSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public event EventHandler? ValueChanged;

    /// <summary>The radios in this group.</summary>
    public IEnumerable<RadioButton> Radios => this.GetLogicalDescendants().OfType<RadioButton>();

    public static object? ValueOf(RadioButton radio) => radio.Tag ?? radio.Content;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SyncRadios();
        SyncChecked();
    }

    private void SyncRadios()
    {
        foreach (var r in Radios)
        {
            r.GroupName = _groupName;
            if (!r.IsSet(Slate.Avalonia.Sl.ToneProperty) || r.GetValue(Slate.Avalonia.Sl.ToneProperty) != Tone)
                Slate.Avalonia.Sl.SetTone(r, Tone);
            Slate.Avalonia.Sl.SetSize(r, Size);
        }
    }

    private void SyncChecked()
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            foreach (var r in Radios)
                r.IsChecked = Equals(ValueOf(r), Value);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void OnRadioChanged(object? sender, RoutedEventArgs e)
    {
        if (_syncing || e.Source is not RadioButton { IsChecked: true } radio)
            return;
        SetCurrentValue(ValueProperty, ValueOf(radio));
    }
}
