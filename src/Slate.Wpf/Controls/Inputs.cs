using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Slate.Wpf;

/// <summary>
/// Labelled text input (docs/design/components.md#text-field): visible label, helper text, error state,
/// prefix/suffix and start icon. Wires the label, description and invalid state into UI Automation.
/// </summary>
[TemplatePart(Name = PartTextBox, Type = typeof(TextBox))]
public class TextField : Control
{
    public const string PartTextBox = "PART_TextBox";

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null, (d, _) => ((TextField)d).SyncAutomation()));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TextField),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, null, true, UpdateSourceTrigger.PropertyChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HelperTextProperty = DependencyProperty.Register(
        nameof(HelperText), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null, (d, _) => ((TextField)d).SyncAutomation()));

    public static readonly DependencyProperty ErrorProperty = DependencyProperty.Register(
        nameof(Error), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null, OnErrorChanged));

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasError), typeof(bool), typeof(TextField), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsRequiredProperty = DependencyProperty.Register(
        nameof(IsRequired), typeof(bool), typeof(TextField), new FrameworkPropertyMetadata(false, (d, _) => ((TextField)d).SyncAutomation()));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(TextField), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty PrefixProperty = DependencyProperty.Register(
        nameof(Prefix), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty StartIconProperty = DependencyProperty.Register(
        nameof(StartIcon), typeof(string), typeof(TextField), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsMultilineProperty = DependencyProperty.Register(
        nameof(IsMultiline), typeof(bool), typeof(TextField), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(TextField), new FrameworkPropertyMetadata(3));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(ControlSize), typeof(TextField), new FrameworkPropertyMetadata(ControlSize.Medium));

    private TextBox? _textBox;

    static TextField()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(typeof(TextField)));
        FocusableProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(false));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(false));
    }

    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Placeholder { get => (string?)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => (string?)GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }

    /// <summary>Non-empty marks the field invalid and replaces the helper text.</summary>
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public bool HasError => (bool)GetValue(HasErrorProperty);
    public bool IsRequired { get => (bool)GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public string? Prefix { get => (string?)GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string? Suffix { get => (string?)GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public string? StartIcon { get => (string?)GetValue(StartIconProperty); set => SetValue(StartIconProperty, value); }
    public bool IsMultiline { get => (bool)GetValue(IsMultilineProperty); set => SetValue(IsMultilineProperty, value); }
    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The inner TextBox once the template is applied.</summary>
    public TextBox? TextBox => _textBox;

    /// <summary>Text announced as the field's description: the error when invalid, else the helper.</summary>
    public string? Description => HasError ? Error : HelperText;

    private static void OnErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var field = (TextField)d;
        field.SetValue(HasErrorPropertyKey, !string.IsNullOrWhiteSpace((string?)e.NewValue));
        field.SyncAutomation();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textBox = GetTemplateChild(PartTextBox) as TextBox;
        SyncAutomation();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        if (ReferenceEquals(e.NewFocus, this))
            _textBox?.Focus();
    }

    internal void SyncAutomation()
    {
        if (_textBox is null)
            return;

        AutomationProperties.SetName(_textBox, Label ?? "");
        AutomationProperties.SetHelpText(_textBox, Description ?? "");
        AutomationProperties.SetIsRequiredForForm(_textBox, IsRequired);
        AutomationProperties.SetItemStatus(_textBox, HasError ? "Invalid" : "");
        Ui.SetHasError(_textBox, HasError);
    }
}

public enum LabelPosition { Before, After }

/// <summary>
/// On/off switch (docs/design/components.md#switch). A ToggleButton exposed to UI Automation as a
/// "switch"; Space toggles, Enter does not.
/// </summary>
public class Switch : ToggleButton
{
    public static readonly DependencyProperty LabelPositionProperty = DependencyProperty.Register(
        nameof(LabelPosition), typeof(LabelPosition), typeof(Switch), new FrameworkPropertyMetadata(LabelPosition.Before));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(Switch), new FrameworkPropertyMetadata(null));

    static Switch()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Switch), new FrameworkPropertyMetadata(typeof(Switch)));
        IsThreeStateProperty.OverrideMetadata(typeof(Switch), new FrameworkPropertyMetadata(false));
    }

    public LabelPosition LabelPosition { get => (LabelPosition)GetValue(LabelPositionProperty); set => SetValue(LabelPositionProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Enter must not toggle (it submits forms / activates default buttons); Space toggles.
        if (e.Key == Key.Enter)
            return;
        base.OnKeyDown(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchAutomationPeer(this);

    private sealed class SwitchAutomationPeer(Switch owner) : ToggleButtonAutomationPeer(owner)
    {
        protected override string GetLocalizedControlTypeCore() => "switch";
        protected override string GetClassNameCore() => nameof(Switch);
    }
}
