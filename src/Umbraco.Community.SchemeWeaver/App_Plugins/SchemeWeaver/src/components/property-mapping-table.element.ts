import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing } from '@umbraco-cms/backoffice/external/lit';

/**
 * UI row model for the property mapping table.
 * Combines fields from PropertyMappingDto and PropertyMappingSuggestion for display/editing.
 */
export interface PropertyMappingRow {
  schemaPropertyName: string;
  schemaPropertyType: string;
  sourceType: string;
  contentTypePropertyAlias: string;
  sourceContentTypeAlias: string;
  staticValue: string;
  confidence: number | null;
  editorAlias: string;
  nestedSchemaTypeName: string;
  resolverConfig: string | null;
}

/** Editor aliases that support block content source type */
const BLOCK_EDITOR_ALIASES = ['Umbraco.BlockList', 'Umbraco.BlockGrid'];

/** Map of complex editor aliases to their display badge labels */
const EDITOR_BADGE_MAP: Record<string, string> = {
  'Umbraco.MediaPicker3': 'schemeWeaver_mediaPicker',
  'Umbraco.BlockList': 'schemeWeaver_blockList',
  'Umbraco.BlockGrid': 'schemeWeaver_blockGrid',
  'Umbraco.ContentPicker': 'schemeWeaver_contentPicker',
  'Umbraco.RichText': 'schemeWeaver_richText',
};

@customElement('schemeweaver-property-mapping-table')
export class PropertyMappingTableElement extends UmbLitElement {
  @property({ type: Array })
  mappings: PropertyMappingRow[] = [];

  @property({ type: Array })
  availableProperties: string[] = [];

  @property({ type: Boolean })
  readonly = false;

  @state()
  private _showUnmapped = false;

  /** Source type values matching C# (lowercase) */
  private _getSourceTypes(editorAlias: string) {
    const types = [
      { value: 'property', label: this.localize.term('schemeWeaver_sourceCurrentNode') },
      { value: 'static', label: this.localize.term('schemeWeaver_sourceStaticValue') },
      { value: 'parent', label: this.localize.term('schemeWeaver_sourceParentNode') },
      { value: 'ancestor', label: this.localize.term('schemeWeaver_sourceAncestorNode') },
      { value: 'sibling', label: this.localize.term('schemeWeaver_sourceSiblingNode') },
    ];

    if (BLOCK_EDITOR_ALIASES.includes(editorAlias)) {
      types.push({ value: 'blockContent', label: this.localize.term('schemeWeaver_sourceBlockContent') });
    }

    return types;
  }

  private _dispatchChange() {
    this.dispatchEvent(
      new CustomEvent('mappings-changed', {
        detail: { mappings: this.mappings },
        bubbles: true,
        composed: true,
      })
    );
  }

  private _handleSourceTypeChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = {
      ...updated[index],
      sourceType: value,
      contentTypePropertyAlias: '',
      staticValue: '',
      sourceContentTypeAlias: '',
      nestedSchemaTypeName: value === 'blockContent' ? updated[index].nestedSchemaTypeName : '',
      resolverConfig: value === 'blockContent' ? updated[index].resolverConfig : null,
    };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handlePropertyChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], contentTypePropertyAlias: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleStaticValueChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], staticValue: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleSourceContentTypeAliasChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], sourceContentTypeAlias: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleNestedSchemaTypeChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], nestedSchemaTypeName: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleConfigureNestedMapping(index: number) {
    const mapping = this.mappings[index];
    this.dispatchEvent(
      new CustomEvent('configure-nested-mapping', {
        detail: {
          index,
          schemaPropertyName: mapping.schemaPropertyName,
          nestedSchemaTypeName: mapping.nestedSchemaTypeName,
          contentTypePropertyAlias: mapping.contentTypePropertyAlias,
          resolverConfig: mapping.resolverConfig,
        },
        bubbles: true,
        composed: true,
      })
    );
  }

  /** Confidence is an integer 0-100 from C# auto-mapper */
  private _renderConfidenceBadge(mapping: PropertyMappingRow) {
    // Only show confidence when there is an actual property mapped
    if (!mapping.contentTypePropertyAlias && mapping.sourceType !== 'static') return nothing;
    const confidence = mapping.confidence;
    if (confidence === null) return nothing;
    if (confidence >= 80) return html`<uui-badge color="positive">${this.localize.term('schemeWeaver_confidenceHigh')}</uui-badge>`;
    if (confidence >= 50) return html`<uui-badge color="warning">${this.localize.term('schemeWeaver_confidenceMedium')}</uui-badge>`;
    return html`<uui-badge color="danger">${this.localize.term('schemeWeaver_confidenceLow')}</uui-badge>`;
  }

  /** Whether a row has an actual mapping configured */
  private _isMapped(mapping: PropertyMappingRow) {
    return !!(mapping.contentTypePropertyAlias || mapping.staticValue || mapping.sourceContentTypeAlias);
  }

  private _renderEditorBadge(editorAlias: string) {
    const termKey = EDITOR_BADGE_MAP[editorAlias];
    if (!termKey) return nothing;
    return html`<uui-tag look="secondary" class="editor-badge">${this.localize.term(termKey)}</uui-tag>`;
  }

  private _getNestedMappingCount(resolverConfig: string | null): number {
    if (!resolverConfig) return 0;
    try {
      const config = JSON.parse(resolverConfig);
      return config.nestedMappings?.length || 0;
    } catch {
      return 0;
    }
  }

  private _renderRow(mapping: PropertyMappingRow, index: number, dimmed = false) {
    return html`
      <uui-table-row class=${dimmed ? 'unmapped-row' : ''}>
        <uui-table-cell>
          <strong>${mapping.schemaPropertyName}</strong>
        </uui-table-cell>
        <uui-table-cell>
          <small class="type-label">${mapping.schemaPropertyType}</small>
        </uui-table-cell>
        <uui-table-cell>
          ${this.readonly
            ? html`<span>${mapping.sourceType}</span>`
            : html`
                <uui-select
                  label=${this.localize.term('schemeWeaver_source')}
                  .options=${this._getSourceTypes(mapping.editorAlias).map((st) => ({
                    name: st.label,
                    value: st.value,
                    selected: mapping.sourceType === st.value,
                  }))}
                  @change=${(e: Event) =>
                    this._handleSourceTypeChange(index, (e.target as HTMLSelectElement).value)}
                ></uui-select>
              `}
        </uui-table-cell>
        <uui-table-cell>
          ${this.readonly
            ? html`<span>${mapping.sourceType === 'static' ? mapping.staticValue : mapping.contentTypePropertyAlias}</span>`
            : this._renderValueInput(mapping, index)}
        </uui-table-cell>
        <uui-table-cell>
          ${this._renderConfidenceBadge(mapping)}
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  render() {
    const mapped: Array<{ mapping: PropertyMappingRow; index: number }> = [];
    const unmapped: Array<{ mapping: PropertyMappingRow; index: number }> = [];

    this.mappings.forEach((mapping, index) => {
      if (this._isMapped(mapping)) {
        mapped.push({ mapping, index });
      } else {
        unmapped.push({ mapping, index });
      }
    });

    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_type')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_confidence')}</uui-table-head-cell>
        </uui-table-head>

        ${mapped.map(({ mapping, index }) => this._renderRow(mapping, index))}

        ${this._showUnmapped
          ? unmapped.map(({ mapping, index }) => this._renderRow(mapping, index, true))
          : nothing}
      </uui-table>

      ${unmapped.length > 0
        ? html`
            <div class="unmapped-toggle">
              <uui-button
                look="placeholder"
                @click=${() => { this._showUnmapped = !this._showUnmapped; }}
                label=${this._showUnmapped ? 'Hide unmapped properties' : `Show ${unmapped.length} unmapped properties`}
              >
                <uui-icon name=${this._showUnmapped ? 'icon-arrow-up' : 'icon-arrow-down'}></uui-icon>
                ${this._showUnmapped
                  ? this.localize.term('schemeWeaver_hideUnmapped')
                  : `${unmapped.length} ${this.localize.term('schemeWeaver_unmappedProperties')}`}
              </uui-button>
            </div>
          `
        : nothing}

      ${mapped.length === 0 && !this._showUnmapped
        ? html`<p class="no-mappings-hint">${this.localize.term('schemeWeaver_noMappedProperties')}</p>`
        : nothing}
    `;
  }

  private _renderValueInput(mapping: PropertyMappingRow, index: number) {
    if (mapping.sourceType === 'static') {
      return html`
        <uui-input
          .value=${mapping.staticValue}
          @input=${(e: Event) => this._handleStaticValueChange(index, (e.target as HTMLInputElement).value)}
          placeholder=${this.localize.term('schemeWeaver_enterStaticValue')}
          label=${this.localize.term('schemeWeaver_staticValueFor') + ' ' + mapping.schemaPropertyName}
        ></uui-input>
      `;
    }

    if (mapping.sourceType === 'blockContent') {
      return this._renderBlockContentInput(mapping, index);
    }

    const needsSourceContentTypeAlias = mapping.sourceType === 'ancestor' || mapping.sourceType === 'sibling';
    const isMediaPicker = mapping.editorAlias === 'Umbraco.MediaPicker3';

    return html`
      <div class="value-inputs">
        ${needsSourceContentTypeAlias
          ? html`
              <uui-input
                .value=${mapping.sourceContentTypeAlias}
                @input=${(e: Event) => this._handleSourceContentTypeAliasChange(index, (e.target as HTMLInputElement).value)}
                placeholder=${this.localize.term('schemeWeaver_contentTypeAliasPlaceholder')}
                label=${this.localize.term('schemeWeaver_contentTypeAlias')}
                class="content-type-input"
              ></uui-input>
            `
          : ''}
        <div class="property-select-row">
          <uui-select
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            .options=${this.availableProperties.map((p) => ({
              name: p,
              value: p,
              selected: mapping.contentTypePropertyAlias === p,
            }))}
            @change=${(e: Event) => this._handlePropertyChange(index, (e.target as HTMLSelectElement).value)}
          ></uui-select>
          ${this._renderEditorBadge(mapping.editorAlias)}
          ${isMediaPicker ? html`<small class="auto-url-indicator">[${this.localize.term('schemeWeaver_autoUrl')}]</small>` : nothing}
        </div>
      </div>
    `;
  }

  private _renderBlockContentInput(mapping: PropertyMappingRow, index: number) {
    const nestedCount = this._getNestedMappingCount(mapping.resolverConfig);

    return html`
      <div class="value-inputs">
        <div class="property-select-row">
          <uui-select
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            .options=${this.availableProperties.map((p) => ({
              name: p,
              value: p,
              selected: mapping.contentTypePropertyAlias === p,
            }))}
            @change=${(e: Event) => this._handlePropertyChange(index, (e.target as HTMLSelectElement).value)}
          ></uui-select>
          ${this._renderEditorBadge(mapping.editorAlias)}
        </div>
        <uui-input
          .value=${mapping.nestedSchemaTypeName}
          @input=${(e: Event) => this._handleNestedSchemaTypeChange(index, (e.target as HTMLInputElement).value)}
          placeholder=${this.localize.term('schemeWeaver_nestedSchemaType')}
          label=${this.localize.term('schemeWeaver_nestedSchemaType')}
          class="nested-schema-input"
        ></uui-input>
        <div class="block-actions">
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term('schemeWeaver_configureNestedMapping')}
            @click=${() => this._handleConfigureNestedMapping(index)}
          >
            ${this.localize.term('schemeWeaver_configureNestedMapping')}
          </uui-button>
          ${nestedCount > 0
            ? html`<uui-tag look="secondary" class="nested-count-badge">${nestedCount} ${this.localize.term('schemeWeaver_nestedMappingCount')}</uui-tag>`
            : nothing}
        </div>
      </div>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .type-label {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      .value-inputs {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .content-type-input {
        font-size: 0.85rem;
      }

      .property-select-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .editor-badge {
        font-size: 0.7rem;
        --uui-tag-min-height: 20px;
      }

      .auto-url-indicator {
        color: var(--uui-color-positive);
        font-style: italic;
        white-space: nowrap;
      }

      .nested-schema-input {
        font-size: 0.85rem;
      }

      .block-actions {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .nested-count-badge {
        font-size: 0.7rem;
        --uui-tag-min-height: 20px;
      }

      uui-select {
        min-width: 150px;
      }

      .unmapped-row {
        opacity: 0.55;
      }

      .unmapped-toggle {
        display: flex;
        justify-content: center;
        padding: var(--uui-size-space-3) 0;
      }

      .no-mappings-hint {
        color: var(--uui-color-text-alt);
        font-style: italic;
        text-align: center;
        padding: var(--uui-size-space-4);
      }
    `,
  ];
}

export default PropertyMappingTableElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-mapping-table': PropertyMappingTableElement;
  }
}
