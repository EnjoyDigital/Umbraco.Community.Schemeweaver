import { manifests as entityActionManifests } from './entity-actions/manifests.js';

const modalManifests = [
  {
    type: 'modal',
    alias: 'SchemeWeaver.AI.BulkAnalysis.Modal',
    name: 'SchemeWeaver AI Bulk Analysis Modal',
    element: () => import('./modals/ai-bulk-analysis-modal.element.js'),
  },
];

export const manifests = [...entityActionManifests, ...modalManifests];
