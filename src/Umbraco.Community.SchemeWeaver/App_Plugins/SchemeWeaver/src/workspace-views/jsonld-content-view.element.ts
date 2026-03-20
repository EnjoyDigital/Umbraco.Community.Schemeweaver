import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import '../components/jsonld-preview.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { JsonLdPreviewResponse } from '../api/types.js';

@customElement('schemeweaver-jsonld-content-view')
export class JsonLdContentViewElement extends UmbLitElement {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _generating = false;

  @state()
  private _hasMappng = false;

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
      const workspaceContext = await this.getContext(UMB_WORKSPACE_CONTEXT) as any;

      if (workspaceContext?.getUnique) {
        this._contentKey = workspaceContext.getUnique() ?? '';
      }

      // contentTypeUnique is an observable on the Document workspace context
      if (workspaceContext?.contentTypeUnique) {
        this.observe(
          workspaceContext.contentTypeUnique,
          async (contentTypeId: string | null) => {
            if (contentTypeId) {
              const alias = await this.#repository.resolveContentTypeAlias(contentTypeId);
              if (alias) {
                this._contentTypeAlias = alias;
                await this._checkMapping();
              } else {
                this._hasMappng = false;
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
      const mapping = await this.#repository.requestMapping(this._contentTypeAlias);
      this._hasMappng = !!mapping;
      if (this._hasMappng) {
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
      const result = await this.#repository.requestPreview(this._contentTypeAlias, this._contentKey);
      if (result) {
        this._preview = result;
      } else {
        this._unpublished = true;
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: message || 'Failed to generate preview',
        },
      });
    } finally {
      this._generating = false;
      this._loading = false;
    }
  }

  render() {
    if (this._loading) {
      return html`
        <umb-body-layout headline="JSON-LD">
          <div class="loading">
            <uui-loader-circle></uui-loader-circle>
          </div>
        </umb-body-layout>
      `;
    }

    if (!this._hasMappng) {
      return html`
        <umb-body-layout headline="JSON-LD">
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
      <umb-body-layout headline="JSON-LD">
        <uui-box headline=${this.localize.term('schemeWeaver_jsonLdPreview')}>
          <uui-button
            slot="header-actions"
            look="default"
            compact
            @click=${this._generatePreview}
            ?disabled=${this._generating}
            label=${this.localize.term('schemeWeaver_generatePreview')}
          >
            <uui-icon name="icon-refresh"></uui-icon>
          </uui-button>

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
