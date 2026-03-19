import { expect } from '@open-wc/testing';
import { MapToSchemaAction } from './map-to-schema.action.js';

describe('MapToSchemaAction', () => {
  it('can be instantiated', () => {
    const action = new MapToSchemaAction(null as any, { unique: 'blogArticle' } as any);
    expect(action).to.be.instanceOf(MapToSchemaAction);
  });

  it('is exported as api', async () => {
    const module = await import('./map-to-schema.action.js');
    expect(module.api).to.equal(MapToSchemaAction);
  });
});
