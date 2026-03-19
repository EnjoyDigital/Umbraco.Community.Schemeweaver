import type { SchemaMappingDto, SchemaTypeInfo, SchemaPropertyInfo, ContentTypeInfo, PropertyMappingSuggestion, JsonLdPreviewResponse } from '../../api/types.js';

class SchemeWeaverMockDb {
  private _mappings: SchemaMappingDto[] = [
    {
      contentTypeAlias: 'blogArticle',
      contentTypeKey: '00000000-0000-0000-0000-000000000001',
      schemaTypeName: 'Article',
      isEnabled: true,
      propertyMappings: [
        {
          schemaPropertyName: 'headline',
          sourceType: 'property',
          contentTypePropertyAlias: 'title',
          sourceContentTypeAlias: null,
          transformType: null,
          isAutoMapped: true,
          staticValue: null,
          nestedSchemaTypeName: null,
        },
        {
          schemaPropertyName: 'author',
          sourceType: 'property',
          contentTypePropertyAlias: 'authorName',
          sourceContentTypeAlias: null,
          transformType: null,
          isAutoMapped: true,
          staticValue: null,
          nestedSchemaTypeName: null,
        },
        {
          schemaPropertyName: 'datePublished',
          sourceType: 'property',
          contentTypePropertyAlias: 'publishDate',
          sourceContentTypeAlias: null,
          transformType: 'formatDate',
          isAutoMapped: true,
          staticValue: null,
          nestedSchemaTypeName: null,
        },
      ],
    },
  ];

  private _contentTypes: (ContentTypeInfo & { properties?: string[] })[] = [
    { alias: 'blogArticle', name: 'Blog Article', key: '00000000-0000-0000-0000-000000000001', propertyCount: 5, properties: ['title', 'authorName', 'publishDate', 'bodyText', 'summary'] },
    { alias: 'faqPage', name: 'FAQ Page', key: '00000000-0000-0000-0000-000000000002', propertyCount: 3, properties: ['title', 'questions', 'bodyText'] },
    { alias: 'productPage', name: 'Product Page', key: '00000000-0000-0000-0000-000000000003', propertyCount: 4, properties: ['productName', 'price', 'description', 'sku'] },
  ];

  private _schemaTypes: SchemaTypeInfo[] = [
    { name: 'Article', description: 'An article, such as a news article or blog post.', parentTypeName: 'CreativeWork', propertyCount: 5 },
    { name: 'BlogPosting', description: 'A blog post.', parentTypeName: 'Article', propertyCount: 5 },
    { name: 'FAQPage', description: 'A page with frequently asked questions.', parentTypeName: 'WebPage', propertyCount: 3 },
    { name: 'Product', description: 'A product offered for sale.', parentTypeName: 'Thing', propertyCount: 6 },
    { name: 'WebPage', description: 'A web page.', parentTypeName: 'CreativeWork', propertyCount: 4 },
    { name: 'Organization', description: 'An organization such as a company.', parentTypeName: 'Thing', propertyCount: 4 },
  ];

  private _schemaProperties: Record<string, SchemaPropertyInfo[]> = {
    Article: [
      { name: 'headline', propertyType: 'Text', isRequired: false },
      { name: 'author', propertyType: 'Person', isRequired: false },
      { name: 'datePublished', propertyType: 'Date', isRequired: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false },
    ],
    BlogPosting: [
      { name: 'headline', propertyType: 'Text', isRequired: false },
      { name: 'author', propertyType: 'Person', isRequired: false },
      { name: 'datePublished', propertyType: 'Date', isRequired: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false },
    ],
    FAQPage: [
      { name: 'mainEntity', propertyType: 'Question', isRequired: false },
      { name: 'name', propertyType: 'Text', isRequired: false },
      { name: 'description', propertyType: 'Text', isRequired: false },
    ],
    Product: [
      { name: 'name', propertyType: 'Text', isRequired: false },
      { name: 'description', propertyType: 'Text', isRequired: false },
      { name: 'sku', propertyType: 'Text', isRequired: false },
      { name: 'price', propertyType: 'Number', isRequired: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false },
      { name: 'brand', propertyType: 'Organization', isRequired: false },
    ],
    WebPage: [
      { name: 'name', propertyType: 'Text', isRequired: false },
      { name: 'description', propertyType: 'Text', isRequired: false },
      { name: 'url', propertyType: 'URL', isRequired: false },
      { name: 'breadcrumb', propertyType: 'BreadcrumbList', isRequired: false },
    ],
    Organization: [
      { name: 'name', propertyType: 'Text', isRequired: false },
      { name: 'url', propertyType: 'URL', isRequired: false },
      { name: 'logo', propertyType: 'ImageObject', isRequired: false },
      { name: 'contactPoint', propertyType: 'ContactPoint', isRequired: false },
    ],
  };

  reset(): void {
    // Re-initialise default mapping
    this._mappings = [
      {
        contentTypeAlias: 'blogArticle',
        contentTypeKey: '00000000-0000-0000-0000-000000000001',
        schemaTypeName: 'Article',
        isEnabled: true,
        propertyMappings: [
          {
            schemaPropertyName: 'headline',
            sourceType: 'property',
            contentTypePropertyAlias: 'title',
            sourceContentTypeAlias: null,
            transformType: null,
            isAutoMapped: true,
            staticValue: null,
            nestedSchemaTypeName: null,
          },
          {
            schemaPropertyName: 'author',
            sourceType: 'property',
            contentTypePropertyAlias: 'authorName',
            sourceContentTypeAlias: null,
            transformType: null,
            isAutoMapped: true,
            staticValue: null,
            nestedSchemaTypeName: null,
          },
          {
            schemaPropertyName: 'datePublished',
            sourceType: 'property',
            contentTypePropertyAlias: 'publishDate',
            sourceContentTypeAlias: null,
            transformType: 'formatDate',
            isAutoMapped: true,
            staticValue: null,
            nestedSchemaTypeName: null,
          },
        ],
      },
    ];
  }

  getMappings(): SchemaMappingDto[] {
    return [...this._mappings];
  }

  getMappingByAlias(alias: string): SchemaMappingDto | undefined {
    return this._mappings.find((m) => m.contentTypeAlias === alias);
  }

  getContentTypes(): ContentTypeInfo[] {
    return this._contentTypes.map(({ alias, name, key, propertyCount }) => ({ alias, name, key, propertyCount }));
  }

  getContentTypeProperties(alias: string): string[] {
    return this._contentTypes.find((ct) => ct.alias === alias)?.properties || [];
  }

  getSchemaTypes(search?: string): SchemaTypeInfo[] {
    if (!search) return [...this._schemaTypes];
    const term = search.toLowerCase();
    return this._schemaTypes.filter(
      (st) => st.name.toLowerCase().includes(term) || (st.description?.toLowerCase().includes(term) ?? false)
    );
  }

  getSchemaTypeProperties(name: string): SchemaPropertyInfo[] {
    return this._schemaProperties[name] || [];
  }

  createMapping(mapping: SchemaMappingDto): SchemaMappingDto {
    const existingIndex = this._mappings.findIndex((m) => m.contentTypeAlias === mapping.contentTypeAlias);
    if (existingIndex >= 0) {
      this._mappings[existingIndex] = mapping;
    } else {
      this._mappings.push(mapping);
    }
    return mapping;
  }

  deleteMapping(alias: string): boolean {
    const index = this._mappings.findIndex((m) => m.contentTypeAlias === alias);
    if (index >= 0) {
      this._mappings.splice(index, 1);
      return true;
    }
    return false;
  }

  /** Returns flat array of PropertyMappingSuggestion (matching C# API) */
  autoMap(alias: string, schemaType: string): PropertyMappingSuggestion[] {
    const contentType = this._contentTypes.find((ct) => ct.alias === alias);
    const schemaProps = this._schemaProperties[schemaType];
    if (!contentType || !schemaProps) return [];

    return schemaProps.map((prop) => {
      const matchedProp = contentType.properties?.find((p) => p.toLowerCase().includes(prop.name.toLowerCase()));
      return {
        schemaPropertyName: prop.name,
        schemaPropertyType: prop.propertyType,
        suggestedContentTypePropertyAlias: matchedProp || null,
        suggestedSourceType: 'property',
        confidence: matchedProp ? 80 : 30,
        isAutoMapped: !!matchedProp,
      };
    });
  }

  preview(alias: string): JsonLdPreviewResponse {
    const mapping = this._mappings.find((m) => m.contentTypeAlias === alias);
    if (!mapping) {
      return { jsonLd: '{}', isValid: false, errors: ['No mapping found'] };
    }

    const result: Record<string, unknown> = {
      '@context': 'https://schema.org',
      '@type': mapping.schemaTypeName,
    };
    for (const pm of mapping.propertyMappings) {
      if (pm.contentTypePropertyAlias || pm.staticValue) {
        result[pm.schemaPropertyName] = pm.staticValue || `[${pm.contentTypePropertyAlias}]`;
      }
    }
    return {
      jsonLd: JSON.stringify(result, null, 2),
      isValid: true,
      errors: [],
    };
  }
}

export const schemeWeaverDb = new SchemeWeaverMockDb();
