import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL } from '../modals/generate-doctype-modal.token.js';

export class GenerateFromSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    await modalManager
      .open(this, SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL, {
        data: {
          contentTypeAlias: this.args.unique ?? '',
        },
      })
      .onSubmit()
      .catch(() => null);
  }
}

export { GenerateFromSchemaAction as api };
