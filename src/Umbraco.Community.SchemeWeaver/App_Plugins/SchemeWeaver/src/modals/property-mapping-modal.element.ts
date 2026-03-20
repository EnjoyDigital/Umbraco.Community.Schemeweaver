import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { PropertyMappingSuggestion } from '../api/types.js';
import { SCHEMEWEAVER_NESTED_MAPPING_MODAL } from './nested-mapping-modal.token.js';

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
    editorAlias: s.editorAlias || '',
    nestedSchemaTypeName: '',
    resolverConfig: null,
    acceptedTypes: s.acceptedTypes || [],
    isComplexType: s.isComplexType || false,
    expanded: false,
    subMappings: [],
    selectedSubType: '',
  };
}

@customElement('schemeweaver-property-mapping-modal')
export class PropertyMappingModalElement extends UmbModalBaseElement<PropertyMappingModalData, PropertyMappingModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #modalManagerContext?: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _saving = false;

  @state()
  private _mappings: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
    this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (context) => {
      this.#modalManagerContext = context;
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
  }

  private async _handleLoadSubTypeProperties(e: CustomEvent) {
    const { index, typeName } = e.detail;
    const props = await this.#repository.requestSchemaTypeProperties(typeName);
    if (props) {
      const table = this.shadowRoot?.querySelector('schemeweaver-property-mapping-table') as any;
      table?.setSubTypeProperties(index, props.map((p: any) => ({ name: p.name, propertyType: p.propertyType })));
    }
  }

  private async _handleConfigureNestedMapping(e: CustomEvent) {
    const detail = e.detail;
    const index = detail.index as number;
    const mapping = this._mappings[index];

    if (!mapping || !mapping.nestedSchemaTypeName) {
      this.#notificationContext?.peek('warning', {
        data: {
          message: 'Please enter a nested schema type name first.',
        },
      });
      return;
    }

    const modalHandler = this.#modalManagerContext?.open(this, SCHEMEWEAVER_NESTED_MAPPING_MODAL, {
      data: {
        nestedSchemaTypeName: mapping.nestedSchemaTypeName,
        contentTypePropertyAlias: mapping.contentTypePropertyAlias,
        contentTypeAlias: this.data?.contentTypeAlias || '',
        existingConfig: mapping.resolverConfig,
      },
    });

    if (!modalHandler) return;

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._mappings];
        updated[index] = { ...updated[index], resolverConfig: result.resolverConfig };
        this._mappings = updated;
      }
    } catch {
      // Modal was rejected / closed — do nothing
    }
  }

  private async _handleSave() {
    this._saving = true;

    try {
      await this.#repository.saveMapping({
        contentTypeAlias: this.data?.contentTypeAlias || '',
        contentTypeKey: this.data?.contentTypeKey ?? '',
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
          nestedSchemaTypeName: row.nestedSchemaTypeName || null,
          resolverConfig: row.resolverConfig,
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
                  @configure-nested-mapping=${this._handleConfigureNestedMapping}
                  @load-sub-type-properties=${this._handleLoadSubTypeProperties}
                ></schemeweaver-property-mapping-table>
              </uui-box>
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
