import { expect } from '@open-wc/testing';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { __mockContextRegistry } from '../__mocks__/context-api.js';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SchemaMappingDto } from '../api/types.js';
import { MapToSchemaAction } from './map-to-schema.action.js';

const BASE = '/umbraco/management/api/v1/schemeweaver';
const BLOG_ARTICLE_KEY = '00000000-0000-0000-0000-000000000001';

interface OpenedModal {
  alias?: string;
  data?: Record<string, unknown>;
}

/** See the workspace-view spec — same headless stand-in for the modal manager. */
function stubModalManager(responses: { schemaType?: string; confirm?: boolean }): OpenedModal[] {
  const opened: OpenedModal[] = [];
  const manager = {
    open(_host: unknown, token: { alias?: string }, options?: { data?: Record<string, unknown> }) {
      opened.push({ alias: token?.alias, data: options?.data });
      const cancelled = () => ({ onSubmit: () => Promise.reject(new Error('cancelled')) });
      if (token?.alias === 'SchemeWeaver.Modal.SchemaPicker') {
        return responses.schemaType
          ? { onSubmit: () => Promise.resolve({ schemaType: responses.schemaType }) }
          : cancelled();
      }
      return responses.confirm ? { onSubmit: () => Promise.resolve(undefined) } : cancelled();
    },
  };
  __mockContextRegistry.provide(UMB_MODAL_MANAGER_CONTEXT, manager);
  return opened;
}

describe('MapToSchemaAction', () => {
  it('can be instantiated', () => {
    const action = new MapToSchemaAction(null as any, { unique: 'blogArticle' } as any);
    expect(action).to.be.instanceOf(MapToSchemaAction);
  });

  it('is exported as api', async () => {
    const module = await import('./map-to-schema.action.js');
    expect(module.api).to.equal(MapToSchemaAction);
  });

  // Issue #41 — running this action on a content type that was already mapped
  // used to re-seed the rows from auto-map and overwrite the user's work with no
  // warning. It now routes through the reconciling change-type flow instead.
  describe('on a content type that is already mapped', () => {
    let original: SchemaMappingDto;

    before(async () => {
      await startMockServiceWorker();
    });

    after(() => {
      stopMockServiceWorker();
    });

    beforeEach(async () => {
      original = (await (await fetch(`${BASE}/mappings/blogArticle`)).json()) as SchemaMappingDto;
      // The action peeks notifications on every path; a sink is all we need here.
      __mockContextRegistry.provide(UMB_NOTIFICATION_CONTEXT, { peek: () => {} });
    });

    afterEach(async () => {
      await fetch(`${BASE}/mappings`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(original),
      });
    });

    function createAction() {
      return new MapToSchemaAction(null as any, {
        unique: BLOG_ARTICLE_KEY,
        entityType: 'document-type',
      } as any);
    }

    it('changes the type while keeping the existing property mappings', async () => {
      const opened = stubModalManager({ schemaType: 'BlogPosting', confirm: true });

      await createAction().execute();

      // The picker knows the current type, and exactly one confirmation followed
      // it — no property-mapping modal, which is what used to clobber the rows.
      expect(opened).to.have.lengthOf(2);
      expect(opened[0].alias).to.equal('SchemeWeaver.Modal.SchemaPicker');
      expect(opened[0].data!.currentSchemaType).to.equal('Article');

      const persisted = (await (await fetch(`${BASE}/mappings/blogArticle`)).json()) as SchemaMappingDto;
      expect(persisted.schemaTypeName).to.equal('BlogPosting');
      expect(persisted.propertyMappings.map((p) => p.schemaPropertyName)).to.deep.equal(
        original.propertyMappings.map((p) => p.schemaPropertyName),
      );
    });

    it('leaves the mapping untouched when the confirmation is declined', async () => {
      stubModalManager({ schemaType: 'FAQPage', confirm: false });

      await createAction().execute();

      const persisted = (await (await fetch(`${BASE}/mappings/blogArticle`)).json()) as SchemaMappingDto;
      expect(persisted.schemaTypeName).to.equal('Article');
      expect(persisted.propertyMappings).to.have.lengthOf(original.propertyMappings.length);
    });

    it('never opens the property mapping modal for an existing mapping', async () => {
      const opened = stubModalManager({ schemaType: 'BlogPosting', confirm: true });

      await createAction().execute();

      expect(opened.some((o) => o.alias === 'SchemeWeaver.Modal.PropertyMapping')).to.be.false;
    });
  });
});
