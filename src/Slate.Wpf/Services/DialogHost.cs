using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Slate.Dialogs;
using MessageBoxOptions = Slate.Dialogs.MessageBoxOptions;

namespace Slate.Wpf;

/// <summary>Commands dialog content uses to close its dialog: <c>Command="sl:DialogCommands.Ok" CommandParameter="{Binding Result}"</c>.</summary>
public static class DialogCommands
{
    /// <summary>Closes the enclosing dialog with <see cref="DialogResult.Ok"/>; the command parameter becomes the data.</summary>
    public static readonly RoutedCommand Ok = new(nameof(Ok), typeof(DialogCommands));

    /// <summary>Cancels the enclosing dialog.</summary>
    public static readonly RoutedCommand Cancel = new(nameof(Cancel), typeof(DialogCommands));
}

/// <summary>
/// In-window modal layer for an <see cref="IDialogService"/>. Stacks dialogs, each with its own scrim; only the
/// top one is interactive. <see cref="SlateWindow"/> includes one.
/// </summary>
public class DialogHost : ItemsControl
{
    public static readonly DependencyProperty ServiceProperty = DependencyProperty.Register(
        nameof(Service), typeof(IDialogService), typeof(DialogHost), new FrameworkPropertyMetadata(null, (d, _) => ((DialogHost)d).Attach()));

    /// <summary>Template used for <see cref="MessageBoxOptions"/> content.</summary>
    public static readonly ComponentResourceKey MessageBoxTemplateKey = new(typeof(DialogHost), "MessageBoxTemplate");

    private DialogStack? _stack;
    private readonly Dictionary<DialogReference, IInputElement?> _restoreFocus = new();

    static DialogHost()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogHost), new FrameworkPropertyMetadata(typeof(DialogHost)));
        FocusableProperty.OverrideMetadata(typeof(DialogHost), new FrameworkPropertyMetadata(false));
    }

    public DialogHost()
    {
        Visibility = Visibility.Collapsed;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Panel.SetZIndex(this, (int)SlateTokens.Z.Dialog);
    }

    public IDialogService? Service { get => (IDialogService?)GetValue(ServiceProperty); set => SetValue(ServiceProperty, value); }

    internal DialogStack Stack => _stack ?? (Service ?? SlateServices.Dialogs).Stack;

    private void Attach()
    {
        var stack = (Service ?? SlateServices.Dialogs).Stack;
        if (ReferenceEquals(stack, _stack)) return;
        Detach();
        _stack = stack;
        _stack.Changed += OnStackChanged;
        Sync();
    }

    private void Detach()
    {
        if (_stack is not null)
            _stack.Changed -= OnStackChanged;
        _stack = null;
    }

    private void OnStackChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Sync();
        else Dispatcher.BeginInvoke(Sync);
    }

    /// <summary>Mirrors the stack into Items, remembering and restoring focus around each dialog.</summary>
    internal void Sync()
    {
        if (_stack is null) return;
        var open = _stack.Open;

        foreach (var closed in Items.Cast<DialogReference>().Where(d => !open.Contains(d)).ToList())
        {
            Items.Remove(closed);
            if (_restoreFocus.Remove(closed, out var previous) && previous is UIElement { IsVisible: true } element)
                element.Focus();
        }

        foreach (var dialog in open.Where(d => !Items.Contains(d)))
        {
            _restoreFocus[dialog] = Keyboard.FocusedElement;
            Items.Add(dialog);
        }

        Visibility = Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Only the top dialog is interactive.
        for (var i = 0; i < Items.Count; i++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(i) is DialogFrame frame)
                frame.IsEnabled = i == Items.Count - 1;
        }
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is DialogFrame;

    protected override DependencyObject GetContainerForItemOverride() => new DialogFrame();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is DialogFrame frame && item is DialogReference dialog)
        {
            frame.Attach(this, dialog);
            frame.IsEnabled = ReferenceEquals(dialog, Stack.Top);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape || _stack?.Top is not { } top)
            return;
        // DialogContent may override CloseOnEscape, so ask the frame for the effective rule.
        var frame = ItemContainerGenerator.ContainerFromItem(top) as DialogFrame;
        if ((frame?.EffectiveOptions ?? top.Options).CloseOnEscape && _stack.Close(top, DialogResult.Cancel()))
            e.Handled = true;
    }
}

/// <summary>One dialog: scrim + panel with header (title, close), body (the content) — and focus handling.</summary>
[TemplatePart(Name = "PART_Scrim", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Panel", Type = typeof(FrameworkElement))]
public class DialogFrame : ContentControl
{
    private static readonly DependencyPropertyKey DialogPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Dialog), typeof(DialogReference), typeof(DialogFrame), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty DialogProperty = DialogPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey EffectiveOptionsPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(EffectiveOptions), typeof(DialogOptions), typeof(DialogFrame), new FrameworkPropertyMetadata(new DialogOptions()));

    public static readonly DependencyProperty EffectiveOptionsProperty = EffectiveOptionsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ShowHeaderPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ShowHeader), typeof(bool), typeof(DialogFrame), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty ShowHeaderProperty = ShowHeaderPropertyKey.DependencyProperty;

    private DialogHost? _host;

    static DialogFrame()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogFrame), new FrameworkPropertyMetadata(typeof(DialogFrame)));
        FocusableProperty.OverrideMetadata(typeof(DialogFrame), new FrameworkPropertyMetadata(false));
    }

    public DialogFrame()
    {
        CommandBindings.Add(new CommandBinding(DialogCommands.Ok, (_, e) => Close(DialogResult.Ok(e.Parameter))));
        CommandBindings.Add(new CommandBinding(DialogCommands.Cancel, (_, _) => Close(DialogResult.Cancel())));
    }

    public DialogReference? Dialog => (DialogReference?)GetValue(DialogProperty);

    public DialogOptions Options => Dialog?.Options ?? new DialogOptions();

    /// <summary>The dialog's options with any values set on a <see cref="DialogContent"/> body applied on top.</summary>
    public DialogOptions EffectiveOptions => (DialogOptions)GetValue(EffectiveOptionsProperty);

    /// <summary>False for message boxes, whose template draws its own header.</summary>
    public bool ShowHeader => (bool)GetValue(ShowHeaderProperty);

    internal void Attach(DialogHost host, DialogReference dialog)
    {
        _host = host;
        SetValue(DialogPropertyKey, dialog);
        Content = dialog.Content;
        var effective = dialog.Content is DialogContent body ? body.Merge(dialog.Options) : dialog.Options;
        SetValue(EffectiveOptionsPropertyKey, effective);

        // Icon tile colours: the tone's tint with its text colour (neutral: muted fill, secondary text).
        var keys = Styling.Keys(effective.Tone);
        SetResourceReference(Sl.FillProperty, effective.Tone == Tone.Neutral ? "Sl.Brush.Background.Muted" : keys.Subtle);
        SetResourceReference(Sl.OnFillProperty, effective.Tone == Tone.Neutral ? "Sl.Brush.Text.Secondary" : keys.Text);
        if (dialog.Content is MessageBoxOptions)
        {
            SetValue(ShowHeaderPropertyKey, false);
            SetResourceReference(ContentTemplateProperty, DialogHost.MessageBoxTemplateKey);
        }
        AutomationProperties.SetName(this, effective.Title ?? (dialog.Content as MessageBoxOptions)?.Title ?? "Dialog");
        AutomationProperties.SetHelpText(this, effective.Description ?? (dialog.Content as MessageBoxOptions)?.Message ?? "");
    }

    private void Close(DialogResult result)
    {
        if (_host is not null && Dialog is not null)
            _host.Stack.Close(Dialog, result);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Scrim") is FrameworkElement scrim)
        {
            scrim.MouseLeftButtonDown += (_, e) =>
            {
                if (_host is not null && Dialog is not null && ReferenceEquals(_host.Stack.Top, Dialog)
                    && EffectiveOptions.CloseOnBackdropClick && _host.Stack.Close(Dialog, DialogResult.Cancel()))
                    e.Handled = true;
            };
        }

        if (GetTemplateChild("PART_Panel") is FrameworkElement panel)
        {
            ApplyLayout(panel);
            Animate(scrim: GetTemplateChild("PART_Scrim") as UIElement, panel);
            panel.Loaded += (_, _) => FocusInitial(panel);
        }
    }

    private void ApplyLayout(FrameworkElement panel)
    {
        var o = EffectiveOptions;
        if (o.FullScreen)
        {
            panel.Margin = new Thickness(0);
            panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.VerticalAlignment = VerticalAlignment.Stretch;
            return;
        }

        panel.MaxWidth = DialogOptions.WidthPixels(o.MaxWidth);
        panel.HorizontalAlignment = o.FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        panel.VerticalAlignment = o.Placement == DialogPlacement.Top ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (o.Placement == DialogPlacement.Top)
            panel.Margin = new Thickness(32, 64, 32, 32);
    }

    /// <summary>Focus the element marked with FocusManager.FocusedElement, else the first focusable, else the panel.</summary>
    private static void FocusInitial(FrameworkElement panel)
    {
        if (FocusManager.GetFocusedElement(panel) is UIElement marked && marked.Focus())
            return;
        if (!panel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
            panel.Focus();
    }

    private static void Animate(UIElement? scrim, UIElement panel)
    {
        if (SlateMotion.IsReduced) return;
        var slow = TimeSpan.FromMilliseconds(SlateTokens.Motion.Duration.Slow);
        var ease = new CubicBezierEase(SlateTokens.Motion.Easing.Emphasized);

        scrim?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, slow));
        panel.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.98, 0.98);
        panel.RenderTransform = scale;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, slow) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1, slow) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1, slow) { EasingFunction = ease });
    }
}

/// <summary>
/// Body + footer layout for custom dialog content (footer: background.subtle, actions right-aligned). Options set
/// here override the <see cref="DialogOptions"/> the dialog was opened with (design/api/components.json, Dialog).
/// The contract's <c>MaxWidth</c> is <see cref="DialogMaxWidth"/> here because FrameworkElement.MaxWidth already exists.
/// </summary>
public class DialogContent : ContentControl
{
    private static DependencyProperty Option<T>(string name, T defaultValue) =>
        DependencyProperty.Register(name, typeof(T), typeof(DialogContent), new FrameworkPropertyMetadata(defaultValue));

    public static readonly DependencyProperty FooterProperty = Option<object?>(nameof(Footer), null);
    public static readonly DependencyProperty TitleProperty = Option<string?>(nameof(Title), null);
    public static readonly DependencyProperty DescriptionProperty = Option<string?>(nameof(Description), null);
    public static readonly DependencyProperty IconProperty = Option<string?>(nameof(Icon), null);
    public static readonly DependencyProperty ToneProperty = Option(nameof(Tone), Tone.Neutral);
    public static readonly DependencyProperty DialogMaxWidthProperty = Option(nameof(DialogMaxWidth), DialogWidth.Sm);
    public static readonly DependencyProperty FullWidthProperty = Option(nameof(FullWidth), false);
    public static readonly DependencyProperty FullScreenProperty = Option(nameof(FullScreen), false);
    public static readonly DependencyProperty CloseOnEscapeProperty = Option(nameof(CloseOnEscape), true);
    public static readonly DependencyProperty CloseOnBackdropClickProperty = Option(nameof(CloseOnBackdropClick), true);
    public static readonly DependencyProperty ShowCloseButtonProperty = Option(nameof(ShowCloseButton), true);

    static DialogContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogContent), new FrameworkPropertyMetadata(typeof(DialogContent)));
        FocusableProperty.OverrideMetadata(typeof(DialogContent), new FrameworkPropertyMetadata(false));
    }

    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public DialogWidth DialogMaxWidth { get => (DialogWidth)GetValue(DialogMaxWidthProperty); set => SetValue(DialogMaxWidthProperty, value); }
    public bool FullWidth { get => (bool)GetValue(FullWidthProperty); set => SetValue(FullWidthProperty, value); }
    public bool FullScreen { get => (bool)GetValue(FullScreenProperty); set => SetValue(FullScreenProperty, value); }
    public bool CloseOnEscape { get => (bool)GetValue(CloseOnEscapeProperty); set => SetValue(CloseOnEscapeProperty, value); }
    public bool CloseOnBackdropClick { get => (bool)GetValue(CloseOnBackdropClickProperty); set => SetValue(CloseOnBackdropClickProperty, value); }
    public bool ShowCloseButton { get => (bool)GetValue(ShowCloseButtonProperty); set => SetValue(ShowCloseButtonProperty, value); }

    /// <summary>Applies every option set on this element over <paramref name="options"/>.</summary>
    public DialogOptions Merge(DialogOptions options)
    {
        T Pick<T>(DependencyProperty p, T fallback) => Styling.IsSet(this, p) ? (T)GetValue(p) : fallback;
        return options with
        {
            Title = Pick(TitleProperty, options.Title),
            Description = Pick(DescriptionProperty, options.Description),
            Icon = Pick(IconProperty, options.Icon),
            Tone = Pick(ToneProperty, options.Tone),
            MaxWidth = Pick(DialogMaxWidthProperty, options.MaxWidth),
            FullWidth = Pick(FullWidthProperty, options.FullWidth),
            FullScreen = Pick(FullScreenProperty, options.FullScreen),
            CloseOnEscape = Pick(CloseOnEscapeProperty, options.CloseOnEscape),
            CloseOnBackdropClick = Pick(CloseOnBackdropClickProperty, options.CloseOnBackdropClick),
            ShowCloseButton = Pick(ShowCloseButtonProperty, options.ShowCloseButton),
        };
    }
}

/// <summary>Easing from a token cubic-bezier (x1, y1, x2, y2), solved numerically.</summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezierEase(double[] points) : this(points[0], points[1], points[2], points[3]) { }

    public CubicBezierEase(double x1, double y1, double x2, double y2)
    {
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
        EasingMode = EasingMode.EaseIn; // EaseInCore is used as-is.
    }

    private static double Bezier(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;

    protected override double EaseInCore(double normalizedTime)
    {
        // Binary search t such that x(t) = time, then return y(t).
        double lo = 0, hi = 1, t = normalizedTime;
        for (var i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            if (Bezier(t, _x1, _x2) < normalizedTime) lo = t; else hi = t;
        }
        return Bezier(t, _y1, _y2);
    }

    protected override Freezable CreateInstanceCore() => new CubicBezierEase(_x1, _y1, _x2, _y2);
}
