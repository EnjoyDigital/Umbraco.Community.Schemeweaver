import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, nothing, customElement, state, query } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { UMB_DOCUMENT_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/document';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../context/seo.context-token.js';
import type { SchemeWeaverSeoContext } from '../context/seo.context.js';
import type { SeoPageSummary } from '../api/seo-types.js';
import type { JsonLdPreviewElement } from '../core-bridge/elements.js';
import { GOOGLE_RICH_RESULTS_TEST_URL } from '../constants.js';

interface Glance {
  type?: string;
  title?: string;
  image?: string;
  datePublished?: string;
  dateModified?: string;
  author?: string;
  publisher?: string;
}

/**
 * The content item's "Schema" tab (Yoast's per-post Schema sidebar): what the page emits in
 * plain language, a page-type override limited to the kind's variants, an exclude toggle, the
 * validation checklist and the raw JSON-LD.
 */
@customElement('schemeweaver-seo-page-schema-view')
export class SeoPageSchemaViewElement extends UmbLitElement {
  #context?: SchemeWeaverSeoContext;
  #notifications?: typeof UMB_NOTIFICATION_CONTEXT.TYPE;

  @state() private _contentKey = '';
  @state() private _culture?: string;
  @state() private _summary?: SeoPageSummary;
  @state() private _loading = true;
  @state() private _saving = false;

  @query('schemeweaver-jsonld-preview')
  private _previewEl?: JsonLdPreviewElement;

  constructor() {
    super();
    this.consumeContext(UMB_NOTIFICATION_CONTEXT, (c) => (this.#notifications = c));
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, (c) => {
      if (!c) return;
      this.#context = c;
      void this.#load();
    });
  }

  async connectedCallback() {
    super.connectedCallback();
    try {
      const workspace = await this.getContext(UMB_DOCUMENT_WORKSPACE_CONTEXT);
      if (!workspace) {
        this._loading = false;
        return;
      }
      this._contentKey = workspace.getUnique() ?? '';
      this.observe(workspace.splitView.activeVariantByIndex(0), (variant) => {
        this._culture = variant?.culture ?? undefined;
      }, '_variant');
      void this.#load();
    } catch {
      this._loading = false;
    }
  }

  async #load() {
    if (!this.#context || !this._contentKey) return;
    this._loading = true;
    try {
      this._summary = await this.#context.getPageSummary(this._contentKey, this._culture);
    } finally {
      this._loading = false;
    }
  }

  async #saveOverride(patch: { schemaTypeName?: string | null; isExcluded?: boolean }) {
    if (!this.#context || !this._summary) return;
    this._saving = true;
    try {
      const current = this._summary.override;
      const request = {
        schemaTypeName: patch.schemaTypeName !== undefined ? patch.schemaTypeName : (current?.schemaTypeName ?? null),
        isExcluded: patch.isExcluded !== undefined ? patch.isExcluded : (current?.isExcluded ?? false),
      };
      const saved = await this.#context.savePageOverride(this._contentKey, request);
      if (saved) {
        this.#notifications?.peek('positive', { data: { message: this.localize.term('schemeWeaverSeo_pageOverrideSaved') } });
        await this.#load();
      }
    } finally {
      this._saving = false;
    }
  }

  async #copy() {
    const text = this._previewEl?.formattedJson ?? this._summary?.preview?.jsonLd;
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.#notifications?.peek('positive', { data: { message: this.localize.term('schemeWeaverSeo_copied') } });
    } catch {
      /* clipboard unavailable */
    }
  }

  #glance(): Glance {
    const preview = this._summary?.preview;
    const type = this._summary?.effectiveSchemaTypeName ?? undefined;
    if (!preview || !type) return { type };
    try {
      const root = JSON.parse(preview.jsonLd) as { '@graph'?: Array<Record<string, unknown>> } & Record<string, unknown>;
      const nodes = root['@graph'] ?? [root];
      const node = nodes.find((n) => n['@type'] === type) ?? nodes.find((n) => typeof n['@type'] === 'string' && n['@type'] !== 'WebSite' && n['@type'] !== 'BreadcrumbList');
      if (!node) return { type };
      const text = (v: unknown): string | undefined => {
        if (!v) return undefined;
        if (typeof v === 'string') return v;
        if (Array.isArray(v)) return text(v[0]);
        if (typeof v === 'object') {
          const o = v as Record<string, unknown>;
          return text(o.name) ?? text(o.url) ?? text(o['@id']);
        }
        return String(v);
      };
      return {
        type,
        title: text(node.headline) ?? text(node.name) ?? text(node.title),
        image: text(node.image) ?? text(node.thumbnailUrl),
        datePublished: text(node.datePublished),
        dateModified: text(node.dateModified),
        author: text(node.author),
        publisher: text(node.publisher),
      };
    } catch {
      return { type };
    }
  }

  render() {
    const t = (k: string, ...args: unknown[]) => this.localize.term(`schemeWeaverSeo_${k}`, ...args);
    if (this._loading && !this._summary) {
      return html`<umb-body-layout><uui-loader-circle></uui-loader-circle></umb-body-layout>`;
    }
    const s = this._summary;
    if (!s || !s.hasMapping) {
      return html`<umb-body-layout>
        <uui-box data-mark="schemeweaver-seo:page-not-set-up">
          <div class="empty"><uui-icon name="icon-globe"></uui-icon>
            <strong>${t('pageNotSetUp')}</strong>
            <span class="muted">${t('pageNotSetUpHint')}</span>
          </div>
        </uui-box>
      </umb-body-layout>`;
    }

    const glance = this.#glance();
    const googleUrl = s.publicUrl ? `${GOOGLE_RICH_RESULTS_TEST_URL}?url=${encodeURIComponent(s.publicUrl)}` : null;
    return html`
      <umb-body-layout>
        <div id="main">
          <uui-box headline=${t('pageWhatItSays')}>
            <div slot="header-actions" class="actions">
              <uui-button look="secondary" compact target="_blank" href=${googleUrl ?? nothing} .disabled=${!googleUrl}
                title=${googleUrl ? '' : t('pageUnpublished')} label=${t('testOnGoogle')} data-mark="schemeweaver-seo:test-on-google">
                <uui-icon name="icon-out"></uui-icon> ${t('testOnGoogle')}
              </uui-button>
              <uui-button look="secondary" compact label=${t('refresh')} @click=${this.#load}><uui-icon name="icon-refresh"></uui-icon></uui-button>
            </div>
            <p class="muted">${t('pageIntro')}</p>
            ${s.isExcluded
              ? html`<p class="excluded" data-mark="schemeweaver-seo:page-excluded"><uui-icon name="icon-block"></uui-icon> ${t('pageExcluded')}</p>`
              : !s.isPublished
                ? html`<p class="muted">${t('pageUnpublished')}</p>`
                : html`<dl class="glance">
                    <dt>${t('pageKind')}</dt><dd>${s.templateLabel ?? ''} <code>${glance.type}</code></dd>
                    ${glance.title ? html`<dt>${t('pageHeadline')}</dt><dd>${glance.title}</dd>` : nothing}
                    ${glance.image ? html`<dt>${t('pageImage')}</dt><dd><img class="thumb" src=${glance.image} alt="" /> <span class="muted">${glance.image}</span></dd>` : nothing}
                    ${glance.datePublished || glance.dateModified ? html`<dt>${t('pageDates')}</dt><dd>${glance.datePublished ?? ''} ${glance.dateModified ? `→ ${glance.dateModified}` : ''}</dd>` : nothing}
                    ${glance.author ? html`<dt>${t('pageAuthor')}</dt><dd>${glance.author}</dd>` : nothing}
                    ${glance.publisher ? html`<dt>${t('pagePublisher')}</dt><dd>${glance.publisher}</dd>` : nothing}
                  </dl>`}
          </uui-box>

          <uui-box headline=${t('pageType')}>
            <umb-property-layout label=${t('pageType')}>
              <div slot="editor">
                <uui-select data-mark="schemeweaver-seo:page-type-select" .disabled=${this._saving}
                  .options=${[
                    { name: t('pageTypeDefault', s.defaultSchemaTypeName ?? ''), value: '', selected: !s.override?.schemaTypeName },
                    ...s.allowedPageTypes
                      .filter((v) => v.schemaType !== s.defaultSchemaTypeName)
                      .map((v) => ({ name: `${v.label} (${v.schemaType})`, value: v.schemaType, selected: s.override?.schemaTypeName === v.schemaType })),
                  ]}
                  @change=${(e: Event) => this.#saveOverride({ schemaTypeName: (e.target as HTMLSelectElement).value || null })}></uui-select>
              </div>
            </umb-property-layout>
            <umb-property-layout label=${t('pageExclude')} description=${t('pageExcludeHelp')}>
              <div slot="editor">
                <uui-toggle data-mark="schemeweaver-seo:exclude-toggle" .checked=${s.isExcluded} .disabled=${this._saving}
                  @change=${(e: Event) => this.#saveOverride({ isExcluded: (e.target as HTMLInputElement).checked })}></uui-toggle>
              </div>
            </umb-property-layout>
          </uui-box>

          ${s.preview
            ? html`<uui-box headline=${t('pageJsonLd')}>
                <uui-button slot="header-actions" look="secondary" compact label=${t('copy')} @click=${this.#copy}><uui-icon name="icon-documents"></uui-icon></uui-button>
                <schemeweaver-validation-panel .issues=${s.preview.issues ?? []}></schemeweaver-validation-panel>
                <details><summary>${t('pageJsonLd')}</summary>
                  <schemeweaver-jsonld-preview .jsonLd=${s.preview}></schemeweaver-jsonld-preview>
                </details>
              </uui-box>`
            : nothing}
        </div>
      </umb-body-layout>`;
  }

  static styles = [
    UmbTextStyles,
    css`
      :host { display: block; padding: var(--uui-size-space-5); }
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-5); }
      .muted { color: var(--uui-color-text-alt); font-size: var(--uui-type-small-size); }
      .actions { display: flex; gap: var(--uui-size-space-2); }
      .empty { display: flex; flex-direction: column; align-items: center; gap: var(--uui-size-space-2); padding: var(--uui-size-space-6); text-align: center; }
      .empty uui-icon { font-size: 2.5rem; color: var(--uui-color-text-alt); }
      .excluded { color: var(--uui-color-warning-standalone); display: flex; align-items: center; gap: var(--uui-size-space-2); }
      dl.glance { display: grid; grid-template-columns: max-content 1fr; gap: var(--uui-size-space-2) var(--uui-size-space-5); margin: 0; }
      dl.glance dt { color: var(--uui-color-text-alt); }
      dl.glance dd { margin: 0; }
      .thumb { max-height: 48px; vertical-align: middle; border-radius: var(--uui-border-radius); }
      details summary { cursor: pointer; margin-top: var(--uui-size-space-3); }
    `,
  ];
}

export default SeoPageSchemaViewElement;
