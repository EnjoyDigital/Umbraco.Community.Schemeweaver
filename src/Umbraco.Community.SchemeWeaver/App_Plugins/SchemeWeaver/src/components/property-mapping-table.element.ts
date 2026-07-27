import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import type { RankedSchemaPropertyInfo } from '../api/types.js';
import { SourceType, type SourceTypeValue } from '../constants/source-type.js';
import { summariseResolverConfig } from './block-route-model.js';
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
  sourceType: SourceTypeValue;
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
  sourceType: SourceTypeValue;
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
  /**
   * Server-authoritative range-compatibility warning for this row, if any.
   * Populated from the saved mapping's Warnings (keyed by SchemaPropertyName).
   * The browser has no Schema.org inheritance graph, so this is never computed
   * client-side — a literal membership test would mis-fire on legitimate
   * subtypes (e.g. LocalBusiness under an Organization-ranged property).
   */
  rangeWarning?: string;
  /**
   * Server-authoritative non-blocking advisory for this row, if any (e.g. a
   * stripHtml/wrapInListItem/missing-required hint). Populated from the saved
   * mapping's `suggestion`-severity warnings (keyed by SchemaPropertyName).
   * Rendered as a neutral lightbulb hint, distinct from the red range warning.
   */
  suggestion?: string;
  /**
   * Schema-property recommendation rank (the ranked endpoint's confidence, 0–100) used purely
   * for display ordering — DISTINCT from {@link confidence}, which is the auto-map match quality
   * and drives the High/Med/Low badge. Undefined when ranked data is unavailable.
   */
  schemaRank?: number;
  /**
   * Position of this row in the STORED mapping at load time. Persistence re-emits
   * rows in this order (display sorting is presentation-only) so an untouched save
   * never reorders stored rows — reordering flips uSync drift to content-differs
   * for mappings the user never edited. New rows have no loadOrder and append
   * after all stored rows.
   */
  loadOrder?: number;
  /**
   * The stored isAutoMapped flag, carried through the UI so an untouched save
   * round-trips it verbatim. Rows auto-mapped in THIS session signal via
   * {@link confidence} instead.
   */
  isAutoMapped?: boolean;
  /**
   * The stored top-level transform (e.g. `stripHtml`), carried through the UI
   * so an untouched save round-trips it verbatim — there is no top-level
   * transform editor, so persistence must never null it out.
   */
  transformType?: string | null;
  /**
   * For `reference` rows: the graph piece key the row points at (e.g.
   * `organization`, `website`). Carried through the UI so a save round-trips
   * it — reference rows have no property alias, so persistence keys off this
   * field instead.
   */
  targetPieceKey?: string | null;
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
    import('@umbraco-cms/backoffice/content-picker').catch((err) =>
      console.warn('[SchemeWeaver] Failed to load @umbraco-cms/backoffice/content-picker:', err),
    );
    import('@umbraco-cms/backoffice/document-type').catch((err) =>
      console.warn('[SchemeWeaver] Failed to load @umbraco-cms/backoffice/document-type:', err),
    );
  }

  @property({ type: Array })
  mappings: PropertyMappingRow[] = [];

  @property({ type: Array })
  availableProperties: string[] = [];

  @property({ type: Array })
  allSchemaProperties: RankedSchemaPropertyInfo[] = [];

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
      case SourceType.Property: return 'icon-document';
      case SourceType.Static: return 'icon-edit';
      case SourceType.Parent: return 'icon-arrow-up';
      case SourceType.Ancestor: return 'icon-hierarchy';
      case SourceType.Sibling: return 'icon-split-alt';
      case SourceType.BlockContent: return 'icon-grid';
      case SourceType.ComplexType: return 'icon-brackets';
      case SourceType.Reference: return 'icon-link';
      default: return 'icon-document';
    }
  }

  /** Source type label key mapping */
  private _getSourceLabelKey(sourceType: string): string {
    switch (sourceType) {
      case SourceType.Property: return 'schemeWeaver_sourceCurrentNode';
      case SourceType.Static: return 'schemeWeaver_sourceStaticValue';
      case SourceType.Parent: return 'schemeWeaver_sourceParentNode';
      case SourceType.Ancestor: return 'schemeWeaver_sourceAncestorNode';
      case SourceType.Sibling: return 'schemeWeaver_sourceSiblingNode';
      case SourceType.BlockContent: return 'schemeWeaver_sourceBlockContent';
      case SourceType.ComplexType: return 'schemeWeaver_sourceComplexType';
      case SourceType.Reference: return 'schemeWeaver_sourceReference';
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
    return sourceType === SourceType.Parent || sourceType === SourceType.Ancestor || sourceType === SourceType.Sibling;
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
    if (!mapping.contentTypePropertyAlias && mapping.sourceType !== SourceType.Static) return nothing;
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

  /**
   * Server-authoritative range warning badge. The full message is carried on
   * title + aria-label; the visible text is a short, localised label.
   */
  private _renderRangeWarningBadge(mapping: PropertyMappingRow) {
    if (!mapping.rangeWarning) return nothing;
    return html`<uui-tag
      look="secondary"
      color="warning"
      class="range-warning-badge"
      title=${mapping.rangeWarning}
      aria-label=${mapping.rangeWarning}
    >${this.localize.term('schemeWeaver_rangeWarning')}</uui-tag>`;
  }

  /**
   * Non-blocking advisory hint badge (lightbulb). Distinct from the red
   * range-warning badge: a neutral/positive look so it reads as a helpful
   * suggestion (e.g. stripHtml, wrap-in-list-item) rather than an error. The
   * full advisory text is carried on title + aria-label.
   */
  private _renderSuggestionBadge(mapping: PropertyMappingRow) {
    if (!mapping.suggestion) return nothing;
    return html`<uui-tag
      look="secondary"
      color="positive"
      class="suggestion-badge"
      title=${mapping.suggestion}
      aria-label=${mapping.suggestion}
    ><uui-icon name="icon-lightbulb"></uui-icon>${this.localize.term('schemeWeaver_suggestionHint')}</uui-tag>`;
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

  /**
   * Schema properties not yet in the mappings list. The ranked endpoint already returns them
   * confidence-DESC (recommended first), so we only filter out the already-mapped ones and
   * preserve that order — no re-sorting needed.
   */
  private get _availableSchemaProperties(): RankedSchemaPropertyInfo[] {
    const existingNames = new Set(this.mappings.map(r => r.schemaPropertyName.toLowerCase()));
    return this.allSchemaProperties.filter(sp => !existingNames.has(sp.name.toLowerCase()));
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
      sourceType: schemaProp.isComplexType ? SourceType.ComplexType : SourceType.Property,
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
            <strong>${mapping.schemaPropertyName}</strong>
            <small class="type-label">${mapping.schemaPropertyType}</small>
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
              ? html`<span>${mapping.sourceType === SourceType.Static ? mapping.staticValue : mapping.contentTypePropertyAlias}</span>`
              : this._renderValueInput(mapping, index)}
            <div class="value-badges">${this._renderConfidenceTag(mapping)}${this._renderRangeWarningBadge(mapping)}${this._renderSuggestionBadge(mapping)}</div>
          </div>
        </uui-table-cell>
        <uui-table-cell class="actions-cell">
          ${!this.readonly
            ? html`<uui-button
                compact
                class="remove-row-btn"
                label=${this.localize.term('schemeWeaver_removeProperty')}
                @click=${() => this._handleRemoveRow(index)}
              ><uui-icon name="icon-trash"></uui-icon></uui-button>`
            : nothing}
        </uui-table-cell>
      </uui-table-row>
    `;
  }

  render() {
    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-table-column class="col-property"></uui-table-column>
        <uui-table-column class="col-source"></uui-table-column>
        <uui-table-column class="col-value"></uui-table-column>
        <uui-table-column class="col-actions"></uui-table-column>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell aria-label=${this.localize.term('schemeWeaver_actions')}></uui-table-head-cell>
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

    const regex = this._addPropertySearch ? new RegExp(this._addPropertySearch.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') : null;
    const filtered = regex
      ? available.filter(sp => regex.test(sp.name) || regex.test(sp.propertyType))
      : available;

    // One subtle divider between the recommended block and the long tail (both already
    // ordered recommended-first by the ranked endpoint). Skipped while searching.
    const lastRecommendedIndex = !regex
      ? filtered.reduce((acc, sp, i) => (sp.isPopular ? i : acc), -1)
      : -1;

    return html`
      <div class="add-property-row">
        <uui-form-layout-item>
          <uui-label slot="label" for="add-schema-property">
            ${this.localize.term('schemeWeaver_addSchemaProperty')}
          </uui-label>
          <span slot="description" class="add-property-description">${this.localize.term('schemeWeaver_addSchemaPropertyDescription')}</span>
        <uui-combobox
          id="add-schema-property"
          data-mark="schemeweaver:add-schema-property"
          .value=${this._addPropertyValue}
          label=${this.localize.term('schemeWeaver_addSchemaProperty')}
          placeholder=${this.localize.term('schemeWeaver_addSchemaPropertyPlaceholder')}
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
          <uui-icon slot="input-prepend" name="icon-add" id="add-property-icon"></uui-icon>
          <uui-combobox-list>
            ${repeat(
              filtered,
              (sp) => sp.name,
              (sp, i) => html`
                <uui-combobox-list-option .value=${sp.name} .displayValue=${sp.name}>
                  <div class="add-option">
                    <span class="add-option-name">${sp.name}</span>
                    <small class="add-option-type">${sp.propertyType}</small>
                    ${sp.isPopular
                      ? html`<uui-tag look="secondary" color="positive" class="add-option-rec">
                          ${this.localize.term('schemeWeaver_recommended')}
                        </uui-tag>`
                      : nothing}
                    ${sp.isComplexType
                      ? html`<uui-icon name="icon-brackets" class="add-option-complex-icon"></uui-icon>`
                      : nothing}
                  </div>
                </uui-combobox-list-option>
                ${i === lastRecommendedIndex && i < filtered.length - 1
                  ? html`<hr class="add-option-divider" aria-hidden="true" />`
                  : nothing}
              `,
            )}
          </uui-combobox-list>
        </uui-combobox>
        </uui-form-layout-item>
      </div>
    `;
  }

  private _renderValueInput(mapping: PropertyMappingRow, index: number) {
    if (mapping.sourceType === SourceType.Static) {
      return html`
        <uui-input
          .value=${mapping.staticValue}
          @input=${(e: Event) => this._handleStaticValueChange(index, (e.target as HTMLInputElement).value)}
          placeholder=${this.localize.term('schemeWeaver_enterStaticValue')}
          label=${this.localize.term('schemeWeaver_staticValueForProperty', mapping.schemaPropertyName)}
        ></uui-input>
      `;
    }

    if (mapping.sourceType === SourceType.ComplexType) {
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

    if (mapping.sourceType === SourceType.BlockContent) {
      return this._renderBlockContentInput(mapping, index);
    }

    if (mapping.sourceType === SourceType.Reference) {
      return html`
        <div class="value-inputs">
          <uui-tag look="secondary">
            <uui-icon name="icon-link"></uui-icon>
            ${mapping.targetPieceKey || this.localize.term('schemeWeaver_referenceNoTarget')}
          </uui-tag>
        </div>
      `;
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
            label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaPropertyName)}
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
                label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaPropertyName)}
                placeholder=${this.localize.term('schemeWeaver_selectProperty')}
                @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
              ></schemeweaver-property-combobox>
            `
          : nothing}
      </div>
    `;
  }

  private _renderBlockContentInput(mapping: PropertyMappingRow, index: number) {
    const hasPropertyAlias = !!mapping.contentTypePropertyAlias;

    return html`
      <div class="value-inputs">
        <div class="property-select-row">
          <schemeweaver-property-combobox
            .properties=${this.availableProperties}
            .value=${mapping.contentTypePropertyAlias}
            label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaPropertyName)}
            placeholder=${this.localize.term('schemeWeaver_selectProperty')}
            @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
          ></schemeweaver-property-combobox>
          ${this._renderEditorBadge(mapping.editorAlias)}
        </div>
        <div class="block-actions">
          <uui-button
            look="secondary"
            compact
            data-mark="schemeweaver:map-blocks:${mapping.schemaPropertyName}"
            label=${this.localize.term('schemeWeaver_mapBlocks')}
            title=${hasPropertyAlias ? nothing : this.localize.term('schemeWeaver_mapBlocksDisabledHint')}
            ?disabled=${!hasPropertyAlias}
            @click=${() => this._handleConfigureNestedMapping(index)}
          >
            ${this.localize.term('schemeWeaver_mapBlocks')}
          </uui-button>
          ${mapping.resolverConfig
            ? html`<uui-icon name="icon-check" class="configured-check"></uui-icon>`
            : nothing}
        </div>
        ${this._renderBlockRouteSummary(mapping)}
      </div>
    `;
  }

  /**
   * Honest read-only summary of the configured block routes for a blockContent row.
   * The per-row "Nested Schema Type" input is gone — the backend ignores it once
   * routes exist, so the per-route types shown here are the source of truth. Legacy
   * flat configs (nestedMappings + the persisted nestedSchemaTypeName) summarise as
   * wildcard routes; string-list extraction reports its source property instead.
   */
  private _renderBlockRouteSummary(mapping: PropertyMappingRow) {
    const summary = summariseResolverConfig(mapping.resolverConfig, mapping.nestedSchemaTypeName);
    return html`
      <div class="block-route-summary" data-mark="schemeweaver:block-summary:${mapping.schemaPropertyName}">
        ${summary.kind === 'routes'
          ? summary.routes.map(
              (r) => html`<uui-tag look="secondary" class="route-summary-tag">
                ${r.blockAlias || this.localize.term('schemeWeaver_anyBlock')} → ${r.nestedSchemaType}
              </uui-tag>`,
            )
          : summary.kind === 'stringList'
            ? html`<span class="block-summary-string-list" title=${summary.contentProperty}>
                ${this.localize.term('schemeWeaver_textListSummary', summary.contentProperty)}
              </span>`
            : html`<span class="block-summary-empty">${this.localize.term('schemeWeaver_noBlocksMappedYet')}</span>`}
      </div>
    `;
  }

  static styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
      }

      /* Column sizing — uui-table-column is UUI's <col> equivalent. */
      uui-table-column.col-property {
        width: 30%;
      }

      uui-table-column.col-source {
        width: 18%;
        min-width: 150px;
      }

      uui-table-column.col-actions {
        width: 48px;
      }

      .type-label {
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: var(--uui-type-small-size);
        margin-top: 2px;
      }

      .value-cell {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .value-cell > :first-child {
        flex: 1 1 auto;
        min-width: 0;
      }

      /* Badges cluster left-aligned with a fixed gap right after the input —
         never stretched apart to the far edge of the cell. */
      .value-badges {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
        flex: 0 0 auto;
      }

      .value-badges:empty {
        display: none;
      }

      .confidence-tag {
        flex-shrink: 0;
        font-size: var(--uui-type-small-size);
        --uui-tag-min-height: 22px;
      }

      .value-inputs {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .property-select-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .property-select-row > schemeweaver-property-combobox {
        flex: 1 1 auto;
        min-width: 0;
      }

      .editor-badge {
        font-size: var(--uui-type-small-size);
        --uui-tag-min-height: 20px;
      }

      .suggestion-badge {
        flex-shrink: 0;
        font-size: var(--uui-type-small-size);
        --uui-tag-min-height: 20px;
      }

      .suggestion-badge uui-icon {
        font-size: var(--uui-type-small-size);
        margin-right: 2px;
      }

      .auto-url-indicator {
        color: var(--uui-color-positive);
        font-style: italic;
        white-space: nowrap;
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

      .block-route-summary {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-1);
      }

      .route-summary-tag {
        font-size: var(--uui-type-small-size);
        --uui-tag-min-height: 20px;
      }

      .block-summary-string-list {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      .block-summary-empty {
        color: var(--uui-color-text-alt);
        font-style: italic;
        font-size: var(--uui-type-small-size);
      }

      .no-mappings-hint {
        color: var(--uui-color-text-alt);
        font-style: italic;
        text-align: center;
        padding: var(--uui-size-space-4);
      }

      .source-chip {
        white-space: nowrap;
        font-size: var(--uui-type-default-size);
      }

      .source-chip uui-icon {
        margin-right: var(--uui-size-space-1);
      }

      /* Row actions: icon-only trash in its own column, hover-revealed per row. */
      .actions-cell {
        text-align: right;
      }

      .remove-row-btn {
        opacity: 0;
        transition: opacity 0.15s ease;
      }

      uui-table-row:hover .remove-row-btn,
      uui-table-row:focus-within .remove-row-btn {
        opacity: 0.6;
      }

      .remove-row-btn:hover,
      .remove-row-btn:focus {
        opacity: 1;
      }

      .add-property-row {
        margin-top: var(--uui-size-space-4);
        padding: var(--uui-size-space-4) 0 var(--uui-size-space-3);
        border-top: 1px solid var(--uui-color-divider);
      }

      .add-property-row uui-combobox {
        width: 100%;
      }

      .add-property-description {
        color: var(--uui-color-text-alt);
        font-size: var(--uui-type-small-size);
      }

      #add-property-icon {
        display: flex;
        height: 100%;
        align-items: center;
        padding-left: var(--uui-size-space-2);
        color: var(--uui-color-border);
      }

      .add-option {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        width: 100%;
      }

      .add-option-name {
        font-weight: 500;
      }

      .add-option-type {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: var(--uui-type-small-size);
      }

      .add-option-rec {
        margin-left: auto;
        --uui-tag-min-height: 18px;
        font-size: var(--uui-type-small-size);
      }

      .add-option-complex-icon {
        font-size: var(--uui-type-small-size);
        color: var(--uui-color-text-alt);
      }

      .add-option-divider {
        border: none;
        border-top: 1px solid var(--uui-color-divider);
        margin: var(--uui-size-space-1) 0;
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
