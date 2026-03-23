import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, nothing } from '@umbraco-cms/backoffice/external/lit';
import { unsafeHTML } from '@umbraco-cms/backoffice/external/lit';
import type { JsonLdPreviewResponse } from '../api/types.js';

@customElement('schemeweaver-jsonld-preview')
export class JsonLdPreviewElement extends UmbLitElement {
  @property({ type: Object })
  jsonLd: JsonLdPreviewResponse | null = null;

  get formattedJson(): string {
    if (!this.jsonLd?.jsonLd) return '';
    try {
      return JSON.stringify(JSON.parse(this.jsonLd.jsonLd), null, 2);
    } catch {
      return this.jsonLd.jsonLd;
    }
  }

  private get _highlightedJson(): string {
    const json = this.formattedJson;
    if (!json) return '';

    // Escape HTML entities first to prevent XSS
    const escaped = json
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Apply syntax highlighting via CSS classes
    return escaped
      // Keys: "propertyName":
      .replace(/(&quot;(?:\\.|[^&])*?&quot;)\s*:/g, '<span class="json-key">$1</span>:')
      // String values after colon
      .replace(/:\s*(&quot;(?:\\.|[^&])*?&quot;)/g, ': <span class="json-string">$1</span>')
      // Numbers
      .replace(/:\s*(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g, ': <span class="json-number">$1</span>')
      // Booleans
      .replace(/:\s*(true|false)/g, ': <span class="json-boolean">$1</span>')
      // Null
      .replace(/:\s*(null)/g, ': <span class="json-null">$1</span>');
  }

  render() {
    if (!this.jsonLd) {
      return html`<div class="empty">${this.localize.term('schemeWeaver_noPreviewData')}</div>`;
    }

    return html`
      ${this.jsonLd.errors.length > 0
        ? html`
            <ul class="error-list">
              ${this.jsonLd.errors.map(
                (err) => html`
                  <li class="error-item">
                    <uui-icon name="icon-alert"></uui-icon>
                    <span>${err}</span>
                  </li>
                `,
              )}
            </ul>
          `
        : nothing}
      <pre class="json-code">${unsafeHTML(this._highlightedJson)}</pre>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .error-list {
        list-style: none;
        margin: 0;
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-border);
      }

      .error-item {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) 0;
        color: var(--uui-color-danger);
        font-size: 0.85rem;
      }

      .error-item uui-icon {
        flex-shrink: 0;
        margin-top: 2px;
      }

      .json-code {
        margin: 0;
        padding: var(--uui-size-space-4);
        overflow-x: auto;
        overflow-y: auto;
        background-color: transparent;
        font-family: 'Courier New', Courier, monospace;
        font-size: 0.85rem;
        line-height: 1.5;
      }

      .json-key {
        color: var(--uui-color-default-emphasis, #1b264f);
      }

      .json-string {
        color: #c2185b;
      }

      .json-number {
        color: #1565c0;
      }

      .json-boolean {
        color: #1565c0;
      }

      .json-null {
        color: #e65100;
      }

      .empty {
        color: var(--uui-color-text-alt);
        font-style: italic;
        padding: var(--uui-size-space-4);
        text-align: center;
      }
    `,
  ];
}

export default JsonLdPreviewElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-jsonld-preview': JsonLdPreviewElement;
  }
}
