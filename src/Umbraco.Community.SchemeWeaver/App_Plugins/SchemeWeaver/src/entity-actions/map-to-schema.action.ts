import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_SCHEMA_PICKER_MODAL } from '../modals/schema-picker-modal.token.js';
import { SCHEMEWEAVER_PROPERTY_MAPPING_MODAL } from '../modals/property-mapping-modal.token.js';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';

export class MapToSchemaAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    // Resolve GUID to alias — entity actions receive unique (GUID), but the API expects alias
    const repository = new SchemeWeaverRepository(this);
    const contentTypeAlias = await repository.resolveContentTypeAlias(this.args.unique ?? '') ?? this.args.unique ?? '';

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
    await modalManager
      .open(this, SCHEMEWEAVER_PROPERTY_MAPPING_MODAL, {
        data: {
          contentTypeAlias,
          schemaType: pickerResult.schemaType,
          contentTypeKey: this.args.unique ?? '',
        },
      })
      .onSubmit()
      .catch(() => null);
  }
}

export { MapToSchemaAction as api };
