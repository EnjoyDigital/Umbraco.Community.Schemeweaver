import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaTypeInfo, SchemaPropertyInfo } from '../api/types.js';
import type { GenerateDoctypeModalData, GenerateDoctypeModalValue } from './generate-doctype-modal.token.js';

@customElement('schemeweaver-generate-doctype-modal')
export class GenerateDoctypeModalElement extends UmbModalBaseElement<GenerateDoctypeModalData, GenerateDoctypeModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = false;

  @state()
  private _generating = false;

  @state()
  private _searchTerm = '';

  @state()
  private _schemaTypes: SchemaTypeInfo[] = [];

  @state()
  private _selectedSchemaType: SchemaTypeInfo | null = null;

  @state()
  private _selectedTypeProperties: SchemaPropertyInfo[] = [];

  @state()
  private _selectedProperties: Set<string> = new Set();

  @state()
  private _documentTypeName = '';

  @state()
  private _documentTypeAlias = '';

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchSchemaTypes();
  }

  private async _fetchSchemaTypes() {
    this._loading = true;
    try {
      const types = await this.#repository.requestSchemaTypes();
      if (types) {
        this._schemaTypes = types;
      }
    } catch (error) {
      console.error('SchemeWeaver: Error fetching schema types:', error);
    } finally {
      this._loading = false;
    }
  }

  private async _handleSearch(e: Event) {
    this._searchTerm = (e.target as HTMLInputElement).value;
    try {
      const types = await this.#repository.requestSchemaTypes(this._searchTerm || undefined);
      if (types) {
        this._schemaTypes = types;
      }
    } catch (error) {
      console.error('SchemeWeaver: Search error:', error);
    }
  }

  private async _handleSelectSchemaType(type: SchemaTypeInfo) {
    this._selectedSchemaType = type;
    this._documentTypeName = type.name;
    this._documentTypeAlias = type.name.charAt(0).toLowerCase() + type.name.slice(1);

    // Fetch full properties from the properties endpoint
    try {
      const props = await this.#repository.requestSchemaTypeProperties(type.name);
      if (props) {
        this._selectedTypeProperties = props;
        this._selectedProperties = new Set(props.map((p) => p.name));
      }
    } catch (error) {
      console.error('SchemeWeaver: Error fetching schema properties:', error);
    }
  }

  private _toggleProperty(name: string) {
    const newSet = new Set(this._selectedProperties);
    if (newSet.has(name)) {
      newSet.delete(name);
    } else {
      newSet.add(name);
    }
    this._selectedProperties = newSet;
  }

  private async _handleGenerate() {
    if (!this._selectedSchemaType || !this._documentTypeName || !this._documentTypeAlias) return;

    this._generating = true;

    try {
      await this.#repository.generateContentType({
        schemaTypeName: this._selectedSchemaType.name,
        documentTypeName: this._documentTypeName,
        documentTypeAlias: this._documentTypeAlias,
        selectedProperties: Array.from(this._selectedProperties),
      });

      this.modalContext?.setValue({ generated: true });
      this.modalContext?.submit();
    } catch (error) {
      console.error('SchemeWeaver: Generate error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to generate content type',
        },
      });
    } finally {
      this._generating = false;
    }
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_generateContentType')}>
        ${!this._selectedSchemaType
          ? this._renderSchemaTypePicker()
          : this._renderPropertySelection()}

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          ${this._selectedSchemaType
            ? html`
                <uui-button look="secondary" @click=${() => (this._selectedSchemaType = null)} label=${this.localize.term('schemeWeaver_back')}>
                  ${this.localize.term('schemeWeaver_back')}
                </uui-button>
                <uui-button
                  look="primary"
                  @click=${this._handleGenerate}
                  ?disabled=${this._generating || this._selectedProperties.size === 0}
                  .state=${this._generating ? 'waiting' : undefined}
                  label=${this.localize.term('schemeWeaver_generate')}
                >
                  ${this._generating ? this.localize.term('schemeWeaver_generating') : this.localize.term('schemeWeaver_generate')}
                </uui-button>
              `
            : ''}
        </div>
      </umb-body-layout>
    `;
  }

  private _renderSchemaTypePicker() {
    return html`
      <uui-box>
        <uui-input
          placeholder=${this.localize.term('schemeWeaver_searchSchemaTypes')}
          @input=${this._handleSearch}
          .value=${this._searchTerm}
          class="search-input"
          label=${this.localize.term('schemeWeaver_searchSchemaTypes')}
        >
          <uui-icon name="icon-search" slot="prepend"></uui-icon>
        </uui-input>

        ${this._loading
          ? html`
              <div class="loading">
                <uui-loader-circle></uui-loader-circle>
              </div>
            `
          : html`
              <div class="schema-list">
                ${this._schemaTypes.map(
                  (type) => html`
                    <div class="schema-item" role="option" tabindex="0" @click=${() => this._handleSelectSchemaType(type)} @keydown=${(e: KeyboardEvent) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); this._handleSelectSchemaType(type); } }}>
                      <strong>${type.name}</strong>
                      <p class="schema-description">${type.description}</p>
                    </div>
                  `
                )}
              </div>
            `}
      </uui-box>
    `;
  }

  private _renderPropertySelection() {
    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_contentTypeSettings')}>
        <div class="naming-fields">
          <uui-form-layout-item>
            <uui-label slot="label">${this.localize.term('schemeWeaver_contentTypeName')}</uui-label>
            <uui-input
              .value=${this._documentTypeName}
              @input=${(e: Event) => (this._documentTypeName = (e.target as HTMLInputElement).value)}
            ></uui-input>
          </uui-form-layout-item>
          <uui-form-layout-item>
            <uui-label slot="label">${this.localize.term('schemeWeaver_contentTypeAlias')}</uui-label>
            <uui-input
              .value=${this._documentTypeAlias}
              @input=${(e: Event) => (this._documentTypeAlias = (e.target as HTMLInputElement).value)}
            ></uui-input>
          </uui-form-layout-item>
        </div>
      </uui-box>

      <uui-box headline=${this.localize.term('schemeWeaver_selectProperties')}>
        <p>${this.localize.term('schemeWeaver_selectPropertiesDescription')}</p>
        <div class="property-list">
          ${this._selectedTypeProperties.map(
            (prop) => html`
              <div class="property-item">
                <uui-checkbox
                  .checked=${this._selectedProperties.has(prop.name)}
                  @change=${() => this._toggleProperty(prop.name)}
                  label=${prop.name}
                ></uui-checkbox>
                <div class="property-details">
                  <small class="property-type">${prop.propertyType}</small>
                </div>
              </div>
            `
          )}
        </div>
      </uui-box>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      uui-box {
        margin-bottom: var(--uui-size-space-4);
      }

      .search-input {
        width: 100%;
        margin-bottom: var(--uui-size-space-4);
      }

      .loading {
        display: flex;
        justify-content: center;
        padding: var(--uui-size-space-6);
      }

      .schema-list {
        max-height: 400px;
        overflow-y: auto;
      }

      .schema-item {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        cursor: pointer;
        transition: background-color 0.15s ease;
      }

      .schema-item:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .schema-description {
        margin: var(--uui-size-space-1) 0 0 0;
        color: var(--uui-color-text-alt);
        font-size: 0.875rem;
      }

      .naming-fields {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-4);
      }

      .property-list {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
        max-height: 400px;
        overflow-y: auto;
      }

      .property-item {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-radius: var(--uui-border-radius);
      }

      .property-item:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .property-details {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-1);
      }

      .property-type {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }
    `,
  ];
}

export default GenerateDoctypeModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-generate-doctype-modal': GenerateDoctypeModalElement;
  }
}
