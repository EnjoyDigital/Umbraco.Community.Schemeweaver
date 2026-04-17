import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';

export class MapToSchemaAction extends UmbEntityActionBase<never> {
  #localize = new UmbLocalizationController(this);

  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);

    // Resolve GUID to alias — entity actions receive unique (GUID), but the API expects alias
    let contentTypeAlias: string;
    try {
      const context = await this.getContext(SCHEMEWEAVER_CONTEXT);
      if (!context) throw new Error('SchemeWeaverContext not provided');
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
}

export { MapToSchemaAction as api };
