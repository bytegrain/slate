import '../src/styles/index.css';
import './demo.css';
import { dialog, snackbar, type SlDialog, type SlProvider, type SlProgress, type SnackbarPosition } from '../src/index';

const provider = document.getElementById('provider') as SlProvider;

// ---- Theme & density ----
const themeButtons = [...document.querySelectorAll<HTMLButtonElement>('[data-theme]')];
for (const button of themeButtons) {
  button.addEventListener('click', () => {
    provider.theme = button.dataset.theme as SlProvider['theme'];
    for (const b of themeButtons) {
      const on = b === button;
      b.setAttribute('aria-pressed', String(on));
      b.classList.toggle('is-pressed', on);
    }
    renderSwatches();
  });
}
// ?theme=dark|light|system and ?density=comfortable preselect (handy for screenshots and links).
const params = new URLSearchParams(location.search);
(themeButtons.find((b) => b.dataset.theme === params.get('theme')) ?? themeButtons[0]).click();
if (params.get('density') === 'comfortable') {
  provider.density = 'comfortable';
  (document.getElementById('density') as HTMLElement & { checked: boolean }).checked = true;
}

document.getElementById('density')!.addEventListener('change', (e) => {
  provider.density = (e.target as HTMLInputElement & { checked: boolean }).checked ? 'comfortable' : 'compact';
});

// ---- Swatches (read live token values so they follow the theme) ----
const swatchTokens: Array<[string, string]> = [
  ['Canvas', '--sl-color-background-canvas'],
  ['Surface', '--sl-color-background-surface'],
  ['Subtle', '--sl-color-background-subtle'],
  ['Border', '--sl-color-border-strong'],
  ['Text', '--sl-color-text-primary'],
  ['Accent', '--sl-color-accent-default'],
  ['Focus', '--sl-color-focus-ring'],
  ['Success', '--sl-color-status-success-fg'],
  ['Warning', '--sl-color-status-warning-fg'],
  ['Danger', '--sl-color-status-danger-fg'],
  ['Info', '--sl-color-status-info-fg'],
  ['Inverse', '--sl-color-inverse-background'],
];

function renderSwatches(): void {
  const host = document.getElementById('swatches')!;
  requestAnimationFrame(() => {
    const style = getComputedStyle(provider);
    host.replaceChildren(
      ...swatchTokens.map(([name, token]) => {
        const item = document.createElement('div');
        item.className = 'demo-swatch';
        item.innerHTML = `<div class="demo-swatch__chip" style="background: var(${token})"></div><b>${name}</b><span>${style.getPropertyValue(token).trim()}</span>`;
        return item;
      }),
    );
  });
}
renderSwatches();

// ---- Nav highlighting ----
const navItems = [...document.querySelectorAll<HTMLAnchorElement>('.sl-nav__item')];
for (const item of navItems) {
  item.addEventListener('click', () => {
    for (const i of navItems) i.classList.toggle('is-active', i === item);
  });
}

// ---- Form ----
const form = document.getElementById('demo-form') as HTMLFormElement;
form.addEventListener('submit', (e) => {
  e.preventDefault();
  const data = new FormData(form);
  snackbar.success(`Created ${data.get('project') || 'project'}`, { title: 'Project created' });
});

// ---- Progress animation ----
const progress = document.getElementById('progress') as SlProgress;
const progressLabel = document.getElementById('progress-label')!;
let value = 64;
setInterval(() => {
  value = value >= 100 ? 8 : value + 4;
  progress.value = value;
  progressLabel.textContent = `${value}%`;
}, 900);

// ---- Interactive card ----
document.getElementById('interactive-card')!.addEventListener('click', () => snackbar.info('Card clicked'));

// ---- Snackbars ----
let deleted = 0;
const snackActions: Record<string, () => void> = {
  normal: () => snackbar.show('Copied to clipboard'),
  info: () => snackbar.info('A new version of Slate is available.'),
  success: () => snackbar.success('slate-web is live in 3 regions.', { title: 'Deployed' }),
  warning: () => snackbar.warning('Two components are missing dark tokens.'),
  error: () => snackbar.error('Slate.Wpf failed to compile ResourceDictionary.xaml.', { title: 'Build failed' }),
  action: () => {
    const n = ++deleted;
    snackbar.show({
      message: `Deleted ${n} file${n === 1 ? '' : 's'}`,
      key: `deleted-${n}`,
      action: { label: 'Undo', onInvoke: () => void snackbar.success('Restored') },
    });
  },
  sticky: () => snackbar.warning('Connection lost. Changes will sync when you reconnect.', { requireInteraction: true, key: 'offline' }),
  burst: () => {
    for (let i = 1; i <= 6; i++) snackbar.show({ message: `Uploaded asset_${String(i).padStart(2, '0')}.png`, key: `burst-${Date.now()}-${i}` });
  },
  duplicate: () => {
    for (let i = 0; i < 3; i++) snackbar.show('Saved (shown once — duplicates are suppressed)');
  },
  clear: () => snackbar.clear(),
};
for (const button of document.querySelectorAll<HTMLElement>('[data-snack]')) {
  button.addEventListener('click', () => snackActions[button.dataset.snack!]());
}

document.getElementById('snack-position')!.addEventListener('change', (e) => {
  provider.snackbarPosition = (e.currentTarget as HTMLElement & { value: string }).value as SnackbarPosition;
});

// ---- Dialogs ----
/** A rename form that validates before accepting. */
function renameDialog(): { content: (d: SlDialog) => Node; submit: (d: SlDialog) => void } {
  let field: (HTMLElement & { value: string; error?: string }) | undefined;
  const submit = (d: SlDialog) => {
    if (!field || !/^[A-Za-z0-9.]+$/.test(field.value)) {
      if (field) field.error = 'Use letters, numbers and dots only.';
      return;
    }
    d.accept(field.value);
  };
  const content = (d: SlDialog) => {
    const wrap = document.createElement('div');
    wrap.innerHTML = `<sl-text-field label="Package name" value="Slate.Wpf" required helper-text="Letters, numbers and dots." autofocus></sl-text-field>`;
    field = wrap.firstElementChild as HTMLElement & { value: string; error?: string };
    field.addEventListener('input', () => (field!.error = undefined));
    field.addEventListener('keydown', (e) => e.key === 'Enter' && submit(d));
    return wrap;
  };
  return { content, submit };
}

const dialogActions: Record<string, () => Promise<void>> = {
  form: async () => {
    const rename = renameDialog();
    const r = await dialog.show({
      heading: 'Rename package',
      description: 'Renaming updates every reference in the solution.',
      icon: 'pencil',
      width: 'xs',
      closeOnBackdropClick: false,
      content: rename.content,
      actions: [
        { label: 'Cancel', cancel: true },
        { label: 'Rename', variant: 'primary', onClick: rename.submit },
      ],
    });
    if (!r.canceled) snackbar.success(`Renamed to ${String(r.data)}`);
  },
  confirm: async () => {
    const ok = await dialog.confirm({ title: 'Publish 0.1.4?', message: 'This pushes all four packages to npm and NuGet.', confirmText: 'Publish' });
    snackbar.show(ok ? 'Publishing…' : 'Publish cancelled', ok ? 'info' : 'normal');
  },
  destructive: async () => {
    const ok = await dialog.confirm({
      title: 'Delete Slate.Wpf?',
      message: 'This removes the package and its 14 versions from the feed. This can’t be undone.',
      confirmText: 'Delete package',
      destructive: true,
    });
    if (ok) snackbar.show({ message: 'Slate.Wpf deleted', action: { label: 'Undo', onInvoke: () => void snackbar.success('Restored Slate.Wpf') } });
  },
  alert: async () => {
    await dialog.alert({ title: 'Tokens rebuilt', message: 'All 11 generated files are up to date.', severity: 'success' });
  },
  stacked: async () => {
    await dialog.show({
      heading: 'Project settings',
      description: 'Dialogs stack: only the top one responds to Escape.',
      width: 'md',
      content: (d) => {
        const b = document.createElement('sl-button');
        b.textContent = 'Open a second dialog';
        b.addEventListener('click', async () => {
          const ok = await dialog.confirm({ title: 'Discard changes?', message: 'You have unsaved edits.', confirmText: 'Discard', destructive: true });
          if (ok) d.close();
        });
        return b;
      },
      actions: [{ label: 'Close', cancel: true }],
    });
  },
  declarative: async () => {
    const el = document.getElementById('declarative') as SlDialog;
    const r = await el.show();
    if (!r.canceled) snackbar.success('Invites sent');
  },
};

const declarative = document.getElementById('declarative') as SlDialog;
declarative.querySelector('[data-close]')!.addEventListener('click', () => declarative.close());
declarative.querySelector('[data-accept]')!.addEventListener('click', () => declarative.accept());

for (const button of document.querySelectorAll<HTMLElement>('[data-dialog]')) {
  button.addEventListener('click', () => void dialogActions[button.dataset.dialog!]());
}

// ?show=snackbars|dialog opens examples on load (used for screenshots).
if (params.get('show') === 'snackbars') {
  snackActions.success();
  snackActions.action();
  snackActions.error();
}
if (params.get('show') === 'dialog') void dialogActions.destructive();
if (params.get('show') === 'form') void dialogActions.form();
