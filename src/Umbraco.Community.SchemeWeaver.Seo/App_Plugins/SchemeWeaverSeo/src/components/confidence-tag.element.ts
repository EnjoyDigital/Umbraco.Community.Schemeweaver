import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { html, nothing, customElement, property } from '@umbraco-cms/backoffice/external/lit';
import { CONFIDENCE_HIGH, CONFIDENCE_MEDIUM } from '../constants.js';

/** Same thresholds and colours as the core mapping table. */
@customElement('schemeweaver-seo-confidence-tag')
export class SeoConfidenceTagElement extends UmbLitElement {
  @property({ type: Number })
  confidence: number | null | undefined;

  render() {
    if (this.confidence === null || this.confidence === undefined) return nothing;
    const [color, key] =
      this.confidence >= CONFIDENCE_HIGH
        ? ['positive', 'schemeWeaverSeo_confidenceHigh']
        : this.confidence >= CONFIDENCE_MEDIUM
          ? ['warning', 'schemeWeaverSeo_confidenceMedium']
          : ['danger', 'schemeWeaverSeo_confidenceLow'];
    return html`<uui-tag look="secondary" color=${color} title="${this.confidence}%">${this.localize.term(key)}</uui-tag>`;
  }
}

declare global {
  interface HTMLElementTagNameMap {
    'schemeweaver-seo-confidence-tag': SeoConfidenceTagElement;
  }
}
