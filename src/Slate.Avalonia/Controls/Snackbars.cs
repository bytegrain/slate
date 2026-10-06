using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Slate.Avalonia.Services;
using Slate.Snackbars;

namespace Slate.Avalonia.Controls;

/// <summary>
/// Renders an <see cref="ISnackbarService"/> over its <see cref="ContentControl.Content"/>. One per window;
/// <see cref="SlateWindow"/> includes one. Hovering or focusing a snackbar pauses its timer.
/// </summary>
[TemplatePart("PART_Items", typeof(StackPanel))]
public class SnackbarHost : ContentControl
{
    public static readonly StyledProperty<ISnackbarService?> ServiceProperty =
        AvaloniaProperty.Register<SnackbarHost, ISnackbarService?>(nameof(Service));

    /// <summary>Overrides the service configuration's position for this host.</summary>
    public static readonly StyledProperty<SnackbarPosition?> PositionProperty =
        AvaloniaProperty.Register<SnackbarHost, SnackbarPosition?>(nameof(Position));

    /// <summary>Accessible name of the notification region.</summary>
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<SnackbarHost, string>(nameof(Label), "Notifications");

    private StackPanel? _items;
    private readonly Dictionary<long, SnackbarItem> _views = new();
    private DispatcherTimer? _progressTimer;

    static SnackbarHost()
    {
        ServiceProperty.Changed.AddClassHandler<SnackbarHost>((h, e) => h.OnServiceChanged(e.GetOldValue<ISnackbarService?>(), e.GetNewValue<ISnackbarService?>()));
        PositionProperty.Changed.AddClassHandler<SnackbarHost>((h, _) => h.ApplyPosition());
        LabelProperty.Changed.AddClassHandler<SnackbarHost>((h, e) =>
        {
            if (h._items is not null)
                AutomationProperties.SetName(h._items, e.GetNewValue<string>());
        });
    }

    public ISnackbarService? Service { get => GetValue(ServiceProperty); set => SetValue(ServiceProperty, value); }
    public SnackbarPosition? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public string Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    /// <summary>The snackbar views currently shown, in display order.</summary>
    public IReadOnlyList<SnackbarItem> Items => _items?.Children.OfType<SnackbarItem>().ToList() ?? [];

    public SnackbarPosition EffectivePosition => Position ?? Service?.Configuration.Position ?? SnackbarPosition.BottomRight;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _items = e.NameScope.Find<StackPanel>("PART_Items");
        if (_items is not null)
            AutomationProperties.SetName(_items, Label);
        _views.Clear();
        ApplyPosition();
        Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _progressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => UpdateProgress());
        _progressTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _progressTimer?.Stop();
        _progressTimer = null;
    }

    private void OnServiceChanged(ISnackbarService? oldService, ISnackbarService? newService)
    {
        if (oldService is not null)
            oldService.Changed -= OnChanged;
        if (newService is not null)
            newService.Changed += OnChanged;
        ApplyPosition();
        Sync();
    }

    private void OnChanged(object? sender, EventArgs e) => Sync();

    private void ApplyPosition()
    {
        if (_items is null)
            return;
        var p = EffectivePosition;
        _items.HorizontalAlignment = p switch
        {
            SnackbarPosition.TopLeft or SnackbarPosition.BottomLeft => HorizontalAlignment.Left,
            SnackbarPosition.TopCenter or SnackbarPosition.BottomCenter => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right,
        };
        _items.VerticalAlignment = p is SnackbarPosition.TopLeft or SnackbarPosition.TopCenter or SnackbarPosition.TopRight
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
        // Each snackbar keeps its own width (min..max tokens) and hugs the anchored edge.
        foreach (var view in _items.Children.OfType<SnackbarItem>())
            view.HorizontalAlignment = _items.HorizontalAlignment;
    }

    /// <summary>Reconciles the views with <see cref="ISnackbarService.Visible"/>.</summary>
    internal void Sync()
    {
        if (_items is null)
            return;

        var visible = Service?.Visible ?? [];
        var ids = visible.Select(s => s.Id).ToHashSet();

        foreach (var (id, view) in _views.Where(kv => !ids.Contains(kv.Key)).ToList())
        {
            _items.Children.Remove(view);
            _views.Remove(id);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var snackbar = visible[i];
            if (!_views.TryGetValue(snackbar.Id, out var view))
            {
                view = new SnackbarItem(snackbar, this) { HorizontalAlignment = _items.HorizontalAlignment };
                _views[snackbar.Id] = view;
            }
            view.Refresh();
            var at = _items.Children.IndexOf(view);
            if (at != i)
            {
                if (at >= 0)
                    _items.Children.RemoveAt(at);
                _items.Children.Insert(Math.Min(i, _items.Children.Count), view);
            }
        }
    }

    private void UpdateProgress()
    {
        foreach (var view in _views.Values)
            view.UpdateProgress();
    }
}

/// <summary>One snackbar. Created by <see cref="SnackbarHost"/>; not meant to be placed directly.</summary>
[TemplatePart("PART_Action", typeof(Button))]
[TemplatePart("PART_Close", typeof(Button))]
[PseudoClasses(":normal", ":info", ":success", ":warning", ":error", ":has-title", ":has-action", ":timed", ":paused")]
public class SnackbarItem : TemplatedControl
{
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<SnackbarItem, double>(nameof(Progress), 1);

    private readonly SnackbarHost? _host;
    private readonly Stopwatch _sinceResume = Stopwatch.StartNew();
    private bool _pointerOver;
    private bool _wasPaused;

    public SnackbarItem(Snackbar snackbar, SnackbarHost? host = null)
    {
        Snackbar = snackbar;
        _host = host;
        Focusable = false;
        var o = snackbar.Options;
        PseudoClasses.Set(":" + o.Severity.ToString().ToLowerInvariant(), true);
        PseudoClasses.Set(":has-title", !string.IsNullOrEmpty(o.Title));
        PseudoClasses.Set(":has-action", o.Action is not null);
        PseudoClasses.Set(":timed", snackbar.Duration is not null);
        AutomationProperties.SetLiveSetting(this, o.Severity is Severity.Error or Severity.Warning
            ? global::Avalonia.Automation.AutomationLiveSetting.Assertive
            : global::Avalonia.Automation.AutomationLiveSetting.Polite);
        AutomationProperties.SetName(this, string.IsNullOrEmpty(o.Title) ? o.Message : $"{o.Title}. {o.Message}");
    }

    public Snackbar Snackbar { get; }
    public string? Title => Snackbar.Options.Title;
    public string Message => Snackbar.Options.Message;
    public string? ActionLabel => Snackbar.Options.Action?.Label;
    public bool ShowCloseButton => Snackbar.Options.ShowCloseButton;
    public string IconKind => Alert.IconFor(Snackbar.Severity);
    public bool HasIcon => Snackbar.Severity != Severity.Normal;

    /// <summary>Remaining time as a 0–1 fraction (drives the bottom progress line).</summary>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    private ISnackbarService? Service => _host?.Service;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Action") is { } action)
            action.Click += async (_, _) => { if (Service is { } s) await s.InvokeActionAsync(Snackbar); };
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Service?.Dismiss(Snackbar, SnackbarCloseReason.User);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerOver = true;
        UpdatePause();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = false;
        UpdatePause();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsKeyboardFocusWithinProperty)
            UpdatePause();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && Service is { } s)
        {
            s.Dismiss(Snackbar, SnackbarCloseReason.User);
            e.Handled = true;
        }
    }

    private void UpdatePause()
    {
        if (Service is not { } s)
            return;
        if (_pointerOver || IsKeyboardFocusWithin)
            s.Pause(Snackbar);
        else
            s.Resume(Snackbar);
    }

    internal void Refresh()
    {
        PseudoClasses.Set(":paused", Snackbar.IsPaused);
        if (_wasPaused && !Snackbar.IsPaused)
            _sinceResume.Restart();
        _wasPaused = Snackbar.IsPaused;
        UpdateProgress();
    }

    internal void UpdateProgress()
    {
        if (Snackbar.Duration is not { } total || Snackbar.Remaining is not { } remaining || total <= TimeSpan.Zero)
            return;
        var left = Snackbar.IsPaused ? remaining : remaining - _sinceResume.Elapsed;
        Progress = Math.Clamp(left / total, 0, 1);
    }
}
