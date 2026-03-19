import { expect, fixture, html } from '@open-wc/testing';
import './jsonld-preview.element.js';
import type { JsonLdPreviewResponse } from '../api/types.js';

describe('JsonLdPreviewElement', () => {
  it('renders empty state when jsonLd is null', async () => {
    const el = await fixture(html`<schemeweaver-jsonld-preview></schemeweaver-jsonld-preview>`);
    const empty = el.shadowRoot!.querySelector('.empty');
    expect(empty).to.exist;
    expect(empty!.textContent).to.contain('No JSON-LD data to preview');
  });

  it('does not render copy button in empty state', async () => {
    const el = await fixture(html`<schemeweaver-jsonld-preview></schemeweaver-jsonld-preview>`);
    const btn = el.shadowRoot!.querySelector('uui-button');
    expect(btn).to.not.exist;
  });

  it('renders formatted JSON when jsonLd is set', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"@context":"https://schema.org","@type":"Article"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const code = el.shadowRoot!.querySelector('code');
    expect(code).to.exist;
    expect(code!.textContent).to.contain('"@context"');
    expect(code!.textContent).to.contain('"@type"');
  });

  it('pretty-prints JSON with indentation', async () => {
    const inner = { name: 'Test' };
    const data: JsonLdPreviewResponse = {
      jsonLd: JSON.stringify(inner),
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const code = el.shadowRoot!.querySelector('code');
    const expected = JSON.stringify(inner, null, 2);
    expect(code!.textContent).to.equal(expected);
  });

  it('renders copy button when data exists', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"@type":"Article"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const btn = el.shadowRoot!.querySelector('uui-button[label="Copy to clipboard"]');
    expect(btn).to.exist;
  });

  it('shows Valid badge when isValid is true', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"@type":"Article"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const badge = el.shadowRoot!.querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.textContent!.trim()).to.equal('Valid');
    expect(badge!.getAttribute('color')).to.equal('positive');
  });

  it('shows Invalid badge and errors when isValid is false', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{}',
      isValid: false,
      errors: ['Missing @type'],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const badge = el.shadowRoot!.querySelector('uui-badge');
    expect(badge).to.exist;
    expect(badge!.textContent!.trim()).to.equal('Invalid');
    expect(badge!.getAttribute('color')).to.equal('danger');

    const errors = el.shadowRoot!.querySelectorAll('.error-item');
    expect(errors.length).to.equal(1);
    expect(errors[0].textContent).to.contain('Missing @type');
  });
});
