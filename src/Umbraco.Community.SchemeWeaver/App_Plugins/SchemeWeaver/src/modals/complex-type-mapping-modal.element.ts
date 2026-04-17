import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL } from './source-origin-picker-modal.token.js';
import { SCHEMEWEAVER_CONTENT_TYPE_PICKER_MODAL } from './content-type-picker-modal.token.js';
import type { RankedSchemaPropertyInfo } from '../api/types.js';
import { SourceType, type SourceTypeValue } from '../constants/source-type.js';
import { SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL } from './complex-type-mapping-modal.token.js';
import type { ComplexTypeMappingModalData, ComplexTypeMappingModalValue } from './complex-type-mapping-modal.token.js';
import { filterOutPrimitiveSchemaTypes } from '../utils/schema-primitives.js';
import '../components/property-combobox.element.js';

interface ComplexSubMapping {
  schemaProperty: string;
  schemaPropertyType: string;
  sourceType: SourceTypeValue;
  contentTypePropertyAlias: string;
  staticValue: string;
  sourceContentTypeAlias: string;
  sourceContentTypeProperties: string[];
  acceptedTypes: string[];
  isComplexType: boolean;
  resolverConfig: string | null;
  isPopular: boolean;
}

type WizardStep = 'type-selection' | 'mappings' | 'preview';

@customElement('schemeweaver-complex-type-mapping-modal')
export class ComplexTypeMappingModalElement extends UmbModalBaseElement<ComplexTypeMappingModalData, ComplexTypeMappingModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;
  #modalManagerContext?: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _currentStep: WizardStep = 'type-selection';

  @state()
  private _selectedSubType = '';

  @state()
  private _subMappings: ComplexSubMapping[] = [];

  @state()
  private _previewJson = '';

  @state()
  private _autoMapping = false;

  @state()
  private _showAdditional = false;

  /** `data.acceptedTypes` minus Schema.org primitives (Text, URL, Number, etc.).
   *  Primitives have no sub-properties to map, so they are hidden from the type picker —
   *  users should express primitive values via the simple mapping on the main screen. */
  private get _complexAcceptedTypes(): string[] {
    return filterOutPrimitiveSchemaTypes(this.data?.acceptedTypes ?? []);
  }

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => { this.#notificationContext = ctx; });
    this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (ctx) => { this.#modalManagerContext = ctx; });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._initialise();
  }

  private async _initialise() {
    this._loading = true;
    try {
      // If there's an existing config, parse it
      if (this.data?.existingConfig) {
        this._loadExistingConfig();
      }

      const complexTypes = this._complexAcceptedTypes;

      // If a sub-type was already selected, or there's only one (non-primitive) option
      if (this.data?.selectedSubType) {
        this._selectedSubType = this.data.selectedSubType;
      } else if (complexTypes.length === 1) {
        this._selectedSubType = complexTypes[0];
      }

      // If we have a selected type and no existing mappings, load properties
      if (this._selectedSubType && this._subMappings.length === 0) {
        await this._loadSubTypeProperties(this._selectedSubType);
      }

      // Skip to mappings if we already have data
      if (this._selectedSubType && this._subMappings.length > 0) {
        this._currentStep = 'mappings';
      } else if (this._selectedSubType) {
        this._currentStep = 'mappings';
      }

      // Auto-select and skip if only 1 (non-primitive) type
      if (complexTypes.length === 1 && this._currentStep === 'type-selection') {
        this._selectedSubType = complexTypes[0];
        await this._loadSubTypeProperties(this._selectedSubType);
        this._currentStep = 'mappings';
      }
    } catch (error) {
      this.#notificationContext?.peek('danger', {
        data: { message: error instanceof Error ? error.message : this.localize.term('schemeWeaver_failedToLoadMappingData') },
      });
    } finally {
      this._loading = false;
    }
  }

  private _loadExistingConfig() {
    try {
      const config = JSON.parse(this.data!.existingConfig!);
      if (config.complexTypeMappings && Array.isArray(config.complexTypeMappings)) {
        this._selectedSubType = config.selectedSubType || this.data?.selectedSubType || '';
        this._subMappings = config.complexTypeMappings.map((m: Record<string, unknown>) => ({
          schemaProperty: (m.schemaProperty as string) || '',
          schemaPropertyType: '',
          sourceType: (m.sourceType as SourceTypeValue) || SourceType.Property,
          contentTypePropertyAlias: (m.contentTypePropertyAlias as string) || '',
          staticValue: (m.staticValue as string) || '',
          sourceContentTypeAlias: (m.sourceContentTypeAlias as string) || '',
          sourceContentTypeProperties: [],
          acceptedTypes: [],
          isComplexType: false,
          resolverConfig: m.resolverConfig
            ? (typeof m.resolverConfig === 'string'
              ? m.resolverConfig as string
              : JSON.stringify(m.resolverConfig))
            : null,
          isPopular: true,
        }));
      }
    } catch {
      // Silently ignore parse errors — fall back to default config
    }
  }

  private async _loadSubTypeProperties(typeName: string) {
    const props = await this.#repository.requestSchemaTypeProperties(typeName, true);
    if (!props) return;

    // Merge with existing mappings, preserving user data
    const existingMap = new Map(this._subMappings.map(m => [m.schemaProperty.toLowerCase(), m]));

    this._subMappings = props.map((prop: RankedSchemaPropertyInfo) => {
      const existing = existingMap.get(prop.name.toLowerCase());
      // Properties the user has already configured stay in the popular section
      // so toggling the disclosure never hides an active mapping.
      const userConfigured = !!existing && (
        !!existing.contentTypePropertyAlias ||
        !!existing.staticValue ||
        !!existing.resolverConfig
      );
      if (existing) {
        return {
          ...existing,
          schemaPropertyType: prop.propertyType,
          acceptedTypes: prop.acceptedTypes || [],
          isComplexType: prop.isComplexType || false,
          isPopular: prop.isPopular || userConfigured,
        };
      }
      return {
        schemaProperty: prop.name,
        schemaPropertyType: prop.propertyType,
        sourceType: SourceType.Property,
        contentTypePropertyAlias: '',
        staticValue: '',
        sourceContentTypeAlias: '',
        sourceContentTypeProperties: [],
        acceptedTypes: prop.acceptedTypes || [],
        isComplexType: prop.isComplexType || false,
        resolverConfig: null,
        isPopular: prop.isPopular,
      };
    });

    // Load properties for any existing source content types
    const sourceAliases = [...new Set(
      this._subMappings
        .filter(m => m.sourceContentTypeAlias && [SourceType.Parent, SourceType.Ancestor, SourceType.Sibling].includes(m.sourceType))
        .map(m => m.sourceContentTypeAlias)
    )];
    for (const alias of sourceAliases) {
      const sourceProps = await this.#repository.requestContentTypeProperties(alias);
      if (sourceProps) {
        const propAliases = sourceProps.map(p => p.alias);
        this._subMappings = this._subMappings.map(m =>
          m.sourceContentTypeAlias === alias ? { ...m, sourceContentTypeProperties: propAliases } : m
        );
      }
    }
  }

  private async _handleSelectType(typeName: string) {
    this._selectedSubType = typeName;
    this._loading = true;
    try {
      await this._loadSubTypeProperties(typeName);
      await this._autoMapMappings();
    } finally {
      this._loading = false;
    }
    this._currentStep = 'mappings';
  }

  // ── Source type helpers ──────────────────────────────────────────────

  private _getSourceIcon(sourceType: string): string {
    switch (sourceType) {
      case SourceType.Property: return 'icon-document';
      case SourceType.Static: return 'icon-edit';
      case SourceType.Parent: return 'icon-arrow-up';
      case SourceType.Ancestor: return 'icon-hierarchy';
      case SourceType.Sibling: return 'icon-split-alt';
      case SourceType.ComplexType: return 'icon-brackets';
      default: return 'icon-document';
    }
  }

  private _getSourceLabelKey(sourceType: string): string {
    switch (sourceType) {
      case SourceType.Property: return 'schemeWeaver_sourceCurrentNode';
      case SourceType.Static: return 'schemeWeaver_sourceStaticValue';
      case SourceType.Parent: return 'schemeWeaver_sourceParentNode';
      case SourceType.Ancestor: return 'schemeWeaver_sourceAncestorNode';
      case SourceType.Sibling: return 'schemeWeaver_sourceSiblingNode';
      case SourceType.ComplexType: return 'schemeWeaver_sourceComplexType';
      default: return 'schemeWeaver_sourceCurrentNode';
    }
  }

  private _needsSourceContentType(sourceType: string): boolean {
    return sourceType === SourceType.Parent || sourceType === SourceType.Ancestor || sourceType === SourceType.Sibling;
  }

  // ── Mapping handlers ──────────────────────────────────────────────

  private async _handlePickSourceOrigin(index: number) {
    if (!this.#modalManagerContext) return;
    const mapping = this._subMappings[index];

    const result = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_SOURCE_ORIGIN_PICKER_MODAL, {
        data: {
          editorAlias: '',
          isComplexType: mapping.isComplexType,
          currentSourceType: mapping.sourceType,
          restrictToSimpleSources: true,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.sourceType) return;

    const updated = [...this._subMappings];
    updated[index] = {
      ...updated[index],
      sourceType: result.sourceType,
      contentTypePropertyAlias: '',
      staticValue: '',
      sourceContentTypeAlias: '',
      sourceContentTypeProperties: [],
    };
    this._subMappings = updated;
  }

  private async _handlePickSourceContentType(index: number) {
    if (!this.#modalManagerContext) return;

    const result = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_CONTENT_TYPE_PICKER_MODAL, {
        data: { currentAlias: this._subMappings[index].sourceContentTypeAlias },
      })
      .onSubmit()
      .catch(() => null);

    if (!result?.contentTypeAlias) return;

    const props = await this.#repository.requestContentTypeProperties(result.contentTypeAlias);
    const propertyAliases = props?.map(p => p.alias) || [];

    const updated = [...this._subMappings];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: result.contentTypeAlias,
      sourceContentTypeProperties: propertyAliases,
      contentTypePropertyAlias: '',
    };
    this._subMappings = updated;
  }

  private _handleRemoveSourceContentType(index: number) {
    const updated = [...this._subMappings];
    updated[index] = {
      ...updated[index],
      sourceContentTypeAlias: '',
      sourceContentTypeProperties: [],
      contentTypePropertyAlias: '',
    };
    this._subMappings = updated;
  }

  private _handlePropertyChange(index: number, value: string) {
    const updated = [...this._subMappings];
    updated[index] = { ...updated[index], contentTypePropertyAlias: value };
    this._subMappings = updated;
  }

  private _handleStaticValueChange(index: number, value: string) {
    const updated = [...this._subMappings];
    updated[index] = { ...updated[index], staticValue: value };
    this._subMappings = updated;
  }

  private async _handleConfigureSubComplexType(index: number) {
    if (!this.#modalManagerContext) return;
    const mapping = this._subMappings[index];
    if (!mapping.isComplexType || mapping.acceptedTypes.length === 0) return;

    let existingSelectedSubType = '';
    if (mapping.resolverConfig) {
      try {
        const parsed = JSON.parse(mapping.resolverConfig);
        existingSelectedSubType = parsed.selectedSubType || '';
      } catch { /* ignore */ }
    }

    const modalHandler = this.#modalManagerContext.open(
      this,
      SCHEMEWEAVER_COMPLEX_TYPE_MAPPING_MODAL,
      {
        data: {
          schemaPropertyName: mapping.schemaProperty,
          acceptedTypes: mapping.acceptedTypes,
          selectedSubType: existingSelectedSubType,
          contentTypeAlias: this.data?.contentTypeAlias || '',
          availableProperties: this.data?.availableProperties || [],
          existingConfig: mapping.resolverConfig,
          parentPath: (this.data?.parentPath ? this.data.parentPath + ' > ' : '') + (this.data?.schemaPropertyName || ''),
        },
      }
    );

    try {
      const result = await modalHandler.onSubmit();
      if (result?.resolverConfig) {
        const updated = [...this._subMappings];
        updated[index] = {
          ...updated[index],
          resolverConfig: result.resolverConfig,
          sourceType: SourceType.ComplexType,
          contentTypePropertyAlias: '',
          staticValue: '',
        };
        this._subMappings = updated;
      }
    } catch {
      // Modal rejected/closed
    }
  }

  // ── Auto-map ──────────────────────────────────────────────────────

  private async _handleAutoMap() {
    this._autoMapping = true;
    try {
      await this._autoMapMappings();
    } finally {
      this._autoMapping = false;
    }
  }

  private async _autoMapMappings() {
    const availableProps = this.data?.availableProperties || [];
    if (availableProps.length === 0) return;

    const updated = [...this._subMappings];
    const usedProps = new Set(updated.filter(m => m.contentTypePropertyAlias).map(m => m.contentTypePropertyAlias));

    for (let i = 0; i < updated.length; i++) {
      if (updated[i].contentTypePropertyAlias) continue;

      // Exact match
      const exact = availableProps.find(
        p => !usedProps.has(p) && p.toLowerCase() === updated[i].schemaProperty.toLowerCase()
      );
      if (exact) {
        updated[i] = { ...updated[i], contentTypePropertyAlias: exact };
        usedProps.add(exact);
        continue;
      }

      // Partial match
      const partial = availableProps.find(
        p => !usedProps.has(p) &&
          (p.toLowerCase().includes(updated[i].schemaProperty.toLowerCase()) ||
           updated[i].schemaProperty.toLowerCase().includes(p.toLowerCase()))
      );
      if (partial) {
        updated[i] = { ...updated[i], contentTypePropertyAlias: partial };
        usedProps.add(partial);
      }
    }

    this._subMappings = updated;
  }

  // ── Preview & Save ──────────────────────────────────────────────

  private _goToStep(step: WizardStep) {
    if (step === 'preview') {
      this._generatePreview();
    }
    this._currentStep = step;
  }

  private _buildConfig() {
    const active = this._subMappings.filter(m => m.contentTypePropertyAlias || m.staticValue || m.resolverConfig);
    return {
      selectedSubType: this._selectedSubType,
      complexTypeMappings: active.map(m => ({
        schemaProperty: m.schemaProperty,
        sourceType: m.sourceType,
        ...(m.contentTypePropertyAlias ? { contentTypePropertyAlias: m.contentTypePropertyAlias } : {}),
        ...(m.staticValue ? { staticValue: m.staticValue } : {}),
        ...(m.sourceContentTypeAlias ? { sourceContentTypeAlias: m.sourceContentTypeAlias } : {}),
        ...(m.resolverConfig ? { resolverConfig: m.resolverConfig } : {}),
      })),
    };
  }

  private _generatePreview() {
    this._previewJson = JSON.stringify(this._buildConfig(), null, 2);
  }

  private _handleSave() {
    const config = JSON.stringify(this._buildConfig());
    this.modalContext?.setValue({ resolverConfig: config, selectedSubType: this._selectedSubType });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  // ── Render ──────────────────────────────────────────────────────

  render() {
    const stepNumber = this._currentStep === 'type-selection' ? 1 : this._currentStep === 'mappings' ? 2 : 3;

    return html`
      <umb-body-layout headline="${this.localize.term('schemeWeaver_configureComplexType')} — ${this.data?.parentPath ? this.data.parentPath + ' > ' : ''}${this.data?.schemaPropertyName || ''}">
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
                  <span class="step-label">${this.localize.term('schemeWeaver_type')}</span>
                </div>
                <div class="step-divider"></div>
                <div class="step-indicator ${stepNumber >= 2 ? 'active' : ''} ${stepNumber > 2 ? 'completed' : ''}">
                  <span class="step-number">2</span>
                  <span class="step-label">${this.localize.term('schemeWeaver_complexTypeMappings')}</span>
                </div>
                <div class="step-divider"></div>
                <div class="step-indicator ${stepNumber >= 3 ? 'active' : ''}">
                  <span class="step-number">3</span>
                  <span class="step-label">${this.localize.term('schemeWeaver_preview')}</span>
                </div>
              </div>

              ${this._currentStep === 'type-selection' ? this._renderTypePicker() : nothing}
              ${this._currentStep === 'mappings' ? this._renderMappings() : nothing}
              ${this._currentStep === 'preview' ? this._renderPreview() : nothing}
            `}

        <div slot="actions">
          ${this._currentStep !== 'type-selection'
            ? html`
                <uui-button
                  look="secondary"
                  @click=${() => {
                    if (this._currentStep === 'preview') {
                      this._goToStep('mappings');
                    } else if (this._complexAcceptedTypes.length === 1) {
                      this._handleClose();
                    } else {
                      this._goToStep('type-selection');
                    }
                  }}
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

          ${this._currentStep === 'mappings'
            ? html`
                <uui-button look="primary" @click=${() => this._goToStep('preview')} label=${this.localize.term('schemeWeaver_preview')}>
                  ${this.localize.term('schemeWeaver_preview')}
                </uui-button>
              `
            : nothing}

          ${this._currentStep === 'preview'
            ? html`
                <uui-button look="primary" @click=${this._handleSave} label=${this.localize.term('schemeWeaver_save')}>
                  ${this.localize.term('schemeWeaver_save')}
                </uui-button>
              `
            : nothing}
        </div>
      </umb-body-layout>
    `;
  }

  private _renderTypePicker() {
    const types = this._complexAcceptedTypes;

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_selectSchemaSubType')}>
        <uui-ref-list>
          ${repeat(
            types,
            (t) => t,
            (t) => html`
              <umb-ref-item
                name=${t}
                detail=${this.localize.term('schemeWeaver_schemaType')}
                icon="icon-brackets"
                @open=${() => this._handleSelectType(t)}
              ></umb-ref-item>
            `,
          )}
        </uui-ref-list>
      </uui-box>
    `;
  }

  private _renderMappings() {
    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_complexTypeMappings')}>
        <div class="mapping-header-info">
          <uui-tag color="primary">${this._selectedSubType}</uui-tag>
          <span>${this.localize.term('schemeWeaver_mappedTo')}</span>
          <uui-tag color="default">${this.data?.contentTypeAlias}</uui-tag>

          <uui-button
            class="auto-map-button"
            look="secondary"
            ?disabled=${this._autoMapping}
            @click=${this._handleAutoMap}
            label=${this.localize.term('schemeWeaver_autoMap')}
          >
            <uui-icon name="icon-wand"></uui-icon>
            ${this._autoMapping ? this.localize.term('schemeWeaver_loadingEllipsis') : this.localize.term('schemeWeaver_autoMap')}
          </uui-button>
        </div>

        ${this._renderMappingSections()}
      </uui-box>
    `;
  }

  private _renderMappingSections() {
    if (this._subMappings.length === 0) {
      return html`<p class="primitive-type-hint">${this.localize.term('schemeWeaver_noPropertiesToMap')}</p>`;
    }

    // Partition while preserving the original index so mutation handlers keep working.
    const popular: Array<{ mapping: ComplexSubMapping; index: number }> = [];
    const other: Array<{ mapping: ComplexSubMapping; index: number }> = [];
    this._subMappings.forEach((mapping, index) => {
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

  private _renderMappingTable(rows: Array<{ mapping: ComplexSubMapping; index: number }>) {
    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_complexTypeMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
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
            </uui-table-cell>
            <uui-table-cell>
              ${this._renderSubMappingValue(mapping, index)}
            </uui-table-cell>
          </uui-table-row>
        `)}
      </uui-table>
    `;
  }

  private _renderSubMappingValue(mapping: ComplexSubMapping, index: number) {
    if (mapping.isComplexType && mapping.acceptedTypes.length > 0) {
      return html`
        <div class="block-actions">
          <uui-button
            look="secondary"
            compact
            label=${this.localize.term('schemeWeaver_configureComplexType')}
            @click=${() => this._handleConfigureSubComplexType(index)}
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

    if (mapping.sourceType === SourceType.Static) {
      return html`
        <uui-input
          .value=${mapping.staticValue}
          @input=${(e: Event) => this._handleStaticValueChange(index, (e.target as HTMLInputElement).value)}
          placeholder=${this.localize.term('schemeWeaver_enterStaticValue')}
          label=${this.localize.term('schemeWeaver_staticValueForProperty', mapping.schemaProperty)}
        ></uui-input>
      `;
    }

    if (this._needsSourceContentType(mapping.sourceType)) {
      return html`
        <div class="value-inputs">
          ${mapping.sourceContentTypeAlias
            ? html`
                <uui-ref-node
                  standalone
                  name=${mapping.sourceContentTypeAlias}
                  detail=${this.localize.term(this._getSourceLabelKey(mapping.sourceType))}
                >
                  <umb-icon slot="icon" name=${this._getSourceIcon(mapping.sourceType)}></umb-icon>
                  <uui-action-bar slot="actions">
                    <uui-button
                      label=${this.localize.term('general_edit')}
                      @click=${() => this._handlePickSourceContentType(index)}
                    ></uui-button>
                    <uui-button
                      label=${this.localize.term('general_remove')}
                      @click=${() => this._handleRemoveSourceContentType(index)}
                    ></uui-button>
                  </uui-action-bar>
                </uui-ref-node>
                <schemeweaver-property-combobox
                  .properties=${mapping.sourceContentTypeProperties}
                  .value=${mapping.contentTypePropertyAlias}
                  label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaProperty)}
                  placeholder=${this.localize.term('schemeWeaver_selectProperty')}
                  @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
                ></schemeweaver-property-combobox>
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

    // Default: property from current node
    return html`
      <schemeweaver-property-combobox
        .properties=${this.data?.availableProperties || []}
        .value=${mapping.contentTypePropertyAlias}
        label=${this.localize.term('schemeWeaver_valueForProperty', mapping.schemaProperty)}
        placeholder=${this.localize.term('schemeWeaver_selectProperty')}
        @change=${(e: CustomEvent) => this._handlePropertyChange(index, e.detail.value)}
      ></schemeweaver-property-combobox>
    `;
  }

  private _renderPreview() {
    const active = this._subMappings.filter(m => m.contentTypePropertyAlias || m.staticValue || m.resolverConfig);

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_preview')}>
        <div class="preview-summary">
          <p><strong>${active.length}</strong> ${this.localize.term('schemeWeaver_propertyMappingsConfigured')} <strong>${this._selectedSubType}</strong></p>
          ${active.map(m => html`
            <div class="preview-mapping-row">
              <uui-icon name="icon-navigation-right"></uui-icon>
              <span>${m.schemaProperty}</span>
              <span class="preview-arrow">&larr;</span>
              <uui-tag look="secondary" class="source-tag">${this.localize.term(this._getSourceLabelKey(m.sourceType))}</uui-tag>
              <span>${m.sourceType === SourceType.Static ? m.staticValue : m.resolverConfig ? this.localize.term('schemeWeaver_complexTypeConfigured') : m.contentTypePropertyAlias}</span>
              ${m.sourceContentTypeAlias
                ? html`<small class="type-label">(${m.sourceContentTypeAlias})</small>`
                : nothing}
            </div>
          `)}
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
      :host { display: block; }

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

      .step-indicator.active { opacity: 1; }
      .step-indicator.completed { opacity: 0.7; }

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

      .step-label { font-size: 0.85rem; white-space: nowrap; }
      .step-divider { width: 30px; height: 2px; background: var(--uui-color-border); }

      .mapping-header-info {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin-bottom: var(--uui-size-space-4);
      }

      .auto-map-button { margin-left: auto; }

      .type-label {
        display: block;
        color: var(--uui-color-text-alt);
        font-family: monospace;
        font-size: 0.8rem;
        margin-top: 2px;
      }

      .source-chip {
        white-space: nowrap;
        font-size: 0.85rem;
      }

      .source-chip uui-icon {
        margin-right: var(--uui-size-space-1);
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

      .value-inputs {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .preview-summary { margin-bottom: var(--uui-size-space-4); }

      .preview-mapping-row {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) 0;
      }

      .preview-arrow { color: var(--uui-color-text-alt); }
      .source-tag { font-size: 0.7rem; --uui-tag-min-height: 18px; }

      .json-details { margin-top: var(--uui-size-space-3); }
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

      .primitive-type-hint {
        color: var(--uui-color-text-alt);
        font-style: italic;
        text-align: center;
        padding: var(--uui-size-space-6);
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

export default ComplexTypeMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-complex-type-mapping-modal': ComplexTypeMappingModalElement;
  }
}
