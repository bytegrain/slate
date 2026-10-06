import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeClock } from '../src/core/clock';
import { DialogResult, dialogStack } from '../src/core/dialog-stack';
import { SnackbarService } from '../src/services/snackbar';
import { dialog } from '../src/services/dialog';
import type { SlSnackbarHost } from '../src/components/snackbar-host';
import type { SlDialog } from '../src/components/dialog';
import { $, cleanup, fixture, key, settle, shadow } from './helpers';

afterEach(() => {
  dialogStack.closeAll();
  cleanup();
});

describe('sl-snackbar-host', () => {
  let clock: FakeClock;
  let service: SnackbarService;
  let host: SlSnackbarHost;

  beforeEach(async () => {
    clock = new FakeClock();
    service = new SnackbarService({}, clock);
    host = document.createElement('sl-snackbar-host');
    host.service = service;
    document.body.append(host);
    await host.updateComplete;
  });

  const rendered = () => [...shadow(host).querySelectorAll<HTMLElement>('.sl-snackbar')].filter((e) => !e.classList.contains('is-leaving'));

  it('is a labelled region positioned from configuration', async () => {
    const region = $(host, 'section');
    expect(region.getAttribute('aria-label')).toBe('Notifications');
    expect(region.className).toContain('sl-snackbar-host--bottom-right');
    host.position = 'top-center';
    await host.updateComplete;
    expect($(host, 'section').className).toContain('sl-snackbar-host--top-center');
  });

  it('renders severity, title, message and live-region role', async () => {
    service.show({ message: 'Deployed', title: 'slate-web', severity: 'success' });
    service.error('Build failed');
    await host.updateComplete;
    const [ok, err] = rendered();
    expect(ok.className).toContain('sl-snackbar--success');
    expect(ok.getAttribute('role')).toBe('status');
    expect(ok.querySelector('.sl-snackbar__title')!.textContent).toBe('slate-web');
    expect(ok.querySelector('.sl-snackbar__message')!.textContent).toBe('Deployed');
    expect(ok.querySelector('.sl-snackbar__icon')).not.toBeNull();
    expect(err.getAttribute('role')).toBe('alert');
    expect(err.style.getPropertyValue('--_duration')).toBe('8000ms');
  });

  it('normal snackbars have no severity tile and sticky ones no timer', async () => {
    service.show({ message: 'Hi', requireInteraction: true });
    await host.updateComplete;
    const [s] = rendered();
    expect(s.querySelector('.sl-snackbar__icon')).toBeNull();
    expect(s.querySelector('.sl-snackbar__timer')).toBeNull();
  });

  it('close button dismisses with reason user', async () => {
    const s = service.show('Saved');
    await host.updateComplete;
    rendered()[0].querySelector<HTMLButtonElement>('.sl-snackbar__close')!.click();
    await host.updateComplete;
    expect(s.closeReason).toBe('user');
    expect(rendered()).toHaveLength(0);
  });

  it('action button runs the action and closes', async () => {
    const undo = vi.fn();
    const s = service.show({ message: 'Deleted', action: { label: 'Undo', onInvoke: undo } });
    await host.updateComplete;
    const action = rendered()[0].querySelector<HTMLButtonElement>('.sl-snackbar__action')!;
    expect(action.textContent).toBe('Undo');
    action.click();
    await Promise.resolve();
    expect(undo).toHaveBeenCalledTimes(1);
    expect(s.closeReason).toBe('action');
  });

  it('hover pauses the countdown; leaving resumes it', async () => {
    const s = service.show({ message: 'x', duration: 5000 });
    await host.updateComplete;
    const el = rendered()[0];
    clock.advance(1000);
    el.dispatchEvent(new MouseEvent('mouseenter'));
    await host.updateComplete;
    expect(s.isPaused).toBe(true);
    expect(rendered()[0].classList.contains('is-paused')).toBe(true);
    clock.advance(60_000);
    expect(s.state).toBe('visible');
    el.dispatchEvent(new MouseEvent('mouseleave'));
    clock.advance(4000);
    expect(s.state).toBe('closed');
  });

  it('focus inside pauses and Escape dismisses', async () => {
    const s = service.show('Escape me');
    await host.updateComplete;
    const el = rendered()[0];
    el.dispatchEvent(new FocusEvent('focusin'));
    expect(s.isPaused).toBe(true);
    key(el.querySelector('.sl-snackbar__close')!, 'Escape');
    expect(s.closeReason).toBe('user');
  });

  it('only maxVisible are rendered; the rest wait', async () => {
    for (let i = 0; i < 5; i++) service.show(`m${i}`);
    await host.updateComplete;
    expect(rendered()).toHaveLength(3);
    expect(service.queue.queuedCount).toBe(2);
  });

  it('follows configure() swapping the queue', async () => {
    service.configure({ maxVisible: 1, position: 'top-left' });
    service.show('a');
    service.show('b');
    await host.updateComplete;
    expect(rendered()).toHaveLength(1);
    expect($(host, 'section').className).toContain('sl-snackbar-host--top-left');
  });

  it('registers itself so the service does not create another host', () => {
    expect(service.hasHost).toBe(true);
    host.remove();
    expect(service.hasHost).toBe(false);
  });
});

describe('sl-dialog', () => {
  const open = async (markup: string) => {
    const el = await fixture<SlDialog>(markup);
    const result = el.show();
    await settle();
    return { el, result };
  };

  it('opens modally with title/description wiring', async () => {
    const { el } = await open('<sl-dialog heading="Rename" description="Pick a new name">Body</sl-dialog>');
    const dlg = $(el, 'dialog') as HTMLDialogElement;
    expect(dlg.open).toBe(true);
    expect(el.open).toBe(true);
    const title = $(el, '.sl-dialog__title');
    expect(dlg.getAttribute('aria-labelledby')).toBe(title.id);
    expect(dlg.getAttribute('aria-describedby')).toBe($(el, '.sl-dialog__description').id);
    expect(dialogStack.top?.content).toBe(el);
  });

  it('width, placement and modifiers map to classes', async () => {
    const { el } = await open('<sl-dialog heading="x" width="lg" placement="top" full-width></sl-dialog>');
    const cls = $(el, 'dialog').className;
    expect(cls).toContain('sl-dialog--lg');
    expect(cls).toContain('sl-dialog--top');
    expect(cls).toContain('sl-dialog--full-width');
  });

  it('accept resolves Ok with data and fires sl-close', async () => {
    const { el, result } = await open('<sl-dialog heading="x"></sl-dialog>');
    const closed = vi.fn();
    el.addEventListener('sl-close', closed);
    el.accept('new-name');
    const r = await result;
    expect(r).toEqual({ canceled: false, data: 'new-name' });
    expect(closed).toHaveBeenCalledTimes(1);
    expect(el.open).toBe(false);
  });

  it('Escape cancels unless disabled', async () => {
    const { el, result } = await open('<sl-dialog heading="x"><button>ok</button></sl-dialog>');
    key($(el, 'dialog'), 'Escape');
    expect((await result).canceled).toBe(true);

    const locked = await open('<sl-dialog heading="y" close-on-escape="false"></sl-dialog>');
    key($(locked.el, 'dialog'), 'Escape');
    await settle();
    expect(locked.el.open).toBe(true);
  });

  it('the native cancel request is routed through the stack', async () => {
    const { el, result } = await open('<sl-dialog heading="x"></sl-dialog>');
    const cancel = new Event('cancel', { cancelable: true });
    $(el, 'dialog').dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true);
    expect((await result).canceled).toBe(true);
  });

  it('scrim click cancels unless disabled', async () => {
    const locked = await open('<sl-dialog heading="y" close-on-backdrop-click="false"></sl-dialog>');
    $(locked.el, '.sl-dialog__scrim').click();
    await settle();
    expect(locked.el.open).toBe(true);
    locked.el.close();
    await locked.result;

    const { el, result } = await open('<sl-dialog heading="x"></sl-dialog>');
    $(el, '.sl-dialog__scrim').click();
    expect((await result).canceled).toBe(true);
  });

  it('close button cancels and can be hidden', async () => {
    const { el, result } = await open('<sl-dialog heading="x"></sl-dialog>');
    $(el, '.sl-dialog__close').click();
    expect((await result).canceled).toBe(true);
    const hidden = await fixture<SlDialog>('<sl-dialog heading="x" show-close-button="false"></sl-dialog>');
    expect(shadow(hidden).querySelector('.sl-dialog__close')).toBeNull();
  });

  it('only the top of a stack responds to Escape', async () => {
    const a = await open('<sl-dialog heading="a"></sl-dialog>');
    const b = await open('<sl-dialog heading="b"></sl-dialog>');
    key($(a.el, 'dialog'), 'Escape'); // not the top: ignored
    await settle();
    expect(a.el.open).toBe(true);
    key($(b.el, 'dialog'), 'Escape');
    expect((await b.result).canceled).toBe(true);
    expect(a.el.open).toBe(true);
  });

  it('moves focus in on open and restores it on close', async () => {
    const trigger = await fixture<HTMLButtonElement>('<button>Open</button>');
    trigger.focus();
    const { el, result } = await open('<sl-dialog heading="x"><input id="first" /></sl-dialog>');
    expect(document.activeElement).toBe(el.querySelector('#first'));
    el.close();
    await result;
    expect(document.activeElement).toBe(trigger);
  });

  it('autofocus wins over the first focusable', async () => {
    const { el } = await open('<sl-dialog heading="x"><input /><button autofocus id="af">Go</button></sl-dialog>');
    expect(document.activeElement).toBe(el.querySelector('#af'));
  });

  it('the open attribute opens and closes declaratively', async () => {
    const el = await fixture<SlDialog>('<sl-dialog heading="x" open></sl-dialog>');
    await settle();
    expect(dialogStack.top?.content).toBe(el);
    el.open = false;
    await settle();
    expect(dialogStack.top).toBeNull();
  });

  it('removing an open dialog cancels it', async () => {
    const { el, result } = await open('<sl-dialog heading="x"></sl-dialog>');
    el.remove();
    expect((await result).canceled).toBe(true);
  });
});

describe('dialog service', () => {
  const nextDialog = async () => {
    await settle();
    const el = document.querySelector('sl-dialog') as SlDialog;
    await settle(el);
    return el;
  };

  it('show() builds actions and resolves with the clicked value; the element is removed', async () => {
    const pending = dialog.show({
      heading: 'Choose',
      content: 'Pick one',
      actions: [{ label: 'Cancel', cancel: true }, { label: 'Keep', value: 'keep' }],
    });
    const el = await nextDialog();
    const buttons = [...el.querySelectorAll('sl-button')];
    expect(buttons.map((b) => b.textContent)).toEqual(['Cancel', 'Keep']);
    expect(buttons.map((b) => b.getAttribute('variant'))).toEqual(['secondary', 'primary']);
    expect(el.querySelector('p')!.textContent).toBe('Pick one');
    $(buttons[1], 'button').click();
    expect(await pending).toEqual(DialogResult.ok('keep'));
    expect(document.querySelector('sl-dialog')).toBeNull();
  });

  it('content factories receive the dialog', async () => {
    const pending = dialog.show({
      heading: 'Rename',
      content: (d) => {
        const b = document.createElement('button');
        b.addEventListener('click', () => d.accept('renamed'));
        return b;
      },
    });
    const el = await nextDialog();
    el.querySelector('button')!.click();
    expect((await pending).data).toBe('renamed');
  });

  it('confirm resolves true/false and styles destructive confirms', async () => {
    const pending = dialog.confirm({ title: 'Delete Slate.Wpf?', message: 'Gone forever.', confirmText: 'Delete', destructive: true });
    const el = await nextDialog();
    const [cancel, confirm] = [...el.querySelectorAll('sl-button')];
    expect(confirm.getAttribute('variant')).toBe('danger-solid');
    expect(cancel.hasAttribute('autofocus')).toBe(true); // safe default for destructive
    expect(el.icon).toBe('alert-triangle');
    expect(el.tone).toBe('danger');
    $(confirm, 'button').click();
    expect(await pending).toBe(true);

    const second = dialog.confirm({ message: 'Sure?' });
    const el2 = await nextDialog();
    $(el2.querySelector('sl-button')!, 'button').click(); // Cancel
    expect(await second).toBe(false);
  });

  it('alert has a single acknowledgement button and ignores the scrim', async () => {
    const pending = dialog.alert({ title: 'Saved', message: 'All done' });
    const el = await nextDialog();
    expect(el.querySelectorAll('sl-button')).toHaveLength(1);
    expect(el.closeOnBackdropClick).toBe(false);
    $(el.querySelector('sl-button')!, 'button').click();
    await pending;
  });
});
