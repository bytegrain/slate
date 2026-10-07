# Slate.Avalonia

The Alloy design system for Avalonia 12. Follows `docs/design/*` (tokens come from `design/tokens` via the generator).

## Setup

```xml
<!-- App.axaml -->
<Application xmlns:sl="https://slate.dev/avalonia" RequestedThemeVariant="Default">
  <Application.Styles>
    <sl:SlateTheme Density="Compact" />
  </Application.Styles>
</Application>
```

```csharp
services.AddSlate(o =>               // ISnackbarService + IDialogService (also sets SlateServices defaults)
{
    o.Defaults.Button.Size = ControlSize.Small;      // app-wide defaults (SlateTheme.Defaults)
    o.Defaults.Field.Variant = FieldVariant.Filled;
    o.Defaults.Snackbar = new SnackbarConfiguration { Position = SnackbarPosition.TopRight };
    o.Theme = new SlateThemeOptions { Accent = "#5B3DF5", RadiusScale = 1.5 };  // custom theme
});
// or without DI: SlateServices.Snackbars / SlateServices.Dialogs, SlateTheme.Defaults, SlateTheme.Current.Options
```

Use `sl:SlateWindow` for the 36px Alloy title bar with built-in dialog and snackbar hosts, or place
`sl:DialogHost` / `sl:SnackbarHost` yourself and bind `Service`.

Runtime switches: `SlateTheme.Current.Mode` (Light/Dark/System), `.Density`, `.ReduceMotion`, `.Options` (custom theme).

## Configuration (docs/design/configurability.md)

| Level | How |
|---|---|
| Theme | `SlateTheme.Current.Options = new SlateThemeOptions { Accent, Base, RadiusScale, FontFamily, Overrides }` (or `Apply(ThemeBuilder.Build(…))`), `ResetTheme()`. Scope to a subtree with `SlateTheme.ApplyTo(element, theme)` (use a `ThemeVariantScope` to force its variant). |
| Component tokens | Override any `Sl.Component.*` resource in any `Resources` scope, e.g. `Sl.Component.Button.Radius.Corner`, `Sl.Component.Card.Background.Brush`, `Sl.Component.Field.Border.Brush`. |
| Defaults | `SlateTheme.Defaults` (`SlateDefaults`): button variant/tone/size/radius, field variant/size/radius, card variant/radius, selection label placement, dialog and snackbar options. Read when a control loads and has no local value. |
| Per instance | Slate control properties, or `sl:Sl.*` on native controls: `Variant`, `Tone`, `Size`, `Radius`, `StartIcon`, `EndIcon`, `IconOnly`, `Label`, `Loading`, `FullWidth`, `Pressed`, `Shortcut`, `Description`, `LabelPlacement`. Scoped density: `sl:Sl.Density="Comfortable"` on any element. |

Buttons combine `Variant` (Outlined, Solid, Soft, Ghost, Link) × `Tone` (Neutral, Accent, Success, Warning, Danger, Info):
the primary action is `sl:Sl.Variant="Solid" sl:Sl.Tone="Accent"`. Plain classes still work (`Classes="solid tone-accent"`).

XAML spellings are recorded in design/api/components.json (`xamlType`, and `xaml`/`avalonia` per option) and shared
with Slate.Wpf. Slate controls keep the contract names in the `sl:` namespace — `sl:Menu`, `sl:DatePicker` (+ `sl:CalendarView`),
`sl:TreeView` — distinct from Avalonia's own `Menu`, `DatePicker` and `TreeView` (in C#, alias them, e.g.
`using TreeView = Slate.Avalonia.Controls.TreeView;`). Options whose canonical name clashes with an existing member use the
same alias on both XAML platforms: `ContainerMaxWidth`, `DialogMaxWidth`, `AsContextMenu`, `TooltipPlacement`, `DisplayName`,
`Minimum`/`Maximum`, `ShowTicks`; native controls keep `IsChecked`, `IsEnabled`, `IsIndeterminate`, `ShowProgressText`.
`tests/Slate.Avalonia.Tests/ConformanceTests.cs` reads those spellings from the JSON and checks every option.

## How the theme is built

`SlateTheme` layers on `FluentTheme`: Fluent provides templates for controls Slate doesn't restyle yet
(menus, ComboBox, DataGrid…), re-coloured from Slate tokens. Slate's own ControlThemes (Button, ToggleButton,
CheckBox, RadioButton, ToggleSwitch and every Slate control) win because a Styles' own resources are searched
before its children's. TextBox, ProgressBar, ScrollBar, ToolTip and Window keep Fluent templates with Alloy
styles on top (`Themes/NativeOverrides.axaml`).

## Controls

| Native (styled) | Slate controls |
|---|---|
| Button (`sl:Sl.*`; or classes `outlined solid soft ghost link`, `tone-*`, `small large`, `radius-*`) | `TextField`, `Icon`, `LoadingSpinner`, `Kbd`, `Badge`, `Alert`, `Card`, `Divider` |
| ToggleButton, CheckBox (incl. indeterminate), RadioButton, ToggleSwitch | Selection: `Switch` (ToggleSwitch with the canonical options), `RadioGroup` |
| | Layout: `Container`, `Stack` + `Spacer`, `ResponsiveGrid`, `Toolbar` |
| TextBox, ProgressBar, ScrollBar, ToolTip, Window | Shell: `AppShell`, `AppBar`, `Drawer`, `NavItem`, `SlateWindow` |
| | Systems: `SnackbarHost`, `DialogHost`, `DialogContent` |

Notes: the busy indicator is `LoadingSpinner` (Avalonia already has `Avalonia.Controls.Spinner`).
`ResponsiveGrid` chooses its breakpoint from its own width, so it responds to the panel it is in.
