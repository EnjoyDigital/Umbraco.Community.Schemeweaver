import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, nothing, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import '@umbraco-cms/backoffice/media';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../../context/seo.context-token.js';
import type { SchemeWeaverSeoContext } from '../../context/seo.context.js';
import { EMPTY_SITE_IDENTITY, type SeoBusinessType, type SiteIdentity, type SiteIdentityOpeningHours } from '../../api/seo-types.js';

const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday', 'PublicHolidays'];

/** Yoast's "Site representation" form: who publishes the site. */
@customElement('schemeweaver-seo-site-identity-view')
export class SeoSiteIdentityViewElement extends UmbLitElement {
  #context?: SchemeWeaverSeoContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state() private _draft: SiteIdentity = structuredClone(EMPTY_SITE_IDENTITY);
  @state() private _loaded = false;
  @state() private _dirty = false;
  @state() private _saving = false;
  @state() private _localBusiness = false;
  @state() private _businessTypes: SeoBusinessType[] = [];

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (c) => (this.#notifications = c));
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, async (context) => {
      if (!context) return;
      this.#context = context;
      const [identity, catalogue] = await Promise.all([context.loadSiteIdentity(), context.loadCatalogue()]);
      this._businessTypes = catalogue?.businessTypes ?? [];
      if (identity) {
        this._draft = { ...structuredClone(EMPTY_SITE_IDENTITY), ...identity };
        this._localBusiness = !!identity.businessType;
      }
      this._loaded = true;
    });
  }

  #set<K extends keyof SiteIdentity>(key: K, value: SiteIdentity[K]) {
    this._draft = { ...this._draft, [key]: value };
    this._dirty = true;
  }

  #setAddress(key: string, value: string) {
    this._draft = { ...this._draft, address: { ...(this._draft.address ?? {}), [key]: value } };
    this._dirty = true;
  }

  #setHours(index: number, patch: Partial<SiteIdentityOpeningHours>) {
    const hours = [...this._draft.openingHours];
    hours[index] = { ...hours[index], ...patch };
    this.#set('openingHours', hours);
  }

  #toggleDay(index: number, day: string, on: boolean) {
    const current = this._draft.openingHours[index]?.dayOfWeek ?? [];
    this.#setHours(index, { dayOfWeek: on ? [...new Set([...current, day])] : current.filter((d) => d !== day) });
  }

  async #save() {
    if (!this.#context) return;
    this._saving = true;
    try {
      const payload: SiteIdentity = {
        ...this._draft,
        businessType: this._localBusiness ? this._draft.businessType || 'LocalBusiness' : null,
        address: this._localBusiness ? this._draft.address : null,
        geo: this._localBusiness ? this._draft.geo : null,
        openingHours: this._localBusiness ? this._draft.openingHours.filter((h) => h.dayOfWeek.length) : [],
        priceRange: this._localBusiness ? this._draft.priceRange : null,
      };
      const saved = await this.#context.saveSiteIdentity(payload);
      if (saved) {
        this._draft = { ...structuredClone(EMPTY_SITE_IDENTITY), ...saved };
        this._dirty = false;
        this.#notifications?.peek('positive', { data: { message: this.localize.term('schemeWeaverSeo_identitySaved') } });
      }
    } finally {
      this._saving = false;
    }
  }

  render() {
    if (!this._loaded) return html`<uui-loader-circle></uui-loader-circle>`;
    const isPerson = this._draft.kind === 'person';
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaverSeo_siteIdentity')}>
        <div id="main">
          <p class="intro">${this.localize.term('schemeWeaverSeo_identityIntro')}</p>
          <uui-box>
            ${this.#row('identityKind', html`
              <uui-radio-group .value=${this._draft.kind} @change=${(e: Event) => this.#set('kind', (e.target as HTMLInputElement).value as SiteIdentity['kind'])}>
                <uui-radio value="organisation" label=${this.localize.term('schemeWeaverSeo_identityOrganisation')}></uui-radio>
                <uui-radio value="person" label=${this.localize.term('schemeWeaverSeo_identityPerson')}></uui-radio>
              </uui-radio-group>`)}
            ${this.#row('identityName', this.#input('name', 'schemeweaver-seo:identity-name'), 'identityNameHelp')}
            ${this.#row('identityAlternateName', this.#input('alternateName'))}
            ${isPerson ? this.#row('identityJobTitle', this.#input('jobTitle')) : nothing}
            ${this.#row('identityUrl', this.#input('url'), 'identityUrlHelp')}
            ${this.#row(isPerson ? 'identityPhoto' : 'identityLogo', html`
              <umb-input-media
                .selection=${this._draft.logoMediaKey ? [this._draft.logoMediaKey] : []}
                .max=${1}
                @change=${(e: Event) => {
                  const el = e.target as HTMLElement & { selection: string[] };
                  this.#set('logoMediaKey', el.selection?.[0] ?? null);
                }}></umb-input-media>`, 'identityLogoHelp')}
            ${this.#row('identitySameAs', html`
              <uui-textarea
                data-mark="schemeweaver-seo:identity-same-as"
                .value=${this._draft.sameAs.join('\n')}
                rows="4"
                @input=${(e: Event) => this.#set('sameAs', (e.target as HTMLTextAreaElement).value.split(/\r?\n/).map((s) => s.trim()).filter(Boolean))}></uui-textarea>`, 'identitySameAsHelp')}
            ${this.#row('identityTelephone', this.#input('telephone'))}
            ${this.#row('identityEmail', this.#input('email'))}
            ${this.#row('identityDescription', html`<uui-textarea .value=${this._draft.description ?? ''} rows="3"
              @input=${(e: Event) => this.#set('description', (e.target as HTMLTextAreaElement).value)}></uui-textarea>`)}
          </uui-box>

          ${isPerson ? nothing : html`
            <uui-box headline=${this.localize.term('schemeWeaverSeo_identityLocalBusiness')}>
              <uui-toggle slot="header-actions" data-mark="schemeweaver-seo:identity-local-business"
                .checked=${this._localBusiness}
                @change=${(e: Event) => { this._localBusiness = (e.target as HTMLInputElement).checked; this._dirty = true; }}></uui-toggle>
              <p class="muted">${this.localize.term('schemeWeaverSeo_identityLocalBusinessHelp')}</p>
              ${this._localBusiness ? html`
                ${this.#row('identityBusinessType', html`
                  <uui-select
                    .options=${this._businessTypes.map((b) => ({ name: b.label, value: b.schemaType, selected: b.schemaType === (this._draft.businessType ?? 'LocalBusiness') }))}
                    @change=${(e: Event) => this.#set('businessType', (e.target as HTMLSelectElement).value)}></uui-select>`)}
                ${this.#row('identityAddress', html`
                  <div class="address">
                    ${this.#addressInput('streetAddress', 'identityStreet')}
                    ${this.#addressInput('addressLocality', 'identityLocality')}
                    ${this.#addressInput('addressRegion', 'identityRegion')}
                    ${this.#addressInput('postalCode', 'identityPostcode')}
                    ${this.#addressInput('addressCountry', 'identityCountry')}
                  </div>`, 'identityCountryHelp')}
                ${this.#row('identityGeo', html`
                  <div class="pair">
                    <uui-input type="number" step="any" placeholder=${this.localize.term('schemeWeaverSeo_identityLatitude')}
                      .value=${this._draft.geo?.latitude?.toString() ?? ''}
                      @input=${(e: Event) => this.#setGeo('latitude', (e.target as HTMLInputElement).value)}></uui-input>
                    <uui-input type="number" step="any" placeholder=${this.localize.term('schemeWeaverSeo_identityLongitude')}
                      .value=${this._draft.geo?.longitude?.toString() ?? ''}
                      @input=${(e: Event) => this.#setGeo('longitude', (e.target as HTMLInputElement).value)}></uui-input>
                  </div>`)}
                ${this.#row('identityPriceRange', this.#input('priceRange'), 'identityPriceRangeHelp')}
                ${this.#row('identityOpeningHours', this.#renderOpeningHours())}
              ` : nothing}
            </uui-box>`}

          <uui-box headline=${this.localize.term('schemeWeaverSeo_identitySearch')}>
            <p class="muted">${this.localize.term('schemeWeaverSeo_identitySearchHelp')}</p>
            <uui-input .value=${this._draft.searchUrlTemplate ?? ''} placeholder="https://www.example.com/search?q={search_term_string}"
              @input=${(e: Event) => this.#set('searchUrlTemplate', (e.target as HTMLInputElement).value)}></uui-input>
          </uui-box>
        </div>
        <div slot="actions">
          <uui-button look="primary" color="positive" data-mark="schemeweaver-seo:identity-save"
            .disabled=${!this._dirty || this._saving}
            .state=${this._saving ? 'waiting' : undefined}
            label=${this.localize.term('schemeWeaverSeo_save')} @click=${this.#save}></uui-button>
        </div>
      </umb-body-layout>`;
  }

  #setGeo(axis: 'latitude' | 'longitude', raw: string) {
    const value = parseFloat(raw);
    const geo = { latitude: this._draft.geo?.latitude ?? 0, longitude: this._draft.geo?.longitude ?? 0 };
    if (Number.isNaN(value)) {
      this.#set('geo', null);
      return;
    }
    geo[axis] = value;
    this.#set('geo', geo);
  }

  #renderOpeningHours() {
    return html`
      ${this._draft.openingHours.map((hours, index) => html`
        <div class="hours">
          <div class="days">
            ${DAYS.map((day) => html`<uui-checkbox label=${day === 'PublicHolidays' ? 'Public holidays' : day.slice(0, 3)}
              .checked=${hours.dayOfWeek.includes(day)}
              @change=${(e: Event) => this.#toggleDay(index, day, (e.target as HTMLInputElement).checked)}></uui-checkbox>`)}
          </div>
          <div class="pair">
            <uui-input type="time" .value=${hours.opens ?? ''} label=${this.localize.term('schemeWeaverSeo_identityOpens')}
              @input=${(e: Event) => this.#setHours(index, { opens: (e.target as HTMLInputElement).value })}></uui-input>
            <uui-input type="time" .value=${hours.closes ?? ''} label=${this.localize.term('schemeWeaverSeo_identityCloses')}
              @input=${(e: Event) => this.#setHours(index, { closes: (e.target as HTMLInputElement).value })}></uui-input>
            <uui-button look="secondary" compact label=${this.localize.term('schemeWeaverSeo_remove')}
              @click=${() => this.#set('openingHours', this._draft.openingHours.filter((_, i) => i !== index))}>
              <uui-icon name="icon-trash"></uui-icon>
            </uui-button>
          </div>
        </div>`)}
      <uui-button look="secondary" compact label=${this.localize.term('schemeWeaverSeo_identityOpeningHoursAdd')}
        @click=${() => this.#set('openingHours', [...this._draft.openingHours, { dayOfWeek: [], opens: '09:00', closes: '17:00' }])}>
        <uui-icon name="icon-add"></uui-icon> ${this.localize.term('schemeWeaverSeo_identityOpeningHoursAdd')}
      </uui-button>`;
  }

  #row(labelKey: string, editor: unknown, helpKey?: string) {
    return html`<umb-property-layout label=${this.localize.term(`schemeWeaverSeo_${labelKey}`)}
      description=${helpKey ? this.localize.term(`schemeWeaverSeo_${helpKey}`) : ''}>
      <div slot="editor">${editor}</div>
    </umb-property-layout>`;
  }

  #input(key: keyof SiteIdentity, mark?: string) {
    return html`<uui-input data-mark=${mark ?? nothing} .value=${(this._draft[key] as string | null | undefined) ?? ''}
      @input=${(e: Event) => this.#set(key, (e.target as HTMLInputElement).value as never)}></uui-input>`;
  }

  #addressInput(key: string, labelKey: string) {
    const value = (this._draft.address as Record<string, string | null | undefined> | null | undefined)?.[key] ?? '';
    return html`<uui-input placeholder=${this.localize.term(`schemeWeaverSeo_${labelKey}`)} .value=${value}
      @input=${(e: Event) => this.#setAddress(key, (e.target as HTMLInputElement).value)}></uui-input>`;
  }

  static styles = [
    UmbTextStyles,
    css`
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-5); }
      .intro { margin: 0; max-width: 70ch; }
      .muted { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); margin-top: 0; }
      uui-input, uui-textarea, uui-select { width: 100%; }
      .address { display: grid; gap: var(--uui-size-space-2); grid-template-columns: 1fr 1fr; }
      .address uui-input:first-child { grid-column: 1 / -1; }
      .pair { display: flex; gap: var(--uui-size-space-2); align-items: center; }
      .hours { border: 1px solid var(--uui-color-border); border-radius: var(--uui-border-radius); padding: var(--uui-size-space-3); margin-bottom: var(--uui-size-space-3); display: flex; flex-direction: column; gap: var(--uui-size-space-2); }
      .days { display: flex; flex-wrap: wrap; gap: var(--uui-size-space-3); }
    `,
  ];
}

export default SeoSiteIdentityViewElement;
