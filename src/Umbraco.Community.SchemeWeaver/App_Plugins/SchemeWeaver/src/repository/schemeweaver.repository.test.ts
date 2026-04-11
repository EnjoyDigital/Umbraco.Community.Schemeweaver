import { expect } from '@open-wc/testing';
import type { SetupWorker } from 'msw/browser';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import { mappingNotFoundHandlers, serverErrorHandlers } from '../mocks/handlers.js';
import { SchemeWeaverRepository } from './schemeweaver.repository.js';
import type { SchemaMappingDto } from '../api/types.js';

/**
 * Behavioural tests for SchemeWeaverRepository. Goes through the real server
 * data source → fetch → MSW handlers defined in `src/mocks/handlers.ts`, so
 * each test exercises the full client-side request path rather than just
 * asserting on method existence.
 */
describe('SchemeWeaverRepository', () => {
  let worker: SetupWorker;

  // Minimal UmbControllerHost stub. The mocked UmbControllerBase in
  // `__mocks__/controller.js` stores whatever is passed and its `getContext`
  // returns `{}`, so any truthy value works for construction. Auth headers
  // are resolved via getContext(UMB_AUTH_CONTEXT) which returns {} and falls
  // through the try/catch in `getAuthHeaders`, leaving requests unauthenticated.
  const createHost = () => document.createElement('div') as unknown as never;

  before(async () => {
    worker = await startMockServiceWorker();
  });

  after(() => {
    stopMockServiceWorker();
  });

  it('instantiates with a host', () => {
    const repo = new SchemeWeaverRepository(createHost());
    expect(repo).to.be.instanceOf(SchemeWeaverRepository);
  });

  describe('requestMappings', () => {
    it('returns the seeded mappings from the mock database', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const mappings = await repo.requestMappings();

      expect(mappings).to.be.an('array');
      expect(mappings!.length).to.be.greaterThan(0);
      expect(mappings!.some((m) => m.contentTypeAlias === 'blogArticle')).to.be.true;
    });
  });

  describe('requestMapping', () => {
    it('returns a single mapping for an existing alias', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const mapping = await repo.requestMapping('blogArticle');

      expect(mapping).to.exist;
      expect(mapping!.contentTypeAlias).to.equal('blogArticle');
      expect(mapping!.schemaTypeName).to.equal('Article');
      expect(mapping!.propertyMappings).to.be.an('array');
      expect(mapping!.propertyMappings.length).to.be.greaterThan(0);
    });

    it('returns undefined when the mapping does not exist (404 handled)', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const mapping = await repo.requestMapping('doesNotExist');

      expect(mapping).to.be.undefined;
    });
  });

  describe('requestSchemaTypes', () => {
    it('returns schema types without a search filter', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const types = await repo.requestSchemaTypes();

      expect(types).to.be.an('array');
      expect(types!.length).to.be.greaterThan(0);
    });

    it('filters results when a search term is provided', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const types = await repo.requestSchemaTypes('Article');

      expect(types).to.be.an('array');
      expect(types!.every((t) => t.name.toLowerCase().includes('article'))).to.be.true;
    });
  });

  describe('saveMapping', () => {
    it('POSTs a new mapping and returns the created entity', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const dto: SchemaMappingDto = {
        contentTypeAlias: 'newMapping',
        contentTypeKey: '00000000-0000-0000-0000-000000000042',
        schemaTypeName: 'Event',
        isEnabled: true,
        isInherited: false,
        propertyMappings: [],
      };

      const saved = await repo.saveMapping(dto);

      expect(saved).to.exist;
      expect(saved!.contentTypeAlias).to.equal('newMapping');
      expect(saved!.schemaTypeName).to.equal('Event');
    });
  });

  describe('deleteMapping', () => {
    it('resolves without error when deleting an existing mapping', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      // Seed the mapping first so the delete has something to remove.
      await repo.saveMapping({
        contentTypeAlias: 'toDelete',
        contentTypeKey: '00000000-0000-0000-0000-000000000099',
        schemaTypeName: 'Thing',
        isEnabled: true,
        isInherited: false,
        propertyMappings: [],
      });

      await repo.deleteMapping('toDelete');

      // After deletion the mapping is gone (requestMapping should 404).
      const afterDelete = await repo.requestMapping('toDelete');
      expect(afterDelete).to.be.undefined;
    });
  });

  describe('requestAutoMap', () => {
    it('returns a flat array of property mapping suggestions', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const suggestions = await repo.requestAutoMap('blogArticle', 'Article');

      expect(suggestions).to.be.an('array');
    });
  });

  describe('requestPreview', () => {
    it('returns a JSON-LD preview response for an existing mapping', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const preview = await repo.requestPreview('blogArticle');

      expect(preview).to.exist;
      expect(preview).to.have.property('jsonLd');
    });
  });

  describe('error handling', () => {
    it('returns undefined when the mapping GET endpoint returns 500', async () => {
      worker.use(...serverErrorHandlers);

      const repo = new SchemeWeaverRepository(createHost());
      const result = await repo.requestMappings();

      // tryExecute swallows the error and returns undefined via the `error`
      // branch of the Umbraco resources helper.
      expect(result).to.be.undefined;

      worker.resetHandlers();
    });

    it('returns undefined when requesting a non-existent mapping (404)', async () => {
      worker.use(...mappingNotFoundHandlers);

      const repo = new SchemeWeaverRepository(createHost());
      const result = await repo.requestMapping('anything');

      // The data source's `expect404` flag converts 404 into a clean undefined.
      expect(result).to.be.undefined;

      worker.resetHandlers();
    });
  });

  describe('resolveContentTypeAlias', () => {
    it('returns the alias when the key matches a known content type', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      // Fetch a known content type first so we have a valid key to resolve.
      const contentTypes = await repo.requestContentTypes();
      expect(contentTypes).to.exist;
      expect(contentTypes!.length).to.be.greaterThan(0);
      const first = contentTypes![0];

      const alias = await repo.resolveContentTypeAlias(first.key);

      expect(alias).to.equal(first.alias);
    });

    it('returns undefined for an unknown key', async () => {
      const repo = new SchemeWeaverRepository(createHost());

      const alias = await repo.resolveContentTypeAlias('00000000-0000-0000-0000-000000000000');

      expect(alias).to.be.undefined;
    });
  });
});
