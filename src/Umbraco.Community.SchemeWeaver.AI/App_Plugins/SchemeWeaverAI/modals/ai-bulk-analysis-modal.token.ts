import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export interface AIBulkAnalysisModalData {}

export interface AIBulkAnalysisModalValue {
  applied: boolean;
}

export const SCHEMEWEAVER_AI_BULK_ANALYSIS_MODAL = new UmbModalToken<
  AIBulkAnalysisModalData,
  AIBulkAnalysisModalValue
>('SchemeWeaver.AI.BulkAnalysis.Modal', {
  modal: {
    type: 'dialog',
    size: 'large',
  },
});
