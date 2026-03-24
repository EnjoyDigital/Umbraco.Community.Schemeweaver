import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property, state, repeat, nothing } from '@umbraco-cms/backoffice/external/lit';
import type { UUIComboboxElement, UUIComboboxEvent } from '@umbraco-cms/backoffice/external/uui';

/** Built-in property alias display names */
const BUILT_IN_DISPLAY_NAMES: Record<string, string> = {
  '__url': 'URL (Built-in)',
  '__name': 'Name (Built-in)',
  '__createDate': 'Create Date (Built-in)',
  '__updateDate': 'Update Date (Built-in)',
};

/** Returns a display-friendly name for a property alias */
export function formatPropertyDisplayName(alias: string): string {
  return BUILT_IN_DISPLAY_NAMES[alias] ?? alias;
}

@customElement('schemeweaver-property-combobox')
export class PropertyComboboxElement extends UmbLitElement {
  @property({ type: Array })
  public set properties(value: string[]) {
    this.#properties = value;
    this._filteredProperties = value;
  }
  public get properties(): string[] {
    return this.#properties;
  }
  #properties: string[] = [];

  @property({ type: String })
  value = '';

  @property({ type: String })
  placeholder = '';

  @property({ type: String })
  label = '';

  @state()
  private _filteredProperties: string[] = [];

  private _onSearch(event: UUIComboboxEvent) {
    const searchTerm = (event.currentTarget as UUIComboboxElement)?.search ?? '';
    if (!searchTerm) {
      this._filteredProperties = this.properties;
      return;
    }
    const pattern = new RegExp(searchTerm.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i');
    this._filteredProperties = this.properties.filter(
      (p) => pattern.test(p) || pattern.test(formatPropertyDisplayName(p)),
    );
  }

  private _onChange(event: UUIComboboxEvent) {
    const newValue = ((event.currentTarget as UUIComboboxElement)?.value as string) ?? '';
    this.value = newValue;
    this.dispatchEvent(
      new CustomEvent('change', {
        detail: { value: newValue },
        bubbles: true,
        composed: true,
      }),
    );
  }

  render() {
    return html`
      <uui-combobox
        .value=${this.value}
        label=${this.label || this.placeholder}
        @search=${this._onSearch}
        @change=${this._onChange}
      >
        <uui-combobox-list>
          ${this._filteredProperties.length === 0
            ? html`<div class="no-results">${this.localize.term('schemeWeaver_noProperties')}</div>`
            : repeat(
                this._filteredProperties,
                (p) => p,
                (p) => html`
                  <uui-combobox-list-option .value=${p} .displayValue=${formatPropertyDisplayName(p)}>
                    ${formatPropertyDisplayName(p)}
                  </uui-combobox-list-option>
                `,
              )}
        </uui-combobox-list>
      </uui-combobox>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      uui-combobox {
        width: 100%;
      }

      .no-results {
        padding: var(--uui-size-space-3);
        color: var(--uui-color-text-alt);
        font-style: italic;
        text-align: center;
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
