import { expect } from '@open-wc/testing';
import type { SetupWorker } from 'msw/browser';
import { ensureRootContext, startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import { seoDb } from '../mocks/data/seo.db.js';
import type { SeoContentTypeStatus } from '../api/seo-types.js';

describe('SchemeWeaverSeoContext (against the MSW handlers)', () => {
  let worker: SetupWorker;
  before(async () => {
    worker = await startMockServiceWorker();
  });
  after(() => {
    worker.resetHandlers();
    stopMockServiceWorker();
  });
  beforeEach(() => seoDb.reset());

  it('loads and caches the catalogue', async () => {
    const context = ensureRootContext();
    const first = await context.loadCatalogue();
    expect(first?.templates.map((t) => t.id)).to.include('article');
    expect(context.getTemplate('article')?.schemaType).to.equal('Article');
    expect(await context.loadCatalogue()).to.equal(first);
  });

  it('reports the section alias the backend expects', async () => {
    const context = ensureRootContext();
    const sc = await context.loadServerContext();
    expect(sc?.sectionAlias).to.equal('SchemeWeaver.Seo.Section');
  });

  it('runs set-up, saves the slots as a mapping and updates the status row', async () => {
    const context = ensureRootContext();
    await context.loadContentTypes();
    let row: SeoContentTypeStatus | undefined;
    context.contentTypes.subscribe((rows) => (row = rows.find((r) => r.alias === 'blogArticle')));
    expect(row?.status).to.equal('not-set-up');

    const projection = await context.setup('blogArticle', { templateId: 'article', schemaTypeName: 'BlogPosting' });
    expect(projection?.schemaTypeName).to.equal('BlogPosting');
    const headline = projection!.slots.find((s) => s.slot.schemaProperty === 'headline')!;
    expect(headline.value).to.include({ kind: 'property', propertyAlias: 'title' });

    const slots = Object.fromEntries(projection!.slots.map((s) => [s.slot.schemaProperty, s.value]));
    slots.author = { kind: 'property', propertyAlias: 'authorName' };
    const saved = await context.saveMapping('blogArticle', { templateId: 'article', schemaTypeName: 'BlogPosting', slots });
    expect(saved?.status).to.equal('set-up');
    expect(row?.status).to.equal('set-up');
    expect(row?.mapping?.schemaTypeName).to.equal('BlogPosting');

    await context.deleteMapping('blogArticle');
    expect(row?.status).to.equal('not-set-up');
  });

  it('saves and reads page overrides and summaries', async () => {
    const context = ensureRootContext();
    await context.saveMapping('blogArticle', { templateId: 'article', slots: { headline: { kind: 'property', propertyAlias: 'title' } } });
    const key = '11111111-1111-1111-1111-111111111111';
    const before = await context.getPageSummary(key);
    expect(before?.effectiveSchemaTypeName).to.equal('Article');

    await context.savePageOverride(key, { schemaTypeName: 'NewsArticle', isExcluded: false });
    const retyped = await context.getPageSummary(key);
    expect(retyped?.effectiveSchemaTypeName).to.equal('NewsArticle');
    expect(retyped?.preview?.jsonLd).to.contain('"NewsArticle"');

    await context.savePageOverride(key, { isExcluded: true });
    const excluded = await context.getPageSummary(key);
    expect(excluded?.isExcluded).to.equal(true);
    expect(excluded?.preview).to.equal(null);

    await context.deletePageOverride(key);
    expect((await context.getPageSummary(key))?.override).to.equal(null);
  });

  it('rejects an identity without a name and accepts a valid one', async () => {
    const context = ensureRootContext();
    expect(await context.saveSiteIdentity({ kind: 'organisation', name: '', sameAs: [], openingHours: [], isConfigured: false })).to.equal(undefined);
    const saved = await context.saveSiteIdentity({ kind: 'organisation', name: 'Acme', sameAs: ['https://x.com/acme'], openingHours: [], isConfigured: false });
    expect(saved?.isConfigured).to.equal(true);
    expect((await context.loadServerContext())?.siteIdentityConfigured).to.equal(true);
  });
});
