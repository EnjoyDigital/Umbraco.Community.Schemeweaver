import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';

interface ContentTypeMapping {
  contentTypeAlias: string;
  contentTypeName: string;
  contentTypeKey: string;
  schemaTypeName: string | null;
  isMapped: boolean;
  isInherited: boolean;
  propertyMappingCount: number;
}

@customElement('schemeweaver-schema-mappings-dashboard')
export class SchemaMappingsDashboardElement extends UmbLitElement {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state()
  private _loading = true;

  @state()
  private _mappings: ContentTypeMapping[] = [];

  @state()
  private _searchTerm = '';

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (context) => {
      this.#notificationContext = context;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchMappings();
  }

  private async _fetchMappings() {
    this._loading = true;

    try {
      const [mappings, contentTypes] = await Promise.all([
        this.#repository.requestMappings(),
        this.#repository.requestContentTypes(),
      ]);

      if (!mappings || !contentTypes) {
        throw new Error('Failed to fetch data from SchemeWeaver API');
      }

      this._mappings = contentTypes.map((ct) => {
        const mapping = mappings.find((m) => m.contentTypeAlias === ct.alias);
        return {
          contentTypeAlias: ct.alias,
          contentTypeName: ct.name,
          contentTypeKey: ct.key,
          schemaTypeName: mapping?.schemaTypeName || null,
          isMapped: !!mapping,
          isInherited: mapping?.isInherited || false,
          propertyMappingCount: mapping?.propertyMappings?.length || 0,
        };
      });
    } catch (error) {
      console.error('SchemeWeaver: Error fetching mappings:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to load mappings',
        },
      });
    } finally {
      this._loading = false;
    }
  }

  private get _filteredMappings(): ContentTypeMapping[] {
    if (!this._searchTerm) return this._mappings;
    const term = this._searchTerm.toLowerCase();
    return this._mappings.filter(
      (m) =>
        m.contentTypeName.toLowerCase().includes(term) ||
        m.contentTypeAlias.toLowerCase().includes(term) ||
        (m.schemaTypeName && m.schemaTypeName.toLowerCase().includes(term))
    );
  }

  private _handleSearch(e: Event) {
    this._searchTerm = (e.target as HTMLInputElement).value;
  }

  private async _handleDelete(alias: string) {
    try {
      await this.#repository.deleteMapping(alias);
      this.#notificationContext?.peek('positive', {
        data: { message: this.localize.term('schemeWeaver_mappingDeleted') },
      });
      await this._fetchMappings();
    } catch (error) {
      console.error('SchemeWeaver: Error deleting mapping:', error);
      this.#notificationContext?.peek('danger', {
        data: {
          message: error instanceof Error ? error.message : 'Failed to delete mapping',
        },
      });
    }
  }

  /** Opens schema picker -> property mapping modal sequence (like entity actions) */
  private async _handleMap(alias: string) {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const mapping = this._mappings.find((m) => m.contentTypeAlias === alias);

    const pickerResult = await modalManager
      .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
        data: { contentTypeAlias: alias },
      })
      .onSubmit()
      .catch(() => null);

    if (!pickerResult?.schemaType) return;

    await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias: alias,
          schemaType: pickerResult.schemaType,
          contentTypeKey: mapping?.contentTypeKey ?? '',
        },
      })
      .onSubmit()
      .catch(() => null);

    await this._fetchMappings();
  }

  /** Opens property mapping modal for editing existing mapping */
  private async _handleEdit(alias: string) {
    const mapping = this._mappings.find((m) => m.contentTypeAlias === alias);
    if (!mapping?.schemaTypeName) return;

    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias: alias,
          schemaType: mapping.schemaTypeName,
          contentTypeKey: mapping.contentTypeKey ?? '',
        },
      })
      .onSubmit()
      .catch(() => null);

    await this._fetchMappings();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_dashboardHeadline')}>
        <uui-box>
          <div class="header-actions">
            <uui-input
              placeholder=${this.localize.term('schemeWeaver_searchContentTypes')}
              @input=${this._handleSearch}
              .value=${this._searchTerm}
              label=${this.localize.term('schemeWeaver_searchContentTypes')}
            >
              <uui-icon name="icon-search" slot="prepend"></uui-icon>
            </uui-input>
            <uui-button
              look="outline"
              @click=${this._fetchMappings}
              label=${this.localize.term('schemeWeaver_refresh')}
            >
              <uui-icon name="icon-refresh"></uui-icon>
              ${this.localize.term('schemeWeaver_refresh')}
            </uui-button>
          </div>

          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>${this.localize.term('schemeWeaver_loadingMappings')}</p>
                </div>
              `
            : html`
                  <uui-table>
                    <uui-table-head>
                      <uui-table-head-cell>${this.localize.term('schemeWeaver_contentType')}</uui-table-head-cell>
                      <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaType')}</uui-table-head-cell>
                      <uui-table-head-cell>${this.localize.term('schemeWeaver_status')}</uui-table-head-cell>
                      <uui-table-head-cell>${this.localize.term('schemeWeaver_properties')}</uui-table-head-cell>
                      <uui-table-head-cell>${this.localize.term('schemeWeaver_actions')}</uui-table-head-cell>
                    </uui-table-head>

                    ${this._filteredMappings.map(
                      (mapping) => html`
                        <uui-table-row>
                          <uui-table-cell>
                            <strong>${mapping.contentTypeName}</strong>
                            <br />
                            <small class="alias">${mapping.contentTypeAlias}</small>
                          </uui-table-cell>
                          <uui-table-cell>
                            ${mapping.schemaTypeName
                              ? html`<uui-tag color="primary" look="primary">${mapping.schemaTypeName}</uui-tag>`
                              : html`<span class="unmapped-text">${this.localize.term('schemeWeaver_notMapped')}</span>`}
                          </uui-table-cell>
                          <uui-table-cell>
                            ${mapping.isMapped
                              ? html`<uui-badge color="positive">${this.localize.term('schemeWeaver_mapped')}</uui-badge>`
                              : html`<uui-badge color="default">${this.localize.term('schemeWeaver_unmapped')}</uui-badge>`}
                            ${mapping.isInherited
                              ? html` <uui-tag color="warning" look="outline" compact>${this.localize.term('schemeWeaver_inherited')}</uui-tag>`
                              : ''}
                          </uui-table-cell>
                          <uui-table-cell>
                            ${mapping.isMapped
                              ? html`${mapping.propertyMappingCount} ${this.localize.term('schemeWeaver_properties')}`
                              : html`-`}
                          </uui-table-cell>
                          <uui-table-cell>
                            <div class="action-buttons">
                              ${mapping.isMapped
                                ? html`
                                    <uui-button
                                      look="outline"
                                      compact
                                      @click=${() => this._handleEdit(mapping.contentTypeAlias)}
                                      label=${this.localize.term('schemeWeaver_editMapping')}
                                    >
                                      <uui-icon name="icon-edit"></uui-icon>
                                    </uui-button>
                                    <uui-button
                                      look="outline"
                                      color="danger"
                                      compact
                                      @click=${() => this._handleDelete(mapping.contentTypeAlias)}
                                      label=${this.localize.term('schemeWeaver_deleteMapping')}
                                    >
                                      <uui-icon name="icon-trash"></uui-icon>
                                    </uui-button>
                                  `
                                : html`
                                    <uui-button
                                      look="primary"
                                      compact
                                      @click=${() => this._handleMap(mapping.contentTypeAlias)}
                                      label=${this.localize.term('schemeWeaver_mapToSchema')}
                                    >
                                      <uui-icon name="icon-brackets"></uui-icon>
                                      ${this.localize.term('schemeWeaver_map')}
                                    </uui-button>
                                  `}
                            </div>
                          </uui-table-cell>
                        </uui-table-row>
                      `
                    )}
                  </uui-table>

                  ${this._filteredMappings.length === 0
                    ? html`<p class="no-results">${this.localize.term('schemeWeaver_noResults')}</p>`
                    : ''}
                `}
        </uui-box>
      </umb-body-layout>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
        padding: var(--uui-size-space-5);
      }

      .header-actions {
        display: flex;
        gap: var(--uui-size-space-4);
        margin-bottom: var(--uui-size-space-5);
        align-items: center;
      }

      .header-actions uui-input {
        flex: 1;
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .alias {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      .unmapped-text {
        color: var(--uui-color-text-alt);
        font-style: italic;
      }

      .action-buttons {
        display: flex;
        gap: var(--uui-size-space-2);
      }

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        padding: var(--uui-size-space-6);
      }
    `,
  ];
}

export default SchemaMappingsDashboardElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-schema-mappings-dashboard': SchemaMappingsDashboardElement;
  }
}
