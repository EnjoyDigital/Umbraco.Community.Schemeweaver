import { expect } from '@open-wc/testing';
import { DeleteSchemaMappingAction } from './delete-schema-mapping.action.js';

describe('DeleteSchemaMappingAction', () => {
  it('can be instantiated', () => {
    const action = new DeleteSchemaMappingAction(null as any, { unique: 'blogArticle' } as any);
    expect(action).to.be.instanceOf(DeleteSchemaMappingAction);
  });

  it('is exported as api', async () => {
    const module = await import('./delete-schema-mapping.action.js');
    expect(module.api).to.equal(DeleteSchemaMappingAction);
  });

  it('has an execute method', () => {
    const action = new DeleteSchemaMappingAction(null as any, { unique: 'blogArticle' } as any);
    expect(typeof action.execute).to.equal('function');
  });
});
