import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state } from '@umbraco-cms/backoffice/external/lit';
import type { UUIComboboxElement, UUIComboboxEvent } from '@umbraco-cms/backoffice/external/uui';

/** Built-in property alias display name map */
const BUILT_IN_DISPLAY_NAMES: Record<string, string> = {
  '__url': 'URL (Built-in)',
  '__name': 'Name (Built-in)',
  '__createDate': 'Create Date (Built-in)',
  '__updateDate': 'Update Date (Built-in)',
};

/** Returns a display-friendly name for a property alias, with built-in indicator */
export function formatPropertyName(alias: string): string {
  return BUILT_IN_DISPLAY_NAMES[alias] ?? alias;
}

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

  private _filterList(searchTerm: string): string[] {
    if (!searchTerm) return this.properties;
    const regex = new RegExp(searchTerm, 'i');
    return this.properties.filter(
      (alias) => regex.test(alias) || regex.test(formatPropertyName(alias)),
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
              <uui-combobox-list-option .value=${alias} .displayValue=${formatPropertyName(alias)}>
                ${formatPropertyName(alias)}
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
