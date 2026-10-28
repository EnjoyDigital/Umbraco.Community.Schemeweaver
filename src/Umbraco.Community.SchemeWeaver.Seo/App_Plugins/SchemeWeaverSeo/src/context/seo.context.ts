import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbArrayState, UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { SeoServerDataSource } from '../repository/seo.server-data-source.js';
import { SCHEMEWEAVER_SEO_CONTEXT } from './seo.context-token.js';
import type {
  SeoContentTypeStatus,
  SeoHealthReport,
  SeoMappingSaveRequest,
  SeoPageOverrideRequest,
  SeoServerContext,
  SeoSetupRequest,
  SeoTemplate,
  SeoTemplateCatalogue,
  SiteIdentity,
} from '../api/seo-types.js';

/**
 * Root context for the SEO bundle (provided once from the entry point). Holds the catalogue,
 * the server context, the site identity and the content-type statuses as observables so the
 * section views and the page tab share one copy, and wraps every API call.
 */
export class SchemeWeaverSeoContext extends UmbControllerBase {
  #dataSource: SeoServerDataSource;

  public get dataSource(): SeoServerDataSource {
    return this.#dataSource;
  }

  #catalogue = new UmbObjectState<SeoTemplateCatalogue | undefined>(undefined);
  public readonly catalogue = this.#catalogue.asObservable();

  #serverContext = new UmbObjectState<SeoServerContext | undefined>(undefined);
  public readonly serverContext = this.#serverContext.asObservable();

  #siteIdentity = new UmbObjectState<SiteIdentity | undefined>(undefined);
  public readonly siteIdentity = this.#siteIdentity.asObservable();

  #contentTypes = new UmbArrayState<SeoContentTypeStatus>([], (x) => x.alias);
  public readonly contentTypes = this.#contentTypes.asObservable();

  #health = new UmbObjectState<SeoHealthReport | undefined>(undefined);
  public readonly health = this.#health.asObservable();

  #loading = new UmbObjectState<boolean>(false);
  public readonly loading = this.#loading.asObservable();

  constructor(host: UmbControllerHost) {
    super(host);
    this.#dataSource = new SeoServerDataSource(host);
    this.provideContext(SCHEMEWEAVER_SEO_CONTEXT, this);
  }

  async loadCatalogue(): Promise<SeoTemplateCatalogue | undefined> {
    const current = this.#catalogue.getValue();
    if (current) return current;
    const result = await this.#dataSource.getTemplates();
    if (result) this.#catalogue.setValue(result);
    return result;
  }

  getTemplate(id: string | null | undefined): SeoTemplate | undefined {
    if (!id) return undefined;
    return this.#catalogue.getValue()?.templates.find((t) => t.id === id);
  }

  async loadServerContext(): Promise<SeoServerContext | undefined> {
    const result = await this.#dataSource.getServerContext();
    if (result) this.#serverContext.setValue(result);
    return result;
  }

  async loadSiteIdentity(): Promise<SiteIdentity | undefined> {
    const result = await this.#dataSource.getSiteIdentity();
    if (result) this.#siteIdentity.setValue(result);
    return result;
  }

  async saveSiteIdentity(identity: SiteIdentity): Promise<SiteIdentity | undefined> {
    const result = await this.#dataSource.saveSiteIdentity(identity);
    if (result) {
      this.#siteIdentity.setValue(result);
      await this.loadServerContext();
    }
    return result;
  }

  async loadContentTypes(): Promise<SeoContentTypeStatus[] | undefined> {
    this.#loading.setValue(true);
    try {
      const result = await this.#dataSource.getContentTypes();
      if (result) this.#contentTypes.setValue(result);
      return result;
    } finally {
      this.#loading.setValue(false);
    }
  }

  async refreshContentType(alias: string) {
    const result = await this.#dataSource.getContentType(alias);
    if (result) this.#replaceContentType(result);
    return result;
  }

  #replaceContentType(row: SeoContentTypeStatus) {
    const current = this.#contentTypes.getValue();
    const index = current.findIndex((c) => c.alias === row.alias);
    this.#contentTypes.setValue(index < 0 ? [...current, row] : current.map((c) => (c.alias === row.alias ? row : c)));
  }

  setup(alias: string, request: SeoSetupRequest) {
    return this.#dataSource.setup(alias, request);
  }

  async saveMapping(alias: string, request: SeoMappingSaveRequest) {
    const result = await this.#dataSource.saveMapping(alias, request);
    if (result) {
      this.#replaceContentType(result);
      this.#health.setValue(undefined);
    }
    return result;
  }

  async deleteMapping(alias: string) {
    await this.#dataSource.deleteMapping(alias);
    await this.refreshContentType(alias);
    this.#health.setValue(undefined);
  }

  getSamplePages(alias: string) {
    return this.#dataSource.getSamplePages(alias);
  }

  getContentTypeProperties(alias: string) {
    return this.#dataSource.getContentTypeProperties(alias);
  }

  getPageSummary(contentKey: string, culture?: string) {
    return this.#dataSource.getPageSummary(contentKey, culture);
  }

  savePageOverride(contentKey: string, request: SeoPageOverrideRequest) {
    return this.#dataSource.savePageOverride(contentKey, request);
  }

  deletePageOverride(contentKey: string) {
    return this.#dataSource.deletePageOverride(contentKey);
  }

  async runHealthCheck(force = false): Promise<SeoHealthReport | undefined> {
    const result = await this.#dataSource.runHealthCheck(force);
    if (result) this.#health.setValue(result);
    return result;
  }
}
