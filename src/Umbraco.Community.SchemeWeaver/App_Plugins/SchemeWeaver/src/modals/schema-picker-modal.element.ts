import { css, html, customElement, state, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';
import type { SchemaTypeInfo, SchemaTypeSuggestion } from '../api/types.js';
import type { SchemaPickerModalData, SchemaPickerModalValue } from './schema-picker-modal.token.js';

interface SchemaTypeGroup {
  parent: string;
  types: SchemaTypeInfo[];
}

@customElement('schemeweaver-schema-picker-modal')
export class SchemaPickerModalElement extends UmbModalBaseElement<SchemaPickerModalData, SchemaPickerModalValue> {
  #context?: SchemeWeaverContext;
  #searchTimer?: ReturnType<typeof setTimeout>;
  #notificationContext?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_CONTEXT, (ctx) => {
      this.#context = ctx;
    });
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (ctx) => {
      this.#notificationContext = ctx;
    });
  }

  @state()
  private _loading = true;

  @state()
  private _searchTerm = '';

  @state()
  private _schemaTypes: SchemaTypeInfo[] = [];

  @state()
  private _selectedType = '';

  @state()
  private _aiAvailable = false;

  @state()
  private _aiChecking = true;

  @state()
  private _aiLoading = false;

  @state()
  private _aiSuggestions: SchemaTypeSuggestion[] = [];

  async connectedCallback() {
    super.connectedCallback();
    await this._fetchSchemaTypes();
    this._checkAIStatus();
  }

  override disconnectedCallback() {
    // Clear any pending debounced search so a callback can't fire after the
    // modal has been closed and the element is being torn down.
    if (this.#searchTimer) {
      clearTimeout(this.#searchTimer);
      this.#searchTimer = undefined;
    }
    super.disconnectedCallback();
  }

  private async _checkAIStatus() {
    this._aiChecking = true;
    try {
      const status = await this.#context?.repository.requestAIStatus();
      this._aiAvailable = status?.available === true;
    } catch {
      this._aiAvailable = false;
    } finally {
      this._aiChecking = false;
    }
  }

  private async _handleAISuggest() {
    const contentTypeAlias = this.data?.contentTypeAlias;
    if (!contentTypeAlias) return;

    this._aiLoading = true;
    this._aiSuggestions = [];
    try {
      const suggestions = await this.#context?.repository.requestAISuggestSchemaType(contentTypeAlias);
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
      const types = await this.#context?.repository.requestSchemaTypes();
      if (types) {
        this._schemaTypes = types;
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
    this._searchTerm = (e.target as HTMLInputElement).value;
    this._loading = true;
    clearTimeout(this.#searchTimer);
    this.#searchTimer = setTimeout(() => this._doSearch(), 300);
  }

  private async _doSearch() {
    this._loading = true;
    try {
      const types = await this.#context?.repository.requestSchemaTypes(this._searchTerm || undefined);
      if (types) {
        this._schemaTypes = types;
      }
    } catch {
      this.#notificationContext?.peek('warning', {
        data: { message: this.localize.term('schemeWeaver_searchFailed') },
      });
    } finally {
      this._loading = false;
    }
  }

  private get _groupedTypes(): SchemaTypeGroup[] {
    const groups = new Map<string, SchemaTypeInfo[]>();

    for (const type of this._schemaTypes) {
      const parent = type.parentTypeName || 'Thing';
      if (!groups.has(parent)) {
        groups.set(parent, []);
      }
      groups.get(parent)!.push(type);
    }

    return Array.from(groups.entries()).map(([parent, types]) => ({
      parent,
      types: types.sort((a, b) => a.name.localeCompare(b.name)),
    }));
  }

  private _handleSelect(typeName: string) {
    this._selectedType = typeName;
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
        <uui-box>
          ${this._aiChecking ? html`
            <div class="ai-section">
              <uui-loader-bar></uui-loader-bar>
            </div>
          ` : this._aiAvailable ? html`
            <div class="ai-section">
              ${this._aiSuggestions.length === 0 ? html`
                <uui-button
                  look="outline"
                  color="positive"
                  @click=${this._handleAISuggest}
                  ?disabled=${this._aiLoading}
                  .state=${this._aiLoading ? 'waiting' : undefined}
                  label=${this.localize.term('schemeWeaver_aiAnalyse')}
                >
                  <uui-icon name="icon-wand"></uui-icon>
                  ${this._aiLoading ? this.localize.term('schemeWeaver_aiAnalysing') : this.localize.term('schemeWeaver_aiAnalyse')}
                </uui-button>
              ` : html`
                <div class="ai-suggestions">
                  <h4 class="ai-suggestions-header">${this.localize.term('schemeWeaver_aiSuggestedSchema')}</h4>
                  ${this._aiSuggestions.map((suggestion) => html`
                    <div
                      class="ai-suggestion-card ${this._selectedType === suggestion.schemaTypeName ? 'selected' : ''}"
                      @click=${() => this._handleAISuggestionSelect(suggestion.schemaTypeName)}
                      @keydown=${(e: KeyboardEvent) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); this._handleAISuggestionSelect(suggestion.schemaTypeName); } }}
                      tabindex="0"
                      role="option"
                      aria-selected="${this._selectedType === suggestion.schemaTypeName}"
                    >
                      <div class="ai-suggestion-header">
                        <strong>${suggestion.schemaTypeName}</strong>
                        <uui-tag color=${suggestion.confidence >= 80 ? 'positive' : suggestion.confidence >= 50 ? 'warning' : 'default'}>
                          ${suggestion.confidence}%
                        </uui-tag>
                      </div>
                      ${suggestion.reasoning ? html`<p class="ai-suggestion-reasoning">${suggestion.reasoning}</p>` : ''}
                    </div>
                  `)}
                </div>
              `}
            </div>
          ` : ''}

          <uui-input
            placeholder=${this.localize.term('schemeWeaver_searchSchemaTypes')}
            @input=${this._handleSearch}
            .value=${this._searchTerm}
            class="search-input"
            label=${this.localize.term('schemeWeaver_searchSchemaTypes')}
          >
            <uui-icon name="icon-search" slot="prepend"></uui-icon>
          </uui-input>

          ${this._loading
            ? html`
                <div class="loading">
                  <uui-loader-circle></uui-loader-circle>
                  <p>${this.localize.term('schemeWeaver_loadingSchemaTypes')}</p>
                </div>
              `
            : html`
                <div class="schema-list" role="listbox" aria-label=${this.localize.term('schemeWeaver_schemaTypesListLabel')}>
                  ${this._groupedTypes.map(
                    (group) => html`
                      <div class="schema-group">
                        <h4 class="group-header">${group.parent}</h4>
                        ${group.types.map(
                          (type) => html`
                            <div
                              class="schema-item ${this._selectedType === type.name ? 'selected' : ''}"
                              role="option"
                              tabindex="0"
                              aria-selected="${this._selectedType === type.name}"
                              @click=${() => this._handleSelect(type.name)}
                              @keydown=${(e: KeyboardEvent) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); this._handleSelect(type.name); } }}
                            >
                              <div class="schema-item-header">
                                <strong>${type.name}</strong>
                                ${type.parentTypeName
                                  ? html`<small class="parent-label">${this.localize.term('schemeWeaver_extends')} ${type.parentTypeName}</small>`
                                  : ''}
                              </div>
                              <p class="schema-description">${type.description}</p>
                              ${type.propertyCount > 0
                                ? html`<small class="property-count">${this.localize.term('schemeWeaver_schemaPropertyCount', type.propertyCount)}</small>`
                                : ''}
                            </div>
                          `
                        )}
                      </div>
                    `
                  )}

                  ${this._schemaTypes.length === 0
                    ? html`<p class="no-results">${this.localize.term('schemeWeaver_noSchemaTypes')}</p>`
                    : ''}
                </div>
              `}
        </uui-box>

        <div slot="actions">
          <uui-button look="secondary" @click=${this._handleClose} label=${this.localize.term('schemeWeaver_cancel')}>
            ${this.localize.term('schemeWeaver_cancel')}
          </uui-button>
          <uui-button look="primary" @click=${this._handleSubmit} ?disabled=${!this._selectedType} label=${this.localize.term('general_select')}>
            ${this.localize.term('general_select')}
          </uui-button>
          ${!this._selectedType
            ? html`<small class="disabled-hint">${this.localize.term('schemeWeaver_selectASchemaType')}</small>`
            : nothing}
        </div>
      </umb-body-layout>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .search-input {
        width: 100%;
        margin-bottom: var(--uui-size-space-4);
      }

      .loading {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--uui-size-space-3);
        padding: var(--uui-size-space-6);
      }

      .schema-list {
        max-height: 500px;
        overflow-y: auto;
      }

      .schema-group {
        margin-bottom: var(--uui-size-space-4);
      }

      .group-header {
        color: var(--uui-color-text-alt);
        font-size: 0.8rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        padding: var(--uui-size-space-2) var(--uui-size-space-3);
        border-bottom: 1px solid var(--uui-color-border);
        margin: 0 0 var(--uui-size-space-2) 0;
      }

      .schema-item {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        cursor: pointer;
        transition: background-color 0.15s ease;
        border: 2px solid transparent;
      }

      .schema-item:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .schema-item.selected {
        background-color: var(--uui-color-selected);
        border-color: var(--uui-color-focus);
      }

      .schema-item-header {
        display: flex;
        align-items: baseline;
        gap: var(--uui-size-space-3);
      }

      .parent-label {
        color: var(--uui-color-text-alt);
        font-style: italic;
      }

      .schema-description {
        margin: var(--uui-size-space-1) 0 0 0;
        color: var(--uui-color-text-alt);
        font-size: 0.875rem;
        line-height: 1.4;
      }

      .property-count {
        color: var(--uui-color-text-alt);
      }

      .no-results {
        text-align: center;
        color: var(--uui-color-text-alt);
        padding: var(--uui-size-space-6);
      }

      .disabled-hint {
        display: block;
        color: var(--uui-color-text-alt);
        font-size: 0.8rem;
        margin-top: var(--uui-size-space-2);
      }

      .ai-section {
        margin-bottom: var(--uui-size-space-4);
      }

      .ai-suggestions {
        margin-bottom: var(--uui-size-space-2);
      }

      .ai-suggestions-header {
        color: var(--uui-color-text-alt);
        font-size: 0.8rem;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        margin: 0 0 var(--uui-size-space-2) 0;
      }

      .ai-suggestion-card {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-radius: var(--uui-border-radius);
        border: 2px solid var(--uui-color-positive);
        background: var(--uui-color-positive-standalone, rgba(27, 183, 110, 0.05));
        cursor: pointer;
        margin-bottom: var(--uui-size-space-2);
        transition: background-color 0.15s ease, border-color 0.15s ease;
      }

      .ai-suggestion-card:hover {
        background-color: var(--uui-color-surface-alt);
      }

      .ai-suggestion-card.selected {
        background-color: var(--uui-color-selected);
        border-color: var(--uui-color-focus);
      }

      .ai-suggestion-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--uui-size-space-3);
      }

      .ai-suggestion-reasoning {
        margin: var(--uui-size-space-1) 0 0 0;
        color: var(--uui-color-text-alt);
        font-size: 0.85rem;
        line-height: 1.4;
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
