import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaPropertyInfo, RankedSchemaPropertyInfo, BlockElementTypeInfo } from '../api/types.js';

import type { NestedMappingModalData, NestedMappingModalValue } from './nested-mapping-modal.token.js';

interface NestedMappingEntry {
  schemaProperty: string;
  contentProperty: string;
  wrapInType: string;
  wrapInProperty: string;
  /** Schema property type for display */
  schemaPropertyType: string;
  /** Accepted types for wrap-in picker */
  acceptedTypes: string[];
  isComplexType: boolean;
  /** True when the row belongs in the Popular section of the mapping table. */
  isPopular: boolean;
}

type WizardStep = 'block-type' | 'mappings' | 'preview';

@customElement('schemeweaver-nested-mapping-modal')
export class NestedMappingModalElement extends UmbModalBaseElement<NestedMappingModalData, NestedMappingModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _currentStep: WizardStep = 'block-type';

  @state()
  private _blockElementTypes: BlockElementTypeInfo[] = [];

  @state()
  private _selectedBlockType: BlockElementTypeInfo | null = null;

  @state()
  private _schemaProperties: RankedSchemaPropertyInfo[] = [];

  @state()
  private _showAdditional = false;

  @state()
  private _nestedMappings: NestedMappingEntry[] = [];

  @state()
  private _previewJson = '';

  @state()
  private _autoMapping = false;

  @state()
  private _wrapOverrideRows = new Set<number>();

  /** Cache of schema type properties for wrap detection */
  private _typePropsCache: Record<string, SchemaPropertyInfo[]> = {};

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._initialise();
  }

  private async _initialise() {
    this._loading = true;
    try {
      const schemaTypeName = this.data?.nestedSchemaTypeName || '';
      const contentTypeAlias = this.data?.contentTypeAlias || '';
      const propertyAlias = this.data?.contentTypePropertyAlias || '';

      // Fetch schema type properties (ranked — popular-first) and block element types in parallel
      const [schemaProps, blockTypes] = await Promise.all([
        this.#repository.requestSchemaTypeProperties(schemaTypeName, true),
        propertyAlias
          ? this.#repository.requestBlockElementTypes(contentTypeAlias, propertyAlias)
          : Promise.resolve(undefined),
      ]);

      if (schemaProps) {
        this._schemaProperties = schemaProps;
      }

      if (blockTypes) {
        this._blockElementTypes = blockTypes;
      }

      // Parse existing config if present
      if (this.data?.existingConfig) {
        this._loadExistingConfig();
        this._ensureSingleTypeWrapping();
      }

      // If we have an existing config with a block alias, skip to mappings step
      if (this._selectedBlockType || this._nestedMappings.length > 0) {
        this._currentStep = 'mappings';
      }

      // If only one block type available, auto-select it
      if (!this._selectedBlockType && this._blockElementTypes.length === 1) {
        await this._selectBlockType(this._blockElementTypes[0]);
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

  private _loadExistingConfig() {
    try {
      const config = JSON.parse(this.data!.existingConfig!);
      if (config.nestedMappings && Array.isArray(config.nestedMappings)) {
        // Try to find the matching block type
        const blockAlias = config.nestedMappings[0]?.blockAlias;
        if (blockAlias) {
          const matchingBlock = this._blockElementTypes.find((bt) => bt.alias === blockAlias);
          if (matchingBlock) {
            this._selectedBlockType = matchingBlock;
          }
        }

        // Rebuild mappings from schema properties, preserving existing values.
        // User-configured rows always count as popular so toggling the disclosure
        // never hides an active mapping.
        this._nestedMappings = this._schemaProperties.map((prop) => {
          const existing = config.nestedMappings.find(
            (m: Record<string, string>) => m.schemaProperty === prop.name
          );
          const userConfigured = !!existing?.contentProperty;
          return {
            schemaProperty: prop.name,
            contentProperty: existing?.contentProperty || '',
            wrapInType: existing?.wrapInType || '',
            wrapInProperty: existing?.wrapInProperty || '',
            schemaPropertyType: prop.propertyType,
            acceptedTypes: prop.acceptedTypes,
            isComplexType: prop.isComplexType,
            isPopular: prop.isPopular || userConfigured,
          };
        });
      }
    } catch {
      // Silently ignore parse errors — fall back to default config
    }
  }

  /**
   * Ensure wrapInType is set for complex properties with only one accepted type.
   * Called after data loading to avoid state mutation during render.
   */
  private _ensureSingleTypeWrapping() {
    let changed = false;
    const updated = [...this._nestedMappings];
    for (let i = 0; i < updated.length; i++) {
      const m = updated[i];
      if (m.isComplexType && m.contentProperty && !m.wrapInType && m.acceptedTypes.length === 1) {
        updated[i] = { ...m, wrapInType: m.acceptedTypes[0] };
        changed = true;
      }
    }
    if (changed) {
      this._nestedMappings = updated;
    }
  }

  private async _selectBlockType(blockType: BlockElementTypeInfo) {
    this._selectedBlockType = blockType;

    // If no existing mappings, create from schema properties and auto-map
    if (this._nestedMappings.length === 0) {
      this._nestedMappings = this._schemaProperties.map((prop) => ({
        schemaProperty: prop.name,
        contentProperty: '',
        wrapInType: '',
        wrapInProperty: '',
        schemaPropertyType: prop.propertyType,
        acceptedTypes: prop.acceptedTypes,
        isComplexType: prop.isComplexType,
        isPopular: prop.isPopular,
      }));
      await this._autoMapMappings();
      this._ensureSingleTypeWrapping();
    }

    this._currentStep = 'mappings';
  }

  private async _handleContentPropertyChange(index: number, value: string) {
    const updated = [...this._nestedMappings];
    const mapping = updated[index];
    // Setting a content property promotes the row to popular so it stays
    // visible even if the user later collapses the disclosure.
    updated[index] = { ...mapping, contentProperty: value, isPopular: value ? true : mapping.isPopular };

    // Auto-detect wrapping for complex types when a property is selected
    if (mapping.isComplexType && value && mapping.acceptedTypes.length > 0) {
      const wrapResult = await this._detectWrapping(mapping.acceptedTypes, value);
      if (wrapResult) {
        updated[index] = { ...updated[index], wrapInType: wrapResult.wrapInType, wrapInProperty: wrapResult.wrapInProperty };
      }
    } else if (!value) {
      updated[index] = { ...updated[index], wrapInType: '', wrapInProperty: '' };
    }

    this._nestedMappings = updated;
  }

  private _handleWrapInTypeChange(index: number, value: string) {
    const updated = [...this._nestedMappings];
    updated[index] = { ...updated[index], wrapInType: value };
    this._nestedMappings = updated;
  }

  private _toggleWrapOverride(index: number) {
    const updated = new Set(this._wrapOverrideRows);
    if (updated.has(index)) updated.delete(index);
    else updated.add(index);
    this._wrapOverrideRows = updated;
  }

  // ── Auto-map algorithm ──────────────────────────────────────────────

  private async _handleAutoMap() {
    this._autoMapping = true;
    try {
      await this._autoMapMappings();
    } finally {
      this._autoMapping = false;
    }
  }

  /**
   * Auto-maps block properties to schema properties using a 3-tier algorithm:
   * 1. Exact name match (case-insensitive)
   * 2. Partial/contains match
   * 3. Complex type sub-property match (fetches accepted type's properties)
   * Only fills empty mappings — does not overwrite user selections.
   */
  private async _autoMapMappings(): Promise<void> {
    if (!this._selectedBlockType) return;
    const blockProps = this._selectedBlockType.properties;
    if (blockProps.length === 0) return;

    const updated = [...this._nestedMappings];
    const usedBlockProps = new Set(
      updated.filter((m) => m.contentProperty).map((m) => m.contentProperty),
    );

    for (let i = 0; i < updated.length; i++) {
      const mapping = updated[i];
      if (mapping.contentProperty) continue; // skip already mapped

      // Tier 1: exact match
      const exactMatch = blockProps.find(
        (bp) => !usedBlockProps.has(bp) && bp.toLowerCase() === mapping.schemaProperty.toLowerCase(),
      );
      if (exactMatch) {
        updated[i] = { ...mapping, contentProperty: exactMatch };
        usedBlockProps.add(exactMatch);
        // Auto-detect wrapping for complex types
        if (mapping.isComplexType && mapping.acceptedTypes.length > 0) {
          const wrapResult = await this._detectWrapping(mapping.acceptedTypes, exactMatch);
          if (wrapResult) {
            updated[i] = { ...updated[i], ...wrapResult };
          }
        }
        continue;
      }

      // Tier 2: partial match (one name contains the other)
      const partialMatch = blockProps.find(
        (bp) =>
          !usedBlockProps.has(bp) &&
          (bp.toLowerCase().includes(mapping.schemaProperty.toLowerCase()) ||
            mapping.schemaProperty.toLowerCase().includes(bp.toLowerCase())),
      );
      if (partialMatch) {
        updated[i] = { ...mapping, contentProperty: partialMatch };
        usedBlockProps.add(partialMatch);
        // Auto-detect wrapping for complex types
        if (mapping.isComplexType && mapping.acceptedTypes.length > 0) {
          const wrapResult = await this._detectWrapping(mapping.acceptedTypes, partialMatch);
          if (wrapResult) {
            updated[i] = { ...updated[i], ...wrapResult };
          }
        }
        continue;
      }

      // Tier 3: complex type sub-property match
      if (mapping.isComplexType && mapping.acceptedTypes.length > 0) {
        const complexResult = await this._findComplexTypeMatch(mapping, blockProps, usedBlockProps);
        if (complexResult) {
          updated[i] = { ...mapping, ...complexResult };
          usedBlockProps.add(complexResult.contentProperty);
        }
      }
    }

    // Anything that just got a content property is now an active mapping —
    // promote to popular so the disclosure can't hide it.
    for (let i = 0; i < updated.length; i++) {
      if (updated[i].contentProperty) {
        updated[i] = { ...updated[i], isPopular: true };
      }
    }

    this._nestedMappings = updated;
  }

  /**
   * For a complex schema property, check if any block property matches a sub-property
   * of an accepted type. E.g., block has "ratingValue" → schema "reviewRating" accepts "Rating"
   * → Rating has "ratingValue" → match with wrap.
   */
  private async _findComplexTypeMatch(
    mapping: NestedMappingEntry,
    blockProps: string[],
    usedBlockProps: Set<string>,
  ): Promise<Partial<NestedMappingEntry> | null> {
    for (const acceptedType of mapping.acceptedTypes) {
      const subProps = await this._getTypeProperties(acceptedType);

      for (const subProp of subProps) {
        // Exact match of block property to sub-property
        const match = blockProps.find(
          (bp) => !usedBlockProps.has(bp) && bp.toLowerCase() === subProp.name.toLowerCase(),
        );
        if (match) {
          return {
            contentProperty: match,
            wrapInType: acceptedType,
            wrapInProperty: subProp.name,
          };
        }

        // Partial match
        const partialMatch = blockProps.find(
          (bp) =>
            !usedBlockProps.has(bp) &&
            (bp.toLowerCase().includes(subProp.name.toLowerCase()) ||
              subProp.name.toLowerCase().includes(bp.toLowerCase())),
        );
        if (partialMatch) {
          return {
            contentProperty: partialMatch,
            wrapInType: acceptedType,
            wrapInProperty: subProp.name,
          };
        }
      }
    }
    return null;
  }

  /**
   * Auto-detect wrapping: given accepted types and a content property name,
   * find the best wrapper type and property.
   */
  private async _detectWrapping(
    acceptedTypes: string[],
    contentPropertyName: string,
  ): Promise<{ wrapInType: string; wrapInProperty: string } | null> {
    // Check all accepted types for the best match, not just the first
    for (const wrapType of acceptedTypes) {
      const subProps = await this._getTypeProperties(wrapType);
      if (subProps.length === 0) continue;

      // Exact match
      const exact = subProps.find(
        (sp) => sp.name.toLowerCase() === contentPropertyName.toLowerCase(),
      );
      if (exact) return { wrapInType: wrapType, wrapInProperty: exact.name };

      // Partial match
      const partial = subProps.find(
        (sp) =>
          sp.name.toLowerCase().includes(contentPropertyName.toLowerCase()) ||
          contentPropertyName.toLowerCase().includes(sp.name.toLowerCase()),
      );
      if (partial) return { wrapInType: wrapType, wrapInProperty: partial.name };
    }

    // Fallback: only auto-wrap if there is exactly one accepted type (unambiguous)
    if (acceptedTypes.length === 1) {
      const fallbackType = acceptedTypes[0];
      const fallbackProps = await this._getTypeProperties(fallbackType);
      if (fallbackProps.length > 0) {
        const textProp = fallbackProps.find((sp) => sp.name.toLowerCase() === 'text');
        return { wrapInType: fallbackType, wrapInProperty: textProp?.name || fallbackProps[0]?.name || 'Text' };
      }
    }

    return null;
  }

  /** Fetch and cache schema type properties */
  private async _getTypeProperties(typeName: string): Promise<SchemaPropertyInfo[]> {
    if (!this._typePropsCache[typeName]) {
      const props = await this.#repository.requestSchemaTypeProperties(typeName);
      this._typePropsCache[typeName] = props || [];
    }
    return this._typePropsCache[typeName];
  }

  private _goToStep(step: WizardStep) {
    if (step === 'preview') {
      this._generatePreview();
    }
    this._currentStep = step;
  }

  private _generatePreview() {
    const activeMappings = this._nestedMappings.filter((m) => m.contentProperty.trim() !== '');
    const config = {
      nestedMappings: activeMappings.map((m) => ({
        blockAlias: this._selectedBlockType?.alias || '',
        schemaProperty: m.schemaProperty,
        contentProperty: m.contentProperty,
        ...(m.wrapInType ? { wrapInType: m.wrapInType } : {}),
        ...(m.wrapInProperty ? { wrapInProperty: m.wrapInProperty } : {}),
      })),
    };
    this._previewJson = JSON.stringify(config, null, 2);
  }

  private _handleSave() {
    const activeMappings = this._nestedMappings.filter((m) => m.contentProperty.trim() !== '');
    const config = JSON.stringify({
      nestedMappings: activeMappings.map((m) => ({
        blockAlias: this._selectedBlockType?.alias || '',
        schemaProperty: m.schemaProperty,
        contentProperty: m.contentProperty,
        ...(m.wrapInType ? { wrapInType: m.wrapInType } : {}),
        ...(m.wrapInProperty ? { wrapInProperty: m.wrapInProperty } : {}),
      })),
    });

    this.modalContext?.setValue({ resolverConfig: config });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    const stepNumber = this._currentStep === 'block-type' ? 1 : this._currentStep === 'mappings' ? 2 : 3;

    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_nestedMappings')}: ${this.data?.nestedSchemaTypeName ?? ''}">
        ${this._loading
          ? html`
              <div class="loading">
                <uui-loader-circle></uui-loader-circle>
                <p>${this.localize.term('schemeWeaver_loadingProperties')}</p>
              </div>
            `
          : html`
              <div class="wizard-steps">
                <div class="step-indicator ${stepNumber >= 1 ? 'active' : ''} ${stepNumber > 1 ? 'completed' : ''}">
                  <span class="step-number">1</span>
                  <span class="step-label">${this.localize.term('schemeWeaver_blockElementType')}</span>
                </div>
                <div class="step-divider"></div>
                <div class="step-indicator ${stepNumber >= 2 ? 'active' : ''} ${stepNumber > 2 ? 'completed' : ''}">
                  <span class="step-number">2</span>
                  <span class="step-label">${this.localize.term('schemeWeaver_nestedMappings')}</span>
                </div>
                <div class="step-divider"></div>
                <div class="step-indicator ${stepNumber >= 3 ? 'active' : ''}">
                  <span class="step-number">3</span>
                  <span class="step-label">${this.localize.term('schemeWeaver_preview')}</span>
                </div>
              </div>

              ${this._currentStep === 'block-type' ? this._renderBlockTypePicker() : nothing}
              ${this._currentStep === 'mappings' ? this._renderMappings() : nothing}
              ${this._currentStep === 'preview' ? this._renderPreview() : nothing}
            `}

        <div slot="actions">
          ${this._currentStep !== 'block-type'
            ? html`
                <uui-button
                  look="secondary"
                  @click=${() => this._goToStep(this._currentStep === 'preview' ? 'mappings' : 'block-type')}
                  label=${this.localize.term('schemeWeaver_back')}
                >
                  ${this.localize.term('schemeWeaver_back')}
                </uui-button>
              `
            : html`
                <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
                  ${this.localize.term('schemeWeaver_cancel')}
                </uui-button>
              `}

          ${this._currentStep === 'block-type'
            ? html`
                <uui-button
                  look="primary"
                  ?disabled=${!this._selectedBlockType}
                  @click=${() => this._goToStep('mappings')}
                  label=${this.localize.term('schemeWeaver_next')}
                >
                  ${this.localize.term('schemeWeaver_next')}
                </uui-button>
              `
            : nothing}

          ${this._currentStep === 'mappings'
            ? html`
                <uui-button
                  look="primary"
                  @click=${() => this._goToStep('preview')}
                  label="${this.localize.term('schemeWeaver_preview')}"
                >
                  ${this.localize.term('schemeWeaver_preview')}
                </uui-button>
              `
            : nothing}

          ${this._currentStep === 'preview'
            ? html`
                <uui-button
                  look="primary"
                  @click=${this._handleSave}
                  label=${this.localize.term('schemeWeaver_save')}
                >
                  ${this.localize.term('schemeWeaver_save')}
                </uui-button>
              `
            : nothing}
        </div>
      </umb-body-layout>
    `;
  }

  private _renderBlockTypePicker() {
    if (this._blockElementTypes.length === 0) {
      return html`
        <uui-box headline=${this.localize.term('schemeWeaver_blockElementType')}>
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesHint')}</p>
          <p class="no-block-types-hint">${this.localize.term('schemeWeaver_noBlockTypesConfigureHint')}</p>
          <uui-input
            .value=${this._selectedBlockType?.alias || ''}
            @input=${(e: Event) => {
              const alias = (e.target as HTMLInputElement).value;
              this._selectedBlockType = { alias, name: alias, properties: [] };
            }}
            placeholder=${this.localize.term('schemeWeaver_blockElementType')}
            label=${this.localize.term('schemeWeaver_blockElementType')}
          ></uui-input>
        </uui-box>
      `;
    }

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_blockElementType')}>
        <p class="step-description">${this.localize.term('schemeWeaver_selectBlockTypeHint')} ${this.data?.nestedSchemaTypeName || ''}:</p>
        <div class="block-type-list">
          ${this._blockElementTypes.map(
            (bt) => html`
              <uui-button
                class="block-type-card ${this._selectedBlockType?.alias === bt.alias ? 'selected' : ''}"
                look="placeholder"
                label=${bt.name}
                @click=${() => {
                  this._selectedBlockType = bt;
                }}
              >
                <div class="block-type-header">
                  <strong>${bt.name}</strong>
                  <small class="block-alias">${bt.alias}</small>
                </div>
                <div class="block-type-props">
                  ${bt.properties.map((p) => html`<uui-tag look="secondary" class="prop-tag">${p}</uui-tag>`)}
                </div>
              </uui-button>
            `
          )}
        </div>
      </uui-box>
    `;
  }

  private _renderMappings() {
    // When the resolver config doesn't pin a specific block alias (the mapping
    // applies to every block type that exposes a matching property — e.g. the
    // home page contentGrid spans hero / feature / quote blocks), fall back to
    // listing the available block aliases so the source tag never renders empty.
    const sourceLabel = this._selectedBlockType?.name
      || this._selectedBlockType?.alias
      || (this._blockElementTypes.length > 0
        ? this._blockElementTypes.map((bt) => bt.name || bt.alias).join(', ')
        : this.localize.term('schemeWeaver_fromAnyBlock'));

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_nestedMappings')}>
        <div class="mapping-header-info">
          <uui-tag color="primary">${this.data?.nestedSchemaTypeName}</uui-tag>
          <span>${this.localize.term('schemeWeaver_from')}</span>
          <uui-tag color="default">${sourceLabel}</uui-tag>

          <uui-button
            class="auto-map-button"
            look="secondary"
            ?disabled=${this._autoMapping}
            @click=${this._handleAutoMap}
            label=${this.localize.term('schemeWeaver_autoMapNested')}
          >
            <uui-icon name="icon-wand"></uui-icon>
            ${this._autoMapping ? this.localize.term('schemeWeaver_loadingEllipsis') : this.localize.term('schemeWeaver_autoMapNested')}
          </uui-button>
        </div>

        ${this._renderMappingSections()}
      </uui-box>
    `;
  }

  private _renderMappingSections() {
    if (this._nestedMappings.length === 0) {
      return nothing;
    }

    // Partition while preserving the original index so mutation handlers keep working.
    const popular: Array<{ mapping: NestedMappingEntry; index: number }> = [];
    const other: Array<{ mapping: NestedMappingEntry; index: number }> = [];
    this._nestedMappings.forEach((mapping, index) => {
      (mapping.isPopular ? popular : other).push({ mapping, index });
    });

    return html`
      ${popular.length > 0
        ? html`
            <div class="section-header">
              <uui-icon name="icon-wand"></uui-icon>
              <span>${this.localize.term('schemeWeaver_popularProperties')}</span>
              <uui-tag look="secondary" size="s">${popular.length}</uui-tag>
            </div>
            ${this._renderMappingTable(popular)}
          `
        : nothing}

      ${other.length > 0
        ? html`
            <div class="disclosure-wrap">
              <uui-button
                look="secondary"
                class="disclosure-toggle"
                @click=${() => { this._showAdditional = !this._showAdditional; }}
                label=${this._showAdditional
                  ? this.localize.term('schemeWeaver_hideAdditionalProperties')
                  : this.localize.term('schemeWeaver_showMoreProperties', other.length)}
              >
                <uui-icon name=${this._showAdditional ? 'icon-navigation-up' : 'icon-navigation-down'}></uui-icon>
                ${this._showAdditional
                  ? this.localize.term('schemeWeaver_hideAdditionalProperties')
                  : this.localize.term('schemeWeaver_showMoreProperties', other.length)}
              </uui-button>
            </div>
            ${this._showAdditional ? this._renderMappingTable(other) : nothing}
          `
        : nothing}
    `;
  }

  private _renderMappingTable(rows: Array<{ mapping: NestedMappingEntry; index: number }>) {
    const blockProperties = this._selectedBlockType?.properties || [];

    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_nestedMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_wrapInType')}</uui-table-head-cell>
        </uui-table-head>

        ${rows.map(({ mapping, index }) => html`
          <uui-table-row>
            <uui-table-cell>
              <div>
                <strong>${mapping.schemaProperty}</strong>
                <small class="type-label">${mapping.schemaPropertyType}</small>
              </div>
            </uui-table-cell>
            <uui-table-cell>
              ${blockProperties.length > 0
                ? html`
                    <uui-select
                      label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaProperty)}
                      .options=${[
                        { name: this.localize.term('schemeWeaver_none'), value: '', selected: !mapping.contentProperty },
                        ...blockProperties.map((p) => ({
                          name: p,
                          value: p,
                          selected: mapping.contentProperty === p,
                        })),
                      ]}
                      @change=${(e: Event) => this._handleContentPropertyChange(index, (e.target as HTMLSelectElement).value)}
                    ></uui-select>
                  `
                : html`
                    <uui-input
                      .value=${mapping.contentProperty}
                      @input=${(e: Event) => this._handleContentPropertyChange(index, (e.target as HTMLInputElement).value)}
                      placeholder=${this.localize.term('schemeWeaver_blockPropertyPlaceholder')}
                      label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaProperty)}
                    ></uui-input>
                  `}
            </uui-table-cell>
            <uui-table-cell>
              ${this._renderWrapColumn(mapping, index)}
            </uui-table-cell>
          </uui-table-row>
        `)}
      </uui-table>
    `;
  }

  private _renderWrapColumn(mapping: NestedMappingEntry, index: number) {
    // Non-complex types don't need wrapping
    if (!mapping.isComplexType) {
      return html`<span class="type-label">--</span>`;
    }

    // If only one accepted type, wrapping is required — show as badge (no None option).
    // The wrapInType is set when the content property changes (_handleContentPropertyChange)
    // or when existing config is loaded (_ensureSingleTypeWrapping).
    if (mapping.acceptedTypes.length === 1) {
      const singleType = mapping.acceptedTypes[0];
      if (mapping.contentProperty) {
        return html`
          <uui-tag color="positive" look="secondary" class="wrap-tag">
            ${singleType}${mapping.wrapInProperty ? `.${mapping.wrapInProperty}` : ''}
          </uui-tag>
        `;
      }
      return html`<span class="type-label">--</span>`;
    }

    // Show override dropdown if user clicked edit (multiple accepted types — show None option)
    if (this._wrapOverrideRows.has(index)) {
      return html`
        <div class="wrap-override-row">
          ${mapping.acceptedTypes.length > 0
            ? html`
                <uui-select
                  label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', mapping.schemaProperty)}
                  .options=${[
                    { name: this.localize.term('schemeWeaver_none'), value: '', selected: !mapping.wrapInType },
                    ...mapping.acceptedTypes.map((t) => ({
                      name: t,
                      value: t,
                      selected: mapping.wrapInType === t,
                    })),
                  ]}
                  @change=${(e: Event) => this._handleWrapInTypeChange(index, (e.target as HTMLSelectElement).value)}
                ></uui-select>
              `
            : html`
                <uui-input
                  .value=${mapping.wrapInType}
                  @input=${(e: Event) => this._handleWrapInTypeChange(index, (e.target as HTMLInputElement).value)}
                  placeholder=${this.localize.term('schemeWeaver_wrapInType')}
                  label=${this.localize.term('schemeWeaver_wrapInTypeForProperty', mapping.schemaProperty)}
                ></uui-input>
              `}
          <uui-button compact look="secondary" label=${this.localize.term('schemeWeaver_done')} @click=${() => this._toggleWrapOverride(index)}>
            <uui-icon name="icon-check"></uui-icon>
          </uui-button>
        </div>
      `;
    }

    // Auto-detected wrap: show badge
    if (mapping.wrapInType && mapping.contentProperty) {
      return html`
        <div class="wrap-auto-detected">
          <uui-tag color="positive" look="secondary" class="wrap-tag">
            ${mapping.wrapInType}${mapping.wrapInProperty ? `.${mapping.wrapInProperty}` : ''}
          </uui-tag>
          <uui-button compact look="secondary" label=${this.localize.term('schemeWeaver_change')} @click=${() => this._toggleWrapOverride(index)}>
            <uui-icon name="icon-edit"></uui-icon>
          </uui-button>
        </div>
      `;
    }

    // No wrap set and no content property — show placeholder
    if (!mapping.contentProperty) {
      return html`<span class="type-label">--</span>`;
    }

    // Content property set but no wrap detected — show "None" with edit option
    return html`
      <div class="wrap-auto-detected">
        <span class="type-label">${this.localize.term('schemeWeaver_none')}</span>
        <uui-button compact look="secondary" label=${this.localize.term('schemeWeaver_set')} @click=${() => this._toggleWrapOverride(index)}>
          <uui-icon name="icon-edit"></uui-icon>
        </uui-button>
      </div>
    `;
  }

  private _renderPreview() {
    const activeMappings = this._nestedMappings.filter((m) => m.contentProperty.trim() !== '');

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_preview')}>
        <div class="preview-summary">
          <p><strong>${activeMappings.length}</strong> ${this.localize.term('schemeWeaver_propertyMappingsConfigured')} <strong>${this.data?.nestedSchemaTypeName}</strong></p>
          ${activeMappings.map(
            (m) => html`
              <div class="preview-mapping-row">
                <uui-icon name="icon-navigation-right"></uui-icon>
                <span>${m.schemaProperty}</span>
                <span class="preview-arrow">&larr;</span>
                <span>${m.contentProperty}</span>
                ${m.wrapInType
                  ? html`<uui-tag look="secondary" class="wrap-tag">${this.localize.term('schemeWeaver_wrapPrefix')}: ${m.wrapInType}</uui-tag>`
                  : nothing}
              </div>
            `
          )}
        </div>

        <details class="json-details">
          <summary>${this.localize.term('schemeWeaver_resolverConfigJson')}</summary>
          <pre class="json-preview">${this._previewJson}</pre>
        </details>
      </uui-box>
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

      .wizard-steps {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-4) 0;
        margin-bottom: var(--uui-size-space-4);
      }

      .step-indicator {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        opacity: 0.4;
      }

      .step-indicator.active {
        opacity: 1;
      }

      .step-indicator.completed {
        opacity: 0.7;
      }

      .step-number {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 50%;
        background: var(--uui-color-border);
        font-size: 0.8rem;
        font-weight: 600;
      }

      .step-indicator.active .step-number {
        background: var(--uui-color-interactive);
        color: var(--uui-color-surface);
      }

      .step-indicator.completed .step-number {
        background: var(--uui-color-positive);
        color: var(--uui-color-surface);
      }

      .step-label {
        font-size: 0.85rem;
        white-space: nowrap;
      }

      .step-divider {
        width: 30px;
        height: 2px;
        background: var(--uui-color-border);
      }

      .step-description {
        color: var(--uui-color-text-alt);
        margin: 0 0 var(--uui-size-space-3) 0;
      }

      .block-type-list {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .block-type-card {
        display: block;
        width: 100%;
        text-align: left;
        --uui-button-padding-left-factor: 3;
        --uui-button-padding-right-factor: 3;
        border: 2px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .block-type-card:hover {
        border-color: var(--uui-color-interactive);
      }

      .block-type-card.selected {
        border-color: var(--uui-color-interactive);
        background: var(--uui-color-surface-alt);
      }

      .block-type-header {
        display: flex;
        align-items: baseline;
        gap: var(--uui-size-space-2);
        margin-bottom: var(--uui-size-space-2);
      }

      .block-alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
      }

      .block-type-props {
        display: flex;
        flex-wrap: wrap;
        gap: var(--uui-size-space-1);
      }

      .prop-tag {
        font-size: 0.7rem;
        --uui-tag-min-height: 18px;
      }

      .no-block-types-hint {
        color: var(--uui-color-text-alt);
        margin: 0 0 var(--uui-size-space-3) 0;
      }

      .mapping-header-info {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }

      .auto-map-button {
        margin-left: auto;
      }

      .wrap-auto-detected {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .wrap-override-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
      }

      .type-label {
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-top: 2px;
      }

      uui-select {
        min-width: 130px;
      }

      .preview-summary {
        margin-bottom: var(--uui-size-space-4);
      }

      .preview-mapping-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) 0;
      }

      .preview-arrow {
        color: var(--uui-color-text-alt);
      }

      .wrap-tag {
        font-size: 0.7rem;
        --uui-tag-min-height: 18px;
      }

      .json-details {
        margin-top: var(--uui-size-space-3);
      }

      .json-details summary {
        cursor: pointer;
        color: var(--uui-color-text-alt);
        font-size: 0.85rem;
      }

      .json-preview {
        background: var(--uui-color-surface-alt);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        padding: var(--uui-size-space-3);
        font-family: monospace;
        font-size: 0.8rem;
        overflow-x: auto;
        white-space: pre;
      }

      .section-header {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        margin: var(--uui-size-space-4) 0 var(--uui-size-space-2);
        font-size: 0.9rem;
        font-weight: 600;
        color: var(--uui-color-text-alt);
      }

      .section-header uui-icon {
        color: var(--uui-color-interactive);
      }

      .disclosure-wrap {
        display: flex;
        justify-content: center;
        margin: var(--uui-size-space-4) 0 var(--uui-size-space-2);
      }

      .disclosure-toggle uui-icon {
        margin-right: var(--uui-size-space-1);
      }
    `,
  ];
}

export default NestedMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-nested-mapping-modal': NestedMappingModalElement;
  }
}
