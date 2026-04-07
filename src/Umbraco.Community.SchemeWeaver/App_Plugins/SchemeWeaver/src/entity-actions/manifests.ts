export const manifests = [
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.MapToSchema',
    name: 'Map to Schema.org',
    weight: 300,
    api: () => import('./map-to-schema.action.js'),
    meta: {
      icon: 'icon-brackets',
      label: '#schemeWeaver_mapToSchema',
    },
    forEntityTypes: ['document-type'],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.DeleteSchemaMapping',
    name: 'Delete Schema.org Mapping',
    weight: 200,
    api: () => import('./delete-schema-mapping.action.js'),
    meta: {
      icon: 'icon-trash',
      label: '#schemeWeaver_deleteMapping',
    },
    forEntityTypes: ['document-type'],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'SchemeWeaver.EntityAction.GenerateFromSchema',
    name: 'Generate from Schema.org',
    weight: 100,
    api: () => import('./generate-from-schema.action.js'),
    meta: {
      icon: 'icon-wand',
      label: '#schemeWeaver_generateFromSchema',
    },
    forEntityTypes: ['document-type'],
  },
];
