import { manifests as workspaceViewManifests } from './workspace-views/manifests.js';
import { manifests as entityActionManifests } from './entity-actions/manifests.js';
import { manifests as modalManifests } from './modals/manifests.js';
import { manifests as localizationManifests } from './localization/manifests.js';

const entryPointManifest: UmbExtensionManifest = {
  type: 'backofficeEntryPoint',
  alias: 'Umbraco.Community.SchemeWeaver.EntryPoint',
  name: 'SchemeWeaver Entry Point',
  js: () => import('./entry-point.js'),
};

export const manifests: Array<UmbExtensionManifest> = [
  entryPointManifest,
  ...workspaceViewManifests,
  ...entityActionManifests,
  ...modalManifests,
  ...localizationManifests,
];
