import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { UmbNotificationContext } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { ContentTypeProperty } from '../api/types.js';
import type { PropertyPickerModalData, PropertyPickerModalValue } from './property-picker-modal.token.js';

@customElement('schemeweaver-property-picker-modal')
export class PropertyPickerModalElement extends UmbModalBaseElement<PropertyPickerModalData, PropertyPickerModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: UmbNotificationContext;

  @state()
  private _loading = true;

  @state()
  private _searchTerm = '';

  @state()
  private _properties: ContentTypeProperty[] = [];

  @state()
  private _selectedProperty = '';

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => {
      this.#notificationContext = ctx;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchProperties();
  }

  private async _fetchProperties() {
    this._loading = true;
    try {
      const contentTypeAlias = this.data?.contentTypeAlias;
      if (!contentTypeAlias) {
        this._loading = false;
        return;
      }

      const properties = await this.#repository.requestContentTypeProperties(contentTypeAlias);
      if (properties) {
        this._properties = properties;
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_noProperties') },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleSearch(e: Event) {
    this._searchTerm = (e.target as HTMLInputElement).value.toLowerCase();
  }

  private get _filteredProperties(): ContentTypeProperty[] {
    if (!this._searchTerm) {
      return this._properties;
    }
    return this._properties.filter((prop) =>
      prop.alias.toLowerCase().includes(this._searchTerm) ||
      prop.name.toLowerCase().includes(this._searchTerm)
    );
  }

  private _handleSelect(propertyAlias: string) {
    this._selectedProperty = propertyAlias;
  }

  private _handleSubmit() {
    if (!this._selectedProperty) return;
    this.modalContext?.setValue({ propertyAlias: this._selectedProperty });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_selectProperty')}>
        <uui-box>
          <uui-input
            type="search"
            placeholder=${this.localize.term('schemeWeaver_searchProperties')}
            @input=${this._handleSearch}
            .value=${this._searchTerm}
            class="search-input"
            label=${this.localize.term('schemeWeaver_searchProperties')}
          >
            <div slot="prepend" class="search-prepend">
              <uui-icon name="icon-search"></uui-icon>
            </div>
          </uui-input>

          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>${this.localize.term('schemeWeaver_loadingProperties')}</p>
                </div>
              `
            : html`
                <div class="property-list">
                  ${this._filteredProperties.length > 0
                    ? html`<uui-ref-list>
                        ${repeat(
                          this._filteredProperties,
                          (prop) => prop.alias,
                          (prop) => html`
                            <umb-ref-item
                              selectable
                              select-only
                              ?selected=${this._selectedProperty === prop.alias}
                              name=${prop.alias}
                              detail=${[prop.name, prop.description].filter(Boolean).join(' — ')}
                              icon="icon-document"
                              @selected=${() => this._handleSelect(prop.alias)}
                              @deselected=${() => { this._selectedProperty = ''; }}
                            >
                              ${prop.editorAlias
                                ? html`<uui-tag slot="tag" look="secondary">${prop.editorAlias}</uui-tag>`
                                : nothing}
                            </umb-ref-item>
                          `,
                        )}
                      </uui-ref-list>`
                    : html`<p class="no-results">${this.localize.term('schemeWeaver_noProperties')}</p>`}
                </div>
              `}
        </uui-box>

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button look="primary" @click=${this._handleSubmit} ?disabled=${!this._selectedProperty} label=${this.localize.term('buttons_select')}>
            ${this.localize.term('buttons_select')}
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

      .search-prepend {
        display: flex;
        align-items: center;
        padding: 0 var(--uui-size-space-3);
        color: var(--uui-color-text-alt);
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .property-list {
        max-height: 500px;
        overflow-y: auto;
      }

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        padding: var(--uui-size-space-6);
      }
    `,
  ];
}

export default PropertyPickerModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-picker-modal': PropertyPickerModalElement;
  }
}
