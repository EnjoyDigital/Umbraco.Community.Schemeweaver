import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property } from '@umbraco-cms/backoffice/external/lit';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';

/**
 * A Schema.org type field for nested block routing: a free-text input (power users can type an
 * exact type) paired with a browse button that opens the full searchable schema-type picker.
 *
 * The bare text input alone gave no affordance — at deep nesting levels users had no way to
 * discover which type names are valid. This wraps the same value model with a real picker.
 *
 * @element schemeweaver-schema-type-input
 * @fires change - whenever the value changes (typed or picked). Read the new value from `.value`.
 */
@customElement('schemeweaver-schema-type-input')
export class SchemaTypeInputElement extends UmbLitElement {
  /** The current Schema.org type name. */
  @property({ type: String })
  value = '';

  /** Optional content type alias forwarded to the picker for AI suggestions. */
  @property({ type: String, attribute: 'content-type-alias' })
  contentTypeAlias = '';

  #modalManagerContext?: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE;

  constructor() {
    super();
    this.consumeContext(UMB_MODAL_MANAGER_CONTEXT, (ctx) => {
      this.#modalManagerContext = ctx;
    });
  }

  private _emit(value: string) {
    this.value = value;
    this.dispatchEvent(new CustomEvent('change', { bubbles: false, composed: false }));
  }

  private _onInputChange(e: Event) {
    e.stopPropagation();
    this._emit((e.target as HTMLInputElement).value);
  }

  private async _browse() {
    if (!this.#modalManagerContext) return;
    const result = await this.#modalManagerContext
      .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
        data: { contentTypeAlias: this.contentTypeAlias },
      })
      .onSubmit()
      .catch(() => null);
    if (result?.schemaType) this._emit(result.schemaType);
  }

  override render() {
    return html`
      <div class="wrap">
        <uui-input
          class="schema-type-input"
          .value=${this.value}
          placeholder=${this.localize.term('schemeWeaver_nestedSchemaType')}
          label=${this.localize.term('schemeWeaver_nestedSchemaType')}
          @change=${this._onInputChange}
        ></uui-input>
        <uui-button
          compact
          look="secondary"
          class="browse-btn"
          label=${this.localize.term('schemeWeaver_browseSchemaTypes')}
          title=${this.localize.term('schemeWeaver_browseSchemaTypes')}
          @click=${this._browse}
        >
          <uui-icon name="icon-search"></uui-icon>
        </uui-button>
      </div>
    `;
  }

  static override styles = [
    css`
      :host {
        display: inline-block;
      }
      .wrap {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-1);
      }
      .schema-type-input {
        min-width: 150px;
      }
    `,
  ];
}

export default SchemaTypeInputElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-schema-type-input': SchemaTypeInputElement;
  }
}
