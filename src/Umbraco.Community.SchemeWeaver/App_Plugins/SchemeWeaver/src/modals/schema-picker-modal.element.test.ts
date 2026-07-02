import { expect, fixture, html, waitUntil } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import './schema-picker-modal.element.js';

/**
 * The curated shortlist from COMMON_SCHEMA_TYPES in the element, intersected
 * with the mock DB (every curated name exists in the mock DB), in curated order.
 */
const EXPECTED_COMMON_TYPES = [
  'Article',
  'NewsArticle',
  'BlogPosting',
  'WebPage',
  'WebSite',
  'CollectionPage',
  'AboutPage',
  'ContactPage',
  'FAQPage',
  'Product',
  'Offer',
  'Event',
  'Recipe',
  'HowTo',
  'Organization',
  'LocalBusiness',
  'Person',
  'JobPosting',
  'Review',
  'VideoObject',
];

const RESULT_CAP = 50;

async function waitForLoad(el: Element): Promise<void> {
  await waitUntil(
    () => !el.shadowRoot!.querySelector('.loading'),
    'Loading did not complete',
    { timeout: 5000 }
  );
}

/** Result rows only (excludes AI-suggestion rows, which carry no schema-option data-mark). */
function resultItems(el: Element): HTMLElement[] {
  return Array.from(el.shadowRoot!.querySelectorAll('[data-mark^="schemeweaver:schema-option:"]'));
}

function resultNames(el: Element): string[] {
  return resultItems(el).map((item) => item.getAttribute('name') ?? '');
}

/** Type into the search input — filtering is in-memory, so one render pass suffices. */
async function search(el: Element, term: string): Promise<void> {
  const input = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-search"]') as HTMLInputElement;
  input.value = term;
  input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
  await (el as any).updateComplete;
}

describe('SchemaPickerModalElement', () => {
  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('shows loading spinner while fetching', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    const loader = el.shadowRoot!.querySelector('uui-loader-circle');
    expect(loader).to.exist;
  });

  it('renders only the curated common types (in curated order) when there is no query', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    expect(resultNames(el)).to.deep.equal(EXPECTED_COMMON_TYPES);
    // The curated shortlist is never capped.
    expect(el.shadowRoot!.querySelector('.cap-note')).to.not.exist;
  });

  it('filters in memory and caps results at 50 with a cap note', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    // 'a' matches virtually every one of the 108 mock types (name or description).
    await search(el, 'a');
    expect(resultItems(el).length).to.equal(RESULT_CAP);
    expect(el.shadowRoot!.querySelector('.cap-note')).to.exist;
  });

  it('ranks name startsWith before name includes before description includes', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);

    // Tier 1 (startsWith): Event, EventReservation. Tier 2 (includes): MusicEvent etc.
    await search(el, 'event');
    let names = resultNames(el);
    expect(names[0]).to.equal('Event');
    expect(names.indexOf('EventReservation')).to.be.greaterThan(-1);
    expect(names.indexOf('MusicEvent')).to.be.greaterThan(names.indexOf('EventReservation'));

    // All three tiers: startsWith (BlogPosting, Blog), includes (LiveBlogPosting),
    // description includes (Article — "... or blog post."). No cap note under 50.
    await search(el, 'blog');
    names = resultNames(el);
    expect(names).to.deep.equal(['BlogPosting', 'Blog', 'LiveBlogPosting', 'Article']);
    expect(el.shadowRoot!.querySelector('.cap-note')).to.not.exist;
  });

  it('shows the no-results message when nothing matches', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    await search(el, 'zzzz-no-such-type');
    expect(resultItems(el).length).to.equal(0);
    expect(el.shadowRoot!.querySelector('.no-results')).to.exist;
  });

  it('marks the selected type with the selected attribute', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const firstItem = resultItems(el)[0];
    // umb-ref-item fires `selected` (UUI selectable mixin) — the modal binds @selected.
    firstItem.dispatchEvent(new CustomEvent('selected', { bubbles: true }));
    await (el as any).updateComplete;
    expect(firstItem.hasAttribute('selected')).to.be.true;
  });

  it('submit button is disabled when no type selected', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const submitBtn = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-picker-submit"]')!;
    expect(submitBtn.hasAttribute('disabled')).to.be.true;
  });

  it('submit button is enabled after selection', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    resultItems(el)[0].dispatchEvent(new CustomEvent('selected', { bubbles: true }));
    await (el as any).updateComplete;
    const submitBtn = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-picker-submit"]')!;
    expect(submitBtn.hasAttribute('disabled')).to.be.false;
  });

  it('renders Cancel and Select in the body-layout actions slot (pinned footer)', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);
    const actions = el.shadowRoot!.querySelector('[slot="actions"]')!;
    expect(actions).to.exist;
    expect(actions.querySelector('[data-mark="schemeweaver:schema-picker-cancel"]')).to.exist;
    expect(actions.querySelector('[data-mark="schemeweaver:schema-picker-submit"]')).to.exist;
    // The old floating footer hint is gone.
    expect(actions.querySelector('small')).to.not.exist;
  });

  it('submits the unchanged modal value shape { schemaType }', async () => {
    const el = await fixture(html`<schemeweaver-schema-picker-modal></schemeweaver-schema-picker-modal>`);
    await waitForLoad(el);

    let receivedValue: unknown;
    let submitted = false;
    (el as any).modalContext = {
      setValue: (v: unknown) => { receivedValue = v; },
      submit: () => { submitted = true; },
      reject: () => {},
    };

    const firstItem = resultItems(el)[0];
    firstItem.dispatchEvent(new CustomEvent('selected', { bubbles: true }));
    await (el as any).updateComplete;

    const submitBtn = el.shadowRoot!.querySelector('[data-mark="schemeweaver:schema-picker-submit"]') as HTMLElement;
    // Dispatch on the uui-button host (where lit's @click listener lives) — the
    // registered UUI button delegates HTMLElement.click() to its shadow internals.
    submitBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));

    expect(receivedValue).to.deep.equal({ schemaType: firstItem.getAttribute('name') });
    expect(submitted).to.be.true;
  });
});
