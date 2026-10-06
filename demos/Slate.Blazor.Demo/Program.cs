using Slate.Blazor;
using Slate.Blazor.Demo.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Snackbar and dialog services (one set per circuit) plus app-wide defaults and an optional custom theme:
//   options.Defaults.Button.Size = ControlSize.Small;  options.Theme = new() { Accent = "#5B3DF5" };
builder.Services.AddSlate(options =>
{
    options.Defaults.Snackbar = options.Defaults.Snackbar with { Position = Slate.Snackbars.SnackbarPosition.BottomRight };
});
builder.Services.AddScoped<Slate.Blazor.Demo.DemoTheme>();

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
