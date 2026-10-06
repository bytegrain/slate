import { LitElement } from 'lit';

/**
 * Base for components with the canonical `Title` option (docs/design/configurability.md). On the web the
 * `title` attribute would also show a native tooltip on the host, so the attribute is read into the
 * property and then removed. Setting the `title` property never creates the attribute.
 */
export class TitledElement extends LitElement {
  private removingTitleAttribute = false;

  override attributeChangedCallback(name: string, old: string | null, value: string | null): void {
    if (name !== 'title') {
      super.attributeChangedCallback(name, old, value);
      return;
    }
    if (value === null && this.removingTitleAttribute) {
      this.removingTitleAttribute = false;
      return;
    }
    super.attributeChangedCallback(name, old, value);
    if (value !== null) {
      this.removingTitleAttribute = true;
      this.removeAttribute('title');
    }
  }
}
