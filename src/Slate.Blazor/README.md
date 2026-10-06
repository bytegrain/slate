# Slate.Blazor

The Alloy design system as Razor components. Visuals come entirely from `@slate/web`'s `slate.css`
(shipped in this package under `_content/Slate.Blazor/`), so Blazor renders exactly the markup in
[`docs/design/css-classes.md`](../../docs/design/css-classes.md).

```csharp
// Program.cs
builder.Services.AddSlate(o => o.Snackbars = o.Snackbars with { Position = SnackbarPosition.BottomRight });
```
```html
<!-- App.razor <head> -->
<link rel="stylesheet" href="_content/Slate.Blazor/slate.css" />
<link rel="stylesheet" href="_content/Slate.Blazor/slate-blazor.css" />
```
```razor
@* MainLayout.razor *@
<SlateProvider @bind-Theme="_theme" Density="Density.Compact">
    <SlAppShell>
        <SlAppBar Title="@(b => b.AddContent(0, "My app"))" />
        <SlDrawer><SlNav><SlNavLink Href="" Icon="home">Home</SlNavLink></SlNav></SlDrawer>
        <SlMain>@Body</SlMain>
    </SlAppShell>
</SlateProvider>
```

| Area | Components |
|---|---|
| Root | `SlateProvider` (Theme Light/Dark/System, Density, hosts snackbars + dialogs) |
| Layout | `SlAppShell`, `SlAppBar`, `SlDrawer` (Responsive/Persistent/Temporary/Mini), `SlNav`/`SlNavHeading`/`SlNavLink`, `SlMain`, `SlContainer`, `SlGrid` + `SlItem`, `SlStack`, `SlSpacer`, `SlDivider`, `SlCard`, `SlToolbar`, `SlButtonGroup` |
| Inputs | `SlButton`, `SlTextField<T>`, `SlCheckbox<bool / bool?>`, `SlSwitch`, `SlRadioGroup<T>` + `SlRadio<T>` — all work standalone and inside `EditForm` (validation messages become the field error) |
| Display | `SlText`, `SlIcon`, `SlBadge`, `SlAlert`, `SlProgress`, `SlSpinner`, `SlKbd` |
| Systems | `ISnackbarService` (+ `Success/Info/Warning/Error` helpers), `IDialogService` (`ShowAsync<T>`, `ConfirmAsync`, `AlertAsync`), declarative `SlDialog`, `SlDialogBody`/`SlDialogFooter`, cascading `SlDialogInstance` |

Render modes: works in Interactive Server, WebAssembly and Auto, and prerenders without JavaScript
(System theme uses `data-sl-theme="auto"`; JS only adds focus trapping, media listeners and `indeterminate`).

Building: the project copies `packages/web/dist/slate.css` and fonts into `wwwroot` before each build (running
`npm run build -w @slate/web` if needed). Set `SlateSkipWebAssets=true` to skip.
