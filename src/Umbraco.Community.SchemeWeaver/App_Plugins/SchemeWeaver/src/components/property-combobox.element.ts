import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state } from '@umbraco-cms/backoffice/external/lit';
import type { UUIComboboxElement, UUIComboboxEvent } from '@umbraco-cms/backoffice/external/uui';

/**
 * Maps built-in property aliases to their localisation key + English fallback.
 * The fallback is used when no localisation provider is registered (e.g. in
 * isolated component tests) so the dropdown still shows readable text.
 */
const BUILT_IN_DISPLAY: Record<string, { key: string; fallback: string }> = {
  '__url': { key: 'schemeWeaver_builtInUrl', fallback: 'URL (Built-in)' },
  '__name': { key: 'schemeWeaver_builtInName', fallback: 'Name (Built-in)' },
  '__createDate': { key: 'schemeWeaver_builtInCreateDate', fallback: 'Create Date (Built-in)' },
  '__updateDate': { key: 'schemeWeaver_builtInUpdateDate', fallback: 'Update Date (Built-in)' },
};

/**
 * Searchable property combobox component.
 * Replaces uui-select for property dropdowns with a filterable uui-combobox.
 *
 * @element schemeweaver-property-combobox
 * @fires change - Dispatched when the selected value changes
 */
@customElement('schemeweaver-property-combobox')
export class PropertyComboboxElement extends UmbLitElement {
  @property({ type: Array })
  properties: string[] = [];

  @property({ type: String })
  value = '';

  @property({ type: String })
  placeholder = '';

  @property({ type: String })
  label = '';

  @state()
  private _filteredProperties: string[] = [];

  override connectedCallback() {
    super.connectedCallback();
    this._filteredProperties = this.properties;
  }

  override updated(changedProperties: Map<string, unknown>) {
    super.updated(changedProperties);
    if (changedProperties.has('properties')) {
      this._filteredProperties = this._filterList(this._lastSearch);
    }
  }

  private _lastSearch = '';

  #onSearch(event: UUIComboboxEvent) {
    event.stopPropagation();
    const searchTerm = (event.currentTarget as UUIComboboxElement)?.search ?? '';
    this._lastSearch = searchTerm;
    this._filteredProperties = this._filterList(searchTerm);
  }

  /** Resolves a property alias to a display-friendly name using the localisation map. */
  #formatPropertyName(alias: string): string {
    const entry = BUILT_IN_DISPLAY[alias];
    if (!entry) return alias;
    // `localize.term` returns the key itself when no provider is registered;
    // detect that case and fall back to the English string.
    const localised = this.localize.term(entry.key);
    return localised && localised !== entry.key ? localised : entry.fallback;
  }

  private _filterList(searchTerm: string): string[] {
    if (!searchTerm) return this.properties;
    const regex = new RegExp(searchTerm, 'i');
    return this.properties.filter(
      (alias) => regex.test(alias) || regex.test(this.#formatPropertyName(alias)),
    );
  }

  #onChange(event: UUIComboboxEvent) {
    event.stopPropagation();
    const newValue = ((event.currentTarget as UUIComboboxElement)?.value as string) ?? '';
    this.value = newValue;
    this.dispatchEvent(
      new CustomEvent('change', {
        detail: { value: newValue },
        bubbles: true,
        composed: false,
      }),
    );
  }

  override render() {
    return html`
      <uui-combobox
        .value=${this.value}
        label=${this.label || this.localize.term('schemeWeaver_selectProperty')}
        @search=${this.#onSearch}
        @change=${this.#onChange}>
        <uui-combobox-list>
          ${this._filteredProperties.map(
            (alias) => html`
              <uui-combobox-list-option .value=${alias} .displayValue=${this.#formatPropertyName(alias)}>
                ${this.#formatPropertyName(alias)}
              </uui-combobox-list-option>
            `,
          )}
        </uui-combobox-list>
      </uui-combobox>
    `;
  }

  static override styles = [
    css`
      :host {
        display: block;
      }

      uui-combobox {
        width: 100%;
        min-width: 150px;
      }
    `,
  ];
}

export default PropertyComboboxElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-combobox': PropertyComboboxElement;
  }
}
