import { expect, fixture, html } from '@open-wc/testing';
import './jsonld-content-view.element.js';
import type { JsonLdContentViewElement } from './jsonld-content-view.element.js';
import type { JsonLdPreviewResponse } from '../api/types.js';

describe('JsonLdContentViewElement', () => {
  it('renders loading state initially', async () => {
    const el = await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    );

    const loader = el.shadowRoot!.querySelector('uui-loader-circle');
    expect(loader).to.exist;
  });

  it('renders empty state when no mapping exists for the content type', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _hasMapping: boolean;
      _loading: boolean;
      updateComplete: Promise<void>;
    } & HTMLElement;

    el._hasMapping = false;
    el._loading = false;
    await el.updateComplete;

    const emptyState = el.shadowRoot!.querySelector('.empty-state');
    expect(emptyState).to.exist;

    const heading = el.shadowRoot!.querySelector('.empty-state h3');
    expect(heading).to.exist;
  });

  it('renders preview with valid tag when preview response is valid', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _hasMapping: boolean;
      _loading: boolean;
      _generating: boolean;
      _preview: JsonLdPreviewResponse;
      updateComplete: Promise<void>;
    } & HTMLElement;

    el._hasMapping = true;
    el._loading = false;
    el._generating = false;
    el._preview = {
      jsonLd: '{"@context":"https://schema.org","@type":"BlogPosting","headline":"Hello"}',
      isValid: true,
      errors: [],
      warnings: [],
    } as JsonLdPreviewResponse;
    await el.updateComplete;

    const tag = el.shadowRoot!.querySelector('uui-tag[color="positive"]');
    expect(tag).to.exist;

    const preview = el.shadowRoot!.querySelector('schemeweaver-jsonld-preview');
    expect(preview).to.exist;
  });

  it('renders invalid tag when preview response has errors', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _hasMapping: boolean;
      _loading: boolean;
      _generating: boolean;
      _preview: JsonLdPreviewResponse;
      updateComplete: Promise<void>;
    } & HTMLElement;

    el._hasMapping = true;
    el._loading = false;
    el._generating = false;
    el._preview = {
      jsonLd: '{}',
      isValid: false,
      errors: ['Missing required property: headline'],
      warnings: [],
    } as JsonLdPreviewResponse;
    await el.updateComplete;

    const tag = el.shadowRoot!.querySelector('uui-tag[color="danger"]');
    expect(tag).to.exist;
  });

  it('renders unpublished message when mapping exists but preview is null', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _hasMapping: boolean;
      _loading: boolean;
      _generating: boolean;
      _unpublished: boolean;
      _preview: null;
      updateComplete: Promise<void>;
    } & HTMLElement;

    el._hasMapping = true;
    el._loading = false;
    el._generating = false;
    el._unpublished = true;
    el._preview = null;
    await el.updateComplete;

    const unpublishedMessage = el.shadowRoot!.querySelector('.unpublished-message');
    expect(unpublishedMessage).to.exist;
  });

  it('exposes _culture state for variant-aware preview', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _culture: string | undefined;
      updateComplete: Promise<void>;
    } & HTMLElement;

    // Default: invariant content has no culture
    expect(el._culture).to.be.undefined;
  });

  it('stores culture when set externally (simulates variant workspace)', async () => {
    const el = (await fixture<JsonLdContentViewElement>(
      html`<schemeweaver-jsonld-content-view></schemeweaver-jsonld-content-view>`,
    )) as unknown as {
      _culture: string | undefined;
      _hasMapping: boolean;
      _loading: boolean;
      _generating: boolean;
      _preview: JsonLdPreviewResponse;
      updateComplete: Promise<void>;
    } & HTMLElement;

    // Simulate variant workspace setting culture to German
    el._culture = 'de-DE';
    el._hasMapping = true;
    el._loading = false;
    el._generating = false;
    el._preview = {
      jsonLd: '{"@context":"https://schema.org","@type":"BlogPosting","headline":"Hallo"}',
      isValid: true,
      errors: [],
      warnings: [],
    } as JsonLdPreviewResponse;
    await el.updateComplete;

    expect(el._culture).to.equal('de-DE');
    // Preview should still render normally
    const preview = el.shadowRoot!.querySelector('schemeweaver-jsonld-preview');
    expect(preview).to.exist;
  });
});
