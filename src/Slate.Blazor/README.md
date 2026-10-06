# Slate.Blazor

The Alloy design system as Razor components. Visuals come entirely from `@slate/web`'s `slate.css`
(shipped in this package under `_content/Slate.Blazor/`), so Blazor renders exactly the markup in
[`docs/design/css-classes.md`](../../docs/design/css-classes.md). Option names follow the canonical API in
[`design/api/components.json`](../../design/api/components.json) (checked by `ConformanceTests`).

```csharp
// Program.cs — services, app-wide defaults and an optional custom theme
builder.Services.AddSlate(o =>
{
    o.Defaults.Button.Size = ControlSize.Small;           // any unset parameter reads these
    o.Defaults.Field.Variant = FieldVariant.Filled;
    o.Defaults.Snackbar = o.Defaults.Snackbar with { Position = SnackbarPosition.TopCenter };
    o.Theme = new SlateThemeOptions { Accent = "#5B3DF5", RadiusScale = 1.5 };
});
```
```html
<!-- App.razor <head> -->
<link rel="stylesheet" href="_content/Slate.Blazor/slate.css" />
<link rel="stylesheet" href="_content/Slate.Blazor/slate-blazor.css" />
```
```razor
@* MainLayout.razor *@
<SlateProvider @bind-Mode="_mode" Density="Density.Compact">
    <SlAppShell @bind-DrawerOpen="_open" FillViewport="true">
        <AppBar><SlAppBar Title="My app" /></AppBar>
        <Drawer><SlNav><SlNavItem Href="" Icon="home" Label="Home" /></SlNav></Drawer>
        <ChildContent>@Body</ChildContent>
    </SlAppShell>
</SlateProvider>
```

## Configuring

Four levels, each overriding the previous ([configurability](../../docs/design/configurability.md)):

1. **Theme** — `SlateProvider ThemeOptions="…"` (or `Theme="…"` with a pre-built `SlateThemeDefinition`, or
   `AddSlate(o => o.Theme = …)`). Rendered as scoped CSS variables; System mode emits light rules plus a
   `prefers-color-scheme: dark` block, so it works during prerender without script. Nested providers scope theme,
   mode and density to a region and re-derive the surrounding custom theme for their own mode.
2. **Component tokens** — override any `--sl-component-*` variable in a stylesheet or `Style`.
3. **Defaults** — `AddSlate(o => o.Defaults…)` (Slate.Core `SlateDefaults`).
4. **Parameters** — e.g. `<SlButton Variant="ButtonVariant.Solid" Tone="Tone.Accent" Radius="Radius.Full">`.
   Every component also takes `Class`, `Style` and arbitrary attributes.

| Area | Components |
|---|---|
| Root | `SlateProvider` (Mode, Theme/ThemeOptions, Density; the outermost hosts snackbars + dialogs) |
| Layout | `SlAppShell` (DrawerVariant, DrawerOpen, ResponsiveBreakpoint, FillViewport; AppBar/Drawer regions or SlAppBar/SlDrawer/SlMain children), `SlAppBar` (Title, Leading, Center, Actions, ShowMenuButton), `SlDrawer`, `SlNav`/`SlNavHeading`/`SlNavItem`, `SlMain`, `SlContainer`, `SlGrid` + `SlItem`, `SlStack`, `SlSpacer`, `SlDivider`, `SlCard`, `SlToolbar`, `SlButtonGroup` |
| Inputs | `SlButton` (Variant × Tone), `SlTextField<T>` (outlined/filled/underlined, counter, clearable, adornments), `SlCheckbox<bool / bool?>` and `SlSwitch` (`@bind-Checked`), `SlRadioGroup<T>` + `SlRadio<T>` — all work standalone and inside `EditForm` |
| Display | `SlText`, `SlIcon`, `SlBadge`, `SlAlert`, `SlProgress`, `SlSpinner`, `SlKbd` |
| Systems | `ISnackbarService`, `IDialogService` (`ShowAsync<T>`, `ConfirmAsync`, `AlertAsync`; defaults from `Defaults.Dialog`), declarative `SlDialog` (Icon, Tone, Footer), `SlDialogBody`/`SlDialogFooter`, cascading `SlDialogInstance` |

Blazor spellings of canonical names: the default content region is `ChildContent`; events keep their canonical
names (`Click`, `Dismissed`, `ValueChanged`, `CheckedChanged`, `DrawerOpenChanged`).

Building: the project copies `packages/web/dist/slate.css` and fonts into `wwwroot` before each build (running
`npm run build -w @slate/web` if needed). Set `SlateSkipWebAssets=true` to skip.
