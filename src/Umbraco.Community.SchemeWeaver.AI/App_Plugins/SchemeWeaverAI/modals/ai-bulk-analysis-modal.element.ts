import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_AUTH_CONTEXT } from '@umbraco-cms/backoffice/auth';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbContextToken, UmbContextMinimal } from '@umbraco-cms/backoffice/context-api';
import type { AIBulkAnalysisModalData, AIBulkAnalysisModalValue } from './ai-bulk-analysis-modal.token.js';

const API_BASE = '/umbraco/management/api/v1/schemeweaver';

type ContextHost = UmbControllerHost & {
  getContext<TContext extends UmbContextMinimal>(token: UmbContextToken<TContext>): Promise<TContext>;
};

async function getAuthHeaders(host: UmbControllerHost): Promise<Record<string, string>> {
  try {
    const authContext = await (host as ContextHost).getContext(UMB_AUTH_CONTEXT);
    const config = authContext.getOpenApiConfiguration();
    const token = typeof config.token === 'function' ? await config.token() : undefined;
    return token ? { Authorization: `Bearer ${token}` } : {};
  } catch {
    return {};
  }
}

async function fetchApi<T>(
  host: UmbControllerHost,
  path: string,
  options: RequestInit = {},
  signal?: AbortSignal,
): Promise<T | undefined> {
  const { data } = await tryExecute(
    host,
    (async () => {
      const authHeaders = await getAuthHeaders(host);
      const response = await fetch(`${API_BASE}${path}`, {
        ...options,
        signal,
        headers: {
          ...authHeaders,
          ...options.headers,
        },
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => 'Unknown error');
        throw new Error(errorText || `HTTP ${response.status}`);
      }

      if (response.status === 204) {
        return { data: undefined as T };
      }

      const json = await response.json();
      return { data: json as T };
    })(),
  );

  return data;
}

interface BulkSuggestion {
  contentTypeAlias: string;
  contentTypeName: string | null;
  suggestions: { schemaTypeName: string; confidence: number; reasoning: string | null }[];
}

interface ContentTypeInfo {
  alias: string;
  name: string;
  key: string;
  propertyCount: number;
}

interface PropertyMappingSuggestion {
  schemaPropertyName: string;
  suggestedContentTypePropertyAlias: string | null;
  suggestedSourceType: string;
  confidence: number;
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
  #abortController: AbortController | null = null;

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

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#abortController?.abort();
    this.#abortController = null;
  }

  private async _analyse() {
    this._loading = true;
    this.#abortController?.abort();
    this.#abortController = new AbortController();
    const { signal } = this.#abortController;

    try {
      const results = await fetchApi<BulkSuggestion[]>(
        this,
        '/ai/suggest-schema-types-bulk',
        { method: 'POST' },
        signal,
      );

      if (!results) return;

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
      if (signal.aborted) return;
      console.error('SchemeWeaver AI: Bulk analysis failed:', error);
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_aiAnalysisFailed') },
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
    this.#abortController?.abort();
    this.#abortController = new AbortController();
    const { signal } = this.#abortController;

    let applied = 0;

    try {
      // Fetch content types once to build an alias→key map (M-5)
      const contentTypes = await fetchApi<ContentTypeInfo[]>(this, '/content-types', {}, signal);
      const keyMap = new Map<string, string>();
      if (contentTypes) {
        for (const ct of contentTypes) {
          keyMap.set(ct.alias, ct.key);
        }
      }

      for (const row of selected) {
        if (signal.aborted) break;

        const suggestions = await fetchApi<PropertyMappingSuggestion[]>(
          this,
          `/ai/ai-auto-map/${encodeURIComponent(row.contentTypeAlias)}?schemaTypeName=${encodeURIComponent(row.schemaTypeName)}`,
          { method: 'POST' },
          signal,
        );

        if (!suggestions || signal.aborted) continue;

        const mapping = {
          contentTypeAlias: row.contentTypeAlias,
          contentTypeKey: keyMap.get(row.contentTypeAlias) ?? '',
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

        const saved = await fetchApi<unknown>(
          this,
          '/mappings',
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(mapping),
          },
          signal,
        );

        if (saved !== undefined) applied++;
      }

      if (!signal.aborted) {
        this.#notificationContext?.peek('positive', {
          data: { message: this.localize.term('schemeWeaver_aiBulkApplied', applied, selected.length) },
        });

        this.modalContext?.setValue({ applied: true });
        this.modalContext?.submit();
      }
    } catch (error) {
      if (signal.aborted) return;
      console.error('SchemeWeaver AI: Apply failed:', error);
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_aiBulkApplyFailed') },
      });
    } finally {
      this._applying = false;
    }
  }

  private _handleClose() {
    this.#abortController?.abort();
    this.#abortController = null;
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
            <uui-button look="outline" @click=${this._selectAll} label=${this.localize.term('schemeWeaver_aiSelectAll')}>
              ${this.localize.term('schemeWeaver_aiSelectAll')}
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
