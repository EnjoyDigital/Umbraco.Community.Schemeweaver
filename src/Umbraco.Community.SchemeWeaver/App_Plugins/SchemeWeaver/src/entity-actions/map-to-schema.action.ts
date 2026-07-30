import { UmbEntityActionBase, UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';
import type { SchemeWeaverContext } from '../context/schemeweaver.context.js';
import type { SchemaMappingDto } from '../api/types.js';
import { changeSchemaType } from '../utils/change-schema-type.js';
import { dtoToRow, rowsToPropertyMappingDtos } from '../utils/mapping-converters.js';

export class MapToSchemaAction extends UmbEntityActionBase<never> {
  #localize = new UmbLocalizationController(this);

  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);

    // Resolve GUID to alias — entity actions receive unique (GUID), but the API expects alias
    let contentTypeAlias: string;
    let context: SchemeWeaverContext;
    try {
      const resolved = await this.getContext(SCHEMEWEAVER_CONTEXT);
      if (!resolved) throw new Error('SchemeWeaverContext not provided');
      context = resolved;
      contentTypeAlias = await context.resolveContentTypeAlias(this.args.unique ?? '') ?? this.args.unique ?? '';
    } catch {
      notificationContext?.peek('danger', {
        data: {
          headline: this.#localize.term('schemeWeaver_mapToSchema'),
          message: this.#localize.term('schemeWeaver_failedToResolveContentType'),
        },
      });
      return;
    }

    // Already mapped? Changing the type must not silently discard the mapping the
    // user has built (issue #41) — route through the reconciling change flow
    // instead of re-running auto-map over the top of it.
    const existing = await context.requestMapping(contentTypeAlias).catch(() => null);
    if (existing) {
      await this.#changeExistingMapping(modalManager, notificationContext, context, existing, contentTypeAlias);
      return;
    }

    // First open schema picker
    const pickerResult = await modalManager
      .open(this, SCHEMEWEAVER_SCHEMA_PICKER_MODAL, {
        data: {
          contentTypeAlias,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (!pickerResult?.schemaType) return;

    // Then open property mapping modal
    const mappingResult = await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias,
          schemaType: pickerResult.schemaType,
          contentTypeKey: this.args.unique ?? '',
        },
      })
      .onSubmit()
      .catch(() => null);

    if (mappingResult !== null) {
      notificationContext?.peek('positive', {
        data: { message: this.#localize.term('schemeWeaver_mappingSaved') },
      });
    }
  }

  async #changeExistingMapping(
    modalManager: typeof UMB_MODAL_MANAGER_CONTEXT.TYPE,
    notificationContext: typeof UMB_NOTIFICATION_CONTEXT.TYPE | undefined,
    context: SchemeWeaverContext,
    existing: SchemaMappingDto,
    contentTypeAlias: string,
  ) {
    try {
      const result = await changeSchemaType({
        host: this,
        modalManager,
        localize: this.#localize,
        contentTypeAlias,
        currentSchemaType: existing.schemaTypeName,
        rows: existing.propertyMappings.map(dtoToRow),
        requestSchemaTypeProperties: async (name) => context.requestSchemaTypeProperties(name, true),
      });

      if (!result) return;

      await context.saveMapping({
        ...existing,
        schemaTypeName: result.schemaTypeName,
        propertyMappings: rowsToPropertyMappingDtos(result.rows),
      });

      notificationContext?.peek('positive', {
        data: { message: this.#localize.term('schemeWeaver_schemaTypeChanged', result.schemaTypeName) },
      });

      // Refresh any open workspace so the Schema.org tab shows the new type.
      const eventContext = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
      eventContext?.dispatchEvent(
        new UmbRequestReloadStructureForEntityEvent({
          unique: this.args.unique ?? '',
          entityType: this.args.entityType,
        }),
      );
    } catch (error) {
      notificationContext?.peek('danger', {
        data: {
          headline: this.#localize.term('schemeWeaver_changeSchemaType'),
          message: error instanceof Error ? error.message : this.#localize.term('schemeWeaver_failedToSave'),
        },
      });
    }
  }
}

export { MapToSchemaAction as api };
