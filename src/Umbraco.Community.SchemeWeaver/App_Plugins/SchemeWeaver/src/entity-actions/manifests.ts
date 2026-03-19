export const manifests = [
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.MapToSchema',
    name: 'Map to Schema.org',
    weight: 100,
    api: () => import('./map-to-schema.action.js'),
    meta: {
      icon: 'icon-brackets',
      label: 'Map to Schema.org',
    },
    forEntityTypes: ['document-type'],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.GenerateFromSchema',
    name: 'Generate from Schema.org',
    weight: 101,
    api: () => import('./generate-from-schema.action.js'),
    meta: {
      icon: 'icon-wand',
      label: 'Generate from Schema.org',
    },
    forEntityTypes: ['document-type'],
  },
];
