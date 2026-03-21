import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaPropertyInfo, BlockElementTypeInfo } from '../api/types.js';

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
  private _schemaProperties: SchemaPropertyInfo[] = [];

  @state()
  private _nestedMappings: NestedMappingEntry[] = [];

  @state()
  private _previewJson = '';

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

      // Fetch schema type properties and block element types in parallel
      const [schemaProps, blockTypes] = await Promise.all([
        this.#repository.requestSchemaTypeProperties(schemaTypeName),
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
      }

      // If we have an existing config with a block alias, skip to mappings step
      if (this._selectedBlockType || this._nestedMappings.length > 0) {
        this._currentStep = 'mappings';
      }

      // If only one block type available, auto-select it
      if (!this._selectedBlockType && this._blockElementTypes.length === 1) {
        this._selectBlockType(this._blockElementTypes[0]);
      }
    } catch (error) {
      console.error('SchemeWeaver: Error loading nested mapping data:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to load nested mapping data',
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

        // Rebuild mappings from schema properties, preserving existing values
        this._nestedMappings = this._schemaProperties.map((prop) => {
          const existing = config.nestedMappings.find(
            (m: any) => m.schemaProperty === prop.name
          );
          return {
            schemaProperty: prop.name,
            contentProperty: existing?.contentProperty || '',
            wrapInType: existing?.wrapInType || '',
            wrapInProperty: existing?.wrapInProperty || '',
            schemaPropertyType: prop.propertyType,
            acceptedTypes: prop.acceptedTypes,
            isComplexType: prop.isComplexType,
          };
        });
      }
    } catch {
      console.warn('SchemeWeaver: Could not parse existing nested mapping config');
    }
  }

  private _selectBlockType(blockType: BlockElementTypeInfo) {
    this._selectedBlockType = blockType;

    // If no existing mappings, create from schema properties
    if (this._nestedMappings.length === 0) {
      this._nestedMappings = this._schemaProperties.map((prop) => ({
        schemaProperty: prop.name,
        contentProperty: '',
        wrapInType: '',
        wrapInProperty: '',
        schemaPropertyType: prop.propertyType,
        acceptedTypes: prop.acceptedTypes,
        isComplexType: prop.isComplexType,
      }));
    }

    this._currentStep = 'mappings';
  }

  private _handleContentPropertyChange(index: number, value: string) {
    const updated = [...this._nestedMappings];
    updated[index] = { ...updated[index], contentProperty: value };
    this._nestedMappings = updated;
  }

  private _handleWrapInTypeChange(index: number, value: string) {
    const updated = [...this._nestedMappings];
    updated[index] = { ...updated[index], wrapInType: value };
    this._nestedMappings = updated;
  }

  private _handleWrapInPropertyChange(index: number, value: string) {
    const updated = [...this._nestedMappings];
    updated[index] = { ...updated[index], wrapInProperty: value };
    this._nestedMappings = updated;
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
      <umb-body-layout headline="${this.localize.term('schemeWeaver_nestedMappings')} - ${this.data?.nestedSchemaTypeName || ''}">
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
                  label="Next"
                >
                  Next
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
          <p class="no-block-types-hint">No block element types found for this property. Enter an alias manually:</p>
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
        <p class="step-description">Select the block element type that contains the content for each ${this.data?.nestedSchemaTypeName || ''} item:</p>
        <div class="block-type-list">
          ${this._blockElementTypes.map(
            (bt) => html`
              <button
                class="block-type-card ${this._selectedBlockType?.alias === bt.alias ? 'selected' : ''}"
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
              </button>
            `
          )}
        </div>
      </uui-box>
    `;
  }

  private _renderMappings() {
    const blockProperties = this._selectedBlockType?.properties || [];

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_nestedMappings')}>
        <div class="mapping-header-info">
          <uui-tag color="primary">${this.data?.nestedSchemaTypeName}</uui-tag>
          <span>from</span>
          <uui-tag color="default">${this._selectedBlockType?.name || this._selectedBlockType?.alias}</uui-tag>
        </div>

        <uui-table aria-label=${this.localize.term('schemeWeaver_nestedMappings')}>
          <uui-table-head>
            <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
            <uui-table-head-cell>${this.localize.term('schemeWeaver_wrapInType')}</uui-table-head-cell>
          </uui-table-head>

          ${this._nestedMappings.map((mapping, index) => html`
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
                        label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaProperty}
                        .options=${[
                          { name: '-- None --', value: '', selected: !mapping.contentProperty },
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
                        placeholder="Block property alias..."
                        label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaProperty}
                      ></uui-input>
                    `}
              </uui-table-cell>
              <uui-table-cell>
                ${mapping.isComplexType && mapping.acceptedTypes.length > 0
                  ? html`
                      <uui-select
                        label=${this.localize.term('schemeWeaver_wrapInType') + ' ' + mapping.schemaProperty}
                        .options=${[
                          { name: '-- None --', value: '', selected: !mapping.wrapInType },
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
                        label=${this.localize.term('schemeWeaver_wrapInType') + ' ' + mapping.schemaProperty}
                      ></uui-input>
                    `}
              </uui-table-cell>
            </uui-table-row>
          `)}
        </uui-table>
      </uui-box>
    `;
  }

  private _renderPreview() {
    const activeMappings = this._nestedMappings.filter((m) => m.contentProperty.trim() !== '');

    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_preview')}>
        <div class="preview-summary">
          <p><strong>${activeMappings.length}</strong> property mappings configured for <strong>${this.data?.nestedSchemaTypeName}</strong></p>
          ${activeMappings.map(
            (m) => html`
              <div class="preview-mapping-row">
                <uui-icon name="icon-navigation-right"></uui-icon>
                <span>${m.schemaProperty}</span>
                <span class="preview-arrow">&larr;</span>
                <span>${m.contentProperty}</span>
                ${m.wrapInType
                  ? html`<uui-tag look="secondary" class="wrap-tag">wrap: ${m.wrapInType}</uui-tag>`
                  : nothing}
              </div>
            `
          )}
        </div>

        <details class="json-details">
          <summary>Resolver Config JSON</summary>
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
        padding: var(--uui-size-space-3);
        border: 2px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        cursor: pointer;
        transition: border-color 0.15s;
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
    `,
  ];
}

export default NestedMappingModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-nested-mapping-modal': NestedMappingModalElement;
  }
}
