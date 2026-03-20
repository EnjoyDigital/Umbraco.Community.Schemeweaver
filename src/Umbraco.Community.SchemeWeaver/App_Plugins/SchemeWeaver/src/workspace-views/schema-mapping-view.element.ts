import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SCHEMEWEAVER_CONTENT_TYPE_PICKER_MODAL } from '../modals/content-type-picker-modal.token.js';
import type { SchemaMappingDto, PropertyMappingDto, PropertyMappingSuggestion, ContentTypeProperty } from '../api/types.js';
import type { PropertyMappingTableElement } from '../components/property-mapping-table.element.js';

/** Convert stored PropertyMappingDto to UI row model */
function dtoToRow(dto: PropertyMappingDto): PropertyMappingRow {
  return {
    schemaPropertyName: dto.schemaPropertyName || '',
    schemaPropertyType: '',
    sourceType: dto.sourceType || 'property',
    contentTypePropertyAlias: dto.contentTypePropertyAlias || '',
    sourceContentTypeAlias: dto.sourceContentTypeAlias || '',
    staticValue: dto.staticValue || '',
    confidence: null,
    editorAlias: '',
    nestedSchemaTypeName: dto.nestedSchemaTypeName || '',
    resolverConfig: dto.resolverConfig || null,
    sourceContentTypeProperties: [],
  };
}

/** Convert PropertyMappingSuggestion to UI row model */
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
    sourceContentTypeProperties: [],
  };
}

@customElement('schemeweaver-schema-mapping-view')
export class SchemaMappingViewElement extends UmbLitElement {
  #repository = new SchemeWeaverRepository(this);
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
  private _contentTypeAlias = '';

  @state()
  private _contentTypeKey = '';

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
      const mapping = await this.#repository.requestMapping(this._contentTypeAlias);

      if (!mapping) {
        this._mapping = null;
        this._rows = [];
        this._loading = false;
        return;
      }

      this._mapping = mapping;
      this._rows = mapping.propertyMappings.map(dtoToRow);

      const props = await this.#repository.requestContentTypeProperties(this._contentTypeAlias);
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
            const sourceProps = await this.#repository.requestContentTypeProperties(alias);
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
          message: error instanceof Error ? error.message : 'Failed to load mapping',
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
      const suggestions = await this.#repository.requestAutoMap(
        this._contentTypeAlias,
        this._mapping.schemaTypeName
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._rows = suggestions.map(suggestionToRow);
      }
    } catch (error) {
      console.error('SchemeWeaver: Auto-map error:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Auto-map failed',
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
        propertyMappings: this._rows.map((row) => ({
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
      await this.#repository.saveMapping(dto);
      this.#notificationContext?.peek('positive', {
        data: { message: this.localize.term('schemeWeaver_mappingSaved') },
      });
      await this._fetchMapping();
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

  private _handleMappingsChanged(e: CustomEvent) {
    this._rows = e.detail.mappings;
  }

  private async _handlePickSourceContentType(e: CustomEvent) {
    const { index, currentAlias } = e.detail;
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const result = await modalManager
      .open(this, SCHEMEWEAVER_CONTENT_TYPE_PICKER_MODAL, {
        data: { currentAlias },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.contentTypeAlias) return;

    const props = await this.#repository.requestContentTypeProperties(result.contentTypeAlias);
    const propertyAliases = props?.map((p) => p.alias) || [];

    const table = this.shadowRoot?.querySelector('schemeweaver-property-mapping-table') as PropertyMappingTableElement | null;
    table?.setSourceContentType(index, result.contentTypeAlias, propertyAliases);
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
            @mappings-changed=${this._handleMappingsChanged}
            @pick-source-content-type=${this._handlePickSourceContentType}
          ></schemeweaver-property-mapping-table>
        </uui-box>

        <div class="save-bar">
          <uui-button
            look="primary"
            @click=${this._handleSave}
            ?disabled=${this._saving}
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

      .content-type-alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      .actions-bar {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .save-bar {
        display: flex;
        justify-content: flex-end;
        padding: var(--uui-size-space-4) 0;
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
