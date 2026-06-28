import { expect } from '@open-wc/testing';
import type { BlockRouteSuggestion } from '../api/types.js';
import { convertSuggestedRoutes, serialisePropertyMappings, seedEntriesFromRaw } from './block-route-model.js';

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
