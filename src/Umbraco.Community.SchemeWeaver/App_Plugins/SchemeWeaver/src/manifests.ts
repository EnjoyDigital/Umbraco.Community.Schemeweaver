import { manifests as workspaceViewManifests } from './workspace-views/manifests.js';
import { manifests as entityActionManifests } from './entity-actions/manifests.js';
import { manifests as modalManifests } from './modals/manifests.js';
import { manifests as localizationManifests } from './localization/manifests.js';

export const manifests = [
  ...workspaceViewManifests,
  ...entityActionManifests,
  ...modalManifests,
  ...localizationManifests,
];
