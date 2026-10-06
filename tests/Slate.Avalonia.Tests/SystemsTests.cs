using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Slate.Avalonia.Controls;
using Slate.Avalonia.Services;
using Slate.Dialogs;
using Slate.Snackbars;
using static Slate.Avalonia.Tests.TestHelpers;

namespace Slate.Avalonia.Tests;

public class SnackbarHostTests
{
    private readonly FakeTimeProvider _time = new();

    private (SnackbarService Service, SnackbarHost Host, Window Window) Create(SnackbarConfiguration? config = null)
    {
        var service = new SnackbarService(config, _time);
        var host = new SnackbarHost { Service = service, Content = new Border() };
        return (service, host, Show(host, 1000, 700));
    }

    [AvaloniaFact]
    public void Adds_queues_and_renders_up_to_max_visible()
    {
        var (service, host, w) = Create(new SnackbarConfiguration { MaxVisible = 2 });
        service.Add("One");
        service.Add("Two");
        service.Add("Three");
        Pump(w);
        Assert.Equal(["One", "Two"], host.Items.Select(i => i.Message));
        Assert.Equal(1, service.QueuedCount);
        w.Close();
    }

    [AvaloniaFact]
    public void Timeout_removes_the_view_and_promotes_the_queue()
    {
        var (service, host, w) = Create(new SnackbarConfiguration { MaxVisible = 1 });
        var first = service.Add("Saved");
        service.Add("Next");
        Pump(w);

        _time.Advance(first.Duration!.Value);
        Pump(w);
        Assert.Equal(["Next"], host.Items.Select(i => i.Message));
        w.Close();
    }

    [AvaloniaFact]
    public void Close_button_dismisses_with_user_reason()
    {
        var (service, host, w) = Create();
        var s = service.Add("Saved");
        Pump(w);
        w.Click(host.Items.Single().Part<Button>("PART_Close"));
        Assert.Equal(SnackbarCloseReason.User, s.CloseReason);
        Assert.Empty(host.Items);
        w.Close();
    }

    [AvaloniaFact]
    public void Action_button_runs_the_action_and_closes()
    {
        var (service, host, w) = Create();
        var undone = false;
        var s = service.Add(new SnackbarOptions { Message = "Deleted", Action = SnackbarAction.Create("Undo", () => undone = true) });
        Pump(w);
        var action = host.Items.Single().Part<Button>("PART_Action");
        Assert.True(action.IsVisible);
        Assert.Equal("Undo", action.Content);
        w.Click(action);
        Pump(w);
        Assert.True(undone);
        Assert.Equal(SnackbarCloseReason.Action, s.CloseReason);
        w.Close();
    }

    [AvaloniaFact]
    public void Hover_pauses_and_leaving_resumes()
    {
        var (service, host, w) = Create();
        var s = service.Add(new SnackbarOptions { Message = "Hover me", Duration = TimeSpan.FromSeconds(5) });
        Pump(w);
        var item = host.Items.Single();

        w.Hover(item);
        Assert.True(s.IsPaused);
        _time.Advance(TimeSpan.FromMinutes(1));
        Pump(w);
        Assert.Equal(SnackbarState.Visible, s.State);

        w.MoveTo(new Point(5, 5));
        Assert.False(s.IsPaused);
        _time.Advance(TimeSpan.FromSeconds(5));
        Pump(w);
        Assert.Equal(SnackbarState.Closed, s.State);
        w.Close();
    }

    [AvaloniaFact]
    public void Escape_on_a_focused_snackbar_dismisses_it()
    {
        var (service, host, w) = Create();
        var s = service.Add(new SnackbarOptions { Message = "x", Action = new SnackbarAction("Undo") });
        Pump(w);
        host.Items.Single().Part<Button>("PART_Action").Focus(NavigationMethod.Tab);
        Assert.True(s.IsPaused); // keyboard focus pauses too
        w.Press(Key.Escape);
        Assert.Equal(SnackbarCloseReason.User, s.CloseReason);
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(Severity.Info, AutomationLiveSetting.Polite, true)]
    [InlineData(Severity.Normal, AutomationLiveSetting.Polite, false)]
    [InlineData(Severity.Warning, AutomationLiveSetting.Assertive, true)]
    [InlineData(Severity.Error, AutomationLiveSetting.Assertive, true)]
    public void Severity_sets_announcement_and_icon_tile(Severity severity, AutomationLiveSetting live, bool hasIcon)
    {
        var (service, host, w) = Create();
        service.Add(new SnackbarOptions { Message = "m", Title = "t", Severity = severity });
        Pump(w);
        var item = host.Items.Single();
        Assert.Equal(live, AutomationProperties.GetLiveSetting(item));
        Assert.Equal(hasIcon, item.Part<Border>("PART_IconTile").IsVisible);
        Assert.True(item.Part<TextBlock>("PART_Title").IsVisible);
        Assert.Equal("t. m", AutomationProperties.GetName(item));
        w.Close();
    }

    [AvaloniaTheory]
    [InlineData(SnackbarPosition.BottomRight, HorizontalAlignment.Right, VerticalAlignment.Bottom)]
    [InlineData(SnackbarPosition.TopCenter, HorizontalAlignment.Center, VerticalAlignment.Top)]
    [InlineData(SnackbarPosition.BottomLeft, HorizontalAlignment.Left, VerticalAlignment.Bottom)]
    public void Position_anchors_the_stack(SnackbarPosition position, HorizontalAlignment h, VerticalAlignment v)
    {
        var (_, host, w) = Create(new SnackbarConfiguration { Position = position });
        var items = host.Part<StackPanel>("PART_Items");
        Assert.Equal(h, items.HorizontalAlignment);
        Assert.Equal(v, items.VerticalAlignment);
        Assert.Equal("Notifications", AutomationProperties.GetName(items));
        w.Close();
    }

    [AvaloniaFact]
    public void Item_width_is_clamped_to_snackbar_tokens()
    {
        var (service, host, w) = Create();
        service.Add("Short");
        service.Add(new string('x', 400));
        Pump(w);
        Assert.Equal(SlateTokens.Size.Snackbar.Min, host.Items[0].Bounds.Width);
        Assert.Equal(SlateTokens.Size.Snackbar.Max, host.Items[1].Bounds.Width);
        w.Close();
    }
}

public class DialogHostTests
{
    private static (DialogService Service, DialogHost Host, Button Opener, Window Window) Create()
    {
        var service = new DialogService();
        var opener = new Button { Content = "Open" };
        var host = new DialogHost { Service = service, Content = new StackPanel { Children = { opener } } };
        var w = Show(host, 1000, 700);
        opener.Focus(NavigationMethod.Tab);
        return (service, host, opener, w);
    }

    [AvaloniaFact]
    public async Task Show_renders_title_and_focuses_the_first_field()
    {
        var (service, host, _, w) = Create();
        var name = new TextBox();
        var task = service.ShowAsync(new StackPanel { Children = { new TextBlock { Text = "Name" }, name } }, new DialogOptions { Title = "Rename" });
        Pump(w);

        var dialog = Assert.Single(host.Dialogs);
        Assert.Equal("Rename", dialog.Part<TextBlock>("PART_Title").Text);
        Assert.True(dialog.IsTop);
        Assert.Same(name, w.Focused());

        service.Close(service.Stack.Top!, DialogResult.Ok("new-name"));
        var result = await task;
        Assert.Equal("new-name", result.GetData<string>());
        w.Close();
    }

    [AvaloniaFact]
    public async Task Escape_cancels_and_restores_focus()
    {
        var (service, host, opener, w) = Create();
        var task = service.ShowAsync(new TextBox());
        Pump(w);
        Assert.NotSame(opener, w.Focused());

        w.Press(Key.Escape);
        Assert.True((await task).Canceled);
        Assert.Empty(host.Dialogs);
        Assert.Same(opener, w.Focused());
        w.Close();
    }

    [AvaloniaFact]
    public void Escape_is_ignored_when_disallowed_and_never_reaches_the_app()
    {
        var (service, host, _, w) = Create();
        var appSawEscape = false;
        host.AddHandler(InputElement.KeyDownEvent, (_, e) => appSawEscape |= e.Key == Key.Escape, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: false);
        _ = service.ShowAsync(new TextBox(), new DialogOptions { CloseOnEscape = false });
        Pump(w);
        w.Press(Key.Escape);
        Assert.Single(host.Dialogs);
        Assert.False(appSawEscape);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Backdrop_click_cancels_only_when_allowed()
    {
        var (service, host, _, w) = Create();
        var locked = service.ShowAsync(new TextBlock { Text = "x" }, new DialogOptions { CloseOnBackdropClick = false });
        Pump(w);
        w.Click(host.Dialogs.Single().Part<Border>("PART_Scrim"));
        Assert.Single(host.Dialogs);

        service.Close(service.Stack.Top!);
        await locked;
        var open = service.ShowAsync(new TextBlock { Text = "y" });
        Pump(w);
        w.ClickAt(new Point(5, 5));
        Assert.True((await open).Canceled);
        w.Close();
    }

    [AvaloniaFact]
    public void Width_comes_from_dialog_width_tokens()
    {
        var (service, host, _, w) = Create();
        _ = service.ShowAsync(new TextBlock { Text = "wide" }, new DialogOptions { MaxWidth = DialogWidth.Md, FullWidth = true });
        Pump(w);
        Assert.Equal(SlateTokens.Size.Dialog.Md, host.Dialogs.Single().Part<Border>("PART_Panel").Bounds.Width);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Confirm_destructive_uses_danger_styling_and_safe_focus()
    {
        var (service, host, _, w) = Create();
        var task = service.ConfirmAsync(new MessageBoxOptions { Title = "Delete Slate.Wpf?", Message = "This can't be undone.", ConfirmText = "Delete", Destructive = true });
        Pump(w);
        var dialog = host.Dialogs.Single();

        Assert.True(dialog.IsDestructive);
        Assert.True(dialog.Part<Border>("PART_IconTile").IsVisible);
        Assert.Equal("alert-triangle", dialog.IconKind);
        Assert.Contains("solid", dialog.ConfirmButton!.Classes);
        Assert.Contains("tone-danger", dialog.ConfirmButton!.Classes);
        Assert.Same(dialog.CancelButton, w.Focused()); // destructive: cancel is the safe default

        // A destructive confirmation can't be dismissed by clicking outside it.
        w.ClickAt(new Point(5, 5));
        Assert.Single(host.Dialogs);

        w.Click(dialog.ConfirmButton);
        Assert.True(await task);
        w.Close();
    }

    [AvaloniaFact]
    public async Task Confirm_cancel_button_returns_false_and_alert_has_one_button()
    {
        var (service, host, _, w) = Create();
        var confirm = service.ConfirmAsync(new MessageBoxOptions { Message = "Continue?" });
        Pump(w);
        var dialog = host.Dialogs.Single();
        Assert.Contains("solid", dialog.ConfirmButton!.Classes);
        Assert.Contains("tone-accent", dialog.ConfirmButton!.Classes);
        w.Click(dialog.CancelButton!);
        Assert.False(await confirm);

        var alert = service.AlertAsync("Done.");
        Pump(w);
        Assert.Null(host.Dialogs.Single().CancelButton);
        w.Click(host.Dialogs.Single().ConfirmButton!);
        await alert;
        w.Close();
    }

    [AvaloniaFact]
    public async Task Stacked_dialogs_only_the_top_is_interactive()
    {
        var (service, host, _, w) = Create();
        var first = service.ShowAsync(new TextBox());
        Pump(w);
        var second = service.ShowAsync(new TextBox());
        Pump(w);

        Assert.Equal(2, host.Dialogs.Count);
        Assert.False(host.Dialogs[0].IsTop);
        Assert.False(host.Dialogs[0].IsHitTestVisible);
        Assert.True(host.Dialogs[1].IsTop);

        w.Press(Key.Escape);
        Assert.True((await second).Canceled);
        Assert.True(host.Dialogs.Single().IsTop);
        w.Press(Key.Escape);
        await first;
        w.Close();
    }

    [AvaloniaFact]
    public async Task Dialog_content_footer_and_close_with_buttons()
    {
        var (service, host, _, w) = Create();
        var save = new Button { Content = "Save", Classes = { "solid", "tone-accent" } };
        DialogHost.SetCloseWith(save, DialogCloseAction.Ok);
        DialogHost.SetResultData(save, 7);
        var content = new DialogContent { Description = "Pick a name.", Content = new TextBox(), Footer = new StackPanel { Children = { save } } };
        var task = service.ShowAsync(content, new DialogOptions { Title = "Rename" });
        Pump(w);

        var dialog = host.Dialogs.Single();
        Assert.True(dialog.Part<Border>("PART_Footer").IsVisible);
        Assert.Equal("Pick a name.", dialog.Part<TextBlock>("PART_Description").Text);
        w.Click(save);
        Assert.Equal(7, (await task).GetData<int>());
        w.Close();
    }

    [AvaloniaFact]
    public async Task Tab_stays_inside_the_dialog()
    {
        var (service, host, opener, w) = Create();
        TextBox a = new(), b = new();
        _ = service.ShowAsync(new StackPanel { Children = { a, b } }, new DialogOptions { ShowCloseButton = false });
        Pump(w);
        for (var i = 0; i < 5; i++)
        {
            w.Press(Key.Tab);
            Assert.NotSame(opener, w.Focused());
        }
        service.Stack.CloseAll();
        Pump(w);
        await Task.CompletedTask;
        w.Close();
    }

    [AvaloniaFact]
    public void Show_view_by_type_sets_data_context()
    {
        var (service, host, _, w) = Create();
        _ = service.ShowAsync<TextBlock>(dataContext: "vm");
        Pump(w);
        Assert.Equal("vm", ((Control)host.Dialogs.Single().Body!).DataContext);
        w.Close();
    }
}

public class ServiceRegistrationTests
{
    [AvaloniaFact]
    public void Add_slate_registers_singletons_and_sets_defaults()
    {
        var (snackbars, dialogs) = (SlateServices.Snackbars, SlateServices.Dialogs);
        try
        {
            var provider = new ServiceCollection().AddSlate(new SnackbarConfiguration { MaxVisible = 1 }).BuildServiceProvider();
            var s = provider.GetRequiredService<ISnackbarService>();
            Assert.Same(s, provider.GetRequiredService<ISnackbarService>());
            Assert.Same(s, SlateServices.Snackbars);
            Assert.Same(provider.GetRequiredService<IDialogService>(), SlateServices.Dialogs);
            Assert.Equal(1, s.Configuration.MaxVisible);
        }
        finally
        {
            SlateServices.Snackbars = snackbars;
            SlateServices.Dialogs = dialogs;
        }
    }

    [AvaloniaFact]
    public void Slate_window_hosts_dialogs_and_snackbars_with_a_custom_title_bar()
    {
        var window = new SlateWindow { Title = "Slate", Width = 900, Height = 600, Content = new TextBlock { Text = "Body" } };
        window.Show();
        Pump(window);

        Assert.Equal(SlateTokens.Size.Titlebar, window.Part<Border>("PART_TitleBar").Bounds.Height);
        Assert.NotNull(window.FindPart<Button>("PART_Minimize"));
        Assert.NotNull(window.FindPart<Button>("PART_Maximize"));
        Assert.NotNull(window.FindPart<Button>("PART_Close"));
        Assert.Same(SlateServices.Dialogs, FindHost<DialogHost>(window).Service);
        Assert.Same(SlateServices.Snackbars, FindHost<SnackbarHost>(window).Service);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        window.Close();
    }

    private static T FindHost<T>(Window w) where T : Control =>
        global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(w).OfType<T>().Single();
}
