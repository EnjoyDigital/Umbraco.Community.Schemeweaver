import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { fetchApi } from '../core-bridge/data-source.js';
import type { ContentTypeProperty } from '../core-bridge/types.js';
import { CORE_API_BASE, SEO_API_BASE } from '../constants.js';
import type {
  SeoContentTypeStatus,
  SeoHealthReport,
  SeoMappingSaveRequest,
  SeoPageOverride,
  SeoPageOverrideRequest,
  SeoPageSummary,
  SeoProjection,
  SeoSamplePage,
  SeoServerContext,
  SeoSetupRequest,
  SeoTemplateCatalogue,
  SiteIdentity,
} from '../api/seo-types.js';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

/**
 * HTTP client for the SEO management API (and the two core endpoints the section needs).
 * Reuses the core's authenticated fetch helper, so session expiry and error toasts behave
 * identically in both bundles.
 */
export class SeoServerDataSource {
  #host: UmbControllerHost;

  constructor(host: UmbControllerHost) {
    this.#host = host;
  }

  getTemplates() {
    return fetchApi<SeoTemplateCatalogue>(this.#host, `${SEO_API_BASE}/templates`);
  }

  getServerContext() {
    return fetchApi<SeoServerContext>(this.#host, `${SEO_API_BASE}/server-context`);
  }

  getSiteIdentity() {
    return fetchApi<SiteIdentity>(this.#host, `${SEO_API_BASE}/site-identity`);
  }

  saveSiteIdentity(identity: SiteIdentity) {
    return fetchApi<SiteIdentity>(this.#host, `${SEO_API_BASE}/site-identity`, {
      method: 'PUT',
      headers: JSON_HEADERS,
      body: JSON.stringify(identity),
    });
  }

  getContentTypes() {
    return fetchApi<SeoContentTypeStatus[]>(this.#host, `${SEO_API_BASE}/content-types`);
  }

  getContentType(alias: string) {
    return fetchApi<SeoContentTypeStatus>(this.#host, `${SEO_API_BASE}/content-types/${encodeURIComponent(alias)}`, {}, true);
  }

  setup(alias: string, request: SeoSetupRequest) {
    return fetchApi<SeoProjection>(this.#host, `${SEO_API_BASE}/content-types/${encodeURIComponent(alias)}/setup`, {
      method: 'POST',
      headers: JSON_HEADERS,
      body: JSON.stringify(request),
    });
  }

  saveMapping(alias: string, request: SeoMappingSaveRequest) {
    return fetchApi<SeoContentTypeStatus>(this.#host, `${SEO_API_BASE}/content-types/${encodeURIComponent(alias)}/mapping`, {
      method: 'PUT',
      headers: JSON_HEADERS,
      body: JSON.stringify(request),
    });
  }

  deleteMapping(alias: string) {
    return fetchApi<void>(this.#host, `${SEO_API_BASE}/content-types/${encodeURIComponent(alias)}/mapping`, { method: 'DELETE' });
  }

  getSamplePages(alias: string, take = 5) {
    return fetchApi<SeoSamplePage[]>(this.#host, `${SEO_API_BASE}/content-types/${encodeURIComponent(alias)}/sample-pages?take=${take}`);
  }

  /** Core endpoint: the fields of a content type (built-ins first), used to populate slot pickers. */
  getContentTypeProperties(alias: string) {
    return fetchApi<ContentTypeProperty[]>(this.#host, `${CORE_API_BASE}/content-types/${encodeURIComponent(alias)}/properties`);
  }

  getPageOverride(contentKey: string) {
    return fetchApi<SeoPageOverride>(this.#host, `${SEO_API_BASE}/pages/${contentKey}`, {}, true);
  }

  savePageOverride(contentKey: string, request: SeoPageOverrideRequest) {
    return fetchApi<SeoPageOverride>(this.#host, `${SEO_API_BASE}/pages/${contentKey}`, {
      method: 'PUT',
      headers: JSON_HEADERS,
      body: JSON.stringify(request),
    });
  }

  deletePageOverride(contentKey: string) {
    return fetchApi<void>(this.#host, `${SEO_API_BASE}/pages/${contentKey}`, { method: 'DELETE' });
  }

  getPageSummary(contentKey: string, culture?: string) {
    const query = culture ? `?culture=${encodeURIComponent(culture)}` : '';
    return fetchApi<SeoPageSummary>(this.#host, `${SEO_API_BASE}/pages/${contentKey}/summary${query}`, {}, true);
  }

  runHealthCheck(force = false) {
    return fetchApi<SeoHealthReport>(this.#host, `${SEO_API_BASE}/health-check?force=${force}`, { method: 'POST' });
  }
}
