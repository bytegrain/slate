import { tokenNumber } from './tokens';

export type DialogWidth = 'xs' | 'sm' | 'md' | 'lg' | 'xl';
export type DialogPlacement = 'center' | 'top';

export interface DialogOptions {
  title?: string;
  maxWidth: DialogWidth;
  /** Use the full max width instead of sizing to content. */
  fullWidth: boolean;
  fullScreen: boolean;
  placement: DialogPlacement;
  closeOnEscape: boolean;
  /** Clicking the scrim cancels. Turn off for forms that could lose input. */
  closeOnBackdropClick: boolean;
  showCloseButton: boolean;
}

export const defaultDialogOptions: Readonly<DialogOptions> = Object.freeze({
  maxWidth: 'sm',
  fullWidth: false,
  fullScreen: false,
  placement: 'center',
  closeOnEscape: true,
  closeOnBackdropClick: true,
  showCloseButton: true,
});

/** Max width in px from the size.dialog.* tokens. Mirrors DialogOptions.WidthPixels. */
export function dialogWidthPixels(width: DialogWidth): number {
  return tokenNumber(`--sl-size-dialog-${width}`);
}

/** How a dialog ended. Cancel covers Escape, the close button and backdrop clicks. */
export interface DialogResult<T = unknown> {
  readonly canceled: boolean;
  readonly data: T | undefined;
}

export const DialogResult = {
  ok<T>(data?: T): DialogResult<T> {
    return Object.freeze({ canceled: false, data });
  },
  cancel<T = never>(): DialogResult<T> {
    return Object.freeze({ canceled: true, data: undefined });
  },
};

export class DialogReference<TContent = unknown> {
  readonly result: Promise<DialogResult>;
  private resolve!: (r: DialogResult) => void;
  private settled = false;

  constructor(
    readonly id: number,
    readonly content: TContent,
    readonly options: Readonly<DialogOptions>,
  ) {
    this.result = new Promise((resolve) => (this.resolve = resolve));
  }

  get isOpen(): boolean {
    return !this.settled;
  }

  /** @internal */
  complete(result: DialogResult): boolean {
    if (this.settled) return false;
    this.settled = true;
    this.resolve(result);
    return true;
  }
}

/** Stack of modal dialogs; only the top one is interactive. Mirrors Slate.Dialogs.DialogStack. */
export class DialogStack {
  private readonly openList: DialogReference[] = [];
  private readonly listeners = new Set<() => void>();
  private nextId = 0;

  /** Open dialogs, bottom to top. */
  get open(): readonly DialogReference[] {
    return [...this.openList];
  }

  get top(): DialogReference | null {
    return this.openList.at(-1) ?? null;
  }

  onChanged(listener: () => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  push<T>(content: T, options: Partial<DialogOptions> = {}): DialogReference<T> {
    if (content === null || content === undefined) throw new TypeError('Dialog content is required.');
    const dialog = new DialogReference(++this.nextId, content, Object.freeze({ ...defaultDialogOptions, ...options }));
    this.openList.push(dialog);
    this.emit();
    return dialog;
  }

  /** Closes a dialog (not necessarily the top one). No result means cancel. */
  close(dialog: DialogReference, result: DialogResult = DialogResult.cancel()): boolean {
    const i = this.openList.indexOf(dialog);
    if (i < 0) return false;
    this.openList.splice(i, 1);
    dialog.complete(result);
    this.emit();
    return true;
  }

  /** Escape: cancels the top dialog if it allows it. */
  handleEscape(): boolean {
    const top = this.top;
    return !!top && top.options.closeOnEscape && this.close(top, DialogResult.cancel());
  }

  /** Scrim click behind `dialog`: cancels it only if it is on top and allows it. */
  handleBackdropClick(dialog: DialogReference): boolean {
    return this.top === dialog && dialog.options.closeOnBackdropClick && this.close(dialog, DialogResult.cancel());
  }

  closeAll(): void {
    const all = [...this.openList].reverse();
    this.openList.length = 0;
    for (const d of all) d.complete(DialogResult.cancel());
    if (all.length > 0) this.emit();
  }

  private emit(): void {
    for (const l of this.listeners) l();
  }
}

/** The page-wide stack used by <sl-dialog> and the dialog service. */
export const dialogStack = new DialogStack();
