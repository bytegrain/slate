using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Slate.Wpf;

/// <summary>Inline keyboard hint, e.g. ⌘K (docs/design/components.md#kbd).</summary>
public class Kbd : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(Kbd), new FrameworkPropertyMetadata(null));

    static Kbd()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Kbd), new FrameworkPropertyMetadata(typeof(Kbd)));
        FocusableProperty.OverrideMetadata(typeof(Kbd), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Kbd), new FrameworkPropertyMetadata(false));
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
}

/// <summary>Small status label (docs/design/components.md#badge). Content is the text.</summary>
public class Badge : ContentControl
{
    public static readonly DependencyProperty ToneProperty = Sl.ToneProperty.AddOwner(typeof(Badge));
    public static readonly DependencyProperty SizeProperty = Sl.SizeProperty.AddOwner(typeof(Badge));
    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(Badge));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(BadgeVariant), typeof(Badge), new FrameworkPropertyMetadata(BadgeVariant.Soft, (d, _) => Styling.Refresh(d)));

    public static readonly DependencyProperty DotProperty = DependencyProperty.Register(
        nameof(Dot), typeof(bool), typeof(Badge), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(Badge), new FrameworkPropertyMetadata(null));

    static Badge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));
        FocusableProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
    }

    public Badge() => Sl.SetKind(this, SlKind.Badge);

    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    /// <summary>Soft (default), Solid or Outlined.</summary>
    public BadgeVariant Variant { get => (BadgeVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Small or Medium (Large renders as Medium).</summary>
    public ControlSize Size { get => (ControlSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    /// <summary>Leading 6px dot in the badge's colour.</summary>
    public bool Dot { get => (bool)GetValue(DotProperty); set => SetValue(DotProperty, value); }

    /// <summary>Leading icon name.</summary>
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

/// <summary>Inline persistent message (docs/design/components.md#alert). Content is the message body.</summary>
[TemplatePart(Name = "PART_Close", Type = typeof(ButtonBase))]
public class Alert : ContentControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(Severity), typeof(Alert), new FrameworkPropertyMetadata(Severity.Info, OnAppearanceChanged));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(AlertVariant), typeof(Alert), new FrameworkPropertyMetadata(AlertVariant.Soft, OnAppearanceChanged));

    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(Alert));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(null, OnAppearanceChanged));

    private static readonly DependencyPropertyKey ActualIconPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualIcon), typeof(string), typeof(Alert), new FrameworkPropertyMetadata("info"));

    public static readonly DependencyProperty ActualIconProperty = ActualIconPropertyKey.DependencyProperty;

    public static readonly DependencyProperty DenseProperty = DependencyProperty.Register(
        nameof(Dense), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DismissibleProperty = DependencyProperty.Register(
        nameof(Dismissible), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty LiveProperty = DependencyProperty.Register(
        nameof(Live), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(false, OnAppearanceChanged));

    public static readonly RoutedEvent DismissedEvent = EventManager.RegisterRoutedEvent(
        nameof(Dismissed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Alert));

    static Alert()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(typeof(Alert)));
        FocusableProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(false));
    }

    public Alert()
    {
        Sl.SetKind(this, SlKind.Alert);
        UpdateLive();
    }

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }

    /// <summary>Soft (default), Outlined or Solid.</summary>
    public AlertVariant Variant { get => (AlertVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Overrides the severity icon; "none" hides it.</summary>
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>The icon drawn: <see cref="Icon"/>, else the severity's icon; null when hidden.</summary>
    public string? ActualIcon => (string?)GetValue(ActualIconProperty);

    /// <summary>Tighter padding for alerts inside cards and tables.</summary>
    public bool Dense { get => (bool)GetValue(DenseProperty); set => SetValue(DenseProperty, value); }

    /// <summary>Buttons shown at the end of the alert.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    public bool Dismissible { get => (bool)GetValue(DismissibleProperty); set => SetValue(DismissibleProperty, value); }

    /// <summary>Announce the alert to screen readers when it appears (assertive for errors, polite otherwise).</summary>
    public bool Live { get => (bool)GetValue(LiveProperty); set => SetValue(LiveProperty, value); }

    /// <summary>Raised when the close button is pressed. The alert collapses itself unless the handler marks it handled.</summary>
    public event RoutedEventHandler Dismissed { add => AddHandler(DismissedEvent, value); remove => RemoveHandler(DismissedEvent, value); }

    /// <summary>Icon name for a severity (shared with snackbars and dialogs).</summary>
    public static string IconFor(Severity severity) => severity switch
    {
        Severity.Success => "check-circle",
        Severity.Warning => "alert-triangle",
        Severity.Error => "alert-circle",
        _ => "info",
    };

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var alert = (Alert)d;
        alert.SetValue(ActualIconPropertyKey, alert.Icon switch
        {
            "none" => null,
            { } icon => icon,
            null => IconFor(alert.Severity),
        });
        alert.UpdateLive();
        Styling.Refresh(d);
    }

    private void UpdateLive() =>
        AutomationProperties.SetLiveSetting(this, !Live ? AutomationLiveSetting.Off
            : Severity == Severity.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Close") is ButtonBase close)
        {
            close.Click += (_, _) =>
            {
                var args = new RoutedEventArgs(DismissedEvent, this);
                RaiseEvent(args);
                if (!args.Handled)
                    Visibility = Visibility.Collapsed;
            };
        }
    }
}

/// <summary>
/// Surface with an optional header bar (Title/Subtitle or a custom Header, plus HeaderActions), body (Content) and
/// footer (docs/design/layout.md#card). Options follow design/api/components.json (Card).
/// </summary>
public class Card : HeaderedContentControl
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(CardVariant), typeof(Card), new FrameworkPropertyMetadata(CardVariant.Elevated, (d, _) => Styling.Refresh(d)));

    private static readonly DependencyPropertyKey ActualVariantPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ActualVariant), typeof(CardVariant), typeof(Card), new FrameworkPropertyMetadata(CardVariant.Elevated));

    public static readonly DependencyProperty ActualVariantProperty = ActualVariantPropertyKey.DependencyProperty;

    public static readonly DependencyProperty RadiusProperty = Sl.RadiusProperty.AddOwner(typeof(Card));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions), typeof(object), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty FlushProperty = DependencyProperty.Register(
        nameof(Flush), typeof(bool), typeof(Card), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(Card), new FrameworkPropertyMetadata(false, (d, e) => ((Card)d).Focusable = (bool)e.NewValue));

    public static readonly RoutedEvent ClickEvent = EventManager.RegisterRoutedEvent(
        nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Card));

    private bool _pressed;

    static Card()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(typeof(Card)));
        FocusableProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(false));
    }

    public Card() => Sl.SetKind(this, SlKind.Card);

    /// <summary>Elevated (default), Outlined or Flat. Unset = Defaults.Card.Variant.</summary>
    public CardVariant Variant { get => (CardVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary><see cref="Variant"/> with defaults applied (what the template renders).</summary>
    public CardVariant ActualVariant => (CardVariant)GetValue(ActualVariantProperty);

    public Radius Radius { get => (Radius)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>No body padding (tables, media).</summary>
    public bool Flush { get => (bool)GetValue(FlushProperty); set => SetValue(FlushProperty, value); }

    /// <summary>Lifts on hover, is focusable and raises <see cref="Click"/>.</summary>
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }

    internal void SetActualVariant(CardVariant v) => SetValue(ActualVariantPropertyKey, v);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = Interactive;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressed && Interactive && IsMouseOver)
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        _pressed = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Interactive && !e.Handled && e.Key is Key.Enter or Key.Space && ReferenceEquals(e.OriginalSource, this))
        {
            RaiseEvent(new RoutedEventArgs(ClickEvent, this));
            e.Handled = true;
        }
    }
}

/// <summary>A compact surface row of tools (Alloy .tbar). Use <see cref="ToolbarSeparator"/> between groups.</summary>
public class Toolbar : ItemsControl
{
    static Toolbar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Toolbar), new FrameworkPropertyMetadata(typeof(Toolbar)));
        FocusableProperty.OverrideMetadata(typeof(Toolbar), new FrameworkPropertyMetadata(false));
    }

    public Toolbar()
    {
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(this, "Toolbar");
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolbarAutomationPeer(this);

    private sealed class ToolbarAutomationPeer(Toolbar owner) : ItemsControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ToolBar;
        protected override string GetClassNameCore() => nameof(Toolbar);
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => new ToolbarItemPeer(item, this);
    }

    private sealed class ToolbarItemPeer(object item, ItemsControlAutomationPeer parent) : ItemAutomationPeer(item, parent)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Custom;
        protected override string GetClassNameCore() => "ToolbarItem";
    }
}

public class ToolbarSeparator : Control
{
    static ToolbarSeparator()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ToolbarSeparator), new FrameworkPropertyMetadata(typeof(ToolbarSeparator)));
        FocusableProperty.OverrideMetadata(typeof(ToolbarSeparator), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(ToolbarSeparator), new FrameworkPropertyMetadata(false));
    }
}
