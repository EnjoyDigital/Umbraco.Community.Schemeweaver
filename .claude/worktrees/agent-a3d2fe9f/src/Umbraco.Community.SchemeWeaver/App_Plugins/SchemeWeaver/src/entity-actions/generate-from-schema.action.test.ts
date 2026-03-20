import { expect } from '@open-wc/testing';
import { GenerateFromSchemaAction } from './generate-from-schema.action.js';

describe('GenerateFromSchemaAction', () => {
  it('can be instantiated', () => {
    const action = new GenerateFromSchemaAction(null as any, { unique: 'blogArticle' } as any);
    expect(action).to.be.instanceOf(GenerateFromSchemaAction);
  });

  it('is exported as api', async () => {
    const module = await import('./generate-from-schema.action.js');
    expect(module.api).to.equal(GenerateFromSchemaAction);
  });

  it('stores the unique arg from constructor', () => {
    const action = new GenerateFromSchemaAction(null as any, { unique: 'productPage' } as any);
    expect(action.args.unique).to.equal('productPage');
  });
});
