import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL } from '../modals/generate-doctype-modal.token.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';

export class GenerateFromSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    // Resolve GUID to alias — entity actions receive unique (GUID), but the API expects alias
    const repository = new SchemeWeaverRepository(this);
    const contentTypeAlias = await repository.resolveContentTypeAlias(this.args.unique ?? '') ?? this.args.unique ?? '';

    await modalManager
      .open(this, SCHEMEWEAVER_GENERATE_DOCTYPE_MODAL, {
        data: {
          contentTypeAlias,
        },
      })
      .onSubmit()
      .catch(() => null);
  }
}

export { GenerateFromSchemaAction as api };
