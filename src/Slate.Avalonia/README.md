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
services.AddSlate();                 // ISnackbarService + IDialogService (also sets SlateServices defaults)
// or without DI: SlateServices.Snackbars / SlateServices.Dialogs
```

Use `sl:SlateWindow` for the 36px Alloy title bar with built-in dialog and snackbar hosts, or place
`sl:DialogHost` / `sl:SnackbarHost` yourself and bind `Service`.

Runtime switches: `SlateTheme.Current.Mode` (Light/Dark/System), `.Density`, `.ReduceMotion`.

## How the theme is built

`SlateTheme` layers on `FluentTheme`: Fluent provides templates for controls Slate doesn't restyle yet
(menus, ComboBox, DataGrid…), re-coloured from Slate tokens. Slate's own ControlThemes (Button, ToggleButton,
CheckBox, RadioButton, ToggleSwitch and every Slate control) win because a Styles' own resources are searched
before its children's. TextBox, ProgressBar, ScrollBar, ToolTip and Window keep Fluent templates with Alloy
styles on top (`Themes/NativeOverrides.axaml`).

## Controls

| Native (styled) | Slate controls |
|---|---|
| Button (`sl:Sl.Variant`, `Size`, `Icon`, `IsLoading`, `Shortcut`; or classes `primary ghost danger danger-solid link small large`) | `TextField`, `Icon`, `LoadingSpinner`, `Kbd`, `Badge`, `Alert`, `Card`, `Divider` |
| ToggleButton, CheckBox (incl. indeterminate), RadioButton, ToggleSwitch | Layout: `Container`, `Stack` + `Spacer`, `ResponsiveGrid`, `Toolbar` |
| TextBox, ProgressBar, ScrollBar, ToolTip, Window | Shell: `AppShell`, `AppBar`, `Drawer`, `NavItem`, `SlateWindow` |
| | Systems: `SnackbarHost`, `DialogHost`, `DialogContent` |

Notes: the busy indicator is `LoadingSpinner` (Avalonia already has `Avalonia.Controls.Spinner`).
`ResponsiveGrid` chooses its breakpoint from its own width, so it responds to the panel it is in.
