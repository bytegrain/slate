# Systems: snackbars & dialogs

Systems are app-wide services, not components you place. Each platform exposes them the idiomatic way
(service via DI in Blazor/WPF/Avalonia, a JS API on web) but the **behaviour is defined once** — in
`Slate.Core` (`SnackbarQueue`, `DialogStack`) and mirrored in `@slate/web` against the same test cases.

## Snackbars

```csharp
snackbar.Add("Saved");
snackbar.Add(new SnackbarOptions {
    Title = "Deleted 3 files", Message = "They're in the bin for 30 days.",
    Severity = Severity.Success, Action = SnackbarAction.Create("Undo", Restore) });
```
```js
import { snackbar } from '@slate/web';
snackbar.show({ message: 'Saved' });
```

### Behaviour (`SnackbarQueue`)

| Rule | Value |
|---|---|
| Max visible | `snackbar.maxVisible` (3); extra are queued FIFO |
| Queue limit | 20 by default; overflow drops the **oldest queued** (reason `Cleared`) |
| Default duration | `snackbar.duration.default` 5s |
| Errors | `snackbar.duration.error` 8s |
| With an action | at least `snackbar.duration.withAction` 8s |
| Sticky | `RequireInteraction` → no timer |
| Queued time | does not count; the timer starts when shown |
| Pause | while hovered or focused (and optionally while the window is inactive); resumes with the time left |
| Duplicates | an identical active snackbar (severity + title + message, or `Key`) is returned instead of re-added |
| Action | closes the snackbar first (reason `Action`), then runs once |
| Close reasons | `Timeout`, `User`, `Action`, `Programmatic`, `Cleared` |

### Visual

`inverse.background`, `inverse.text`, `radius.lg`, `shadow.e3`, width `size.snackbar.min`–`max`, padding
10 12. Leading 18px severity icon tile in `status.*.solid` with `status.*.onSolid` glyph (none for normal).
Title `bodyStrong`; message `body` in `inverse.textMuted` when a title is present. Action: small ghost
button in `inverse.text`; close: icon-only ghost. A 2px progress line along the bottom edge in
`focus.ring` shows remaining time and freezes while paused (hidden under reduced motion).

Position: `bottom-right` default (desktop-first); configurable to any corner or centre. Stack gap `space.2`,
offset `space.4` from the viewport edges. Enter: fade + 8px slide from the anchored edge (`base`,
`emphasized`); exit: fade (`fast`, `exit`).

### Accessibility

Host is a labelled region ("Notifications"). Normal/info/success announce politely (`status`);
warning/error announce assertively (`alert`). Snackbars never steal focus. Escape dismisses the focused
snackbar. Actions are real buttons reachable by Tab.

## Dialogs

```csharp
var result = await dialogs.ShowAsync<RenameDialog>("Rename", parameters, new DialogOptions { MaxWidth = DialogWidth.Xs });
if (!result.Canceled) Rename(result.GetData<string>());

bool ok = await dialogs.ConfirmAsync(new MessageBoxOptions {
    Title = "Delete Slate.Wpf?", Message = "This can't be undone.", ConfirmText = "Delete", Destructive = true });
```
```js
const result = await dialog.confirm({ title: 'Delete?', message: '…', confirmText: 'Delete', destructive: true });
```

### Behaviour (`DialogStack`)

- Dialogs stack; only the top one is interactive. Each has its own scrim.
- `Result` completes exactly once: `Ok(data)` or `Cancel()` (Escape, close button, scrim click all cancel).
- Escape closes the top dialog only if `CloseOnEscape`; scrim click only if `CloseOnBackdropClick` (turn off
  for forms that could lose input).
- Result continuations run asynchronously, never inline inside the stack update.

### Visual

Scrim `color.scrim`. Panel `background.raised`, `radius.2xl`, `shadow.e3`, width up to
`size.dialog.{xs..xl}` (default `sm` 480), max height = viewport − 64px with body scrolling.

```
┌ header ─ padding 18 20 0 ──────────────────────────┐
│ [icon tile]  Title (typography.title)        [ × ] │
│              Description (body, text.secondary)    │
├ body ─ padding 16 20 ──────────────────────────────┤
├ footer ─ background.subtle, top hairline, 12 20 ───┤
│                           [Cancel] [Primary action] │
└────────────────────────────────────────────────────┘
```

Footer actions are right-aligned with the primary action **last**. Destructive confirmations use
`dangerSolid` for the confirm button and an `alert-triangle` icon tile in `status.danger.bg/fg`.
Enter: scrim fade + panel fade/scale 0.98→1 (`slow`, `emphasized`). Exit: `base`, `exit`.

### Accessibility

Modal semantics with the title as the accessible name and the description as its description. Focus
moves into the dialog (the element marked autofocus, else the first focusable, else the panel), is
trapped while open, and returns to the previously focused element on close. Background content is inert.
