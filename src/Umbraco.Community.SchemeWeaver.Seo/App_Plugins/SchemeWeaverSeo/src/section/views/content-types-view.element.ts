import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, nothing, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_MODAL_MANAGER_CONTEXT, UMB_CONFIRM_MODAL } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../../context/seo.context-token.js';
import type { SchemeWeaverSeoContext } from '../../context/seo.context.js';
import type { SeoContentTypeStatus } from '../../api/seo-types.js';
import { SEO_SETUP_WIZARD_MODAL } from '../../wizard/setup-wizard-modal.token.js';
import '../../components/status-tag.element.js';

/** Yoast's "Content types" settings: one row per page type with its kind and status. */
@customElement('schemeweaver-seo-content-types-view')
export class SeoContentTypesViewElement extends UmbLitElement {
  #context?: SchemeWeaverSeoContext;

  @state() private _contentTypes: SeoContentTypeStatus[] = [];
  @state() private _loading = true;
  @state() private _hideNonRoutable = true;
  @state() private _hideElements = true;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, (context) => {
      if (!context) return;
      this.#context = context;
      this.observe(context.contentTypes, (v) => (this._contentTypes = v), '_ct');
      this.observe(context.loading, (v) => (this._loading = v), '_loading');
      void context.loadCatalogue();
      void context.loadContentTypes().then((rows) => {
        if (rows) this._contentTypes = rows;
        this._loading = false;
      });
    });
  }

  get #rows() {
    return this._contentTypes.filter((c) => {
      if (this._hideElements && c.isElement) return false;
      if (this._hideNonRoutable && !c.isElement && !c.hasTemplate && !c.allowedAsRoot && c.publishedCount === 0) return false;
      return true;
    });
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
    void this.#context?.refreshContentType(contentType.alias);
  }

  async #clear(contentType: SeoContentTypeStatus) {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    const modal = modalManager?.open(this, UMB_CONFIRM_MODAL, {
      data: {
        headline: this.localize.term('schemeWeaverSeo_clearSchema'),
        content: this.localize.term('schemeWeaverSeo_clearSchemaConfirm', contentType.name),
        color: 'danger',
        confirmLabel: this.localize.term('schemeWeaverSeo_clearSchema'),
      },
    });
    try {
      await modal?.onSubmit();
    } catch {
      return;
    }
    await this.#context?.deleteMapping(contentType.alias);
  }

  render() {
    return html`
      <umb-body-layout headline=${this.localize.term('schemeWeaverSeo_contentTypes')}>
        <div id="main">
          <p class="intro">${this.localize.term('schemeWeaverSeo_contentTypesIntro')}</p>
          <div class="filters">
            <uui-checkbox label=${this.localize.term('schemeWeaverSeo_hideNonRoutable')} .checked=${this._hideNonRoutable}
              @change=${(e: Event) => (this._hideNonRoutable = (e.target as HTMLInputElement).checked)}></uui-checkbox>
            <uui-checkbox label=${this.localize.term('schemeWeaverSeo_hideElements')} .checked=${this._hideElements}
              @change=${(e: Event) => (this._hideElements = (e.target as HTMLInputElement).checked)}></uui-checkbox>
          </div>
          <uui-box>
            ${this._loading && this._contentTypes.length === 0
              ? html`<uui-loader-bar></uui-loader-bar>`
              : this.#rows.length === 0
                ? html`<p class="muted">${this.localize.term('schemeWeaverSeo_noContentTypes')}</p>`
                : this.#renderTable()}
          </uui-box>
        </div>
      </umb-body-layout>`;
  }

  #renderTable() {
    return html`<uui-table data-mark="schemeweaver-seo:content-types-table">
      <uui-table-head>
        <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnName')}</uui-table-head-cell>
        <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnStatus')}</uui-table-head-cell>
        <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnKind')}</uui-table-head-cell>
        <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnRequired')}</uui-table-head-cell>
        <uui-table-head-cell>${this.localize.term('schemeWeaverSeo_columnPages')}</uui-table-head-cell>
        <uui-table-head-cell></uui-table-head-cell>
      </uui-table-head>
      ${this.#rows.map((row) => this.#renderRow(row))}
    </uui-table>`;
  }

  #renderRow(row: SeoContentTypeStatus) {
    const m = row.mapping;
    const developerManaged = row.status === 'developer-managed';
    return html`<uui-table-row data-mark="schemeweaver-seo:content-type:${row.alias}">
      <uui-table-cell>
        <div class="name"><umb-icon name=${row.icon ?? 'icon-document'}></umb-icon> <strong>${row.name}</strong></div>
        <span class="muted">${row.alias}</span>
      </uui-table-cell>
      <uui-table-cell>
        <schemeweaver-seo-status-tag .status=${row.status}></schemeweaver-seo-status-tag>
        ${developerManaged ? html`<div class="muted">${this.localize.term('schemeWeaverSeo_statusDeveloperManagedHelp')}</div>` : nothing}
      </uui-table-cell>
      <uui-table-cell>
        ${m ? html`${m.templateLabel ?? m.schemaTypeName} <span class="muted">${m.schemaTypeName}</span>
          ${m.isRetired ? html`<uui-tag look="secondary" color="warning">${this.localize.term('schemeWeaverSeo_retired')}</uui-tag>` : nothing}` : '—'}
      </uui-table-cell>
      <uui-table-cell>
        ${m && m.templateId
          ? html`<span class=${m.requiredFilled < m.requiredTotal ? 'short' : 'full'}>${m.requiredFilled}/${m.requiredTotal}</span>
              ${m.missingRequired.length ? html`<div class="muted">${m.missingRequired.join(', ')}</div>` : nothing}
              ${m.advancedRowCount ? html`<div class="muted">${this.localize.term('schemeWeaverSeo_advancedRows', m.advancedRowCount)}</div>` : nothing}`
          : '—'}
      </uui-table-cell>
      <uui-table-cell>${row.isElement ? '—' : row.publishedCount}</uui-table-cell>
      <uui-table-cell>
        <div class="actions">
          ${developerManaged
            ? nothing
            : html`<uui-button look=${m ? 'secondary' : 'primary'} compact data-mark="schemeweaver-seo:set-up:${row.alias}"
                label=${this.localize.term(m ? 'schemeWeaverSeo_review' : 'schemeWeaverSeo_setUp')}
                @click=${() => this.#openWizard(row)}></uui-button>`}
          ${m
            ? html`<uui-button look="secondary" color="danger" compact label=${this.localize.term('schemeWeaverSeo_clearSchema')}
                @click=${() => this.#clear(row)}><uui-icon name="icon-trash"></uui-icon></uui-button>`
            : nothing}
        </div>
      </uui-table-cell>
    </uui-table-row>`;
  }

  static styles = [
    UmbTextStyles,
    css`
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-4); }
      .intro { margin: 0; max-width: 70ch; }
      .filters { display: flex; gap: var(--uui-size-space-5); }
      .muted { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
      .name { display: flex; align-items: center; gap: var(--uui-size-space-2); }
      .actions { display: flex; gap: var(--uui-size-space-2); justify-content: flex-end; }
      .short { color: var(--uui-color-warning-standalone); font-weight: 700; }
      .full { color: var(--uui-color-positive-standalone); font-weight: 700; }
    `,
  ];
}

export default SeoContentTypesViewElement;
