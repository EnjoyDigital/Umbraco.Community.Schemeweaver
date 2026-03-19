import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';

export class GenerateFromSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);

    await modalManager
      .open(this, 'schemeweaver-generate-doctype-modal', {
        data: {
          contentTypeAlias: this.args.unique,
        },
      })
      .onSubmit()
      .catch(() => null);
  }
}

export { GenerateFromSchemaAction as api };
export default GenerateFromSchemaAction;
