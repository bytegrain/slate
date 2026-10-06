# Slate.Wpf

The Alloy design system for WPF (.NET 10).

```xml
<!-- App.xaml -->
<Application.Resources>
  <sl:SlateTheme Mode="System" Density="Compact" />   <!-- xmlns:sl="https://slate.dev/wpf" -->
</Application.Resources>
```

```xml
<sl:SlateWindow Title="My app" AppIcon="layers">       <!-- 36px title bar, snackbar + dialog layers -->
  <sl:AppShell>
    <sl:AppShell.Drawer>
      <ListBox Style="{DynamicResource Sl.NavList}">
        <sl:NavItem Icon="home" Label="Home" />
      </ListBox>
    </sl:AppShell.Drawer>
    <Button sl:Sl.Variant="Solid" sl:Sl.Tone="Accent" sl:Sl.StartIcon="plus" Content="New project" />
  </sl:AppShell>
</sl:SlateWindow>
```

```csharp
services.AddSlate(o =>                                  // or use SlateServices.Snackbar / .Dialogs directly
{
    o.Defaults.Button.Variant = ButtonVariant.Soft;     // app-wide defaults for unset options (= SlateTheme.Defaults)
    o.Theme = new SlateThemeOptions { Accent = "#5B3DF5", RadiusScale = 1.5 };
});
snackbar.Add("Saved", Severity.Success);
bool ok = await dialogs.ConfirmAsync(new MessageBoxOptions { Title = "Delete?", Message = "…", Destructive = true });
```

## Configuration

Options follow the canonical names in `design/api/components.json` (see `docs/design/configurability.md`).

- Native controls (Button, TextBox, PasswordBox, ComboBox, CheckBox, RadioButton, ProgressBar, ScrollBar, ToolTip,
  Menu/ContextMenu, ListBox) are styled implicitly and configured with attached `sl:Sl.*` options:
  `Variant | Tone | Size | Radius | StartIcon | EndIcon | IconOnly | Label | Loading | FullWidth | Pressed | Shortcut |
  Description | LabelPlacement | ShowValue | Placeholder`. Slate controls expose the same names as properties.
- Precedence: local value (e.g. `Background="…"`) > option set on the element > `SlateTheme.Defaults` > token.
- `sl:Sl.Density="Comfortable"` on any element re-scopes density for its subtree (inheritable).
- Runtime theme: `SlateTheme.Current.Options = new SlateThemeOptions { … }` (or `Apply(definition)`); `ResetTheme()`
  returns to Alloy. Custom themes are rebuilt for the current base after `Mode` changes.
- Component tokens are `DynamicResource` keys `Sl.Component.<Component>.<Token>[.Brush|.Corner|.Thickness|.Effect]`.

WPF spellings that differ from the contract (WPF already owns the name):

| Contract | WPF |
| --- | --- |
| `Disabled` | `IsEnabled` (inverted) |
| `Checked` / `CheckedChanged` | `IsChecked` / `Checked`+`Unchecked` |
| `Max` | `Maximum` |
| `Indeterminate` | `IsIndeterminate` |
| `MaxWidth` | `Container.ContainerMaxWidth`, `DialogContent.DialogMaxWidth` |

## Controls

- Slate controls: TextField, Switch, RadioGroup, Icon, Spinner, Kbd, Badge, Alert, Card, Divider, Stack/Spacer, Container,
  ResponsiveGrid, Toolbar, AppShell/AppBar/NavItem/SectionHeader, SlateWindow, SnackbarHost, DialogHost/DialogContent.
- Theme/density switch live: `SlateTheme.Current.Mode = ThemeMode.Dark`.
- Every themed value is a `DynamicResource` into the generated `Themes/Generated` dictionaries (never edit those — run the token build).
