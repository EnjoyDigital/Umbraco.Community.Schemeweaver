import { http, HttpResponse } from 'msw';
import { seoDb } from './data/seo.db.js';
import type { SeoMappingSaveRequest, SeoPageOverrideRequest, SeoSetupRequest, SiteIdentity } from '../api/seo-types.js';

const SEO = '/umbraco/management/api/v1/schemeweaver-seo';
const CORE = '/umbraco/management/api/v1/schemeweaver';

/**
 * The Umbraco mock backoffice user has a fixed list of allowed sections; add ours so the
 * section and tab conditions pass in the mocked-backoffice tier. Registered last so it wins
 * over Umbraco's own handler (runtime handlers are prepended).
 */
const currentUserHandler = http.get('/umbraco/management/api/v1/user/current', async () => {
  const sections = ['Umb.Section.Content', 'Umb.Section.Media', 'Umb.Section.Settings', 'Umb.Section.Users', 'Umb.Section.Members', 'Umb.Section.Packages', 'SchemeWeaver.Seo.Section'];
  return HttpResponse.json({
    id: '00000000-0000-0000-0000-000000000001',
    email: 'seo@example.com',
    userName: 'seo@example.com',
    name: 'SEO Editor',
    userGroupIds: [],
    languageIsoCode: 'en-US',
    documentStartNodeIds: [],
    hasDocumentRootAccess: true,
    mediaStartNodeIds: [],
    hasMediaRootAccess: true,
    avatarUrls: [],
    languages: [],
    hasAccessToAllLanguages: true,
    hasAccessToSensitiveData: true,
    fallbackPermissions: [],
    permissions: [],
    allowedSections: sections,
    isAdmin: true,
  });
});

export const seoHandlers = [
  currentUserHandler,
  http.get(`${SEO}/templates`, () => HttpResponse.json(seoDb.getCatalogue())),
  http.get(`${SEO}/server-context`, () => HttpResponse.json(seoDb.getServerContext())),
  http.get(`${SEO}/site-identity`, () => HttpResponse.json(seoDb.getSiteIdentity())),
  http.put(`${SEO}/site-identity`, async ({ request }) => {
    const result = seoDb.saveSiteIdentity((await request.json()) as SiteIdentity);
    return result.ok ? HttpResponse.json(result.value) : HttpResponse.json({ error: result.errors.join(' '), errors: result.errors }, { status: 400 });
  }),
  http.get(`${SEO}/content-types`, () => HttpResponse.json(seoDb.getContentTypes())),
  http.get(`${SEO}/content-types/:alias`, ({ params }) => {
    const row = seoDb.getContentTypes().find((c) => c.alias === params.alias);
    return row ? HttpResponse.json(row) : HttpResponse.json({ error: 'Not found' }, { status: 404 });
  }),
  http.post(`${SEO}/content-types/:alias/setup`, async ({ params, request }) => {
    const body = (await request.json()) as SeoSetupRequest;
    const projection = seoDb.setup(params.alias as string, body.templateId, body.schemaTypeName);
    return projection ? HttpResponse.json(projection) : HttpResponse.json({ error: 'Unknown kind' }, { status: 404 });
  }),
  http.put(`${SEO}/content-types/:alias/mapping`, async ({ params, request }) => {
    const status = seoDb.saveMapping(params.alias as string, (await request.json()) as SeoMappingSaveRequest);
    return status ? HttpResponse.json(status) : HttpResponse.json({ error: 'Unknown kind' }, { status: 404 });
  }),
  http.delete(`${SEO}/content-types/:alias/mapping`, ({ params }) => {
    seoDb.deleteMapping(params.alias as string);
    return new HttpResponse(null, { status: 204 });
  }),
  http.get(`${SEO}/content-types/:alias/sample-pages`, ({ params }) => HttpResponse.json(seoDb.getSamplePages(params.alias as string))),
  http.get(`${SEO}/pages/:key`, ({ params }) => {
    const row = seoDb.getPageOverride(params.key as string);
    return row ? HttpResponse.json(row) : HttpResponse.json({ error: 'Not found' }, { status: 404 });
  }),
  http.put(`${SEO}/pages/:key`, async ({ params, request }) =>
    HttpResponse.json(seoDb.savePageOverride(params.key as string, (await request.json()) as SeoPageOverrideRequest))),
  http.delete(`${SEO}/pages/:key`, ({ params }) => {
    seoDb.deletePageOverride(params.key as string);
    return new HttpResponse(null, { status: 204 });
  }),
  http.get(`${SEO}/pages/:key/summary`, ({ params }) => HttpResponse.json(seoDb.getPageSummary(params.key as string))),
  http.post(`${SEO}/health-check`, () => HttpResponse.json(seoDb.healthReport())),
  http.get(`${CORE}/content-types/:alias/properties`, ({ params }) => HttpResponse.json(seoDb.getProperties(params.alias as string))),
];
