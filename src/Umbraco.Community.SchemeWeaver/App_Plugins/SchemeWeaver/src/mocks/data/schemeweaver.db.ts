import type { SchemaMappingDto, SchemaTypeInfo, SchemaPropertyInfo, ContentTypeInfo, PropertyMappingSuggestion, JsonLdPreviewResponse, BlockElementTypeInfo } from '../../api/types.js';

interface ContentTypeWithProperties extends ContentTypeInfo {
  properties?: Array<{ alias: string; editorAlias: string }>;
}

/** Pre-built resolver configs for popular schema patterns */
const POPULAR_RESOLVER_CONFIGS: Record<string, string> = {
  'FAQPage.mainEntity': JSON.stringify({
    nestedMappings: [
      { schemaProperty: 'name', contentProperty: 'question' },
      { schemaProperty: 'acceptedAnswer', contentProperty: 'answer', wrapInType: 'Answer', wrapInProperty: 'Text' },
    ],
  }),
  'Recipe.recipeInstructions': JSON.stringify({
    nestedMappings: [
      { schemaProperty: 'name', contentProperty: 'stepName' },
      { schemaProperty: 'text', contentProperty: 'stepText' },
    ],
  }),
  'Product.review': JSON.stringify({
    nestedMappings: [
      { schemaProperty: 'author', contentProperty: 'reviewAuthor' },
      { schemaProperty: 'reviewRating', contentProperty: 'ratingValue' },
      { schemaProperty: 'reviewBody', contentProperty: 'reviewBody' },
    ],
  }),
};

/** Mock block element types keyed by "contentTypeAlias.propertyAlias" */
const BLOCK_ELEMENT_TYPES: Record<string, BlockElementTypeInfo[]> = {
  'faqPage.faqItems': [
    { alias: 'faqItem', name: 'FAQ Item', properties: ['question', 'answer'] },
  ],
  'faqPage.questions': [
    { alias: 'faqItem', name: 'FAQ Item', properties: ['question', 'answer'] },
  ],
  'recipePage.ingredients': [
    { alias: 'ingredientItem', name: 'Ingredient Item', properties: ['ingredientName', 'quantity', 'unit'] },
  ],
  'recipePage.instructions': [
    { alias: 'instructionStep', name: 'Instruction Step', properties: ['stepName', 'stepText'] },
  ],
  'productPage.reviews': [
    { alias: 'reviewItem', name: 'Review Item', properties: ['reviewAuthor', 'reviewBody', 'ratingValue', 'datePublished'] },
  ],
};

const DEFAULT_MAPPINGS: SchemaMappingDto[] = [
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
        resolverConfig: null,
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
        resolverConfig: null,
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
        resolverConfig: null,
      },
    ],
  },
];

class SchemeWeaverMockDb {
  private _mappings: SchemaMappingDto[] = [...DEFAULT_MAPPINGS];

  private _contentTypes: ContentTypeWithProperties[] = [
    {
      alias: 'blogArticle',
      name: 'Blog Article',
      key: '00000000-0000-0000-0000-000000000001',
      propertyCount: 5,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'authorName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'publishDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'summary', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'faqPage',
      name: 'FAQ Page',
      key: '00000000-0000-0000-0000-000000000002',
      propertyCount: 4,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'questions', editorAlias: 'Umbraco.BlockList' },
        { alias: 'faqItems', editorAlias: 'Umbraco.BlockList' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
      ],
    },
    {
      alias: 'productPage',
      name: 'Product Page',
      key: '00000000-0000-0000-0000-000000000003',
      propertyCount: 10,
      properties: [
        { alias: 'productName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'price', editorAlias: 'Umbraco.Decimal' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'sku', editorAlias: 'Umbraco.TextBox' },
        { alias: 'brand', editorAlias: 'Umbraco.TextBox' },
        { alias: 'availability', editorAlias: 'Umbraco.TextBox' },
        { alias: 'currency', editorAlias: 'Umbraco.TextBox' },
        { alias: 'productImage', editorAlias: 'Umbraco.MediaPicker3' },
        { alias: 'reviews', editorAlias: 'Umbraco.BlockList' },
      ],
    },
    {
      alias: 'contactPage',
      name: 'Contact Page',
      key: '00000000-0000-0000-0000-000000000004',
      propertyCount: 8,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'telephone', editorAlias: 'Umbraco.TextBox' },
        { alias: 'email', editorAlias: 'Umbraco.TextBox' },
        { alias: 'streetAddress', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressLocality', editorAlias: 'Umbraco.TextBox' },
        { alias: 'postalCode', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressCountry', editorAlias: 'Umbraco.TextBox' },
        { alias: 'openingHours', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'eventPage',
      name: 'Event Page',
      key: '00000000-0000-0000-0000-000000000005',
      propertyCount: 10,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'startDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'endDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'locationName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'locationAddress', editorAlias: 'Umbraco.TextBox' },
        { alias: 'organiserName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'ticketPrice', editorAlias: 'Umbraco.Decimal' },
        { alias: 'ticketUrl', editorAlias: 'Umbraco.TextBox' },
        { alias: 'eventImage', editorAlias: 'Umbraco.MediaPicker3' },
      ],
    },
    {
      alias: 'recipePage',
      name: 'Recipe Page',
      key: '00000000-0000-0000-0000-000000000006',
      propertyCount: 14,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'prepTime', editorAlias: 'Umbraco.TextBox' },
        { alias: 'cookTime', editorAlias: 'Umbraco.TextBox' },
        { alias: 'totalTime', editorAlias: 'Umbraco.TextBox' },
        { alias: 'recipeYield', editorAlias: 'Umbraco.TextBox' },
        { alias: 'calories', editorAlias: 'Umbraco.TextBox' },
        { alias: 'recipeCategory', editorAlias: 'Umbraco.TextBox' },
        { alias: 'recipeCuisine', editorAlias: 'Umbraco.TextBox' },
        { alias: 'authorName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'recipeImage', editorAlias: 'Umbraco.MediaPicker3' },
        { alias: 'ingredients', editorAlias: 'Umbraco.BlockList' },
        { alias: 'instructions', editorAlias: 'Umbraco.BlockList' },
      ],
    },
  ];

  private _schemaTypes: SchemaTypeInfo[] = [
    { name: 'Article', description: 'An article, such as a news article or blog post.', parentTypeName: 'CreativeWork', propertyCount: 5 },
    { name: 'BlogPosting', description: 'A blog post.', parentTypeName: 'Article', propertyCount: 5 },
    { name: 'FAQPage', description: 'A page with frequently asked questions.', parentTypeName: 'WebPage', propertyCount: 3 },
    { name: 'Product', description: 'A product offered for sale.', parentTypeName: 'Thing', propertyCount: 10 },
    { name: 'WebPage', description: 'A web page.', parentTypeName: 'CreativeWork', propertyCount: 4 },
    { name: 'Organization', description: 'An organization such as a company.', parentTypeName: 'Thing', propertyCount: 4 },
    { name: 'Question', description: 'A specific question.', parentTypeName: 'CreativeWork', propertyCount: 3 },
    { name: 'Person', description: 'A person.', parentTypeName: 'Thing', propertyCount: 4 },
    { name: 'Answer', description: 'An answer to a question.', parentTypeName: 'CreativeWork', propertyCount: 1 },
    { name: 'LocalBusiness', description: 'A local business.', parentTypeName: 'Organization', propertyCount: 6 },
    { name: 'PostalAddress', description: 'A mailing address.', parentTypeName: 'ContactPoint', propertyCount: 5 },
    { name: 'Recipe', description: 'A recipe for cooking.', parentTypeName: 'HowTo', propertyCount: 11 },
    { name: 'HowToStep', description: 'A step in a how-to guide.', parentTypeName: 'ListItem', propertyCount: 2 },
    { name: 'Event', description: 'An event happening at a certain time and location.', parentTypeName: 'Thing', propertyCount: 10 },
    { name: 'Review', description: 'A review of an item.', parentTypeName: 'CreativeWork', propertyCount: 4 },
    { name: 'AggregateRating', description: 'The average rating for a product.', parentTypeName: 'Rating', propertyCount: 4 },
    { name: 'Offer', description: 'An offer to transfer an item.', parentTypeName: 'Intangible', propertyCount: 4 },
  ];

  private _schemaProperties: Record<string, SchemaPropertyInfo[]> = {
    Article: [
      { name: 'headline', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'author', propertyType: 'IOrganization | IPerson', isRequired: false, acceptedTypes: ['Organization', 'Person'], isComplexType: true },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false, acceptedTypes: ['ImageObject'], isComplexType: true },
    ],
    BlogPosting: [
      { name: 'headline', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'author', propertyType: 'IOrganization | IPerson', isRequired: false, acceptedTypes: ['Organization', 'Person'], isComplexType: true },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false, acceptedTypes: ['ImageObject'], isComplexType: true },
    ],
    FAQPage: [
      { name: 'mainEntity', propertyType: 'Question', isRequired: false, acceptedTypes: ['Question'], isComplexType: true },
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Product: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'sku', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'offers', propertyType: 'Offer', isRequired: false, acceptedTypes: ['Offer'], isComplexType: true },
      { name: 'brand', propertyType: 'Organization', isRequired: false, acceptedTypes: ['Organization'], isComplexType: true },
      { name: 'image', propertyType: 'ImageObject', isRequired: false, acceptedTypes: ['ImageObject'], isComplexType: true },
      { name: 'review', propertyType: 'Review', isRequired: false, acceptedTypes: ['Review'], isComplexType: true },
      { name: 'aggregateRating', propertyType: 'AggregateRating', isRequired: false, acceptedTypes: ['AggregateRating'], isComplexType: true },
    ],
    WebPage: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'breadcrumb', propertyType: 'BreadcrumbList', isRequired: false, acceptedTypes: ['BreadcrumbList'], isComplexType: true },
    ],
    Organization: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'logo', propertyType: 'ImageObject', isRequired: false, acceptedTypes: ['ImageObject'], isComplexType: true },
      { name: 'contactPoint', propertyType: 'ContactPoint', isRequired: false, acceptedTypes: ['ContactPoint'], isComplexType: true },
    ],
    Question: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'acceptedAnswer', propertyType: 'Answer', isRequired: false, acceptedTypes: ['Answer'], isComplexType: true },
      { name: 'text', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Person: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'email', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'jobTitle', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Answer: [
      { name: 'text', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    LocalBusiness: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'telephone', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'email', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'address', propertyType: 'PostalAddress', isRequired: false, acceptedTypes: ['PostalAddress'], isComplexType: true },
      { name: 'openingHoursSpecification', propertyType: 'OpeningHoursSpecification', isRequired: false, acceptedTypes: ['OpeningHoursSpecification'], isComplexType: true },
      { name: 'geo', propertyType: 'GeoCoordinates', isRequired: false, acceptedTypes: ['GeoCoordinates'], isComplexType: true },
    ],
    PostalAddress: [
      { name: 'streetAddress', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'addressLocality', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'addressRegion', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'postalCode', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'addressCountry', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Recipe: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'recipeIngredient', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'recipeInstructions', propertyType: 'HowToStep', isRequired: false, acceptedTypes: ['HowToStep'], isComplexType: true },
      { name: 'nutrition', propertyType: 'NutritionInformation', isRequired: false, acceptedTypes: ['NutritionInformation'], isComplexType: true },
      { name: 'author', propertyType: 'IPerson', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
      { name: 'prepTime', propertyType: 'Duration', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'cookTime', propertyType: 'Duration', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'totalTime', propertyType: 'Duration', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'recipeYield', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'recipeCategory', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'recipeCuisine', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    HowToStep: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'text', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Event: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'startDate', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'endDate', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'location', propertyType: 'Place', isRequired: false, acceptedTypes: ['Place'], isComplexType: true },
      { name: 'organizer', propertyType: 'IOrganization | IPerson', isRequired: false, acceptedTypes: ['Organization', 'Person'], isComplexType: true },
      { name: 'offers', propertyType: 'Offer', isRequired: false, acceptedTypes: ['Offer'], isComplexType: true },
      { name: 'eventStatus', propertyType: 'EventStatusType', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'eventAttendanceMode', propertyType: 'EventAttendanceModeEnumeration', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'image', propertyType: 'ImageObject', isRequired: false, acceptedTypes: ['ImageObject'], isComplexType: true },
    ],
    Review: [
      { name: 'author', propertyType: 'IPerson', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
      { name: 'reviewBody', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'reviewRating', propertyType: 'Rating', isRequired: false, acceptedTypes: ['Rating'], isComplexType: true },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
    ],
    AggregateRating: [
      { name: 'ratingValue', propertyType: 'Number', isRequired: false, acceptedTypes: ['Number'], isComplexType: false },
      { name: 'reviewCount', propertyType: 'Number', isRequired: false, acceptedTypes: ['Number'], isComplexType: false },
      { name: 'bestRating', propertyType: 'Number', isRequired: false, acceptedTypes: ['Number'], isComplexType: false },
      { name: 'worstRating', propertyType: 'Number', isRequired: false, acceptedTypes: ['Number'], isComplexType: false },
    ],
    Offer: [
      { name: 'price', propertyType: 'Number', isRequired: false, acceptedTypes: ['Number'], isComplexType: false },
      { name: 'priceCurrency', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'availability', propertyType: 'ItemAvailability', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
    ],
  };

  reset(): void {
    this._mappings = [...DEFAULT_MAPPINGS];
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

  getContentTypeProperties(alias: string): Array<{ alias: string; name: string; editorAlias: string; description: string }> {
    return this._contentTypes.find((ct) => ct.alias === alias)?.properties?.map((p) => ({
      alias: p.alias,
      name: p.alias.charAt(0).toUpperCase() + p.alias.slice(1).replace(/([A-Z])/g, ' $1'),
      editorAlias: p.editorAlias,
      description: '',
    })) || [];
  }

  /** Get editor alias for a content type property */
  getEditorAlias(contentTypeAlias: string, propertyAlias: string): string {
    const ct = this._contentTypes.find((ct) => ct.alias === contentTypeAlias);
    if (!ct?.properties) return '';
    const prop = ct.properties.find((p) => p.alias === propertyAlias);
    return prop?.editorAlias || '';
  }

  /** Get all property details for a content type (with editor aliases) */
  getContentTypePropertyDetails(alias: string): Array<{ alias: string; editorAlias: string }> {
    return this._contentTypes.find((ct) => ct.alias === alias)?.properties || [];
  }

  /** Get block element types for a content type's block list property */
  getBlockElementTypes(contentTypeAlias: string, propertyAlias: string): BlockElementTypeInfo[] {
    const key = `${contentTypeAlias}.${propertyAlias}`;
    return BLOCK_ELEMENT_TYPES[key] || [];
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

  /** Returns flat array of PropertyMappingSuggestion (matching C# API) with complex type intelligence */
  autoMap(alias: string, schemaType: string): PropertyMappingSuggestion[] {
    const contentType = this._contentTypes.find((ct) => ct.alias === alias);
    const schemaProps = this._schemaProperties[schemaType];
    if (!contentType || !schemaProps) return [];

    return schemaProps.map((prop) => {
      const matchedProp = contentType.properties?.find((p) => p.alias.toLowerCase().includes(prop.name.toLowerCase()));

      // Check for pre-built resolver config
      const configKey = `${schemaType}.${prop.name}`;
      const resolverConfig = POPULAR_RESOLVER_CONFIGS[configKey] || undefined;

      // Complex type intelligence
      if (prop.isComplexType && matchedProp?.editorAlias === 'Umbraco.BlockList') {
        return {
          schemaPropertyName: prop.name,
          schemaPropertyType: prop.propertyType,
          suggestedContentTypePropertyAlias: matchedProp.alias,
          suggestedSourceType: 'blockContent',
          confidence: 70,
          isAutoMapped: true,
          editorAlias: matchedProp.editorAlias,
          acceptedTypes: prop.acceptedTypes,
          isComplexType: prop.isComplexType,
          suggestedNestedSchemaTypeName: prop.acceptedTypes[0],
          suggestedResolverConfig: resolverConfig,
        };
      }

      if (prop.isComplexType && !matchedProp) {
        return {
          schemaPropertyName: prop.name,
          schemaPropertyType: prop.propertyType,
          suggestedContentTypePropertyAlias: null,
          suggestedSourceType: 'complexType',
          confidence: 60,
          isAutoMapped: false,
          editorAlias: null,
          acceptedTypes: prop.acceptedTypes,
          isComplexType: prop.isComplexType,
          suggestedNestedSchemaTypeName: prop.acceptedTypes[0],
          suggestedResolverConfig: resolverConfig,
        };
      }

      return {
        schemaPropertyName: prop.name,
        schemaPropertyType: prop.propertyType,
        suggestedContentTypePropertyAlias: matchedProp?.alias || null,
        suggestedSourceType: 'property',
        confidence: matchedProp ? 80 : 30,
        isAutoMapped: !!matchedProp,
        editorAlias: matchedProp?.editorAlias || null,
        acceptedTypes: prop.acceptedTypes,
        isComplexType: prop.isComplexType,
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
