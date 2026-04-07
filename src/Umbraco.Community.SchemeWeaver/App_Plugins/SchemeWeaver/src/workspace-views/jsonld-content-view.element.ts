import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import '../components/jsonld-preview.element.js';
import type { JsonLdPreviewElement } from '../components/jsonld-preview.element.js';
import { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import type { JsonLdPreviewResponse } from '../api/types.js';

@customElement('schemeweaver-jsonld-content-view')
export class JsonLdContentViewElement extends UmbLitElement {
  // Per-view context instance — see schema-mapping-view for rationale.
  #context = new SchemeWeaverContext(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _generating = false;

  @state()
  private _hasMapping = false;

  @state()
  private _contentTypeAlias = '';

  @state()
  private _contentKey = '';

  @state()
  private _preview: JsonLdPreviewResponse | null = null;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();

    try {
      const workspaceContext = await this.getContext(UMB_WORKSPACE_CONTEXT) as
        { getUnique?(): string | undefined; contentTypeUnique?: { subscribe(cb: (v: string | null) => void): void } };

      if (workspaceContext?.getUnique) {
        this._contentKey = workspaceContext.getUnique() ?? '';
      }

      // contentTypeUnique is an observable on the Document workspace context
      if (workspaceContext?.contentTypeUnique) {
        this.observe(
          workspaceContext.contentTypeUnique,
          async (contentTypeId: string | null) => {
            if (contentTypeId) {
              const alias = await this.#context.resolveContentTypeAlias(contentTypeId);
              if (alias) {
                this._contentTypeAlias = alias;
                await this._checkMapping();
              } else {
                this._hasMapping = false;
                this._loading = false;
              }
            }
          },
          '_observeContentTypeUnique',
        );
      } else {
        this._loading = false;
      }
    } catch {
      // Workspace context may not be available
      this._loading = false;
    }
  }

  @state()
  private _unpublished = false;

  private async _checkMapping() {
    if (this._contentTypeAlias) {
      const mapping = await this.#context.requestMapping(this._contentTypeAlias);
      this._hasMapping = !!mapping;
      if (this._hasMapping) {
        await this._generatePreview();
        return;
      }
    }
    this._loading = false;
  }

  private async _generatePreview() {
    if (!this._contentTypeAlias || !this._contentKey) {
      this._loading = false;
      return;
    }

    this._generating = true;
    this._unpublished = false;
    try {
      const result = await this.#context.requestPreview(this._contentTypeAlias, this._contentKey);
      if (result) {
        this._preview = result;
      } else {
        this._unpublished = true;
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: message || this.localize.term('schemeWeaver_failedToGeneratePreview'),
        },
      });
    } finally {
      this._generating = false;
      this._loading = false;
    }
  }

  private _handleCopy(): void {
    const previewEl = this.shadowRoot?.querySelector<JsonLdPreviewElement>('schemeweaver-jsonld-preview');
    const text = previewEl?.formattedJson ?? '';
    navigator.clipboard.writeText(text).then(
      () => {
        this.#notificationContext?.peek('positive', {
          data: { message: this.localize.term('schemeWeaver_copiedToClipboard') },
        });
      },
      () => {
        this.#notificationContext?.peek('warning', {
          data: { message: this.localize.term('schemeWeaver_copyFailed') },
        });
      },
    );
  }

  render() {
    if (this._loading) {
      return html`
        <umb-body-layout>
          <div class="loading">
            <uui-loader-circle></uui-loader-circle>
          </div>
        </umb-body-layout>
      `;
    }

    if (!this._hasMapping) {
      return html`
        <umb-body-layout>
          <uui-box>
            <div class="empty-state">
              <uui-icon name="icon-brackets" class="empty-icon"></uui-icon>
              <h3>${this.localize.term('schemeWeaver_noMapping')}</h3>
              <p>${this.localize.term('schemeWeaver_noMappingForPreview')}</p>
            </div>
          </uui-box>
        </umb-body-layout>
      `;
    }

    return html`
      <umb-body-layout>
        <uui-box>
          <span slot="headline">${this.localize.term('schemeWeaver_preview')}</span>
          <div slot="header-actions" class="header-actions">
            ${this._preview
              ? this._preview.isValid
                ? html`<uui-tag look="secondary" color="positive" compact>${this.localize.term('schemeWeaver_valid')}</uui-tag>`
                : html`<uui-tag look="secondary" color="danger" compact>${this.localize.term('schemeWeaver_invalid')}</uui-tag>`
              : nothing}
            <uui-button
              look="default"
              compact
              @click=${this._handleCopy}
              ?disabled=${!this._preview}
              label=${this.localize.term('schemeWeaver_copy')}
            >
              <uui-icon name="icon-documents"></uui-icon>
            </uui-button>
            <uui-button
              look="default"
              compact
              @click=${this._generatePreview}
              ?disabled=${this._generating}
              label=${this.localize.term('schemeWeaver_refresh')}
            >
              <uui-icon name="icon-refresh"></uui-icon>
            </uui-button>
          </div>

          ${this._generating
            ? html`<uui-loader-bar></uui-loader-bar>`
            : this._unpublished
              ? html`<div class="unpublished-message">
                  <uui-icon name="icon-alert"></uui-icon>
                  <span>${this.localize.term('schemeWeaver_publishRequired')}</span>
                </div>`
              : this._preview
                ? html`<schemeweaver-jsonld-preview .jsonLd=${this._preview}></schemeweaver-jsonld-preview>`
                : html`<p class="hint">${this.localize.term('schemeWeaver_noPreviewData')}</p>`}
        </uui-box>
      </umb-body-layout>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
        padding: var(--uui-size-space-5);
      }

      .loading {
        display: flex;
        justify-content: center;
        padding: var(--uui-size-space-6);
      }

      .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-6);
        text-align: center;
      }

      .empty-icon {
        font-size: 3rem;
        color: var(--uui-color-text-alt);
      }

      .header-actions {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .hint {
        color: var(--uui-color-text-alt);
        font-style: italic;
        text-align: center;
        padding: var(--uui-size-space-4);
      }

      .unpublished-message {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4);
        color: var(--uui-color-warning);
        font-style: italic;
      }
    `,
  ];
}

export default JsonLdContentViewElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-jsonld-content-view': JsonLdContentViewElement;
  }
}
