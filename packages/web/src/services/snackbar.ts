import { systemClock, type Clock } from '../core/clock';
import {
  SnackbarQueue,
  type Severity,
  type Snackbar,
  type SnackbarCloseReason,
  type SnackbarConfiguration,
  type SnackbarOptions,
} from '../core/snackbar-queue';

type ShowOptions = Omit<SnackbarOptions, 'message' | 'severity'>;

/**
 * App-wide snackbar API. One queue per service; any number of <sl-snackbar-host> elements render it
 * (normally the one inside <sl-provider>). If none is connected when you show a snackbar, a host is
 * created on document.body so messages are never lost.
 *
 *   snackbar.show('Saved');
 *   snackbar.success('Deployed', { action: { label: 'View', onInvoke: open } });
 */
export class SnackbarService {
  private queueInstance: SnackbarQueue;
  private readonly hosts = new Set<HTMLElement>();
  private readonly queueListeners = new Set<(queue: SnackbarQueue) => void>();

  constructor(
    configuration: Partial<SnackbarConfiguration> = {},
    private readonly clock: Clock = systemClock,
  ) {
    this.queueInstance = new SnackbarQueue(configuration, clock);
  }

  get queue(): SnackbarQueue {
    return this.queueInstance;
  }

  get configuration(): SnackbarConfiguration {
    return this.queueInstance.configuration;
  }

  /** Replaces the configuration. Active snackbars are cleared. */
  configure(configuration: Partial<SnackbarConfiguration>): void {
    this.queueInstance.clear();
    this.queueInstance.dispose();
    this.queueInstance = new SnackbarQueue({ ...this.queueInstance.configuration, ...configuration }, this.clock);
    for (const l of this.queueListeners) l(this.queueInstance);
  }

  /** Hosts call this to follow configure() swaps. */
  onQueueReplaced(listener: (queue: SnackbarQueue) => void): () => void {
    this.queueListeners.add(listener);
    return () => this.queueListeners.delete(listener);
  }

  show(options: SnackbarOptions | string, severity: Severity = 'normal'): Snackbar {
    this.ensureHost();
    return this.queueInstance.add(options, severity);
  }

  info(message: string, options: ShowOptions = {}): Snackbar {
    return this.show({ ...options, message, severity: 'info' });
  }

  success(message: string, options: ShowOptions = {}): Snackbar {
    return this.show({ ...options, message, severity: 'success' });
  }

  warning(message: string, options: ShowOptions = {}): Snackbar {
    return this.show({ ...options, message, severity: 'warning' });
  }

  error(message: string, options: ShowOptions = {}): Snackbar {
    return this.show({ ...options, message, severity: 'error' });
  }

  dismiss(snackbar: Snackbar, reason: SnackbarCloseReason = 'programmatic'): boolean {
    return this.queueInstance.dismiss(snackbar, reason);
  }

  clear(): void {
    this.queueInstance.clear();
  }

  /** @internal Called by <sl-snackbar-host>. */
  registerHost(host: HTMLElement): () => void {
    this.hosts.add(host);
    return () => this.hosts.delete(host);
  }

  get hasHost(): boolean {
    return this.hosts.size > 0;
  }

  private ensureHost(): void {
    if (this.hasHost || typeof document === 'undefined' || !customElements.get('sl-snackbar-host')) return;
    const host = document.createElement('sl-snackbar-host');
    (document.querySelector('sl-provider') ?? document.body).append(host);
  }
}

/** The default, page-wide snackbar service. */
export const snackbar = new SnackbarService();
