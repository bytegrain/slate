using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Slate.Avalonia.Services;
using Slate.Dialogs;

namespace Slate.Avalonia.Controls;

/// <summary>What a button inside a dialog does when clicked (<c>sl:DialogHost.CloseWith="Ok"</c>).</summary>
public enum DialogCloseAction
{
    None,
    Ok,
    Cancel,
}

/// <summary>
/// Structured dialog content: a body (<see cref="ContentControl.Content"/>), an optional description under the
/// title, and a <see cref="Footer"/> for actions (right-aligned, primary last).
/// </summary>
public class DialogContent : ContentControl
{
    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<DialogContent, string?>(nameof(Description));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<DialogContent, object?>(nameof(Footer));

    public string? Description { get => GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
}

/// <summary>
/// Renders an <see cref="IDialogService"/>'s stack over its <see cref="ContentControl.Content"/>: one scrim and
/// panel per open dialog. Handles Escape and backdrop clicks per <see cref="DialogOptions"/>, traps Tab inside
/// the top dialog, focuses its first field and restores focus on close.
/// </summary>
[TemplatePart("PART_Overlay", typeof(Panel))]
public class DialogHost : ContentControl
{
    public static readonly StyledProperty<IDialogService?> ServiceProperty =
        AvaloniaProperty.Register<DialogHost, IDialogService?>(nameof(Service));

    /// <summary>Marks the element that receives focus when its dialog opens.</summary>
    public static readonly AttachedProperty<bool> AutoFocusProperty =
        AvaloniaProperty.RegisterAttached<DialogHost, Control, bool>("AutoFocus");

    public static readonly AttachedProperty<DialogCloseAction> CloseWithProperty =
        AvaloniaProperty.RegisterAttached<DialogHost, Button, DialogCloseAction>("CloseWith");

    /// <summary>Data returned with <see cref="DialogCloseAction.Ok"/> (defaults to the button's DataContext).</summary>
    public static readonly AttachedProperty<object?> ResultDataProperty =
        AvaloniaProperty.RegisterAttached<DialogHost, Button, object?>("ResultData");

    private Panel? _overlay;
    private readonly Dictionary<long, DialogContainer> _views = new();

    static DialogHost()
    {
        ServiceProperty.Changed.AddClassHandler<DialogHost>((h, e) => h.OnServiceChanged(e.GetOldValue<IDialogService?>(), e.GetNewValue<IDialogService?>()));
        Button.ClickEvent.AddClassHandler<Button>(OnAnyButtonClick);
    }

    public DialogHost()
    {
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    public IDialogService? Service { get => GetValue(ServiceProperty); set => SetValue(ServiceProperty, value); }

    public static bool GetAutoFocus(Control c) => c.GetValue(AutoFocusProperty);
    public static void SetAutoFocus(Control c, bool v) => c.SetValue(AutoFocusProperty, v);
    public static DialogCloseAction GetCloseWith(Button b) => b.GetValue(CloseWithProperty);
    public static void SetCloseWith(Button b, DialogCloseAction v) => b.SetValue(CloseWithProperty, v);
    public static object? GetResultData(Button b) => b.GetValue(ResultDataProperty);
    public static void SetResultData(Button b, object? v) => b.SetValue(ResultDataProperty, v);

    /// <summary>Open dialog views, bottom to top.</summary>
    public IReadOnlyList<DialogContainer> Dialogs => _overlay?.Children.OfType<DialogContainer>().ToList() ?? [];

    /// <summary>Closes the dialog that contains <paramref name="element"/>.</summary>
    public static bool Close(Control element, DialogResult? result = null)
    {
        var container = element.FindAncestorOfType<DialogContainer>(includeSelf: true);
        return container?.Host?.Service?.Close(container.Dialog, result) ?? false;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _overlay = e.NameScope.Find<Panel>("PART_Overlay");
        _views.Clear();
        Sync();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        foreach (var v in _views.Values)
            v.PanelMaxHeight = Math.Max(120, e.NewSize.Height - 64);
    }

    private void OnServiceChanged(IDialogService? oldService, IDialogService? newService)
    {
        if (oldService is not null)
            oldService.Stack.Changed -= OnStackChanged;
        if (newService is not null)
            newService.Stack.Changed += OnStackChanged;
        Sync();
    }

    private void OnStackChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Sync();
        else
            Dispatcher.UIThread.Post(Sync);
    }

    internal void Sync()
    {
        if (_overlay is null)
            return;

        var open = Service?.Stack.Open ?? [];
        var ids = open.Select(d => d.Id).ToHashSet();

        foreach (var (id, view) in _views.Where(kv => !ids.Contains(kv.Key)).ToList())
        {
            _overlay.Children.Remove(view);
            _views.Remove(id);
            var restore = view.RestoreFocusTo;
            Dispatcher.UIThread.Post(() =>
            {
                if (restore is Visual { } v && v.IsAttachedToVisualTree() && restore.IsEffectivelyEnabled)
                    restore.Focus();
            });
        }

        foreach (var dialog in open)
        {
            if (_views.ContainsKey(dialog.Id))
                continue;
            var view = new DialogContainer(dialog, this)
            {
                RestoreFocusTo = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement(),
                PanelMaxHeight = Bounds.Height > 0 ? Math.Max(120, Bounds.Height - 64) : double.PositiveInfinity,
            };
            _views[dialog.Id] = view;
            _overlay.Children.Add(view);
            Dispatcher.UIThread.Post(view.FocusInitial, DispatcherPriority.Loaded);
        }

        // Only the top dialog is interactive.
        var top = open.Count > 0 ? open[^1].Id : -1;
        foreach (var (id, view) in _views)
            view.IsTop = id == top;
        _overlay.IsVisible = _views.Count > 0;
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Service is { } s && s.Stack.Top is not null)
        {
            s.Stack.HandleEscape();
            e.Handled = true; // never let Escape reach the app behind a modal
        }
    }

    private static void OnAnyButtonClick(Button button, RoutedEventArgs e)
    {
        var action = GetCloseWith(button);
        if (action == DialogCloseAction.None)
            return;
        var data = button.IsSet(ResultDataProperty) ? GetResultData(button) : button.DataContext;
        Close(button, action == DialogCloseAction.Ok ? DialogResult.Ok(data) : DialogResult.Cancel());
    }
}

/// <summary>The scrim and panel for one open dialog. Created by <see cref="DialogHost"/>.</summary>
[TemplatePart("PART_Scrim", typeof(Border))]
[TemplatePart("PART_Panel", typeof(Border))]
[TemplatePart("PART_Close", typeof(Button))]
[PseudoClasses(":top", ":destructive", ":has-icon", ":has-footer", ":has-title", ":full-screen", ":placement-top")]
public class DialogContainer : TemplatedControl
{
    public static readonly StyledProperty<double> PanelMaxHeightProperty =
        AvaloniaProperty.Register<DialogContainer, double>(nameof(PanelMaxHeight), double.PositiveInfinity);

    public static readonly StyledProperty<bool> IsTopProperty = AvaloniaProperty.Register<DialogContainer, bool>(nameof(IsTop));

    private Border? _panel;

    public DialogContainer(DialogReference dialog, DialogHost? host = null)
    {
        Dialog = dialog;
        Host = host;
        var o = dialog.Options;
        Title = o.Title;
        PanelMaxWidth = o.FullScreen ? double.PositiveInfinity : DialogOptions.WidthPixels(o.MaxWidth);

        switch (dialog.Content)
        {
            case MessageBoxOptions box:
                Title ??= box.Title;
                Description = null;
                Body = new TextBlock { Text = box.Message, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, Classes = { "dialog-message" } };
                Footer = BuildMessageBoxFooter(box);
                IconKind = box.Destructive ? "alert-triangle" : box.Severity switch
                {
                    Severity.Normal => null,
                    var s => Alert.IconFor(s),
                };
                IsDestructive = box.Destructive;
                break;
            case DialogContent content:
                Description = content.Description;
                Footer = content.Footer;
                Body = content;
                break;
            default:
                Body = dialog.Content;
                break;
        }

        PseudoClasses.Set(":destructive", IsDestructive);
        PseudoClasses.Set(":has-icon", IconKind is not null);
        PseudoClasses.Set(":has-footer", Footer is not null);
        PseudoClasses.Set(":has-title", !string.IsNullOrEmpty(Title));
        PseudoClasses.Set(":full-screen", o.FullScreen);
        PseudoClasses.Set(":placement-top", o.Placement == DialogPlacement.Top);
        ShowCloseButton = o.ShowCloseButton;
        // Footer content is presented by the container, so it needs the dialog's data context too.
        DataContext = dialog.Content switch
        {
            MessageBoxOptions => null,
            Control ctl => ctl.DataContext,
            var vm => vm,
        };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    public DialogReference Dialog { get; }
    public DialogHost? Host { get; }
    public string? Title { get; }
    public string? Description { get; }
    public object? Body { get; }
    public object? Footer { get; }
    public string? IconKind { get; }
    public bool IsDestructive { get; }
    public bool ShowCloseButton { get; }
    public double PanelMaxWidth { get; }

    /// <summary>The panel stretches to <see cref="PanelMaxWidth"/> instead of sizing to content.</summary>
    public bool FullWidth => Dialog.Options.FullWidth || Dialog.Options.FullScreen;

    public double PanelMaxHeight { get => GetValue(PanelMaxHeightProperty); set => SetValue(PanelMaxHeightProperty, value); }

    public bool IsTop
    {
        get => GetValue(IsTopProperty);
        set
        {
            SetValue(IsTopProperty, value);
            PseudoClasses.Set(":top", value);
            IsHitTestVisible = value;
        }
    }

    internal IInputElement? RestoreFocusTo { get; init; }

    /// <summary>The confirm button of a message box (null for other content).</summary>
    public Button? ConfirmButton { get; private set; }

    public Button? CancelButton { get; private set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _panel = e.NameScope.Find<Border>("PART_Panel");
        if (_panel is not null)
        {
            _panel.HorizontalAlignment = FullWidth ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            AutomationProperties.SetName(_panel, Title);
            if (Description is not null)
                AutomationProperties.SetHelpText(_panel, Description);
        }
        if (e.NameScope.Find<Border>("PART_Scrim") is { } scrim)
            scrim.PointerPressed += (_, args) =>
            {
                Host?.Service?.Stack.HandleBackdropClick(Dialog);
                args.Handled = true;
            };
        if (e.NameScope.Find<Button>("PART_Close") is { } close)
            close.Click += (_, _) => Host?.Service?.Close(Dialog, DialogResult.Cancel());
    }

    /// <summary>Focus order: an AutoFocus element, else the first focusable control in the body, else the panel.</summary>
    internal void FocusInitial()
    {
        if (_panel is null || !IsTop)
            return;
        var focusables = _panel.GetVisualDescendants().OfType<InputElement>()
            .Where(x => x.Focusable && x.IsEffectivelyVisible && x.IsEffectivelyEnabled).ToList();
        var target = focusables.OfType<Control>().FirstOrDefault(GetAutoFocus)
                     ?? (ConfirmButton is { } c && IsDestructive ? CancelButton : null) // destructive: safe default
                     ?? focusables.FirstOrDefault(x => x is not Button { Name: "PART_Close" })
                     ?? focusables.FirstOrDefault();
        if (target is not null)
            target.Focus(NavigationMethod.Tab);
        else
            _panel.Focus();
    }

    private static bool GetAutoFocus(Control c) => DialogHost.GetAutoFocus(c);

    private Control BuildMessageBoxFooter(MessageBoxOptions box)
    {
        var row = new Stack { Orientation = Orientation.Horizontal, Spacing = 2, Justify = StackJustify.End };
        if (box.CancelText is { } cancelText)
        {
            CancelButton = new Button { Content = cancelText };
            DialogHost.SetCloseWith(CancelButton, DialogCloseAction.Cancel);
            row.Children.Add(CancelButton);
        }
        ConfirmButton = new Button { Content = box.ConfirmText, IsDefault = !box.Destructive };
        Sl.SetVariant(ConfirmButton, box.Destructive ? ButtonVariant.DangerSolid : ButtonVariant.Primary);
        DialogHost.SetCloseWith(ConfirmButton, DialogCloseAction.Ok);
        DialogHost.SetResultData(ConfirmButton, true);
        row.Children.Add(ConfirmButton);
        return row;
    }
}
