import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { UMB_MODAL_MANAGER_CONTEXT } from '@umbraco-cms/backoffice/modal';
import { SCHEMEWEAVER_AI_BULK_ANALYSIS_MODAL } from '../modals/ai-bulk-analysis-modal.token.js';

export class AIAnalyseAllAction extends UmbEntityActionBase<never> {
  async execute() {
    const modalManager = await this.getContext(UMB_MODAL_MANAGER_CONTEXT);
    if (!modalManager) return;

    await modalManager
      .open(this, SCHEMEWEAVER_AI_BULK_ANALYSIS_MODAL, { data: {} })
      .onSubmit()
      .catch(() => null);
  }
}

export { AIAnalyseAllAction as api };
