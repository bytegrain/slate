using System.Windows;
using System.Windows.Controls;
using Slate.Snackbars;

namespace Slate.Wpf.Demo.Pages;

public partial class FeedbackPage : UserControl
{
    private static ISnackbarService Snackbar => SlateServices.Snackbar;

    public FeedbackPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Snackbar.Queue.Closed += OnClosed;
        Unloaded += (_, _) => Snackbar.Queue.Closed -= OnClosed;
    }

    private void OnClosed(object? sender, SnackbarClosedEventArgs e) =>
        Dispatcher.BeginInvoke(() => LastClosed.Text = $"Last closed: \"{e.Snackbar.Message}\" — {e.Reason}");

    private void OnSimple(object sender, RoutedEventArgs e) => Snackbar.Add("Draft saved");

    private void OnSuccess(object sender, RoutedEventArgs e) =>
        Snackbar.Add(new SnackbarOptions { Title = "Deployed", Message = "slate-web is live in 3 regions.", Severity = Severity.Success });

    private void OnError(object sender, RoutedEventArgs e) =>
        Snackbar.Add(new SnackbarOptions { Title = "Couldn't sync", Message = "Check your connection and try again.", Severity = Severity.Error });

    private void OnAction(object sender, RoutedEventArgs e) =>
        Snackbar.Add(new SnackbarOptions
        {
            Message = "Moved 3 files to the bin",
            Action = SnackbarAction.Create("Undo", () => Snackbar.Add("Restored 3 files", Severity.Success)),
        });

    private void OnSticky(object sender, RoutedEventArgs e) =>
        Snackbar.Add(new SnackbarOptions { Title = "Connection lost", Message = "Reconnecting… this stays until you dismiss it.", Severity = Severity.Warning, RequireInteraction = true });

    private void OnBurst(object sender, RoutedEventArgs e)
    {
        for (var i = 1; i <= 6; i++)
            Snackbar.Add(new SnackbarOptions { Message = $"Uploaded texture_{i:00}.png", Severity = Severity.Info });
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < 3; i++)
            Snackbar.Add("Copied link"); // de-duplicated: shows once
    }

    private void OnClear(object sender, RoutedEventArgs e) => Snackbar.Clear();
}
