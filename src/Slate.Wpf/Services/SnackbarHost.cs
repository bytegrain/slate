using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Slate.Snackbars;

namespace Slate.Wpf;

/// <summary>
/// Renders an <see cref="ISnackbarService"/> in a corner of its parent (place it last in a Grid covering the window;
/// <see cref="SlateWindow"/> includes one). Announces new snackbars through UI Automation without taking focus.
/// </summary>
public class SnackbarHost : ItemsControl
{
    public static readonly DependencyProperty ServiceProperty = DependencyProperty.Register(
        nameof(Service), typeof(ISnackbarService), typeof(SnackbarHost), new FrameworkPropertyMetadata(null, (d, _) => ((SnackbarHost)d).Attach()));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(SnackbarPosition?), typeof(SnackbarHost), new FrameworkPropertyMetadata(null, (d, _) => ((SnackbarHost)d).Attach()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(SnackbarHost),
        new FrameworkPropertyMetadata("Notifications", (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    static SnackbarHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SnackbarHost), new FrameworkPropertyMetadata(typeof(SnackbarHost)));
        FocusableProperty.OverrideMetadata(typeof(SnackbarHost), new FrameworkPropertyMetadata(false));
    }

    public SnackbarHost()
    {
        AutomationProperties.SetName(this, "Notifications");
        Loaded += (_, _) => Attach();
        Panel.SetZIndex(this, (int)SlateTokens.Z.Snackbar);
    }

    /// <summary>The service to render; defaults to <see cref="SlateServices.Snackbar"/>.</summary>
    public ISnackbarService? Service { get => (ISnackbarService?)GetValue(ServiceProperty); set => SetValue(ServiceProperty, value); }

    /// <summary>Corner/edge to stack in; null uses the service's configuration (Defaults.Snackbar.Position).</summary>
    public SnackbarPosition? Position { get => (SnackbarPosition?)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    /// <summary>Accessible name of the notifications region (default "Notifications").</summary>
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    internal ISnackbarService EffectiveService => Service ?? SlateServices.Snackbar;

    private void Attach()
    {
        var service = EffectiveService;
        if (!ReferenceEquals(ItemsSource, service.Visible))
            ItemsSource = service.Visible;
        ApplyPosition(Position ?? service.Configuration.Position);
    }

    private void ApplyPosition(SnackbarPosition position)
    {
        HorizontalAlignment = position switch
        {
            SnackbarPosition.TopLeft or SnackbarPosition.BottomLeft => HorizontalAlignment.Left,
            SnackbarPosition.TopCenter or SnackbarPosition.BottomCenter => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right,
        };
        VerticalAlignment = position is SnackbarPosition.TopLeft or SnackbarPosition.TopCenter or SnackbarPosition.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is SnackbarItem;

    protected override DependencyObject GetContainerForItemOverride() => new SnackbarItem();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is SnackbarItem container && item is Snackbar snackbar)
            container.Attach(EffectiveService.Queue, snackbar);
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null)
            return;

        foreach (Snackbar s in e.NewItems)
            Announce(s);
    }

    private void Announce(Snackbar s)
    {
        var peer = UIElementAutomationPeer.FromElement(this) ?? UIElementAutomationPeer.CreatePeerForElement(this);
        var text = s.Options.Title is { } title ? $"{title}. {s.Message}" : s.Message;
        var processing = s.Severity is Severity.Error or Severity.Warning
            ? AutomationNotificationProcessing.ImportantAll
            : AutomationNotificationProcessing.All;
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, processing, text, "Slate.Snackbar");
    }
}

/// <summary>One snackbar. Pauses while hovered or focused; Escape dismisses; shows remaining time as a line.</summary>
[TemplatePart(Name = "PART_Action", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Close", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Progress", Type = typeof(FrameworkElement))]
public class SnackbarItem : ContentControl
{
    private static readonly DependencyPropertyKey SnackbarPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Snackbar), typeof(Snackbar), typeof(SnackbarItem), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SnackbarProperty = SnackbarPropertyKey.DependencyProperty;

    private SnackbarQueue? _queue;
    private readonly ScaleTransform _progress = new(1, 1);

    static SnackbarItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SnackbarItem), new FrameworkPropertyMetadata(typeof(SnackbarItem)));
        FocusableProperty.OverrideMetadata(typeof(SnackbarItem), new FrameworkPropertyMetadata(false));
    }

    public Snackbar? Snackbar => (Snackbar?)GetValue(SnackbarProperty);

    /// <summary>Icon for the severity tile (null for Normal).</summary>
    public string? SeverityIcon => Snackbar?.Severity is null or Severity.Normal ? null : Alert.IconFor(Snackbar.Severity);

    internal void Attach(SnackbarQueue queue, Snackbar snackbar)
    {
        _queue = queue;
        SetValue(SnackbarPropertyKey, snackbar);
        DataContext = snackbar;
        AutomationProperties.SetLiveSetting(this, snackbar.Severity is Severity.Error or Severity.Warning
            ? AutomationLiveSetting.Assertive
            : AutomationLiveSetting.Polite);
        StartProgress();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Action") is ButtonBase action)
            action.Click += async (_, _) => { if (_queue is not null && Snackbar is not null) await _queue.InvokeActionAsync(Snackbar); };
        if (GetTemplateChild("PART_Close") is ButtonBase close)
            close.Click += (_, _) => Dismiss();
        if (GetTemplateChild("PART_Progress") is FrameworkElement line)
        {
            line.RenderTransformOrigin = new Point(0, 0.5);
            line.RenderTransform = _progress;
            line.Visibility = Snackbar?.Duration is null || SlateMotion.IsReduced ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Dismiss()
    {
        if (_queue is not null && Snackbar is not null)
            _queue.Dismiss(Snackbar, SnackbarCloseReason.User);
    }

    protected override void OnMouseEnter(MouseEventArgs e) { base.OnMouseEnter(e); Pause(); }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsKeyboardFocusWithin) Resume();
    }

    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        if ((bool)e.NewValue) Pause();
        else if (!IsMouseOver) Resume();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
        }
    }

    private void Pause()
    {
        if (_queue is null || Snackbar is null) return;
        _queue.Pause(Snackbar);
        var current = _progress.ScaleX;
        _progress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _progress.ScaleX = current;
    }

    private void Resume()
    {
        if (_queue is null || Snackbar is null) return;
        _queue.Resume(Snackbar);
        StartProgress();
    }

    private void StartProgress()
    {
        if (Snackbar is not { Duration: { } total, Remaining: { } left } || total <= TimeSpan.Zero || SlateMotion.IsReduced)
            return;
        var from = Math.Clamp(left.TotalMilliseconds / total.TotalMilliseconds, 0, 1);
        _progress.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, 0, left));
    }
}
