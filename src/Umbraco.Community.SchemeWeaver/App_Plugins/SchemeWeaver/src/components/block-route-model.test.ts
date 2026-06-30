import { expect } from '@open-wc/testing';
import type { BlockRouteSuggestion, RankedSchemaPropertyInfo } from '../api/types.js';
import {
  convertSuggestedRoutes,
  serialisePropertyMappings,
  seedEntriesFromRaw,
  makePropEntry,
  makeBlockRow,
  alignPropertyMappings,
  allowedObjectSchemaTypes,
  visibleEntries,
  recommendedCount,
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
});
