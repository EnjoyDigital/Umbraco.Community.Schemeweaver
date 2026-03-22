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
  'Recipe.recipeIngredient': JSON.stringify({
    extractAs: 'stringList',
    contentProperty: 'ingredientName',
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
  'HowTo.step': JSON.stringify({
    nestedMappings: [
      { schemaProperty: 'name', contentProperty: 'stepName' },
      { schemaProperty: 'text', contentProperty: 'stepText' },
    ],
  }),
  'HowTo.tool': JSON.stringify({
    extractAs: 'stringList',
    contentProperty: 'toolName',
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
  'howToPage.howToSteps': [
    { alias: 'howToStep', name: 'How-To Step', properties: ['stepName', 'stepText'] },
  ],
  'howToPage.howToTools': [
    { alias: 'howToTool', name: 'How-To Tool', properties: ['toolName'] },
  ],
  'locationPage.openingHours': [
    { alias: 'openingHoursItem', name: 'Opening Hours', properties: ['dayOfWeek', 'opens', 'closes'] },
  ],
  'restaurantPage.openingHours': [
    { alias: 'openingHoursItem', name: 'Opening Hours', properties: ['dayOfWeek', 'opens', 'closes'] },
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
  {
    contentTypeAlias: 'homePage',
    contentTypeKey: '00000000-0000-0000-0000-000000000007',
    schemaTypeName: 'WebSite',
    isEnabled: true,
    propertyMappings: [
      {
        schemaPropertyName: 'name',
        sourceType: 'property',
        contentTypePropertyAlias: 'siteName',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'url',
        sourceType: 'property',
        contentTypePropertyAlias: 'siteUrl',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'description',
        sourceType: 'property',
        contentTypePropertyAlias: 'siteDescription',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
    ],
  },
  {
    contentTypeAlias: 'videoPage',
    contentTypeKey: '00000000-0000-0000-0000-000000000018',
    schemaTypeName: 'VideoObject',
    isEnabled: true,
    propertyMappings: [
      {
        schemaPropertyName: 'name',
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
        schemaPropertyName: 'description',
        sourceType: 'property',
        contentTypePropertyAlias: 'description',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'thumbnailUrl',
        sourceType: 'property',
        contentTypePropertyAlias: 'thumbnailUrl',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'uploadDate',
        sourceType: 'property',
        contentTypePropertyAlias: 'uploadDate',
        sourceContentTypeAlias: null,
        transformType: 'formatDate',
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'contentUrl',
        sourceType: 'property',
        contentTypePropertyAlias: 'contentUrl',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
    ],
  },
  {
    contentTypeAlias: 'jobPostingPage',
    contentTypeKey: '00000000-0000-0000-0000-000000000019',
    schemaTypeName: 'JobPosting',
    isEnabled: true,
    propertyMappings: [
      {
        schemaPropertyName: 'title',
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
        schemaPropertyName: 'description',
        sourceType: 'property',
        contentTypePropertyAlias: 'description',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'datePosted',
        sourceType: 'property',
        contentTypePropertyAlias: 'datePosted',
        sourceContentTypeAlias: null,
        transformType: 'formatDate',
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'employmentType',
        sourceType: 'property',
        contentTypePropertyAlias: 'employmentType',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
    ],
  },
  {
    contentTypeAlias: 'locationPage',
    contentTypeKey: '00000000-0000-0000-0000-000000000021',
    schemaTypeName: 'LocalBusiness',
    isEnabled: true,
    propertyMappings: [
      {
        schemaPropertyName: 'name',
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
        schemaPropertyName: 'description',
        sourceType: 'property',
        contentTypePropertyAlias: 'description',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'telephone',
        sourceType: 'property',
        contentTypePropertyAlias: 'telephone',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'email',
        sourceType: 'property',
        contentTypePropertyAlias: 'email',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
    ],
  },
  {
    contentTypeAlias: 'howToPage',
    contentTypeKey: '00000000-0000-0000-0000-000000000017',
    schemaTypeName: 'HowTo',
    isEnabled: true,
    propertyMappings: [
      {
        schemaPropertyName: 'name',
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
        schemaPropertyName: 'description',
        sourceType: 'property',
        contentTypePropertyAlias: 'description',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'totalTime',
        sourceType: 'property',
        contentTypePropertyAlias: 'totalTime',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: null,
        resolverConfig: null,
      },
      {
        schemaPropertyName: 'step',
        sourceType: 'blockContent',
        contentTypePropertyAlias: 'howToSteps',
        sourceContentTypeAlias: null,
        transformType: null,
        isAutoMapped: true,
        staticValue: null,
        nestedSchemaTypeName: 'HowToStep',
        resolverConfig: POPULAR_RESOLVER_CONFIGS['HowTo.step'],
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
    // --- New content types ---
    {
      alias: 'homePage',
      name: 'Home Page',
      key: '00000000-0000-0000-0000-000000000007',
      propertyCount: 7,
      properties: [
        { alias: 'siteName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'siteDescription', editorAlias: 'Umbraco.TextArea' },
        { alias: 'siteUrl', editorAlias: 'Umbraco.TextBox' },
        { alias: 'organisationName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'organisationEmail', editorAlias: 'Umbraco.TextBox' },
        { alias: 'organisationTelephone', editorAlias: 'Umbraco.TextBox' },
        { alias: 'sameAs', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'aboutPage',
      name: 'About Page',
      key: '00000000-0000-0000-0000-000000000008',
      propertyCount: 6,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'organisationName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'foundingDate', editorAlias: 'Umbraco.TextBox' },
        { alias: 'numberOfEmployees', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'blogListing',
      name: 'Blog Listing',
      key: '00000000-0000-0000-0000-000000000009',
      propertyCount: 2,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'newsArticle',
      name: 'News Article',
      key: '00000000-0000-0000-0000-000000000010',
      propertyCount: 7,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'authorName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'publishDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'keywords', editorAlias: 'Umbraco.TextBox' },
        { alias: 'dateline', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'techArticle',
      name: 'Tech Article',
      key: '00000000-0000-0000-0000-000000000011',
      propertyCount: 7,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'authorName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'publishDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'proficiencyLevel', editorAlias: 'Umbraco.TextBox' },
        { alias: 'dependencies', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'productListing',
      name: 'Product Listing',
      key: '00000000-0000-0000-0000-000000000012',
      propertyCount: 2,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'softwarePage',
      name: 'Software Page',
      key: '00000000-0000-0000-0000-000000000013',
      propertyCount: 10,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'applicationCategory', editorAlias: 'Umbraco.TextBox' },
        { alias: 'operatingSystem', editorAlias: 'Umbraco.TextBox' },
        { alias: 'softwareVersion', editorAlias: 'Umbraco.TextBox' },
        { alias: 'downloadUrl', editorAlias: 'Umbraco.TextBox' },
        { alias: 'price', editorAlias: 'Umbraco.TextBox' },
        { alias: 'currency', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'coursePage',
      name: 'Course Page',
      key: '00000000-0000-0000-0000-000000000014',
      propertyCount: 9,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'courseCode', editorAlias: 'Umbraco.TextBox' },
        { alias: 'providerName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'duration', editorAlias: 'Umbraco.TextBox' },
        { alias: 'price', editorAlias: 'Umbraco.TextBox' },
        { alias: 'currency', editorAlias: 'Umbraco.TextBox' },
        { alias: 'startDate', editorAlias: 'Umbraco.DateTime' },
      ],
    },
    {
      alias: 'eventListing',
      name: 'Event Listing',
      key: '00000000-0000-0000-0000-000000000015',
      propertyCount: 2,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'recipeListing',
      name: 'Recipe Listing',
      key: '00000000-0000-0000-0000-000000000016',
      propertyCount: 2,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'howToPage',
      name: 'How-To Page',
      key: '00000000-0000-0000-0000-000000000017',
      propertyCount: 6,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'totalTime', editorAlias: 'Umbraco.TextBox' },
        { alias: 'estimatedCost', editorAlias: 'Umbraco.TextBox' },
        { alias: 'howToSteps', editorAlias: 'Umbraco.BlockList' },
        { alias: 'howToTools', editorAlias: 'Umbraco.BlockList' },
      ],
    },
    {
      alias: 'videoPage',
      name: 'Video Page',
      key: '00000000-0000-0000-0000-000000000018',
      propertyCount: 7,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'thumbnailUrl', editorAlias: 'Umbraco.TextBox' },
        { alias: 'uploadDate', editorAlias: 'Umbraco.DateTime' },
        { alias: 'duration', editorAlias: 'Umbraco.TextBox' },
        { alias: 'contentUrl', editorAlias: 'Umbraco.TextBox' },
        { alias: 'embedUrl', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'jobPostingPage',
      name: 'Job Posting Page',
      key: '00000000-0000-0000-0000-000000000019',
      propertyCount: 11,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'datePosted', editorAlias: 'Umbraco.DateTime' },
        { alias: 'validThrough', editorAlias: 'Umbraco.DateTime' },
        { alias: 'employmentType', editorAlias: 'Umbraco.TextBox' },
        { alias: 'hiringOrganisation', editorAlias: 'Umbraco.TextBox' },
        { alias: 'salary', editorAlias: 'Umbraco.TextBox' },
        { alias: 'jobLocationName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'jobLocationAddress', editorAlias: 'Umbraco.TextBox' },
        { alias: 'qualifications', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'profilePage',
      name: 'Profile Page',
      key: '00000000-0000-0000-0000-000000000020',
      propertyCount: 8,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'givenName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'familyName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'jobTitle', editorAlias: 'Umbraco.TextBox' },
        { alias: 'email', editorAlias: 'Umbraco.TextBox' },
        { alias: 'worksFor', editorAlias: 'Umbraco.TextBox' },
        { alias: 'sameAs', editorAlias: 'Umbraco.TextArea' },
      ],
    },
    {
      alias: 'locationPage',
      name: 'Location Page',
      key: '00000000-0000-0000-0000-000000000021',
      propertyCount: 12,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'telephone', editorAlias: 'Umbraco.TextBox' },
        { alias: 'email', editorAlias: 'Umbraco.TextBox' },
        { alias: 'streetAddress', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressLocality', editorAlias: 'Umbraco.TextBox' },
        { alias: 'postalCode', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressCountry', editorAlias: 'Umbraco.TextBox' },
        { alias: 'latitude', editorAlias: 'Umbraco.TextBox' },
        { alias: 'longitude', editorAlias: 'Umbraco.TextBox' },
        { alias: 'priceRange', editorAlias: 'Umbraco.TextBox' },
        { alias: 'openingHours', editorAlias: 'Umbraco.BlockList' },
      ],
    },
    {
      alias: 'restaurantPage',
      name: 'Restaurant Page',
      key: '00000000-0000-0000-0000-000000000022',
      propertyCount: 15,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'telephone', editorAlias: 'Umbraco.TextBox' },
        { alias: 'email', editorAlias: 'Umbraco.TextBox' },
        { alias: 'streetAddress', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressLocality', editorAlias: 'Umbraco.TextBox' },
        { alias: 'postalCode', editorAlias: 'Umbraco.TextBox' },
        { alias: 'addressCountry', editorAlias: 'Umbraco.TextBox' },
        { alias: 'latitude', editorAlias: 'Umbraco.TextBox' },
        { alias: 'longitude', editorAlias: 'Umbraco.TextBox' },
        { alias: 'priceRange', editorAlias: 'Umbraco.TextBox' },
        { alias: 'openingHours', editorAlias: 'Umbraco.BlockList' },
        { alias: 'servesCuisine', editorAlias: 'Umbraco.TextBox' },
        { alias: 'menu', editorAlias: 'Umbraco.TextBox' },
        { alias: 'acceptsReservations', editorAlias: 'Umbraco.TextBox' },
      ],
    },
    {
      alias: 'bookPage',
      name: 'Book Page',
      key: '00000000-0000-0000-0000-000000000023',
      propertyCount: 11,
      properties: [
        { alias: 'title', editorAlias: 'Umbraco.TextBox' },
        { alias: 'description', editorAlias: 'Umbraco.TextArea' },
        { alias: 'bodyText', editorAlias: 'Umbraco.RichText' },
        { alias: 'authorName', editorAlias: 'Umbraco.TextBox' },
        { alias: 'isbn', editorAlias: 'Umbraco.TextBox' },
        { alias: 'bookFormat', editorAlias: 'Umbraco.TextBox' },
        { alias: 'numberOfPages', editorAlias: 'Umbraco.TextBox' },
        { alias: 'publisher', editorAlias: 'Umbraco.TextBox' },
        { alias: 'datePublished', editorAlias: 'Umbraco.DateTime' },
        { alias: 'price', editorAlias: 'Umbraco.TextBox' },
        { alias: 'currency', editorAlias: 'Umbraco.TextBox' },
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
    // --- New schema types ---
    { name: 'WebSite', description: 'A website.', parentTypeName: 'CreativeWork', propertyCount: 4 },
    { name: 'NewsArticle', description: 'A news article.', parentTypeName: 'Article', propertyCount: 6 },
    { name: 'TechArticle', description: 'A technical article with how-to content.', parentTypeName: 'Article', propertyCount: 6 },
    { name: 'VideoObject', description: 'A video file.', parentTypeName: 'MediaObject', propertyCount: 8 },
    { name: 'JobPosting', description: 'A listing that describes a job opening.', parentTypeName: 'Intangible', propertyCount: 9 },
    { name: 'Course', description: 'A description of an educational course.', parentTypeName: 'CreativeWork', propertyCount: 6 },
    { name: 'SoftwareApplication', description: 'A software application.', parentTypeName: 'CreativeWork', propertyCount: 8 },
    { name: 'Book', description: 'A book.', parentTypeName: 'CreativeWork', propertyCount: 8 },
    { name: 'HowTo', description: 'Instructions for how to achieve a result.', parentTypeName: 'CreativeWork', propertyCount: 6 },
    { name: 'Restaurant', description: 'A restaurant.', parentTypeName: 'LocalBusiness', propertyCount: 11 },
    { name: 'AboutPage', description: 'A web page that provides information about the entity.', parentTypeName: 'WebPage', propertyCount: 4 },
    { name: 'ContactPage', description: 'A web page with contact information.', parentTypeName: 'WebPage', propertyCount: 5 },
    { name: 'CollectionPage', description: 'A web page that lists items.', parentTypeName: 'WebPage', propertyCount: 3 },
    { name: 'ProfilePage', description: 'A web page representing a person\'s profile.', parentTypeName: 'WebPage', propertyCount: 4 },
    { name: 'BreadcrumbList', description: 'A breadcrumb trail for a page.', parentTypeName: 'ItemList', propertyCount: 1 },
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
    // --- New schema type properties ---
    WebSite: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'publisher', propertyType: 'Organization', isRequired: false, acceptedTypes: ['Organization'], isComplexType: true },
    ],
    NewsArticle: [
      { name: 'headline', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'author', propertyType: 'IPerson', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'dateline', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'keywords', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    TechArticle: [
      { name: 'headline', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'author', propertyType: 'IPerson', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'articleBody', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'proficiencyLevel', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'dependencies', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    VideoObject: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'thumbnailUrl', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'uploadDate', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'duration', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'contentUrl', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'embedUrl', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
    ],
    JobPosting: [
      { name: 'title', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'datePosted', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'validThrough', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'employmentType', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'hiringOrganization', propertyType: 'Organization', isRequired: false, acceptedTypes: ['Organization'], isComplexType: true },
      { name: 'jobLocation', propertyType: 'Place', isRequired: false, acceptedTypes: ['Place'], isComplexType: true },
      { name: 'baseSalary', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'qualifications', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Course: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'provider', propertyType: 'Organization', isRequired: false, acceptedTypes: ['Organization'], isComplexType: true },
      { name: 'courseCode', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'timeRequired', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
    ],
    SoftwareApplication: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'applicationCategory', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'operatingSystem', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'softwareVersion', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'downloadUrl', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'offers', propertyType: 'Offer', isRequired: false, acceptedTypes: ['Offer'], isComplexType: true },
      { name: 'aggregateRating', propertyType: 'AggregateRating', isRequired: false, acceptedTypes: ['AggregateRating'], isComplexType: true },
    ],
    Book: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'author', propertyType: 'IPerson', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
      { name: 'isbn', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'bookFormat', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'numberOfPages', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'publisher', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'datePublished', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
      { name: 'offers', propertyType: 'Offer', isRequired: false, acceptedTypes: ['Offer'], isComplexType: true },
    ],
    HowTo: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'step', propertyType: 'HowToStep', isRequired: false, acceptedTypes: ['HowToStep'], isComplexType: true },
      { name: 'tool', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'totalTime', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'estimatedCost', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
    ],
    Restaurant: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'telephone', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'email', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'address', propertyType: 'PostalAddress', isRequired: false, acceptedTypes: ['PostalAddress'], isComplexType: true },
      { name: 'geo', propertyType: 'GeoCoordinates', isRequired: false, acceptedTypes: ['GeoCoordinates'], isComplexType: true },
      { name: 'servesCuisine', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'menu', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'acceptsReservations', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'priceRange', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'openingHoursSpecification', propertyType: 'OpeningHoursSpecification', isRequired: false, acceptedTypes: ['OpeningHoursSpecification'], isComplexType: true },
    ],
    AboutPage: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'dateModified', propertyType: 'Date', isRequired: false, acceptedTypes: ['DateTime'], isComplexType: false },
    ],
    ContactPage: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'telephone', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'email', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'address', propertyType: 'PostalAddress', isRequired: false, acceptedTypes: ['PostalAddress'], isComplexType: true },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
    ],
    CollectionPage: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
    ],
    ProfilePage: [
      { name: 'name', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'description', propertyType: 'Text', isRequired: false, acceptedTypes: ['String'], isComplexType: false },
      { name: 'url', propertyType: 'URL', isRequired: false, acceptedTypes: ['Uri'], isComplexType: false },
      { name: 'mainEntity', propertyType: 'Person', isRequired: false, acceptedTypes: ['Person'], isComplexType: true },
    ],
    BreadcrumbList: [
      { name: 'itemListElement', propertyType: 'ListItem', isRequired: false, acceptedTypes: ['ListItem'], isComplexType: true },
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
    const customProps = this._contentTypes.find((ct) => ct.alias === alias)?.properties?.map((p) => ({
      alias: p.alias,
      name: p.alias.charAt(0).toUpperCase() + p.alias.slice(1).replace(/([A-Z])/g, ' $1'),
      editorAlias: p.editorAlias,
      description: '',
    })) || [];

    const builtInProps = [
      { alias: '__url', name: 'URL', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
      { alias: '__name', name: 'Name', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
      { alias: '__createDate', name: 'Create Date', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
      { alias: '__updateDate', name: 'Update Date', editorAlias: 'SchemeWeaver.BuiltIn', description: '' },
    ];

    return [...customProps, ...builtInProps];
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
        // For blockContent popular defaults with no name match, suggest the first BlockList property
        if (resolverConfig) {
          const blockListProp = contentType.properties?.find((p) => p.editorAlias === 'Umbraco.BlockList');
          if (blockListProp) {
            return {
              schemaPropertyName: prop.name,
              schemaPropertyType: prop.propertyType,
              suggestedContentTypePropertyAlias: blockListProp.alias,
              suggestedSourceType: 'blockContent',
              confidence: 60,
              isAutoMapped: true,
              editorAlias: blockListProp.editorAlias,
              acceptedTypes: prop.acceptedTypes,
              isComplexType: prop.isComplexType,
              suggestedNestedSchemaTypeName: prop.acceptedTypes[0],
              suggestedResolverConfig: resolverConfig,
            };
          }
        }
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

      if (matchedProp) {
        return {
          schemaPropertyName: prop.name,
          schemaPropertyType: prop.propertyType,
          suggestedContentTypePropertyAlias: matchedProp.alias,
          suggestedSourceType: 'property',
          confidence: 80,
          isAutoMapped: true,
          editorAlias: matchedProp.editorAlias,
          acceptedTypes: prop.acceptedTypes,
          isComplexType: prop.isComplexType,
        };
      }

      // Built-in property fallback for URL/name/date schema properties
      const builtInAlias = this._tryMatchBuiltIn(prop.name, prop.propertyType);
      if (builtInAlias) {
        return {
          schemaPropertyName: prop.name,
          schemaPropertyType: prop.propertyType,
          suggestedContentTypePropertyAlias: builtInAlias,
          suggestedSourceType: 'property',
          confidence: 70,
          isAutoMapped: true,
          editorAlias: 'SchemeWeaver.BuiltIn',
          acceptedTypes: prop.acceptedTypes,
          isComplexType: prop.isComplexType,
        };
      }

      return {
        schemaPropertyName: prop.name,
        schemaPropertyType: prop.propertyType,
        suggestedContentTypePropertyAlias: null,
        suggestedSourceType: 'property',
        confidence: 30,
        isAutoMapped: false,
        editorAlias: null,
        acceptedTypes: prop.acceptedTypes,
        isComplexType: prop.isComplexType,
      };
    });
  }

  private _tryMatchBuiltIn(schemaPropertyName: string, propertyType: string): string | null {
    const name = schemaPropertyName.toLowerCase();
    if (name === 'url' || propertyType?.toLowerCase().includes('url')) return '__url';
    if (name === 'name') return '__name';
    if (name === 'datemodified') return '__updateDate';
    if (name === 'datepublished' || name === 'datecreated') return '__createDate';
    return null;
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
