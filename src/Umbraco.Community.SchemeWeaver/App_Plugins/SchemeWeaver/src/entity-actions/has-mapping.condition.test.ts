import { aTimeout, expect } from '@open-wc/testing';
import { UMB_ENTITY_CONTEXT } from '@umbraco-cms/backoffice/entity';
import { __mockContextRegistry } from '../__mocks__/context-api.js';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import { SchemeWeaverHasMappingCondition } from './has-mapping.condition.js';

const BLOG_ARTICLE_KEY = '00000000-0000-0000-0000-000000000001';
/** faqPage is the seeded UNMAPPED content type in the mock database. */
const FAQ_PAGE_KEY = '00000000-0000-0000-0000-000000000002';

/** Minimal stand-in for UMB_ENTITY_CONTEXT — the condition only observes `unique`. */
function provideEntity(unique: string | undefined) {
  __mockContextRegistry.provide(UMB_ENTITY_CONTEXT, {
    unique: { getValue: () => unique },
  });
}

/**
 * Builds a condition and returns its verdict. `permitted` starts false and the
 * lookup is async, so a true verdict is polled for and a false one is whatever
 * remains once the lookup has had time to land.
 */
async function evaluate(match: boolean): Promise<boolean> {
  const condition = new SchemeWeaverHasMappingCondition(null as any, {
    config: { alias: 'SchemeWeaver.Condition.HasMapping', match } as any,
    onChange: () => {},
  });

  // Short deadline on purpose: a "denied" verdict can only be observed by waiting
  // it out, so a generous budget here is multiplied across every such case.
  for (let waited = 0; waited < 400 && !condition.permitted; waited += 20) {
    await aTimeout(20);
  }
  return condition.permitted;
}

describe('SchemeWeaverHasMappingCondition', () => {
  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('permits the mapped-only extension for a mapped document type', async () => {
    provideEntity(BLOG_ARTICLE_KEY);
    expect(await evaluate(true)).to.be.true;
  });

  it('denies the mapped-only extension for an unmapped document type', async () => {
    provideEntity(FAQ_PAGE_KEY);
    expect(await evaluate(true)).to.be.false;
  });

  it('permits the unmapped-only extension for an unmapped document type', async () => {
    provideEntity(FAQ_PAGE_KEY);
    expect(await evaluate(false)).to.be.true;
  });

  it('denies the unmapped-only extension for a mapped document type', async () => {
    provideEntity(BLOG_ARTICLE_KEY);
    expect(await evaluate(false)).to.be.false;
  });

  // Losing the entry entirely would strand the user with no way to start a
  // mapping, so an indeterminate answer shows "Map to Schema.org".
  it('fails open to the unmapped-only extension when the key is unknown', async () => {
    provideEntity('99999999-9999-4999-8999-999999999999');
    expect(await evaluate(false)).to.be.true;
    expect(await evaluate(true)).to.be.false;
  });

  it('fails open when there is no entity in context', async () => {
    provideEntity(undefined);
    expect(await evaluate(false)).to.be.true;
  });
});
