import type { ButtonVariant, Tone } from '../core/defaults';
import type { DialogTone, SlDialog } from '../components/dialog';
import { DialogResult, type DialogPlacement, type DialogWidth } from '../core/dialog-stack';
import type { Severity } from '../core/snackbar-queue';
import { severityIcon } from '../components/display';

export interface DialogAction {
  label: string;
  /** Default: solid for accept actions, outlined for cancel actions. */
  variant?: ButtonVariant;
  /** Default: accent for accept actions, neutral for cancel actions. */
  tone?: Tone;
  /** Data returned with Ok(value). Ignored for cancel actions. */
  value?: unknown;
  /** This action cancels instead of accepting. */
  cancel?: boolean;
  autofocus?: boolean;
  /** Custom handler (e.g. validate first). When set, the dialog stays open until you call accept()/close(). */
  onClick?: (dialog: SlDialog) => void;
}

export interface DialogShowOptions {
  title: string;
  description?: string;
  /** Body: text, a node, or a factory that receives the dialog element (call dialog.accept(data) / close()). */
  content?: string | Node | ((dialog: SlDialog) => Node);
  /** Footer buttons, in order. The primary action goes last. */
  actions?: DialogAction[];
  icon?: string;
  tone?: DialogTone;
  maxWidth?: DialogWidth;
  placement?: DialogPlacement;
  fullWidth?: boolean;
  fullScreen?: boolean;
  closeOnEscape?: boolean;
  closeOnBackdropClick?: boolean;
  showCloseButton?: boolean;
}

/** Mirrors Slate.Dialogs.MessageBoxOptions. */
export interface MessageBoxOptions {
  title?: string;
  message: string;
  confirmText?: string;
  /** null hides the cancel button (acknowledgement). */
  cancelText?: string | null;
  destructive?: boolean;
  severity?: Severity;
}

function container(): HTMLElement {
  return document.querySelector<HTMLElement>('sl-provider') ?? document.body;
}

const severityTone: Record<Severity, DialogTone> = {
  normal: 'neutral',
  info: 'info',
  success: 'success',
  warning: 'warning',
  error: 'danger',
};

/**
 * Programmatic dialogs.
 *   const r = await dialog.show({ title: 'Rename', content: form, actions: [...] });
 * Options left unset use configureDefaults({ dialog: … }).
 *   if (await dialog.confirm({ title: 'Delete?', message: '…', confirmText: 'Delete', destructive: true })) …
 */
export const dialog = {
  async show(options: DialogShowOptions): Promise<DialogResult> {
    const el = document.createElement('sl-dialog') as SlDialog;
    el.title = options.title;
    if (options.description) el.description = options.description;
    if (options.icon) el.icon = options.icon;
    if (options.tone) el.tone = options.tone;
    if (options.maxWidth) el.maxWidth = options.maxWidth;
    if (options.placement) el.placement = options.placement;
    if (options.fullWidth !== undefined) el.fullWidth = options.fullWidth;
    if (options.fullScreen !== undefined) el.fullScreen = options.fullScreen;
    if (options.closeOnEscape !== undefined) el.closeOnEscape = options.closeOnEscape;
    if (options.closeOnBackdropClick !== undefined) el.closeOnBackdropClick = options.closeOnBackdropClick;
    if (options.showCloseButton !== undefined) el.showCloseButton = options.showCloseButton;

    const { content } = options;
    if (typeof content === 'string') {
      const p = document.createElement('p');
      p.className = 'sl-text sl-text--body sl-text--secondary';
      p.style.margin = '0';
      p.textContent = content;
      el.append(p);
    } else if (typeof content === 'function') {
      el.append(content(el));
    } else if (content) {
      el.append(content);
    }

    for (const action of options.actions ?? []) {
      const button = document.createElement('sl-button');
      button.slot = 'footer';
      button.textContent = action.label;
      button.setAttribute('variant', action.variant ?? (action.cancel ? 'outlined' : 'solid'));
      button.setAttribute('tone', action.tone ?? (action.cancel ? 'neutral' : 'accent'));
      if (action.autofocus) button.setAttribute('autofocus', '');
      button.addEventListener('click', () => {
        if (action.onClick) action.onClick(el);
        else el.close(action.cancel ? DialogResult.cancel() : DialogResult.ok(action.value));
      });
      el.append(button);
    }

    container().append(el);
    try {
      return await el.show();
    } finally {
      el.remove();
    }
  },

  /** Resolves true when confirmed. */
  async confirm(options: MessageBoxOptions): Promise<boolean> {
    const severity = options.severity ?? (options.destructive ? 'error' : 'normal');
    const actions: DialogAction[] = [];
    if (options.cancelText !== null) {
      actions.push({ label: options.cancelText ?? 'Cancel', cancel: true, autofocus: !!options.destructive });
    }
    actions.push({
      label: options.confirmText ?? 'OK',
      variant: 'solid',
      tone: options.destructive ? 'danger' : 'accent',
      value: true,
      autofocus: !options.destructive,
    });
    const result = await dialog.show({
      title: options.title ?? '',
      content: options.message,
      actions,
      maxWidth: 'xs',
      icon: options.destructive ? 'alert-triangle' : severity === 'normal' ? undefined : severityIcon(severity),
      tone: options.destructive ? 'danger' : severityTone[severity],
      showCloseButton: false,
      closeOnBackdropClick: options.cancelText !== null,
    });
    return !result.canceled;
  },

  /** An acknowledgement box with a single button. */
  async alert(options: Omit<MessageBoxOptions, 'cancelText' | 'destructive'>): Promise<void> {
    await dialog.confirm({ ...options, cancelText: null });
  },
};
