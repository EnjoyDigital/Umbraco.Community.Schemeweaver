import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_NESTED_MAPPING_MODAL } from './nested-mapping-modal.token.js';
import { SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL } from './complex-type-mapping-modal.token.js';
import { SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL } from './source-origin-picker-modal.token.js';
import { mergeAutoMapSuggestions, applySourceTypeChange } from '../utils/mapping-converters.js';

import type { SchemaPropertyInfo } from '../api/types.js';
import type { PropertyMappingModalData, PropertyMappingModalValue } from './property-mapping-modal.token.js';

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

  @state()
  private _allSchemaProperties: SchemaPropertyInfo[] = [];

  @state()
  private _aiAvailable = false;

  @state()
  private _aiLoading = false;

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
    this._checkAIStatus();
  }

  private async _checkAIStatus() {
    try {
      const status = await this.#repository.requestAIStatus();
      this._aiAvailable = status?.available === true;
    } catch {
      this._aiAvailable = false;
    }
  }

  private async _handleAIAutoMap() {
    this._aiLoading = true;
    try {
      const suggestions = await this.#repository.requestAIAutoMap(
        this.data?.contentTypeAlias || '',
        this.data?.schemaType || '',
      );
      if (suggestions && Array.isArray(suggestions)) {
        this._mappings = mergeAutoMapSuggestions(this._mappings, suggestions);
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_aiAutoMapFailed') },
      });
    } finally {
      this._aiLoading = false;
    }
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
        this._mappings = mergeAutoMapSuggestions(this._mappings, suggestions);
      }

      const [props, schemaProps] = await Promise.all([
        this.#repository.requestContentTypeProperties(this.data?.contentTypeAlias || ''),
        this.#repository.requestSchemaTypeProperties(this.data?.schemaType || ''),
      ]);
      if (props) {
        this._availableProperties = props.map((p) => p.alias);
      }
      if (schemaProps) {
        this._allSchemaProperties = schemaProps;
        // Enrich rows with schema property metadata
        this._mappings = this._mappings.map(row => {
          const sp = schemaProps.find(
            (s: SchemaPropertyInfo) => s.name.toLowerCase() === row.schemaPropertyName.toLowerCase()
          );
          if (sp) {
            return {
              ...row,
              schemaPropertyType: sp.propertyType || row.schemaPropertyType,
              acceptedTypes: sp.acceptedTypes || row.acceptedTypes,
              isComplexType: sp.isComplexType || row.isComplexType,
            };
          }
          return row;
        });
      }
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToLoadMappingData'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._mappings = e.detail.mappings;
  }

  private async _handleConfigureComplexTypeMapping(e: CustomEvent) {
    const { index, schemaPropertyName, acceptedTypes, selectedSubType, resolverConfig } = e.detail;
    if (!this.#modalManagerContext) return;

    const modalHandler = this.#modalManagerContext.open(this, SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL, {
      data: {
        schemaPropertyName,
        acceptedTypes: acceptedTypes || [],
        selectedSubType: selectedSubType || '',
        contentTypeAlias: this.data?.contentTypeAlias || '',
        availableProperties: this._availableProperties,
        existingConfig: resolverConfig,
      },
    });

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._mappings];
        updated[index] = {
          ...updated[index],
          resolverConfig: result.resolverConfig,
          selectedSubType: result.selectedSubType,
          nestedSchemaTypeName: result.selectedSubType,
        };
        this._mappings = updated;
      }
    } catch {
      // Modal was rejected / closed
    }
  }

  private async _handlePickSourceOrigin(e: CustomEvent) {
    const { index, editorAlias, isComplexType, currentSourceType } = e.detail;
    if (!this.#modalManagerContext) return;

    const result = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL, {
        data: { editorAlias, isComplexType, currentSourceType },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.sourceType) return;

    const updated = [...this._mappings];
    updated[index] = applySourceTypeChange(updated[index], result.sourceType);
    this._mappings = updated;
  }

  private async _handleResolveDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    const contentTypes = await this.#repository.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#repository.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._mappings];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: match.alias,
      sourceContentTypeProperties: propertyAliases,
      contentTypePropertyAlias: '',
    };
    this._mappings = updated;
  }

  private async _handleConfigureNestedMapping(e: CustomEvent) {
    const detail = e.detail;
    const index = detail.index as number;
    const mapping = this._mappings[index];

    if (!mapping || !mapping.nestedSchemaTypeName) {
      this.#notificationContext?.peek('warning', {
        data: {
          message: this.localize.term('schemeWeaver_pleaseEnterNestedSchemaType'),
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
        isInherited: false,
        propertyMappings: this._mappings
          .filter((row) => {
            // Only save rows that are actually configured
            if (row.sourceType === 'static') return !!row.staticValue;
            if (row.sourceType === 'complexType') return !!row.resolverConfig;
            if (row.sourceType === 'blockContent') return !!row.contentTypePropertyAlias;
            // property/parent/ancestor/sibling: need a content property alias
            return !!row.contentTypePropertyAlias;
          })
          .map((row) => ({
            schemaPropertyName: row.schemaPropertyName,
            sourceType: row.sourceType,
            contentTypePropertyAlias: row.contentTypePropertyAlias || null,
            sourceContentTypeAlias: row.sourceContentTypeAlias || null,
            transformType: null,
            isAutoMapped: row.confidence !== null,
            staticValue: row.staticValue || null,
            nestedSchemaTypeName: row.nestedSchemaTypeName || null,
            resolverConfig: row.resolverConfig,
            dynamicRootConfig: row.dynamicRootConfig ? JSON.stringify(row.dynamicRootConfig) : null,
          })),
      });

      this.modalContext?.setValue({ saved: true });
      this.modalContext?.submit();
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToSave'),
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
      <umb-body-layout headline="${this.localize.term('schemeWeaver_mapProperties')}: ${this.data?.schemaType ?? ''}">
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
                  ${this._aiAvailable ? html`
                    <uui-button
                      look="outline"
                      color="positive"
                      compact
                      @click=${this._handleAIAutoMap}
                      ?disabled=${this._aiLoading}
                      .state=${this._aiLoading ? 'waiting' : undefined}
                      label=${this.localize.term('schemeWeaver_aiAutoMap')}
                    >
                      <uui-icon name="icon-wand"></uui-icon>
                      ${this._aiLoading ? this.localize.term('schemeWeaver_aiAnalysing') : this.localize.term('schemeWeaver_aiAutoMap')}
                    </uui-button>
                  ` : ''}
                </div>

                <schemeweaver-property-mapping-table
                  .mappings=${this._mappings}
                  .availableProperties=${this._availableProperties}
                  .allSchemaProperties=${this._allSchemaProperties}
                  @mappings-changed=${this._handleMappingsChanged}
                  @configure-nested-mapping=${this._handleConfigureNestedMapping}
                  @configure-complex-type-mapping=${this._handleConfigureComplexTypeMapping}
                  @pick-source-origin=${this._handlePickSourceOrigin}
                  @resolve-document-type=${this._handleResolveDocumentType}
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
