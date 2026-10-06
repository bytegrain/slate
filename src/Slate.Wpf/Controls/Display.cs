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

public enum BadgeTone { Neutral, Success, Warning, Danger, Info, Accent }

/// <summary>Small status label (docs/design/components.md#badge).</summary>
public class Badge : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(BadgeTone), typeof(Badge), new FrameworkPropertyMetadata(BadgeTone.Neutral));

    public static readonly DependencyProperty ShowDotProperty = DependencyProperty.Register(
        nameof(ShowDot), typeof(bool), typeof(Badge), new FrameworkPropertyMetadata(false));

    static Badge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));
        FocusableProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
    }

    public BadgeTone Tone { get => (BadgeTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool ShowDot { get => (bool)GetValue(ShowDotProperty); set => SetValue(ShowDotProperty, value); }

    /// <summary>The tone that represents a severity.</summary>
    public static BadgeTone ToneFor(Severity severity) => severity switch
    {
        Severity.Success => BadgeTone.Success,
        Severity.Warning => BadgeTone.Warning,
        Severity.Error => BadgeTone.Danger,
        Severity.Info => BadgeTone.Info,
        _ => BadgeTone.Neutral,
    };
}

/// <summary>Inline persistent message (docs/design/components.md#alert). Content is the message body.</summary>
[TemplatePart(Name = "PART_Close", Type = typeof(ButtonBase))]
public class Alert : ContentControl
{
    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(Severity), typeof(Alert), new FrameworkPropertyMetadata(Severity.Info, OnSeverityChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsDismissibleProperty = DependencyProperty.Register(
        nameof(IsDismissible), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(false));

    public static readonly RoutedEvent DismissedEvent = EventManager.RegisterRoutedEvent(
        nameof(Dismissed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Alert));

    static Alert()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(typeof(Alert)));
        FocusableProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(false));
    }

    public Alert() => AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>Buttons shown at the end of the alert.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    public bool IsDismissible { get => (bool)GetValue(IsDismissibleProperty); set => SetValue(IsDismissibleProperty, value); }

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

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        AutomationProperties.SetLiveSetting(d, (Severity)e.NewValue == Severity.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);

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

public enum CardVariant { Elevated, Outlined }

/// <summary>Surface with optional header bar (Header, Subtitle, HeaderActions), body (Content) and footer (docs/design/layout.md#card).</summary>
public class Card : HeaderedContentControl
{
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HeaderActionsProperty = DependencyProperty.Register(
        nameof(HeaderActions), typeof(object), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(Card), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(CardVariant), typeof(Card), new FrameworkPropertyMetadata(CardVariant.Elevated));

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(Card), new FrameworkPropertyMetadata(false));

    static Card()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(typeof(Card)));
        FocusableProperty.OverrideMetadata(typeof(Card), new FrameworkPropertyMetadata(false));
    }

    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? HeaderActions { get => GetValue(HeaderActionsProperty); set => SetValue(HeaderActionsProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public CardVariant Variant { get => (CardVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }

    /// <summary>Lifts to e2 on hover (for clickable cards).</summary>
    public bool IsInteractive { get => (bool)GetValue(IsInteractiveProperty); set => SetValue(IsInteractiveProperty, value); }
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
