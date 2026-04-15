import { http, HttpResponse } from 'msw';
import { schemeWeaverDb } from './data/schemeweaver.db.js';
import type { SchemaMappingDto } from '../api/types.js';

const BASE = '/umbraco/management/api/v1/schemeweaver';

export const handlers = [
  http.get(`${BASE}/mappings`, () => {
    return HttpResponse.json(schemeWeaverDb.getMappings());
  }),

  http.get(`${BASE}/mappings/:alias`, ({ params }) => {
    const alias = params.alias as string;
    const mapping = schemeWeaverDb.getMappingByAlias(alias);
    if (!mapping) {
      return HttpResponse.json({ error: 'Not found' }, { status: 404 });
    }
    return HttpResponse.json(mapping);
  }),

  http.get(`${BASE}/content-types`, () => {
    return HttpResponse.json(schemeWeaverDb.getContentTypes());
  }),

  http.get(`${BASE}/content-types/:alias/properties/:propertyAlias/block-types`, ({ params }) => {
    const alias = params.alias as string;
    const propertyAlias = params.propertyAlias as string;
    return HttpResponse.json(schemeWeaverDb.getBlockElementTypes(alias, propertyAlias));
  }),

  http.get(`${BASE}/content-types/:alias/properties`, ({ params }) => {
    const alias = params.alias as string;
    return HttpResponse.json(schemeWeaverDb.getContentTypeProperties(alias));
  }),

  http.get(`${BASE}/schema-types`, ({ request }) => {
    const url = new URL(request.url);
    const search = url.searchParams.get('search') || undefined;
    return HttpResponse.json(schemeWeaverDb.getSchemaTypes(search));
  }),

  http.get(`${BASE}/schema-types/:name/properties`, ({ params, request }) => {
    const name = params.name as string;
    const url = new URL(request.url);
    const ranked = url.searchParams.get('ranked') === 'true';
    const props = schemeWeaverDb.getSchemaTypeProperties(name);
    if (!ranked) return HttpResponse.json(props);

    // Mirror C# ranking tiers (see SchemaAutoMapper.RankSchemaProperties):
    // 80 for globally-popular names, 60 for complex types, 30 for the long tail.
    const globalPopular = new Set(['name', 'description', 'image', 'url', 'headline']);
    const ranked_props = props.map((p) => {
      let confidence = 30;
      if (globalPopular.has(p.name.toLowerCase())) confidence = 80;
      else if (p.isComplexType) confidence = 60;
      return { ...p, confidence, isPopular: confidence >= 60 };
    });
    ranked_props.sort((a, b) => b.confidence - a.confidence || a.name.localeCompare(b.name));
    return HttpResponse.json(ranked_props);
  }),

  http.post(`${BASE}/mappings`, async ({ request }) => {
    const body = (await request.json()) as SchemaMappingDto;
    const result = schemeWeaverDb.createMapping(body);
    return HttpResponse.json(result, { status: 201 });
  }),

  http.delete(`${BASE}/mappings/:alias`, ({ params }) => {
    const alias = params.alias as string;
    const deleted = schemeWeaverDb.deleteMapping(alias);
    if (deleted) {
      return new HttpResponse(null, { status: 204 });
    }
    return HttpResponse.json({ error: 'Not found' }, { status: 404 });
  }),

  /** Returns flat array of PropertyMappingSuggestion (matching C# API) */
  http.post(`${BASE}/mappings/:alias/auto-map`, ({ params, request }) => {
    const alias = params.alias as string;
    const url = new URL(request.url);
    const schemaType = url.searchParams.get('schemaTypeName') || '';
    return HttpResponse.json(schemeWeaverDb.autoMap(alias, schemaType));
  }),

  http.post(`${BASE}/mappings/:alias/preview`, ({ params, request }) => {
    const alias = params.alias as string;
    const url = new URL(request.url);
    const culture = url.searchParams.get('culture');
    const result = schemeWeaverDb.preview(alias, culture ?? undefined);
    return HttpResponse.json(result);
  }),

  http.post(`${BASE}/generate-content-type`, async () => {
    return HttpResponse.json({
      key: '00000000-0000-0000-0000-000000000099',
    }, { status: 201 });
  }),
];

// ---------------------------------------------------------------------------
// Error-path overrides. Pass to `worker.use(...)` inside an individual test to
// switch the relevant endpoint into an error mode. They are scoped to a single
// test run and reset by MSW between tests via `worker.resetHandlers()`.
// ---------------------------------------------------------------------------

/** All mapping GETs return 404 — exercises the empty-state / not-found paths. */
export const mappingNotFoundHandlers = [
  http.get(`${BASE}/mappings`, () => HttpResponse.json([], { status: 200 })),
  http.get(`${BASE}/mappings/:alias`, () =>
    HttpResponse.json({ error: 'Not found' }, { status: 404 }),
  ),
];

/** Save / auto-map endpoints return 400 — exercises client-side validation error handling. */
export const validationErrorHandlers = [
  http.post(`${BASE}/mappings`, () =>
    HttpResponse.json({ error: 'ContentTypeAlias is required.' }, { status: 400 }),
  ),
  http.post(`${BASE}/mappings/:alias/auto-map`, () =>
    HttpResponse.json({ error: 'schemaTypeName query parameter is required.' }, { status: 400 }),
  ),
];

/** Every endpoint returns 500 — exercises generic error handling in the UI. */
export const serverErrorHandlers = [
  http.get(`${BASE}/mappings`, () =>
    HttpResponse.json({ error: 'Internal server error' }, { status: 500 }),
  ),
  http.get(`${BASE}/mappings/:alias`, () =>
    HttpResponse.json({ error: 'Internal server error' }, { status: 500 }),
  ),
  http.post(`${BASE}/mappings`, () =>
    HttpResponse.json({ error: 'Internal server error' }, { status: 500 }),
  ),
  http.post(`${BASE}/mappings/:alias/preview`, () =>
    HttpResponse.json({ error: 'Internal server error' }, { status: 500 }),
  ),
];
