import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { css, html, customElement, state } from '@umbraco-cms/backoffice/external/lit';
import { UmbTextStyles } from '@umbraco-cms/backoffice/style';
import { SCHEMEWEAVER_SEO_CONTEXT } from '../../context/seo.context-token.js';
import type { SeoServerContext } from '../../api/seo-types.js';

/** Read-only diagnostics: how this install emits structured data (set by developers in config). */
@customElement('schemeweaver-seo-settings-view')
export class SeoSettingsViewElement extends UmbLitElement {
  @state() private _serverContext?: SeoServerContext;

  constructor() {
    super();
    this.consumeContext(SCHEMEWEAVER_SEO_CONTEXT, (context) => {
      if (!context) return;
      this.observe(context.serverContext, (v) => (this._serverContext = v), '_sc');
      void context.loadServerContext().then((v) => (this._serverContext = v ?? this._serverContext));
    });
  }

  render() {
    const sc = this._serverContext;
    const t = (key: string, ...args: unknown[]) => this.localize.term(`schemeWeaverSeo_${key}`, ...args);
    return html`
      <umb-body-layout headline=${t('settings')}>
        <div id="main">
          <p class="intro">${t('settingsIntro')}</p>
          ${sc
            ? html`<uui-box>
                ${this.#row(t('settingsGraphModel'), sc.useGraphModel ? t('settingsGraphModelOn') : t('settingsGraphModelOff'))}
                ${this.#row(t('settingsBreadcrumbs'), sc.emitBreadcrumbsInDeliveryApi ? t('settingsOn') : t('settingsOff'))}
                ${this.#row(t('settingsPublicSiteUrl'), sc.publicSiteUrl ?? `${t('settingsPublicSiteUrlDefault')} (${sc.siteUrl ?? '—'})`)}
                ${this.#row(t('settingsIdentityPrecedence'), t('settingsIdentityPrecedenceHelp'))}
                ${this.#row(t('settingsDeveloperTab'), sc.hideDeveloperJsonLdTab ? t('settingsDeveloperTabHidden') : t('settingsDeveloperTabShown'))}
                ${this.#row(t('settingsSection'), t('settingsSectionHelp', sc.sectionAlias))}
              </uui-box>`
            : html`<uui-loader-circle></uui-loader-circle>`}
        </div>
      </umb-body-layout>`;
  }

  #row(label: string, value: string) {
    return html`<umb-property-layout label=${label}><div slot="editor">${value}</div></umb-property-layout>`;
  }

  static styles = [
    UmbTextStyles,
    css`
      #main { display: flex; flex-direction: column; gap: var(--uui-size-space-5); }
      .intro { margin: 0; max-width: 70ch; }
    `,
  ];
}

export default SeoSettingsViewElement;
