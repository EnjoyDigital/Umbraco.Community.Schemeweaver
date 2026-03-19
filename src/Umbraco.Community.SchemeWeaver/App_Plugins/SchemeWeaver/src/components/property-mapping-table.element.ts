import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, property } from '@umbraco-cms/backoffice/external/lit';

/**
 * UI row model for the property mapping table.
 * Combines fields from PropertyMappingDto and PropertyMappingSuggestion for display/editing.
 */
export interface PropertyMappingRow {
  schemaPropertyName: string;
  schemaPropertyType: string;
  sourceType: string;
  contentTypePropertyAlias: string;
  sourceContentTypeAlias: string;
  staticValue: string;
  confidence: number | null;
}

@customElement('schemeweaver-property-mapping-table')
export class PropertyMappingTableElement extends UmbLitElement {
  @property({ type: Array })
  mappings: PropertyMappingRow[] = [];

  @property({ type: Array })
  availableProperties: string[] = [];

  @property({ type: Boolean })
  readonly = false;

  /** Source type values matching C# (lowercase) */
  private get _sourceTypes() {
    return [
      { value: 'property', label: this.localize.term('schemeWeaver_sourceCurrentNode') },
      { value: 'static', label: this.localize.term('schemeWeaver_sourceStaticValue') },
      { value: 'parent', label: this.localize.term('schemeWeaver_sourceParentNode') },
      { value: 'ancestor', label: this.localize.term('schemeWeaver_sourceAncestorNode') },
      { value: 'sibling', label: this.localize.term('schemeWeaver_sourceSiblingNode') },
    ];
  }

  private _dispatchChange() {
    this.dispatchEvent(
      new CustomEvent('mappings-changed', {
        detail: { mappings: this.mappings },
        bubbles: true,
        composed: true,
      })
    );
  }

  private _handleSourceTypeChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], sourceType: value, contentTypePropertyAlias: '', staticValue: '', sourceContentTypeAlias: '' };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handlePropertyChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], contentTypePropertyAlias: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleStaticValueChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], staticValue: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  private _handleSourceContentTypeAliasChange(index: number, value: string) {
    const updated = [...this.mappings];
    updated[index] = { ...updated[index], sourceContentTypeAlias: value };
    this.mappings = updated;
    this._dispatchChange();
  }

  /** Confidence is an integer 0-100 from C# auto-mapper */
  private _renderConfidenceBadge(confidence: number | null) {
    if (confidence === null) return '';
    if (confidence >= 80) return html`<uui-badge color="positive">${this.localize.term('schemeWeaver_confidenceHigh')}</uui-badge>`;
    if (confidence >= 50) return html`<uui-badge color="warning">${this.localize.term('schemeWeaver_confidenceMedium')}</uui-badge>`;
    return html`<uui-badge color="danger">${this.localize.term('schemeWeaver_confidenceLow')}</uui-badge>`;
  }

  render() {
    return html`
      <uui-table aria-label=${this.localize.term('schemeWeaver_propertyMappings')}>
        <uui-table-head>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_schemaProperty')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_type')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_source')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_value')}</uui-table-head-cell>
          <uui-table-head-cell>${this.localize.term('schemeWeaver_confidence')}</uui-table-head-cell>
        </uui-table-head>

        ${this.mappings.map(
          (mapping, index) => html`
            <uui-table-row>
              <uui-table-cell>
                <strong>${mapping.schemaPropertyName}</strong>
              </uui-table-cell>
              <uui-table-cell>
                <small class="type-label">${mapping.schemaPropertyType}</small>
              </uui-table-cell>
              <uui-table-cell>
                ${this.readonly
                  ? html`<span>${mapping.sourceType}</span>`
                  : html`
                      <uui-select
                        label=${this.localize.term('schemeWeaver_source')}
                        .options=${this._sourceTypes.map((st) => ({
                          name: st.label,
                          value: st.value,
                          selected: mapping.sourceType === st.value,
                        }))}
                        @change=${(e: Event) =>
                          this._handleSourceTypeChange(index, (e.target as HTMLSelectElement).value)}
                      ></uui-select>
                    `}
              </uui-table-cell>
              <uui-table-cell>
                ${this.readonly
                  ? html`<span>${mapping.sourceType === 'static' ? mapping.staticValue : mapping.contentTypePropertyAlias}</span>`
                  : this._renderValueInput(mapping, index)}
              </uui-table-cell>
              <uui-table-cell>
                ${this._renderConfidenceBadge(mapping.confidence)}
              </uui-table-cell>
            </uui-table-row>
          `
        )}
      </uui-table>
    `;
  }

  private _renderValueInput(mapping: PropertyMappingRow, index: number) {
    if (mapping.sourceType === 'static') {
      return html`
        <uui-input
          .value=${mapping.staticValue}
          @input=${(e: Event) => this._handleStaticValueChange(index, (e.target as HTMLInputElement).value)}
          placeholder=${this.localize.term('schemeWeaver_enterStaticValue')}
          label=${this.localize.term('schemeWeaver_staticValueFor') + ' ' + mapping.schemaPropertyName}
        ></uui-input>
      `;
    }

    const needsSourceContentTypeAlias = mapping.sourceType === 'ancestor' || mapping.sourceType === 'sibling';

    return html`
      <div class="value-inputs">
        ${needsSourceContentTypeAlias
          ? html`
              <uui-input
                .value=${mapping.sourceContentTypeAlias}
                @input=${(e: Event) => this._handleSourceContentTypeAliasChange(index, (e.target as HTMLInputElement).value)}
                placeholder=${this.localize.term('schemeWeaver_contentTypeAliasPlaceholder')}
                label=${this.localize.term('schemeWeaver_contentTypeAlias')}
                class="content-type-input"
              ></uui-input>
            `
          : ''}
        <uui-select
          label=${this.localize.term('schemeWeaver_value') + ' ' + mapping.schemaPropertyName}
          .options=${this.availableProperties.map((p) => ({
            name: p,
            value: p,
            selected: mapping.contentTypePropertyAlias === p,
          }))}
          @change=${(e: Event) => this._handlePropertyChange(index, (e.target as HTMLSelectElement).value)}
        ></uui-select>
      </div>
    `;
  }

  static styles = [
    css`
      :host {
        display: block;
      }

      .type-label {
        color: var(--uui-color-text-alt);
        font-family: monospace;
      }

      .value-inputs {
        display: flex;
        flex-direction: column;
        gap: var(--uui-size-space-2);
      }

      .content-type-input {
        font-size: 0.85rem;
      }

      uui-select {
        min-width: 150px;
      }
    `,
  ];
}

export default PropertyMappingTableElement;

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-property-mapping-table': PropertyMappingTableElement;
  }
}
