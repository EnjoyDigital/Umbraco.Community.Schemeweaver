import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL } from '../modals/generate-doctype-modal.token.js';
import { SCHEMEWEAVER_CONTEXT } from '../context/schemeweaver.context-token.js';

export class GenerateFromSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const localize = new UmbLocalizationController(this);
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
          headline: localize.term('schemeWeaver_generateFromSchema'),
          message: localize.term('schemeWeaver_failedToResolveContentType'),
        },
      });
      return;
    }

    const result = await modalManager
      .open(this, SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL, {
        data: {
          contentTypeAlias,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (result !== null) {
      notificationContext?.peek('positive', {
        data: { message: localize.term('schemeWeaver_contentTypeGenerated') },
      });
    }
  }
}

export { GenerateFromSchemaAction as api };
