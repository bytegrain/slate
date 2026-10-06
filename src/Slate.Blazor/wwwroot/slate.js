// Slate.Blazor interop. Loaded lazily (never during prerender). Keep this small: everything that can be
// done with markup + CSS is done there.

const focusable = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"]), [contenteditable="true"]';

function visible(el) {
  return !el.closest('[inert]') && (el.offsetWidth > 0 || el.offsetHeight > 0 || el.getClientRects().length > 0);
}

/** Traps Tab inside `panel`, focuses the [autofocus] element / first focusable / the panel, and on release
 *  restores focus to what was focused before. */
export function trapFocus(panel) {
  const previous = document.activeElement;
  const items = () => [...panel.querySelectorAll(focusable)].filter(visible);

  const initial = panel.querySelector('[autofocus]') ?? items().find(el => !el.classList.contains('sl-dialog__close')) ?? panel;
  initial.focus({ preventScroll: true });

  const onKeyDown = (e) => {
    if (e.key !== 'Tab') return;
    const list = items();
    if (list.length === 0) { e.preventDefault(); panel.focus(); return; }
    const first = list[0], last = list[list.length - 1];
    if (e.shiftKey && (document.activeElement === first || document.activeElement === panel)) { e.preventDefault(); last.focus(); }
    else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
  };
  panel.addEventListener('keydown', onKeyDown);

  return {
    release(restore = true) {
      panel.removeEventListener('keydown', onKeyDown);
      if (restore && previous instanceof HTMLElement && previous.isConnected) previous.focus({ preventScroll: true });
    },
  };
}

/** Calls dotnet.invokeMethodAsync('OnMediaChanged', matches) whenever the query changes; returns a disposer. */
export function watchMedia(query, dotnet) {
  const mq = window.matchMedia(query);
  const handler = () => dotnet.invokeMethodAsync('OnMediaChanged', mq.matches);
  mq.addEventListener('change', handler);
  return {
    matches: mq.matches,
    dispose() { mq.removeEventListener('change', handler); },
  };
}

export function mediaMatches(query) {
  return window.matchMedia(query).matches;
}

export function focusElement(el) {
  el?.focus?.({ preventScroll: true });
}

/** `indeterminate` is a property only, so it needs script. */
export function setIndeterminate(el, value) {
  if (el) el.indeterminate = !!value;
}

/** Switches must not submit their form on Enter (some browsers do for checkboxes). */
export function preventEnter(el) {
  el?.addEventListener('keydown', (e) => { if (e.key === 'Enter') e.preventDefault(); });
}
