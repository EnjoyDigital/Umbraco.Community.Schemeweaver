import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import type { AIBulkAnalysisModalData, AIBulkAnalysisModalValue } from './ai-bulk-analysis-modal.token.js';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

interface BulkSuggestion {
  contentTypeAlias: string;
  contentTypeName: string | null;
  suggestions: { schemaTypeName: string; confidence: number; reasoning: string | null }[];
}

interface BulkRow {
  contentTypeAlias: string;
  contentTypeName: string;
  schemaTypeName: string;
  confidence: number;
  reasoning: string;
  selected: boolean;
}

@customElement('schemeweaver-ai-bulk-analysis-modal')
export class AIBulkAnalysisModalElement extends UmbModalBaseElement<AIBulkAnalysisModalData, AIBulkAnalysisModalValue> {
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state() private _loading = true;
  @state() private _applying = false;
  @state() private _rows: BulkRow[] = [];

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => {
      this.#notificationContext = ctx;
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._analyse();
  }

  private async _getAuthHeaders(): Promise<Record<string, string>> {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const authContext = await (this as any).getContext(UMB_AUTH_CONTEXT);
      const config = authContext.getOpenApiConfiguration();
      const token = typeof config.TOKEN === 'function'
        ? await config.TOKEN({ url: API_BASE })
        : config.TOKEN;
      return token ? { Authorization: `Bearer ${token}` } : {};
    } catch {
      return {};
    }
  }

  private async _analyse() {
    this._loading = true;
    try {
      const authHeaders = await this._getAuthHeaders();
      const response = await fetch(`${API_BASE}/ai/suggest-schema-types-bulk`, {
        method: 'POST',
        headers: authHeaders,
      });

      if (!response.ok) throw new Error(`HTTP ${response.status}`);

      const results = await response.json() as BulkSuggestion[];

      this._rows = results
        .filter((r) => r.suggestions.length > 0)
        .map((r) => ({
          contentTypeAlias: r.contentTypeAlias,
          contentTypeName: r.contentTypeName || r.contentTypeAlias,
          schemaTypeName: r.suggestions[0].schemaTypeName,
          confidence: r.suggestions[0].confidence,
          reasoning: r.suggestions[0].reasoning || '',
          selected: r.suggestions[0].confidence >= 70,
        }));
    } catch (error) {
      console.error('SchemeWeaver AI: Bulk analysis failed:', error);
      this.#notificationContext?.peek('danger', {
        data: { message: 'AI bulk analysis failed. Please try again.' },
      });
    } finally {
      this._loading = false;
    }
  }

  private _toggleRow(index: number) {
    const updated = [...this._rows];
    updated[index] = { ...updated[index], selected: !updated[index].selected };
    this._rows = updated;
  }

  private _selectAll() {
    this._rows = this._rows.map((r) => ({ ...r, selected: true }));
  }

  private async _applySelected() {
    const selected = this._rows.filter((r) => r.selected);
    if (selected.length === 0) return;

    this._applying = true;
    let applied = 0;

    try {
      const authHeaders = await this._getAuthHeaders();

      for (const row of selected) {
        // Get AI auto-map suggestions for each
        const mapResponse = await fetch(
          `${API_BASE}/ai/ai-auto-map/${encodeURIComponent(row.contentTypeAlias)}?schemaTypeName=${encodeURIComponent(row.schemaTypeName)}`,
          { method: 'POST', headers: authHeaders },
        );

        if (!mapResponse.ok) continue;

        const suggestions = await mapResponse.json() as {
          schemaPropertyName: string;
          suggestedContentTypePropertyAlias: string | null;
          suggestedSourceType: string;
          confidence: number;
        }[];

        // Save the mapping
        const mapping = {
          contentTypeAlias: row.contentTypeAlias,
          contentTypeKey: '',
          schemaTypeName: row.schemaTypeName,
          isEnabled: true,
          isInherited: false,
          propertyMappings: suggestions
            .filter((s) => s.suggestedContentTypePropertyAlias && s.confidence >= 50)
            .map((s) => ({
              schemaPropertyName: s.schemaPropertyName,
              sourceType: s.suggestedSourceType || 'property',
              contentTypePropertyAlias: s.suggestedContentTypePropertyAlias,
              sourceContentTypeAlias: null,
              transformType: null,
              isAutoMapped: true,
              staticValue: null,
              nestedSchemaTypeName: null,
              resolverConfig: null,
            })),
        };

        const saveResponse = await fetch(`${API_BASE}/mappings`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', ...authHeaders },
          body: JSON.stringify(mapping),
        });

        if (saveResponse.ok) applied++;
      }

      this.#notificationContext?.peek('positive', {
        data: { message: `Applied ${applied} of ${selected.length} mappings.` },
      });

      this.modalContext?.setValue({ applied: true });
      this.modalContext?.submit();
    } catch (error) {
      console.error('SchemeWeaver AI: Apply failed:', error);
      this.#notificationContext?.peek('danger', {
        data: { message: 'Failed to apply some mappings.' },
      });
    } finally {
      this._applying = false;
    }
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_aiBulkResults')}>
        ${this._loading
          ? html`
              <div class="loading">
                <uui-loader-circle></uui-loader-circle>
                <p>${this.localize.term('schemeWeaver_aiAnalysing')}</p>
              </div>
            `
          : html`
              <uui-box>
                ${this._rows.length === 0
                  ? html`<p class="no-results">${this.localize.term('schemeWeaver_aiNoSuggestions')}</p>`
                  : html`
                      <uui-table>
                        <uui-table-head>
                          <uui-table-head-cell></uui-table-head-cell>
                          <uui-table-head-cell>${this.localize.term('schemeWeaver_contentType')}</uui-table-head-cell>
                          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaType')}</uui-table-head-cell>
                          <uui-table-head-cell>${this.localize.term('schemeWeaver_confidence')}</uui-table-head-cell>
                          <uui-table-head-cell>${this.localize.term('schemeWeaver_aiReasoning')}</uui-table-head-cell>
                        </uui-table-head>
                        ${this._rows.map(
                          (row, index) => html`
                            <uui-table-row>
                              <uui-table-cell>
                                <uui-checkbox
                                  .checked=${row.selected}
                                  @change=${() => this._toggleRow(index)}
                                ></uui-checkbox>
                              </uui-table-cell>
                              <uui-table-cell><strong>${row.contentTypeName}</strong></uui-table-cell>
                              <uui-table-cell>${row.schemaTypeName}</uui-table-cell>
                              <uui-table-cell>
                                <uui-tag color=${row.confidence >= 80 ? 'positive' : row.confidence >= 50 ? 'warning' : 'default'}>
                                  ${row.confidence}%
                                </uui-tag>
                              </uui-table-cell>
                              <uui-table-cell class="reasoning-cell">${row.reasoning}</uui-table-cell>
                            </uui-table-row>
                          `
                        )}
                      </uui-table>
                    `}
              </uui-box>
            `}

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          ${this._rows.length > 0 ? html`
            <uui-button look="outline" @click=${this._selectAll} label="Select all">
              Select All
            </uui-button>
            <uui-button
              look="primary"
              color="positive"
              @click=${this._applySelected}
              ?disabled=${this._applying || this._rows.filter((r) => r.selected).length === 0}
              .state=${this._applying ? 'waiting' : undefined}
              label=${this.localize.term('schemeWeaver_aiApplyAll')}
            >
              ${this._applying
                ? this.localize.term('schemeWeaver_aiApplying')
                : `${this.localize.term('schemeWeaver_aiApply')} (${this._rows.filter((r) => r.selected).length})`}
            </uui-button>
          ` : ''}
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

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        padding: var(--uui-size-space-6);
      }

      .reasoning-cell {
        font-size: 0.85rem;
        color: var(--uui-color-text-alt);
        max-width: 300px;
      }
    `,
  ];
}

export default AIBulkAnalysisModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-ai-bulk-analysis-modal': AIBulkAnalysisModalElement;
  }
}
