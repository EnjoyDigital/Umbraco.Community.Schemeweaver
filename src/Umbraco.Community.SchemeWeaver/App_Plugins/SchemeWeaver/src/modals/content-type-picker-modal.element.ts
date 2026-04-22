import { css, html, customElement, state, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';
import type { ContentTypeInfo } from '../api/types.js';
import type { ContentTypePickerModalData, ContentTypePickerModalValue } from './content-type-picker-modal.token.js';

@customElement('schemeweaver-content-type-picker-modal')
export class ContentTypePickerModalElement extends UmbModalBaseElement<ContentTypePickerModalData, ContentTypePickerModalValue> {
  #context?: SchemeWeaverContext;
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_CONTEXT, (ctx) => {
      this.#context = ctx;
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => {
      this.#notificationContext = ctx;
    });
  }

  @state()
  private _loading = true;

  @state()
  private _searchTerm = '';

  @state()
  private _contentTypes: ContentTypeInfo[] = [];

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchContentTypes();
  }

  private async _fetchContentTypes() {
    this._loading = true;
    try {
      // Await the context — consumeContext fires after connectedCallback.
      const ctx = await this.getContext(SCHEMEWEAVER_CONTEXT);
      this.#context = ctx;
      const types = await ctx.repository.requestContentTypes();
      if (types) {
        this._contentTypes = types;
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_failedToLoadContentTypes') },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleSearch(e: Event) {
    this._searchTerm = (e.target as HTMLInputElement).value.toLowerCase();
  }

  private get _filteredTypes(): ContentTypeInfo[] {
    if (!this._searchTerm) {
      return this._contentTypes;
    }
    return this._contentTypes.filter(
      (ct) =>
        ct.name.toLowerCase().includes(this._searchTerm) ||
        ct.alias.toLowerCase().includes(this._searchTerm)
    );
  }

  private _handleSelect(ct: ContentTypeInfo) {
    this.modalContext?.setValue({
      contentTypeAlias: ct.alias,
      contentTypeName: ct.name,
    });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_selectContentType')}>
        <uui-box>
          <uui-input
            placeholder=${this.localize.term('schemeWeaver_searchContentTypes')}
            @input=${this._handleSearch}
            .value=${this._searchTerm}
            class="search-input"
            label=${this.localize.term('schemeWeaver_searchContentTypes')}
          >
            <uui-icon name="icon-search" slot="prepend"></uui-icon>
          </uui-input>

          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>${this.localize.term('schemeWeaver_loadingContentTypes')}</p>
                </div>
              `
            : html`
                <uui-ref-list>
                  ${this._filteredTypes.length > 0
                    ? repeat(
                        this._filteredTypes,
                        (ct) => ct.alias,
                        (ct) => html`
                          <umb-ref-item
                            name=${ct.name}
                            detail="${ct.alias}${ct.propertyCount > 0 ? ` · ${this.localize.term('schemeWeaver_schemaPropertyCount', ct.propertyCount)}` : ''}"
                            icon="icon-document"
                            @open=${() => this._handleSelect(ct)}
                          ></umb-ref-item>
                        `,
                      )
                    : html`<p class="no-results">${this.localize.term('schemeWeaver_noContentTypes')}</p>`}
                </uui-ref-list>
              `}
        </uui-box>

        <div slot="actions">
          <uui-button look="default" @click=${this._handleClose} label=${this.localize.term('general_close')}>
            ${this.localize.term('general_close')}
          </uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .search-input {
        width: 100%;
        margin-bottom: var(--uui-size-space-4);
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        padding: var(--uui-size-space-6);
      }
    `,
  ];
}

export default ContentTypePickerModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-content-type-picker-modal': ContentTypePickerModalElement;
  }
}
