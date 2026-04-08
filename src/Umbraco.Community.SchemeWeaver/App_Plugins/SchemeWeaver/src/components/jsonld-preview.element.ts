import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { JsonLdPreviewResponse } from '../api/types.js';

interface JsonToken {
  type: 'key' | 'string' | 'number' | 'boolean' | 'null' | 'text';
  value: string;
}

@customElement('schemeweaver-jsonld-preview')
export class JsonLdPreviewElement extends UmbLitElement {
  @property({ type: Object, attribute: false })
  jsonLd: JsonLdPreviewResponse | null = null;

  get formattedJson(): string {
    if (!this.jsonLd?.jsonLd) return '';
    try {
      return JSON.stringify(JSON.parse(this.jsonLd.jsonLd), null, 2);
    } catch {
      return this.jsonLd.jsonLd;
    }
  }

  /** Tokenise formatted JSON for safe syntax highlighting without innerHTML. */
  private _tokeniseJson(): JsonToken[] {
    const json = this.formattedJson;
    if (!json) return [];

    const tokens: JsonToken[] = [];
    let i = 0;

    while (i < json.length) {
      const ch = json[i];

      if (ch === '"') {
        // Consume a quoted string (handles escaped characters)
        let end = i + 1;
        while (end < json.length && json[end] !== '"') {
          if (json[end] === '\\') end++; // skip escaped char
          end++;
        }
        const str = json.substring(i, end + 1);

        // Determine if this is a key (followed by ':') or a string value
        let j = end + 1;
        while (j < json.length && /\s/.test(json[j])) j++;
        tokens.push({ type: json[j] === ':' ? 'key' : 'string', value: str });
        i = end + 1;
      } else if (/[-0-9]/.test(ch)) {
        // Number
        let end = i;
        while (end < json.length && /[0-9.eE+\-]/.test(json[end])) end++;
        tokens.push({ type: 'number', value: json.substring(i, end) });
        i = end;
      } else if (json.substring(i, i + 4) === 'true') {
        tokens.push({ type: 'boolean', value: 'true' });
        i += 4;
      } else if (json.substring(i, i + 5) === 'false') {
        tokens.push({ type: 'boolean', value: 'false' });
        i += 5;
      } else if (json.substring(i, i + 4) === 'null') {
        tokens.push({ type: 'null', value: 'null' });
        i += 4;
      } else {
        // Whitespace, brackets, colons, commas — accumulate plain text
        let end = i + 1;
        while (end < json.length && !/["0-9\-tfn]/.test(json[end])) end++;
        tokens.push({ type: 'text', value: json.substring(i, end) });
        i = end;
      }
    }

    return tokens;
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
      <pre class="json-code">${this._tokeniseJson().map((t) =>
        t.type === 'text'
          ? t.value
          : html`<span class="json-${t.type}">${t.value}</span>`
      )}</pre>
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
        color: var(--uui-color-default-emphasis, var(--uui-color-text));
      }

      .json-string {
        color: var(--uui-color-danger-emphasis, var(--uui-color-danger));
      }

      .json-number {
        color: var(--uui-color-interactive-emphasis, var(--uui-color-interactive));
      }

      .json-boolean {
        color: var(--uui-color-interactive-emphasis, var(--uui-color-interactive));
      }

      .json-null {
        color: var(--uui-color-warning-emphasis, var(--uui-color-warning));
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
