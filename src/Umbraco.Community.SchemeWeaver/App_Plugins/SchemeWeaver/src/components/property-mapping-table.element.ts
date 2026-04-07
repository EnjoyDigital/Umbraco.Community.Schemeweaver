import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { POPULAR_PROPERTIES } from '../utils/mapping-converters.js';
import type { SchemaPropertyInfo } from '../api/types.js';
import './property-combobox.element.js';

/** Local shape for uui-combobox events — search/value are exposed by the web component. */
interface UUIComboboxEventTarget extends HTMLElement {
  value: string;
  search: string;
}

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
    import('@umbraco-cms/backoffice/content-picker').catch(() => {});
    import('@umbraco-cms/backoffice/document-type').catch(() => {});
  }

  @property({ type: Array })
  mappings: PropertyMappingRow[] = [];

  @property({ type: Array })
  availableProperties: string[] = [];

  @property({ type: Array })
  allSchemaProperties: SchemaPropertyInfo[] = [];

  @property({ type: Boolean })
  readonly = false;

  @property({ type: String })
  contentTypeAlias = '';

  @state()
  private _addPropertySearch = '';

  @state()
  private _addPropertyValue = '';


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
        composed: false,
      })
    );
  }

  private _dispatchChange() {
    this.dispatchEvent(
      new CustomEvent('mappings-changed', {
        detail: { mappings: this.mappings },
        bubbles: true,
        composed: false,
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

    if (selection.length > 0) {
      this.dispatchEvent(
        new CustomEvent('resolve-document-type', {
          detail: { index, documentTypeUnique: selection[0] },
          bubbles: true,
          composed: false,
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
        composed: false,
      })
    );
  }


  /** Confidence is an integer 0-100 from C# auto-mapper */
  private _renderConfidenceTag(mapping: PropertyMappingRow) {
    if (!mapping.contentTypePropertyAlias && mapping.sourceType !== 'static') return nothing;
    const confidence = mapping.confidence;
    if (confidence === null) return nothing;
    if (confidence >= 80) return html`<uui-tag look="secondary" color="positive" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceHigh')}</uui-tag>`;
    if (confidence >= 50) return html`<uui-tag look="secondary" color="warning" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceMedium')}</uui-tag>`;
    return html`<uui-tag look="secondary" color="danger" class="confidence-tag">${this.localize.term('schemeWeaver_confidenceLow')}</uui-tag>`;
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
        composed: false,
      })
    );
  }

  private _handleRemoveRow(index: number) {
    const updated = [...this.mappings];
    updated.splice(index, 1);
    this.mappings = updated;
    this._dispatchChange();
  }

  /** Schema properties not yet in the mappings list, grouped for the combobox */
  private get _availableSchemaProperties(): SchemaPropertyInfo[] {
    const existingNames = new Set(this.mappings.map(r => r.schemaPropertyName.toLowerCase()));
    const popularSet = new Set(POPULAR_PROPERTIES.map(p => p.toLowerCase()));

    return this.allSchemaProperties
      .filter(sp => !existingNames.has(sp.name.toLowerCase()))
      .sort((a, b) => {
        const aPopular = popularSet.has(a.name.toLowerCase());
        const bPopular = popularSet.has(b.name.toLowerCase());
        if (aPopular && !bPopular) return -1;
        if (!aPopular && bPopular) return 1;
        if (aPopular && bPopular) {
          return POPULAR_PROPERTIES.indexOf(a.name) - POPULAR_PROPERTIES.indexOf(b.name);
        }
        if (a.isComplexType && !b.isComplexType) return -1;
        if (!a.isComplexType && b.isComplexType) return 1;
        return a.name.localeCompare(b.name);
      });
  }

  private _handleAddSchemaProperty(propertyName: string) {
    if (!propertyName) return;

    // Guard: prevent duplicate add
    if (this.mappings.some(m => m.schemaPropertyName.toLowerCase() === propertyName.toLowerCase())) return;

    const schemaProp = this.allSchemaProperties.find(
      sp => sp.name.toLowerCase() === propertyName.toLowerCase()
    );
    if (!schemaProp) return;

    const newRow: PropertyMappingRow = {
      schemaPropertyName: schemaProp.name,
      schemaPropertyType: schemaProp.propertyType || '',
      sourceType: schemaProp.isComplexType ? 'complexType' : 'property',
      contentTypePropertyAlias: '',
      sourceContentTypeAlias: '',
      staticValue: '',
      confidence: null,
      editorAlias: '',
      nestedSchemaTypeName: '',
      resolverConfig: null,
      acceptedTypes: schemaProp.acceptedTypes || [],
      isComplexType: schemaProp.isComplexType || false,
      expanded: false,
      subMappings: [],
      selectedSubType: '',
      sourceContentTypeProperties: [],
    };

    this.mappings = [...this.mappings, newRow];
    this._dispatchChange();
  }

  private _renderRow(mapping: PropertyMappingRow, index: number) {
    return html`
      <uui-table-row>
        <uui-table-cell>
          <div class="property-name-cell">
            <div>
              <strong>${mapping.schemaPropertyName}</strong>
              <small class="type-label">${mapping.schemaPropertyType}</small>
            </div>
            ${!this.readonly
              ? html`<uui-button
                  compact
                  look="outline"
                  class="remove-row-btn"
                  label=${this.localize.term('schemeWeaver_removeProperty')}
                  @click=${() => this._handleRemoveRow(index)}
                ><uui-icon name="icon-trash"></uui-icon></uui-button>`
              : nothing}
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

  render() {
    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
        </uui-table-head>

        ${repeat(
          this.mappings,
          (m) => m.schemaPropertyName,
          (mapping, index) => this._renderRow(mapping, index),
        )}
      </uui-table>

      ${!this.readonly && this.allSchemaProperties.length > 0
        ? this._renderAddPropertyCombobox()
        : nothing}

      ${this.mappings.length === 0
        ? html`<p class="no-mappings-hint">${this.localize.term('schemeWeaver_noMappedProperties')}</p>`
        : nothing}
    `;
  }

  private _renderAddPropertyCombobox() {
    const available = this._availableSchemaProperties;
    if (available.length === 0) return nothing;

    const popularSet = new Set(POPULAR_PROPERTIES.map(p => p.toLowerCase()));
    const regex = this._addPropertySearch ? new RegExp(this._addPropertySearch.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') : null;
    const filtered = regex
      ? available.filter(sp => regex.test(sp.name) || regex.test(sp.propertyType))
      : available;

    let lastGroup = '';

    return html`
      <div class="add-property-row">
        <uui-form-layout-item>
          <uui-label slot="label" for="add-schema-property">
            ${this.localize.term('schemeWeaver_addSchemaProperty')}
          </uui-label>
          <span slot="description">${this.localize.term('schemeWeaver_addSchemaPropertyDescription')}</span>
        <uui-combobox
          id="add-schema-property"
          .value=${this._addPropertyValue}
          label=${this.localize.term('schemeWeaver_addSchemaProperty')}
          @search=${(e: Event) => {
            e.stopPropagation();
            this._addPropertySearch = (e.currentTarget as UUIComboboxEventTarget | null)?.search ?? '';
          }}
          @change=${(e: Event) => {
            e.stopPropagation();
            const val = (e.currentTarget as UUIComboboxEventTarget | null)?.value ?? '';
            if (val) {
              this._handleAddSchemaProperty(val);
              this._addPropertyValue = '';
              this._addPropertySearch = '';
            }
          }}
        >
          <uui-combobox-list>
            ${repeat(
              filtered,
              (sp) => sp.name,
              (sp) => {
                const group = popularSet.has(sp.name.toLowerCase())
                  ? 'popular'
                  : sp.isComplexType ? 'complex' : 'other';
                const showGroupLabel = group !== lastGroup;
                lastGroup = group;
                return html`
                  ${showGroupLabel
                    ? html`<uui-combobox-list-option disabled class="combobox-group-label" .value=${''} role="presentation">
                        ${group === 'popular'
                          ? this.localize.term('schemeWeaver_popularProperties')
                          : group === 'complex'
                            ? this.localize.term('schemeWeaver_complexTypeProperties')
                            : this.localize.term('schemeWeaver_otherProperties')}
                      </uui-combobox-list-option>`
                    : nothing}
                  <uui-combobox-list-option .value=${sp.name} .displayValue=${sp.name}>
                    <span class="add-option-name">${sp.name}</span>
                    <small class="add-option-type">${sp.propertyType}</small>
                    ${sp.isComplexType
                      ? html`<uui-icon name="icon-brackets" class="add-option-complex-icon"></uui-icon>`
                      : nothing}
                  </uui-combobox-list-option>
                `;
              },
            )}
          </uui-combobox-list>
        </uui-combobox>
        </uui-form-layout-item>
      </div>
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

      .remove-row-btn {
        opacity: 0;
        transition: opacity 0.15s ease;
        --uui-button-font-size: 0.75rem;
      }

      .property-name-cell:hover .remove-row-btn {
        opacity: 0.6;
      }

      .remove-row-btn:hover {
        opacity: 1 !important;
      }

      .add-property-row {
        padding: var(--uui-size-space-3) 0;
      }

      .add-property-row uui-combobox {
        width: 100%;
      }

      .combobox-group-label {
        font-size: 0.75rem;
        font-weight: 600;
        color: var(--uui-color-text-alt);
        text-transform: uppercase;
        letter-spacing: 0.05em;
        pointer-events: none;
        opacity: 0.7;
      }

      .add-option-name {
        font-weight: 500;
      }

      .add-option-type {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-left: var(--uui-size-space-2);
      }

      .add-option-complex-icon {
        font-size: 0.8rem;
        color: var(--uui-color-text-alt);
        margin-left: var(--uui-size-space-2);
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
