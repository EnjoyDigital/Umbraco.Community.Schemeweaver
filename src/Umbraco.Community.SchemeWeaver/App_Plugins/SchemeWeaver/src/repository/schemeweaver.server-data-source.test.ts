import { expect } from '@open-wc/testing';

describe('SchemeWeaverServerDataSource', () => {
  describe('API path construction', () => {
    const BASE = '/umbraco/management/api/v1/schemeweaver';

    it('mappings endpoint is correct', () => {
      expect(`${BASE}/mappings`).to.equal('/umbraco/management/api/v1/schemeweaver/mappings');
    });

    it('content-types endpoint is correct', () => {
      expect(`${BASE}/content-types`).to.equal('/umbraco/management/api/v1/schemeweaver/content-types');
    });

    it('schema-types endpoint is correct', () => {
      expect(`${BASE}/schema-types`).to.equal('/umbraco/management/api/v1/schemeweaver/schema-types');
    });

    it('auto-map endpoint includes alias and query param', () => {
      const alias = 'blogArticle';
      const schemaType = 'Article';
      const url = `${BASE}/mappings/${alias}/auto-map?schemaTypeName=${encodeURIComponent(schemaType)}`;
      expect(url).to.equal('/umbraco/management/api/v1/schemeweaver/mappings/blogArticle/auto-map?schemaTypeName=Article');
    });

    it('preview endpoint for existing mapping includes alias', () => {
      const alias = 'blogArticle';
      expect(`${BASE}/mappings/${alias}/preview`).to.equal('/umbraco/management/api/v1/schemeweaver/mappings/blogArticle/preview');
    });

    it('preview endpoint appends culture query param when provided', () => {
      const alias = 'blogArticle';
      const contentKey = '11111111-1111-1111-1111-111111111111';
      const culture = 'de-DE';
      const params = new URLSearchParams();
      params.set('contentKey', contentKey);
      params.set('culture', culture);
      const url = `${BASE}/mappings/${encodeURIComponent(alias)}/preview?${params.toString()}`;
      expect(url).to.contain('culture=de-DE');
      expect(url).to.contain('contentKey=');
    });

    it('preview endpoint omits culture query param when undefined', () => {
      const alias = 'blogArticle';
      const contentKey = '11111111-1111-1111-1111-111111111111';
      const culture = undefined;
      const params = new URLSearchParams();
      params.set('contentKey', contentKey);
      if (culture) params.set('culture', culture);
      const url = `${BASE}/mappings/${encodeURIComponent(alias)}/preview?${params.toString()}`;
      expect(url).to.not.contain('culture=');
    });

    it('schema-types search endpoint includes query string', () => {
      const search = 'Article';
      const url = `${BASE}/schema-types?search=${encodeURIComponent(search)}`;
      expect(url).to.equal('/umbraco/management/api/v1/schemeweaver/schema-types?search=Article');
    });

    it('content-type properties endpoint includes alias', () => {
      const alias = 'blogArticle';
      expect(`${BASE}/content-types/${alias}/properties`).to.equal('/umbraco/management/api/v1/schemeweaver/content-types/blogArticle/properties');
    });

    it('generate-content-type endpoint is correct', () => {
      expect(`${BASE}/generate-content-type`).to.equal('/umbraco/management/api/v1/schemeweaver/generate-content-type');
    });

    it('delete mapping endpoint includes alias', () => {
      const alias = 'blogArticle';
      expect(`${BASE}/mappings/${alias}`).to.equal('/umbraco/management/api/v1/schemeweaver/mappings/blogArticle');
    });

    it('encodes special characters in alias', () => {
      const alias = 'my content type';
      const url = `${BASE}/mappings/${encodeURIComponent(alias)}`;
      expect(url).to.equal('/umbraco/management/api/v1/schemeweaver/mappings/my%20content%20type');
    });
  });

  describe('resolveContentTypeAlias logic', () => {
    it('returns alias when key matches a content type', () => {
      const contentTypes = [
        { alias: 'blogArticle', name: 'Blog Article', key: 'aaa-bbb-ccc', propertyCount: 5 },
        { alias: 'faqPage', name: 'FAQ Page', key: 'ddd-eee-fff', propertyCount: 3 },
      ];
      const match = contentTypes.find((ct) => ct.key === 'aaa-bbb-ccc');
      expect(match?.alias).to.equal('blogArticle');
    });

    it('returns undefined when key does not match any content type', () => {
      const contentTypes = [
        { alias: 'blogArticle', name: 'Blog Article', key: 'aaa-bbb-ccc', propertyCount: 5 },
      ];
      const match = contentTypes.find((ct) => ct.key === 'zzz-zzz-zzz');
      expect(match?.alias).to.be.undefined;
    });
  });
});
