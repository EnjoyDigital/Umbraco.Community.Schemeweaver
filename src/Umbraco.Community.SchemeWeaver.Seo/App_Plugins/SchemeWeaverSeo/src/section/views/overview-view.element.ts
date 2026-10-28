import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, nothing, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../../context/seo.context-token.js';
import type { SchemeWeaverSeoContext } from '../../context/seo.context.js';
import type { SeoContentTypeStatus, SeoHealthReport, SeoServerContext, SiteIdentity } from '../../api/seo-types.js';
import { GOOGLE_RICH_RESULTS_TEST_URL, SEO_SECTION_PATHNAME } from '../../constants.js';
import { SEO_SETUP_WIZARD_MODAL } from '../../wizard/setup-wizard-modal.token.js';
import '../../components/status-tag.element.js';

@customElement('schemeweaver-seo-overview-view')
export class SeoOverviewViewElement extends UmbLitElement {
  #context?: SchemeWeaverSeoContext;

  @state() private _serverContext?: SeoServerContext;
  @state() private _identity?: SiteIdentity;
  @state() private _contentTypes: SeoContentTypeStatus[] = [];
  @state() private _health?: SeoHealthReport;
  @state() private _runningHealth = false;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, (context) => {
      if (!context) return;
      this.#context = context;
      this.observe(context.serverContext, (v) => (this._serverContext = v), '_sc');
      this.observe(context.siteIdentity, (v) => (this._identity = v), '_id');
      this.observe(context.contentTypes, (v) => (this._contentTypes = v), '_ct');
      this.observe(context.health, (v) => (this._health = v), '_health');
      void context.loadServerContext().then((v) => (this._serverContext = v ?? this._serverContext));
      void context.loadSiteIdentity().then((v) => (this._identity = v ?? this._identity));
      void context.loadContentTypes().then((v) => (this._contentTypes = v ?? this._contentTypes));
      void context.loadCatalogue();
    });
  }

  get #pageTypes() {
    return this._contentTypes.filter((c) => !c.isElement);
  }

  get #setUp() {
    return this.#pageTypes.filter((c) => c.status === 'set-up' || c.status === 'developer-managed');
  }

  get #attention() {
    return this.#pageTypes.filter((c) => c.status === 'needs-attention');
  }

  async #runHealth() {
    this._runningHealth = true;
    try {
      await this.#context?.runHealthCheck(true);
    } finally {
      this._runningHealth = false;
    }
  }

  async #openWizard(contentType: SeoContentTypeStatus) {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    const modal = modalManager?.open(this, SEO_SETUP_WIZARD_MODAL, {
      data: { contentTypeAlias: contentType.alias, contentTypeName: contentType.name, contentTypeKey: contentType.key },
    });
    try {
      await modal?.onSubmit();
    } catch {
      /* cancelled */
    }
    void this.#context?.loadContentTypes();
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaverSeo_sectionLabel')}>
        <div id="main">
          <p class="intro">${this.localize.term('schemeWeaverSeo_overviewIntro')}</p>
          ${this.#renderManagedAsCode()}
          <div class="grid">
            ${this.#renderIdentityCard()} ${this.#renderContentTypesCard()}
          </div>
          ${this.#renderHealth()}
        </div>
      </umb-body-layout>
    `;
  }

  #renderManagedAsCode() {
    const sc = this._serverContext;
    if (!sc?.mappingsManagedAsCode) return nothing;
    const upsert = sc.uSyncBootImport?.toLowerCase() === 'upsert';
    return html`<uui-box class="warning" data-mark="schemeweaver-seo:managed-as-code">
      <strong>${this.localize.term('schemeWeaverSeo_managedAsCodeTitle')}</strong>
      <p>${this.localize.term(upsert ? 'schemeWeaverSeo_managedAsCodeUpsert' : 'schemeWeaverSeo_managedAsCodeSeed')}</p>
    </uui-box>`;
  }

  #renderIdentityCard() {
    const done = this._identity?.isConfigured === true;
    return html`<uui-box headline=${this.localize.term('schemeWeaverSeo_checklistIdentity')} data-mark="schemeweaver-seo:overview-identity">
      <div class="check ${done ? 'done' : ''}">
        <uui-icon name=${done ? 'icon-check' : 'icon-alert'}></uui-icon>
        <span>${this.localize.term(done ? 'schemeWeaverSeo_checklistIdentityDone' : 'schemeWeaverSeo_checklistIdentityTodo')}</span>
      </div>
      ${done ? html`<p class="muted">${this._identity?.name}</p>` : nothing}
      <uui-button slot="header-actions" look="secondary" compact href="section/${SEO_SECTION_PATHNAME}/view/site-identity"
        label=${this.localize.term(done ? 'schemeWeaverSeo_review' : 'schemeWeaverSeo_setUp')}></uui-button>
    </uui-box>`;
  }

  #renderContentTypesCard() {
    const total = this.#pageTypes.length;
    const done = this.#setUp.length;
    return html`<uui-box headline=${this.localize.term('schemeWeaverSeo_checklistContentTypes')} data-mark="schemeweaver-seo:overview-content-types">
      <div class="check ${done > 0 ? 'done' : ''}">
        <uui-icon name=${done > 0 ? 'icon-check' : 'icon-alert'}></uui-icon>
        <span>${done > 0
          ? this.localize.term('schemeWeaverSeo_checklistContentTypesSummary', done, total)
          : this.localize.term('schemeWeaverSeo_checklistContentTypesTodo')}</span>
      </div>
      ${this.#attention.length
        ? html`<p class="muted">${this.localize.term('schemeWeaverSeo_needsAttention')}:</p>
            <ul class="attention">
              ${this.#attention.map(
                (c) => html`<li>
                  <uui-button look="link" compact label=${c.name} @click=${() => this.#openWizard(c)}>${c.name}</uui-button>
                  <span class="muted">${c.mapping?.missingRequired.join(', ')}</span>
                </li>`,
              )}
            </ul>`
        : nothing}
      <uui-button slot="header-actions" look="secondary" compact href="section/${SEO_SECTION_PATHNAME}/view/content-types"
        label=${this.localize.term('schemeWeaverSeo_contentTypes')}></uui-button>
    </uui-box>`;
  }

  #renderHealth() {
    const h = this._health;
    const siteUrl = this._serverContext?.publicSiteUrl ?? this._serverContext?.siteUrl;
    const googleUrl = siteUrl ? `${GOOGLE_RICH_RESULTS_TEST_URL}?url=${encodeURIComponent(siteUrl)}` : GOOGLE_RICH_RESULTS_TEST_URL;
    return html`<uui-box headline=${this.localize.term('schemeWeaverSeo_healthCheck')} data-mark="schemeweaver-seo:overview-health">
      <div slot="header-actions" class="actions">
        <uui-button look="secondary" compact target="_blank" href=${googleUrl}
          label=${this.localize.term('schemeWeaverSeo_testOnGoogle')} title=${this.localize.term('schemeWeaverSeo_testOnGoogleHint')}>
          <uui-icon name="icon-out"></uui-icon> ${this.localize.term('schemeWeaverSeo_testOnGoogle')}
        </uui-button>
        <uui-button look="primary" compact data-mark="schemeweaver-seo:run-health" .state=${this._runningHealth ? 'waiting' : undefined}
          label=${this.localize.term('schemeWeaverSeo_runHealthCheck')} @click=${this.#runHealth}></uui-button>
      </div>
      <p class="muted">${this.localize.term('schemeWeaverSeo_healthIntro')}</p>
      ${h
        ? html`<p class="muted">
              ${this.localize.term('schemeWeaverSeo_healthLastRun', new Date(h.generatedAt).toLocaleString())}
              ${h.cached ? this.localize.term('schemeWeaverSeo_healthCached') : ''}
            </p>
            ${h.critical === 0
              ? html`<p class="ok"><uui-icon name="icon-check"></uui-icon> ${this.localize.term('schemeWeaverSeo_healthNoIssues')}</p>`
              : nothing}
            <uui-table>
              <uui-table-head>
                <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnName')}</uui-table-head-cell>
                <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnStatus')}</uui-table-head-cell>
                <uui-table-head-cell></uui-table-head-cell>
              </uui-table-head>
              ${h.items.map(
                (item) => html`<uui-table-row>
                  <uui-table-cell>${item.name} <span class="muted">${item.samplePageName ?? ''}</span></uui-table-cell>
                  <uui-table-cell>
                    ${item.status === 'no-published-page'
                      ? html`<uui-tag look="secondary">${this.localize.term('schemeWeaverSeo_healthNoPublishedPage')}</uui-tag>`
                      : html`${item.critical
                            ? html`<uui-tag color="danger" look="primary">${this.localize.term('schemeWeaverSeo_healthCritical', item.critical)}</uui-tag>`
                            : nothing}
                          ${item.warning
                            ? html`<uui-tag color="warning" look="secondary">${this.localize.term('schemeWeaverSeo_healthWarning', item.warning)}</uui-tag>`
                            : nothing}
                          ${!item.critical && !item.warning ? html`<uui-tag color="positive" look="primary">OK</uui-tag>` : nothing}`}
                  </uui-table-cell>
                  <uui-table-cell>
                    ${item.issues.length
                      ? html`<details><summary>${item.issues.length}</summary>
                          <ul class="issues">${item.issues.map((i) => html`<li><code>${i.path}</code> ${i.message}</li>`)}</ul>
                        </details>`
                      : nothing}
                  </uui-table-cell>
                </uui-table-row>`,
              )}
            </uui-table>`
        : nothing}
    </uui-box>`;
  }

  static styles = [
    UmbTextStyles,
    css`
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-5); }
      .intro { margin: 0; max-width: 70ch; }
      .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: var(--uui-size-space-5); }
      .check { display: flex; align-items: center; gap: var(--uui-size-space-3); }
      .check uui-icon { color: var(--uui-color-warning-standalone); }
      .check.done uui-icon { color: var(--uui-color-positive-standalone); }
      .muted { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
      .ok { color: var(--uui-color-positive-standalone); }
      .warning { border-left: 4px solid var(--uui-color-warning-standalone); }
      .actions { display: flex; gap: var(--uui-size-space-2); }
      ul.attention, ul.issues { margin: 0; padding-left: var(--uui-size-space-5); }
      ul.issues li { margin: var(--uui-size-space-1) 0; }
      details summary { cursor: pointer; }
    `,
  ];
}

export default SeoOverviewViewElement;
