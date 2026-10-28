import { manifests as sectionManifests } from './section/manifests.js';
import { manifests as wizardManifests } from './wizard/manifests.js';
import { manifests as workspaceViewManifests } from './workspace-views/manifests.js';
import { manifests as localizationManifests } from './localization/manifests.js';

export const manifests: Array<UmbExtensionManifest> = [
  {
    type: 'backofficeEntryPoint',
    alias: 'SchemeWeaver.Seo.EntryPoint',
    name: 'SchemeWeaver SEO Entry Point',
    js: () => import('./entry-point.js'),
  },
  ...sectionManifests,
  ...wizardManifests,
  ...workspaceViewManifests,
  ...localizationManifests,
];
