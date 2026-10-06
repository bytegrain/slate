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
        <sl:NavItem Icon="home" Content="Home" />
      </ListBox>
    </sl:AppShell.Drawer>
    <Button sl:Ui.Variant="Primary" sl:Ui.Icon="plus" Content="New project" />
  </sl:AppShell>
</sl:SlateWindow>
```

```csharp
services.AddSlate();                                    // or use SlateServices.Snackbar / .Dialogs directly
snackbar.Add("Saved", Severity.Success);
bool ok = await dialogs.ConfirmAsync(new MessageBoxOptions { Title = "Delete?", Message = "…", Destructive = true });
```

- Native controls (Button, TextBox, PasswordBox, ComboBox, CheckBox, RadioButton, ProgressBar, ScrollBar, ToolTip,
  Menu/ContextMenu, ListBox) are styled implicitly. Variants via `sl:Ui.Variant | Size | Icon | IconEnd | IsLoading | Shortcut | Placeholder`.
- Slate controls: TextField, Switch, Icon, Spinner, Kbd, Badge, Alert, Card, Divider, Stack/Spacer, Container,
  ResponsiveGrid, Toolbar, AppShell/AppBar/NavItem/SectionHeader, SlateWindow, SnackbarHost, DialogHost/DialogContent.
- Theme/density switch live: `SlateTheme.Current.Mode = ThemeMode.Dark`.
- Every themed value is a `DynamicResource` into the generated `Themes/Generated` dictionaries (never edit those — run the token build).
