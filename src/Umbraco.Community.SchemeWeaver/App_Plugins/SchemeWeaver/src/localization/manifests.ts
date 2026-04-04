import type { ManifestLocalization } from '@umbraco-cms/backoffice/localization-api';

export const manifests: ManifestLocalization[] = [
  {
    type: 'localization',
    alias: 'SchemeWeaver.Localization.En',
    name: 'SchemeWeaver English',
    meta: {
      culture: 'en',
    },
    js: () => import('./en.js'),
  },
];
