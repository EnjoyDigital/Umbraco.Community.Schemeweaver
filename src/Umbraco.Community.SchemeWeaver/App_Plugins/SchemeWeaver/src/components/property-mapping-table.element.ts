import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import './property-combobox.element.js';

/** Local type matching UmbContentPickerDynamicRoot to avoid hard import dependency */
interface DynamicRootConfig {
  originAlias: string;
  originKey?: string;
  querySteps?: Array<{ unique: string; alias: string; anyOfDocTypeKeys?: Array<string> }>;
}

/** Sub-property mapping within a complex type configuration */
export interface SubPropertyMapping {
  schemaProperty: string;
  schemaPropertyType: string;
  sourceType: string;
  contentTypePropertyAlias: string;
  staticValue: string;
}

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
  acceptedTypes: string[];
  isComplexType: boolean;
  expanded: boolean;
  subMappings: SubPropertyMapping[];
  selectedSubType: string;
  sourceContentTypeProperties: string[];
  dynamicRootConfig?: DynamicRootConfig;
  sourceDocumentTypeUnique?: string;
}

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
  connectedCallback() {
    super.connectedCallback();
    // Dynamically import Umbraco picker components to register custom elements.
    // These are side-effect imports that register <umb-input-content-picker-document-root>
    // and <umb-input-document-type>. Dynamic import avoids breaking test environments.
    import('@umbraco-cms/backoffice/content-picker').catch(() => {});
    import('@umbraco-cms/backoffice/document-type').catch(() => {});
  }

  @property({ type: Array })
  mappings: PropertyMappingRow[] = [];

  @property({ type: Array })
  availableProperties: string[] = [];

  @property({ type: Boolean })
  readonly = false;

  @property({ type: String })
  contentTypeAlias = '';

  @state()
  private _showMore = false;


  /** Source type icon mapping */
  private _getSourceIcon(sourceType: string): string {
    switch (sourceType) {
      case 'property': return 'icon-document';
      case 'static': return 'icon-edit';
      case 'parent': return 'icon-arrow-up';
      case 'ancestor': return 'icon-hierarchy';
      case 'sibling': return 'icon-split-alt';
      case 'blockContent': return 'icon-grid';
      case 'complexType': return 'icon-brackets';
      default: return 'icon-document';
    }
  }

  /** Source type label key mapping */
  private _getSourceLabelKey(sourceType: string): string {
    switch (sourceType) {
      case 'property': return 'schemeWeaver_sourceCurrentNode';
      case 'static': return 'schemeWeaver_sourceStaticValue';
      case 'parent': return 'schemeWeaver_sourceParentNode';
      case 'ancestor': return 'schemeWeaver_sourceAncestorNode';
      case 'sibling': return 'schemeWeaver_sourceSiblingNode';
      case 'blockContent': return 'schemeWeaver_sourceBlockContent';
      case 'complexType': return 'schemeWeaver_sourceComplexType';
      default: return 'schemeWeaver_sourceCurrentNode';
    }
  }

  private _handlePickSourceOrigin(index: number) {
    const mapping = this.mappings[index];
    this.dispatchEvent(
      new CustomEvent('pick-source-origin', {
        detail: {
          index,
          editorAlias: mapping.editorAlias,
          isComplexType: mapping.isComplexType,
          currentSourceType: mapping.sourceType,
        },
        bubbles: true,
        composed: true,
      })
    );
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

  /** Whether this source type uses the dynamic root + document type picker */
  private _needsSourceContentType(sourceType: string): boolean {
    return sourceType === 'parent' || sourceType === 'ancestor' || sourceType === 'sibling';
  }

  private _handleDynamicRootChange(index: number, e: Event) {
    const target = e.target as HTMLElement & { data?: DynamicRootConfig };
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], dynamicRootConfig: target.data };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleDocumentTypeChange(index: number, e: Event) {
    const target = e.target as HTMLElement & { selection: string[] };
    const selection = target.selection;
    const updated = [...this.mappings];
    updated[index] = {
      ...updated[index],
      sourceDocumentTypeUnique: selection.length > 0 ? selection[0] : undefined,
      contentTypePropertyAlias: '',
    };
    this.mappings = updated;
    this._dispatchChange();

    // Dispatch event so parent can resolve the unique to an alias and load properties
    if (selection.length > 0) {
      this.dispatchEvent(
        new CustomEvent('resolve-document-type', {
          detail: { index, documentTypeUnique: selection[0] },
          bubbles: true,
          composed: true,
        })
      );
    }
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
  private _renderConfidenceTag(mapping: PropertyMappingRow) {
    // Only show confidence when there is an actual property mapped
    if (!mapping.contentTypePropertyAlias && mapping.sourceType !== 'static') return nothing;
    const confidence = mapping.confidence;
    if (confidence === null) return nothing;
    if (confidence >= 80) return html`<uui-tag look="secondary" color="positive" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceHigh')}</uui-tag>`;
    if (confidence >= 50) return html`<uui-tag look="secondary" color="warning" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceMedium')}</uui-tag>`;
    return html`<uui-tag look="secondary" color="danger" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceLow')}</uui-tag>`;
  }

  /** Whether a row has an actual mapping configured or is actively being configured */
  private _isMapped(mapping: PropertyMappingRow) {
    // If the user explicitly chose a non-default source type, keep the row visible
    if (mapping.sourceType !== 'property') return true;
    return !!(mapping.contentTypePropertyAlias || mapping.staticValue || mapping.sourceContentTypeAlias);
  }

  private _renderEditorBadge(editorAlias: string) {
    const termKey = EDITOR_BADGE_MAP[editorAlias];
    if (!termKey) return nothing;
    return html`<uui-tag look="secondary" class="editor-badge">${this.localize.term(termKey)}</uui-tag>`;
  }

  private _handleConfigureComplexType(index: number) {
    const mapping = this.mappings[index];
    this.dispatchEvent(
      new CustomEvent('configure-complex-type-mapping', {
        detail: {
          index,
          schemaPropertyName: mapping.schemaPropertyName,
          acceptedTypes: mapping.acceptedTypes,
          selectedSubType: mapping.selectedSubType,
          resolverConfig: mapping.resolverConfig,
        },
        bubbles: true,
        composed: true,
      })
    );
  }

  private _renderRow(mapping: PropertyMappingRow, index: number, dimmed = false) {
    return html`
      <uui-table-row class=${dimmed ? 'unmapped-row' : ''}>
        <uui-table-cell>
          <div class="property-name-cell">
            <div>
              <strong>${mapping.schemaPropertyName}</strong>
              <small class="type-label">${mapping.schemaPropertyType}</small>
            </div>
          </div>
        </uui-table-cell>
        <uui-table-cell>
          ${this.readonly
            ? html`<span>${this.localize.term(this._getSourceLabelKey(mapping.sourceType))}</span>`
            : html`
                <uui-button
                  compact
                  look="outline"
                  class="source-chip"
                  label=${this.localize.term(this._getSourceLabelKey(mapping.sourceType))}
                  @click=${() => this._handlePickSourceOrigin(index)}
                >
                  <uui-icon name=${this._getSourceIcon(mapping.sourceType)}></uui-icon>
                  ${this.localize.term(this._getSourceLabelKey(mapping.sourceType))}
                </uui-button>
              `}
        </uui-table-cell>
        <uui-table-cell>
          <div class="value-cell">
            ${this.readonly
              ? html`<span>${mapping.sourceType === 'static' ? mapping.staticValue : mapping.contentTypePropertyAlias}</span>`
              : this._renderValueInput(mapping, index)}
            ${this._renderConfidenceTag(mapping)}
          </div>
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  /** Whether a property is "likely" — either already mapped or has a reasonable auto-map suggestion */
  private _isLikely(mapping: PropertyMappingRow) {
    if (this._isMapped(mapping)) return true;
    return mapping.confidence !== null && mapping.confidence >= 50;
  }

  render() {
    // Split into likely (shown by default) and other (behind "Show more")
    const likely: Array<{ mapping: PropertyMappingRow; index: number }> = [];
    const other: Array<{ mapping: PropertyMappingRow; index: number }> = [];

    this.mappings.forEach((mapping, index) => {
      if (this._isLikely(mapping)) {
        likely.push({ mapping, index });
      } else {
        other.push({ mapping, index });
      }
    });

    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
        </uui-table-head>

        ${likely.map(({ mapping, index }) => this._renderRow(mapping, index))}

        ${this._showMore
          ? other.map(({ mapping, index }) => this._renderRow(mapping, index, true))
          : nothing}
      </uui-table>

      ${other.length > 0
        ? html`
            <div class="unmapped-toggle">
              <uui-button
                look="placeholder"
                aria-expanded=${this._showMore}
                @click=${() => { this._showMore = !this._showMore; }}
                label=${this._showMore
                  ? this.localize.term('schemeWeaver_showFewerProperties')
                  : this.localize.term('schemeWeaver_showMoreProperties').replace('{0}', String(other.length))}
              >
                <uui-icon name=${this._showMore ? 'icon-navigation-up' : 'icon-navigation-down'}></uui-icon>
                ${this._showMore
                  ? this.localize.term('schemeWeaver_showFewerProperties')
                  : this.localize.term('schemeWeaver_showMoreProperties').replace('{0}', String(other.length))}
              </uui-button>
            </div>
          `
        : nothing}

      ${likely.length === 0 && other.length > 0 && !this._showMore
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

    if (mapping.sourceType === 'complexType') {
      return html`
        <div class="block-actions">
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term('schemeWeaver_configureComplexType')}
            @click=${() => this._handleConfigureComplexType(index)}
          >
            <uui-icon name="icon-brackets"></uui-icon>
            ${this.localize.term('schemeWeaver_configureComplexType')}
          </uui-button>
          ${mapping.resolverConfig
            ? html`<uui-icon name="icon-check" class="configured-check"></uui-icon>`
            : nothing}
        </div>
      `;
    }

    if (mapping.sourceType === 'blockContent') {
      return this._renderBlockContentInput(mapping, index);
    }

    if (this._needsSourceContentType(mapping.sourceType)) {
      return this._renderSourceContentTypeInput(mapping, index);
    }

    const isMediaPicker = mapping.editorAlias === 'Umbraco.MediaPicker3';

    return html`
      <div class="value-inputs">
        <div class="property-select-row">
          <schemeweaver-property-combobox
            .properties=${this.availableProperties}
            .value=${mapping.contentTypePropertyAlias}
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            placeholder=${this.localize.term('schemeWeaver_selectProperty')}
            @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
          ></schemeweaver-property-combobox>
          ${this._renderEditorBadge(mapping.editorAlias)}
          ${isMediaPicker ? html`<small class="auto-url-indicator">[${this.localize.term('schemeWeaver_autoUrl')}]</small>` : nothing}
        </div>
      </div>
    `;
  }

  private _renderSourceContentTypeInput(mapping: PropertyMappingRow, index: number) {
    return html`
      <div class="value-inputs">
        <umb-input-content-picker-document-root
          .data=${mapping.dynamicRootConfig}
          @change=${(e: Event) => this._handleDynamicRootChange(index, e)}
        ></umb-input-content-picker-document-root>

        <umb-input-document-type
          .documentTypesOnly=${true}
          .max=${1}
          .selection=${mapping.sourceDocumentTypeUnique ? [mapping.sourceDocumentTypeUnique] : []}
          @change=${(e: Event) => this._handleDocumentTypeChange(index, e)}
        ></umb-input-document-type>

        ${mapping.sourceContentTypeProperties?.length
          ? html`
              <schemeweaver-property-combobox
                .properties=${mapping.sourceContentTypeProperties}
                .value=${mapping.contentTypePropertyAlias}
                label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
                placeholder=${this.localize.term('schemeWeaver_selectProperty')}
                @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
              ></schemeweaver-property-combobox>
            `
          : nothing}
      </div>
    `;
  }

  private _renderBlockContentInput(mapping: PropertyMappingRow, index: number) {
    const hasAcceptedTypes = mapping.acceptedTypes.length > 0;

    return html`
      <div class="value-inputs">
        <div class="property-select-row">
          <schemeweaver-property-combobox
            .properties=${this.availableProperties}
            .value=${mapping.contentTypePropertyAlias}
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            placeholder=${this.localize.term('schemeWeaver_selectProperty')}
            @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
          ></schemeweaver-property-combobox>
          ${this._renderEditorBadge(mapping.editorAlias)}
        </div>
        ${hasAcceptedTypes
          ? html`
              <uui-select
                label=${this.localize.term('schemeWeaver_nestedSchemaType')}
                .options=${[
                  { name: this.localize.term('schemeWeaver_selectNestedType'), value: '', selected: !mapping.nestedSchemaTypeName },
                  ...mapping.acceptedTypes.map((t) => ({
                    name: t,
                    value: t,
                    selected: mapping.nestedSchemaTypeName === t,
                  })),
                ]}
                @change=${(e: Event) => this._handleNestedSchemaTypeChange(index, (e.target as HTMLSelectElement).value)}
              ></uui-select>
            `
          : html`
              <uui-input
                .value=${mapping.nestedSchemaTypeName}
                @input=${(e: Event) => this._handleNestedSchemaTypeChange(index, (e.target as HTMLInputElement).value)}
                placeholder=${this.localize.term('schemeWeaver_nestedSchemaType')}
                label=${this.localize.term('schemeWeaver_nestedSchemaType')}
                class="nested-schema-input"
              ></uui-input>
            `}
        <div class="block-actions">
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term('schemeWeaver_configureNestedMapping')}
            @click=${() => this._handleConfigureNestedMapping(index)}
          >
            ${this.localize.term('schemeWeaver_configureNestedMapping')}
          </uui-button>
          ${mapping.resolverConfig
            ? html`<uui-icon name="icon-check" class="configured-check"></uui-icon>`
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
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-top: 2px;
      }

      .value-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .value-cell > :first-child {
        flex: 1;
        min-width: 0;
      }

      .confidence-tag {
        flex-shrink: 0;
        font-size: 0.75rem;
        --uui-tag-min-height: 22px;
      }

      .value-inputs {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .content-type-input {
        font-size: 0.85rem;
      }

      .source-content-type-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
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

      .configured-check {
        color: var(--uui-color-positive);
        font-size: 1.2rem;
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

      .property-name-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .source-chip {
        white-space: nowrap;
        font-size: 0.85rem;
      }

      .source-chip uui-icon {
        margin-right: var(--uui-size-space-1);
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
