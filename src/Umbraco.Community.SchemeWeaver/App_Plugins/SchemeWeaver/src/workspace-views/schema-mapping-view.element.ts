import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL } from '../modals/source-origin-picker-modal.token.js';
import { SCHEMEWEAVER_NESTED_MAPPING_MODAL } from '../modals/nested-mapping-modal.token.js';
import { SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL } from '../modals/complex-type-mapping-modal.token.js';
import type { SchemaMappingDto, ContentTypeProperty, SchemaPropertyInfo } from '../api/types.js';

import { dtoToRow, mergeAutoMapSuggestions, sortMappingRows, applySourceTypeChange } from '../utils/mapping-converters.js';

@customElement('schemeweaver-schema-mapping-view')
export class SchemaMappingViewElement extends UmbLitElement {
  #context = new SchemeWeaverContext(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _mapping: SchemaMappingDto | null = null;

  @state()
  private _rows: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

  @state()
  private _saving = false;

  @state()
  private _allSchemaProperties: SchemaPropertyInfo[] = [];

  @state()
  private _contentTypeAlias = '';

  @state()
  private _contentTypeKey = '';

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });

    // Auto-save schema mapping when the document type is saved
    this.consumeContext(UMB_ACTION_EVENT_CONTEXT, (context) => {
      context?.addEventListener(
        UmbRequestReloadStructureForEntityEvent.TYPE,
        () => {
          if (this._mapping && this._rows.length > 0) {
            this._handleSave();
          }
        },
      );
    });
  }

  async connectedCallback() {
    super.connectedCallback();

    try {
      const workspaceContext = await this.getContext(UMB_WORKSPACE_CONTEXT) as
        { alias?: { subscribe(cb: (v: string | null) => void): void }; getUnique?(): string | undefined };
      if (workspaceContext?.alias) {
        this.observe(
          workspaceContext.alias,
          (alias: string | null) => {
            if (alias) {
              this._contentTypeAlias = alias;
              this._fetchMapping();
            }
          },
          '_observeAlias'
        );
      }
      if (workspaceContext?.getUnique) {
        const unique = workspaceContext.getUnique();
        if (unique) {
          this._contentTypeKey = unique;
        }
      }
    } catch {
      this._loading = false;
    }
  }

  private async _fetchMapping() {
    this._loading = true;

    try {
      const mapping = await this.#context.requestMapping(this._contentTypeAlias);

      if (!mapping) {
        this._mapping = null;
        this._rows = [];
        this._loading = false;
        return;
      }

      this._mapping = mapping;
      this._rows = sortMappingRows(mapping.propertyMappings.map(dtoToRow));

      // Enrich rows with schema property info (acceptedTypes, isComplexType)
      const schemaProps = await this.#context.requestSchemaTypeProperties(mapping.schemaTypeName);
      if (schemaProps) {
        this._allSchemaProperties = schemaProps;
        this._rows = this._rows.map(row => {
          const schemaProp = schemaProps.find(
            (sp: SchemaPropertyInfo) => sp.name.toLowerCase() === row.schemaPropertyName.toLowerCase()
          );
          if (schemaProp) {
            const enriched = {
              ...row,
              schemaPropertyType: schemaProp.propertyType || row.schemaPropertyType,
              acceptedTypes: schemaProp.acceptedTypes || [],
              isComplexType: schemaProp.isComplexType || false,
            };
            // Restore sub-mappings from saved resolverConfig for complexType rows
            if (enriched.sourceType === 'complexType' && enriched.resolverConfig) {
              try {
                const config = JSON.parse(enriched.resolverConfig);
                if (config.complexTypeMappings?.length) {
                  enriched.selectedSubType = enriched.nestedSchemaTypeName || enriched.acceptedTypes[0] || '';
                  enriched.subMappings = config.complexTypeMappings.map((m: Record<string, string>) => ({
                    schemaProperty: m.schemaProperty || '',
                    schemaPropertyType: '',
                    sourceType: m.sourceType || 'property',
                    contentTypePropertyAlias: m.contentTypePropertyAlias || '',
                    staticValue: m.staticValue || '',
                  }));
                }
              } catch { /* ignore parse errors */ }
            }
            return enriched;
          }
          return row;
        });
        this._rows = sortMappingRows(this._rows);
      }

      const props = await this.#context.requestContentTypeProperties(this._contentTypeAlias);
      if (props) {
        this._availableProperties = props.map((p: ContentTypeProperty) => p.alias);
      }

      // Fetch properties for any existing parent/ancestor/sibling source content types
      const sourceAliases = [...new Set(
        this._rows
          .filter((r) => r.sourceContentTypeAlias && ['parent', 'ancestor', 'sibling'].includes(r.sourceType))
          .map((r) => r.sourceContentTypeAlias)
      )];

      if (sourceAliases.length > 0) {
        const sourcePropsMap = new Map<string, string[]>();
        await Promise.all(
          sourceAliases.map(async (alias) => {
            const sourceProps = await this.#context.requestContentTypeProperties(alias);
            if (sourceProps) {
              sourcePropsMap.set(alias, sourceProps.map((p) => p.alias));
            }
          })
        );

        this._rows = this._rows.map((row) => {
          if (row.sourceContentTypeAlias && sourcePropsMap.has(row.sourceContentTypeAlias)) {
            return { ...row, sourceContentTypeProperties: sourcePropsMap.get(row.sourceContentTypeAlias)! };
          }
          return row;
        });
      }
    } catch (error) {
      console.error('SchemeWeaver: Error fetching mapping:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToLoadMapping'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private async _handleMapToSchema() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const pickerResult = await modalManager
      .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
        data: { contentTypeAlias: this._contentTypeAlias },
      })
      .onSubmit()
      .catch(() => null);

    if (!pickerResult?.schemaType) return;

    const mappingResult = await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias: this._contentTypeAlias,
          schemaType: pickerResult.schemaType,
          contentTypeKey: this._contentTypeKey,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (mappingResult?.saved) {
      await this._fetchMapping();
    }
  }

  private async _handleAutoMap() {
    if (!this._contentTypeAlias || !this._mapping?.schemaTypeName) return;

    this._loading = true;
    try {
      const suggestions = await this.#context.autoMap(
        this._contentTypeAlias,
        this._mapping.schemaTypeName
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._rows = mergeAutoMapSuggestions(this._rows, suggestions);
      }
    } catch (error) {
      console.error('SchemeWeaver: Auto-map error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_autoMapFailed'),
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private async _handleSave() {
    if (!this._mapping) return;

    this._saving = true;
    try {
      const dto: SchemaMappingDto = {
        ...this._mapping,
        contentTypeKey: this._contentTypeKey || this._mapping.contentTypeKey,
        propertyMappings: this._rows
          .filter((row) => {
            if (row.sourceType === 'static') return !!row.staticValue;
            if (row.sourceType === 'complexType') return !!row.resolverConfig;
            if (row.sourceType === 'blockContent') return !!row.contentTypePropertyAlias;
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
          })),
      };
      await this.#context.saveMapping(dto);
      this.#notificationContext?.peek('positive', {
        data: { message: this.localize.term('schemeWeaver_mappingSaved') },
      });
      await this._fetchMapping();
    } catch (error) {
      console.error('SchemeWeaver: Save error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToSave'),
        },
      });
    } finally {
      this._saving = false;
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._rows = e.detail.mappings;
  }

  private _handleInheritedToggle(e: Event) {
    if (!this._mapping) return;
    this._mapping = {
      ...this._mapping,
      isInherited: (e.target as HTMLInputElement).checked,
    };
  }

  private async _handleResolveDocumentType(e: CustomEvent) {
    const { index, documentTypeUnique } = e.detail;
    if (!documentTypeUnique) return;

    // Look up the content type by its unique key to get the alias
    const contentTypes = await this.#context.requestContentTypes();
    const match = contentTypes?.find((ct) => ct.key === documentTypeUnique);
    if (!match) return;

    const props = await this.#context.requestContentTypeProperties(match.alias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const updated = [...this._rows];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: match.alias,
      sourceContentTypeProperties: propertyAliases,
      contentTypePropertyAlias: '',
    };
    this._rows = updated;
  }

  private async _handlePickSourceOrigin(e: CustomEvent) {
    const { index, editorAlias, isComplexType, currentSourceType } = e.detail;
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const result = await modalManager
      .open(this, SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL, {
        data: { editorAlias, isComplexType, currentSourceType },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.sourceType) return;

    const updated = [...this._rows];
    updated[index] = applySourceTypeChange(updated[index], result.sourceType);
    this._rows = updated;
  }

  private async _handleConfigureNestedMapping(e: CustomEvent) {
    const { index } = e.detail;
    const mapping = this._rows[index];

    if (!mapping || !mapping.nestedSchemaTypeName) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_pleaseEnterNestedSchemaType') },
      });
      return;
    }

    if (!mapping.contentTypePropertyAlias) {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_pleaseSelectBlockContentProperty') },
      });
      return;
    }

    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const modalHandler = modalManager.open(this, SCHEMEWEAVER_NESTED_MAPPING_MODAL, {
      data: {
        nestedSchemaTypeName: mapping.nestedSchemaTypeName,
        contentTypePropertyAlias: mapping.contentTypePropertyAlias,
        contentTypeAlias: this._contentTypeAlias,
        existingConfig: mapping.resolverConfig,
      },
    });

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._rows];
        updated[index] = { ...updated[index], resolverConfig: result.resolverConfig };
        this._rows = updated;
      }
    } catch {
      // Modal was rejected / closed — do nothing
    }
  }

  private async _handleConfigureComplexTypeMapping(e: CustomEvent) {
    const { index, schemaPropertyName, acceptedTypes, selectedSubType, resolverConfig } = e.detail;
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const modalHandler = modalManager.open(this, SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL, {
      data: {
        schemaPropertyName,
        acceptedTypes: acceptedTypes || [],
        selectedSubType: selectedSubType || '',
        contentTypeAlias: this._contentTypeAlias,
        availableProperties: this._availableProperties,
        existingConfig: resolverConfig,
      },
    });

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._rows];
        updated[index] = {
          ...updated[index],
          resolverConfig: result.resolverConfig,
          selectedSubType: result.selectedSubType,
          nestedSchemaTypeName: result.selectedSubType,
        };
        this._rows = updated;
      }
    } catch {
      // Modal was rejected / closed
    }
  }

  render() {
    if (this._loading) {
      return html`
        <umb-body-layout headline=${this.localize.term('schemeWeaver_schemaOrgMapping')}>
          <div class="loading">
            <uui-loader-circle></uui-loader-circle>
            <p>${this.localize.term('schemeWeaver_loadingMappings')}</p>
          </div>
        </umb-body-layout>
      `;
    }

    if (!this._mapping) {
      return html`
        <umb-body-layout headline=${this.localize.term('schemeWeaver_schemaOrgMapping')}>
          <uui-box>
            <div class="empty-state">
              <uui-icon name="icon-brackets" class="empty-icon"></uui-icon>
              <h3>${this.localize.term('schemeWeaver_noMapping')}</h3>
              <p>${this.localize.term('schemeWeaver_noMappingDescription')}</p>
              <uui-button look="primary" @click=${this._handleMapToSchema} label=${this.localize.term('schemeWeaver_mapToSchema')}>
                <uui-icon name="icon-brackets"></uui-icon>
                ${this.localize.term('schemeWeaver_mapToSchema')}
              </uui-button>
            </div>
          </uui-box>
        </umb-body-layout>
      `;
    }

    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_schemaOrgMapping')}>
        <uui-box headline=${this.localize.term('schemeWeaver_schemaType')}>
          <div class="schema-type-info">
            <uui-tag color="primary" look="primary">${this._mapping.schemaTypeName}</uui-tag>
            <span class="content-type-alias">${this._mapping.contentTypeAlias}</span>
          </div>
          <div class="inherited-toggle">
            <uui-toggle
              .checked=${this._mapping.isInherited}
              @change=${this._handleInheritedToggle}
              label=${this.localize.term('schemeWeaver_inherited')}
            >
              ${this.localize.term('schemeWeaver_inherited')}
            </uui-toggle>
            <small>${this.localize.term('schemeWeaver_inheritedDescription')}</small>
          </div>
        </uui-box>

        <uui-box headline=${this.localize.term('schemeWeaver_propertyMappings')}>
          <div class="actions-bar" slot="header-actions">
            <uui-button look="outline" compact @click=${this._handleAutoMap} label=${this.localize.term('schemeWeaver_autoMap')}>
              <uui-icon name="icon-wand"></uui-icon>
              ${this.localize.term('schemeWeaver_autoMap')}
            </uui-button>
          </div>

          <schemeweaver-property-mapping-table
            .mappings=${this._rows}
            .availableProperties=${this._availableProperties}
            .allSchemaProperties=${this._allSchemaProperties}
            @mappings-changed=${this._handleMappingsChanged}
            @pick-source-origin=${this._handlePickSourceOrigin}
            @resolve-document-type=${this._handleResolveDocumentType}
            @configure-nested-mapping=${this._handleConfigureNestedMapping}
            @configure-complex-type-mapping=${this._handleConfigureComplexTypeMapping}
          ></schemeweaver-property-mapping-table>
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

      uui-box {
        margin-bottom: var(--uui-size-space-5);
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
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

      .schema-type-info {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .inherited-toggle {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-4);
        margin-top: var(--uui-size-space-4);
        padding-top: var(--uui-size-space-4);
        border-top: 1px solid var(--uui-color-border);
      }

      .inherited-toggle small {
        color: var(--uui-color-text-alt);
      }

      .content-type-alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      .actions-bar {
        display: flex;
        gap: var(--uui-size-space-2);
      }

    `,
  ];
}

export default SchemaMappingViewElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-schema-mapping-view': SchemaMappingViewElement;
  }
}
