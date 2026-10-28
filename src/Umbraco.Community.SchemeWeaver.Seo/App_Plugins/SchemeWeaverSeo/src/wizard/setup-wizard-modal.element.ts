import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { css, html, nothing, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import type { SeoSetupWizardModalData, SeoSetupWizardModalValue } from './setup-wizard-modal.token.js';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../context/seo.context-token.js';
import type { SchemeWeaverSeoContext } from '../context/seo.context.js';
import type { SeoProjection, SeoSamplePage, SeoSlotValue, SeoTemplate, SeoTemplateCatalogue, SeoTemplateVariant } from '../api/seo-types.js';
import type { ContentTypeProperty, JsonLdPreviewResponse } from '../core-bridge/types.js';
import { SchemeWeaverMappingChangedEvent } from '../core-bridge/mapping-changed-event.js';
import './slot-editor.element.js';

type Step = 0 | 1 | 2;

const FAMILY_ORDER = ['content', 'commerce', 'events', 'business', 'learning', 'media', 'other'];

/** Three steps: pick a kind, confirm the fields, save and check against a real page. */
@customElement('schemeweaver-seo-setup-wizard-modal')
export class SeoSetupWizardModalElement extends UmbModalBaseElement<SeoSetupWizardModalData, SeoSetupWizardModalValue> {
  #context?: SchemeWeaverSeoContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state() private _step: Step = 0;
  @state() private _catalogue?: SeoTemplateCatalogue;
  @state() private _showRetired = false;
  @state() private _templateId?: string;
  @state() private _schemaType?: string;
  @state() private _properties: ContentTypeProperty[] = [];
  @state() private _projection?: SeoProjection;
  @state() private _values = new Map<string, SeoSlotValue>();
  @state() private _showAllFields = false;
  @state() private _siteIdentityConfigured = false;
  @state() private _busy = false;
  @state() private _saved = false;
  @state() private _samplePages: SeoSamplePage[] = [];
  @state() private _sampleKey?: string;
  @state() private _preview: JsonLdPreviewResponse | null = null;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (c) => (this.#notifications = c));
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, async (context) => {
      if (!context) return;
      this.#context = context;
      const [catalogue, serverContext, properties, status] = await Promise.all([
        context.loadCatalogue(),
        context.loadServerContext(),
        context.getContentTypeProperties(this.data!.contentTypeAlias),
        context.dataSource.getContentType(this.data!.contentTypeAlias),
      ]);
      this._catalogue = catalogue;
      this._siteIdentityConfigured = serverContext?.siteIdentityConfigured ?? false;
      this._properties = properties ?? [];
      // Revisiting a set-up type: start from its kind.
      if (status?.mapping?.templateId) {
        this._templateId = status.mapping.templateId;
        this._schemaType = status.mapping.schemaTypeName;
      } else {
        this._templateId = this.#suggestedTemplate()?.id;
      }
    });
  }

  get #template(): SeoTemplate | undefined {
    return this._catalogue?.templates.find((t) => t.id === this._templateId);
  }

  get #variants(): SeoTemplateVariant[] {
    const t = this.#template;
    if (!t) return [];
    if (t.useBusinessTypes) return this._catalogue?.businessTypes.map((b) => ({ schemaType: b.schemaType, label: b.label })) ?? [];
    return t.variants;
  }

  #suggestedTemplate(): SeoTemplate | undefined {
    const haystack = `${this.data?.contentTypeAlias ?? ''} ${this.data?.contentTypeName ?? ''}`.toLowerCase();
    return this._catalogue?.templates.find((t) => t.googleFeature.status !== 'retired' && t.aliasHints.some((h) => haystack.includes(h)));
  }

  #isSuggested(t: SeoTemplate) {
    return this.#suggestedTemplate()?.id === t.id;
  }

  // ---------------------------------------------------------------- navigation

  async #next() {
    if (this._step === 0) {
      await this.#loadProjection();
      if (this._projection) this._step = 1;
    } else if (this._step === 1) {
      this._step = 2;
    }
  }

  #back() {
    if (this._step > 0) this._step = (this._step - 1) as Step;
  }

  async #loadProjection() {
    if (!this.#context || !this._templateId) return;
    this._busy = true;
    try {
      const projection = await this.#context.setup(this.data!.contentTypeAlias, {
        templateId: this._templateId,
        schemaTypeName: this._schemaType ?? null,
      });
      if (projection) {
        this._projection = projection;
        this._values = new Map(projection.slots.map((s) => [s.slot.schemaProperty, s.value]));
      }
    } finally {
      this._busy = false;
    }
  }

  async #saveAndPreview() {
    if (!this.#context || !this._templateId) return;
    this._busy = true;
    try {
      const saved = await this.#context.saveMapping(this.data!.contentTypeAlias, {
        templateId: this._templateId,
        schemaTypeName: this._schemaType ?? null,
        slots: Object.fromEntries(this._values),
      });
      if (!saved) {
        this.#notifications?.peek('danger', { data: { message: this.localize.term('schemeWeaverSeo_wizardSaveFailed') } });
        return;
      }
      this._saved = true;
      this.#notifications?.peek('positive', { data: { message: this.localize.term('schemeWeaverSeo_wizardSaved') } });
      await this.#announceChange();
      this._samplePages = (await this.#context.getSamplePages(this.data!.contentTypeAlias)) ?? [];
      this._sampleKey = this._samplePages[0]?.key;
      await this.#loadPreview();
    } finally {
      this._busy = false;
    }
  }

  async #announceChange() {
    // Tells an open developer Schema.org tab to re-read instead of writing stale rows back.
    try {
      const events = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
      events?.dispatchEvent(new SchemeWeaverMappingChangedEvent(this.data!.contentTypeKey));
    } catch {
      /* not available outside the backoffice shell */
    }
  }

  async #loadPreview() {
    this._preview = null;
    if (!this.#context || !this._sampleKey) return;
    const summary = await this.#context.getPageSummary(this._sampleKey);
    this._preview = summary?.preview ?? null;
  }

  #finish() {
    this.value = { saved: this._saved, schemaTypeName: this._schemaType ?? this.#template?.schemaType };
    this.modalContext?.submit();
  }

  // ---------------------------------------------------------------- render

  render() {
    const t = (k: string, ...args: unknown[]) => this.localize.term(`schemeWeaverSeo_${k}`, ...args);
    return html`
      <umb-body-layout headline=${t('wizardTitle', this.data?.contentTypeName ?? '')}>
        <div slot="header" class="steps">
          ${(['wizardStepKind', 'wizardStepFields', 'wizardStepCheck'] as const).map(
            (key, i) => html`<span class="step ${i === this._step ? 'active' : ''} ${i < this._step ? 'done' : ''}">${i + 1}. ${t(key)}</span>`,
          )}
        </div>
        <div id="main">
          ${!this._catalogue ? html`<uui-loader-circle></uui-loader-circle>` : this.#renderStep()}
        </div>
        <div slot="actions" class="actions">
          <uui-button label=${t('cancel')} @click=${() => this.modalContext?.reject()}></uui-button>
          ${this._step > 0 ? html`<uui-button look="secondary" data-mark="schemeweaver-seo:wizard-back" label=${t('back')} @click=${this.#back}></uui-button>` : nothing}
          ${this._step < 2
            ? html`<uui-button look="primary" data-mark="schemeweaver-seo:wizard-next" label=${t('next')}
                .disabled=${!this._templateId || this._busy} .state=${this._busy ? 'waiting' : undefined} @click=${this.#next}></uui-button>`
            : html`<uui-button look="primary" color="positive" data-mark="schemeweaver-seo:wizard-save" label=${t('wizardSaveAndPreview')}
                  .disabled=${this._busy} .state=${this._busy ? 'waiting' : undefined} @click=${this.#saveAndPreview}></uui-button>
                <uui-button look="primary" data-mark="schemeweaver-seo:wizard-finish" label=${t('finish')} .disabled=${!this._saved} @click=${this.#finish}></uui-button>`}
        </div>
      </umb-body-layout>`;
  }

  #renderStep() {
    switch (this._step) {
      case 0:
        return this.#renderKindStep();
      case 1:
        return this.#renderFieldsStep();
      default:
        return this.#renderCheckStep();
    }
  }

  #renderKindStep() {
    const t = (k: string) => this.localize.term(`schemeWeaverSeo_${k}`);
    const templates = (this._catalogue?.templates ?? []).filter((x) => this._showRetired || x.googleFeature.status !== 'retired');
    const families = [...new Set(templates.map((x) => x.family))].sort((a, b) => FAMILY_ORDER.indexOf(a) - FAMILY_ORDER.indexOf(b));
    return html`
      <p class="intro">${t('wizardKindIntro')}</p>
      ${families.map(
        (family) => html`<div class="family">
          <h4>${family.charAt(0).toUpperCase()}${family.slice(1)}</h4>
          <div class="cards">
            ${templates.filter((x) => x.family === family).map((x) => this.#renderCard(x))}
          </div>
        </div>`,
      )}
      <uui-checkbox label=${t('showRetiredKinds')} .checked=${this._showRetired} @change=${(e: Event) => (this._showRetired = (e.target as HTMLInputElement).checked)}></uui-checkbox>
      ${this.#variants.length > 1 ? this.#renderVariants() : nothing}
      <p class="muted developer">${t('wizardDeveloperTools')}</p>`;
  }

  #renderCard(template: SeoTemplate) {
    const selected = template.id === this._templateId;
    const status = template.googleFeature.status;
    return html`<button
      type="button"
      class="card ${selected ? 'selected' : ''}"
      data-mark="schemeweaver-seo:template-card:${template.id}"
      @click=${() => {
        this._templateId = template.id;
        this._schemaType = template.schemaType;
      }}>
      <div class="card-head"><umb-icon name=${template.icon}></umb-icon> <strong>${template.label}</strong></div>
      <div class="muted">${template.description}</div>
      <div class="chips">
        ${status === 'rich-result' ? html`<uui-tag look="secondary" color="positive">${template.googleFeature.name}</uui-tag>` : nothing}
        ${status === 'foundation' ? html`<uui-tag look="secondary">${this.localize.term('schemeWeaverSeo_foundation')}</uui-tag>` : nothing}
        ${status === 'retired' ? html`<uui-tag look="secondary" color="warning">${this.localize.term('schemeWeaverSeo_retired')}</uui-tag>` : nothing}
        ${this.#isSuggested(template) ? html`<uui-tag look="primary" color="default">${this.localize.term('schemeWeaverSeo_wizardSuggestedForYou')}</uui-tag>` : nothing}
      </div>
    </button>`;
  }

  #renderVariants() {
    const variants = this.#variants;
    const current = this._schemaType ?? this.#template?.schemaType;
    return html`<umb-property-layout label=${this.localize.term('schemeWeaverSeo_wizardVariant')}>
      <div slot="editor">
        ${variants.length > 8
          ? html`<uui-select .options=${variants.map((v) => ({ name: v.label, value: v.schemaType, selected: v.schemaType === current }))}
              @change=${(e: Event) => (this._schemaType = (e.target as HTMLSelectElement).value)}></uui-select>`
          : html`<uui-radio-group .value=${current ?? ''} @change=${(e: Event) => (this._schemaType = (e.target as HTMLInputElement).value)}>
              ${variants.map((v) => html`<uui-radio value=${v.schemaType} label="${v.label}${v.hint ? ` – ${v.hint}` : ''}"></uui-radio>`)}
            </uui-radio-group>`}
      </div>
    </umb-property-layout>`;
  }

  #renderFieldsStep() {
    const t = (k: string) => this.localize.term(`schemeWeaverSeo_${k}`);
    const p = this._projection;
    if (!p) return html`<uui-loader-bar></uui-loader-bar>`;
    const groups: Array<[string, typeof p.slots]> = [
      ['wizardFieldsRequired', p.slots.filter((s) => s.slot.importance === 'required')],
      ['wizardFieldsRecommended', p.slots.filter((s) => s.slot.importance === 'recommended')],
      ['wizardFieldsOptional', p.slots.filter((s) => s.slot.importance === 'optional')],
    ];
    return html`
      <p class="intro">${t('wizardFieldsIntro')}</p>
      <div class="toolbar">
        <uui-tag look="secondary">${this.#template?.label} · ${this._schemaType ?? this.#template?.schemaType}</uui-tag>
        <uui-checkbox label=${t('wizardShowAllFields')} .checked=${this._showAllFields} @change=${(e: Event) => (this._showAllFields = (e.target as HTMLInputElement).checked)}></uui-checkbox>
      </div>
      ${groups.filter(([, slots]) => slots.length).map(
        ([key, slots]) => html`<uui-box headline=${t(key)}>
          ${slots.map(
            (s) => html`<schemeweaver-seo-slot-editor
              .definition=${s.slot}
              .value=${this._values.get(s.slot.schemaProperty) ?? s.value}
              .properties=${this._properties}
              .siteIdentityConfigured=${this._siteIdentityConfigured}
              .showAllFields=${this._showAllFields}
              @slot-change=${(e: CustomEvent<{ schemaProperty: string; value: SeoSlotValue }>) => {
                const next = new Map(this._values);
                next.set(e.detail.schemaProperty, e.detail.value);
                this._values = next;
              }}></schemeweaver-seo-slot-editor>`,
          )}
        </uui-box>`,
      )}
      ${p.advancedRows.length
        ? html`<uui-box headline=${t('wizardAdvanced')}>
            <p class="muted">${t('wizardAdvancedHelp')}</p>
            <ul class="advanced">${p.advancedRows.map((r) => html`<li><code>${r.schemaPropertyName}</code> ← ${r.sourceType}: ${r.contentTypePropertyAlias ?? r.targetPieceKey ?? r.staticValue ?? ''}</li>`)}</ul>
          </uui-box>`
        : nothing}`;
  }

  #renderCheckStep() {
    const t = (k: string) => this.localize.term(`schemeWeaverSeo_${k}`);
    return html`
      <p class="intro">${t('wizardCheckIntro')}</p>
      ${this._saved
        ? html`${this._samplePages.length === 0
              ? html`<uui-box><p>${t('wizardNoSamplePage')}</p></uui-box>`
              : html`<umb-property-layout label=${t('wizardSamplePage')}>
                    <div slot="editor">
                      <uui-select
                        .options=${this._samplePages.map((s) => ({ name: s.name, value: s.key, selected: s.key === this._sampleKey }))}
                        @change=${async (e: Event) => {
                          this._sampleKey = (e.target as HTMLSelectElement).value;
                          await this.#loadPreview();
                        }}></uui-select>
                    </div>
                  </umb-property-layout>
                  ${this._preview
                    ? html`<uui-box headline=${t('wizardPreviewHeading')} data-mark="schemeweaver-seo:wizard-preview">
                        <schemeweaver-validation-panel .issues=${this._preview.issues ?? []}></schemeweaver-validation-panel>
                        <details><summary>${t('pageJsonLd')}</summary>
                          <schemeweaver-jsonld-preview .jsonLd=${this._preview}></schemeweaver-jsonld-preview>
                        </details>
                      </uui-box>`
                    : html`<uui-loader-bar></uui-loader-bar>`}`}`
        : nothing}`;
  }

  static styles = [
    UmbTextStyles,
    css`
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-4); }
      .intro { margin: 0; }
      .muted { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
      .steps { display: flex; gap: var(--uui-size-space-5); }
      .step { color: var(--uui-color-text-alt); }
      .step.active { color: var(--uui-color-text); font-weight: 700; }
      .step.done { color: var(--uui-color-positive-standalone); }
      .family h4 { margin: var(--uui-size-space-3) 0 var(--uui-size-space-2); }
      .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--uui-size-space-3); }
      .card { text-align: left; background: var(--uui-color-surface); border: 2px solid var(--uui-color-border); border-radius: var(--uui-border-radius); padding: var(--uui-size-space-4); cursor: pointer; display: flex; flex-direction: column; gap: var(--uui-size-space-2); font: inherit; color: inherit; }
      .card:hover { border-color: var(--uui-color-interactive-emphasis); }
      .card.selected { border-color: var(--uui-color-positive-standalone); background: var(--uui-color-positive-standalone-0, var(--uui-color-surface-alt)); }
      .card-head { display: flex; align-items: center; gap: var(--uui-size-space-2); }
      .chips { display: flex; flex-wrap: wrap; gap: var(--uui-size-space-1); }
      .toolbar { display: flex; justify-content: space-between; align-items: center; }
      .actions { display: flex; gap: var(--uui-size-space-2); }
      ul.advanced { margin: 0; padding-left: var(--uui-size-space-5); }
      details summary { cursor: pointer; margin-top: var(--uui-size-space-3); }
      .developer { margin-top: var(--uui-size-space-4); }
    `,
  ];
}

export default SeoSetupWizardModalElement;
