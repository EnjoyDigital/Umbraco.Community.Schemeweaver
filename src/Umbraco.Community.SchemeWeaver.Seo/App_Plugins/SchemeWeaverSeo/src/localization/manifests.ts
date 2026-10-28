import type { ManifestLocalization } from '@umbraco-cms/backoffice/localization';

export const manifests: ManifestLocalization[] = [
  {
    type: 'localization',
    alias: 'SchemeWeaver.Seo.Localization.En',
    name: 'SchemeWeaver SEO English',
    meta: { culture: 'en' },
    js: () => import('./en.js'),
  },
];
