using Slate.Blazor;
using Slate.Blazor.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Snackbar and dialog services (one set per circuit). Options: position, limits, durations.
builder.Services.AddSlate(options =>
{
    options.Snackbars = options.Snackbars with { Position = Slate.Snackbars.SnackbarPosition.BottomRight };
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
