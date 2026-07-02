import { css, html, customElement, state, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';
import type { SchemaTypeInfo, SchemaTypeSuggestion } from '../api/types.js';
import type { SchemaPickerModalData, SchemaPickerModalValue } from './schema-picker-modal.token.js';

/**
 * The curated shortlist shown when no search query has been entered. Order is
 * significant (most commonly mapped types first); names missing from the
 * server's type list are silently dropped.
 */
const COMMON_SCHEMA_TYPES: readonly string[] = [
  'Article',
  'NewsArticle',
  'BlogPosting',
  'WebPage',
  'WebSite',
  'CollectionPage',
  'AboutPage',
  'ContactPage',
  'FAQPage',
  'Product',
  'Offer',
  'Event',
  'Recipe',
  'HowTo',
  'Organization',
  'LocalBusiness',
  'Person',
  'JobPosting',
  'Review',
  'VideoObject',
];

/** Maximum number of rows rendered for a search query (the full Schema.org universe is ~800 types). */
const RESULT_CAP = 50;

@customElement('schemeweaver-schema-picker-modal')
export class SchemaPickerModalElement extends UmbModalBaseElement<SchemaPickerModalData, SchemaPickerModalValue> {
  #repository = new SchemeWeaverRepository(this);
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  /** Full type list, fetched ONCE on open. All searching/filtering is in-memory. */
  #allTypes: SchemaTypeInfo[] = [];

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => {
      this.#notificationContext = ctx;
    });
  }

  @state()
  private _loading = true;

  @state()
  private _searchTerm = '';

  @state()
  private _selectedType = '';

  @state()
  private _aiAvailable = false;

  @state()
  private _aiLoading = false;

  @state()
  private _aiSuggestions: SchemaTypeSuggestion[] = [];

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchSchemaTypes();
    this._checkAIStatus();
  }

  private async _checkAIStatus() {
    try {
      const status = await this.#repository.requestAIStatus();
      this._aiAvailable = status?.available === true;
    } catch {
      this._aiAvailable = false;
    }
  }

  private async _handleAISuggest() {
    const contentTypeAlias = this.data?.contentTypeAlias;
    if (!contentTypeAlias) return;

    this._aiLoading = true;
    this._aiSuggestions = [];
    try {
      const suggestions = await this.#repository.requestAISuggestSchemaType(contentTypeAlias);
      if (suggestions && suggestions.length > 0) {
        this._aiSuggestions = suggestions.slice(0, 3);
      } else {
        this.#notificationContext?.peek('warning', {
          data: { message: this.localize.term('schemeWeaver_aiNoSuggestions') },
        });
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_aiAnalysisFailed') },
      });
    } finally {
      this._aiLoading = false;
    }
  }

  private _handleAISuggestionSelect(typeName: string) {
    this._selectedType = typeName;
  }

  private async _fetchSchemaTypes() {
    this._loading = true;
    try {
      const types = await this.#repository.requestSchemaTypes();
      if (types) {
        this.#allTypes = types;
      }
    } catch {
      this.#notificationContext?.peek('danger', {
        data: { message: this.localize.term('schemeWeaver_failedToLoadSchemaTypes') },
      });
    } finally {
      this._loading = false;
    }
  }

  private _handleSearch(e: Event) {
    this._searchTerm = (e.target as HTMLInputElement).value ?? '';
  }

  /**
   * In-memory filter over the cached type list.
   * - No query: the curated {@link COMMON_SCHEMA_TYPES} shortlist (curated order).
   * - Query: ranked — name startsWith, then name includes, then description
   *   includes — capped at {@link RESULT_CAP}.
   */
  private _filterTypes(query: string): { items: SchemaTypeInfo[]; total: number } {
    if (!query) {
      const byName = new Map(this.#allTypes.map((t) => [t.name.toLowerCase(), t]));
      const items = COMMON_SCHEMA_TYPES.map((name) => byName.get(name.toLowerCase())).filter(
        (t): t is SchemaTypeInfo => t !== undefined,
      );
      return { items, total: items.length };
    }

    const q = query.toLowerCase();
    const nameStarts: SchemaTypeInfo[] = [];
    const nameIncludes: SchemaTypeInfo[] = [];
    const descriptionIncludes: SchemaTypeInfo[] = [];

    for (const type of this.#allTypes) {
      const name = type.name.toLowerCase();
      if (name.startsWith(q)) {
        nameStarts.push(type);
      } else if (name.includes(q)) {
        nameIncludes.push(type);
      } else if (type.description?.toLowerCase().includes(q)) {
        descriptionIncludes.push(type);
      }
    }

    const ranked = [...nameStarts, ...nameIncludes, ...descriptionIncludes];
    return { items: ranked.slice(0, RESULT_CAP), total: ranked.length };
  }

  /** Single truncating detail line: `extends {parent} · {n} properties — {description}` (missing segments omitted). */
  private _buildDetail(type: SchemaTypeInfo): string {
    const segments: string[] = [];
    if (type.parentTypeName) {
      segments.push(`${this.localize.term('schemeWeaver_extends')} ${type.parentTypeName}`);
    }
    if (type.propertyCount > 0) {
      segments.push(this.localize.term('schemeWeaver_schemaPropertyCount', type.propertyCount));
    }
    const head = segments.join(' · ');
    const description = type.description ?? '';
    if (head && description) return `${head} — ${description}`;
    return head || description;
  }

  private _handleSelect(typeName: string) {
    this._selectedType = typeName;
  }

  private _handleDeselect() {
    this._selectedType = '';
  }

  private _handleSubmit() {
    if (!this._selectedType) return;
    this.modalContext?.setValue({ schemaType: this._selectedType });
    this.modalContext?.submit();
  }

  private _handleClose() {
    this.modalContext?.reject();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaver_selectSchemaType')}>
        <div id="main">
          <uui-input
            id="search"
            type="search"
            placeholder=${this.localize.term('schemeWeaver_searchSchemaTypes')}
            label=${this.localize.term('schemeWeaver_searchSchemaTypes')}
            .value=${this._searchTerm}
            data-mark="schemeweaver:schema-search"
            autofocus
            @input=${this._handleSearch}
          >
            <uui-icon name="search" slot="prepend" id="search-icon"></uui-icon>
          </uui-input>

          ${this._renderAiBox()}
          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>${this.localize.term('schemeWeaver_loadingSchemaTypes')}</p>
                </div>
              `
            : this._renderResults()}
        </div>

        <div slot="actions">
          <uui-button
            label=${this.localize.term('schemeWeaver_cancel')}
            data-mark="schemeweaver:schema-picker-cancel"
            @click=${this._handleClose}
          ></uui-button>
          <uui-button
            look="primary"
            color="positive"
            label=${this.localize.term('buttons_select')}
            data-mark="schemeweaver:schema-picker-submit"
            ?disabled=${!this._selectedType}
            @click=${this._handleSubmit}
          ></uui-button>
        </div>
      </umb-body-layout>
    `;
  }

  private _renderAiBox() {
    if (!this._aiAvailable) return nothing;
    return html`
      <uui-box headline=${this.localize.term('schemeWeaver_aiSuggestedSchema')}>
        ${this._aiSuggestions.length === 0
          ? html`
              <uui-button
                look="outline"
                color="positive"
                @click=${this._handleAISuggest}
                ?disabled=${this._aiLoading}
                .state=${this._aiLoading ? 'waiting' : undefined}
                label=${this.localize.term('schemeWeaver_aiAnalyse')}
              >
                <uui-icon name="icon-wand"></uui-icon>
                ${this._aiLoading
                  ? this.localize.term('schemeWeaver_aiAnalysing')
                  : this.localize.term('schemeWeaver_aiAnalyse')}
              </uui-button>
            `
          : html`
              <uui-ref-list>
                ${repeat(
                  this._aiSuggestions,
                  (s) => s.schemaTypeName,
                  (suggestion) => html`
                    <umb-ref-item
                      selectable
                      select-only
                      ?selected=${this._selectedType === suggestion.schemaTypeName}
                      name=${suggestion.schemaTypeName}
                      detail=${suggestion.reasoning ?? ''}
                      icon="icon-wand"
                      @selected=${() => this._handleAISuggestionSelect(suggestion.schemaTypeName)}
                      @deselected=${this._handleDeselect}
                    >
                      <uui-tag
                        slot="tag"
                        color=${suggestion.confidence >= 80 ? 'positive' : suggestion.confidence >= 50 ? 'warning' : 'default'}
                      >${suggestion.confidence}%</uui-tag>
                    </umb-ref-item>
                  `,
                )}
              </uui-ref-list>
            `}
      </uui-box>
    `;
  }

  private _renderResults() {
    const query = this._searchTerm.trim();
    const { items, total } = this._filterTypes(query);
    const headline = query
      ? this.localize.term('schemeWeaver_searchResults')
      : this.localize.term('schemeWeaver_commonTypes');

    return html`
      <uui-box headline=${headline}>
        ${items.length < total
          ? html`<small class="cap-note">${this.localize.term('schemeWeaver_showingTopResults', items.length, total)}</small>`
          : nothing}
        ${items.length === 0
          ? html`<p class="no-results">${this.localize.term('schemeWeaver_noSchemaTypes')}</p>`
          : html`
              <uui-ref-list>
                ${repeat(
                  items,
                  (type) => type.name,
                  (type) => html`
                    <umb-ref-item
                      selectable
                      select-only
                      ?selected=${this._selectedType === type.name}
                      name=${type.name}
                      detail=${this._buildDetail(type)}
                      icon="icon-brackets"
                      data-mark="schemeweaver:schema-option:${type.name}"
                      @selected=${() => this._handleSelect(type.name)}
                      @deselected=${this._handleDeselect}
                    >
                    </umb-ref-item>
                  `,
                )}
              </uui-ref-list>
            `}
      </uui-box>
    `;
  }

  static styles = [
    UmbTextStyles,
    css`
      :host {
        display: block;
        height: 100%;
      }

      #main {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-5);
      }

      #search {
        width: 100%;
      }

      #search-icon {
        height: 100%;
        display: flex;
        align-items: center;
        padding-left: var(--uui-size-space-2);
        color: var(--uui-color-border);
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .cap-note {
        display: block;
        color: var(--uui-color-text-alt);
        margin-bottom: var(--uui-size-space-3);
      }

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        margin: 0;
        padding: var(--uui-size-space-4) 0;
      }
    `,
  ];
}

export default SchemaPickerModalElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-schema-picker-modal': SchemaPickerModalElement;
  }
}
