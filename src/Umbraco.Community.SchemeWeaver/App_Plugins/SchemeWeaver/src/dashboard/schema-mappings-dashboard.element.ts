import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';

interface ContentTypeMapping {
  contentTypeAlias: string;
  contentTypeName: string;
  schemaTypeName: string | null;
  isMapped: boolean;
  propertyMappingCount: number;
}

@customElement('schemeweaver-schema-mappings-dashboard')
export class SchemaMappingsDashboardElement extends UmbLitElement {
  #repository = new SchemeWeaverRepository(this);

  @state()
  private _loading = true;

  @state()
  private _mappings: ContentTypeMapping[] = [];

  @state()
  private _searchTerm = '';

  @state()
  private _errorMessage = '';

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchMappings();
  }

  private async _fetchMappings() {
    this._loading = true;
    this._errorMessage = '';

    try {
      const [mappings, contentTypes] = await Promise.all([
        this.#repository.requestMappings(),
        this.#repository.requestContentTypes(),
      ]);

      if (!mappings || !contentTypes) {
        throw new Error('Failed to fetch data from SchemeWeaver API');
      }

      this._mappings = contentTypes.map((ct: any) => {
        const mapping = mappings.find((m: any) => m.contentTypeAlias === ct.alias);
        return {
          contentTypeAlias: ct.alias,
          contentTypeName: ct.name,
          schemaTypeName: mapping?.schemaTypeName || null,
          isMapped: !!mapping,
          propertyMappingCount: mapping?.propertyMappings?.length || 0,
        };
      });
    } catch (error) {
      console.error('SchemeWeaver: Error fetching mappings:', error);
      this._errorMessage = error instanceof Error ? error.message : 'Failed to load mappings';
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
      await this._fetchMappings();
    } catch (error) {
      console.error('SchemeWeaver: Error deleting mapping:', error);
    }
  }

  /** Opens schema picker → property mapping modal sequence (like entity actions) */
  private async _handleMap(alias: string) {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);

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

    await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias: alias,
          schemaType: mapping.schemaTypeName,
        },
      })
      .onSubmit()
      .catch(() => null);

    await this._fetchMappings();
  }

  private async _handlePreview(alias: string) {
    try {
      const preview = await this.#repository.requestPreview(alias);

      if (!preview) {
        throw new Error('Failed to generate preview');
      }

      this.dispatchEvent(
        new CustomEvent('schemeweaver-show-preview', {
          detail: { jsonLd: preview },
          bubbles: true,
          composed: true,
        })
      );
    } catch (error) {
      console.error('SchemeWeaver: Error generating preview:', error);
    }
  }

  render() {
    return html`
      <umb-body-layout headline="Schema.org Mappings">
        <uui-box>
          <div class="header-actions">
            <uui-input
              placeholder="Search content types..."
              @input=${this._handleSearch}
              .value=${this._searchTerm}
            >
              <uui-icon name="icon-search" slot="prepend"></uui-icon>
            </uui-input>
            <uui-button
              look="outline"
              @click=${this._fetchMappings}
              label="Refresh"
            >
              <uui-icon name="icon-refresh"></uui-icon>
              Refresh
            </uui-button>
          </div>

          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>Loading schema mappings...</p>
                </div>
              `
            : this._errorMessage
              ? html`
                  <div class="error-message">
                    <p>${this._errorMessage}</p>
                    <uui-button look="primary" @click=${this._fetchMappings}>Retry</uui-button>
                  </div>
                `
              : html`
                  <uui-table>
                    <uui-table-head>
                      <uui-table-head-cell>Content Type</uui-table-head-cell>
                      <uui-table-head-cell>Schema Type</uui-table-head-cell>
                      <uui-table-head-cell>Status</uui-table-head-cell>
                      <uui-table-head-cell>Properties</uui-table-head-cell>
                      <uui-table-head-cell>Actions</uui-table-head-cell>
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
                              : html`<span class="unmapped-text">Not mapped</span>`}
                          </uui-table-cell>
                          <uui-table-cell>
                            ${mapping.isMapped
                              ? html`<uui-badge color="positive">Mapped</uui-badge>`
                              : html`<uui-badge color="default">Unmapped</uui-badge>`}
                          </uui-table-cell>
                          <uui-table-cell>
                            ${mapping.isMapped
                              ? html`${mapping.propertyMappingCount} properties`
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
                                      label="Edit mapping"
                                    >
                                      <uui-icon name="icon-edit"></uui-icon>
                                    </uui-button>
                                    <uui-button
                                      look="outline"
                                      compact
                                      @click=${() => this._handlePreview(mapping.contentTypeAlias)}
                                      label="Preview JSON-LD"
                                    >
                                      <uui-icon name="icon-brackets"></uui-icon>
                                    </uui-button>
                                    <uui-button
                                      look="outline"
                                      color="danger"
                                      compact
                                      @click=${() => this._handleDelete(mapping.contentTypeAlias)}
                                      label="Delete mapping"
                                    >
                                      <uui-icon name="icon-trash"></uui-icon>
                                    </uui-button>
                                  `
                                : html`
                                    <uui-button
                                      look="primary"
                                      compact
                                      @click=${() => this._handleMap(mapping.contentTypeAlias)}
                                      label="Map to Schema.org"
                                    >
                                      <uui-icon name="icon-brackets"></uui-icon>
                                      Map
                                    </uui-button>
                                  `}
                            </div>
                          </uui-table-cell>
                        </uui-table-row>
                      `
                    )}
                  </uui-table>

                  ${this._filteredMappings.length === 0
                    ? html`<p class="no-results">No content types found matching your search.</p>`
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

      .error-message {
        color: var(--uui-color-danger);
        text-align: center;
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
