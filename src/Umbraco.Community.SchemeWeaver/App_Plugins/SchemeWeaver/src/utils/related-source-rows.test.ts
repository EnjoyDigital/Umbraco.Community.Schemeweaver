import { expect } from '@open-wc/testing';
import type { ContentTypeInfo, ContentTypeProperty } from '../api/types.js';
import type { PropertyMappingRow } from '../components/property-mapping-table.element.js';
import { SourceType } from '../constants/source-type.js';
import {
  enrichRelatedSourceRows,
  isRelatedSourceType,
  relatedSourceAliases,
  type RelatedSourceLookup,
} from './related-source-rows.js';

/** Helper to create a minimal PropertyMappingRow */
function makeRow(overrides: Partial<PropertyMappingRow> & { schemaPropertyName: string }): PropertyMappingRow {
  return {
    schemaPropertyType: '',
    sourceType: SourceType.Property,
    contentTypePropertyAlias: '',
    sourceContentTypeAlias: '',
    staticValue: '',
    confidence: null,
    editorAlias: '',
    nestedSchemaTypeName: '',
    resolverConfig: null,
    acceptedTypes: [],
    isComplexType: false,
    expanded: false,
    subMappings: [],
    selectedSubType: '',
    sourceContentTypeProperties: [],
    ...overrides,
  };
}

interface FakeType {
  key: string;
  properties: string[];
}

interface FakeLookupOptions {
  /** Aliases whose property request rejects, as a deleted or unreadable type would. */
  failProperties?: string[];
  /** Whether the content-type listing rejects. */
  failContentTypes?: boolean;
}

/** In-memory stand-in for the context / repository that records every call it gets. */
function fakeLookup(
  types: Record<string, FakeType>,
  options: FakeLookupOptions = {},
): RelatedSourceLookup & { propertyCalls: string[]; contentTypeCalls: number } {
  const lookup = {
    propertyCalls: [] as string[],
    contentTypeCalls: 0,
    async requestContentTypeProperties(alias: string): Promise<ContentTypeProperty[] | undefined> {
      lookup.propertyCalls.push(alias);
      if (options.failProperties?.includes(alias)) throw new Error(`HTTP 404 for ${alias}`);
      const type = types[alias];
      return type?.properties.map((a) => ({ alias: a, name: a, editorAlias: 'Umbraco.TextBox', description: '' }));
    },
    async requestContentTypes(): Promise<ContentTypeInfo[] | undefined> {
      lookup.contentTypeCalls++;
      if (options.failContentTypes) throw new Error('HTTP 500');
      return Object.entries(types).map(([alias, t]) => ({ alias, name: alias, key: t.key, propertyCount: t.properties.length }));
    },
  };
  return lookup;
}

const TYPES: Record<string, FakeType> = {
  homePage: { key: '00000000-0000-0000-0000-000000000007', properties: ['siteName', 'organisationName'] },
  blogListing: { key: '00000000-0000-0000-0000-000000000010', properties: ['title', 'description'] },
};

describe('isRelatedSourceType / relatedSourceAliases', () => {
  it('recognises exactly the three related-node source types', () => {
    expect(isRelatedSourceType(SourceType.Parent)).to.be.true;
    expect(isRelatedSourceType(SourceType.Ancestor)).to.be.true;
    expect(isRelatedSourceType(SourceType.Sibling)).to.be.true;
    expect(isRelatedSourceType(SourceType.Property)).to.be.false;
    expect(isRelatedSourceType(SourceType.ComplexType)).to.be.false;
    expect(isRelatedSourceType(SourceType.Reference)).to.be.false;
  });

  it('lists each related alias once, in first-seen order, ignoring rows without one', () => {
    const rows = [
      makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' }),
      makeRow({ schemaPropertyName: 'category', sourceType: SourceType.Parent, sourceContentTypeAlias: 'blogListing' }),
      makeRow({ schemaPropertyName: 'isPartOf', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' }),
      makeRow({ schemaPropertyName: 'genre', sourceType: SourceType.Sibling }), // no alias yet
      makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title', sourceContentTypeAlias: 'homePage' }), // not related
    ];
    expect(relatedSourceAliases(rows)).to.deep.equal(['homePage', 'blogListing']);
  });
});

describe('enrichRelatedSourceRows', () => {
  it('hydrates parent, ancestor and sibling rows with the related type properties and key', async () => {
    const rows = [
      makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage', contentTypePropertyAlias: 'organisationName' }),
      makeRow({ schemaPropertyName: 'category', sourceType: SourceType.Parent, sourceContentTypeAlias: 'blogListing', contentTypePropertyAlias: 'title' }),
      makeRow({ schemaPropertyName: 'genre', sourceType: SourceType.Sibling, sourceContentTypeAlias: 'blogListing', contentTypePropertyAlias: 'description' }),
    ];

    const result = await enrichRelatedSourceRows(rows, fakeLookup(TYPES));

    expect(result[0].sourceContentTypeProperties).to.deep.equal(['siteName', 'organisationName']);
    expect(result[0].sourceDocumentTypeUnique).to.equal(TYPES.homePage.key);
    expect(result[1].sourceContentTypeProperties).to.deep.equal(['title', 'description']);
    expect(result[1].sourceDocumentTypeUnique).to.equal(TYPES.blogListing.key);
    expect(result[2].sourceContentTypeProperties).to.deep.equal(['title', 'description']);
    expect(result[2].sourceDocumentTypeUnique).to.equal(TYPES.blogListing.key);
    // The fields that make the row a saved mapping are untouched.
    expect(result[0].sourceContentTypeAlias).to.equal('homePage');
    expect(result[0].contentTypePropertyAlias).to.equal('organisationName');
    expect(result[0].sourceType).to.equal(SourceType.Ancestor);
  });

  it('returns the same array when nothing is related, and the same row objects for unrelated rows', async () => {
    const lookup = fakeLookup(TYPES);
    const plain = makeRow({ schemaPropertyName: 'headline', contentTypePropertyAlias: 'title' });
    const untouched = await enrichRelatedSourceRows([plain], lookup);
    expect(untouched[0]).to.equal(plain);
    expect(lookup.propertyCalls).to.be.empty;
    expect(lookup.contentTypeCalls).to.equal(0);

    const related = makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' });
    const mixed = await enrichRelatedSourceRows([plain, related], lookup);
    expect(mixed[0], 'a row for the page itself keeps identity').to.equal(plain);
    expect(mixed[1]).to.not.equal(related);
    expect(mixed[1].sourceContentTypeProperties).to.include('organisationName');
  });

  it('requests each related type once, however many rows share it, and lists content types once', async () => {
    const lookup = fakeLookup(TYPES);
    const rows = [
      makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' }),
      makeRow({ schemaPropertyName: 'isPartOf', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' }),
      makeRow({ schemaPropertyName: 'category', sourceType: SourceType.Parent, sourceContentTypeAlias: 'blogListing' }),
    ];

    await enrichRelatedSourceRows(rows, lookup);

    expect(lookup.propertyCalls).to.deep.equal(['homePage', 'blogListing']);
    expect(lookup.contentTypeCalls).to.equal(1);
  });

  it('keeps the alias and degrades when one related type cannot be read; the others still hydrate', async () => {
    const lookup = fakeLookup(TYPES, { failProperties: ['homePage'] });
    const rows = [
      makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage', contentTypePropertyAlias: 'organisationName' }),
      makeRow({ schemaPropertyName: 'category', sourceType: SourceType.Parent, sourceContentTypeAlias: 'blogListing', contentTypePropertyAlias: 'title' }),
    ];

    const result = await enrichRelatedSourceRows(rows, lookup);

    // Unreadable: no dropdown, but the picker key still resolves and nothing saved is lost.
    expect(result[0].sourceContentTypeProperties).to.deep.equal([]);
    expect(result[0].sourceDocumentTypeUnique).to.equal(TYPES.homePage.key);
    expect(result[0].sourceContentTypeAlias).to.equal('homePage');
    expect(result[0].contentTypePropertyAlias).to.equal('organisationName');
    // Readable: fully hydrated.
    expect(result[1].sourceContentTypeProperties).to.deep.equal(['title', 'description']);
  });

  it('still hydrates the property list when the content-type listing fails', async () => {
    const lookup = fakeLookup(TYPES, { failContentTypes: true });
    const row = makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'homePage' });

    const [result] = await enrichRelatedSourceRows([row], lookup);

    expect(result.sourceContentTypeProperties).to.deep.equal(['siteName', 'organisationName']);
    expect(result.sourceDocumentTypeUnique).to.equal(undefined);
  });

  it('leaves a row whose type is unknown to both look-ups as the same object', async () => {
    const lookup = fakeLookup(TYPES);
    const row = makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'deletedType' });

    const [result] = await enrichRelatedSourceRows([row], lookup);

    expect(result).to.equal(row);
  });

  it('matches the document-type key case-insensitively, as the server-side resolvers do', async () => {
    const row = makeRow({ schemaPropertyName: 'publisher', sourceType: SourceType.Ancestor, sourceContentTypeAlias: 'HomePage' });
    const lookup = fakeLookup({ ...TYPES, HomePage: TYPES.homePage });

    const [result] = await enrichRelatedSourceRows([row], lookup);

    expect(result.sourceDocumentTypeUnique).to.equal(TYPES.homePage.key);
  });
});
