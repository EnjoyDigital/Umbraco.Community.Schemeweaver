import { expect, fixture, html } from '@open-wc/testing';
import './confidence-tag.element.js';
import type { SeoConfidenceTagElement } from './confidence-tag.element.js';

describe('schemeweaver-seo-confidence-tag', () => {
  const colourFor = async (confidence: number | null) => {
    const el = await fixture<SeoConfidenceTagElement>(html`<schemeweaver-seo-confidence-tag .confidence=${confidence}></schemeweaver-seo-confidence-tag>`);
    return el.shadowRoot!.querySelector('uui-tag')?.getAttribute('color') ?? null;
  };

  it('uses the core thresholds (80 high, 50 medium)', async () => {
    expect(await colourFor(100)).to.equal('positive');
    expect(await colourFor(80)).to.equal('positive');
    expect(await colourFor(79)).to.equal('warning');
    expect(await colourFor(50)).to.equal('warning');
    expect(await colourFor(49)).to.equal('danger');
  });

  it('renders nothing without a confidence', async () => {
    expect(await colourFor(null)).to.equal(null);
  });
});
