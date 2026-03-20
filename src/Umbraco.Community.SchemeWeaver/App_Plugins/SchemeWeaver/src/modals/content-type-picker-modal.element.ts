import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { ContentTypeInfo } from '../api/types.js';
import type { ContentTypePickerModalData, ContentTypePickerModalValue } from './content-type-picker-modal.token.js';

@customElement('schemeweaver-content-type-picker-modal')
export class ContentTypePickerModalElement extends UmbModalBaseElement<ContentTypePickerModalData, ContentTypePickerModalValue> {
  #repository = new SchemeWeaverRepository(this);

  @state()
  private _loading = true;

  @state()
  private _searchTerm = '';

  @state()
  private _contentTypes: ContentTypeInfo[] = [];

  @state()
  private _selectedAlias = '';

  async connectedCallback() {
    super.connectedCallback();
    if (this.data?.currentAlias) {
      this._selectedAlias = this.data.currentAlias;
    }
    await this._fetchContentTypes();
  }

  private async _fetchContentTypes() {
    this._loading = true;
    try {
      const types = await this.#repository.requestContentTypes();
      if (types) {
        this._contentTypes = types;
      }
    } catch (error) {
      console.error('SchemeWeaver: Error fetching content types:', error);
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

  private _handleSelect(alias: string) {
    this._selectedAlias = alias;
  }

  private _handleSubmit() {
    if (!this._selectedAlias) return;
    const ct = this._contentTypes.find((t) => t.alias === this._selectedAlias);
    this.modalContext?.setValue({
      contentTypeAlias: this._selectedAlias,
      contentTypeName: ct?.name || this._selectedAlias,
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
                <div class="content-type-list" role="listbox">
                  ${this._filteredTypes.length > 0
                    ? this._filteredTypes.map(
                        (ct) => html`
                          <div
                            class="content-type-item ${this._selectedAlias === ct.alias ? 'selected' : ''}"
                            role="option"
                            tabindex="0"
                            aria-selected="${this._selectedAlias === ct.alias}"
                            @click=${() => this._handleSelect(ct.alias)}
                            @keydown=${(e: KeyboardEvent) => {
                              if (e.key === 'Enter' || e.key === ' ') {
                                e.preventDefault();
                                this._handleSelect(ct.alias);
                              }
                            }}
                          >
                            <div class="content-type-item-header">
                              <strong>${ct.name}</strong>
                              <small class="alias-label">${ct.alias}</small>
                            </div>
                            ${ct.propertyCount > 0
                              ? html`<small class="property-count">${ct.propertyCount} ${this.localize.term('schemeWeaver_properties')}</small>`
                              : ''}
                          </div>
                        `
                      )
                    : html`<p class="no-results">${this.localize.term('schemeWeaver_noContentTypes')}</p>`}
                </div>
              `}
        </uui-box>

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button look="primary" @click=${this._handleSubmit} ?disabled=${!this._selectedAlias} label=${this.localize.term('general_select')}>
            ${this.localize.term('general_select')}
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

      .content-type-list {
        max-height: 500px;
        overflow-y: auto;
      }

      .content-type-item {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        cursor: pointer;
        transition: background-color 0.15s ease;
        border: 2px solid transparent;
      }

      .content-type-item:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .content-type-item.selected {
        background-color: var(--uui-color-selected);
        border-color: var(--uui-color-focus);
      }

      .content-type-item-header {
        display: flex;
        align-items: baseline;
        gap: var(--uui-size-space-3);
      }

      .alias-label {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.85rem;
      }

      .property-count {
        color: var(--uui-color-text-alt);
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
