// ManifestLocalization is not exported from this path in all Umbraco versions
// Use a compatible type definition instead
interface ManifestLocalization {
  type: 'localization';
  alias: string;
  name: string;
  meta: {
    culture: string;
  };
  js: () => Promise<unknown>;
}

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
