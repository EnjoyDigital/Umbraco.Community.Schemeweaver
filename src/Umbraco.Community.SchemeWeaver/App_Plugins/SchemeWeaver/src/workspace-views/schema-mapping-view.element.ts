import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import '../components/property-mapping-table.element.js';
import '../components/jsonld-preview.element.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaMappingDto, PropertyMappingSuggestion, JsonLdPreviewResponse } from '../api/types.js';

/** Convert stored PropertyMappingDto to UI row model */
function dtoToRow(dto: any): PropertyMappingRow {
  return {
    schemaPropertyName: dto.schemaPropertyName || '',
    schemaPropertyType: '',
    sourceType: dto.sourceType || 'property',
    contentTypePropertyAlias: dto.contentTypePropertyAlias || '',
    sourceContentTypeAlias: dto.sourceContentTypeAlias || '',
    staticValue: dto.staticValue || '',
    confidence: null,
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
  };
}

@customElement('schemeweaver-schema-mapping-view')
export class SchemaMappingViewElement extends UmbLitElement {
  #repository = new SchemeWeaverRepository(this);

  @state()
  private _loading = true;

  @state()
  private _mapping: SchemaMappingDto | null = null;

  @state()
  private _rows: PropertyMappingRow[] = [];

  @state()
  private _availableProperties: string[] = [];

  @state()
  private _preview: JsonLdPreviewResponse | null = null;

  @state()
  private _saving = false;

  @state()
  private _errorMessage = '';

  @state()
  private _contentTypeAlias = '';

  private _workspaceContext?: any;

  async connectedCallback() {
    super.connectedCallback();

    try {
      this._workspaceContext = await this.getContext(UMB_WORKSPACE_CONTEXT);
      if (this._workspaceContext) {
        this.observe(
          this._workspaceContext.unique,
          (unique: string) => {
            if (unique) {
              this._contentTypeAlias = unique;
              this._fetchMapping();
            }
          },
          '_observeUnique'
        );
      }
    } catch (error) {
      console.error('SchemeWeaver: Error getting workspace context:', error);
      this._loading = false;
    }
  }

  private async _fetchMapping() {
    this._loading = true;
    this._errorMessage = '';

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
        this._availableProperties = props.map((p: any) => typeof p === 'string' ? p : p.alias);
      }
    } catch (error) {
      console.error('SchemeWeaver: Error fetching mapping:', error);
      this._errorMessage = error instanceof Error ? error.message : 'Failed to load mapping';
    } finally {
      this._loading = false;
    }
  }

  private async _handleAutoMap() {
    if (!this._contentTypeAlias) return;

    this._loading = true;
    try {
      const suggestions = await this.#repository.requestAutoMap(
        this._contentTypeAlias,
        this._mapping?.schemaTypeName || ''
      );

      if (suggestions && Array.isArray(suggestions)) {
        this._rows = suggestions.map(suggestionToRow);

        if (!this._mapping) {
          this._mapping = {
            contentTypeAlias: this._contentTypeAlias,
            contentTypeKey: '',
            schemaTypeName: '',
            isEnabled: true,
            propertyMappings: [],
          };
        }
      }
    } catch (error) {
      console.error('SchemeWeaver: Auto-map error:', error);
      this._errorMessage = error instanceof Error ? error.message : 'Auto-map failed';
    } finally {
      this._loading = false;
    }
  }

  private async _handlePreview() {
    if (!this._mapping) return;

    try {
      const preview = await this.#repository.requestPreview(this._contentTypeAlias);

      if (preview) {
        this._preview = preview;
      }
    } catch (error) {
      console.error('SchemeWeaver: Preview error:', error);
    }
  }

  private async _handleSave() {
    if (!this._mapping) return;

    this._saving = true;
    try {
      const dto: SchemaMappingDto = {
        ...this._mapping,
        propertyMappings: this._rows.map((row) => ({
          schemaPropertyName: row.schemaPropertyName,
          sourceType: row.sourceType,
          contentTypePropertyAlias: row.contentTypePropertyAlias || null,
          sourceContentTypeAlias: row.sourceContentTypeAlias || null,
          transformType: null,
          isAutoMapped: row.confidence !== null,
          staticValue: row.staticValue || null,
          nestedSchemaTypeName: null,
        })),
      };
      await this.#repository.saveMapping(dto);
      await this._fetchMapping();
    } catch (error) {
      console.error('SchemeWeaver: Save error:', error);
      this._errorMessage = error instanceof Error ? error.message : 'Failed to save';
    } finally {
      this._saving = false;
    }
  }

  private _handleMappingsChanged(e: CustomEvent) {
    this._rows = e.detail.mappings;
  }

  render() {
    if (this._loading) {
      return html`
        <umb-body-layout headline="Schema.org Mapping">
          <div class="loading">
            <uui-loader-circle></uui-loader-circle>
            <p>Loading schema mapping...</p>
          </div>
        </umb-body-layout>
      `;
    }

    if (!this._mapping) {
      return html`
        <umb-body-layout headline="Schema.org Mapping">
          <uui-box>
            <div class="empty-state">
              <uui-icon name="icon-brackets" class="empty-icon"></uui-icon>
              <h3>No Schema.org Mapping</h3>
              <p>This content type has not been mapped to a Schema.org type yet.</p>
              <uui-button look="primary" @click=${this._handleAutoMap}>
                <uui-icon name="icon-wand"></uui-icon>
                Auto-map Schema
              </uui-button>
            </div>
          </uui-box>
        </umb-body-layout>
      `;
    }

    return html`
      <umb-body-layout headline="Schema.org Mapping">
        ${this._errorMessage
          ? html`<div class="error-banner">${this._errorMessage}</div>`
          : ''}

        <uui-box headline="Schema Type">
          <div class="schema-type-info">
            <uui-tag color="primary" look="primary">${this._mapping.schemaTypeName}</uui-tag>
            <span class="content-type-alias">${this._mapping.contentTypeAlias}</span>
          </div>
        </uui-box>

        <uui-box headline="Property Mappings">
          <div class="actions-bar" slot="header-actions">
            <uui-button look="outline" compact @click=${this._handleAutoMap}>
              <uui-icon name="icon-wand"></uui-icon>
              Auto-map
            </uui-button>
            <uui-button look="outline" compact @click=${this._handlePreview}>
              <uui-icon name="icon-brackets"></uui-icon>
              Preview
            </uui-button>
          </div>

          <schemeweaver-property-mapping-table
            .mappings=${this._rows}
            .availableProperties=${this._availableProperties}
            @mappings-changed=${this._handleMappingsChanged}
          ></schemeweaver-property-mapping-table>
        </uui-box>

        ${this._preview
          ? html`
              <uui-box headline="JSON-LD Preview">
                <schemeweaver-jsonld-preview .jsonLd=${this._preview}></schemeweaver-jsonld-preview>
              </uui-box>
            `
          : ''}

        <div class="save-bar">
          <uui-button
            look="primary"
            @click=${this._handleSave}
            ?disabled=${this._saving}
            .state=${this._saving ? 'waiting' : undefined}
          >
            ${this._saving ? 'Saving...' : 'Save Mapping'}
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

      .error-banner {
        background-color: var(--uui-color-danger);
        color: white;
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        margin-bottom: var(--uui-size-space-4);
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
