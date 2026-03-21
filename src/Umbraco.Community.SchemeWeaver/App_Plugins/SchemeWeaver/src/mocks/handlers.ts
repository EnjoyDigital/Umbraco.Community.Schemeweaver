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

  http.get(`${BASE}/schema-types/:name/properties`, ({ params }) => {
    const name = params.name as string;
    return HttpResponse.json(schemeWeaverDb.getSchemaTypeProperties(name));
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

  http.post(`${BASE}/mappings/:alias/preview`, ({ params }) => {
    const alias = params.alias as string;
    const result = schemeWeaverDb.preview(alias);
    return HttpResponse.json(result);
  }),

  http.post(`${BASE}/generate-content-type`, async () => {
    return HttpResponse.json({
      key: '00000000-0000-0000-0000-000000000099',
    }, { status: 201 });
  }),
];
