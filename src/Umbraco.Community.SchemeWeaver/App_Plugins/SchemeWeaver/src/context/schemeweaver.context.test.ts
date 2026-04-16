import { expect } from '@open-wc/testing';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import { SchemeWeaverContext } from './schemeweaver.context.js';
import type { SchemaMappingDto } from '../api/types.js';

/**
 * Behavioural tests for SchemeWeaverContext. Drives the context's load / save /
 * delete methods through the real repository → fetch → MSW handlers pipeline
 * and asserts on the observable state the UI actually subscribes to.
 */
describe('SchemeWeaverContext', () => {
  const createHost = () => document.createElement('div') as unknown as never;

  before(async () => {
    await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('instantiates with a host and exposes observables', () => {
    const context = new SchemeWeaverContext(createHost());

    expect(context.schemaTypes).to.exist;
    expect(context.contentTypes).to.exist;
    expect(context.mappings).to.exist;
    expect(context.currentMapping).to.exist;
    expect(context.preview).to.exist;
    expect(context.loading).to.exist;
  });

  describe('loadMappings', () => {
    it('populates the mappings observable from the mock database', async () => {
      const context = new SchemeWeaverContext(createHost());

      await context.loadMappings();

      const value = (context.mappings as unknown as { getValue: () => SchemaMappingDto[] }).getValue();
      expect(value).to.be.an('array');
      expect(value.length).to.be.greaterThan(0);
      expect(value.some((m) => m.contentTypeAlias === 'blogArticle')).to.be.true;
    });

    it('toggles the loading observable around the fetch', async () => {
      const context = new SchemeWeaverContext(createHost());
      const loadingSnapshots: boolean[] = [];

      context.loading.subscribe((v: boolean) => loadingSnapshots.push(v));

      await context.loadMappings();

      // At least one true (during fetch) and ends on false.
      expect(loadingSnapshots).to.include(true);
      expect(loadingSnapshots[loadingSnapshots.length - 1]).to.equal(false);
    });
  });

  describe('loadSchemaTypes', () => {
    it('populates the schemaTypes observable', async () => {
      const context = new SchemeWeaverContext(createHost());

      await context.loadSchemaTypes();

      const value = (context.schemaTypes as unknown as { getValue: () => unknown[] }).getValue();
      expect(value).to.be.an('array');
      expect(value.length).to.be.greaterThan(0);
    });
  });

  describe('loadContentTypes', () => {
    it('populates the contentTypes observable', async () => {
      const context = new SchemeWeaverContext(createHost());

      await context.loadContentTypes();

      const value = (context.contentTypes as unknown as { getValue: () => unknown[] }).getValue();
      expect(value).to.be.an('array');
      expect(value.length).to.be.greaterThan(0);
    });
  });

  describe('saveMapping + deleteMapping round trip', () => {
    it('saves a new mapping and then removes it, with mappings observable reflecting each step', async () => {
      const context = new SchemeWeaverContext(createHost());

      const dto: SchemaMappingDto = {
        contentTypeAlias: 'contextTestMapping',
        contentTypeKey: '00000000-0000-0000-0000-000000000055',
        schemaTypeName: 'Event',
        isEnabled: true,
        isInherited: false,
        propertyMappings: [],
      };

      await context.saveMapping(dto);

      let mappings = (context.mappings as unknown as { getValue: () => SchemaMappingDto[] }).getValue();
      expect(mappings.some((m) => m.contentTypeAlias === 'contextTestMapping')).to.be.true;

      await context.deleteMapping('contextTestMapping');

      mappings = (context.mappings as unknown as { getValue: () => SchemaMappingDto[] }).getValue();
      expect(mappings.some((m) => m.contentTypeAlias === 'contextTestMapping')).to.be.false;
    });
  });
});
