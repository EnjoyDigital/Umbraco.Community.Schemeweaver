import { UmbEntityActionBase, UmbRequestReloadStructureForEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT, UMB_CONFIRM_MODAL } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { SchemeWeaverRepository } from '../repository/schemeweaver.repository.js';

export class DeleteSchemaMappingAction extends UmbEntityActionBase<never> {
  async execute() {
    const repository = new SchemeWeaverRepository(this);
    const notificationContext = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    const contentTypeAlias = await repository.resolveContentTypeAlias(this.args.unique ?? '') ?? this.args.unique ?? '';

    // Check if a mapping exists
    const mapping = await repository.requestMapping(contentTypeAlias);
    if (!mapping) {
      notificationContext?.peek('warning', {
        data: { message: 'No mapping exists for this content type' },
      });
      return;
    }

    // Confirm deletion
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    try {
      await modalManager
        .open(this, UMB_CONFIRM_MODAL, {
          data: {
            headline: 'Delete Schema.org Mapping',
            content: `Are you sure you want to delete the Schema.org mapping for "${contentTypeAlias}"?`,
            color: 'danger',
            confirmLabel: 'Delete',
          },
        })
        .onSubmit();
    } catch {
      // User cancelled
      return;
    }

    // Delete the mapping
    try {
      await repository.deleteMapping(contentTypeAlias);

      notificationContext?.peek('positive', {
        data: { message: 'Mapping deleted successfully' },
      });

      // Dispatch reload event so workspace view refreshes
      const eventContext = await this.getContext(UMB_ACTION_EVENT_CONTEXT);
      if (eventContext) {
        const event = new UmbRequestReloadStructureForEntityEvent({
          unique: this.args.unique ?? '',
          entityType: this.args.entityType,
        });
        eventContext.dispatchEvent(event);
      }
    } catch {
      notificationContext?.peek('danger', {
        data: { message: 'Failed to delete mapping' },
      });
    }
  }
}

export { DeleteSchemaMappingAction as api };
