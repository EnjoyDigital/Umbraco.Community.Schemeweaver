import { expect } from '@playwright/test';
import { test } from '@umbraco/playwright-testhelpers';

/**
 * E2E coverage of NESTED block support (a block whose own property is itself a
 * Block List — a block inside a block). Follows the API-driven pattern from
 * `mappings-crud.spec.ts`: every request goes through `umbracoUi.page.request`
 * (which inherits the authenticated backoffice session) so the tests are stable
 * against backoffice DOM changes.
 *
 * Fixtures (created by the F workstream, committed to the TestHost uSync seed):
 *   - doc type `nestedBlocksPage` with a `sections` Block List of `section`
 *   - element type `section` (heading + a nested `questions` Block List of `faqItem`)
 *   - element type `faqItem` (question + answer)
 *   - a published "Nested Blocks Demo" node with one section containing two FAQs
 *
 * The mapping routes the page's `sections` blocks to schema.org WebPage `hasPart`,
 * and each section's nested `questions` blocks to that WebPage's `mainEntity` as
 * Question objects — exercising the recursive resolver end to end.
 */

const BASE = '/umbraco/management/api/v1/schemeweaver';
const DELIVERY = '/umbraco/delivery/api/v2/content';

const PAGE_ALIAS = 'nestedBlocksPage';
const SECTIONS_PROPERTY = 'sections';

// The recursive routes config: a route's property mapping (`questions`) itself
// carries `routes`, nesting the routing model one level deeper.
const NESTED_RESOLVER_CONFIG = JSON.stringify({
  routes: [
    {
      blockAlias: 'section',
      nestedSchemaType: 'WebPage',
      propertyMappings: [
        { schemaProperty: 'name', contentProperty: 'heading' },
        {
          schemaProperty: 'mainEntity',
          contentProperty: 'questions',
          routes: [
            {
              blockAlias: 'faqItem',
              nestedSchemaType: 'Question',
              propertyMappings: [
                { schemaProperty: 'name', contentProperty: 'question' },
                {
                  schemaProperty: 'acceptedAnswer',
                  contentProperty: 'answer',
                  wrapInType: 'Answer',
                  wrapInProperty: 'text',
                },
              ],
            },
          ],
        },
      ],
    },
  ],
});

function buildPayload(contentTypeKey: string) {
  return {
    contentTypeAlias: PAGE_ALIAS,
    contentTypeKey,
    schemaTypeName: 'WebPage',
    isEnabled: true,
    isInherited: false,
    propertyMappings: [
      {
        schemaPropertyName: 'name',
        sourceType: 'property',
        contentTypePropertyAlias: '__name',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: false,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
        dynamicRootConfig: null,
      },
      {
        schemaPropertyName: 'hasPart',
        sourceType: 'blockContent',
        contentTypePropertyAlias: SECTIONS_PROPERTY,
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: false,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: NESTED_RESOLVER_CONFIG,
        dynamicRootConfig: null,
      },
    ],
  };
}

/** Resolve the content-type GUID key for a doc-type alias via the management API. */
async function contentTypeKeyFor(umbracoUi: any, alias: string): Promise<string> {
  const res = await umbracoUi.page.request.get(`${BASE}/content-types`);
  expect(res.ok(), `content-types GET failed: ${res.status()}`).toBeTruthy();
  const body = await res.json();
  const items: Array<{ alias: string; key: string }> = Array.isArray(body) ? body : body.items ?? [];
  const match = items.find((c) => c.alias === alias);
  expect(match, `content type '${alias}' not found — is the nested-block fixture seeded?`).toBeTruthy();
  return match!.key;
}

/** Find the published Nested Blocks Demo node's key via the Delivery API. */
async function nestedDemoContentKey(umbracoUi: any): Promise<string> {
  const res = await umbracoUi.page.request.get(`${DELIVERY}?filter=contentType:${PAGE_ALIAS}&take=1`);
  expect(res.ok(), `Delivery API query failed: ${res.status()}`).toBeTruthy();
  const body = await res.json();
  const item = (body.items ?? [])[0];
  expect(item, `no published '${PAGE_ALIAS}' content found — is the demo node published?`).toBeTruthy();
  return item.id as string;
}

test.describe('Nested blocks (E2E)', () => {
  test('nested routes resolverConfig round-trips through save → get', async ({ umbracoUi }) => {
    const contentTypeKey = await contentTypeKeyFor(umbracoUi, PAGE_ALIAS);

    try {
      const save = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: buildPayload(contentTypeKey) });
      expect(save.ok(), `POST failed: ${save.status()}`).toBeTruthy();

      const get = await umbracoUi.page.request.get(`${BASE}/mappings/${PAGE_ALIAS}`);
      expect(get.ok(), `GET failed: ${get.status()}`).toBeTruthy();
      const saved = await get.json();

      const blockMapping = saved.propertyMappings.find(
        (m: any) => m.sourceType === 'blockContent' && m.contentTypePropertyAlias === SECTIONS_PROPERTY,
      );
      expect(blockMapping, 'block mapping for sections missing').toBeTruthy();

      const config = JSON.parse(blockMapping.resolverConfig);
      expect(config.routes).toHaveLength(1);
      expect(config.routes[0].blockAlias).toBe('section');

      // The crux: the section route's `questions` property mapping carries its
      // OWN nested routes (faqItem → Question) — proving the recursive shape
      // persisted intact.
      const nested = config.routes[0].propertyMappings.find((p: any) => p.contentProperty === 'questions');
      expect(nested, 'nested questions mapping missing').toBeTruthy();
      expect(nested.routes).toHaveLength(1);
      expect(nested.routes[0].blockAlias).toBe('faqItem');
      expect(nested.routes[0].nestedSchemaType).toBe('Question');
    } finally {
      await umbracoUi.page.request.delete(`${BASE}/mappings/${PAGE_ALIAS}`).catch(() => undefined);
    }
  });

  test('preview emits nested Question Things from blocks inside blocks', async ({ umbracoUi }) => {
    const contentTypeKey = await contentTypeKeyFor(umbracoUi, PAGE_ALIAS);
    const contentKey = await nestedDemoContentKey(umbracoUi);

    try {
      const save = await umbracoUi.page.request.post(`${BASE}/mappings`, { data: buildPayload(contentTypeKey) });
      expect(save.ok(), `POST failed: ${save.status()}`).toBeTruthy();

      const preview = await umbracoUi.page.request.post(
        `${BASE}/mappings/${PAGE_ALIAS}/preview?contentKey=${contentKey}`,
      );
      expect(preview.ok(), `preview failed: ${preview.status()}`).toBeTruthy();
      const result = await preview.json();
      const jsonLd = JSON.stringify(result);

      // Outer page → WebPage with a hasPart section (block level 1)
      expect(jsonLd).toContain('WebPage');
      expect(jsonLd).toContain('General Questions');
      // Nested blocks (block level 2): the FAQ Questions resolved from the
      // section's OWN nested `questions` Block List.
      expect(jsonLd).toContain('Question');
      expect(jsonLd).toContain('What is SchemeWeaver?');
      expect(jsonLd).toContain('Does it support nested blocks?');
      expect(jsonLd).toContain('Answer');
    } finally {
      await umbracoUi.page.request.delete(`${BASE}/mappings/${PAGE_ALIAS}`).catch(() => undefined);
    }
  });
});
