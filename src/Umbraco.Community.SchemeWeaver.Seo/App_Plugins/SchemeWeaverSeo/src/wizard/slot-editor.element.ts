import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, nothing, customElement, property, state } from '@umbraco-cms/backoffice/external/lit';
import type { SeoSlot, SeoSlotValue } from '../api/seo-types.js';
import type { ContentTypeProperty } from '../core-bridge/types.js';
import '../components/confidence-tag.element.js';

type SourceChoice = 'none' | 'fixed' | 'siteIdentity' | 'nested' | `property:${string}`;

/**
 * One slot of the wizard's Fields step: a friendly label, a source picker (page fields filtered
 * to compatible editors, built-ins, a fixed value, the site identity, or "fill in parts" for
 * entity slots) and the inputs the chosen source needs. Emits `slot-change` with the new value.
 */
@customElement('schemeweaver-seo-slot-editor')
export class SeoSlotEditorElement extends UmbLitElement {
  @property({ type: Object }) definition!: SeoSlot;
  @property({ type: Object }) value: SeoSlotValue = { kind: 'none' };
  @property({ type: Array }) properties: ContentTypeProperty[] = [];
  @property({ type: Boolean }) siteIdentityConfigured = false;
  @property({ type: Boolean }) showAllFields = false;
  @property({ type: Boolean }) nestedLevel = false;

  @state() private _showAllHere = false;

  get #choice(): SourceChoice {
    switch (this.value.kind) {
      case 'property':
        return `property:${this.value.propertyAlias ?? ''}`;
      case 'fixed':
        return 'fixed';
      case 'siteIdentity':
        return 'siteIdentity';
      case 'nested':
        return 'nested';
      default:
        return 'none';
    }
  }

  #emit(value: SeoSlotValue) {
    this.value = value;
    this.dispatchEvent(new CustomEvent('slot-change', { detail: { schemaProperty: this.definition.schemaProperty, value }, bubbles: true, composed: true }));
  }

  #onChoice(e: Event) {
    const choice = (e.target as HTMLSelectElement).value as SourceChoice;
    if (choice === 'none') return this.#emit({ kind: 'none' });
    if (choice === 'fixed') return this.#emit({ kind: 'fixed', fixedValue: this.definition.fixedValueOptions[0]?.value ?? '' });
    if (choice === 'siteIdentity') return this.#emit({ kind: 'siteIdentity', targetPieceKey: this.definition.siteIdentityPieceKey ?? 'organization' });
    if (choice === 'nested') return this.#emit({ kind: 'nested', nested: this.value.nested ?? {} });
    const alias = choice.slice('property:'.length);
    const editor = this.properties.find((p) => p.alias === alias)?.editorAlias ?? null;
    this.#emit({ kind: 'property', propertyAlias: alias, editorAlias: editor });
  }

  #compatible(p: ContentTypeProperty): boolean {
    if (this.showAllFields || this._showAllHere) return true;
    if (this.definition.builtIns.includes(p.alias)) return true;
    if (p.alias.startsWith('__')) return false;
    return this.definition.acceptedEditorAliases.includes(p.editorAlias);
  }

  #options() {
    const t = (k: string) => this.localize.term(`schemeWeaverSeo_${k}`);
    const current = this.#choice;
    const options: Array<{ name: string; value: string; group?: string; selected?: boolean }> = [
      { name: t('wizardSourceNone'), value: 'none', selected: current === 'none' },
    ];
    const builtIns = this.properties.filter((p) => p.alias.startsWith('__') && this.definition.builtIns.includes(p.alias));
    const fields = this.properties.filter((p) => !p.alias.startsWith('__') && this.#compatible(p));
    for (const p of fields) {
      options.push({ name: `${p.name} (${p.alias})`, value: `property:${p.alias}`, group: t('wizardSourceFields'), selected: current === `property:${p.alias}` });
    }
    // A stored alias that is no longer compatible/visible still has to be selectable.
    if (this.value.kind === 'property' && this.value.propertyAlias && !options.some((o) => o.value === `property:${this.value.propertyAlias}`)) {
      options.push({ name: this.value.propertyAlias, value: `property:${this.value.propertyAlias}`, group: t('wizardSourceFields'), selected: true });
    }
    for (const p of builtIns) {
      options.push({ name: p.name, value: `property:${p.alias}`, group: t('wizardSourceBuiltIns'), selected: current === `property:${p.alias}` });
    }
    if (this.definition.kind !== 'image' && this.definition.kind !== 'list') {
      options.push({ name: t('wizardSourceFixed'), value: 'fixed', selected: current === 'fixed' });
    }
    if (this.definition.siteIdentityPieceKey && this.siteIdentityConfigured) {
      options.push({ name: t('wizardSourceIdentity'), value: 'siteIdentity', selected: current === 'siteIdentity' });
    }
    if (this.definition.nested) {
      options.push({ name: t('wizardSourceNested'), value: 'nested', selected: current === 'nested' });
    }
    return options;
  }

  render() {
    const slot = this.definition;
    const readOnly = this.value.kind === 'blocks' || this.value.kind === 'advanced';
    return html`
      <div class="row ${slot.importance}" data-mark="schemeweaver-seo:slot:${slot.schemaProperty}">
        <div class="label">
          <strong>${slot.label}</strong>
          ${slot.importance === 'required' ? html`<uui-tag look="secondary" color="danger">${this.localize.term('schemeWeaverSeo_required')}</uui-tag>` : nothing}
          ${slot.help ? html`<div class="help">${slot.help}</div>` : nothing}
          <div class="help code">${slot.schemaProperty}</div>
        </div>
        <div class="editor">
          ${readOnly
            ? html`<uui-tag look="secondary">${this.value.kind === 'blocks' ? this.localize.term('schemeWeaverSeo_wizardBlocksManaged') : this.localize.term('schemeWeaverSeo_statusDeveloperManaged')}</uui-tag>
                <span class="help">${this.value.summary ?? ''}</span>`
            : html`<div class="source">
                  <uui-select .options=${this.#options()} @change=${this.#onChoice} data-mark="schemeweaver-seo:slot-source:${slot.schemaProperty}"></uui-select>
                  <schemeweaver-seo-confidence-tag .confidence=${this.value.confidence}></schemeweaver-seo-confidence-tag>
                  ${!this.showAllFields && !this._showAllHere
                    ? html`<uui-button look="link" compact label=${this.localize.term('schemeWeaverSeo_wizardShowAllFields')} @click=${() => (this._showAllHere = true)}>${this.localize.term('schemeWeaverSeo_wizardShowAllFields')}</uui-button>`
                    : nothing}
                </div>
                ${this.#renderValueInput()}`}
        </div>
      </div>`;
  }

  #renderValueInput() {
    const v = this.value;
    if (v.kind === 'fixed') {
      if (this.definition.fixedValueOptions.length) {
        return html`<uui-select
          .options=${this.definition.fixedValueOptions.map((o) => ({ name: o.label, value: o.value, selected: o.value === v.fixedValue }))}
          @change=${(e: Event) => this.#emit({ kind: 'fixed', fixedValue: (e.target as HTMLSelectElement).value })}></uui-select>`;
      }
      return html`<uui-input .value=${v.fixedValue ?? ''} placeholder=${this.localize.term('schemeWeaverSeo_wizardFixedValuePlaceholder')}
        @input=${(e: Event) => this.#emit({ kind: 'fixed', fixedValue: (e.target as HTMLInputElement).value })}></uui-input>`;
    }
    if (v.kind === 'nested' && this.definition.nested) {
      return html`<div class="nested">
        ${this.definition.nested.slots.map(
          (sub) => html`<schemeweaver-seo-slot-editor
            nestedLevel
            .definition=${sub}
            .value=${v.nested?.[sub.schemaProperty] ?? { kind: 'none' }}
            .properties=${this.properties}
            .showAllFields=${this.showAllFields}
            @slot-change=${(e: CustomEvent<{ schemaProperty: string; value: SeoSlotValue }>) => {
              e.stopPropagation();
              this.#emit({ kind: 'nested', nested: { ...(v.nested ?? {}), [e.detail.schemaProperty]: e.detail.value } });
            }}></schemeweaver-seo-slot-editor>`,
        )}
      </div>`;
    }
    return nothing;
  }

  static styles = css`
    .row { display: grid; grid-template-columns: minmax(180px, 1fr) 2fr; gap: var(--uui-size-space-4); padding: var(--uui-size-space-3) 0; border-bottom: 1px solid var(--uui-color-divider); }
    .row:last-child { border-bottom: none; }
    .label { display: flex; flex-direction: column; gap: var(--uui-size-space-1); align-items: flex-start; }
    .help { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
    .code { font-family: monospace; }
    .editor { display: flex; flex-direction: column; gap: var(--uui-size-space-2); }
    .source { display: flex; align-items: center; gap: var(--uui-size-space-2); }
    uui-select, uui-input { width: 100%; }
    .source uui-select { flex: 1; }
    .nested { border-left: 3px solid var(--uui-color-border); padding-left: var(--uui-size-space-4); }
    .nested .row { grid-template-columns: minmax(140px, 1fr) 2fr; }
  `;
}

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-seo-slot-editor': SeoSlotEditorElement;
  }
}
