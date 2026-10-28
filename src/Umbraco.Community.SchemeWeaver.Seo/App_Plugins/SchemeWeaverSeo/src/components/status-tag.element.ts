import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, customElement, property } from '@umbraco-cms/backoffice/external/lit';
import type { ContentTypeStatusKind } from '../api/seo-types.js';

const LOOKS: Record<ContentTypeStatusKind, { color: 'positive' | 'warning' | 'default' | 'danger'; key: string; look: 'primary' | 'secondary' }> = {
  'set-up': { color: 'positive', key: 'schemeWeaverSeo_statusSetUp', look: 'primary' },
  'needs-attention': { color: 'warning', key: 'schemeWeaverSeo_statusNeedsAttention', look: 'primary' },
  'not-set-up': { color: 'default', key: 'schemeWeaverSeo_statusNotSetUp', look: 'secondary' },
  'developer-managed': { color: 'default', key: 'schemeWeaverSeo_statusDeveloperManaged', look: 'secondary' },
};

@customElement('schemeweaver-seo-status-tag')
export class SeoStatusTagElement extends UmbLitElement {
  @property({ type: String })
  status: ContentTypeStatusKind = 'not-set-up';

  render() {
    const look = LOOKS[this.status] ?? LOOKS['not-set-up'];
    return html`<uui-tag look=${look.look} color=${look.color} data-mark="schemeweaver-seo:status:${this.status}"
      >${this.localize.term(look.key)}</uui-tag
    >`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-seo-status-tag': SeoStatusTagElement;
  }
}
