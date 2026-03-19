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
        <div class="preview-header">
          <strong>${this.localize.term('schemeWeaver_jsonLdPreview')}</strong>
          <div class="preview-actions">
            ${this.jsonLd.isValid
              ? html`<uui-badge color="positive">${this.localize.term('schemeWeaver_valid')}</uui-badge>`
              : html`<uui-badge color="danger">${this.localize.term('schemeWeaver_invalid')}</uui-badge>`}
            <uui-button
              look="outline"
              compact
              @click=${this._handleCopy}
              label=${this.localize.term('schemeWeaver_copyToClipboard')}
            >
              <uui-icon name="icon-documents"></uui-icon>
              ${this.localize.term('schemeWeaver_copy')}
            </uui-button>
          </div>
        </div>
        ${this.jsonLd.errors.length > 0
          ? html`
              <div class="errors">
                ${this.jsonLd.errors.map((err) => html`<div class="error-item">${err}</div>`)}
              </div>
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

      .preview-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        background-color: var(--uui-color-surface-alt);
        border-bottom: 1px solid var(--uui-color-border);
      }

      .preview-actions {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .errors {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        background-color: var(--uui-color-danger-emphasis);
        color: white;
        font-size: 0.85rem;
      }

      .error-item {
        padding: var(--uui-size-space-1) 0;
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
