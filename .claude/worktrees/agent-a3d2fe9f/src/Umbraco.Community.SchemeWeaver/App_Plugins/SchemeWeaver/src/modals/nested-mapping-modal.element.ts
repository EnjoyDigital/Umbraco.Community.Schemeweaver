import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaPropertyInfo } from '../api/types.js';

import type { NestedMappingModalData, NestedMappingModalValue } from './nested-mapping-modal.token.js';

interface NestedMappingEntry {
  blockAlias: string;
  schemaProperty: string;
  contentProperty: string;
  wrapInType: string;
}

@customElement('schemeweaver-nested-mapping-modal')
export class NestedMappingModalElement extends UmbModalBaseElement<NestedMappingModalData, NestedMappingModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _schemaProperties: SchemaPropertyInfo[] = [];

  @state()
  private _nestedMappings: NestedMappingEntry[] = [];

  @state()
  private _blockAlias = '';

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

      // Fetch schema type properties
      const props = await this.#repository.requestSchemaTypeProperties(schemaTypeName);
      if (props) {
        this._schemaProperties = props;
      }

      // Parse existing config if present
      if (this.data?.existingConfig) {
        try {
          const config = JSON.parse(this.data.existingConfig);
          if (config.nestedMappings && Array.isArray(config.nestedMappings)) {
            this._nestedMappings = config.nestedMappings;
            if (this._nestedMappings.length > 0) {
              this._blockAlias = this._nestedMappings[0].blockAlias || '';
            }
          }
        } catch {
          console.warn('SchemeWeaver: Could not parse existing nested mapping config');
        }
      }

      // If no existing mappings, create empty ones from schema properties
      if (this._nestedMappings.length === 0 && this._schemaProperties.length > 0) {
        this._nestedMappings = this._schemaProperties.map((prop) => ({
          blockAlias: '',
          schemaProperty: prop.name,
          contentProperty: '',
          wrapInType: '',
        }));
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

  private _handleBlockAliasChange(value: string) {
    this._blockAlias = value;
    this._nestedMappings = this._nestedMappings.map((m) => ({
      ...m,
      blockAlias: value,
    }));
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

  private _handleSave() {
    // Only include mappings that have a content property set
    const activeMappings = this._nestedMappings.filter((m) => m.contentProperty.trim() !== '');

    const config = JSON.stringify({
      nestedMappings: activeMappings.map((m) => ({
        blockAlias: m.blockAlias || this._blockAlias,
        schemaProperty: m.schemaProperty,
        contentProperty: m.contentProperty,
        ...(m.wrapInType ? { wrapInType: m.wrapInType } : {}),
      })),
    });

    this.modalContext?.setValue({ resolverConfig: config });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
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
              <uui-box headline=${this.localize.term('schemeWeaver_blockElementType')}>
                <uui-input
                  .value=${this._blockAlias}
                  @input=${(e: Event) => this._handleBlockAliasChange((e.target as HTMLInputElement).value)}
                  placeholder=${this.localize.term('schemeWeaver_blockElementType')}
                  label=${this.localize.term('schemeWeaver_blockElementType')}
                ></uui-input>
              </uui-box>

              <uui-box headline=${this.localize.term('schemeWeaver_nestedMappings')}>
                <uui-table aria-label=${this.localize.term('schemeWeaver_nestedMappings')}>
                  <uui-table-head>
                    <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('schemeWeaver_type')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
                    <uui-table-head-cell>${this.localize.term('schemeWeaver_wrapInType')}</uui-table-head-cell>
                  </uui-table-head>

                  ${this._nestedMappings.map(
                    (mapping, index) => {
                      const schemaProp = this._schemaProperties.find((p) => p.name === mapping.schemaProperty);
                      return html`
                        <uui-table-row>
                          <uui-table-cell>
                            <strong>${mapping.schemaProperty}</strong>
                          </uui-table-cell>
                          <uui-table-cell>
                            <small class="type-label">${schemaProp?.propertyType || ''}</small>
                          </uui-table-cell>
                          <uui-table-cell>
                            <uui-input
                              .value=${mapping.contentProperty}
                              @input=${(e: Event) =>
                                this._handleContentPropertyChange(index, (e.target as HTMLInputElement).value)}
                              placeholder="Block property alias..."
                              label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaProperty}
                            ></uui-input>
                          </uui-table-cell>
                          <uui-table-cell>
                            <uui-input
                              .value=${mapping.wrapInType}
                              @input=${(e: Event) =>
                                this._handleWrapInTypeChange(index, (e.target as HTMLInputElement).value)}
                              placeholder=${this.localize.term('schemeWeaver_wrapInType')}
                              label=${this.localize.term('schemeWeaver_wrapInType') + ' ' + mapping.schemaProperty}
                            ></uui-input>
                          </uui-table-cell>
                        </uui-table-row>
                      `;
                    }
                  )}
                </uui-table>
              </uui-box>
            `}

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button
            look="primary"
            @click=${this._handleSave}
            ?disabled=${this._loading}
            label=${this.localize.term('schemeWeaver_save')}
          >
            ${this.localize.term('schemeWeaver_save')}
          </uui-button>
        </div>
      </umb-body-layout>
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

      .type-label {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      uui-box + uui-box {
        margin-top: var(--uui-size-space-4);
      }

      uui-input {
        width: 100%;
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
