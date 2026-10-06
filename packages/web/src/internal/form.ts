/**
 * Form association with a fallback for environments without ElementInternals: the closest <form> is
 * used and its `reset` event drives formResetCallback, so submit/reset still work.
 */
export interface FormControlHost extends HTMLElement {
  formResetCallback?(): void;
}

export function attachFormInternals(host: HTMLElement): ElementInternals | undefined {
  try {
    return typeof host.attachInternals === 'function' ? host.attachInternals() : undefined;
  } catch {
    return undefined;
  }
}

export function owningForm(host: HTMLElement, internals: ElementInternals | undefined): HTMLFormElement | null {
  return internals?.form ?? host.closest('form');
}

/** Without ElementInternals the browser never calls formResetCallback; listen for the form's reset instead. */
export function watchFormReset(host: FormControlHost, internals: ElementInternals | undefined): () => void {
  if (internals) return () => {};
  const form = host.closest('form');
  if (!form) return () => {};
  const onReset = () => host.formResetCallback?.();
  form.addEventListener('reset', onReset);
  return () => form.removeEventListener('reset', onReset);
}
