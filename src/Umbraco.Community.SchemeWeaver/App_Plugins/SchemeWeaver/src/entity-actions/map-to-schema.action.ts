import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';

export class MapToSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);

    // First open schema picker
    const pickerResult = await modalManager
      .open(this, 'schemeweaver-schema-picker-modal', {
        data: {
          contentTypeAlias: this.args.unique,
        },
      })
      .onSubmit()
      .catch(() => null);

    if (!pickerResult?.schemaType) return;

    // Then open property mapping modal
    await modalManager
      .open(this, 'schemeweaver-property-mapping-modal', {
        data: {
          contentTypeAlias: this.args.unique,
          schemaType: pickerResult.schemaType,
        },
      })
      .onSubmit()
      .catch(() => null);
  }
}

export { MapToSchemaAction as api };
export default MapToSchemaAction;
