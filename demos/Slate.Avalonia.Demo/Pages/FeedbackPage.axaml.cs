using Avalonia.Controls;
using Slate.Avalonia.Services;
using Slate.Snackbars;

namespace Slate.Avalonia.Demo.Pages;

public partial class FeedbackPage : UserControl
{
    public FeedbackPage()
    {
        InitializeComponent();
        var snackbars = SlateServices.Snackbars;

        Simple.Click += (_, _) => snackbars.Add("Copied to clipboard");
        Success.Click += (_, _) => snackbars.Add(new SnackbarOptions
        {
            Title = "Deployed",
            Message = "slate-web is live in 3 regions.",
            Severity = Severity.Success,
        });
        Undo.Click += (_, _) => snackbars.Add(new SnackbarOptions
        {
            Message = "Deleted 3 files",
            Action = SnackbarAction.Create("Undo", () => snackbars.Add("Restored 3 files", Severity.Info)),
        });
        Error.Click += (_, _) => snackbars.Add(new SnackbarOptions
        {
            Title = "Build failed",
            Message = "Slate.Wpf: XAML parse error, line 212.",
            Severity = Severity.Error,
        });
        Sticky.Click += (_, _) => snackbars.Add(new SnackbarOptions
        {
            Message = "Connection lost — changes are saved locally.",
            Severity = Severity.Warning,
            RequireInteraction = true,
        });
        Burst.Click += (_, _) =>
        {
            for (var i = 1; i <= 6; i++)
                snackbars.Add($"Uploaded asset {i} of 6");
        };
        Clear.Click += (_, _) => snackbars.Clear();
    }
}
