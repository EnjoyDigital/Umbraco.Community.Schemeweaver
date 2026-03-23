import { expect, fixture, html } from '@open-wc/testing';
import './jsonld-preview.element.js';
import type { JsonLdPreviewResponse } from '../api/types.js';

describe('JsonLdPreviewElement', () => {
  it('renders empty state when jsonLd is null', async () => {
    const el = await fixture(html`<schemeweaver-jsonld-preview></schemeweaver-jsonld-preview>`);
    const empty = el.shadowRoot!.querySelector('.empty');
    expect(empty).to.exist;
    expect(empty!.textContent).to.not.be.empty;
  });

  it('renders syntax-highlighted JSON when jsonLd is set', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"@context":"https://schema.org","@type":"Article"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const pre = el.shadowRoot!.querySelector('pre.json-code');
    expect(pre).to.exist;
    expect(pre!.innerHTML).to.contain('json-key');
    expect(pre!.innerHTML).to.contain('json-string');
  });

  it('highlights keys and string values with CSS classes', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"name":"Test"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const keys = el.shadowRoot!.querySelectorAll('.json-key');
    const strings = el.shadowRoot!.querySelectorAll('.json-string');
    expect(keys.length).to.be.greaterThan(0);
    expect(strings.length).to.be.greaterThan(0);
  });

  it('highlights numbers, booleans, and null', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"count":42,"active":true,"deleted":null}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const numbers = el.shadowRoot!.querySelectorAll('.json-number');
    const booleans = el.shadowRoot!.querySelectorAll('.json-boolean');
    const nulls = el.shadowRoot!.querySelectorAll('.json-null');
    expect(numbers.length).to.be.greaterThan(0);
    expect(booleans.length).to.be.greaterThan(0);
    expect(nulls.length).to.be.greaterThan(0);
  });

  it('exposes formattedJson as a public getter', async () => {
    const inner = { name: 'Test' };
    const data: JsonLdPreviewResponse = {
      jsonLd: JSON.stringify(inner),
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`) as any;
    const expected = JSON.stringify(inner, null, 2);
    expect(el.formattedJson).to.equal(expected);
  });

  it('shows errors when isValid is false', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{}',
      isValid: false,
      errors: ['Missing @type'],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const errors = el.shadowRoot!.querySelectorAll('.error-item');
    expect(errors.length).to.equal(1);
    expect(errors[0].textContent).to.contain('Missing @type');
  });

  it('does not render errors when there are none', async () => {
    const data: JsonLdPreviewResponse = {
      jsonLd: '{"@type":"Article"}',
      isValid: true,
      errors: [],
    };
    const el = await fixture(html`<schemeweaver-jsonld-preview .jsonLd=${data}></schemeweaver-jsonld-preview>`);
    const errors = el.shadowRoot!.querySelectorAll('.error-item');
    expect(errors.length).to.equal(0);
  });
});
