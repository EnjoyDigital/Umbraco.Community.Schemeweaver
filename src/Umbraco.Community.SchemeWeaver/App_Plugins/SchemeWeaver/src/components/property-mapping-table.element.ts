import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/workspace';
import { SCHEMEWEAVER_PROPERTY_PICKER_MODAL } from '../modals/property-picker-modal.token.js';

/** Sub-property mapping for complex Schema.org types */
export interface SubPropertyMapping {
  schemaProperty: string;
  schemaPropertyType: string;
  sourceType: string;        // "property" or "static"
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
}

/** Built-in property alias prefix and display name map */
const BUILT_IN_DISPLAY_NAMES: Record<string, string> = {
  '__url': 'URL (Built-in)',
  '__name': 'Name (Built-in)',
  '__createDate': 'Create Date (Built-in)',
  '__updateDate': 'Update Date (Built-in)',
};

/** Returns a display-friendly name for a property alias, with built-in indicator */
function formatPropertyName(alias: string): string {
  return BUILT_IN_DISPLAY_NAMES[alias] ?? alias;
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

  @property({ type: String })
  contentTypeAlias = '';

  @state()
  private _showUnmapped = false;

  @state()
  private _loadingSubProperties: Record<number, boolean> = {};

  /** Source type values matching C# (lowercase) */
  private _getSourceTypes(editorAlias: string, isComplexType: boolean) {
    const types = [
      { value: 'property', label: this.localize.term('schemeWeaver_sourceCurrentNode') },
      { value: 'static', label: this.localize.term('schemeWeaver_sourceStaticValue') },
      { value: 'parent', label: this.localize.term('schemeWeaver_sourceParentNode') },
      { value: 'ancestor', label: this.localize.term('schemeWeaver_sourceAncestorNode') },
      { value: 'sibling', label: this.localize.term('schemeWeaver_sourceSiblingNode') },
    ];

    if (BLOCK_EDITOR_ALIASES.includes(editorAlias) || isComplexType) {
      types.push({ value: 'blockContent', label: this.localize.term('schemeWeaver_sourceBlockContent') });
    }

    if (isComplexType) {
      types.push({ value: 'complexType', label: this.localize.term('schemeWeaver_sourceComplexType') });
    }

    return types;
  }

  private _dispatchChange() {
    // Serialise sub-mappings to resolverConfig for complexType rows
    const mappings = this.mappings.map(m => {
      if (m.sourceType === 'complexType' && m.subMappings.length > 0) {
        const config = {
          complexTypeMappings: m.subMappings
            .filter(s => s.contentTypePropertyAlias || s.staticValue)
            .map(s => ({
              schemaProperty: s.schemaProperty,
              sourceType: s.sourceType,
              contentTypePropertyAlias: s.contentTypePropertyAlias || undefined,
              staticValue: s.staticValue || undefined,
            })),
        };
        return {
          ...m,
          nestedSchemaTypeName: m.selectedSubType,
          resolverConfig: config.complexTypeMappings.length > 0 ? JSON.stringify(config) : null,
        };
      }
      return m;
    });

    this.dispatchEvent(
      new CustomEvent('mappings-changed', {
        detail: { mappings },
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
      sourceContentTypeProperties: [],
      nestedSchemaTypeName: value === 'blockContent' ? updated[index].nestedSchemaTypeName :
                             value === 'complexType' ? updated[index].nestedSchemaTypeName : '',
      resolverConfig: value === 'blockContent' ? updated[index].resolverConfig :
                      value === 'complexType' ? updated[index].resolverConfig : null,
      expanded: value === 'complexType' ? updated[index].expanded : false,
      subMappings: value === 'complexType' ? updated[index].subMappings : [],
      selectedSubType: value === 'complexType' ? updated[index].selectedSubType : '',
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

  /** Whether this source type requires picking a content type */
  private _needsSourceContentType(sourceType: string): boolean {
    return sourceType === 'parent' || sourceType === 'ancestor' || sourceType === 'sibling';
  }

  private _handlePickSourceContentType(index: number) {
    this.dispatchEvent(
      new CustomEvent('pick-source-content-type', {
        detail: { index, currentAlias: this.mappings[index].sourceContentTypeAlias },
        bubbles: true,
        composed: true,
      })
    );
  }

  /** Called by parent after content type picker modal completes */
  public setSourceContentType(index: number, alias: string, properties: string[]) {
    const updated = [...this.mappings];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: alias,
      sourceContentTypeProperties: properties,
      contentTypePropertyAlias: '',
    };
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

  private _handleToggleExpand(index: number) {
    const updated = [...this.mappings];
    const mapping = updated[index];
    updated[index] = { ...mapping, expanded: !mapping.expanded };

    // If expanding and no sub-type selected yet, auto-select first accepted type
    if (!mapping.expanded && !mapping.selectedSubType && mapping.acceptedTypes.length > 0) {
      updated[index] = { ...updated[index], selectedSubType: mapping.acceptedTypes[0] };
      this.mappings = updated;
      this._dispatchChange();
      this._loadSubTypeProperties(index, mapping.acceptedTypes[0]);
    } else {
      this.mappings = updated;
      this._dispatchChange();
    }
  }

  private _handleSubTypeChange(index: number, typeName: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], selectedSubType: typeName, subMappings: [] };
    this.mappings = updated;
    this._dispatchChange();
    this._loadSubTypeProperties(index, typeName);
  }

  private _loadSubTypeProperties(index: number, typeName: string) {
    this._loadingSubProperties = { ...this._loadingSubProperties, [index]: true };
    this.dispatchEvent(
      new CustomEvent('load-sub-type-properties', {
        detail: { index, typeName },
        bubbles: true,
        composed: true,
      })
    );
  }

  public setSubTypeProperties(index: number, properties: Array<{name: string; propertyType: string}>) {
    const updated = [...this.mappings];
    const mapping = updated[index];
    const existingSubs = mapping.subMappings;

    updated[index] = {
      ...mapping,
      subMappings: properties.map(p => {
        // Preserve existing mapping values if we already had a sub-mapping for this property
        const existing = existingSubs.find(
          s => s.schemaProperty.toLowerCase() === p.name.toLowerCase()
        );
        return existing
          ? { ...existing, schemaPropertyType: p.propertyType }
          : {
              schemaProperty: p.name,
              schemaPropertyType: p.propertyType,
              sourceType: 'property',
              contentTypePropertyAlias: '',
              staticValue: '',
            };
      }),
    };
    this.mappings = updated;
    this._loadingSubProperties = { ...this._loadingSubProperties, [index]: false };
    this._dispatchChange();
  }

  private _handleSubMappingSourceChange(parentIndex: number, subIndex: number, value: string) {
    const updated = [...this.mappings];
    const subMappings = [...updated[parentIndex].subMappings];
    subMappings[subIndex] = { ...subMappings[subIndex], sourceType: value, contentTypePropertyAlias: '', staticValue: '' };
    updated[parentIndex] = { ...updated[parentIndex], subMappings };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleSubMappingPropertyChange(parentIndex: number, subIndex: number, value: string) {
    const updated = [...this.mappings];
    const subMappings = [...updated[parentIndex].subMappings];
    subMappings[subIndex] = { ...subMappings[subIndex], contentTypePropertyAlias: value };
    updated[parentIndex] = { ...updated[parentIndex], subMappings };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleSubMappingStaticValueChange(parentIndex: number, subIndex: number, value: string) {
    const updated = [...this.mappings];
    const subMappings = [...updated[parentIndex].subMappings];
    subMappings[subIndex] = { ...subMappings[subIndex], staticValue: value };
    updated[parentIndex] = { ...updated[parentIndex], subMappings };
    this.mappings = updated;
    this._dispatchChange();
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
    const isExpandable = mapping.isComplexType && mapping.sourceType === 'complexType';

    return html`
      <uui-table-row class=${dimmed ? 'unmapped-row' : ''}>
        <uui-table-cell>
          <div class="property-name-cell">
            ${isExpandable && !this.readonly
              ? html`<uui-icon
                  name=${mapping.expanded ? 'icon-navigation-down' : 'icon-navigation-right'}
                  class="expand-chevron"
                  @click=${() => this._handleToggleExpand(index)}
                ></uui-icon>`
              : nothing}
            <div>
              <strong>${mapping.schemaPropertyName}</strong>
              <small class="type-label">${mapping.schemaPropertyType}</small>
            </div>
          </div>
        </uui-table-cell>
        <uui-table-cell>
          ${this.readonly
            ? html`<span>${mapping.sourceType}</span>`
            : html`
                <uui-select
                  label=${this.localize.term('schemeWeaver_source')}
                  .options=${this._getSourceTypes(mapping.editorAlias, mapping.isComplexType).map((st) => ({
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
          <div class="value-cell">
            ${this.readonly
              ? html`<span>${mapping.sourceType === 'static' ? mapping.staticValue : mapping.contentTypePropertyAlias}</span>`
              : this._renderValueInput(mapping, index)}
            ${this._renderConfidenceTag(mapping)}
          </div>
        </uui-table-cell>
      </uui-table-row>
      ${isExpandable && mapping.expanded ? this._renderExpandedSection(mapping, index) : nothing}
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
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
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
                <uui-icon name=${this._showUnmapped ? 'icon-navigation-up' : 'icon-navigation-down'}></uui-icon>
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

  private _renderExpandedSection(mapping: PropertyMappingRow, index: number) {
    const isLoading = this._loadingSubProperties[index];

    return html`
      <uui-table-row class="expanded-section-row">
        <uui-table-cell colspan="3">
          <uui-box class="complex-type-box">
            <div class="sub-type-picker">
              <label>${this.localize.term('schemeWeaver_type')}:</label>
              <uui-select
                label=${this.localize.term('schemeWeaver_selectSubType')}
                .options=${mapping.acceptedTypes.map(t => ({
                  name: t,
                  value: t,
                  selected: mapping.selectedSubType === t,
                }))}
                @change=${(e: Event) => this._handleSubTypeChange(index, (e.target as HTMLSelectElement).value)}
              ></uui-select>
            </div>

            ${isLoading
              ? html`<uui-loader-bar></uui-loader-bar>`
              : mapping.subMappings.length > 0
                ? html`
                    <div class="sub-mappings-list">
                      ${mapping.subMappings.map((sub, subIndex) =>
                        this._renderSubMappingRow(sub, index, subIndex)
                      )}
                    </div>
                  `
                : nothing}
          </uui-box>
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  private _renderSubMappingRow(sub: SubPropertyMapping, parentIndex: number, subIndex: number) {
    return html`
      <div class="sub-mapping-row">
        <div class="sub-property-name">
          <strong>${sub.schemaProperty}</strong>
          <small class="type-label">${sub.schemaPropertyType}</small>
        </div>
        <uui-select
          label=${this.localize.term('schemeWeaver_source')}
          .options=${[
            { name: this.localize.term('schemeWeaver_sourceCurrentNode'), value: 'property', selected: sub.sourceType === 'property' },
            { name: this.localize.term('schemeWeaver_sourceStaticValue'), value: 'static', selected: sub.sourceType === 'static' },
          ]}
          @change=${(e: Event) => this._handleSubMappingSourceChange(parentIndex, subIndex, (e.target as HTMLSelectElement).value)}
        ></uui-select>
        <div class="sub-value-input">
          ${sub.sourceType === 'static'
            ? html`<uui-input
                .value=${sub.staticValue}
                @input=${(e: Event) => this._handleSubMappingStaticValueChange(parentIndex, subIndex, (e.target as HTMLInputElement).value)}
                placeholder=${this.localize.term('schemeWeaver_enterStaticValue')}
                label="Static value"
              ></uui-input>`
            : html`<uui-select
                label="Property"
                .options=${this.availableProperties.map(p => ({
                  name: formatPropertyName(p),
                  value: p,
                  selected: sub.contentTypePropertyAlias === p,
                }))}
                @change=${(e: Event) => this._handleSubMappingPropertyChange(parentIndex, subIndex, (e.target as HTMLSelectElement).value)}
              ></uui-select>`}
        </div>
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
      return html`<small class="complex-type-hint">${this.localize.term('schemeWeaver_configureSubType')}</small>`;
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
          <uui-select
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            .options=${this.availableProperties.map((p) => ({
              name: formatPropertyName(p),
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

  private _renderSourceContentTypeInput(mapping: PropertyMappingRow, index: number) {
    return html`
      <div class="value-inputs">
        ${mapping.sourceContentTypeAlias
          ? html`
              <div class="source-content-type-row">
                <uui-tag look="secondary">${mapping.sourceContentTypeAlias}</uui-tag>
                <uui-button
                  compact
                  look="outline"
                  label=${this.localize.term('schemeWeaver_changeContentType')}
                  @click=${() => this._handlePickSourceContentType(index)}
                >
                  <uui-icon name="icon-edit"></uui-icon>
                </uui-button>
              </div>
              <div class="property-select-row">
                <uui-select
                  label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
                  .options=${(mapping.sourceContentTypeProperties || []).map((p) => ({
                    name: formatPropertyName(p),
                    value: p,
                    selected: mapping.contentTypePropertyAlias === p,
                  }))}
                  @change=${(e: Event) => this._handlePropertyChange(index, (e.target as HTMLSelectElement).value)}
                ></uui-select>
              </div>
            `
          : html`
              <uui-button
                look="placeholder"
                label=${this.localize.term('schemeWeaver_pickContentType')}
                @click=${() => this._handlePickSourceContentType(index)}
              >
                <uui-icon name="icon-document"></uui-icon>
                ${this.localize.term('schemeWeaver_pickContentType')}
              </uui-button>
            `}
      </div>
    `;
  }

  private _renderBlockContentInput(mapping: PropertyMappingRow, index: number) {
    const nestedCount = this._getNestedMappingCount(mapping.resolverConfig);
    const hasAcceptedTypes = mapping.acceptedTypes.length > 0;

    return html`
      <div class="value-inputs">
        <div class="property-select-row">
          <uui-select
            label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
            .options=${this.availableProperties.map((p) => ({
              name: formatPropertyName(p),
              value: p,
              selected: mapping.contentTypePropertyAlias === p,
            }))}
            @change=${(e: Event) => this._handlePropertyChange(index, (e.target as HTMLSelectElement).value)}
          ></uui-select>
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
          ${nestedCount > 0
            ? html`<uui-tag look="secondary" color="positive" class="nested-count-badge">${nestedCount} ${this.localize.term('schemeWeaver_nestedMappingCount')}</uui-tag>`
            : nothing}
          ${mapping.resolverConfig && nestedCount > 0
            ? html`<uui-tag look="secondary" color="warning" class="pre-configured-badge">${this.localize.term('schemeWeaver_preConfigure')}</uui-tag>`
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

      .nested-count-badge {
        font-size: 0.7rem;
        --uui-tag-min-height: 20px;
      }

      .pre-configured-badge {
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

      .property-name-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .expand-chevron {
        cursor: pointer;
        font-size: 0.8rem;
        flex-shrink: 0;
      }

      .expand-chevron:hover {
        color: var(--uui-color-interactive-emphasis);
      }

      .expanded-section-row {
        background: var(--uui-color-surface-alt);
      }

      .complex-type-box {
        margin: var(--uui-size-space-2) var(--uui-size-space-4);
      }

      .sub-type-picker {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-3);
      }

      .sub-type-picker label {
        font-weight: 600;
        white-space: nowrap;
      }

      .sub-mappings-list {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .sub-mapping-row {
        display: grid;
        grid-template-columns: 1fr auto 1fr;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-2) 0;
        border-bottom: 1px solid var(--uui-color-border);
      }

      .sub-mapping-row:last-child {
        border-bottom: none;
      }

      .sub-property-name {
        display: flex;
        flex-direction: column;
      }

      .sub-value-input {
        min-width: 150px;
      }

      .sub-unmapped-hint {
        text-align: center;
        padding: var(--uui-size-space-2);
        color: var(--uui-color-text-alt);
      }

      .complex-type-hint {
        color: var(--uui-color-text-alt);
        font-style: italic;
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
