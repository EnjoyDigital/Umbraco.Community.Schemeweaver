import type {
  SeoContentTypeStatus,
  SeoHealthReport,
  SeoMappingSaveRequest,
  SeoPageOverride,
  SeoPageSummary,
  SeoProjection,
  SeoServerContext,
  SeoTemplateCatalogue,
  SiteIdentity,
} from '../../api/seo-types.js';
import type { ContentTypeProperty } from '../../core-bridge/types.js';

const text = (schemaProperty: string, label: string, importance: 'required' | 'recommended' | 'optional', kind = 'text') => ({
  schemaProperty,
  label,
  help: '',
  importance,
  kind: kind as 'text',
  acceptedEditorAliases: kind === 'image' ? ['Umbraco.MediaPicker3'] : kind === 'date' ? ['Umbraco.DateTime'] : ['Umbraco.TextBox', 'Umbraco.TextArea', 'Umbraco.RichText'],
  builtIns: kind === 'date' ? ['__createDate', '__updateDate'] : kind === 'text' ? ['__name'] : [],
  fixedValueOptions: [],
});

export const CATALOGUE: SeoTemplateCatalogue = {
  templates: [
    {
      id: 'article',
      label: 'Article or blog post',
      description: 'News articles, blog posts and editorial content.',
      family: 'content',
      icon: 'icon-article',
      schemaType: 'Article',
      variants: [
        { schemaType: 'Article', label: 'Article' },
        { schemaType: 'BlogPosting', label: 'Blog post' },
        { schemaType: 'NewsArticle', label: 'News article' },
      ],
      useBusinessTypes: false,
      googleFeature: { name: 'Article', docUrl: 'https://developers.google.com/search/docs/appearance/structured-data/article', status: 'rich-result' },
      aliasHints: ['article', 'blog', 'news'],
      slots: [
        text('headline', 'Headline', 'required'),
        text('image', 'Main image', 'required', 'image'),
        { ...text('datePublished', 'Published date', 'required', 'date'), defaultSource: { sourceType: 'property', alias: '__createDate' } },
        {
          ...text('author', 'Author', 'required', 'entity'),
          acceptedEditorAliases: ['Umbraco.ContentPicker', 'Umbraco.TextBox'],
          nested: { schemaType: 'Person', slots: [text('name', 'Author name', 'required'), text('url', 'Profile URL', 'recommended', 'url')] },
        },
        {
          ...text('publisher', 'Publisher', 'recommended', 'entity'),
          siteIdentityPieceKey: 'organization',
          defaultSource: { sourceType: 'reference', targetPieceKey: 'organization' },
          nested: { schemaType: 'Organization', slots: [text('name', 'Publisher name', 'required')] },
        },
        text('description', 'Summary', 'recommended'),
      ],
    },
    {
      id: 'web-page',
      label: 'Web page',
      description: 'Standard pages.',
      family: 'content',
      icon: 'icon-document',
      schemaType: 'WebPage',
      variants: [
        { schemaType: 'WebPage', label: 'Web page' },
        { schemaType: 'AboutPage', label: 'About page' },
        { schemaType: 'ContactPage', label: 'Contact page' },
      ],
      useBusinessTypes: false,
      googleFeature: { name: 'Web page (foundation)', docUrl: '', status: 'foundation' },
      aliasHints: ['page'],
      slots: [
        { ...text('name', 'Page title', 'required'), defaultSource: { sourceType: 'property', alias: '__name' } },
        { ...text('url', 'Page URL', 'required', 'url'), defaultSource: { sourceType: 'property', alias: '__url' } },
      ],
    },
    {
      id: 'faq',
      label: 'FAQ page',
      description: 'Retired by Google.',
      family: 'content',
      icon: 'icon-help-alt',
      schemaType: 'FAQPage',
      variants: [],
      useBusinessTypes: false,
      googleFeature: { name: 'FAQ (retired)', docUrl: '', status: 'retired' },
      aliasHints: ['faq'],
      slots: [text('mainEntity', 'Questions and answers', 'required', 'list')],
    },
  ],
  retired: [{ schemaType: 'FAQPage', name: 'FAQ rich results', retiredOn: '2026-05', docUrl: '' }],
  businessTypes: [
    { schemaType: 'LocalBusiness', label: 'Other local business' },
    { schemaType: 'Dentist', label: 'Dentist' },
  ],
};

const PROPERTIES: Record<string, ContentTypeProperty[]> = {
  blogArticle: [
    { alias: '__name', name: 'Name', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
    { alias: '__url', name: 'URL', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
    { alias: '__createDate', name: 'Create date', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
    { alias: '__updateDate', name: 'Update date', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
    { alias: 'title', name: 'Title', editorAlias: 'Umbraco.TextBox', description: '' },
    { alias: 'body', name: 'Body', editorAlias: 'Umbraco.RichText', description: '' },
    { alias: 'heroImage', name: 'Hero image', editorAlias: 'Umbraco.MediaPicker3', description: '' },
    { alias: 'authorName', name: 'Author name', editorAlias: 'Umbraco.TextBox', description: '' },
    { alias: 'price', name: 'Price', editorAlias: 'Umbraco.Decimal', description: '' },
  ],
};

class SeoMockDb {
  private _identity: SiteIdentity | undefined;
  private _overrides = new Map<string, SeoPageOverride>();
  private _mappings = new Map<string, { templateId: string; schemaTypeName: string; slots: Record<string, unknown> }>();

  reset() {
    this._identity = undefined;
    this._overrides.clear();
    this._mappings.clear();
    this._mappings.set('homePage', { templateId: 'web-page', schemaTypeName: 'WebPage', slots: { name: { kind: 'property', propertyAlias: '__name' }, url: { kind: 'property', propertyAlias: '__url' } } });
  }

  constructor() {
    this.reset();
  }

  getCatalogue() {
    return CATALOGUE;
  }

  getServerContext(): SeoServerContext {
    return {
      sectionAlias: 'SchemeWeaver.Seo.Section',
      siteUrl: 'https://example.com',
      siteIdentityConfigured: !!this._identity?.name,
      hideDeveloperJsonLdTab: true,
      mappingsManagedAsCode: false,
      uSyncBootImport: 'Off',
      exportMappingsToUSyncOnSave: false,
      useGraphModel: true,
      emitBreadcrumbsInDeliveryApi: true,
      publicSiteUrl: null,
      hasPublishedContent: true,
    };
  }

  getSiteIdentity(): SiteIdentity {
    return this._identity ?? { kind: 'organisation', name: '', sameAs: [], openingHours: [], isConfigured: false };
  }

  saveSiteIdentity(identity: SiteIdentity): { ok: true; value: SiteIdentity } | { ok: false; errors: string[] } {
    if (!identity.name?.trim()) return { ok: false, errors: ['A name is required.'] };
    this._identity = { ...identity, isConfigured: true, updatedDate: new Date().toISOString() };
    return { ok: true, value: this._identity };
  }

  getProperties(alias: string) {
    return PROPERTIES[alias] ?? PROPERTIES.blogArticle;
  }

  getContentTypes(): SeoContentTypeStatus[] {
    return [
      this.status('blogArticle', 'Blog article', 'icon-article', 12),
      this.status('homePage', 'Home page', 'icon-home', 1),
      { ...this.status('seoSettings', 'SEO settings', 'icon-settings', 0), hasTemplate: false, allowedAsRoot: false },
      { ...this.status('cta', 'Call to action', 'icon-block', 0), isElement: true, hasTemplate: false },
    ];
  }

  status(alias: string, name: string, icon: string, publishedCount: number): SeoContentTypeStatus {
    const m = this._mappings.get(alias);
    const template = m ? CATALOGUE.templates.find((t) => t.id === m.templateId) : undefined;
    const required = template?.slots.filter((s) => s.importance === 'required') ?? [];
    const filled = required.filter((s) => (m?.slots[s.schemaProperty] as { kind?: string } | undefined)?.kind && (m?.slots[s.schemaProperty] as { kind: string }).kind !== 'none');
    return {
      alias,
      name,
      key: `00000000-0000-0000-0000-${alias.padStart(12, '0').slice(-12)}`,
      icon,
      isElement: false,
      hasTemplate: true,
      allowedAsRoot: alias === 'homePage',
      publishedCount,
      status: !m ? 'not-set-up' : filled.length < required.length ? 'needs-attention' : 'set-up',
      mapping: m && template
        ? {
            schemaTypeName: m.schemaTypeName,
            templateId: template.id,
            templateLabel: template.label,
            isRetired: template.googleFeature.status === 'retired',
            isEnabled: true,
            requiredFilled: filled.length,
            requiredTotal: required.length,
            recommendedFilled: 0,
            recommendedTotal: template.slots.filter((s) => s.importance === 'recommended').length,
            advancedRowCount: 0,
            missingRequired: required.filter((s) => !filled.includes(s)).map((s) => s.schemaProperty),
          }
        : null,
    };
  }

  setup(alias: string, templateId: string, schemaTypeName?: string | null): SeoProjection | undefined {
    const template = CATALOGUE.templates.find((t) => t.id === templateId);
    if (!template) return undefined;
    const existing = this._mappings.get(alias);
    const properties = this.getProperties(alias);
    return {
      templateId,
      schemaTypeName: schemaTypeName ?? template.schemaType,
      hasExistingMapping: !!existing,
      slots: template.slots.map((slot) => {
        const stored = existing?.slots[slot.schemaProperty] as SeoProjection['slots'][number]['value'] | undefined;
        if (stored) return { slot, value: stored };
        const synonym = properties.find((p) => !p.alias.startsWith('__') && slot.acceptedEditorAliases.includes(p.editorAlias) && (slot.schemaProperty === 'headline' ? p.alias === 'title' : slot.schemaProperty === 'image' ? p.alias === 'heroImage' : false));
        if (synonym) return { slot, value: { kind: 'property', propertyAlias: synonym.alias, editorAlias: synonym.editorAlias, confidence: 90, isAutoMapped: true } };
        if (slot.defaultSource?.sourceType === 'property') return { slot, value: { kind: 'property', propertyAlias: slot.defaultSource.alias, editorAlias: 'SchemeWeaver.BuiltIn', isAutoMapped: true } };
        if (slot.defaultSource?.sourceType === 'reference' && this._identity?.name) return { slot, value: { kind: 'siteIdentity', targetPieceKey: slot.defaultSource.targetPieceKey } };
        return { slot, value: { kind: 'none' } };
      }),
      advancedRows: [],
      unslottedSuggestions: [],
    };
  }

  saveMapping(alias: string, request: SeoMappingSaveRequest) {
    const template = CATALOGUE.templates.find((t) => t.id === request.templateId);
    if (!template) return undefined;
    this._mappings.set(alias, { templateId: request.templateId, schemaTypeName: request.schemaTypeName ?? template.schemaType, slots: request.slots });
    return this.getContentTypes().find((c) => c.alias === alias);
  }

  getMapping(alias: string) {
    return this._mappings.get(alias);
  }

  deleteMapping(alias: string) {
    this._mappings.delete(alias);
  }

  getSamplePages(alias: string) {
    return alias === 'blogArticle'
      ? [{ key: '11111111-1111-1111-1111-111111111111', name: 'Hello world', url: 'https://example.com/blog/hello-world/', updateDate: '2026-10-01T10:00:00Z' }]
      : [];
  }

  getPageOverride(contentKey: string) {
    return this._overrides.get(contentKey);
  }

  savePageOverride(contentKey: string, request: { schemaTypeName?: string | null; isExcluded: boolean }) {
    const row: SeoPageOverride = { contentKey, schemaTypeName: request.schemaTypeName ?? null, isExcluded: request.isExcluded, updatedDate: new Date().toISOString() };
    this._overrides.set(contentKey, row);
    return row;
  }

  deletePageOverride(contentKey: string) {
    this._overrides.delete(contentKey);
  }

  getPageSummary(contentKey: string): SeoPageSummary {
    const m = this._mappings.get('blogArticle');
    const o = this._overrides.get(contentKey);
    const type = o?.schemaTypeName ?? m?.schemaTypeName ?? 'Article';
    const excluded = o?.isExcluded ?? false;
    const jsonLd = JSON.stringify({
      '@context': 'https://schema.org',
      '@graph': [
        { '@type': 'WebSite', '@id': 'https://example.com/#website', name: 'Example' },
        ...(excluded ? [] : [{ '@type': type, '@id': `https://example.com/blog/hello-world/#${type.toLowerCase()}`, headline: 'Hello world', datePublished: '2026-10-01', author: { '@type': 'Person', name: 'Jane' } }]),
      ],
    });
    return {
      contentKey,
      contentTypeAlias: 'blogArticle',
      contentTypeName: 'Blog article',
      hasMapping: !!m,
      templateId: m?.templateId ?? null,
      templateLabel: m ? 'Article or blog post' : null,
      defaultSchemaTypeName: m?.schemaTypeName ?? null,
      effectiveSchemaTypeName: excluded ? null : type,
      allowedPageTypes: CATALOGUE.templates[0].variants,
      override: o ?? null,
      isExcluded: excluded,
      isPublished: true,
      publicUrl: 'https://example.com/blog/hello-world/',
      preview: m && !excluded
        ? { jsonLd, isValid: true, errors: [], issues: [{ severity: 'warning', schemaType: type, path: '$.image', message: 'Missing `image`.' }], context: 'backoffice-preview', resolvedBaseUrl: 'https://example.com' }
        : null,
    };
  }

  healthReport(): SeoHealthReport {
    const items = [...this._mappings.entries()].map(([alias]) => ({
      alias,
      name: alias,
      schemaTypeName: this._mappings.get(alias)!.schemaTypeName,
      templateId: this._mappings.get(alias)!.templateId,
      samplePageKey: '11111111-1111-1111-1111-111111111111',
      samplePageName: 'Hello world',
      critical: 0,
      warning: 1,
      issues: [],
      status: 'ok' as const,
    }));
    return { generatedAt: new Date().toISOString(), cached: false, contentTypesChecked: items.length, critical: 0, warning: items.length, items };
  }
}

export const seoDb = new SeoMockDb();
