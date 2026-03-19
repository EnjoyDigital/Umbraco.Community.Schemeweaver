import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import '../components/jsonld-preview.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { PropertyMappingSuggestion, JsonLdPreviewResponse } from '../api/types.js';

import type { PropertyMappingModalData, PropertyMappingModalValue } from './property-mapping-modal.token.js';

/** Convert C# PropertyMappingSuggestion to UI row model */
function suggestionToRow(s: PropertyMappingSuggestion): PropertyMappingRow {
  return {
    schemaPropertyName: s.schemaPropertyName,
    schemaPropertyType: s.schemaPropertyType || '',
    sourceType: s.suggestedSourceType,
    contentTypePropertyAlias: s.suggestedContentTypePropertyAlias || '',
    sourceContentTypeAlias: '',
    staticValue: '',
    confidence: s.confidence,
  };
}

@customElement('schemeweaver-property-mapping-modal')
export class PropertyMappingModalElement extends UmbModalBaseElement<PropertyMappingModalData, PropertyMappingModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _saving = false;

  @state()
  private _mappings: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

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
    await this._initialise();
  }

  private async _initialise() {
    this._loading = true;
    try {
      // Auto-map returns a flat array of PropertyMappingSuggestion
      const suggestions = await this.#repository.requestAutoMap(
        this.data?.contentTypeAlias || '',
        this.data?.schemaType || ''
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._mappings = suggestions.map(suggestionToRow);
      }

      const props = await this.#repository.requestContentTypeProperties(this.data?.contentTypeAlias || '');
      if (props) {
        this._availableProperties = props.map((p) => p.alias);
      }
    } catch (error) {
      console.error('SchemeWeaver: Error initialising property mapping:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to load mapping data',
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._mappings = e.detail.mappings;
    this._preview = null;
  }

  private async _handlePreview() {
    try {
      const result = await this.#repository.requestPreview(this.data?.contentTypeAlias || '');
      if (result) {
        this._preview = result;
      }
    } catch (error) {
      console.error('SchemeWeaver: Preview error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to generate preview',
        },
      });
    }
  }

  private async _handleSave() {
    this._saving = true;

    try {
      await this.#repository.saveMapping({
        contentTypeAlias: this.data?.contentTypeAlias || '',
        contentTypeKey: '',
        schemaTypeName: this.data?.schemaType || '',
        isEnabled: true,
        propertyMappings: this._mappings.map((row) => ({
          schemaPropertyName: row.schemaPropertyName,
          sourceType: row.sourceType,
          contentTypePropertyAlias: row.contentTypePropertyAlias || null,
          sourceContentTypeAlias: row.sourceContentTypeAlias || null,
          transformType: null,
          isAutoMapped: row.confidence !== null,
          staticValue: row.staticValue || null,
          nestedSchemaTypeName: null,
        })),
      });

      this.modalContext?.setValue({ saved: true });
      this.modalContext?.submit();
    } catch (error) {
      console.error('SchemeWeaver: Save error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to save',
        },
      });
    } finally {
      this._saving = false;
    }
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_mapProperties')} - ${this.data?.schemaType || ''}">
        ${this._loading
          ? html`
              <div class="loading">
                <uui-loader-circle></uui-loader-circle>
                <p>${this.localize.term('schemeWeaver_loadingProperties')}</p>
              </div>
            `
          : html`
              <div class="mapping-layout">
                <uui-box headline=${this.localize.term('schemeWeaver_propertyMappings')}>
                  <div class="mapping-info">
                    <uui-tag color="primary">${this.data?.schemaType}</uui-tag>
                    <span>${this.localize.term('schemeWeaver_mappedTo')}</span>
                    <uui-tag color="default">${this.data?.contentTypeAlias}</uui-tag>
                  </div>

                  <schemeweaver-property-mapping-table
                    .mappings=${this._mappings}
                    .availableProperties=${this._availableProperties}
                    @mappings-changed=${this._handleMappingsChanged}
                  ></schemeweaver-property-mapping-table>
                </uui-box>

                <uui-box headline=${this.localize.term('schemeWeaver_jsonLdPreview')}>
                  <uui-button
                    slot="header-actions"
                    look="outline"
                    compact
                    @click=${this._handlePreview}
                    label=${this.localize.term('schemeWeaver_generatePreview')}
                  >
                    <uui-icon name="icon-refresh"></uui-icon>
                    ${this.localize.term('schemeWeaver_generatePreview')}
                  </uui-button>
                  <schemeweaver-jsonld-preview .jsonLd=${this._preview}></schemeweaver-jsonld-preview>
                </uui-box>
              </div>
            `}

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button
            look="primary"
            @click=${this._handleSave}
            ?disabled=${this._saving || this._loading}
            .state=${this._saving ? 'waiting' : undefined}
            label=${this.localize.term('schemeWeaver_save')}
          >
            ${this._saving ? this.localize.term('schemeWeaver_saving') : this.localize.term('schemeWeaver_save')}
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

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .mapping-layout {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-5);
      }

      .mapping-info {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }
    `,
  ];
}

export default PropertyMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-mapping-modal': PropertyMappingModalElement;
  }
}
