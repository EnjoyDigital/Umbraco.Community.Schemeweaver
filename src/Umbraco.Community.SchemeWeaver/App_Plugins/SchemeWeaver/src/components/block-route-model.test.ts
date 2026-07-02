import { expect } from '@open-wc/testing';
import type { BlockRoutePropertyMapping, BlockRouteSuggestion, RankedSchemaPropertyInfo } from '../api/types.js';
import {
  convertSuggestedRoutes,
  serialisePropertyMappings,
  serialiseRoutes,
  seedEntriesFromRaw,
  seedRowsFromLegacyConfig,
  summariseResolverConfig,
  parseResolverConfig,
  makePropEntry,
  makeBlockRow,
  alignPropertyMappings,
  allowedObjectSchemaTypes,
  visibleEntries,
  recommendedCount,
  recommendedTotal,
  recommendedMapped,
  hiddenCount,
} from './block-route-model.js';
import type { BlockElementTypeInfo } from '../api/types.js';

describe('block-route-model transformType round-trip', () => {
  it('preserves transformType from a suggested route through convertSuggestedRoutes', () => {
    const routes: BlockRouteSuggestion[] = [
      {
        blockAlias: 'textBlock',
        nestedSchemaType: 'Article',
        confidence: 90,
        propertyMappings: [
          {
            schemaProperty: 'articleBody',
            contentProperty: 'richText',
            transformType: 'stripHtml',
          },
        ],
      },
    ];

    const converted = convertSuggestedRoutes(routes);
    expect(converted).to.have.length(1);
    expect(converted[0].propertyMappings[0].transformType).to.equal('stripHtml');
  });

  it('round-trips transformType: suggestion → entry → serialised JSON contains transformType', () => {
    // Convert the suggested route to the stored shape, then seed property entries
    // from it (as the editor does when an author accepts a suggestion) and
    // re-serialise — transformType must survive the whole trip.
    const routes: BlockRouteSuggestion[] = [
      {
        blockAlias: 'textBlock',
        nestedSchemaType: 'Article',
        confidence: 90,
        propertyMappings: [
          { schemaProperty: 'articleBody', contentProperty: 'richText', transformType: 'stripHtml' },
        ],
      },
    ];

    const stored = convertSuggestedRoutes(routes);
    const entries = seedEntriesFromRaw(stored[0].propertyMappings, [], false);
    expect(entries[0].transformType).to.equal('stripHtml');

    const serialised = serialisePropertyMappings(entries);
    expect(serialised[0].transformType).to.equal('stripHtml');

    // And the resolverConfig JSON string preserves it.
    const json = JSON.stringify({ routes: [{ ...stored[0], propertyMappings: serialised }] });
    expect(json).to.contain('"transformType":"stripHtml"');
  });

  it('omits transformType when none is set', () => {
    const routes: BlockRouteSuggestion[] = [
      {
        blockAlias: 'textBlock',
        nestedSchemaType: 'Article',
        confidence: 90,
        propertyMappings: [{ schemaProperty: 'headline', contentProperty: 'title' }],
      },
    ];
    const stored = convertSuggestedRoutes(routes);
    const entries = seedEntriesFromRaw(stored[0].propertyMappings, [], false);
    const serialised = serialisePropertyMappings(entries);
    expect(serialised[0].transformType).to.be.undefined;
  });
});

describe('block-route-model accepted-types plumbing', () => {
  function rankedProp(name: string, acceptedTypes: string[]): RankedSchemaPropertyInfo {
    return { name, propertyType: acceptedTypes.join(' | '), isRequired: false, acceptedTypes, isComplexType: true, confidence: 60, isPopular: true };
  }

  it('alignPropertyMappings carries acceptedTypes onto each entry', () => {
    const [entry] = alignPropertyMappings([rankedProp('AcceptedAnswer', ['Answer', 'ItemList'])], [], []);
    expect(entry.acceptedTypes).to.deep.equal(['Answer', 'ItemList']);
  });

  describe('allowedObjectSchemaTypes', () => {
    const entry = (acceptedTypes: string[]) => makePropEntry('p', '', true, acceptedTypes, []);

    it('returns the object accepted types for a constrained property', () => {
      expect(allowedObjectSchemaTypes(entry(['Answer', 'ItemList']))).to.deep.equal(['Answer', 'ItemList']);
    });

    it('drops primitive accepted types (String/Uri/…)', () => {
      expect(allowedObjectSchemaTypes(entry(['ImageObject', 'Uri']))).to.deep.equal(['ImageObject']);
    });

    it('returns [] (→ picker) when the property accepts the universal Thing root', () => {
      expect(allowedObjectSchemaTypes(entry(['Thing']))).to.deep.equal([]);
    });

    it('returns [] when empty or all-primitive', () => {
      expect(allowedObjectSchemaTypes(entry([]))).to.deep.equal([]);
      expect(allowedObjectSchemaTypes(entry(['String', 'Integer']))).to.deep.equal([]);
    });
  });
});

describe('block-route-model progressive disclosure', () => {
  const bt: BlockElementTypeInfo = { alias: 'faqItem', name: 'FAQ Item', properties: [], propertyInfos: [] };

  // name: recommended + mapped | image: recommended, unmapped | about: neither |
  // legacyLowConf: MAPPED but low-confidence & not recommended (the must-never-hide case).
  function row(showAll = false) {
    const entries = [
      makePropEntry('name', 'String', false, [], [], { contentProperty: 'q' }, 80, true),
      makePropEntry('image', 'Uri', false, [], [], undefined, 80, true),
      makePropEntry('about', 'Thing', false, [], [], undefined, 30, false),
      makePropEntry('legacyLowConf', 'String', false, [], [], { contentProperty: 'x' }, 20, false),
    ];
    return { ...makeBlockRow(bt, { nestedSchemaType: 'Question', propertyMappings: entries }), showAll };
  }

  it('collapsed: shows recommended + mapped, hides the unmapped low-confidence tail', () => {
    const visible = visibleEntries(row(false));
    expect(visible.map((v) => v.entry.schemaProperty)).to.deep.equal(['name', 'legacyLowConf', 'image']);
    expect(hiddenCount(row(false))).to.equal(1); // only `about` hidden
  });

  it('a saved low-confidence mapping is never collapsed out of sight', () => {
    const visible = visibleEntries(row(false));
    expect(visible.some((v) => v.entry.schemaProperty === 'legacyLowConf')).to.equal(true);
  });

  it('carries the ORIGINAL index so row edits target the right entry', () => {
    const visible = visibleEntries(row(false));
    // name@0, legacyLowConf@3, image@1 (mapped-first, then confidence DESC)
    expect(visible.map((v) => v.index)).to.deep.equal([0, 3, 1]);
  });

  it('showAll reveals every property and hides nothing', () => {
    const visible = visibleEntries(row(true));
    expect(visible).to.have.length(4);
    expect(hiddenCount(row(true))).to.equal(0);
  });

  it('recommendedCount counts recommended-or-mapped (the badge denominator)', () => {
    expect(recommendedCount(row(false))).to.equal(3); // name, image (recommended) + legacyLowConf (mapped)
  });

  it('recommendedTotal / recommendedMapped separate the chip numerator and denominator honestly', () => {
    const r = row(false);
    expect(recommendedTotal(r)).to.equal(2); // name, image
    expect(recommendedMapped(r)).to.equal(1); // only name is both recommended AND mapped
  });
});

describe('block-route-model extras passthrough (never lose stored config on re-save)', () => {
  it('round-trips entry-level extractAs/nestedContentProperty/wrapInListItem/positionProperty', () => {
    const stored: BlockRoutePropertyMapping[] = [
      {
        schemaProperty: 'recipeIngredient',
        contentProperty: 'ingredients',
        extractAs: 'stringList',
        nestedContentProperty: 'ingredientName',
        wrapInListItem: true,
        positionProperty: 'sortOrder',
      },
    ];
    const entries = seedEntriesFromRaw(stored, [], false);
    const serialised = serialisePropertyMappings(entries);
    expect(serialised[0].extractAs).to.equal('stringList');
    expect(serialised[0].nestedContentProperty).to.equal('ingredientName');
    expect(serialised[0].wrapInListItem).to.equal(true);
    expect(serialised[0].positionProperty).to.equal('sortOrder');
  });

  it('does not invent extras fields on entries that never had them', () => {
    const entries = seedEntriesFromRaw(
      [{ schemaProperty: 'name', contentProperty: 'title' }],
      [],
      false,
    );
    const serialised = serialisePropertyMappings(entries);
    expect('extractAs' in serialised[0]).to.equal(false);
    expect('wrapInListItem' in serialised[0]).to.equal(false);
  });

  it('serialiseRoutes preserves route-level requiredProperties', () => {
    const bt: BlockElementTypeInfo = { alias: 'faqItem', name: 'FAQ Item', properties: [], propertyInfos: [] };
    const row = makeBlockRow(bt, {
      nestedSchemaType: 'Question',
      propertyMappings: [makePropEntry('name', 'String', false, [], [], { contentProperty: 'q' })],
      requiredProperties: ['name', 'acceptedAnswer'],
    });
    const routes = serialiseRoutes([row]);
    expect(routes[0].requiredProperties).to.deep.equal(['name', 'acceptedAnswer']);
    // …and rows without them stay clean.
    const bare = makeBlockRow(bt, { nestedSchemaType: 'Question', propertyMappings: [] });
    expect('requiredProperties' in serialiseRoutes([bare])[0]).to.equal(false);
  });
});

describe('block-route-model legacy config seeding (wildcard semantics)', () => {
  const reviewItem: BlockElementTypeInfo = {
    alias: 'reviewItem',
    name: 'Review Item',
    properties: ['reviewAuthor', 'ratingValue', 'reviewBody'],
    propertyInfos: [
      { alias: 'reviewAuthor', name: 'Author', editorAlias: '' },
      { alias: 'ratingValue', name: 'Rating', editorAlias: '' },
      { alias: 'reviewBody', name: 'Body', editorAlias: '' },
    ],
  };
  const promoBanner: BlockElementTypeInfo = { alias: 'promoBanner', name: 'Promo', properties: [], propertyInfos: [] };

  it('wildcard entries (no blockAlias — the SchemaAutoMapper/seed shape) apply to EVERY block type', () => {
    const seeds = seedRowsFromLegacyConfig(
      [reviewItem, promoBanner],
      [
        { schemaProperty: 'Author', contentProperty: 'reviewAuthor', wrapInType: 'Person', wrapInProperty: 'Name' },
        { schemaProperty: 'ReviewBody', contentProperty: 'reviewBody' },
      ],
      'Review',
    );
    expect(seeds.size).to.equal(2);
    const review = seeds.get('reviewitem')!;
    expect(review.nestedSchemaType).to.equal('Review');
    expect(review.propertyMappings.map((m) => m.schemaProperty)).to.deep.equal(['Author', 'ReviewBody']);
    expect(review.propertyMappings[0].wrapInType).to.equal('Person');
    expect(seeds.get('promobanner')!.nestedSchemaType).to.equal('Review');
  });

  it('per-alias entries apply only to their block (plus any wildcard entries)', () => {
    const seeds = seedRowsFromLegacyConfig(
      [reviewItem, promoBanner],
      [
        { schemaProperty: 'Name', contentProperty: 'title', blockAlias: 'promoBanner' },
        { schemaProperty: 'Description', contentProperty: 'summary' },
      ],
      'WebPageElement',
    );
    expect(seeds.get('promobanner')!.propertyMappings.map((m) => m.schemaProperty)).to.deep.equal([
      'Description',
      'Name',
    ]);
    expect(seeds.get('reviewitem')!.propertyMappings.map((m) => m.schemaProperty)).to.deep.equal(['Description']);
  });

  it('block-alias matching is case-insensitive', () => {
    const seeds = seedRowsFromLegacyConfig(
      [reviewItem],
      [{ schemaProperty: 'Name', contentProperty: 'title', blockAlias: 'ReviewItem' }],
      'Review',
    );
    expect(seeds.get('reviewitem')!.propertyMappings).to.have.length(1);
  });
});

describe('block-route-model resolver config summary', () => {
  it('summarises a routed config as blockAlias → type pairs', () => {
    const json = JSON.stringify({
      routes: [
        { blockAlias: 'reviewItem', nestedSchemaType: 'Review', propertyMappings: [] },
        { blockAlias: 'faqItem', nestedSchemaType: 'Question', propertyMappings: [] },
      ],
    });
    const summary = summariseResolverConfig(json);
    expect(summary.kind).to.equal('routes');
    if (summary.kind === 'routes') {
      expect(summary.routes).to.deep.equal([
        { blockAlias: 'reviewItem', nestedSchemaType: 'Review' },
        { blockAlias: 'faqItem', nestedSchemaType: 'Question' },
      ]);
    }
  });

  it('summarises a legacy wildcard config as one "any block" route using the mapping-level type', () => {
    const json = JSON.stringify({ nestedMappings: [{ schemaProperty: 'Author', contentProperty: 'reviewAuthor' }] });
    const summary = summariseResolverConfig(json, 'Review');
    expect(summary).to.deep.equal({ kind: 'routes', routes: [{ blockAlias: '', nestedSchemaType: 'Review' }] });
  });

  it('summarises a null config with a nestedSchemaTypeName (auto-mapped legacy) the same way', () => {
    expect(summariseResolverConfig(null, 'Review')).to.deep.equal({
      kind: 'routes',
      routes: [{ blockAlias: '', nestedSchemaType: 'Review' }],
    });
  });

  it('summarises string-list extraction with its source property', () => {
    const json = JSON.stringify({ extractAs: 'stringList', contentProperty: 'ingredientName' });
    expect(summariseResolverConfig(json)).to.deep.equal({ kind: 'stringList', contentProperty: 'ingredientName' });
  });

  it('is empty for no config and safe on invalid JSON', () => {
    expect(summariseResolverConfig(null)).to.deep.equal({ kind: 'empty' });
    expect(summariseResolverConfig('{not json')).to.deep.equal({ kind: 'empty' });
    expect(parseResolverConfig('{not json')).to.equal(null);
    expect(parseResolverConfig('   ')).to.equal(null);
  });
});
