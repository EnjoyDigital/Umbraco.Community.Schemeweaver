import { expect } from '@open-wc/testing';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { __mockContextRegistry } from '../__mocks__/context-api.js';
import { startMockServiceWorker, stopMockServiceWorker } from '../mocks/setup.js';
import type { SchemaMappingDto } from '../api/types.js';
import { SchemeWeaverMappingChangedEvent } from '../utils/mapping-changed-event.js';
import { DeleteSchemaMappingAction } from './delete-schema-mapping.action.js';

const BASE = '/umbraco/management/api/v1/schemeweaver';
const BLOG_ARTICLE_KEY = '00000000-0000-0000-0000-000000000001';

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

  describe('announcing the delete', () => {
    let original: SchemaMappingDto;
    let actionEvents: EventTarget;

    before(async () => {
      await startMockServiceWorker();
    });

    after(() => {
      stopMockServiceWorker();
    });

    beforeEach(async () => {
      original = (await (await fetch(`${BASE}/mappings/blogArticle`)).json()) as SchemaMappingDto;

      actionEvents = new EventTarget();
      __mockContextRegistry.provide(UMB_ACTION_EVENT_CONTEXT, actionEvents);
      __mockContextRegistry.provide(UMB_NOTIFICATION_CONTEXT, { peek: () => {} });
      // Accept the delete confirmation.
      __mockContextRegistry.provide(UMB_MODAL_MANAGER_CONTEXT, {
        open: () => ({ onSubmit: () => Promise.resolve(undefined) }),
      });
    });

    afterEach(async () => {
      await fetch(`${BASE}/mappings`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(original),
      });
    });

    // Regression guard — the workspace view answers
    // UmbRequestReloadStructureForEntityEvent by SAVING its rows (that is how a
    // mapping auto-saves with its document type). Dispatching it here had an open
    // Schema.org tab POST the just-deleted mapping straight back, so the delete
    // silently appeared to fail. Reproduced in the backoffice before the fix.
    it('announces with a re-read event, never the reload-structure event', async () => {
      const seen: string[] = [];
      actionEvents.addEventListener(SchemeWeaverMappingChangedEvent.TYPE, () => seen.push('changed'));
      actionEvents.addEventListener(UmbRequestReloadStructureForEntityEvent.TYPE, () => seen.push('reload'));

      const action = new DeleteSchemaMappingAction(null as any, {
        unique: BLOG_ARTICLE_KEY,
        entityType: 'document-type',
      } as any);
      await action.execute();

      expect(seen).to.deep.equal(['changed']);

      const after = await fetch(`${BASE}/mappings/blogArticle`);
      expect(after.status, 'the mapping must stay deleted').to.equal(404);
    });

    it('carries the document type key so only the matching view re-reads', async () => {
      let unique: string | undefined;
      actionEvents.addEventListener(SchemeWeaverMappingChangedEvent.TYPE, (event: Event) => {
        unique = (event as SchemeWeaverMappingChangedEvent).getUnique();
      });

      const action = new DeleteSchemaMappingAction(null as any, {
        unique: BLOG_ARTICLE_KEY,
        entityType: 'document-type',
      } as any);
      await action.execute();

      expect(unique).to.equal(BLOG_ARTICLE_KEY);
    });
  });
});
