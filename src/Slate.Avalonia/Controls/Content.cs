using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Surface with optional header bar (title, subtitle, actions), body (<see cref="ContentControl.Content"/>)
/// and footer. <c>IsOutlined</c> swaps the shadow for a hairline; <c>IsInteractive</c> lifts on hover.
/// </summary>
[PseudoClasses(":has-header", ":has-footer")]
public class Card : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty = AvaloniaProperty.Register<Card, object?>(nameof(Header));
    public static readonly StyledProperty<string?> SubtitleProperty = AvaloniaProperty.Register<Card, string?>(nameof(Subtitle));
    public static readonly StyledProperty<object?> HeaderActionsProperty = AvaloniaProperty.Register<Card, object?>(nameof(HeaderActions));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<Card, object?>(nameof(Footer));
    public static readonly StyledProperty<bool> IsOutlinedProperty = AvaloniaProperty.Register<Card, bool>(nameof(IsOutlined));
    public static readonly StyledProperty<bool> IsInteractiveProperty = AvaloniaProperty.Register<Card, bool>(nameof(IsInteractive));

    static Card()
    {
        HeaderProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        HeaderActionsProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        FooterProperty.Changed.AddClassHandler<Card>((c, _) => c.UpdateParts());
        IsOutlinedProperty.Changed.AddClassHandler<Card>((c, e) => c.PseudoClasses.Set(":outlined", e.GetNewValue<bool>()));
        IsInteractiveProperty.Changed.AddClassHandler<Card>((c, e) => c.PseudoClasses.Set(":interactive", e.GetNewValue<bool>()));
    }

    public object? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool IsOutlined { get => GetValue(IsOutlinedProperty); set => SetValue(IsOutlinedProperty, value); }
    public bool IsInteractive { get => GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }

    private void UpdateParts()
    {
        PseudoClasses.Set(":has-header", Header is not null || HeaderActions is not null);
        PseudoClasses.Set(":has-footer", Footer is not null);
    }
}

/// <summary>Inline, persistent message with a severity icon, title, message (Content), actions and optional close.</summary>
[PseudoClasses(":normal", ":info", ":success", ":warning", ":error", ":dismissible")]
public class Alert : ContentControl
{
    public static readonly StyledProperty<Severity> SeverityProperty = AvaloniaProperty.Register<Alert, Severity>(nameof(Severity), Severity.Info);
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Alert, string?>(nameof(Title));
    public static readonly StyledProperty<object?> ActionsProperty = AvaloniaProperty.Register<Alert, object?>(nameof(Actions));
    public static readonly StyledProperty<bool> IsDismissibleProperty = AvaloniaProperty.Register<Alert, bool>(nameof(IsDismissible));

    public static readonly RoutedEvent<RoutedEventArgs> DismissedEvent =
        RoutedEvent.Register<Alert, RoutedEventArgs>(nameof(Dismissed), RoutingStrategies.Bubble);

    static Alert()
    {
        SeverityProperty.Changed.AddClassHandler<Alert>((a, _) => a.UpdateSeverity());
        IsDismissibleProperty.Changed.AddClassHandler<Alert>((a, e) => a.PseudoClasses.Set(":dismissible", e.GetNewValue<bool>()));
    }

    public Alert() => UpdateSeverity();

    public Severity Severity { get => GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public bool IsDismissible { get => GetValue(IsDismissibleProperty); set => SetValue(IsDismissibleProperty, value); }

    /// <summary>Icon name for the current severity.</summary>
    public string IconKind => IconFor(Severity);

    public event EventHandler<RoutedEventArgs>? Dismissed
    {
        add => AddHandler(DismissedEvent, value);
        remove => RemoveHandler(DismissedEvent, value);
    }

    public static string IconFor(Severity s) => s switch
    {
        Severity.Success => "check-circle",
        Severity.Warning => "alert-triangle",
        Severity.Error => "alert-circle",
        _ => "info",
    };

    /// <summary>Hides the alert and raises <see cref="Dismissed"/>.</summary>
    public void Dismiss()
    {
        IsVisible = false;
        RaiseEvent(new RoutedEventArgs(DismissedEvent));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Dismiss();
    }

    private void UpdateSeverity()
    {
        foreach (var s in Enum.GetValues<Severity>())
            PseudoClasses.Set(":" + s.ToString().ToLowerInvariant(), s == Severity);
        // Errors announce assertively; everything else politely (only matters when the alert appears dynamically).
        AutomationProperties.SetLiveSetting(this, Severity == Severity.Error
            ? global::Avalonia.Automation.AutomationLiveSetting.Assertive
            : global::Avalonia.Automation.AutomationLiveSetting.Polite);
    }
}

/// <summary>
/// A labelled text input (docs/design/components.md#text-field): visible label, required marker, helper or
/// error text, prefix/suffix segments and a start icon around a native <see cref="TextBox"/>.
/// The label names the TextBox and the helper/error text becomes its automation help text.
/// </summary>
[PseudoClasses(":invalid", ":required", ":has-prefix", ":has-suffix")]
public class TextField : TemplatedControl
{
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Label));
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TextField, string?>(nameof(Text), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> PlaceholderProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Placeholder));
    public static readonly StyledProperty<string?> HelperTextProperty = AvaloniaProperty.Register<TextField, string?>(nameof(HelperText));
    public static readonly StyledProperty<string?> ErrorProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Error));
    public static readonly StyledProperty<bool> IsRequiredProperty = AvaloniaProperty.Register<TextField, bool>(nameof(IsRequired));
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<TextField, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<string?> PrefixProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Prefix));
    public static readonly StyledProperty<string?> SuffixProperty = AvaloniaProperty.Register<TextField, string?>(nameof(Suffix));
    public static readonly StyledProperty<string?> StartIconProperty = AvaloniaProperty.Register<TextField, string?>(nameof(StartIcon));
    public static readonly StyledProperty<char> PasswordCharProperty = AvaloniaProperty.Register<TextField, char>(nameof(PasswordChar));
    public static readonly StyledProperty<bool> IsMultilineProperty = AvaloniaProperty.Register<TextField, bool>(nameof(IsMultiline));
    public static readonly StyledProperty<int> RowsProperty = AvaloniaProperty.Register<TextField, int>(nameof(Rows), 3);

    private TextBox? _input;

    static TextField()
    {
        ErrorProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        HelperTextProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        IsRequiredProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        PrefixProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        SuffixProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        LabelProperty.Changed.AddClassHandler<TextField>((f, _) => f.UpdateState());
        FocusableProperty.OverrideDefaultValue<TextField>(false);
    }

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string? HelperText { get => GetValue(HelperTextProperty); set => SetValue(HelperTextProperty, value); }

    /// <summary>Error message. Non-empty makes the field invalid and replaces the helper text.</summary>
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public bool IsRequired { get => GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public string? Prefix { get => GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string? Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public string? StartIcon { get => GetValue(StartIconProperty); set => SetValue(StartIconProperty, value); }
    public char PasswordChar { get => GetValue(PasswordCharProperty); set => SetValue(PasswordCharProperty, value); }
    public bool IsMultiline { get => GetValue(IsMultilineProperty); set => SetValue(IsMultilineProperty, value); }
    public int Rows { get => GetValue(RowsProperty); set => SetValue(RowsProperty, value); }

    public bool IsInvalid => !string.IsNullOrEmpty(Error);

    /// <summary>The inner TextBox (after the template is applied).</summary>
    public TextBox? Input => _input;

    /// <summary>The text shown under the field: the error if invalid, else the helper text.</summary>
    public string? Description => IsInvalid ? Error : HelperText;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _input = e.NameScope.Find<TextBox>("PART_Input");
        UpdateState();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.Source == this)
            _input?.Focus();
    }

    private void UpdateState()
    {
        PseudoClasses.Set(":invalid", IsInvalid);
        PseudoClasses.Set(":required", IsRequired);
        PseudoClasses.Set(":has-prefix", !string.IsNullOrEmpty(Prefix));
        PseudoClasses.Set(":has-suffix", !string.IsNullOrEmpty(Suffix));

        if (_input is null)
            return;
        AutomationProperties.SetName(_input, Label);
        AutomationProperties.SetHelpText(_input, Description);
        AutomationProperties.SetIsRequiredForForm(_input, IsRequired);
        // A class rather than DataValidationErrors: the field renders its own error line.
        _input.Classes.Set("invalid", IsInvalid);
    }
}
