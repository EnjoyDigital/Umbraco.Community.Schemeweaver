import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property } from '@umbraco-cms/backoffice/external/lit';
import type { JsonLdPreviewResponse } from '../api/types.js';

@customElement('schemeweaver-jsonld-preview')
export class JsonLdPreviewElement extends UmbLitElement {
  @property({ type: Object })
  jsonLd: JsonLdPreviewResponse | null = null;

  private get _formattedJson(): string {
    if (!this.jsonLd?.jsonLd) return '';
    // The jsonLd field is already a JSON string from the API; try to pretty-print it
    try {
      return JSON.stringify(JSON.parse(this.jsonLd.jsonLd), null, 2);
    } catch {
      return this.jsonLd.jsonLd;
    }
  }

  private _handleCopy(): void {
    navigator.clipboard.writeText(this._formattedJson).catch(() => {
      // Clipboard write can fail silently in restricted contexts
    });
  }

  render() {
    if (!this.jsonLd) {
      return html`<div class="empty">${this.localize.term('schemeWeaver_noPreviewData')}</div>`;
    }

    return html`
      <div class="preview-container">
        <div class="preview-toolbar">
          ${this.jsonLd.isValid
            ? html`<uui-tag look="secondary" color="positive">${this.localize.term('schemeWeaver_valid')}</uui-tag>`
            : html`<uui-tag look="secondary" color="danger">${this.localize.term('schemeWeaver_invalid')}</uui-tag>`}
          <uui-button
            look="default"
            compact
            @click=${this._handleCopy}
            label=${this.localize.term('schemeWeaver_copyToClipboard')}
          >
            <uui-icon name="icon-documents"></uui-icon>
            ${this.localize.term('schemeWeaver_copy')}
          </uui-button>
        </div>
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
          : ''}
        <pre class="json-code"><code>${this._formattedJson}</code></pre>
      </div>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .preview-container {
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        overflow: hidden;
      }

      .preview-toolbar {
        display: flex;
        justify-content: flex-end;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        background-color: var(--uui-color-surface-alt);
        border-bottom: 1px solid var(--uui-color-border);
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
        background-color: var(--uui-color-surface);
        font-family: 'Courier New', Courier, monospace;
        font-size: 0.85rem;
        line-height: 1.5;
        max-height: 400px;
        overflow-y: auto;
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
