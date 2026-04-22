import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
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
                <div class="property-list" role="listbox" aria-label=${this.localize.term('schemeWeaver_propertiesListLabel')}>
                  ${this._filteredProperties.length > 0
                    ? this._filteredProperties.map(
                        (prop) => html`
                          <div
                            class="property-item ${this._selectedProperty === prop.alias ? 'selected' : ''}"
                            role="option"
                            tabindex="0"
                            aria-selected="${this._selectedProperty === prop.alias}"
                            @click=${() => this._handleSelect(prop.alias)}
                            @keydown=${(e: KeyboardEvent) => {
                              if (e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                this._handleSelect(prop.alias);
                              }
                            }}
                          >
                            <div class="property-item-header">
                              <strong>${prop.alias}</strong>
                              <small class="property-name">${prop.name}</small>
                            </div>
                            ${prop.editorAlias
                              ? html`<small class="property-editor">${prop.editorAlias}</small>`
                              : ''}
                            ${prop.description
                              ? html`<p class="property-description">${prop.description}</p>`
                              : ''}
                          </div>
                        `
                      )
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

      .property-item {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        cursor: pointer;
        transition: background-color 0.15s ease;
        border: 2px solid transparent;
      }

      .property-item:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .property-item.selected {
        background-color: var(--uui-color-selected);
        border-color: var(--uui-color-focus);
        color: var(--uui-color-selected-contrast);
      }

      .property-item.selected .property-name,
      .property-item.selected .property-editor,
      .property-item.selected .property-description {
        color: var(--uui-color-selected-contrast);
        opacity: 0.85;
      }

      .property-item-header {
        display: flex;
        align-items: baseline;
        gap: var(--uui-size-space-3);
      }

      .property-name {
        color: var(--uui-color-text-alt);
        font-style: italic;
      }

      .property-editor {
        color: var(--uui-color-text-alt);
        font-size: 0.8rem;
      }

      .property-description {
        margin: var(--uui-size-space-1) 0 0 0;
        color: var(--uui-color-text-alt);
        font-size: 0.875rem;
        line-height: 1.4;
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
